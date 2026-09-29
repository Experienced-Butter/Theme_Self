using CardGame.Combat;

namespace CardGame.Core
{
    /// <summary>
    /// 出牌目标选择策略。伤害与 debuff 都作用于该策略选出的目标。
    /// </summary>
    public interface ITargetPolicy
    {
        /// <summary>为本次出牌选出目标；无合法目标时返回 null。</summary>
        Combatant SelectTarget(BattleContext battle, Cards.CardInstance card);
    }

    /// <summary>
    /// 默认策略：第一个存活敌人（ARCHITECTURE 13.3）。
    /// </summary>
    public sealed class FirstAliveEnemyPolicy : ITargetPolicy
    {
        /// <inheritdoc />
        public Combatant SelectTarget(BattleContext battle, Cards.CardInstance card)
        {
            if (battle == null)
            {
                return null;
            }
            System.Collections.Generic.List<Combatant> alive = battle.AliveEnemies();
            if (alive.Count == 0)
            {
                return null;
            }
            return alive[0];
        }
    }
}
