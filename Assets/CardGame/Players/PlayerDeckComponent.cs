using System.Collections.Generic;
using CardGame.Cards;
using CardGame.Combat;
using CardGame.Controllers;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Players
{
    /// <summary>
    /// 玩家牌库与弃牌堆。
    /// 规则出处：
    ///   规则一 —— 牌库构成 A×5、B×5、C×5（共 15 张）；初始手牌 5 张；每回合抽 4 张；手牌上限 6 张。
    ///   规则六第 4 条 —— 开局抽 5 张（确保 ABC 各至少 1 张）、每回合结束后抽 4 张、
    ///                     手牌达到上限时无法继续抽牌、后续抽取为随机抽取、
    ///                     牌库为空时弃牌堆洗牌后重新作为牌库使用。
    /// 所有随机数走 Core.IRandomSource（ARCHITECTURE 第 0 节第 7 条），不使用 UnityEngine.Random。
    /// </summary>
    public class PlayerDeckComponent : MonoBehaviour
    {
        /// <summary>开局手牌数（规则一：5 张）。默认值取自 GameRules.OpeningHand。</summary>
        public int openingHand = GameRules.OpeningHand;

        /// <summary>每回合抽牌数（规则一：4 张）。默认值取自 GameRules.DrawPerTurn。</summary>
        public int drawPerTurn = GameRules.DrawPerTurn;

        // ---- 接线依赖：由 Bootstrap / Controller 注入（与 ARCHITECTURE 第 6.2 节 CardFusionComponent.library 的注入方式一致）----
        /// <summary>卡牌库：克隆卡牌实体的唯一入口（保证牌库是独立实体而非共享引用）。</summary>
        public CardLibraryComponent library;

        /// <summary>卡牌归属的战斗单位（玩家 Combatant）。为空时自动取同实体的 Combatant。</summary>
        public Combatant owner;

        /// <summary>收牌的手牌组件。为空时自动取同实体的 PlayerHandComponent。</summary>
        public PlayerHandComponent hand;

        /// <summary>随机数来源（洗牌与随机抽取）。为空时回退到 GameController.Instance.Random。</summary>
        public IRandomSource randomSource;

        private static readonly CardKind[] BasicKinds = new CardKind[] { CardKind.A, CardKind.B, CardKind.C };

        private readonly List<CardInstance> deck = new List<CardInstance>();
        private readonly List<CardInstance> discard = new List<CardInstance>();

        /// <summary>牌库内容（只读视图，顺序即洗牌后的抽取顺序）。</summary>
        public IReadOnlyList<CardInstance> Deck
        {
            get { return deck; }
        }

        /// <summary>弃牌堆内容（只读视图）。</summary>
        public IReadOnlyList<CardInstance> DiscardPile
        {
            get { return discard; }
        }

        /// <summary>牌库剩余张数。</summary>
        public int DeckCount
        {
            get { return deck.Count; }
        }

        /// <summary>弃牌堆张数。</summary>
        public int DiscardCount
        {
            get { return discard.Count; }
        }

        /// <summary>
        /// 构建牌库：A×5 + B×5 + C×5 = 15 张（规则一 / 规则六第 4 条）。
        /// 每张卡都通过 CardLibraryComponent 从模板克隆得到独立实体（不是共享引用），构建完成后洗牌。
        /// </summary>
        public void BuildFromLibrary()
        {
            ClearPiles();

            CardLibraryComponent lib = ResolveLibrary();
            if (lib == null)
            {
                Debug.LogError("[CardGame] PlayerDeckComponent.BuildFromLibrary：缺少 CardLibraryComponent，请由 Bootstrap/Controller 注入 library");
                return;
            }

            Combatant cardOwner = ResolveOwner();

            for (int k = 0; k < BasicKinds.Length; k++)
            {
                for (int copy = 0; copy < GameRules.DeckCopiesPerBasic; copy++)
                {
                    // parent 传 null：挂载点交给 CardLibraryComponent 决定（其默认是卡牌库/控制者实体）。
                    // 不传本组件所在玩家的 transform——玩家克隆体在同一帧内可能仍带模板组件，
                    // 会被 CardLibraryComponent 判为「挂在模板之下」并告警回退。
                    CardInstance card = lib.CreateCard(BasicKinds[k], cardOwner, null);
                    if (card == null)
                    {
                        Debug.LogError("[CardGame] PlayerDeckComponent.BuildFromLibrary：克隆 " + BasicKinds[k] + " 卡牌失败");
                        continue;
                    }

                    card.owner = cardOwner;
                    card.zone = CardZone.Deck;
                    deck.Add(card);
                }
            }

            ShuffleDeck();

            Debug.Log("[CardGame] 牌库构建完成：共 " + deck.Count + " 张（A×" + GameRules.DeckCopiesPerBasic +
                      "、B×" + GameRules.DeckCopiesPerBasic + "、C×" + GameRules.DeckCopiesPerBasic + "）");
        }

        /// <summary>
        /// 开局抽牌（规则六第 4 条）：抽 openingHand 张，并保证 A/B/C 各至少 1 张。
        /// 实现为「先各保底发 1 张，再从剩余牌中随机补足」。
        /// </summary>
        public void OpeningDraw()
        {
            PlayerHandComponent target = ResolveHand();
            if (target == null)
            {
                Debug.LogError("[CardGame] PlayerDeckComponent.OpeningDraw：找不到 PlayerHandComponent");
                return;
            }

            int total = Mathf.Max(0, openingHand);
            int guaranteed = Mathf.Min(BasicKinds.Length, total);
            int drawn = 0;

            for (int i = 0; i < guaranteed; i++)
            {
                if (target.IsFull)
                {
                    break;   // 手牌上限 6 张（规则一）
                }

                CardInstance card = TakeFirstOfKind(BasicKinds[i]);
                if (card == null)
                {
                    Debug.LogWarning("[CardGame] PlayerDeckComponent.OpeningDraw：牌库中没有 " + BasicKinds[i] + "，无法保底发牌");
                    continue;
                }

                if (!target.TryAdd(card))
                {
                    card.zone = CardZone.Deck;   // 收牌失败：放回牌库
                    deck.Add(card);
                    break;
                }

                drawn++;
            }

            if (drawn < total)
            {
                drawn += DrawCards(total - drawn);   // 剩余张数随机补足
            }

            Debug.Log("[CardGame] 开局抽牌：" + drawn + " 张（已保证 A/B/C 各至少 1 张），牌库剩余 " + deck.Count + " 张");
        }

        /// <summary>
        /// 从牌库抽 count 张（规则六第 4 条：随机抽取；牌库为空时弃牌堆洗牌后重新作为牌库）。
        /// 受手牌上限 6 张限制，达到上限即停止，返回实际抽到的张数。
        /// </summary>
        public int DrawCards(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            PlayerHandComponent target = ResolveHand();
            if (target == null)
            {
                Debug.LogError("[CardGame] PlayerDeckComponent.DrawCards：找不到 PlayerHandComponent");
                return 0;
            }

            int drawn = 0;

            for (int i = 0; i < count; i++)
            {
                if (target.IsFull)
                {
                    break;   // 规则一 / 规则六第 4 条：手牌达到上限（6 张）时无法继续抽牌
                }

                if (deck.Count == 0 && !RecycleDiscard())
                {
                    Debug.Log("[CardGame] 牌库与弃牌堆均已空，停止抽牌");
                    break;
                }

                int index = PickIndex();
                CardInstance card = deck[index];
                deck.RemoveAt(index);

                if (!target.TryAdd(card))
                {
                    deck.Insert(index, card);   // 收牌失败：放回原位置
                    break;
                }

                drawn++;
            }

            return drawn;
        }

        /// <summary>把一张卡放进弃牌堆（规则六第 4 条：牌库为空时弃牌堆会洗回牌库）。</summary>
        public void Discard(CardInstance card)
        {
            if (card == null)
            {
                return;
            }

            if (IndexOfDiscard(card) < 0)
            {
                discard.Add(card);
            }

            card.zone = CardZone.Discard;
        }

        /// <summary>洗牌（规则六第 4 条：后续抽取为随机抽取）。随机数走 Core.IRandomSource。</summary>
        public void ShuffleDeck()
        {
            IRandomSource random = ResolveRandom();
            if (random == null)
            {
                Debug.LogWarning("[CardGame] PlayerDeckComponent.ShuffleDeck：没有可用的 IRandomSource，牌库顺序未打乱");
                return;
            }

            random.Shuffle(deck);
        }

        /// <summary>
        /// 清空牌库与弃牌堆（供 PlayerInitializer 克隆初始化使用）。
        /// 被清掉的卡标记为 Removed 并停用，避免残留实体被再次抽出。
        /// </summary>
        public void ClearPiles()
        {
            RetireAll(deck);
            RetireAll(discard);
            deck.Clear();
            discard.Clear();
        }

        /// <summary>牌库为空时，把弃牌堆洗牌后重新作为牌库（规则六第 4 条）。返回是否拿到了可抽的牌。</summary>
        private bool RecycleDiscard()
        {
            if (discard.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < discard.Count; i++)
            {
                CardInstance card = discard[i];
                if (card == null)
                {
                    continue;
                }

                card.zone = CardZone.Deck;
                deck.Add(card);
            }

            discard.Clear();
            ShuffleDeck();

            Debug.Log("[CardGame] 牌库已空：弃牌堆 " + deck.Count + " 张洗牌后重新作为牌库");
            return deck.Count > 0;
        }

        private CardInstance TakeFirstOfKind(CardKind kind)
        {
            for (int i = 0; i < deck.Count; i++)
            {
                CardInstance card = deck[i];
                if (card == null)
                {
                    continue;
                }

                CardIdentityComponent identity = card.GetComponent<CardIdentityComponent>();
                if (identity == null || identity.Kind != kind)
                {
                    continue;
                }

                deck.RemoveAt(i);
                return card;
            }

            return null;
        }

        private int PickIndex()
        {
            IRandomSource random = ResolveRandom();
            if (random == null)
            {
                return deck.Count - 1;
            }

            return random.Range(0, deck.Count);
        }

        private int IndexOfDiscard(CardInstance card)
        {
            for (int i = 0; i < discard.Count; i++)
            {
                if (discard[i] != null && discard[i] == card)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RetireAll(List<CardInstance> cards)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                if (card == null)
                {
                    continue;
                }

                card.zone = CardZone.Removed;
                card.gameObject.SetActive(false);
            }
        }

        private CardLibraryComponent ResolveLibrary()
        {
            if (library != null)
            {
                return library;
            }

            library = GetComponent<CardLibraryComponent>();
            if (library == null)
            {
                library = GetComponentInParent<CardLibraryComponent>();
            }

            return library;
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

        private PlayerHandComponent ResolveHand()
        {
            if (hand != null)
            {
                return hand;
            }

            hand = GetComponent<PlayerHandComponent>();
            if (hand == null)
            {
                hand = GetComponentInParent<PlayerHandComponent>();
            }

            return hand;
        }

        private IRandomSource ResolveRandom()
        {
            if (randomSource != null)
            {
                return randomSource;
            }

            // 兜底：全局随机数唯一来源（Controllers/RandomComponent，经 GameController 暴露）。
            GameController controller = GameController.Instance;
            if (controller != null && controller.Random != null)
            {
                return controller.Random;
            }

            return null;
        }
    }
}
