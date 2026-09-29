namespace CardGame.Combat
{
    /// <summary>
    /// 伤害计算薄封装：统一的取整与下限规则都在 Core.GameRules 内，本类只做参数装配。
    /// 规则：玩家→怪物 = floor(基础伤害 × 强化倍率) - 怪物防御；怪物→玩家 = 攻击力 - 玩家防御。
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>玩家→怪物伤害。</summary>
        public static int CardDamage(int baseDamage, float multiplier, Combatant target)
        {
            int defense = target == null ? 0 : target.GetDefense();
            return Core.GameRules.ComputeCardDamage(baseDamage, multiplier, defense);
        }

        /// <summary>怪物→玩家伤害。</summary>
        public static int MonsterDamage(int attack, Combatant target)
        {
            int defense = target == null ? 0 : target.GetDefense();
            return Core.GameRules.ComputeMonsterDamage(attack, defense);
        }
    }
}
