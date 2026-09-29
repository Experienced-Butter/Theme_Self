using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards
{
    /// <summary>
    /// 卡牌的身份组件：承载这张卡的静态定义数据。
    /// 规则出处：ARCHITECTURE.md 第 6 节 `Cards/CardIdentityComponent.cs`。
    /// 克隆时由 `Templates/CardTemplate.OnAfterClone` 把模板的 definition 深拷贝写入这里，
    /// 因此每个运行时卡牌实体都有一份独立的数值副本，不会与模板互相污染。
    /// </summary>
    public class CardIdentityComponent : MonoBehaviour
    {
        /// <summary>本卡的定义数据（数值唯一权威来源是 Core.CardDefaults）。</summary>
        public CardDefinitionData definition;

        /// <summary>卡牌种类（A/B/C/AA/BB/.../Ultimate/Duplicator）。definition 为空时返回默认的 A。</summary>
        public CardKind Kind
        {
            get { return definition != null ? definition.kind : CardKind.A; }
        }

        /// <summary>卡牌层级（Basic/Fused/Ultimate/Duplicator）。definition 为空时返回默认的 Basic。</summary>
        public CardTier Tier
        {
            get { return definition != null ? definition.tier : CardTier.Basic; }
        }

        /// <summary>显示名，仅用于日志。</summary>
        public string DisplayName
        {
            get { return definition != null ? definition.displayName : null; }
        }

        /// <summary>
        /// 用一份定义数据深拷贝初始化本组件。
        /// 供模板克隆、牌库生成与运行时兜底克隆共用，保证 definition 永不为 null。
        /// </summary>
        public void Initialize(CardDefinitionData source)
        {
            if (source == null)
            {
                Debug.LogError("[CardGame] CardIdentityComponent.Initialize 收到 null 定义，已退回默认 A 卡定义。");
                definition = CardDefaults.Create(CardKind.A);
                return;
            }

            definition = source.Clone();
        }
    }
}
