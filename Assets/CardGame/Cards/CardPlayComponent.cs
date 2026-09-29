using System.Collections.Generic;
using System.Text;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards
{
    /// <summary>
    /// 卡牌的「生效」入口。
    /// 规则出处：ARCHITECTURE.md 第 6 节 `Cards/CardPlayComponent.cs`。
    /// 一张卡的完整效果 = 它身上挂载的所有 Core.ICardBehavior 组件的叠加，
    /// 打出时按 ResolutionOrder 升序依次执行 —— 这就是「生效」，不存在按 CardKind 分支的巨型 switch。
    /// </summary>
    [RequireComponent(typeof(CardIdentityComponent))]
    [RequireComponent(typeof(CardBuffComponent))]
    public class CardPlayComponent : MonoBehaviour
    {
        /// <summary>
        /// 执行本卡的全部行为。按 ResolutionOrder 升序稳定排序后依次 Resolve；
        /// 数值累加类行为（伤害/治疗）先结算，状态与手牌增益类行为随后生效。
        /// 每次调用都会重新枚举组件，保证运行时挂上的行为同样生效。
        /// </summary>
        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] CardPlayComponent.Resolve 收到 null 上下文。");
                return;
            }

            ICardBehavior[] collected = GetComponents<ICardBehavior>();
            List<ICardBehavior> ordered = new List<ICardBehavior>();
            for (int i = 0; i < collected.Length; i++)
            {
                if (collected[i] != null)
                {
                    ordered.Add(collected[i]);
                }
            }

            if (ordered.Count == 0)
            {
                Debug.LogWarning("[CardGame] " + DescribeCardName() + " 身上没有任何 ICardBehavior，打出后不产生效果。");
                return;
            }

            ordered.Sort(CompareBehaviors);

            for (int i = 0; i < ordered.Count; i++)
            {
                ICardBehavior behavior = ordered[i];
                if (behavior == null)
                {
                    continue;
                }

                behavior.Resolve(context);
            }
        }

        /// <summary>汇总各行为的 Describe()，用于日志与调试显示。</summary>
        public string Describe()
        {
            ICardBehavior[] collected = GetComponents<ICardBehavior>();
            List<ICardBehavior> ordered = new List<ICardBehavior>();
            for (int i = 0; i < collected.Length; i++)
            {
                if (collected[i] != null)
                {
                    ordered.Add(collected[i]);
                }
            }

            ordered.Sort(CompareBehaviors);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ordered.Count; i++)
            {
                string part = ordered[i].Describe();
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(" + ");
                }

                sb.Append(part);
            }

            return sb.ToString();
        }

        // ResolutionOrder 升序；同序号保持原有相对顺序（稳定排序），保证结果可复现。
        private static int CompareBehaviors(ICardBehavior x, ICardBehavior y)
        {
            return x.ResolutionOrder.CompareTo(y.ResolutionOrder);
        }

        private string DescribeCardName()
        {
            CardIdentityComponent identity = GetComponent<CardIdentityComponent>();
            if (identity != null && identity.definition != null && !string.IsNullOrEmpty(identity.definition.displayName))
            {
                return "卡牌 " + identity.definition.displayName;
            }

            return "卡牌 " + name;
        }
    }
}
