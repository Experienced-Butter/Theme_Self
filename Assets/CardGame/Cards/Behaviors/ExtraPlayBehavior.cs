using CardGame.Combat;
using CardGame.Core;
using CardGame.Players;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 额外出牌行为：让本卡的使用者在下一回合多出 extraPlaysNextTurn 次牌。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `ExtraPlayBehavior`；规则原文第三节「AC 造成伤害，下回合出牌次数 +1」；
    /// 第六节第 1 条「大招：下回合增加两次出手机会」。AC 用 1，大招用 2。
    /// </summary>
    public class ExtraPlayBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖额外出牌次数，&lt;= 0 时改用 definition.extraPlaysNextTurn。</summary>
        public int extraPlaysOverride;

        /// <summary>覆盖失效时使用的兜底次数（AC 与规则默认均为 +1）。</summary>
        public int fallbackExtras = 1;

        /// <summary>出牌次数安排属于本卡收尾效果，排在状态类行为之后。</summary>
        public int ResolutionOrder
        {
            get { return 900; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] ExtraPlayBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant user = context.User;
            if (user == null)
            {
                Debug.LogError("[CardGame] ExtraPlayBehavior.Resolve 找不到出牌者。");
                return;
            }

            PlayerTurnComponent turn = user.GetComponent<PlayerTurnComponent>();
            if (turn == null)
            {
                Debug.LogError("[CardGame] ExtraPlayBehavior.Resolve 出牌者身上没有 PlayerTurnComponent，无法增加出牌次数。");
                return;
            }

            int extras = ResolveExtras(context);
            if (extras <= 0)
            {
                context.Log("额外出牌结算：次数为 0，跳过。");
                return;
            }

            turn.GrantExtraPlaysNextTurn(extras);
            context.Log(user.displayName + " 下回合出牌次数 +" + extras + "（待生效 " +
                        turn.PendingExtraPlays + "）。");
        }

        public string Describe()
        {
            string extrasPart = extraPlaysOverride > 0 ? extraPlaysOverride.ToString() : "definition.extraPlaysNextTurn";
            return "下回合出牌次数 +" + extrasPart;
        }

        private int ResolveExtras(CardPlayContext context)
        {
            if (extraPlaysOverride > 0)
            {
                return extraPlaysOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null && identity.definition.extraPlaysNextTurn > 0)
            {
                return identity.definition.extraPlaysNextTurn;
            }

            return fallbackExtras > 0 ? fallbackExtras : 0;
        }
    }
}
