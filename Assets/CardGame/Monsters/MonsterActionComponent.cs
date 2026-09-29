// 归属：monsters（ARCHITECTURE.md 第 9 节、第 12 节）。
// 规则出处：RULES.md 第六节第 6 条「怪物出招方式（固定顺序）」。
using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Monsters
{
    /// <summary>
    /// 怪物行动组件：每回合行动 1 次，按 <see cref="sequence"/> 的固定顺序取当前行动，
    /// 执行后下标 +1，走到末尾回到 0 继续循环。
    /// 怪1（攻击型）：普攻 → 普攻 → 减防35 → 循环。
    /// 怪2（辅助型）：普攻 → 辅助 → 治疗 → 循环。
    /// </summary>
    public class MonsterActionComponent : MonoBehaviour
    {
        // 固定出招顺序。由 SceneTemplateBuilder 按 RULES.md 填入怪物模板，
        // 克隆时由 Templates.MonsterTemplate.OnAfterClone 写入克隆体。
        public MonsterAction[] sequence;

        // 当前行动下标（实例内部状态，不属于公开契约）。模板下标的初值为 0，
        // 克隆体随模板复制到 0，因此每个运行时怪物都从序列第一项开始。
        [SerializeField] private int actionIndex;

        /// <summary>
        /// 执行本回合的 1 次行动，并把出招下标推进一格（循环）。
        /// </summary>
        public void ExecuteTurn(Core.BattleContext battle)
        {
            if (battle == null)
            {
                Debug.LogWarning("[CardGame] MonsterActionComponent.ExecuteTurn：battle 为 null，跳过本回合行动");
                return;
            }

            Combatant self = GetComponent<Combatant>();
            if (self == null || !self.IsAlive)
            {
                // 自身阵亡（或没挂 Combatant）时不行动；控制器可能仍在遍历敌方列表。
                return;
            }

            if (sequence == null || sequence.Length == 0)
            {
                battle.Log(self.displayName + " 未配置出招顺序（MonsterActionComponent.sequence 为空），本回合不行动");
                return;
            }

            if (actionIndex < 0 || actionIndex >= sequence.Length)
            {
                actionIndex = 0;
            }

            MonsterAction action = sequence[actionIndex];
            int order = actionIndex + 1;
            actionIndex = (actionIndex + 1) % sequence.Length;   // 执行后 +1，末尾回到 0

            battle.Log(string.Format("{0} 本回合行动（{1}/{2}）：{3}",
                self.displayName, order, sequence.Length, DescribeAction(action)));

            switch (action)
            {
                case MonsterAction.Attack:
                    ExecuteAttack(battle, self);
                    break;
                case MonsterAction.DefenseBreak:
                    ExecuteDefenseBreak(battle, self);
                    break;
                case MonsterAction.Support:
                    ExecuteSupport(battle, self);
                    break;
                case MonsterAction.Heal:
                    ExecuteHeal(battle, self);
                    break;
            }
        }

        // 普攻：怪物始终攻击玩家（RULES 第四节：怪物攻击力 - 玩家防御）
        private void ExecuteAttack(Core.BattleContext battle, Combatant self)
        {
            MonsterAttackComponent attack = GetComponent<MonsterAttackComponent>();
            if (attack == null)
            {
                battle.Log(self.displayName + " 缺少 MonsterAttackComponent，普攻未生效");
                return;
            }

            Combatant target = battle.Player;
            if (target == null || !target.IsAlive)
            {
                battle.Log(self.displayName + " 无攻击目标（玩家已阵亡）");
                return;
            }

            attack.PerformAttack(self, target, battle.Random);
        }

        // 减防：给玩家施加 DefenseDown（固定值 35）
        private void ExecuteDefenseBreak(Core.BattleContext battle, Combatant self)
        {
            MonsterDefenseBreakComponent defenseBreak = GetComponent<MonsterDefenseBreakComponent>();
            if (defenseBreak == null)
            {
                battle.Log(self.displayName + " 缺少 MonsterDefenseBreakComponent，减防未生效");
                return;
            }

            Combatant target = battle.Player;
            if (target == null || !target.IsAlive)
            {
                battle.Log(self.displayName + " 无减防目标（玩家已阵亡）");
                return;
            }

            defenseBreak.PerformDefenseBreak(self, target);
        }

        // 辅助（怪2）：对全体友军治疗 + 持续恢复
        private void ExecuteSupport(Core.BattleContext battle, Combatant self)
        {
            MonsterSupportComponent support = GetComponent<MonsterSupportComponent>();
            if (support == null)
            {
                battle.Log(self.displayName + " 缺少 MonsterSupportComponent，辅助未生效");
                return;
            }

            support.PerformSupport(self, battle);
        }

        // 治疗（怪2）：回复己方全体 55 点生命 + 28 点/回合持续 2 回合
        private void ExecuteHeal(Core.BattleContext battle, Combatant self)
        {
            MonsterHealComponent heal = GetComponent<MonsterHealComponent>();
            if (heal == null)
            {
                battle.Log(self.displayName + " 缺少 MonsterHealComponent，治疗未生效");
                return;
            }

            heal.HealAllies(self, battle);
        }

        private static string DescribeAction(MonsterAction action)
        {
            switch (action)
            {
                case MonsterAction.Attack:
                    return "普攻";
                case MonsterAction.DefenseBreak:
                    return "减防35";
                case MonsterAction.Support:
                    return "辅助";
                case MonsterAction.Heal:
                    return "治疗";
                default:
                    return action.ToString();
            }
        }
    }
}
