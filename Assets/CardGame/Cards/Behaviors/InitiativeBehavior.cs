using CardGame.Combat;
using CardGame.Core;
using CardGame.Players;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 先手行为：让玩家在下一回合强制先手（AB 卡）。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `InitiativeBehavior`；规则原文第三节「AB 造成伤害，下回合获得先手」；
    /// 第六节第 2 条「初始回合玩家先手，之后每回合交替；玩家 Initiative 状态可强制先手」。
    /// 实际先手判定由 Controllers.TurnOrderComponent 读取该状态完成。
    /// </summary>
    public class InitiativeBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖先手持续回合数，&lt;= 0 时使用默认 1 回合（只影响下一回合）。</summary>
        public int turns = 1;

        /// <summary>先手与伤害同为即时结算，排在伤害之后。</summary>
        public int ResolutionOrder
        {
            get { return 800; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] InitiativeBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant user = context.User;
            if (user == null)
            {
                Debug.LogError("[CardGame] InitiativeBehavior.Resolve 找不到出牌者。");
                return;
            }

            PlayerStatusComponent statuses = user.GetComponent<PlayerStatusComponent>();
            if (statuses == null)
            {
                Debug.LogError("[CardGame] InitiativeBehavior.Resolve 出牌者身上没有 PlayerStatusComponent，无法给予先手。");
                return;
            }

            // 先手是布尔标记：Players.PlayerStatusComponent 用独立字段 + （若存在）状态承载，
            // 重复授予只会刷新同一标记，真正的消费由 TurnOrderComponent 调 ConsumeInitiative 完成。
            statuses.GrantInitiative();

            context.Log(user.displayName + " 获得先手标记，下一回合强制先手。");
        }

        public string Describe()
        {
            int duration = turns > 0 ? turns : 1;
            return "下 " + duration + " 回合获得先手";
        }
    }
}
