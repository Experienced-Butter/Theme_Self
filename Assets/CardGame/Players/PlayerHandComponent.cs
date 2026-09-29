using System.Collections.Generic;
using CardGame.Cards;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Players
{
    /// <summary>
    /// 玩家手牌容器。
    /// 规则出处：
    ///   规则一 / 规则六第 4 条 —— 初始手牌 5 张、每回合抽 4 张、手牌上限 6 张（手牌达到上限时无法继续抽牌）。
    ///   ARCHITECTURE 第 13 节第 6 条 —— 大招临时卡在本回合结束时从未使用的手牌中移除。
    /// 本组件只管手牌集合本身；抽牌与洗牌由 PlayerDeckComponent 负责。
    /// </summary>
    public class PlayerHandComponent : MonoBehaviour
    {
        /// <summary>手牌上限（规则一：6 张）。默认值取自 GameRules.HandLimit，与契约中的 6 等价。</summary>
        public int handLimit = GameRules.HandLimit;

        private readonly List<CardInstance> cards = new List<CardInstance>();

        /// <summary>当前手牌（只读视图，顺序为入手顺序）。</summary>
        public IReadOnlyList<CardInstance> Cards
        {
            get { return cards; }
        }

        /// <summary>手牌张数。</summary>
        public int Count
        {
            get { return cards.Count; }
        }

        /// <summary>手牌是否已达上限（规则六第 4 条：手牌达到上限时无法继续抽牌）。</summary>
        public bool IsFull
        {
            get { return cards.Count >= handLimit; }
        }

        /// <summary>
        /// 尝试把一张卡加入手牌，成功返回 true。
        /// 卡牌为空、已在手牌中、或手牌已达上限（规则一：6 张）时返回 false。
        /// </summary>
        public bool TryAdd(CardInstance card)
        {
            if (card == null)
            {
                Debug.LogWarning("[CardGame] PlayerHandComponent.TryAdd：card 为 null");
                return false;
            }

            if (IsFull)
            {
                // 规则六第 4 条：手牌上限 6 张，达到上限时无法继续抽牌。
                Debug.LogWarning("[CardGame] PlayerHandComponent.TryAdd：手牌已达上限 " + handLimit + " 张，加入失败");
                return false;
            }

            if (IndexOf(card) >= 0)
            {
                return false;   // 同一张卡实体不应同时存在于手牌两次
            }

            cards.Add(card);
            card.zone = CardZone.Hand;
            return true;
        }

        /// <summary>
        /// 从手牌移除一张卡，返回是否确实在手牌中。
        /// 不改写 zone：由调用方决定去向（打出 → Discard，清理 → Removed）。
        /// </summary>
        public bool Remove(CardInstance card)
        {
            int index = IndexOf(card);
            if (index < 0)
            {
                return false;
            }

            cards.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// 移除本回合结束时仍未使用的大招临时卡。
        /// 规则出处：规则六第 1 条（大招抽到的临时卡）+ ARCHITECTURE 第 13 节第 6 条（临时卡生命周期 = 本回合结束）。
        /// </summary>
        public void RemoveTemporaryCards()
        {
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                CardInstance card = cards[i];
                if (card != null && !card.isTemporary)
                {
                    continue;   // 非临时卡保留
                }

                cards.RemoveAt(i);
                if (card != null)
                {
                    Retire(card);
                }
            }
        }

        /// <summary>
        /// 查找手牌中第一张指定种类（A/B/C）的卡，找不到返回 null。
        /// 规则出处：ARCHITECTURE 第 13 节第 7 条 —— 复制器默认复制手牌中第一张 A/B/C。
        /// </summary>
        public CardInstance FindFirst(CardKind kind)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                if (card == null)
                {
                    continue;
                }

                CardIdentityComponent identity = card.GetComponent<CardIdentityComponent>();
                if (identity != null && identity.Kind == kind)
                {
                    return card;
                }
            }

            return null;
        }

        /// <summary>
        /// 清空手牌（供 PlayerInitializer 在克隆体初始化时调用）。
        /// 被清掉的卡标记为 Removed 并停用，避免残留实体被再次抽出。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null)
                {
                    Retire(cards[i]);
                }
            }

            cards.Clear();
        }

        private int IndexOf(CardInstance card)
        {
            if (card == null)
            {
                return -1;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                // 用 Unity 的 == 重载比较，避免已销毁对象被误判为相等。
                if (cards[i] != null && cards[i] == card)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void Retire(CardInstance card)
        {
            card.zone = CardZone.Removed;
            card.gameObject.SetActive(false);
        }
    }
}
