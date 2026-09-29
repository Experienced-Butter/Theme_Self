using UnityEngine;

namespace CardGame.Combat
{
    /// <summary>
    /// 通用生命组件。玩家与怪物共用。
    /// 规则：所有伤害向下取整由 GameRules.FloorToInt 统一处理，本组件只负责上下限与事件广播。
    /// </summary>
    public class HealthComponent : MonoBehaviour
    {
        /// <summary>最大生命值。</summary>
        public int maxHp;

        /// <summary>当前生命值。</summary>
        public int currentHp;

        private StatusComponent _statuses;

        /// <summary>是否存活。</summary>
        public bool IsAlive
        {
            get { return currentHp > 0; }
        }

        /// <summary>血量比例 [0,1]，maxHp &lt;= 0 时按 0 处理。</summary>
        public float HpRatio
        {
            get
            {
                if (maxHp <= 0)
                {
                    return 0f;
                }
                float ratio = (float)currentHp / (float)maxHp;
                if (ratio < 0f)
                {
                    ratio = 0f;
                }
                else if (ratio > 1f)
                {
                    ratio = 1f;
                }
                return ratio;
            }
        }

        /// <summary>实际扣血回调（已结算免疫与下限 0 后的值）。</summary>
        public System.Action<int, Core.DamageSource> OnDamaged;

        /// <summary>实际治疗回调。</summary>
        public System.Action<int> OnHealed;

        /// <summary>死亡回调，仅在本次伤害使其从存活变为阵亡时触发一次。</summary>
        public System.Action OnDied;

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

        /// <summary>以满血初始化。</summary>
        public void Initialize(int maxHp)
        {
            this.maxHp = maxHp < 0 ? 0 : maxHp;
            this.currentHp = this.maxHp;
        }

        /// <summary>
        /// 结算一次伤害。规则：若存在 DamageImmunity 状态，则本次伤害为 0 并消耗该状态（BC 卡效果）；
        /// 否则扣减 currentHp，下限 0。返回实际扣血量。
        /// </summary>
        public int ApplyDamage(int amount, Core.DamageSource source)
        {
            int incoming = amount < 0 ? 0 : amount;

            StatusComponent statuses = Statuses;
            if (statuses != null && statuses.Consume(Core.StatusKind.DamageImmunity))
            {
                // 规则：BC 免疫下次伤害——整次伤害归零并消耗该状态。
                incoming = 0;
            }

            if (incoming > currentHp)
            {
                incoming = currentHp;
            }
            if (incoming < 0)
            {
                incoming = 0;
            }

            bool wasAlive = currentHp > 0;
            currentHp -= incoming;

            if (incoming > 0 && OnDamaged != null)
            {
                OnDamaged(incoming, source);
            }
            if (wasAlive && currentHp <= 0 && OnDied != null)
            {
                OnDied();
            }
            return incoming;
        }

        /// <summary>
        /// 结算一次治疗。规则：不超过 maxHp，返回实际治疗量。
        /// </summary>
        public int ApplyHeal(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }
            int missing = maxHp - currentHp;
            if (missing <= 0)
            {
                return 0;
            }
            int healed = amount > missing ? missing : amount;
            currentHp += healed;
            if (OnHealed != null)
            {
                OnHealed(healed);
            }
            return healed;
        }

        /// <summary>恢复到满血。</summary>
        public void ResetToFull()
        {
            currentHp = maxHp;
        }
    }
}
