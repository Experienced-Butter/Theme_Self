using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 免疫行为：给自身挂 1 层 DamageImmunity，免疫下一次伤害。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `ImmunityBehavior`；规则原文第三节「BC 免疫下次伤害，抽取一张卡牌」。
    /// 消耗由 Combat.HealthComponent.ApplyDamage 负责：命中免疫时本次伤害为 0 并移除该状态。
    /// </summary>
    public class ImmunityBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖层数，&lt;= 0 时使用默认 1 层。</summary>
        public int stacks = 1;

        /// <summary>覆盖持续回合数，&lt;= 0 表示不限期（直到被伤害消耗或战斗结束）。</summary>
        public int turns;

        /// <summary>免疫在伤害/治疗/减益之后结算，作为本卡的收尾保护。</summary>
        public int ResolutionOrder
        {
            get { return 600; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] ImmunityBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant user = context.User;
            if (user == null || user.Statuses == null)
            {
                Debug.LogError("[CardGame] ImmunityBehavior.Resolve 找不到出牌者或其状态组件。");
                return;
            }

            int layers = stacks > 0 ? stacks : 1;
            int duration = turns > 0 ? turns : GameRules.DefaultDebuffTurns;

            for (int i = 0; i < layers; i++)
            {
                user.Statuses.Add(StatusKind.DamageImmunity, 1f, duration, user.team == Team.Player);
            }

            context.Log(user.displayName + " 获得 " + layers + " 层伤害免疫（免疫下一次伤害）。");
        }

        public string Describe()
        {
            int layers = stacks > 0 ? stacks : 1;
            return "获得 " + layers + " 层伤害免疫";
        }
    }
}
