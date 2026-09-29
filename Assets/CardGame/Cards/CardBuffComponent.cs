using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards
{
    /// <summary>
    /// 卡牌自身的增益组件：承载 AA 的「手牌强化」倍率。
    /// 规则出处：ARCHITECTURE.md 第 6 节 `Cards/CardBuffComponent.cs`；
    /// 规则原文：AA 造成伤害，并让手牌中的 A/AA 在之后 2 回合内变成强化 A=440、强化 AA=828。
    /// 强化倍率写在「手牌里那张 A/AA 卡自己的 CardBuffComponent 上」，
    /// 由 DamageBehavior 在结算时读取 damageMultiplier 相乘，从而得到 400×1.10=440、720×1.15=828。
    /// </summary>
    public class CardBuffComponent : MonoBehaviour
    {
        /// <summary>伤害倍率，1 表示未强化。被 HandDamageBuffBehavior 写入 1.10 / 1.15。</summary>
        public float damageMultiplier = 1f;

        /// <summary>剩余强化回合数。0 表示未强化。</summary>
        public int remainingTurns;

        /// <summary>施加/刷新强化：倍率取两来源的较大值（避免弱化），持续回合取较长者。</summary>
        public void Apply(float mult, int turns)
        {
            if (mult <= 0f)
            {
                mult = 1f;
            }

            if (turns <= 0)
            {
                return;
            }

            damageMultiplier = Mathf.Max(damageMultiplier, mult);
            remainingTurns = Mathf.Max(remainingTurns, turns);
        }

        /// <summary>
        /// 回合结束时递减一层。返回 true 表示本次调用后强化刚好到期（倍率已复位为 1）。
        /// </summary>
        public bool ConsumeTurn()
        {
            if (remainingTurns <= 0)
            {
                return false;
            }

            remainingTurns--;
            if (remainingTurns <= 0)
            {
                remainingTurns = 0;
                damageMultiplier = 1f;
                return true;
            }

            return false;
        }

        /// <summary>立刻清除强化。</summary>
        public void Clear()
        {
            damageMultiplier = 1f;
            remainingTurns = 0;
        }

        /// <summary>
        /// 回合结束时统一递减手牌里所有卡的强化计时。
        /// 规则出处：ARCHITECTURE 第 6 节（CardBuffComponent.remainingTurns）+ 规则原文第三节
        /// 「AA 下两回合强化手牌中的 A 和 AA」——强化必须只有 2 回合，到期即失效；
        /// 契约版本为 §14.2（v1.2 改写，采用本静态助手）。
        ///
        /// 接线状态：**已接线，不要重复调用。**
        /// Controllers.GameFlowComponent.EndTurn() 的回合末第 ④ 步
        /// （私有封装 TickHandCardBuffs，位于状态结算之后、RemoveTemporaryCards() 之前）
        /// 会调用本方法一次。重复调用会把 2 回合强化提前成 1 回合。
        /// </summary>
        public static void TickHandBuffs(Combat.Combatant owner)
        {
            if (owner == null)
            {
                return;
            }

            Players.PlayerHandComponent hand = owner.GetComponent<Players.PlayerHandComponent>();
            if (hand == null)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<CardInstance> cards = hand.Cards;
            if (cards == null)
            {
                return;
            }

            // 倒序无必要（只改组件字段、不改集合），正序即可。
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                if (card == null)
                {
                    continue;
                }

                CardBuffComponent buff = card.Buff;
                if (buff != null)
                {
                    buff.ConsumeTurn();
                }
            }
        }

        /// <summary>是否处于强化状态。</summary>
        public bool IsActive
        {
            get { return remainingTurns > 0 && damageMultiplier > 1f; }
        }

        /// <summary>日志用描述。</summary>
        public string Describe()
        {
            if (!IsActive)
            {
                return "无强化";
            }

            return "强化 x" + damageMultiplier.ToString("0.00") + "（剩余 " + remainingTurns + " 回合）";
        }
    }
}
