using CardGame.Combat;
using UnityEngine;

namespace CardGame.Players
{
    /// <summary>
    /// 玩家实体克隆后的运行时初始化。
    /// 调用方：Templates.PlayerTemplate.OnAfterClone（ARCHITECTURE 第 5 节）。
    /// 职责：找齐玩家组件、重置回合计数、清空手牌/牌库标记，使克隆体可以独立工作
    ///      （ARCHITECTURE 第 0 节第 6 条：克隆后必须能独立工作）。
    /// </summary>
    public static class PlayerInitializer
    {
        /// <summary>初始化克隆出来的玩家实体（可重复调用，幂等）。</summary>
        public static void Initialize(GameObject playerInstance)
        {
            if (playerInstance == null)
            {
                Debug.LogError("[CardGame] PlayerInitializer.Initialize：playerInstance 为 null");
                return;
            }

            // 回合计数：重置已用出牌次数、本回合额外次数，并清掉挂起的额外出牌次数
            PlayerTurnComponent turn = playerInstance.GetComponent<PlayerTurnComponent>();
            if (turn != null)
            {
                turn.ResetTurnState();
            }

            // 手牌：清空
            PlayerHandComponent hand = playerInstance.GetComponent<PlayerHandComponent>();
            if (hand != null)
            {
                hand.Clear();
            }

            // 牌库 / 弃牌堆：清空（牌库由流程在开局时通过 BuildFromLibrary 重新构建）
            PlayerDeckComponent deck = playerInstance.GetComponent<PlayerDeckComponent>();
            if (deck != null)
            {
                deck.ClearPiles();
            }

            // 充能：归零
            PlayerChargeComponent charge = playerInstance.GetComponent<PlayerChargeComponent>();
            if (charge != null)
            {
                charge.Consume();
            }

            // 玩家标记：先手标记与免费合成次数归零
            PlayerStatusComponent playerStatuses = playerInstance.GetComponent<PlayerStatusComponent>();
            if (playerStatuses != null)
            {
                playerStatuses.ConsumeInitiative();
                playerStatuses.ConsumeFreeFusion();
            }

            // 通用状态层：清空全部状态
            StatusComponent statuses = playerInstance.GetComponent<StatusComponent>();
            if (statuses != null)
            {
                statuses.Clear();
            }

            Debug.Log("[CardGame] 玩家实体初始化完成：" + playerInstance.name);
        }
    }
}
