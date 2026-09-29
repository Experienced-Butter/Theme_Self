using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 降低命中行为：给目标挂 HitDown，命中率下降 hitDownPercent，持续 hitDownTurns 回合。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `HitDownBehavior`；规则原文第二节
    /// 「B（躲闪）降低敌方 10% 命中 1 回合」；第三节「BB 降低敌方 10% 命中与防御」。
    /// B = 10% × 1 回合；BB = 10% × 1 回合（ARCHITECTURE 第 13 节第 2 条默认取 1 回合）。
    /// </summary>
    public class HitDownBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖降低比例（0.10 = 10%），&lt;= 0 时改用 definition.hitDownPercent。</summary>
        public float percentOverride;

        /// <summary>覆盖持续回合数，&lt;= 0 时改用 definition.hitDownTurns。</summary>
        public int turnsOverride;

        /// <summary>回合数覆盖为 0 时使用的兜底值（沿用规则里 B 明确的 1 回合）。</summary>
        public int fallbackTurns;

        /// <summary>减益行为统一在伤害/治疗之后结算。</summary>
        public int ResolutionOrder
        {
            get { return 400; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] HitDownBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant target = context.Target;
            if (target == null)
            {
                context.Log("降低命中结算：没有可选目标，未生效。");
                return;
            }

            if (target.Statuses == null)
            {
                Debug.LogError("[CardGame] HitDownBehavior.Resolve 目标缺少状态组件。");
                return;
            }

            float percent = ResolvePercent(context);
            int turns = ResolveTurns(context);
            if (percent <= 0f || turns <= 0)
            {
                context.Log("降低命中结算：数值为 0，跳过。");
                return;
            }

            target.Statuses.Add(StatusKind.HitDown, percent, turns, context.User != null && context.User.team == Team.Player);
            context.Log(target.displayName + " 命中率降低 " + (percent * 100f).ToString("0.#") +
                        "%，持续 " + turns + " 回合，当前命中率 " +
                        (target.GetHitRate() * 100f).ToString("0.#") + "%。");
        }

        public string Describe()
        {
            string percentPart = percentOverride > 0f ? (percentOverride * 100f).ToString("0.#") + "%" : "definition.hitDownPercent";
            string turnsPart = turnsOverride > 0 ? turnsOverride.ToString() : "definition.hitDownTurns";
            return "降低目标命中 " + percentPart + " × " + turnsPart + " 回合";
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
                return identity.definition.hitDownPercent;
            }

            return 0f;
        }

        private int ResolveTurns(CardPlayContext context)
        {
            if (turnsOverride > 0)
            {
                return turnsOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null && identity.definition.hitDownTurns > 0)
            {
                return identity.definition.hitDownTurns;
            }

            return fallbackTurns > 0 ? fallbackTurns : GameRules.DefaultDebuffTurns;
        }
    }
}
