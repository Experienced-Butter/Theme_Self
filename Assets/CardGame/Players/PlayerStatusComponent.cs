using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Players
{
    /// <summary>
    /// 玩家状态薄封装（转调同实体的 Combat.StatusComponent）。
    /// 规则出处：
    ///   规则三 —— AB（A+B）：造成伤害，下回合获得先手。
    ///   规则六第 1 条 —— 卡牌复制器：仅复制 ABC，获得一次合成次数（FreeFusion）。
    /// 契约出处：ARCHITECTURE 第 7 节 + 第 14.4 节 —— 先手标记使用 StatusKind.Initiative，
    /// 由 TurnOrderComponent 在决定先手时查询并消耗；额外出手次数不是状态，留在 PlayerTurnComponent。
    /// </summary>
    public class PlayerStatusComponent : MonoBehaviour
    {
        /// <summary>
        /// FreeFusion 状态的保留回合数。规则与 ARCHITECTURE 均未规定持续时间，
        /// 按「获得一次合成次数」的语义处理为保留到被 ConsumeFreeFusion 消费为止。
        /// </summary>
        public int freeFusionTurns = 99;

        /// <summary>
        /// 先手状态的剩余回合数（ARCHITECTURE 第 14.4 节：remainingTurns = 1）。
        /// ⚠️ 时序提示：先手在本回合结束时写入，而 Controllers.GameFlowComponent.EndTurn() 的
        /// 第 2 步（状态 tick）在第 5 步（TurnOrderComponent.AdvanceTurn 查询并消费先手）之前执行，
        /// 因此 1 回合的先手可能在被消费前就被 tick 掉。已向队长提交实测证据；
        /// 若决定由 flow 把 AdvanceTurn 提前到状态 tick 之前，保持 1 即可；
        /// 若不调整顺序，把本字段改为 2 是唯一改动点（无需改任何代码）。
        /// </summary>
        public int initiativeStatusTurns = 1;

        private StatusComponent cachedStatuses;

        /// <summary>是否持有先手标记（规则三：AB 下回合获得先手）。直接查询 StatusKind.Initiative。</summary>
        public bool HasInitiative
        {
            get
            {
                StatusComponent statuses = ResolveStatuses();
                return statuses != null && statuses.Has(StatusKind.Initiative);
            }
        }

        /// <summary>授予先手标记（规则三：AB 触发，下回合玩家强制先手）。</summary>
        public void GrantInitiative()
        {
            StatusComponent statuses = ResolveStatuses();
            if (statuses == null)
            {
                Debug.LogWarning("[CardGame] PlayerStatusComponent.GrantInitiative：找不到 Combat.StatusComponent");
                return;
            }

            statuses.Add(StatusKind.Initiative, 1f, initiativeStatusTurns, true);
        }

        /// <summary>消费先手标记（由 Controllers.TurnOrderComponent 在决定先手时调用）。</summary>
        public void ConsumeInitiative()
        {
            StatusComponent statuses = ResolveStatuses();
            if (statuses == null)
            {
                return;
            }

            // 先手是布尔标记：清空全部同类层，避免残留导致一直先手。
            while (statuses.Consume(StatusKind.Initiative))
            {
            }
        }

        /// <summary>是否持有免费合成次数（规则六第 1 条：卡牌复制器给的合成次数）。</summary>
        public bool HasFreeFusion
        {
            get
            {
                StatusComponent statuses = ResolveStatuses();
                return statuses != null && statuses.Has(StatusKind.FreeFusion);
            }
        }

        /// <summary>授予一次免费合成次数（规则六第 1 条：卡牌复制器）。</summary>
        public void GrantFreeFusion()
        {
            StatusComponent statuses = ResolveStatuses();
            if (statuses == null)
            {
                Debug.LogWarning("[CardGame] PlayerStatusComponent.GrantFreeFusion：找不到 Combat.StatusComponent");
                return;
            }

            statuses.Add(StatusKind.FreeFusion, 1f, freeFusionTurns, true);
        }

        /// <summary>消费一次免费合成次数，返回是否确实持有。</summary>
        public bool ConsumeFreeFusion()
        {
            StatusComponent statuses = ResolveStatuses();
            if (statuses == null)
            {
                return false;
            }

            return statuses.Consume(StatusKind.FreeFusion);
        }

        /// <summary>回合结束结算状态：所有状态剩余回合 -1 并移除到期的（转调 StatusComponent.TickTurnEnd）。</summary>
        public void TickStatuses()
        {
            StatusComponent statuses = ResolveStatuses();
            if (statuses == null)
            {
                Debug.LogWarning("[CardGame] PlayerStatusComponent.TickStatuses：找不到 Combat.StatusComponent");
                return;
            }

            statuses.TickTurnEnd();
        }

        private StatusComponent ResolveStatuses()
        {
            if (cachedStatuses != null)
            {
                return cachedStatuses;
            }

            cachedStatuses = GetComponent<StatusComponent>();
            if (cachedStatuses == null)
            {
                Combatant combatant = GetComponent<Combatant>();
                if (combatant != null)
                {
                    cachedStatuses = combatant.Statuses;
                }
            }

            return cachedStatuses;
        }
    }
}
