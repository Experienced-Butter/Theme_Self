using CardGame.Cards;
using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Players
{
    /// <summary>
    /// 玩家出牌入口。
    /// 规则出处：
    ///   规则一 —— 每回合出牌 4 次（每次出牌消耗 1 次）。
    ///   规则二 / 规则三 —— 卡牌效果由 Cards/Behaviors 上的 ICardBehavior 结算（本组件只负责流程）。
    ///   规则六第 1 条 —— 大招抽到的临时卡在本回合结束时移除。
    ///   ARCHITECTURE 第 13 节第 3 条 —— 目标选择默认「第一个存活敌人」。
    /// 流程：校验出牌次数 → 选目标 → 构造 CardPlayContext → CardPlayComponent.Resolve
    ///       → 消耗 1 次出牌 → 卡牌进弃牌堆（zone = Discard）。
    /// </summary>
    public class PlayerPlayComponent : MonoBehaviour
    {
        /// <summary>目标选择策略；为空时使用默认 FirstAliveEnemyPolicy（第一个存活敌人，第 13 节第 3 条）。</summary>
        public ITargetPolicy targetPolicy;

        /// <summary>出牌者；为空时自动取同实体的 Combatant。</summary>
        public Combatant owner;

        private ITargetPolicy defaultPolicy;

        /// <summary>
        /// 打出一张手牌。成功返回 true。
        /// 失败情形（返回 false 且不消耗出牌次数）：battle/card 为空、出牌次数用尽、卡牌缺少 CardPlayComponent。
        /// </summary>
        public bool TryPlayCard(BattleContext battle, CardInstance card)
        {
            if (battle == null)
            {
                Debug.LogError("[CardGame] PlayerPlayComponent.TryPlayCard：battle 为 null");
                return false;
            }

            if (card == null)
            {
                Debug.LogWarning("[CardGame] PlayerPlayComponent.TryPlayCard：card 为 null");
                return false;
            }

            PlayerTurnComponent turn = GetComponent<PlayerTurnComponent>();
            if (turn == null)
            {
                Debug.LogError("[CardGame] PlayerPlayComponent.TryPlayCard：玩家实体缺少 PlayerTurnComponent");
                return false;
            }

            // 1) 校验出牌次数（规则一：每回合 4 次；合卡同样消耗 1 次，见 CardFusionComponent）
            if (!turn.CanPlay)
            {
                battle.Log("出牌次数已用尽（每回合 " + turn.playsPerTurn + " 次）");
                return false;
            }

            // 2) 卡牌必须能结算
            CardPlayComponent play = card.GetComponent<CardPlayComponent>();
            if (play == null)
            {
                battle.Log("卡牌缺少 CardPlayComponent，无法结算：" + card.name);
                return false;
            }

            Combatant user = ResolveOwner();
            if (user == null)
            {
                Debug.LogError("[CardGame] PlayerPlayComponent.TryPlayCard：找不到出牌者 Combatant");
                return false;
            }

            // 接线兜底：牌库洗牌/随机抽牌需要 IRandomSource（ARCHITECTURE 第 0 节第 7 条），
            // 流程未显式注入时用本次战斗的随机源补齐。
            PlayerDeckComponent deck = GetComponent<PlayerDeckComponent>();
            if (deck != null && deck.randomSource == null)
            {
                deck.randomSource = battle.Random;
            }

            // 3) 选目标（第 13 节第 3 条：默认第一个存活敌人；无存活敌人时可能为 null）
            ITargetPolicy policy = targetPolicy != null ? targetPolicy : ResolveDefaultPolicy();
            Combatant target = policy.SelectTarget(battle, card);

            // 4) 构造上下文并结算（Cards/Behaviors 按 ResolutionOrder 升序执行）
            CardPlayContext context = new CardPlayContext(battle, user, target, card, play);
            card.zone = CardZone.Resolving;
            play.Resolve(context);

            // 5) 消耗 1 次出牌
            if (!turn.TryConsumePlay())
            {
                battle.Log("出牌失败：出牌次数消耗校验未通过");
                card.zone = CardZone.Hand;
                return false;
            }

            // 6) 卡牌进弃牌堆（zone = Discard）
            PlayerHandComponent hand = GetComponent<PlayerHandComponent>();
            if (hand != null)
            {
                hand.Remove(card);
            }

            if (deck != null)
            {
                deck.Discard(card);
            }
            else
            {
                card.zone = CardZone.Discard;
            }

            battle.Log("打出「" + DescribeCard(card) + "」→ " +
                       (target != null ? target.displayName : "无目标") +
                       "（剩余出牌 " + turn.PlaysRemaining + " 次）");
            return true;
        }

        /// <summary>
        /// 回合结束：清理本回合未使用的大招临时卡，返回清理张数。
        /// 规则出处：规则六第 1 条 + ARCHITECTURE 第 13 节第 6 条（临时卡在本回合结束时移除）。
        /// </summary>
        public int RemoveTemporaryCards()
        {
            PlayerHandComponent hand = GetComponent<PlayerHandComponent>();
            if (hand == null)
            {
                return 0;
            }

            int before = hand.Count;
            hand.RemoveTemporaryCards();
            return before - hand.Count;
        }

        private ITargetPolicy ResolveDefaultPolicy()
        {
            if (defaultPolicy == null)
            {
                defaultPolicy = new FirstAliveEnemyPolicy();
            }

            return defaultPolicy;
        }

        private Combatant ResolveOwner()
        {
            if (owner != null)
            {
                return owner;
            }

            owner = GetComponent<Combatant>();
            return owner;
        }

        private static string DescribeCard(CardInstance card)
        {
            CardIdentityComponent identity = card.GetComponent<CardIdentityComponent>();
            if (identity != null && identity.definition != null && !string.IsNullOrEmpty(identity.definition.displayName))
            {
                return identity.definition.displayName;
            }

            CardPlayComponent play = card.GetComponent<CardPlayComponent>();
            if (play != null)
            {
                string described = play.Describe();
                if (!string.IsNullOrEmpty(described))
                {
                    return described;
                }
            }

            return card.name;
        }
    }
}
