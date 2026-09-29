using CardGame.Combat;
using CardGame.Core;
using CardGame.Players;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 抽牌行为：从玩家牌库抽取 drawCount 张（BC 为 1 张）。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `DrawCardBehavior`；规则原文第三节「BC 免疫下次伤害，抽取一张卡牌」。
    /// 具体抽牌（手牌上限、牌库空时弃牌堆洗回）由 Players.PlayerDeckComponent.DrawCards 负责。
    /// </summary>
    public class DrawCardBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖抽牌张数，&lt;= 0 时改用 definition.drawCount。</summary>
        public int drawCountOverride;

        /// <summary>抽牌最先结算，保证后续行为能看到新牌（BC 的抽牌不影响本卡自身效果，但便于日志顺序）。</summary>
        public int ResolutionOrder
        {
            get { return 50; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] DrawCardBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant user = context.User;
            if (user == null)
            {
                Debug.LogError("[CardGame] DrawCardBehavior.Resolve 找不到出牌者。");
                return;
            }

            int count = ResolveCount(context);
            if (count <= 0)
            {
                context.Log("抽牌结算：抽牌数为 0，跳过。");
                return;
            }

            PlayerDeckComponent deck = user.GetComponent<PlayerDeckComponent>();
            if (deck == null)
            {
                Debug.LogError("[CardGame] DrawCardBehavior.Resolve 出牌者身上没有 PlayerDeckComponent，无法抽牌。");
                return;
            }

            int drawn = deck.DrawCards(count);
            context.Log(user.displayName + " 抽牌 " + drawn + "/" + count + " 张（手牌上限 " +
                        GameRules.HandLimit + "）。");
        }

        public string Describe()
        {
            string countPart = drawCountOverride > 0 ? drawCountOverride.ToString() : "definition.drawCount";
            return "抽 " + countPart + " 张牌";
        }

        private int ResolveCount(CardPlayContext context)
        {
            if (drawCountOverride > 0)
            {
                return drawCountOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null)
            {
                return identity.definition.drawCount;
            }

            return 0;
        }
    }
}
