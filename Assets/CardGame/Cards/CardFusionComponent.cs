using System.Collections.Generic;
using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards
{
    /// <summary>
    /// 卡牌融合组件（**挂在玩家实体上**，不是挂在卡牌上）。
    /// 规则出处：ARCHITECTURE.md 第 6.2 节 `Cards/CardFusionComponent.cs`；规则原文第三节
    /// 「AA=A+A, BB=B+B, CC=C+C, AB=A+B, AC=A+C, BC=B+C」；第六节第 1 条
    /// 「充能获取：只有合卡（合成卡牌）时才获得 1 点充能；合卡消耗：合成操作消耗 1 次出牌次数」。
    /// 合成流程：校验配方 → 消耗两张 → 通过 CardLibraryComponent 克隆出产物实体 → 充能 +1 → 消耗 1 次出牌 → 返回产物。
    /// </summary>
    public class CardFusionComponent : MonoBehaviour
    {
        /// <summary>牌库组件，由 Bootstrap 注入，用于克隆出产物卡牌实体。</summary>
        public Controllers.CardLibraryComponent library;

        /// <summary>合成成功回调：两张材料 + 产物。</summary>
        public System.Action<CardInstance, CardInstance, CardInstance> OnFused;

        // 六种配方（无序匹配）：A+A→AA, B+B→BB, C+C→CC, A+B→AB, A+C→AC, B+C→BC。
        // 表在类型初始化时构建，避免每次查询重复装配。
        private static readonly CardKind[][] RecipeTable = BuildRecipeTable();

        /// <summary>配方产物的充能增量：只有合卡才获得充能（规则第六节第 1 条）。</summary>
        public const int ChargePerFusion = 1;

        /// <summary>合成消耗的出牌次数（规则第六节第 1 条）。</summary>
        public const int PlaysPerFusion = 1;

        /// <summary>
        /// 无序匹配配方：任意顺序的两张基础卡 → 合成卡；没有匹配返回 false。
        /// </summary>
        public static bool TryGetRecipe(CardKind a, CardKind b, out CardKind result)
        {
            for (int i = 0; i < RecipeTable.Length; i++)
            {
                CardKind[] recipe = RecipeTable[i];
                if (recipe.Length != 3)
                {
                    continue;
                }

                bool forward = recipe[0] == a && recipe[1] == b;
                bool backward = recipe[0] == b && recipe[1] == a;
                if (forward || backward)
                {
                    result = recipe[2];
                    return true;
                }
            }

            result = CardKind.A;
            return false;
        }

        /// <summary>
        /// 是否可合成：两张卡都非空、不是同一张、都是基础卡 A/B/C、配方有效、且没有消耗两张卡之后无法完成的出牌次数
        /// （持有 FreeFusion 时免费合成，不占出牌次数）。
        /// </summary>
        public bool CanFuse(CardInstance a, CardInstance b)
        {
            CardKind result;
            return CanFuseInternal(a, b, out result);
        }

        /// <summary>
        /// 执行合成。失败（配方不存在 / 材料非法 / 无出牌次数）时返回 null 且不产生任何副作用。
        /// 成功时：材料离场 → 生成产物 → 充能 +1 → 消耗 1 次出牌（有 FreeFusion 则改用免费合成）→ 触发 OnFused。
        /// </summary>
        public CardInstance Fuse(Combatant owner, CardInstance a, CardInstance b)
        {
            CardKind recipeResult;
            if (!CanFuseInternal(a, b, out recipeResult))
            {
                Debug.LogError("[CardGame] CardFusionComponent.Fuse：合成条件不满足（材料非法 / 没有配方 / 出牌次数不足），本次合成取消。");
                return null;
            }

            if (owner == null)
            {
                Debug.LogError("[CardGame] CardFusionComponent.Fuse：owner 为 null，无法合成。");
                return null;
            }

            PlayerFusionDependencies deps;
            if (!TryResolveDependencies(owner, out deps))
            {
                return null;
            }

            bool useFreeFusion = deps.Statuses != null && deps.Statuses.HasFreeFusion;
            if (!useFreeFusion && deps.Turn != null && !deps.Turn.CanPlay)
            {
                Debug.LogError("[CardGame] CardFusionComponent.Fuse：本回合已无出牌次数，合成取消（合成消耗 1 次出牌）。");
                return null;
            }

            // 1) 消耗两张材料（先离场，为产物腾出空间）。
            bool firstRemoved = TryRemove(a, deps);
            bool secondRemoved = TryRemove(b, deps);
            if (!firstRemoved || !secondRemoved)
            {
                // 原子性保证：任一张没能离场就把已离场的那张放回手牌，避免材料凭空消失。
                // （CanFuseInternal 已确保两张都在手牌，正常路径不会走到这里，这里是防御性兜底。）
                if (firstRemoved)
                {
                    Restore(a, deps);
                }

                if (secondRemoved)
                {
                    Restore(b, deps);
                }

                Debug.LogError("[CardGame] CardFusionComponent.Fuse：材料未能全部离场，合成取消并已回滚。");
                return null;
            }

            // 2) 克隆出产物实体。
            CardInstance product = CreateProduct(owner, recipeResult, deps, a);
            if (product == null)
            {
                Debug.LogError("[CardGame] CardFusionComponent.Fuse：产物 " + recipeResult + " 生成失败，两张材料已消耗。");
                return null;
            }

            product.owner = owner;
            product.zone = CardZone.Hand;
            if (deps.Hand != null && !deps.Hand.TryAdd(product))
            {
                Debug.LogError("[CardGame] CardFusionComponent.Fuse：产物未能进入手牌。");
            }

            // 3) 充能 +1（只有合卡获得充能；达到阈值时由 player/flow 侧发放大招卡）。
            if (deps.Charge != null)
            {
                // 契约签名是 int AddFromFusion()：返回 1 表示本次正好累计到阈值 4 点、大招就绪；
                // 但 PlayerChargeComponent.IsReady 才是权威判定，因此两者都检查，避免契约语义漂移导致漏发大招。
                int fusionResult = deps.Charge.AddFromFusion();
                if (fusionResult == 1 || deps.Charge.IsReady)
                {
                    Debug.Log("[CardGame] 充能达到 " + deps.Charge.Threshold + " 点，大招卡已就绪（由 player/flow 侧发放进手牌）。");
                }
            }
            else
            {
                Debug.LogError("[CardGame] CardFusionComponent.Fuse：玩家缺少 PlayerChargeComponent，充能未增加。");
            }

            // 4) 消耗 1 次出牌；持有 FreeFusion 时改为消耗免费合成次数，不占出牌次数。
            if (useFreeFusion)
            {
                deps.Statuses.ConsumeFreeFusion();
                Debug.Log("[CardGame] 本次合成使用免费合成次数（FreeFusion），未消耗出牌次数。");
            }
            else if (deps.Turn != null)
            {
                if (!deps.Turn.TryConsumePlay())
                {
                    Debug.LogError("[CardGame] 合成未能扣掉出牌次数（TryConsumePlay 返回 false）。");
                }
            }

            if (OnFused != null)
            {
                OnFused(a, b, product);
            }

            return product;
        }

        // —— 内部实现 ——

        // 配方装配：把「两张材料 → 产物」的六条规则集中在一处，避免散落的 if 链。
        private static CardKind[][] BuildRecipeTable()
        {
            return new CardKind[][]
            {
                new[] { CardKind.A, CardKind.A, CardKind.AA },
                new[] { CardKind.B, CardKind.B, CardKind.BB },
                new[] { CardKind.C, CardKind.C, CardKind.CC },
                new[] { CardKind.A, CardKind.B, CardKind.AB },
                new[] { CardKind.A, CardKind.C, CardKind.AC },
                new[] { CardKind.B, CardKind.C, CardKind.BC }
            };
        }

        private static bool IsBasicMaterial(CardKind kind)
        {
            return kind == CardKind.A || kind == CardKind.B || kind == CardKind.C;
        }

        private bool CanFuseInternal(CardInstance a, CardInstance b, out CardKind result)
        {
            result = CardKind.A;
            if (a == null || b == null)
            {
                return false;
            }

            if (ReferenceEquals(a, b))
            {
                return false;
            }

            if (!IsBasicMaterial(a.Kind) || !IsBasicMaterial(b.Kind))
            {
                return false;
            }

            if (!TryGetRecipe(a.Kind, b.Kind, out result))
            {
                return false;
            }

            // 材料必须都在手牌中。
            Players.PlayerHandComponent hand = a.owner != null ? a.owner.GetComponent<Players.PlayerHandComponent>() : null;
            if (hand == null || !Contains(hand, a) || !Contains(hand, b))
            {
                return false;
            }

            // 出牌次数校验：免费合成不占次数。
            Players.PlayerStatusComponent statuses = a.owner.GetComponent<Players.PlayerStatusComponent>();
            if (statuses != null && statuses.HasFreeFusion)
            {
                return true;
            }

            Players.PlayerTurnComponent turn = a.owner.GetComponent<Players.PlayerTurnComponent>();
            if (turn != null && !turn.CanPlay)
            {
                return false;
            }

            return true;
        }

        private static bool Contains(Players.PlayerHandComponent hand, CardInstance card)
        {
            IReadOnlyList<CardInstance> cards = hand.Cards;
            if (cards == null)
            {
                return false;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                if (ReferenceEquals(cards[i], card))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryRemove(CardInstance card, PlayerFusionDependencies deps)
        {
            if (deps.Hand != null && deps.Hand.Remove(card))
            {
                card.zone = CardZone.Removed;
                return true;
            }

            // 兜底：材料可能挂在别的玩家实体上，按其 owner 各自移除。
            Players.PlayerHandComponent ownerHand = card.owner != null ? card.owner.GetComponent<Players.PlayerHandComponent>() : null;
            if (ownerHand != null && ownerHand.Remove(card))
            {
                card.zone = CardZone.Removed;
                return true;
            }

            return false;
        }

        // 回滚：把已离场的材料放回手牌，恢复其区域标记。
        private static void Restore(CardInstance card, PlayerFusionDependencies deps)
        {
            if (card == null)
            {
                return;
            }

            Players.PlayerHandComponent hand = deps.Hand;
            if (hand == null && card.owner != null)
            {
                hand = card.owner.GetComponent<Players.PlayerHandComponent>();
            }

            if (hand != null && hand.TryAdd(card))
            {
                card.zone = CardZone.Hand;
            }
        }

        private static CardInstance CreateProduct(Combatant owner, CardKind kind, PlayerFusionDependencies deps, CardInstance material)
        {
            UnityEngine.Transform parent = deps.Hand != null ? deps.Hand.transform : owner.transform;
            if (deps.Library != null)
            {
                CardInstance created = deps.Library.CreateCard(kind, owner, parent);
                if (created != null)
                {
                    return created;
                }

                Debug.LogError("[CardGame] CardLibraryComponent.CreateCard(" + kind + ") 返回 null，改用兜底克隆。");
            }
            else
            {
                Debug.LogError("[CardGame] CardFusionComponent.library 未注入，产物改用兜底克隆（建议由 Bootstrap 注入 CardLibraryComponent）。");
            }

            // 兜底：直接克隆材料实体（Instantiate 深拷贝行为组件），再把定义换成产物定义。
            if (material == null)
            {
                return null;
            }

            CardInstance clone = Instantiate(material, parent);
            clone.name = kind.ToString();
            CardDefinitionData definition = CardDefaults.Create(kind);
            clone.InitializeFrom(definition);
            clone.isTemporary = false;
            return clone;
        }

        private bool TryResolveDependencies(Combatant owner, out PlayerFusionDependencies deps)
        {
            deps = new PlayerFusionDependencies();
            deps.Hand = owner.GetComponent<Players.PlayerHandComponent>();
            deps.Turn = owner.GetComponent<Players.PlayerTurnComponent>();
            deps.Statuses = owner.GetComponent<Players.PlayerStatusComponent>();
            deps.Charge = owner.GetComponent<Players.PlayerChargeComponent>();

            if (deps.Hand == null || deps.Turn == null || deps.Charge == null)
            {
                Debug.LogError("[CardGame] CardFusionComponent.Fuse：玩家实体缺少 PlayerHandComponent / PlayerTurnComponent / PlayerChargeComponent 之一，无法合成。");
                return false;
            }

            if (library != null)
            {
                deps.Library = library;
            }

            return true;
        }

        // 一次合成所需的依赖集合，避免重复 GetComponent。
        private struct PlayerFusionDependencies
        {
            public Players.PlayerHandComponent Hand;
            public Players.PlayerTurnComponent Turn;
            public Players.PlayerStatusComponent Statuses;
            public Players.PlayerChargeComponent Charge;
            public Controllers.CardLibraryComponent Library;
        }
    }
}
