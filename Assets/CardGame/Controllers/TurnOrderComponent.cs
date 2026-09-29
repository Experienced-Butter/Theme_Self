namespace CardGame.Controllers
{
    /// <summary>
    /// 先手轮换。
    /// 规则出处：RULES.md 第六节第 2 条「初始回合为玩家先手。之后每回合玩家与敌人交替先手
    ///          （玩家先手 → 敌人先手 → 玩家先手 → …）」；
    ///          规则三 AB（A+B）「造成伤害，下回合获得先手」。
    /// 契约出处：ARCHITECTURE.md 第 8 节 `Controllers/TurnOrderComponent.cs`。
    /// 结算时机：GameFlowComponent 在每回合结束时调用 AdvanceTurn，得到「下一回合」的先手方；
    ///           若玩家持有先手状态（PlayerStatusComponent.HasInitiative），该回合强制玩家先手并消耗该状态。
    /// </summary>
    public class TurnOrderComponent : UnityEngine.MonoBehaviour
    {
        private int _turnIndex;                 // 0 = 初始回合（玩家先手）
        private bool _playerActsFirst = true;   // 当前回合先手方
        private Players.PlayerStatusComponent _playerStatus;

        /// <summary>
        /// 本回合是否玩家先手。初始回合恒为 true（规则六第 2 条）。
        /// </summary>
        public bool PlayerActsFirst
        {
            get { return _playerActsFirst; }
        }

        /// <summary>
        /// 重置到初始回合状态：玩家先手。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void Reset()`。
        /// </summary>
        public void Reset()
        {
            _turnIndex = 0;
            _playerActsFirst = true;
            _playerStatus = null;
        }

        /// <summary>
        /// 推进到下一回合的先手方：
        /// 玩家持有 Initiative 状态 → 强制玩家先手并消耗该状态；否则玩家与敌人交替先手。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void AdvanceTurn()`。
        /// </summary>
        public void AdvanceTurn()
        {
            _turnIndex++;

            Players.PlayerStatusComponent status = ResolvePlayerStatus();
            if (status != null && status.HasInitiative)
            {
                // 规则三：AB 使玩家下回合获得先手——强制先手并一次性消耗（PlayerStatusComponent 是唯一权威来源）。
                status.ConsumeInitiative();
                _playerActsFirst = true;
                return;
            }

            _playerActsFirst = (_turnIndex % 2) == 0;
        }

        private Players.PlayerStatusComponent ResolvePlayerStatus()
        {
            if (_playerStatus != null)
            {
                return _playerStatus;
            }

            GameController controller = GameController.Instance;
            if (controller == null || controller.Context == null || controller.Context.Player == null)
            {
                return null;
            }

            _playerStatus = controller.Context.Player.GetComponent<Players.PlayerStatusComponent>();
            return _playerStatus;
        }
    }
}
