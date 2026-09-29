using CardGame.Combat;

namespace CardGame.Core
{
    /// <summary>
    /// 一次出牌的上下文：战场、出牌者、目标、卡牌实体与结算组件。
    /// 由 PlayerPlayComponent 构造并交给 CardPlayComponent 逐条执行行为。
    /// </summary>
    public sealed class CardPlayContext
    {
        /// <summary>战场上下文。</summary>
        public BattleContext Battle { get; private set; }

        /// <summary>出牌者。</summary>
        public Combatant User { get; private set; }

        /// <summary>目标选择策略选出的敌人（可能为 null，例如无存活敌人）。</summary>
        public Combatant Target { get; private set; }

        /// <summary>本次打出的卡牌实体。</summary>
        public Cards.CardInstance Card { get; private set; }

        /// <summary>卡牌上的结算组件。</summary>
        public Cards.CardPlayComponent Play { get; private set; }

        public CardPlayContext(BattleContext battle, Combatant user, Combatant target,
                               Cards.CardInstance card, Cards.CardPlayComponent play)
        {
            if (battle == null)
            {
                throw new System.ArgumentNullException("battle");
            }
            if (user == null)
            {
                throw new System.ArgumentNullException("user");
            }
            Battle = battle;
            User = user;
            Target = target;
            Card = card;
            Play = play;
        }

        /// <summary>转发到战场日志。卡牌名称由调用方（行为组件）自行拼入消息。</summary>
        public void Log(string message)
        {
            Battle.Log(message);
        }
    }
}
