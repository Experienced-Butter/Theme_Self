using System.Collections.Generic;

namespace CardGame.Controllers
{
    /// <summary>
    /// 卡牌库：全部运行时卡牌实体的唯一克隆入口。
    /// 契约出处：ARCHITECTURE.md 第 8 节 `Controllers/CardLibraryComponent.cs`、第 8 节末
    /// 「CardLibraryComponent 生成卡牌时统一走 Templates.EntityTemplate.CreateRuntimeInstance，保证克隆语义」。
    /// 要点：CreateCard 永不返回共享实例——每次都从卡牌模板克隆出一个独立实体；
    ///       牌库（PlayerDeckComponent）、手牌、合成产物、大招/临时卡全部经由本组件创建。
    /// </summary>
    public class CardLibraryComponent : UnityEngine.MonoBehaviour
    {
        /// <summary>卡牌模板集合（每个 CardKind 一张，建议由 Bootstrap/SceneTemplateBuilder 装配）。</summary>
        public List<Templates.CardTemplate> cardTemplates = new List<Templates.CardTemplate>();

        private static readonly List<Templates.CardTemplate> EmptyTemplates = new List<Templates.CardTemplate>();

        /// <summary>全部卡牌模板（只读视图）。</summary>
        public System.Collections.Generic.IReadOnlyList<Templates.CardTemplate> All
        {
            get
            {
                if (cardTemplates == null)
                {
                    return EmptyTemplates;
                }

                return cardTemplates;
            }
        }

        /// <summary>
        /// 取得指定种类的卡牌模板；找不到返回 null。
        /// 先按 definition.kind 匹配，其次按 templateId 匹配，便于模板尚未填入 definition 的场景。
        /// </summary>
        public Templates.CardTemplate GetTemplate(Core.CardKind kind)
        {
            if (cardTemplates == null)
            {
                return null;
            }

            for (int i = 0; i < cardTemplates.Count; i++)
            {
                Templates.CardTemplate template = cardTemplates[i];
                if (template == null)
                {
                    continue;
                }

                if (template.definition != null && template.definition.kind == kind)
                {
                    return template;
                }
            }

            string kindName = kind.ToString();
            for (int i = 0; i < cardTemplates.Count; i++)
            {
                Templates.CardTemplate template = cardTemplates[i];
                if (template != null && template.definition == null && template.templateId == kindName)
                {
                    return template;
                }
            }

            return null;
        }

        /// <summary>
        /// 按种类克隆出一张独立的运行时卡牌实体。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `CardInstance CreateCard(CardKind kind, Combatant owner, Transform parent)`。
        /// 绝不会返回共享实例：模板存在时走 EntityTemplate.CreateRuntimeInstance 深拷贝，
        /// 模板缺失时才退化为运行时兜底构造（并输出警告），仍然是一张全新实体。
        /// </summary>
        public Cards.CardInstance CreateCard(Core.CardKind kind, Combat.Combatant owner, UnityEngine.Transform parent)
        {
            UnityEngine.Transform target = ResolveParent(parent);
            Templates.CardTemplate template = GetTemplate(kind);

            UnityEngine.GameObject clone = null;
            if (template != null)
            {
                clone = template.CreateRuntimeInstance(target, kind.ToString());
            }
            else
            {
                clone = BuildFallbackCard(kind, target);
            }

            if (clone == null)
            {
                UnityEngine.Debug.LogError("[CardGame] CardLibraryComponent.CreateCard：" + kind + " 克隆失败，未能创建卡牌实体。");
                return null;
            }

            Cards.CardInstance instance = clone.GetComponent<Cards.CardInstance>();
            if (instance == null)
            {
                instance = clone.AddComponent<Cards.CardInstance>();
            }

            // 模板路径下 CardTemplate.OnAfterClone 已写入 definition 深拷贝；缺失时才补一份。
            Cards.CardIdentityComponent identity = clone.GetComponent<Cards.CardIdentityComponent>();
            if (identity == null || identity.definition == null)
            {
                UnityEngine.Debug.LogWarning("[CardGame] CardLibraryComponent.CreateCard：" + kind +
                                              " 克隆体缺少卡牌定义，已用 CardDefaults 补齐。");
                instance.InitializeFrom(Core.CardDefaults.Create(kind));
                identity = clone.GetComponent<Cards.CardIdentityComponent>();
            }

            instance.owner = owner;

            // 大招抽到的临时卡（definition.isTemporary == true）在本回合结束时移除（ARCHITECTURE 第 13 节第 6 条）。
            if (identity != null && identity.definition != null)
            {
                instance.isTemporary = identity.definition.isTemporary;
            }

            return instance;
        }

