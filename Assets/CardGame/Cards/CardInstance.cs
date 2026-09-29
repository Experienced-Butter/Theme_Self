using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards
{
    /// <summary>
    /// 一张运行时卡牌实体（文件名为 CardInstance.cs，类名与被 ARCHITECTURE.md 指定的类名一致）。
    /// 规则出处：ARCHITECTURE.md 第 6 节 `Cards/CardInstanceComponent.cs`（类名 CardInstance）。
    /// Core 的 `CardPlayContext.Card` 指向本类型。
    /// </summary>
    public class CardInstance : MonoBehaviour
    {
        /// <summary>本体当前所在的区域：牌库 / 手牌 / 弃牌堆 / 结算中 / 已移除。</summary>
        public CardZone zone;

        /// <summary>本卡的持有者（玩家或怪物）。</summary>
        public Combatant owner;

        /// <summary>大招抽出的 A/B/C 临时卡，回合结束时从未使用的手牌中移除（ARCHITECTURE 第 13 节第 6 条）。</summary>
        public bool isTemporary;

        /// <summary>卡牌复制器给的免费合成次数标记（由 Players.PlayerStatusComponent 通过 FreeFusion 状态管理）。</summary>
        public bool grantedFreeFusion;

        /// <summary>把一份定义深拷贝写入本实体的 CardIdentityComponent；组件缺失时自动补齐。</summary>
        public void InitializeFrom(CardDefinitionData def)
        {
            CardIdentityComponent identity = GetComponent<CardIdentityComponent>();
            if (identity == null)
            {
                identity = gameObject.AddComponent<CardIdentityComponent>();
            }

            identity.Initialize(def);
        }

        /// <summary>本卡的 CardIdentityComponent（可能为 null）。</summary>
        public CardIdentityComponent Identity
        {
            get { return GetComponent<CardIdentityComponent>(); }
        }

        /// <summary>本卡的 CardPlayComponent（「生效」入口，可能为 null）。</summary>
        public CardPlayComponent Play
        {
            get { return GetComponent<CardPlayComponent>(); }
        }

        /// <summary>本卡的 CardBuffComponent（承载 AA 的强化倍率，可能为 null）。</summary>
        public CardBuffComponent Buff
        {
            get { return GetComponent<CardBuffComponent>(); }
        }

        /// <summary>本卡的 CardKind；没有身份组件时返回 A（仅用于日志容错）。</summary>
        public CardKind Kind
        {
            get
            {
                CardIdentityComponent identity = Identity;
                return identity != null ? identity.Kind : CardKind.A;
            }
        }

        /// <summary>本卡的 CardTier；没有身份组件时返回 Basic（仅用于日志容错）。</summary>
        public CardTier Tier
        {
            get
            {
                CardIdentityComponent identity = Identity;
                return identity != null ? identity.Tier : CardTier.Basic;
            }
        }

        /// <summary>显示名，优先取 definition.displayName，其次取 GameObject 名。</summary>
        public string DisplayName
        {
            get
            {
                CardIdentityComponent identity = Identity;
                if (identity != null && !string.IsNullOrEmpty(identity.DisplayName))
                {
                    return identity.DisplayName;
                }

                return name;
            }
        }
    }
}
