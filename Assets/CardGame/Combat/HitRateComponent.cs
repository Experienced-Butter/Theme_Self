using UnityEngine;

namespace CardGame.Combat
{
    /// <summary>
    /// 通用命中组件。玩家与怪物共用。
    /// 规则：玩家命中率 100% 固定；怪物命中率 100% 且可被 B/BB 降低（ARCHITECTURE 13.8）。
    /// </summary>
    public class HitRateComponent : MonoBehaviour
    {
        /// <summary>基础命中率，默认 100%。</summary>
        public float baseHitRate = 1f;

        private StatusComponent _statuses;

        private void Awake()
        {
            _statuses = GetComponent<StatusComponent>();
        }

        private StatusComponent Statuses
        {
            get
            {
                if (_statuses == null)
                {
                    _statuses = GetComponent<StatusComponent>();
                }
                return _statuses;
            }
        }

        /// <summary>
        /// 有效命中率 = base - HitDown 百分比，夹到 [0,1]。
        /// </summary>
        public float GetEffectiveHitRate()
        {
            float result = baseHitRate;

            StatusComponent statuses = Statuses;
            if (statuses != null)
            {
                // 规则：B 降低敌方 10% 命中 1 回合；BB 同样降低 10%。
                result -= statuses.MagnitudeOf(Core.StatusKind.HitDown);
            }

            if (result < 0f)
            {
                result = 0f;
            }
            else if (result > 1f)
            {
                result = 1f;
            }
            return result;
        }

        /// <summary>按当前有效命中率做一次命中判定。</summary>
        public bool RollToHit(Core.IRandomSource random)
        {
            if (random == null)
            {
                return false;
            }
            return random.Chance(GetEffectiveHitRate());
        }
    }
}
