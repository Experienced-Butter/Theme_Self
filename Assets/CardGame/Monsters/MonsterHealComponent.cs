// 归属：monsters（ARCHITECTURE.md 第 9 节、第 12 节）。
// 规则出处：RULES.md 第四节 怪物2（辅助型）「治疗：回复己方全体55点生命，下两回合持续恢复28点生命」。
using System.Collections.Generic;
using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Monsters
{
    /// <summary>
    /// 怪2（辅助型）的治疗组件：对全体友军（含自身）立即回复 <see cref="instant"/> 点生命，
    /// 并附加 <see cref="perTurn"/> 点/回合的持续恢复（Regen 状态），持续 <see cref="turns"/> 回合。
    /// </summary>
    public class MonsterHealComponent : MonoBehaviour
    {
        // 55 立即回复 + 28 点/回合 × 2 回合（RULES 第四节）
        public int instant = 55;
        public int perTurn = 28;
        public int turns = 2;

        /// <summary>
        /// 治疗全体友军：立即治疗 + 附加 Regen 状态。
        /// Regen 的每回合生效由回合流程（Controllers.GameFlowComponent）统一结算，
        /// 与玩家 CC 卡的 Regen 走同一套状态逻辑，因此本组件不自行扣减回合数。
        /// </summary>
        public void HealAllies(Combatant self, Core.BattleContext battle)
        {
            if (self == null)
            {
                Debug.LogWarning("[CardGame] MonsterHealComponent.HealAllies：self 为 null，跳过治疗");
                return;
            }

            List<Combatant> allies = CollectAllies(self, battle);
            if (allies.Count == 0)
            {
                Log(battle, self.displayName + " 使用治疗，但己方已无存活单位");
                return;
            }

            Log(battle, string.Format("{0} 使用治疗：对 {1} 名友军立即回复 {2} 点生命，并附加 {3} 点/回合的持续恢复（{4} 回合）",
                self.displayName, allies.Count, instant, perTurn, turns));

            int healedTotal = 0;
            for (int i = 0; i < allies.Count; i++)
            {
                Combatant ally = allies[i];
                int healed = ally.Heal(instant);
                healedTotal += healed;

                StatusComponent statuses = ally.Statuses;
                if (statuses != null)
                {
                    // Regenerate 文案：magnitude = 每回合点数（ARCHITECTURE 第 2 节 StatusEffect 约定）
                    statuses.Add(StatusKind.Regen, perTurn, turns, false);
                }

                Log(battle, string.Format("  └ {0} 回复 {1} 点（当前 {2}/{3}，获得持续恢复 {4}×{5} 回合）",
                    ally.displayName, healed, ally.CurrentHp, ally.MaxHp, perTurn, turns));
            }

            Log(battle, string.Format("{0} 的治疗合计生效 {1} 点生命", self.displayName, healedTotal));
        }

        // 「己方全体」：同阵营存活单位，含施法者自身。
        // BattleContext.AlliesOf 是否包含自身由 Core 实现决定，这里统一补齐，避免漏掉施法者。
        private static List<Combatant> CollectAllies(Combatant self, Core.BattleContext battle)
        {
            List<Combatant> result = new List<Combatant>();

            List<Combatant> allies = battle != null ? battle.AlliesOf(self) : null;
            if (allies != null)
            {
                for (int i = 0; i < allies.Count; i++)
                {
                    Combatant ally = allies[i];
                    if (ally == null || !ally.IsAlive || result.Contains(ally))
                    {
                        continue;
                    }

                    result.Add(ally);
                }
            }

            if (self.IsAlive && !result.Contains(self))
            {
                result.Add(self);
            }

            return result;
        }

        private static void Log(Core.BattleContext battle, string message)
        {
            if (battle != null)
            {
                battle.Log(message);
            }
            else
            {
                Debug.Log("[CardGame] " + message);
            }
        }
    }
}
