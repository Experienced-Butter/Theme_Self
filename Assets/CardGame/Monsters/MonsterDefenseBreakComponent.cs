// 归属：monsters（ARCHITECTURE.md 第 9 节、第 12 节）。
// 规则出处：RULES.md 第六节第 6 条「怪1 出招顺序：普攻 → 普攻 → 减防35」。
using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Monsters
{
    /// <summary>
    /// 怪1（攻击型）的减防行动：给玩家施加 DefenseDown，抬高后续普攻的实际伤害。
    /// 数值：固定值 35（ARCHITECTURE 第 13 节第 1 条：按固定值理解，非百分比），
    /// 持续回合数规则文档未写，本方案默认 2 回合。
    /// magnitude 约定：Combat/DefenseComponent 把 magnitude &gt; 1 视为固定值减免、≤ 1 视为百分比减免，
    /// 因此这里传入 35 表示「防御 -35」（与玩家侧 BB 的 0.10 百分比走同一条路径）。
    /// </summary>
    public class MonsterDefenseBreakComponent : MonoBehaviour
    {
        public int flatReduction = 35;
        public int turns = 2;

        /// <summary>
        /// 对 <paramref name="target"/> 施加 DefenseDown（固定值 <see cref="flatReduction"/>，<see cref="turns"/> 回合）。
        /// </summary>
        public void PerformDefenseBreak(Combatant self, Combatant target)
        {
            if (self == null || target == null)
            {
                Debug.LogWarning("[CardGame] MonsterDefenseBreakComponent.PerformDefenseBreak：self 或 target 为 null，跳过减防");
                return;
            }

            if (!self.IsAlive || !target.IsAlive)
            {
                return;
            }

            StatusComponent statuses = target.Statuses;
            if (statuses == null)
            {
                Debug.LogWarning("[CardGame] " + target.displayName + " 缺少 StatusComponent，减防未生效");
                return;
            }

            int before = target.GetDefense();
            statuses.Add(StatusKind.DefenseDown, flatReduction, turns, false);
            int after = target.GetDefense();

            Debug.Log(string.Format("[CardGame] {0} 减防：对 {1} 施加 DefenseDown（固定 {2}，{3} 回合），防御 {4} → {5}",
                self.displayName, target.displayName, flatReduction, turns, before, after));
        }
    }
}
