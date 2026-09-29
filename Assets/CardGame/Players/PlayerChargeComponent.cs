using CardGame.Core;
using UnityEngine;

namespace CardGame.Players
{
    /// <summary>
    /// 玩家充能（大招能量）。
    /// 规则出处：规则六第 1 条 —— 充能获取：只有合卡（合成卡牌）时才获得 1 点充能；
    /// 大招触发：累计 4 点充能时，可获得大招卡牌（由 Controller 授予，见 ARCHITECTURE 第 13 节第 5 条）。
    /// 本组件只维护充能数值，不负责生成大招卡。
    /// </summary>
    public class PlayerChargeComponent : MonoBehaviour
    {
        /// <summary>当前充能点数。规则六第 1 条：只有合成才 +1，其余行为不改变充能。</summary>
        public int charge;

        /// <summary>触发大招所需的充能点数（规则六第 1 条：4 点；取自 GameRules.ChargeThreshold）。</summary>
        public int Threshold
        {
            get { return GameRules.ChargeThreshold; }
        }

        /// <summary>是否已累计到 4 点（就绪；等待 Controller 授予大招卡后调用 Consume 清零）。</summary>
        public bool IsReady
        {
            get { return charge >= GameRules.ChargeThreshold; }
        }

        /// <summary>
        /// 合成时 +1 充能（规则六第 1 条：只有合卡才获得充能）。
        /// 返回 1 表示本次正好累计到阈值 4 点、触发大招（Controller 负责授予大招卡并随后 Consume）；
        /// 其余情况返回 0。
        /// </summary>
        public int AddFromFusion()
        {
            charge += 1;

            if (charge == GameRules.ChargeThreshold)
            {
                Debug.Log("[CardGame] 玩家充能已达 " + GameRules.ChargeThreshold + " 点：大招就绪，等待授予大招卡");
                return 1;
            }

            return 0;
        }

        /// <summary>大招卡授予后清零（规则六第 1 条；契约要求由 Controller 调用）。</summary>
        public void Consume()
        {
            charge = 0;
        }
    }
}
