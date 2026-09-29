using CardGame.Core;
using UnityEngine;

namespace CardGame.Templates
{
    /// <summary>
    /// 怪物模板实体。
    /// 规则：怪1（攻击型）出招 普攻→普攻→减防35→循环；怪2（辅助型）出招 普攻→辅助→治疗→循环。
    /// </summary>
    public class MonsterTemplate : EntityTemplate
    {
        /// <summary>怪物类型：Attacker = 怪1，Support = 怪2。</summary>
        public MonsterKind kind;

        /// <summary>最大生命值（怪1 关一/关二 2000、支线 1600；怪2 1000）。</summary>
        public int maxHp;

        /// <summary>防御（怪1 0，怪2 25）。</summary>
        public int defense;

        /// <summary>攻击力（怪1 580，怪2 350）。</summary>
        public int attack;

        /// <summary>命中率，默认 100%（可被 B/BB 降低）。</summary>
        public float hitRate = 1f;

        /// <summary>固定出招顺序，循环执行。</summary>
        public MonsterAction[] actionSequence;

        /// <summary>
        /// 把模板参数写入克隆体的战斗组件与出招组件。
        /// actionSequence 会被重新分配为新数组，克隆体与模板不共享同一个数组实例。
        /// </summary>
        protected override void OnAfterClone(GameObject clone)
        {
            if (clone == null)
            {
                return;
            }

            Combat.Combatant combatant = clone.GetComponent<Combat.Combatant>();
            if (combatant == null)
            {
                combatant = clone.GetComponentInChildren<Combat.Combatant>(true);
            }
            if (combatant != null)
            {
                // 规则：怪物均属敌方阵营。
                combatant.team = Core.Team.Enemy;
                combatant.displayName = DisplayNameOf(kind) + "-" + templateId;
            }

            Combat.HealthComponent health = clone.GetComponent<Combat.HealthComponent>();
            if (health == null)
            {
                health = clone.GetComponentInChildren<Combat.HealthComponent>(true);
            }
            if (health != null)
            {
                health.Initialize(maxHp);
            }

            Combat.DefenseComponent defenseComponent = clone.GetComponent<Combat.DefenseComponent>();
            if (defenseComponent == null)
            {
                defenseComponent = clone.GetComponentInChildren<Combat.DefenseComponent>(true);
            }
            if (defenseComponent != null)
            {
                defenseComponent.baseDefense = defense;
            }

            Combat.HitRateComponent hitRateComponent = clone.GetComponent<Combat.HitRateComponent>();
            if (hitRateComponent == null)
            {
                hitRateComponent = clone.GetComponentInChildren<Combat.HitRateComponent>(true);
            }
            if (hitRateComponent != null)
            {
                hitRateComponent.baseHitRate = hitRate;
            }

            Monsters.MonsterActionComponent action = clone.GetComponent<Monsters.MonsterActionComponent>();
            if (action == null)
            {
                action = clone.GetComponentInChildren<Monsters.MonsterActionComponent>(true);
            }
            if (action != null)
            {
                // 独立副本：避免克隆体与模板共享同一个数组实例。
                action.sequence = CloneSequence(actionSequence);
            }

            Monsters.MonsterAttackComponent attackComponent =
                clone.GetComponent<Monsters.MonsterAttackComponent>();
            if (attackComponent == null)
            {
                attackComponent = clone.GetComponentInChildren<Monsters.MonsterAttackComponent>(true);
            }
            if (attackComponent != null)
            {
                attackComponent.attack = attack;
            }
        }

        private static MonsterAction[] CloneSequence(MonsterAction[] source)
        {
            if (source == null || source.Length == 0)
            {
                return new MonsterAction[0];
            }
            MonsterAction[] copy = new MonsterAction[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                copy[i] = source[i];
            }
            return copy;
        }

        /// <summary>怪1 = 攻击型，怪2 = 辅助型。</summary>
        private static string DisplayNameOf(MonsterKind kind)
        {
            return kind == MonsterKind.Support ? "辅助型怪物" : "攻击型怪物";
        }
    }
}
