using CardGame.Core;
using UnityEngine;

namespace CardGame.Players
{
    /// <summary>
    /// 玩家回合资源（出牌次数）。
    /// 规则出处：
    ///   规则一 —— 每回合出牌 4 次（每回合最多打出 4 张牌）。
    ///   规则三 / 规则六第 1 条 —— AC「下回合出牌次数 +1」、大招「下回合增加两次出手机会」，
    ///                             即 +1 / +2 都在**下一回合**生效（ARCHITECTURE 第 13 节第 5 条）。
    ///   规则六第 1 条 —— 合成操作消耗 1 次出牌次数（合成走 CardFusionComponent，最终调用本组件 TryConsumePlay）。
    /// </summary>
    public class PlayerTurnComponent : MonoBehaviour
    {
        /// <summary>每回合基础出牌次数（规则一：4 次）。默认值取自 GameRules.PlaysPerTurn。</summary>
        public int playsPerTurn = GameRules.PlaysPerTurn;

        private int playsUsed;
        private int pendingExtraPlays;      // 已授予、待下一回合结算的额外出牌次数（AC / 大招）
        private int extraPlaysThisTurn;     // 本回合实际生效的额外出牌次数

        /// <summary>本回合已使用的出牌次数。</summary>
        public int PlaysUsed
        {
            get { return playsUsed; }
        }

        /// <summary>本回合剩余出牌次数（下限 0）。</summary>
        public int PlaysRemaining
        {
            get { return Mathf.Max(0, playsPerTurn + extraPlaysThisTurn - playsUsed); }
        }

        /// <summary>已挂起、将在下一回合生效的额外出牌次数（AC 的 +1、大招的 +2）。</summary>
        public int PendingExtraPlays
        {
            get { return pendingExtraPlays; }
        }

        /// <summary>本回合是否还能出牌（或合成）。</summary>
        public bool CanPlay
        {
            get { return PlaysRemaining > 0; }
        }

        /// <summary>
        /// 消耗 1 次出牌次数（出牌与合卡共用）。次数用尽时返回 false 且不改变状态。
        /// </summary>
        public bool TryConsumePlay()
        {
            if (!CanPlay)
            {
                return false;
            }

            playsUsed += 1;
            return true;
        }

        /// <summary>
        /// 授予**下一回合**的额外出牌次数（AC 的 +1、大招的 +2）。
        /// 规则出处：ARCHITECTURE 第 13 节第 5 条 + 规则三（AC：下回合出牌次数 +1）。
        /// </summary>
        public void GrantExtraPlaysNextTurn(int n)
        {
            if (n <= 0)
            {
                return;
            }

            pendingExtraPlays += n;
        }

        /// <summary>
        /// 回合开始：重置本回合已用次数，并结算上一回合挂起的额外出牌次数（下一回合才生效）。
        /// </summary>
        public void BeginTurn()
        {
            playsUsed = 0;
            extraPlaysThisTurn = pendingExtraPlays;
            pendingExtraPlays = 0;
        }

        /// <summary>
        /// 回合结束：本回合未使用的额外次数不跨回合保留，剩余出牌次数作废（CanPlay 变为 false）；
        /// 同时清理本回合未使用的大招临时卡（规则六第 1 条 + ARCHITECTURE 第 13 节第 6 条）。
        /// </summary>
        public void EndTurn()
        {
            extraPlaysThisTurn = 0;
            playsUsed = playsPerTurn;   // 本回合剩余次数归零，避免回合外出牌

            PlayerHandComponent hand = GetComponent<PlayerHandComponent>();
            if (hand != null)
            {
                hand.RemoveTemporaryCards();
            }
        }

        /// <summary>
        /// 把回合资源恢复到全新实体的初始状态：已用次数、本回合额外次数、挂起的额外次数全部归零。
        /// 供 PlayerInitializer 在克隆体初始化时调用（不能复用 BeginTurn：那会把上一回合挂起的 +1/+2 带入新回合）。
        /// </summary>
        public void ResetTurnState()
        {
            playsUsed = 0;
            extraPlaysThisTurn = 0;
            pendingExtraPlays = 0;
        }
    }
}
