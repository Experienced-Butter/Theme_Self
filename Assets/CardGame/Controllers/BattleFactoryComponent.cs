using System.Collections.Generic;

namespace CardGame.Controllers
{
    /// <summary>
    /// 战斗单位工厂：用玩家/怪物模板克隆出运行时实体，并统一挂到 RuntimeRoot 下。
    /// 契约出处：ARCHITECTURE.md 第 8 节 `Controllers/BattleFactoryComponent.cs`、第 5 节（模板 → 克隆语义）。
    /// 规则出处：RULES.md 第四节（怪物参数）、第五节（关卡配置；支线关怪1 血量为 1600）。
    /// </summary>
    public class BattleFactoryComponent : UnityEngine.MonoBehaviour
    {
        /// <summary>玩家模板（PlayerTemplate.maxHp/defense/hitRate/playsPerTurn/openingHand/drawPerTurn/handLimit）。</summary>
        public Templates.PlayerTemplate playerTemplate;

        /// <summary>怪物模板集合（至少包含 Attacker = 怪1、Support = 怪2 各一张）。</summary>
        public List<Templates.MonsterTemplate> monsterTemplates = new List<Templates.MonsterTemplate>();

        private UnityEngine.Transform _runtimeRoot;

        /// <summary>
        /// 所有运行时克隆体的统一父节点（绝不是模板实体本身）。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `Transform RuntimeRoot { get; }`。
        /// </summary>
        public UnityEngine.Transform RuntimeRoot
        {
            get
            {
                if (_runtimeRoot == null)
                {
                    UnityEngine.GameObject root = new UnityEngine.GameObject("RuntimeRoot");
                    root.transform.SetParent(transform, false);
                    _runtimeRoot = root.transform;
                }

                return _runtimeRoot;
            }
        }

        /// <summary>
        /// 克隆出玩家运行时实体（满血、初始手牌/牌库由流程随后构建）。
        /// </summary>
        public Combat.Combatant CreatePlayer(UnityEngine.Transform parent)
        {
            if (playerTemplate == null)
            {
                UnityEngine.Debug.LogError("[CardGame] BattleFactoryComponent.CreatePlayer：缺少 PlayerTemplate。");
                return null;
            }

            UnityEngine.GameObject clone = playerTemplate.CreateRuntimeInstance(ResolveParent(parent), "Player");
            if (clone == null)
            {
                UnityEngine.Debug.LogError("[CardGame] BattleFactoryComponent.CreatePlayer：玩家模板克隆失败。");
                return null;
            }

            Combat.Combatant combatant = clone.GetComponent<Combat.Combatant>();
            if (combatant == null)
            {
                combatant = clone.GetComponentInChildren<Combat.Combatant>(true);
            }

            if (combatant == null)
            {
                UnityEngine.Debug.LogError("[CardGame] BattleFactoryComponent.CreatePlayer：克隆体缺少 Combatant 组件。");
                return null;
            }

            combatant.team = Core.Team.Player;
            combatant.displayName = "玩家";
            return combatant;
        }

        /// <summary>
        /// 克隆出一只怪物运行时实体。
        /// sideLevel = true 表示支线关：怪1 血量按 RULES.md 第五节使用 1600（其余关为 2000），
        /// 攻击力、防御、出招顺序不受影响。
        /// </summary>
        public Combat.Combatant CreateMonster(Core.MonsterKind kind, bool sideLevel, UnityEngine.Transform parent)
        {
            Templates.MonsterTemplate template = FindMonsterTemplate(kind);
            if (template == null)
            {
                UnityEngine.Debug.LogError("[CardGame] BattleFactoryComponent.CreateMonster：缺少 " + kind + " 类型的怪物模板。");
                return null;
            }

            int hp = ResolveMonsterHp(kind, sideLevel);
            string name = BuildMonsterName(kind == Core.MonsterKind.Support ? "怪物2" : "怪物1");

            UnityEngine.GameObject clone = template.CreateRuntimeInstance(ResolveParent(parent), name);
            if (clone == null)
            {
                UnityEngine.Debug.LogError("[CardGame] BattleFactoryComponent.CreateMonster：" + kind + " 模板克隆失败。");
                return null;
            }

            Combat.Combatant combatant = clone.GetComponent<Combat.Combatant>();
            if (combatant == null)
            {
                combatant = clone.GetComponentInChildren<Combat.Combatant>(true);
            }

            if (combatant == null)
            {
                UnityEngine.Debug.LogError("[CardGame] BattleFactoryComponent.CreateMonster：克隆体缺少 Combatant 组件。");
                return null;
            }

            combatant.team = Core.Team.Enemy;
            combatant.displayName = name;

            // 血量按关卡覆盖（模板只保存一套序列化数值，支线关怪1 与普通关怪1 共用同一模板）。
            Combat.HealthComponent health = clone.GetComponent<Combat.HealthComponent>();
            if (health == null)
            {
                health = clone.GetComponentInChildren<Combat.HealthComponent>(true);
            }

            if (health != null)
            {
                health.Initialize(hp);
            }

            EnsureActionSequence(clone, kind);
            return combatant;
        }

