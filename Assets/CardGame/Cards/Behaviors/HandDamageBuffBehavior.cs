using System.Collections.Generic;
using CardGame.Combat;
using CardGame.Core;
using CardGame.Players;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 手牌强化行为（AA 专用）：给「当前手牌」中所有 A 挂 HandDamageUpBasic(1.10)、
    /// 所有 AA 挂 HandDamageUpFused(1.15)，持续 handBuffTurns(2) 回合。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `HandDamageBuffBehavior`；规则原文第三节
    /// 「AA 造成伤害，下两回合强化手牌中的 A 和 AA」：强化 A = 440（1.10×400）、强化 AA = 828（1.15×720）。
    /// ARCHITECTURE 第 13 节第 4 条：只强化结算当时已在手牌中的 A/AA，之后新抽到的卡不强化。
    /// 倍率写在「那张手牌卡自己的 CardBuffComponent」上，由 DamageBehavior 读取并相乘。
    /// </summary>
    public class HandDamageBuffBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖 A 卡强化倍率，&lt;= 0 时改用 definition.handBuffBasic。</summary>
        public float basicMultiplierOverride;

        /// <summary>覆盖 AA 卡强化倍率，&lt;= 0 时改用 definition.handBuffFused。</summary>
        public float fusedMultiplierOverride;

        /// <summary>覆盖持续回合数，&lt;= 0 时改用 definition.handBuffTurns。</summary>
        public int turnsOverride;

        /// <summary>强化手牌是本卡的收尾效果之一，排在状态类行为之后、复制/大招之前。</summary>
        public int ResolutionOrder
        {
            get { return 1000; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] HandDamageBuffBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant user = context.User;
            if (user == null)
            {
                Debug.LogError("[CardGame] HandDamageBuffBehavior.Resolve 找不到出牌者。");
                return;
            }

            PlayerHandComponent hand = user.GetComponent<PlayerHandComponent>();
            if (hand == null)
            {
                Debug.LogError("[CardGame] HandDamageBuffBehavior.Resolve 出牌者身上没有 PlayerHandComponent，无法强化手牌。");
                return;
            }

            float basicMultiplier = ResolveBasicMultiplier(context);
            float fusedMultiplier = ResolveFusedMultiplier(context);
            int turns = ResolveTurns(context);

            if (turns <= 0 || (basicMultiplier <= 0f && fusedMultiplier <= 0f))
            {
                context.Log("手牌强化结算：数值为 0，跳过。");
                return;
            }

            IReadOnlyList<CardInstance> cards = hand.Cards;
            if (cards == null)
            {
                context.Log("手牌强化结算：手牌列表不可用，跳过。");
                return;
            }

            int buffedBasic = 0;
            int buffedFused = 0;

            // 结算当时的手牌快照：AA 自身此时已在 Resolving 区域，因此不会被自己强化。
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                if (card == null)
                {
                    continue;
                }

                CardIdentityComponent identity = card.Identity;
                if (identity == null || identity.definition == null)
                {
                    continue;
                }

                CardKind kind = identity.definition.kind;
                if (kind == CardKind.A && basicMultiplier > 0f)
                {
                    if (ApplyToCard(card, basicMultiplier, turns))
                    {
                        buffedBasic++;
                    }
                }
                else if (kind == CardKind.AA && fusedMultiplier > 0f)
                {
                    if (ApplyToCard(card, fusedMultiplier, turns))
                    {
                        buffedFused++;
                    }
                }
            }

            context.Log("手牌强化：" + buffedBasic + " 张 A → x" + basicMultiplier.ToString("0.00") +
                        "（伤害 " + GameRules.CardADamage + "→" + GameRules.FloorToInt(GameRules.CardADamage * basicMultiplier) +
                        "），" + buffedFused + " 张 AA → x" + fusedMultiplier.ToString("0.00") +
                        "（伤害 " + GameRules.CardAADamage + "→" + GameRules.FloorToInt(GameRules.CardAADamage * fusedMultiplier) +
                        "），持续 " + turns + " 回合。");
        }

        public string Describe()
        {
            string basicPart = basicMultiplierOverride > 0f ? "x" + basicMultiplierOverride.ToString("0.00") : "definition.handBuffBasic";
            string fusedPart = fusedMultiplierOverride > 0f ? "x" + fusedMultiplierOverride.ToString("0.00") : "definition.handBuffFused";
            string turnsPart = turnsOverride > 0 ? turnsOverride.ToString() : "definition.handBuffTurns";
            return "强化手牌中所有 A(" + basicPart + ") 与所有 AA(" + fusedPart + ")，持续 " + turnsPart + " 回合";
        }

        private static bool ApplyToCard(CardInstance card, float multiplier, int turns)
        {
            CardBuffComponent buff = card.Buff;
            if (buff == null)
            {
                buff = card.gameObject.AddComponent<CardBuffComponent>();
            }

            buff.Apply(multiplier, turns);
            return true;
        }

        private float ResolveBasicMultiplier(CardPlayContext context)
        {
            if (basicMultiplierOverride > 0f)
            {
                return basicMultiplierOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null && identity.definition.handBuffBasic > 0f)
            {
                return identity.definition.handBuffBasic;
            }

            return GameRules.CardAAHandBuffBasic;
        }

        private float ResolveFusedMultiplier(CardPlayContext context)
        {
            if (fusedMultiplierOverride > 0f)
            {
                return fusedMultiplierOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null && identity.definition.handBuffFused > 0f)
            {
                return identity.definition.handBuffFused;
            }

            return GameRules.CardAAHandBuffFused;
        }

        private int ResolveTurns(CardPlayContext context)
        {
            if (turnsOverride > 0)
            {
                return turnsOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null && identity.definition.handBuffTurns > 0)
            {
                return identity.definition.handBuffTurns;
            }

            return GameRules.CardAAHandBuffTurns;
        }
    }
}
