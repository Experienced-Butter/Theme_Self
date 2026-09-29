using UnityEngine;

namespace CardGame.Templates
{
    /// <summary>
    /// 卡牌模板实体。克隆体通过 CardIdentityComponent 持有自己的一份数值定义副本。
    /// </summary>
    public class CardTemplate : EntityTemplate
    {
        /// <summary>该模板对应的卡牌数值定义（由 CardDefaults 生成）。</summary>
        public Core.CardDefinitionData definition;

        /// <summary>
        /// 把 definition 深拷贝写入克隆体的 CardIdentityComponent.definition
        /// （经 cards 负责人提供的 Initialize 入口，内部执行 Clone），
        /// 确保克隆体之间、克隆体与模板之间不共享同一个 CardDefinitionData 实例。
        /// </summary>
        protected override void OnAfterClone(GameObject clone)
        {
            if (clone == null)
            {
                return;
            }

            Cards.CardIdentityComponent identity = clone.GetComponent<Cards.CardIdentityComponent>();
            if (identity == null)
            {
                identity = clone.GetComponentInChildren<Cards.CardIdentityComponent>(true);
            }
            if (identity == null)
            {
                Debug.LogWarning("[CardGame] 卡牌模板 " + templateId +
                                 " 的克隆体缺少 CardIdentityComponent，数值定义无法写入。");
                return;
            }
            if (definition == null)
            {
                Debug.LogWarning("[CardGame] 卡牌模板 " + templateId + " 缺少 definition。");
                return;
            }

            // 深拷贝：克隆体拿到自己的数值副本（Initialize 内部执行 definition.Clone()）。
            identity.Initialize(definition);
        }
    }
}
