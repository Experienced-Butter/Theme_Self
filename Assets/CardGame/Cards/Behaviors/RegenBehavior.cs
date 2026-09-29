using CardGame.Combat;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 持续回复行为：给自身挂 Regen 状态，每回合回复 regenPerTurn 点，持续 regenTurns 回合。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `RegenBehavior`；规则原文第三节
    /// 「CC：回复血量/获得两回合持续回血 840 点（540 + 150×2）」。
    /// 状态的实际扣减由 Combat.StatusComponent.TickTurnEnd 在回合结束时负责。
    /// </summary>
    public class RegenBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖每回合回复量，&lt;= 0 时改用 definition.regenPerTurn。</summary>
        public int perTurnOverride;

        /// <summary>覆盖持续回合数，&lt;= 0 时改用 definition.regenTurns。</summary>
        public int turnsOverride;

        /// <summary>持续回复在立即治疗之后结算。</summary>
        public int ResolutionOrder
        {
            get { return 300; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] RegenBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant user = context.User;
            if (user == null || user.Statuses == null)
            {
                Debug.LogError("[CardGame] RegenBehavior.Resolve 找不到出牌者或其状态组件。");
                return;
            }

            int perTurn = ResolvePerTurn(context);
            int turns = ResolveTurns(context);
            if (perTurn <= 0 || turns <= 0)
            {
                context.Log("持续回复结算：数值为 0，跳过。");
                return;
            }

            user.Statuses.Add(StatusKind.Regen, perTurn, turns, user.team == Team.Player);
            context.Log(user.displayName + " 获得持续回复 " + perTurn + " × " + turns + " 回合。");
        }

        public string Describe()
        {
            string perTurnPart = perTurnOverride > 0 ? perTurnOverride.ToString() : "definition.regenPerTurn";
            string turnsPart = turnsOverride > 0 ? turnsOverride.ToString() : "definition.regenTurns";
            return "持续回复 " + perTurnPart + " × " + turnsPart + " 回合";
        }

        private int ResolvePerTurn(CardPlayContext context)
        {
            if (perTurnOverride > 0)
            {
                return perTurnOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null)
            {
                return identity.definition.regenPerTurn;
            }

            return 0;
        }

        private int ResolveTurns(CardPlayContext context)
        {
            if (turnsOverride > 0)
            {
                return turnsOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null && identity.definition.regenTurns > 0)
            {
                return identity.definition.regenTurns;
            }

            return GameRules.DefaultDebuffTurns;
        }
    }
}
