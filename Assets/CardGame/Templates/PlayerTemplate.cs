using UnityEngine;

namespace CardGame.Templates
{
    /// <summary>
    /// 玩家模板实体。序列化字段默认值即规则一「玩家基础参数」的取值。
    /// </summary>
    public class PlayerTemplate : EntityTemplate
    {
        // 规则：玩家生命值 2500。
        public int maxHp = Core.GameRules.PlayerMaxHp;

        // 规则：玩家防御力 300。
        public int defense = Core.GameRules.PlayerDefense;

        // 规则：玩家命中率 100%。
        public float hitRate = Core.GameRules.PlayerHitRate;

        // 规则：每回合出牌次数 4 次。
        public int playsPerTurn = Core.GameRules.PlaysPerTurn;

        // 规则：初始手牌 5 张。
        public int openingHand = Core.GameRules.OpeningHand;

        // 规则：每回合抽牌 4 张。
        public int drawPerTurn = Core.GameRules.DrawPerTurn;

        // 规则：手牌上限 6 张。
        public int handLimit = Core.GameRules.HandLimit;

        /// <summary>
        /// 把模板参数写入克隆体自己的组件，并调用玩家初始化器重置运行时状态。
        /// </summary>
        protected override void OnAfterClone(GameObject clone)
        {
            if (clone == null)
            {
                return;
            }

            Combat.HealthComponent health = GetShared<Combat.HealthComponent>(clone);
            if (health != null)
            {
                health.Initialize(maxHp);
            }

            Combat.DefenseComponent defenseComponent = GetShared<Combat.DefenseComponent>(clone);
            if (defenseComponent != null)
            {
                defenseComponent.baseDefense = defense;
            }

            Combat.HitRateComponent hitRateComponent = GetShared<Combat.HitRateComponent>(clone);
            if (hitRateComponent != null)
            {
                hitRateComponent.baseHitRate = hitRate;
            }

            Combat.Combatant combatant = GetShared<Combat.Combatant>(clone);
            if (combatant != null)
            {
                combatant.team = Core.Team.Player;
                combatant.displayName = "玩家";
            }

            Players.PlayerTurnComponent turn = GetShared<Players.PlayerTurnComponent>(clone);
            if (turn != null)
            {
                turn.playsPerTurn = playsPerTurn;
            }

            Players.PlayerHandComponent hand = GetShared<Players.PlayerHandComponent>(clone);
            if (hand != null)
            {
                hand.handLimit = handLimit;
            }

            Players.PlayerDeckComponent deck = GetShared<Players.PlayerDeckComponent>(clone);
            if (deck != null)
            {
                deck.openingHand = openingHand;
                deck.drawPerTurn = drawPerTurn;
            }

            // 由 player 负责人提供：重置回合计数、清空手牌/牌库标记，保证克隆体是干净的初始玩家。
            Players.PlayerInitializer.Initialize(clone);
        }

        /// <summary>
        /// 优先取克隆体自身的组件；若挂在子节点上则向下查找，避免克隆体因层级差异漏配参数。
        /// </summary>
        private static T GetShared<T>(GameObject clone) where T : Component
        {
            T component = clone.GetComponent<T>();
            if (component == null)
            {
                component = clone.GetComponentInChildren<T>(true);
            }
            return component;
        }
    }
}
