using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 降低防御行为：给目标挂 DefenseDown，防御按百分比或固定值下降，持续 defenseDownTurns 回合。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `DefenseDownBehavior`；规则原文第三节「BB 降低敌方 10% 命中与防御」。
    /// 数值默认取自 definition.defenseDownPercent / defenseDownFlat / defenseDownTurns，序列化字段仅作覆盖。
    ///
    /// 【取值约定（ARCHITECTURE §14.6 正式契约，采用 architect 已实现的数值区间方案）】
    /// Core.StatusEffect 只有 magnitude 一个数值字段，DefenseDown 的两种语义按**区间**区分，
    /// 由 Combat.DefenseComponent.GetEffectiveDefense 消费；同类状态取最大值（StatusComponent.MagnitudeOf），结果下限 0：
    ///   magnitude ∈ (0, 1] → 百分比减免：有效防御 = base × (1 − magnitude)   —— BB 写 0.10
    ///   magnitude &gt; 1     → 固定值减免：有效防御 = base − magnitude         —— 怪1 减防 35 写 35
    /// 本行为因此**按该约定写入原始数值**，不做任何自定义编码。
    ///
    /// **§14.6 硬约束：百分比必须 ≤ 1，固定值必须 &gt; 1。** 违反会让语义翻转（例如百分比 1.5 会被
    /// 当成固定值 1.5 的减防），因此本行为在写入前自检并按边界钳制 + 报错，而不是静默写入一个语义翻转的值。
    /// 本规则集里 BB（降怪物防御，百分比）与怪1 减防 35（降玩家防御，固定值）永远不落在同一战斗单位上，
    /// 单字段足够；若将来出现「固定值 ≤ 1」或「百分比 &gt; 100%」的效果，需改用双字段方案。
    /// </summary>
    public class DefenseDownBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>百分比区间上界：magnitude ≤ 此值按百分比语义解读，与之配合的固定值必须严格大于它。</summary>
        public const float PercentRangeMax = 1f;

        /// <summary>覆盖降低比例（0.10 = 10%），&lt;= 0 时改用 definition.defenseDownPercent。</summary>
        public float percentOverride;

        /// <summary>覆盖固定降低值，&lt; 0 时改用 definition.defenseDownFlat。</summary>
        public int flatOverride = -1;

        /// <summary>覆盖持续回合数，&lt;= 0 时改用 definition.defenseDownTurns。</summary>
        public int turnsOverride;

        /// <summary>回合数覆盖为 0 时使用的兜底值。
        /// 规则出处：ARCHITECTURE 第 13 节第 2 条 —— BB 的持续回合默认与 B 一致，为 1 回合。</summary>
        public int fallbackTurns = 1;

        /// <summary>减益行为统一在伤害/治疗之后结算。</summary>
        public int ResolutionOrder
        {
            get { return 500; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] DefenseDownBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant target = context.Target;
            if (target == null)
            {
                context.Log("降低防御结算：没有可选目标，未生效。");
                return;
            }

            if (target.Statuses == null)
            {
                Debug.LogError("[CardGame] DefenseDownBehavior.Resolve 目标缺少状态组件。");
                return;
            }

            float percent = ResolvePercent(context);
            int flat = ResolveFlat(context);
            int turns = ResolveTurns(context);

            if (turns <= 0)
            {
                context.Log("降低防御结算：持续回合为 0，跳过。");
                return;
            }

            // 只有固定值时写 flat（>1），只有百分比时写 percent（≤1）。
            // 两者同时非零时固定值优先：单字段无法同时表达两种语义，且本规则集不会同时出现。
            float magnitude;
            string description;
            if (flat > 0)
            {
                magnitude = EnforceFlatRange(flat, context);
                description = flat + " 点（固定）";
                if (percent > 0f)
                {
                    description += "，另有百分比减免 " + (percent * 100f).ToString("0.#") + "% 因单字段限制未写入";
                }
            }
            else if (percent > 0f)
            {
                magnitude = EnforcePercentRange(percent, context);
                description = (magnitude * 100f).ToString("0.#") + "%";
            }
            else
            {
                context.Log("降低防御结算：数值为 0，跳过。");
                return;
            }

            target.Statuses.Add(StatusKind.DefenseDown, magnitude, turns, IsFromPlayer(context));
            context.Log(target.displayName + " 防御降低 " + description +
                        "，持续 " + turns + " 回合，当前防御 " + target.GetDefense() + "。");
        }

        public string Describe()
        {
            string percentPart = percentOverride > 0f ? (percentOverride * 100f).ToString("0.#") + "%" : "definition.defenseDownPercent";
            string flatPart = flatOverride >= 0 ? flatOverride.ToString() : "definition.defenseDownFlat";
            string turnsPart = turnsOverride > 0 ? turnsOverride.ToString() : "definition.defenseDownTurns";
            return "降低目标防御 " + percentPart + " / 固定 " + flatPart + " × " + turnsPart + " 回合";
        }

        /// <summary>
        /// 把百分比钳进 §14.6 的百分比区间 (0, 1]。
        /// 超过 100% 的百分比会被 DefenseComponent 误判为固定值减免，属于契约硬约束违规；
        /// 这里钳到 1.0（防御降为 0）并大声报错，避免静默产生语义翻转的数值。
        /// </summary>
        public static float EnforcePercentRange(float percent, CardPlayContext context)
        {
            if (percent <= 0f)
            {
                return 0f;
            }

            if (percent > PercentRangeMax)
            {
                ReportRangeViolation("百分比 " + (percent * 100f).ToString("0.#") + "% 超过 100%，" +
                                     "按 §14.6 会被误判为固定值减免，已钳制为 100%（防御降为 0）", context);
                return PercentRangeMax;
            }

            return percent;
        }

        /// <summary>
        /// 把固定值钳进 §14.6 的固定值区间 (&gt; 1)。
        /// 取值可能来自 float 覆盖字段/definition 而非整数，因此下界判据用「&gt; 1」；
        /// 对整数取值而言该区间实际要求 ≥ 2。
        /// </summary>
        public static float EnforceFlatRange(int flat, CardPlayContext context)
        {
            if (flat > (int)PercentRangeMax)
            {
                return flat;
            }

            ReportRangeViolation("固定值 " + flat + " 未大于 " + PercentRangeMax.ToString("0") +
                                 "，按 §14.6 会被误判为百分比减免，已按固定值 2 写入", context);
            return (int)PercentRangeMax + 1;
        }

        private static void ReportRangeViolation(string detail, CardPlayContext context)
        {
            Debug.LogError("[CardGame] DefenseDownBehavior 违反 §14.6 区间约定：" + detail +
                           "。请修正配置，不要依赖钳制值。");

            if (context != null)
            {
                context.Log("降低防御结算：数值违反区间约定（" + detail + "）。");
            }
        }

        private static bool IsFromPlayer(CardPlayContext context)
        {
            return context.User != null && context.User.team == Team.Player;
        }

        private float ResolvePercent(CardPlayContext context)
        {
            if (percentOverride > 0f)
            {
                return percentOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null)
            {
                return identity.definition.defenseDownPercent;
            }

            return 0f;
        }

        private int ResolveFlat(CardPlayContext context)
        {
            if (flatOverride >= 0)
            {
                return flatOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null)
            {
                return identity.definition.defenseDownFlat;
            }

            return 0;
        }

        private int ResolveTurns(CardPlayContext context)
        {
            if (turnsOverride > 0)
            {
                return turnsOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null && identity.definition.defenseDownTurns > 0)
            {
                return identity.definition.defenseDownTurns;
            }

            return fallbackTurns > 0 ? fallbackTurns : GameRules.DefaultDebuffTurns;
        }
    }
}
