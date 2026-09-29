using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 治疗行为：立即治疗自身 definition.healInstant（A 卡治疗 0，C 卡 300，CC 540）。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `HealBehavior`；规则原文第二节「C（治疗）治疗自身 300 点」、
    /// 第三节「CC 回复血量 840 点（C×1.8 + C×0.5×2 = 540 + 300）」中的 540 为立即治疗部分。
    /// </summary>
    public class HealBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖立即治疗量，&lt;= 0 时改用 definition.healInstant。</summary>
        public int healOverride;

        /// <summary>治疗在伤害之后结算。</summary>
        public int ResolutionOrder
        {
            get { return 200; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] HealBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant user = context.User;
            if (user == null)
            {
                Debug.LogError("[CardGame] HealBehavior.Resolve 找不到出牌者。");
                return;
            }

            int amount = ResolveHealAmount(context);
            if (amount <= 0)
            {
                context.Log("治疗结算：治疗量为 0，跳过。");
                return;
            }

            int applied = user.Heal(amount);
            context.Log(user.displayName + " 治疗 " + applied + " 点生命，当前 " +
                        user.CurrentHp + "/" + user.MaxHp + "。");
        }

        public string Describe()
        {
            string amountPart = healOverride > 0 ? healOverride.ToString() : "definition.healInstant";
            return "治疗自身 " + amountPart;
        }

        private int ResolveHealAmount(CardPlayContext context)
        {
            if (healOverride > 0)
            {
                return healOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null)
            {
                return identity.definition.healInstant;
            }

            return 0;
        }
    }
}
