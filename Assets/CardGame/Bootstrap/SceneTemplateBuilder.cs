// 归属：monsters（ARCHITECTURE.md 第 10 节、第 12 节）。
// 纯 C# 构建逻辑：运行时（GameBootstrap）与编辑器（CardGameSceneBuilder）共用，
// 因此**本文件不引用 UnityEditor**（ARCHITECTURE 第 0 节第 4 条）。
// 调用顺序：先建模板（玩家 / 卡牌库 / 怪物），最后 BuildController 做总装与引用注入。
using System.Collections.Generic;
using CardGame.Cards;
using CardGame.Cards.Behaviors;
using CardGame.Combat;
using CardGame.Controllers;
using CardGame.Core;
using CardGame.Monsters;
using CardGame.Players;
using CardGame.Templates;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGame.Bootstrap
{
    /// <summary>
    /// 场景模板与控制者的程序化装配器（ARCHITECTURE 第 10 节）。
    /// 四个 Build* 方法都是「只创建缺失项」的幂等操作：
    ///   1) 当前场景里已存在对应对象时直接返回它，**绝不覆盖**用户调整过的字段；
    ///   2) 缺失时才在 parent 下创建（parent 为 null 时创建在活动场景根）；
    ///   3) 模板 GameObject 一律 SetActive(false)（ARCHITECTURE 第 5 节），控制者保持激活。
    /// 数值全部取自 Core.GameRules（RULES.md 的参数）与 Core.CardDefaults（卡牌数值唯一权威来源）。
    /// </summary>
    public static class SceneTemplateBuilder
    {
        private const string PlayerTemplateName = "PlayerTemplate";
        private const string CardLibraryName = "CardLibrary";
        private const string ControllerName = "GameController";

        // 怪物模板 templateId：MonsterTemplate.OnAfterClone 用它拼出克隆体显示名
        // （例：「攻击型怪物-Monster1」）。
        private const string Monster1TemplateId = "Monster1";
        private const string Monster2TemplateId = "Monster2";

        // 卡牌模板子结点命名前缀：CardTemplate_A / CardTemplate_AA ...
        private const string CardTemplatePrefix = "CardTemplate_";

        // RULES 第四节：怪物命中率 100%（可被 B/BB 的 HitDown 降低，ARCHITECTURE 第 13 节第 8 条）
        private const float MonsterHitRate = 1f;

        // 全部 11 种卡（ARCHITECTURE 第 2 节 CardKind）。顺序与 RULES 第二节、第三节一致。
        private static readonly CardKind[] AllCardKinds = new CardKind[]
        {
            CardKind.A, CardKind.B, CardKind.C,
            CardKind.AA, CardKind.BB, CardKind.CC,
            CardKind.AB, CardKind.AC, CardKind.BC,
            CardKind.Ultimate, CardKind.Duplicator
        };

        /// <summary>
        /// 玩家模板实体（ARCHITECTURE 第 5 节 PlayerTemplate）。
        /// 数值：生命 2500、防御 300、命中 100%、每回合出牌 4、初始手牌 5、每回合抽牌 4、手牌上限 6。
        /// </summary>
        public static GameObject BuildPlayerTemplate(Transform parent)
        {
            PlayerTemplate existing = FindFirstInScene<PlayerTemplate>();
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject go = CreateChild(parent, PlayerTemplateName);
            go.SetActive(false);   // 模板必须保持非激活（ARCHITECTURE 第 5 节）

            PlayerTemplate template = go.AddComponent<PlayerTemplate>();
            template.templateId = PlayerTemplateName;

            Combatant combatant = go.AddComponent<Combatant>();
            combatant.team = Team.Player;
            combatant.displayName = "玩家";

            HealthComponent health = go.AddComponent<HealthComponent>();
            health.maxHp = GameRules.PlayerMaxHp;
            health.currentHp = GameRules.PlayerMaxHp;

            DefenseComponent defense = go.AddComponent<DefenseComponent>();
            defense.baseDefense = GameRules.PlayerDefense;

            HitRateComponent hitRate = go.AddComponent<HitRateComponent>();
            hitRate.baseHitRate = GameRules.PlayerHitRate;

            go.AddComponent<StatusComponent>();

            // PlayerTemplate 的序列化字段（ARCHITECTURE 第 5 节），克隆时由 OnAfterClone 写回克隆体
            template.maxHp = GameRules.PlayerMaxHp;
            template.defense = GameRules.PlayerDefense;
            template.hitRate = GameRules.PlayerHitRate;
            template.playsPerTurn = GameRules.PlaysPerTurn;
            template.openingHand = GameRules.OpeningHand;
            template.drawPerTurn = GameRules.DrawPerTurn;
            template.handLimit = GameRules.HandLimit;

            PlayerTurnComponent turn = go.AddComponent<PlayerTurnComponent>();
            turn.playsPerTurn = GameRules.PlaysPerTurn;

            PlayerHandComponent hand = go.AddComponent<PlayerHandComponent>();
            hand.handLimit = GameRules.HandLimit;

            PlayerDeckComponent deck = go.AddComponent<PlayerDeckComponent>();
            deck.openingHand = GameRules.OpeningHand;
            deck.drawPerTurn = GameRules.DrawPerTurn;
            // deck.owner / deck.hand 留空：PlayerDeckComponent 会在运行时自取同实体的组件，
            // 这样克隆体各自指向自己的手牌与 Combatant，不会串到模板上。

            go.AddComponent<PlayerChargeComponent>();
            go.AddComponent<PlayerStatusComponent>();
            go.AddComponent<PlayerPlayComponent>();
            go.AddComponent<CardFusionComponent>();

            return go;
        }

        /// <summary>
        /// 卡牌库实体：承载全部 11 种卡牌模板（每种一个 SetActive(false) 的子结点）。
        /// 每种卡牌的数值来自 Core.CardDefaults，行为组件按 ARCHITECTURE 第 6.1 节的对应关系挂载
        /// （CardPlayComponent.Resolve 会枚举本实体上的全部 ICardBehavior）。
        /// 已存在的种类不会重建，缺失的种类补齐到已有的卡牌库根结点下。
        /// </summary>
        public static GameObject BuildCardLibrary(Transform parent)
        {
            List<CardTemplate> existing = FindAllInScene<CardTemplate>();
            GameObject libraryRoot;

            if (existing.Count > 0)
            {
                // 复用已有结点，只补齐缺失种类。
                Transform rootTransform = existing[0] != null ? existing[0].transform.parent : null;
                libraryRoot = rootTransform != null ? rootTransform.gameObject : existing[0].gameObject;
            }
            else
            {
                libraryRoot = CreateChild(parent, CardLibraryName);
            }

            for (int i = 0; i < AllCardKinds.Length; i++)
            {
                CardKind kind = AllCardKinds[i];
                if (FindCardTemplate(kind) != null)
                {
                    continue;
                }

                CreateCardTemplate(libraryRoot.transform, kind);
            }

            return libraryRoot;
        }

        /// <summary>
        /// 控制者实体（ARCHITECTURE 第 8 节）：一个 GameObject 上挂
        /// RandomComponent / CardLibraryComponent / BattleFactoryComponent /
        /// TurnOrderComponent / GameFlowComponent / LevelComponent / GameController。
        /// 创建后立即注入跨对象引用（卡牌模板列表、玩家与怪物模板、玩家的 library），
        /// 并先保持非激活、接线完成后再激活，避免 GameController 的 Awake 在引用齐全前执行。
        /// </summary>
        public static GameObject BuildController(Transform parent)
        {
            GameController existing = FindFirstInScene<GameController>();
            if (existing != null)
            {
                // 已有控制者：只补齐为空的引用，不覆盖用户设置。
                WireControllerReferences(existing.gameObject);
                return existing.gameObject;
            }

            GameObject go = CreateChild(parent, ControllerName);
            go.SetActive(false);

            go.AddComponent<RandomComponent>();
            go.AddComponent<CardLibraryComponent>();
            go.AddComponent<BattleFactoryComponent>();
            go.AddComponent<TurnOrderComponent>();
            go.AddComponent<GameFlowComponent>();
            go.AddComponent<LevelComponent>();
            go.AddComponent<GameController>();   // 总装组件放最后添加

            WireControllerReferences(go);

            go.SetActive(true);   // 控制者是协程宿主，必须激活
            return go;
        }

        /// <summary>
        /// 怪物模板实体（ARCHITECTURE 第 5 节 MonsterTemplate），参数取自 RULES 第四节：
        /// 怪1（攻击型）：生命 2000（支线关 1600 由 BattleFactoryComponent 按 sideLevel 覆盖）、防御 0、
        ///               攻击 580、出招 {普攻, 普攻, 减防35}；减防固定值 35、持续 2 回合（ARCHITECTURE 第 13 节第 1 条）。
        /// 怪2（辅助型）：生命 1000、防御 25、攻击 350、出招 {普攻, 辅助, 治疗}；治疗 55 立即 + 28×2 回合。
        /// </summary>
        public static GameObject BuildMonsterTemplate(Transform parent, MonsterKind kind)
        {
            MonsterTemplate existing = FindMonsterTemplate(kind);
            if (existing != null)
            {
                return existing.gameObject;
            }

            bool attacker = kind == MonsterKind.Attacker;

            GameObject go = CreateChild(parent, MonsterTemplateName(kind));
            go.SetActive(false);

            MonsterTemplate template = go.AddComponent<MonsterTemplate>();
            template.templateId = attacker ? Monster1TemplateId : Monster2TemplateId;
            template.kind = kind;
            template.maxHp = attacker ? GameRules.Monster1HpNormal : GameRules.Monster2Hp;
            template.defense = attacker ? GameRules.Monster1Defense : GameRules.Monster2Defense;
            template.attack = attacker ? GameRules.Monster1Attack : GameRules.Monster2Attack;
            template.hitRate = MonsterHitRate;
            template.actionSequence = attacker
                ? new MonsterAction[] { MonsterAction.Attack, MonsterAction.Attack, MonsterAction.DefenseBreak }
                : new MonsterAction[] { MonsterAction.Attack, MonsterAction.Support, MonsterAction.Heal };

            Combatant combatant = go.AddComponent<Combatant>();
            combatant.team = Team.Enemy;
            combatant.displayName = attacker ? "怪物1（攻击型）" : "怪物2（辅助型）";

            HealthComponent health = go.AddComponent<HealthComponent>();
            health.maxHp = template.maxHp;
            health.currentHp = template.maxHp;

            DefenseComponent defense = go.AddComponent<DefenseComponent>();
            defense.baseDefense = template.defense;

            HitRateComponent hitRate = go.AddComponent<HitRateComponent>();
            hitRate.baseHitRate = template.hitRate;

            go.AddComponent<StatusComponent>();

            MonsterAttackComponent attack = go.AddComponent<MonsterAttackComponent>();
            attack.attack = template.attack;

            MonsterActionComponent action = go.AddComponent<MonsterActionComponent>();
            // 独立副本：模板与克隆体不共享数组实例（MonsterTemplate.OnAfterClone 会再复制一份给克隆体）。
            action.sequence = (MonsterAction[])template.actionSequence.Clone();

            if (attacker)
            {
                MonsterDefenseBreakComponent defenseBreak = go.AddComponent<MonsterDefenseBreakComponent>();
                defenseBreak.flatReduction = GameRules.Monster1DefenseBreakFlat;
                defenseBreak.turns = GameRules.Monster1DefenseBreakTurns;
            }
            else
            {
                MonsterHealComponent heal = go.AddComponent<MonsterHealComponent>();
                heal.instant = GameRules.Monster2HealInstant;
                heal.perTurn = GameRules.Monster2HealPerTurn;
                heal.turns = GameRules.Monster2HealTurns;
                // 辅助行动复用同物体上的治疗组件（数值只在 MonsterHealComponent 定义一次）。
                go.AddComponent<MonsterSupportComponent>();
            }

            return go;
        }

        // ---------------------------------------------------------------- 内部实现

        // 补齐控制器需要的跨对象引用；只填空引用，已设置的一律保留。
        private static void WireControllerReferences(GameObject controllerObject)
        {
            if (controllerObject == null)
            {
                return;
            }

            CardLibraryComponent library = controllerObject.GetComponent<CardLibraryComponent>();
            if (library != null && (library.cardTemplates == null || library.cardTemplates.Count == 0))
            {
                // 卡牌模板是场景里的 CardTemplate 实体（含非激活），由 BuildCardLibrary 创建。
                library.cardTemplates = FindAllInScene<CardTemplate>();
            }

            BattleFactoryComponent factory = controllerObject.GetComponent<BattleFactoryComponent>();
            if (factory != null)
            {
                if (factory.playerTemplate == null)
                {
                    factory.playerTemplate = FindFirstInScene<PlayerTemplate>();
                }

                if (factory.monsterTemplates == null)
                {
                    factory.monsterTemplates = new List<MonsterTemplate>();
                }

                List<MonsterTemplate> templates = FindAllInScene<MonsterTemplate>();
                for (int i = 0; i < templates.Count; i++)
                {
                    MonsterTemplate template = templates[i];
                    if (template == null || HasMonsterKind(factory.monsterTemplates, template.kind))
                    {
                        continue;
                    }

                    factory.monsterTemplates.Add(template);
                }
            }

            // ARCHITECTURE 第 6.2 节：CardFusionComponent.library 由 Bootstrap 注入
            //（PlayerDeckComponent.library 同理，缺了就无法从模板克隆出牌库）。
            if (library == null)
            {
                return;
            }

            CardFusionComponent fusion = FindFirstInScene<CardFusionComponent>();
            if (fusion != null && fusion.library == null)
            {
                fusion.library = library;
            }

            PlayerDeckComponent deck = FindFirstInScene<PlayerDeckComponent>();
            if (deck != null && deck.library == null)
            {
                deck.library = library;
            }
        }

        private static bool HasMonsterKind(List<MonsterTemplate> list, MonsterKind kind)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject CreateCardTemplate(Transform parent, CardKind kind)
        {
            GameObject go = CreateChild(parent, CardTemplatePrefix + kind);
            go.SetActive(false);   // 模板必须保持非激活（ARCHITECTURE 第 5 节）

            CardDefinitionData definition = CardDefaults.Create(kind);   // 数值唯一权威来源

            CardTemplate template = go.AddComponent<CardTemplate>();
            template.templateId = CardTemplatePrefix + kind;
            template.definition = definition;

            CardIdentityComponent identity = go.AddComponent<CardIdentityComponent>();
            identity.Initialize(definition);   // 独立副本（内部 Clone）

            go.AddComponent<CardInstance>();
            go.AddComponent<CardBuffComponent>();
            go.AddComponent<CardPlayComponent>();

            AddBehaviorsFor(go, kind);

            return go;
        }

        // 按 ARCHITECTURE 第 6.1 节的对应关系挂载行为组件。
        // 各行为的数值默认从 CardIdentityComponent.definition 读取，因此模板只需带上该种类真正用到的行为。
        private static void AddBehaviorsFor(GameObject cardTemplate, CardKind kind)
        {
            switch (kind)
            {
                case CardKind.A:
                    // 规则二：A 造成物理伤害 400。
                    cardTemplate.AddComponent<DamageBehavior>();
                    break;

                case CardKind.B:
                    // 规则二：B 降低敌方 10% 命中 1 回合。
                    cardTemplate.AddComponent<HitDownBehavior>();
                    break;

                case CardKind.C:
                    // 规则二：C 治疗自身 300。
                    cardTemplate.AddComponent<HealBehavior>();
                    break;

                case CardKind.AA:
                    // 规则三：AA 造成 720 伤害，并强化手牌中的 A 与 AA 两回合。
                    cardTemplate.AddComponent<DamageBehavior>();
                    cardTemplate.AddComponent<HandDamageBuffBehavior>();
                    break;

                case CardKind.BB:
                    // 规则三：BB 降低敌方 10% 命中与 10% 防御。
                    cardTemplate.AddComponent<HitDownBehavior>();
                    cardTemplate.AddComponent<DefenseDownBehavior>();
                    break;

                case CardKind.CC:
                    // 规则三：CC 回复 840（540 立即 + 150×2 回合）。
                    cardTemplate.AddComponent<HealBehavior>();
                    cardTemplate.AddComponent<RegenBehavior>();
                    break;

                case CardKind.AB:
                    // 规则三：AB 造成 360 伤害，下回合先手。
                    cardTemplate.AddComponent<DamageBehavior>();
                    cardTemplate.AddComponent<InitiativeBehavior>();
                    break;

                case CardKind.AC:
                    // 规则三：AC 造成 360 伤害，下回合出牌次数 +1。
                    cardTemplate.AddComponent<DamageBehavior>();
                    cardTemplate.AddComponent<ExtraPlayBehavior>();
                    break;

                case CardKind.BC:
                    // 规则三：BC 免疫下次伤害并抽 1 张牌。
                    cardTemplate.AddComponent<ImmunityBehavior>();
                    cardTemplate.AddComponent<DrawCardBehavior>();
                    break;

                case CardKind.Ultimate:
                    // 规则六第 1 条：大招结算「下回合 +2 出手、3 张临时卡、1 张复制器」。
                    cardTemplate.AddComponent<UltimateBehavior>();
                    break;

                case CardKind.Duplicator:
                    // 规则六第 1 条：卡牌复制器（复制一张 A/B/C 并获得一次免费合成）。
                    cardTemplate.AddComponent<DuplicateBehavior>();
                    break;
            }
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go;
        }

        private static MonsterTemplate FindMonsterTemplate(MonsterKind kind)
        {
            List<MonsterTemplate> all = FindAllInScene<MonsterTemplate>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].kind == kind)
                {
                    return all[i];
                }
            }

            return null;
        }

        private static CardTemplate FindCardTemplate(CardKind kind)
        {
            List<CardTemplate> all = FindAllInScene<CardTemplate>();
            for (int i = 0; i < all.Count; i++)
            {
                CardTemplate template = all[i];
                if (template != null && KindOfTemplate(template) == kind)
                {
                    return template;
                }
            }

            return null;
        }

        private static CardKind KindOfTemplate(CardTemplate template)
        {
            if (template.definition != null)
            {
                return template.definition.kind;
            }

            CardIdentityComponent identity = template.GetComponent<CardIdentityComponent>();
            if (identity != null && identity.definition != null)
            {
                return identity.definition.kind;
            }

            return CardKind.A;
        }

        /// <summary>活动场景里的全部目标组件（含非激活对象，模板必须能查到）。</summary>
        private static List<T> FindAllInScene<T>() where T : Component
        {
            List<T> result = new List<T>();
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return result;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                result.AddRange(roots[i].GetComponentsInChildren<T>(true));
            }

            return result;
        }

        private static T FindFirstInScene<T>() where T : Component
        {
            List<T> all = FindAllInScene<T>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null)
                {
                    return all[i];
                }
            }

            return null;
        }

        private static string MonsterTemplateName(MonsterKind kind)
        {
            return kind == MonsterKind.Attacker ? "MonsterTemplate_Attacker" : "MonsterTemplate_Support";
        }
    }
}
