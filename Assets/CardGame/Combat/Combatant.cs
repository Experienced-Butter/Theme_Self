using UnityEngine;

namespace CardGame.Combat
{
    /// <summary>
    /// 战斗单位门面：玩家与怪物共用，缓存四个兄弟组件并提供统一的数值读写入口。
    /// </summary>
    // 注意：UnityEngine.RequireComponent 只有 1/2/3 个 Type 参数的构造函数，
    // 4 个类型必须拆成两条 attribute（否则真机 CS1729）。两条同时存在是合法的：
    // 该特性声明了 AllowMultiple = true。
    [RequireComponent(typeof(HealthComponent), typeof(DefenseComponent), typeof(HitRateComponent))]
    [RequireComponent(typeof(StatusComponent))]
    public class Combatant : MonoBehaviour
    {
        /// <summary>阵营。</summary>
        public Core.Team team;

        /// <summary>显示名，用于战报。</summary>
        public string displayName;

        private HealthComponent _health;
        private DefenseComponent _defense;
        private HitRateComponent _hitRate;
        private StatusComponent _statuses;

        /// <summary>生命组件。</summary>
        public HealthComponent Health
        {
            get
            {
                if (_health == null)
                {
                    _health = GetComponent<HealthComponent>();
                }
                return _health;
            }
        }

        /// <summary>防御组件。</summary>
        public DefenseComponent Defense
        {
            get
            {
                if (_defense == null)
                {
                    _defense = GetComponent<DefenseComponent>();
                }
                return _defense;
            }
        }

        /// <summary>命中组件。</summary>
        public HitRateComponent HitRate
        {
            get
            {
                if (_hitRate == null)
                {
                    _hitRate = GetComponent<HitRateComponent>();
                }
                return _hitRate;
            }
        }

        /// <summary>状态组件。</summary>
        public StatusComponent Statuses
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

        /// <summary>是否存活。</summary>
        public bool IsAlive
        {
            get
            {
                HealthComponent health = Health;
                return health != null && health.IsAlive;
            }
        }

        /// <summary>当前生命值。</summary>
        public int CurrentHp
        {
            get
            {
                HealthComponent health = Health;
                return health == null ? 0 : health.currentHp;
            }
        }

        /// <summary>最大生命值。</summary>
        public int MaxHp
        {
            get
            {
                HealthComponent health = Health;
                return health == null ? 0 : health.maxHp;
            }
        }

        /// <summary>有效防御（已计入 DefenseDown）。</summary>
        public int GetDefense()
        {
            DefenseComponent defense = Defense;
            return defense == null ? 0 : defense.GetEffectiveDefense();
        }

        /// <summary>有效命中率（已计入 HitDown）。</summary>
        public float GetHitRate()
        {
            HitRateComponent hitRate = HitRate;
            return hitRate == null ? 0f : hitRate.GetEffectiveHitRate();
        }

        /// <summary>结算受伤，返回实际扣血（免疫时返回 0）。</summary>
        public int TakeDamage(int amount, Core.DamageSource source)
        {
            HealthComponent health = Health;
            return health == null ? 0 : health.ApplyDamage(amount, source);
        }

        /// <summary>结算治疗，返回实际治疗量。</summary>
        public int Heal(int amount)
        {
            HealthComponent health = Health;
            return health == null ? 0 : health.ApplyHeal(amount);
        }
    }
}
