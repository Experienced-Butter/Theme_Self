// 归属：monsters（ARCHITECTURE.md 第 9 节、第 12 节）。
// 规则出处：RULES.md 第四节（怪物攻击力）与第六节第 5 条（伤害计算规则）。
using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Monsters
{
    /// <summary>
    /// 怪物普攻组件：命中判定 → 结算伤害 → 扣血。
    /// 伤害 = 攻击力 - 目标防御（下限 0，向下取整），统一走 <see cref="GameRules.ComputeMonsterDamage"/>。
    /// 命中率取 <see cref="Combatant.GetHitRate"/>（基础 100%，可被玩家 B/BB 的 HitDown 降低，
    /// 见 ARCHITECTURE 第 13 节第 8 条）。
    /// </summary>
    public class MonsterAttackComponent : MonoBehaviour
    {
        // 怪物攻击力：怪1 = 580，怪2 = 350（RULES 第四节）
        public int attack;

        /// <summary>
        /// 本怪物完成一次普攻后触发：(受击目标, 实际扣血)。**只用于表现层**（UI 让红色粒子从攻击者
        /// 飞向玩家），不参与任何战斗结算，也不改变本组件既有的公开签名。
        /// 命中与未命中都会触发（未命中时实际扣血为 0），这样「攻击动作」本身始终有反馈。
        /// </summary>
        public System.Action<Combatant, int> OnAttacked;

        /// <summary>
        /// 对 <paramref name="target"/> 执行一次普攻。self 必须是本组件所属的怪物单位。
        /// 本方法签名不含 BattleContext（契约固定），因此战报直接写 UnityEngine.Debug.Log，
        /// 与 Core.BattleContext.Log 使用同一 "[CardGame] " 前缀。
        /// </summary>
        public void PerformAttack(Combatant self, Combatant target, IRandomSource random)
        {
            if (self == null || target == null)
            {
                Debug.LogWarning("[CardGame] MonsterAttackComponent.PerformAttack：self 或 target 为 null，跳过攻击");
                return;
            }

            if (!self.IsAlive || !target.IsAlive)
            {
                return;
            }

            float hitRate = self.GetHitRate();
            bool hit = random != null ? random.Chance(hitRate) : hitRate >= 1f;
            if (!hit)
            {
                Debug.Log(string.Format("[CardGame] {0} 的普攻未命中 {1}（命中率 {2:P0}）",
                    self.displayName, target.displayName, hitRate));
                if (OnAttacked != null)
                {
                    OnAttacked(target, 0);   // 未命中也要有「攻击动作」的表现
                }
                return;
            }

            int defense = target.GetDefense();
            int damage = GameRules.ComputeMonsterDamage(attack, defense);
            int actual = target.TakeDamage(damage, DamageSource.Monster);

            Debug.Log(string.Format("[CardGame] {0} 普攻 → {1}：攻击 {2} - 防御 {3} = {4}，实际扣血 {5}（剩余 {6}/{7}）",
                self.displayName, target.displayName, attack, defense, damage, actual,
                target.CurrentHp, target.MaxHp));

            if (OnAttacked != null)
            {
                OnAttacked(target, actual);
            }
        }
    }
}