        /// <summary>规则：怪1 出招 普攻→普攻→减防35；怪2 出招 普攻→辅助→治疗（RULES.md 第六节第 6 条）。</summary>
        private static void EnsureActionSequence(UnityEngine.GameObject clone, Core.MonsterKind kind)
        {
            Monsters.MonsterActionComponent action = clone.GetComponent<Monsters.MonsterActionComponent>();
            if (action == null)
            {
                action = clone.GetComponentInChildren<Monsters.MonsterActionComponent>(true);
            }

            if (action == null)
            {
                UnityEngine.Debug.LogError("[CardGame] BattleFactoryComponent：怪物克隆体缺少 MonsterActionComponent，出招顺序不可用。");
                return;
            }

            if (action.sequence != null && action.sequence.Length > 0)
            {
                return;   // 模板已给出顺序，保持模板配置
            }

            action.sequence = kind == Core.MonsterKind.Support
                ? new[] { Core.MonsterAction.Attack, Core.MonsterAction.Support, Core.MonsterAction.Heal }
                : new[] { Core.MonsterAction.Attack, Core.MonsterAction.Attack, Core.MonsterAction.DefenseBreak };
        }

        /// <summary>规则：怪1 关一/关二 2000 血、支线关 1600 血；怪2 恒为 1000 血。</summary>
        private static int ResolveMonsterHp(Core.MonsterKind kind, bool sideLevel)
        {
            if (kind == Core.MonsterKind.Support)
            {
                return Core.GameRules.Monster2Hp;
            }

            return sideLevel ? Core.GameRules.Monster1HpSide : Core.GameRules.Monster1HpNormal;
        }

        private Templates.MonsterTemplate FindMonsterTemplate(Core.MonsterKind kind)
        {
            if (monsterTemplates == null)
            {
                return null;
            }

            for (int i = 0; i < monsterTemplates.Count; i++)
            {
                Templates.MonsterTemplate template = monsterTemplates[i];
                if (template != null && template.kind == kind)
                {
                    return template;
                }
            }

            return null;
        }

        /// <summary>同名怪物追加序号，保证战报里「怪物1」「怪物1-2」可区分。</summary>
        private string BuildMonsterName(string baseName)
        {
            UnityEngine.Transform root = RuntimeRoot;
            string candidate = baseName;
            int index = 1;
            while (HasChildNamed(root, candidate))
            {
                index++;
                candidate = baseName + "-" + index;
            }

            return candidate;
        }

        private static bool HasChildNamed(UnityEngine.Transform root, string name)
        {
            if (root == null)
            {
                return false;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i).name == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>解析挂载父节点：空缺时用 RuntimeRoot；绝不把克隆体挂到模板实体下面。</summary>
        private UnityEngine.Transform ResolveParent(UnityEngine.Transform parent)
        {
            if (parent == null)
            {
                return RuntimeRoot;
            }

            if (parent.GetComponentInParent<Templates.EntityTemplate>() != null)
            {
                UnityEngine.Debug.LogWarning("[CardGame] BattleFactoryComponent：请求的父节点位于模板实体之下，已改为挂到 RuntimeRoot。");
                return RuntimeRoot;
            }

            return parent;
        }
    }
}
