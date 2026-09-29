// 归属：monsters（ARCHITECTURE.md 第 9 节、第 12 节）。
// 规则出处：RULES.md 第六节第 6 条（怪2 出招顺序含「辅助」）+ 第四节（怪2 的治疗数值）。
using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Monsters
{
    /// <summary>
    /// 怪2（辅助型）的「辅助」行动组件。
    /// RULES 第四节只定义了怪2 的一种增益效果（回复己方全体 55 点 + 下两回合持续恢复 28 点），
    /// 因此固定顺序里的「辅助」与「治疗」执行同一效果；数值只在 MonsterHealComponent 上定义一次，
    /// 本组件负责取用同物体上的治疗组件，避免两处数值各自漂移。
    /// </summary>
    public class MonsterSupportComponent : MonoBehaviour
    {
        // 同物体上的治疗组件（由 SceneTemplateBuilder 装配怪物模板时成对添加）。
        [SerializeField] private MonsterHealComponent healer;

        /// <summary>
        /// 执行「辅助」行动：治疗全体友军（55 立即 + 28 点/回合 × 2 回合）。
        /// </summary>
        public void PerformSupport(Combatant self, Core.BattleContext battle)
        {
            MonsterHealComponent heal = ResolveHealer();
            if (heal == null)
            {
                string who = self != null ? self.displayName : "怪物";
                Log(battle, who + " 使用辅助，但缺少 MonsterHealComponent，效果未生效");
                return;
            }

            heal.HealAllies(self, battle);
        }

        private MonsterHealComponent ResolveHealer()
        {
            if (healer != null)
            {
                return healer;
            }

            healer = GetComponent<MonsterHealComponent>();
            return healer;
        }

        private static void Log(Core.BattleContext battle, string message)
        {
            if (battle != null)
            {
                battle.Log(message);
            }
            else
            {
                Debug.LogWarning("[CardGame] " + message);
            }
        }
    }
}
