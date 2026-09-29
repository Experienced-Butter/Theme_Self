using CardGame.Combat;
using CardGame.Core;
using CardGame.Players;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 卡牌复制器行为：复制手牌中的第一张 A/B/C，并给玩家一次免费合成机会（FreeFusion）。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `DuplicateBehavior`；规则原文第六节第 1 条
    /// 「大招效果：抽三张临时卡牌（ABC 随机）与一个卡牌复制器（仅复制 ABC，获得一次合成次数）」。
    /// ARCHITECTURE 第 13 节第 7 条：默认复制「手牌中第一张 A/B/C」；无合法目标时既不复制也不给 FreeFusion。
    /// </summary>
    public class DuplicateBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>为 true 时只复制 sourceKind 指定的那一类卡（Ultimate 生成的复制器可带参使用）。</summary>
        public bool restrictToKind;

        /// <summary>restrictToKind 为 true 时要复制的卡类（限 A/B/C）。</summary>
        public CardKind sourceKind = CardKind.A;

        /// <summary>为 true 时在复制成功的同时给玩家一次 FreeFusion（默认行为，规则要求）。</summary>
        public bool grantFreeFusionOnSuccess = true;

        /// <summary>复制与授予免费合成是本卡的收尾效果，排在手牌强化之后、大招之前。</summary>
        public int ResolutionOrder
        {
            get { return 1100; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] DuplicateBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant user = context.User;
            if (user == null)
            {
                Debug.LogError("[CardGame] DuplicateBehavior.Resolve 找不到出牌者。");
                return;
            }

            PlayerHandComponent hand = user.GetComponent<PlayerHandComponent>();
            if (hand == null)
            {
                Debug.LogError("[CardGame] DuplicateBehavior.Resolve 出牌者身上没有 PlayerHandComponent，无法复制手牌。");
                return;
            }

            CardInstance source = FindFirstCopyable(hand);
            if (source == null)
            {
                // ARCHITECTURE 第 13 节第 7 条：没有合法的 A/B/C 目标时，既不复制也不给 FreeFusion。
                context.Log("复制器结算：手牌中没有可复制的 A/B/C，本次既不复制也不给予免费合成。");
                return;
            }

            CardInstance copy = CreateCopy(context, source);
            if (copy == null)
            {
                context.Log("复制器结算：复制失败（缺少 CardLibraryComponent 且无法兜底克隆），未给予免费合成。");
                return;
            }

            copy.owner = user;
            copy.zone = CardZone.Hand;

            if (hand.IsFull)
            {
                context.Log("复制器结算：手牌已满，无法收纳复制出的 " + copy.DisplayName + "，未给予免费合成。");
                return;
            }

            if (!hand.TryAdd(copy))
            {
                context.Log("复制器结算：复制出的 " + copy.DisplayName + " 未能进入手牌，未给予免费合成。");
                return;
            }

            if (grantFreeFusionOnSuccess)
            {
                PlayerStatusComponent statuses = user.GetComponent<PlayerStatusComponent>();
                if (statuses != null)
                {
                    statuses.GrantFreeFusion();
                }
                else
                {
                    Debug.LogError("[CardGame] DuplicateBehavior.Resolve 出牌者没有 PlayerStatusComponent，无法给予 FreeFusion。");
                }
            }

            context.Log("复制器：复制了手牌中的 " + source.DisplayName + "，并给予一次免费合成机会。");
        }

        public string Describe()
        {
            string target = restrictToKind ? sourceKind.ToString() : "手牌第一张 A/B/C";
            return "复制 " + target + " 并给予一次免费合成";
        }

        // 手牌中第一张 A/B/C（按手牌顺序）。复制器/大招卡自身不是合法目标。
        private CardInstance FindFirstCopyable(PlayerHandComponent hand)
        {
            System.Collections.Generic.IReadOnlyList<CardInstance> cards = hand.Cards;
            if (cards == null)
            {
                return null;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                if (card == null)
                {
                    continue;
                }

                CardIdentityComponent identity = card.Identity;
                if (identity == null || identity.definition == null)
                {
                    continue;
                }

                CardKind kind = identity.definition.kind;
                if (kind != CardKind.A && kind != CardKind.B && kind != CardKind.C)
                {
                    continue;
                }

                if (restrictToKind && kind != sourceKind)
                {
                    continue;
                }

                return card;
            }

            return null;
        }

        private static CardInstance CreateCopy(CardPlayContext context, CardInstance source)
        {
            // 优先走 CardLibraryComponent 的模板克隆（保证与牌库出的卡同源）。
            Controllers.CardLibraryComponent library = ResolveLibrary(context);
            if (library != null)
            {
                CardInstance created = library.CreateCard(source.Kind, source.owner, source.transform.parent);
                if (created != null)
                {
                    return created;
                }
            }

            // 兜底：直接克隆原卡实体（Instantiate 深拷贝），再由 InitializeFrom 写入定义副本。
            CardInstance clone = Instantiate(source, source.transform.parent);
            clone.name = source.name + "_Copy";
            CardIdentityComponent sourceIdentity = source.Identity;
            if (sourceIdentity != null && sourceIdentity.definition != null)
            {
                clone.InitializeFrom(sourceIdentity.definition);
            }

            clone.isTemporary = source.isTemporary;
            return clone;
        }

        private static Controllers.CardLibraryComponent ResolveLibrary(CardPlayContext context)
        {
            CardFusionComponent fusion = context.User != null ? context.User.GetComponent<CardFusionComponent>() : null;
            if (fusion != null && fusion.library != null)
            {
                return fusion.library;
            }

            if (context.Battle != null && context.Battle.Controller != null)
            {
                return context.Battle.Controller.GetComponent<Controllers.CardLibraryComponent>();
            }

            return null;
        }
    }
}