        /// <summary>
        /// 模板缺失时的运行时兜底：新建 GameObject 并按其数值定义挂上对应行为组件，
        /// 保证「任何情况下都不返回共享实例、也不会造出一张完全没有效果的卡」。
        /// </summary>
        private UnityEngine.GameObject BuildFallbackCard(Core.CardKind kind, UnityEngine.Transform parent)
        {
            UnityEngine.Debug.LogWarning("[CardGame] CardLibraryComponent：卡牌库缺少 " + kind +
                                         " 的模板，改用运行时兜底构造（请由 Bootstrap 补齐卡牌模板）。");

            Core.CardDefinitionData definition = Core.CardDefaults.Create(kind);

            UnityEngine.GameObject go = new UnityEngine.GameObject(kind.ToString());
            go.transform.SetParent(parent, false);

            Cards.CardIdentityComponent identity = go.AddComponent<Cards.CardIdentityComponent>();
            identity.Initialize(definition);

            go.AddComponent<Cards.CardInstance>();
            go.AddComponent<Cards.CardBuffComponent>();
            go.AddComponent<Cards.CardPlayComponent>();
            AttachBehaviors(go, definition);
            return go;
        }

        /// <summary>按数值定义补齐行为组件（行为组件默认从 CardIdentityComponent.definition 取值）。</summary>
        private static void AttachBehaviors(UnityEngine.GameObject go, Core.CardDefinitionData def)
        {
            if (def.damage > 0)
            {
                go.AddComponent<Cards.Behaviors.DamageBehavior>();
            }

            if (def.healInstant > 0)
            {
                go.AddComponent<Cards.Behaviors.HealBehavior>();
            }

            if (def.regenPerTurn > 0 && def.regenTurns > 0)
            {
                go.AddComponent<Cards.Behaviors.RegenBehavior>();
            }

            if (def.hitDownPercent > 0f && def.hitDownTurns > 0)
            {
                go.AddComponent<Cards.Behaviors.HitDownBehavior>();
            }

            if ((def.defenseDownPercent > 0f || def.defenseDownFlat > 0) && def.defenseDownTurns > 0)
            {
                go.AddComponent<Cards.Behaviors.DefenseDownBehavior>();
            }

            if (def.grantImmunity)
            {
                go.AddComponent<Cards.Behaviors.ImmunityBehavior>();
            }

            if (def.drawCount > 0)
            {
                go.AddComponent<Cards.Behaviors.DrawCardBehavior>();
            }

            if (def.grantInitiative)
            {
                go.AddComponent<Cards.Behaviors.InitiativeBehavior>();
            }

            if (def.extraPlaysNextTurn > 0)
            {
                go.AddComponent<Cards.Behaviors.ExtraPlayBehavior>();
            }

            if (def.handBuffBasic > 0f || def.handBuffFused > 0f)
            {
                go.AddComponent<Cards.Behaviors.HandDamageBuffBehavior>();
            }

            if (def.kind == Core.CardKind.Duplicator)
            {
                go.AddComponent<Cards.Behaviors.DuplicateBehavior>();
            }

            if (def.kind == Core.CardKind.Ultimate)
            {
                go.AddComponent<Cards.Behaviors.UltimateBehavior>();
            }
        }

        /// <summary>
        /// 解析挂载父节点：不得把克隆体挂到模板实体下面（否则会污染模板层级）。
        /// </summary>
        private UnityEngine.Transform ResolveParent(UnityEngine.Transform parent)
        {
            if (parent == null)
            {
                return transform;
            }

            if (parent.GetComponentInParent<Templates.EntityTemplate>() != null)
            {
                UnityEngine.Debug.LogWarning("[CardGame] CardLibraryComponent：请求的父节点位于模板实体之下，已改为挂到控制者实体上。");
                return transform;
            }

            return parent;
        }
    }
}
