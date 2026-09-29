using UnityEngine;

namespace CardGame.Combat
{
    /// <summary>
    /// 通用防御组件。玩家与怪物共用。
    /// 规则：防御参与减法计算（怪物攻击 - 玩家防御），且可被 debuff 降低。
    /// </summary>
    public class DefenseComponent : MonoBehaviour
    {
        /// <summary>基础防御值。玩家 300；怪1 0；怪2 25。</summary>
        public int baseDefense;

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
        /// 有效防御，结果下限 0。契约出处：ARCHITECTURE §14.6（v1.2 正式契约）。
        ///
        /// DefenseDown 用 magnitude 的**数值区间**区分两种减免，这是全局硬约束：
        /// <code>
        /// magnitude ∈ (0, 1]  → 百分比减免：有效防御 = base × (1 - magnitude)   （BB：0.10 即降 10%）
        /// magnitude  &gt; 1     → 固定值减免：有效防御 = base - magnitude          （怪1「减防35」：35 即降 35 点）
        /// 同类状态取最大值（StatusComponent.MagnitudeOf）；结果下限 0。
        /// </code>
        ///
        /// **硬约束（必须遵守，否则语义翻转）：百分比必须 ≤ 1，固定值必须 &gt; 1。**
        /// 新增任何降防/降命中效果前，先确认它落在哪个区间——
        /// 例如加一个「防御 -1」的效果会被误判成 100% 减防。
        ///
        /// 两种情形不会并存于同一战斗单位：**BB 的降防作用于怪物（玩家出牌），怪1「减防35」作用于玩家（怪物行动）**，
        /// 加上 StatusComponent.Add 对同类状态只保留一条（取最大值），因此单值区间约定在本规则集内是完备的。
        /// 已知限制：若将来出现「固定值 ≤ 1」或「百分比 &gt; 100%」的效果，本约定会误判，届时应改用双字段方案。
        /// </summary>
        public int GetEffectiveDefense()
        {
            float magnitude = 0f;

            StatusComponent statuses = Statuses;
            if (statuses != null)
            {
                magnitude = statuses.MagnitudeOf(Core.StatusKind.DefenseDown);
            }

            float effective = (float)baseDefense;
            if (magnitude > 0f)
            {
                effective = magnitude > 1f
                    ? (float)baseDefense - magnitude              // 固定值减免（怪1 减防 35）
                    : (float)baseDefense * (1f - magnitude);      // 百分比减免（BB 降防 10%）
            }

            int result = Core.GameRules.FloorToInt(effective);
            return result < 0 ? 0 : result;
        }
    }
}
