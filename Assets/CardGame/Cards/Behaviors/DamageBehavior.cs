using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 伤害行为：对 context.Target 造成 damage × CardBuffComponent.damageMultiplier。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `DamageBehavior`；规则原文第五节
    /// 「玩家对怪物伤害 = 卡牌基础伤害 × 强化倍率 - 怪物防御，所有伤害向下取整」。
    /// A(400) / AA(720，强化后 828) / AB(360) / AC(360) 使用本行为。
    ///
    /// 【命中语义】玩家出牌**必定命中**：规则一「命中率 100%」+ ARCHITECTURE 第 13 节第 8 条
    /// 「玩家命中率恒 100%（不受任何 debuff 影响）」。因此本行为不做命中判定、不消耗随机数。
    /// 目标身上的 HitDown（B/BB 施加减命中）只影响**该怪物自己的攻击**，其判定在
    /// Monsters.MonsterAttackComponent.PerformAttack 内完成，与本行为无关。
    /// </summary>
    public class DamageBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖基础伤害，&lt;= 0 时改用 definition.damage。</summary>
        public int damageOverride;

        /// <summary>覆盖强化倍率，&lt;= 0 时改用本卡 CardBuffComponent.damageMultiplier（再退回 1）。</summary>
        public float multiplierOverride;

        /// <summary>伤害行为最先结算，为后续的状态/增益行为提供已生效的战斗结果。</summary>
        public int ResolutionOrder
        {
            get { return 100; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] DamageBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant target = context.Target;
            if (target == null)
            {
                context.Log("伤害结算：没有可选目标，本次伤害落空。");
                return;
            }

            if (!target.IsAlive)
            {
                context.Log("伤害结算：目标 " + target.displayName + " 已阵亡，本次伤害落空。");
                return;
            }

            int baseDamage = ResolveBaseDamage(context);
            if (baseDamage <= 0)
            {
                context.Log("伤害结算：基础伤害为 0，跳过。");
                return;
            }

            float multiplier = ResolveMultiplier(context);

            // 命中判定：本行为只负责「玩家 → 怪物」的卡牌伤害，玩家命中率恒为 100%（规则一「命中率 100%」+
            // ARCHITECTURE 第 13 节第 8 条「玩家命中率恒 100%，不受任何 debuff 影响」），
            // 因此这里**不做命中判定、不消耗随机数**，保证牌打出即必定结算。
            // 注意：绝不能拿 target.GetHitRate() 当命中率——那是目标（怪物）的命中率，
            // 会把「B/BB 降低怪物命中」错误地变成「玩家打不中」，而且怪物被降命中后玩家反而更容易打空。
            // 怪物 → 玩家的命中判定属于 Monsters.MonsterAttackComponent 的职责，与本行为无关。
            int finalDamage = GameRules.ComputeCardDamage(baseDamage, multiplier, target.GetDefense());
            int applied = target.TakeDamage(finalDamage, DamageSource.Card);

            string buffNote = multiplier > 1f ? "（强化 x" + multiplier.ToString("0.00") + "）" : string.Empty;
            context.Log(context.User.displayName + " 对 " + target.displayName +
                        " 造成 " + applied + " 点伤害" + buffNote +
                        "，剩余 " + target.CurrentHp + "/" + target.MaxHp + "。");
        }

        public string Describe()
        {
            string damagePart = damageOverride > 0 ? damageOverride.ToString() : "definition.damage";
            string multPart = multiplierOverride > 0f ? "x" + multiplierOverride.ToString("0.00") : "本卡强化倍率";
            return "造成伤害 " + damagePart + " × " + multPart;
        }

        private int ResolveBaseDamage(CardPlayContext context)
        {
            if (damageOverride > 0)
            {
                return damageOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null)
            {
                return identity.definition.damage;
            }

            return 0;
        }

        private float ResolveMultiplier(CardPlayContext context)
        {
            if (multiplierOverride > 0f)
            {
                return multiplierOverride;
            }

            CardBuffComponent buff = context.Card != null ? context.Card.Buff : null;
            if (buff != null && buff.damageMultiplier > 0f)
            {
                return buff.damageMultiplier;
            }

            return 1f;
        }
    }
}
