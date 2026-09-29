using System.Collections.Generic;
using CardGame.Combat;
using CardGame.Core;
using CardGame.Players;
using UnityEngine;

namespace CardGame.Cards.Behaviors
{
    /// <summary>
    /// 大招行为：结算大招卡的三个效果。
    /// 规则出处：ARCHITECTURE.md 第 6.1 节 `UltimateBehavior`；规则原文第六节第 1 条
    /// 「大招效果：下回合增加两次出手机会，抽取三张临时卡牌（ABC 随机）与一个卡牌复制器（仅复制 ABC，获得一次合成次数）」。
    /// ARCHITECTURE 第 13 节第 5/6 条：充能到 4 点先获得大招卡并进入手牌，打出本卡才结算上述效果；
    /// 临时卡在回合结束时从未使用的手牌中移除。
    /// 注意：大招消耗的充能由 Players.PlayerChargeComponent.Consume() 在大招被打出时扣除（player 负责人）。
    /// </summary>
    public class UltimateBehavior : MonoBehaviour, ICardBehavior
    {
        /// <summary>覆盖下回合额外出手次数，&lt; 0 时改用 definition.extraPlaysNextTurn，仍为 0 时用规则常量 2。</summary>
        public int extraPlaysOverride = -1;

        /// <summary>覆盖抽出的临时卡张数，&lt; 0 时用规则常量 3。</summary>
        public int tempCardCountOverride = -1;

        /// <summary>覆盖赠送的复制器张数，&lt; 0 时用规则常量 1。</summary>
        public int duplicatorCountOverride = -1;

        /// <summary>大招是最后结算的行为，排在所有手牌类行为之后。</summary>
        public int ResolutionOrder
        {
            get { return 1200; }
        }

        public void Resolve(CardPlayContext context)
        {
            if (context == null)
            {
                Debug.LogError("[CardGame] UltimateBehavior.Resolve 收到 null 上下文。");
                return;
            }

            Combatant user = context.User;
            if (user == null)
            {
                Debug.LogError("[CardGame] UltimateBehavior.Resolve 找不到出牌者。");
                return;
            }

            Controllers.CardLibraryComponent library = ResolveLibrary(context);
            if (library == null)
            {
                Debug.LogError("[CardGame] UltimateBehavior.Resolve 找不到 CardLibraryComponent，大招无法生成临时卡与复制器。");
                return;
            }

            PlayerHandComponent hand = user.GetComponent<PlayerHandComponent>();
            if (hand == null)
            {
                Debug.LogError("[CardGame] UltimateBehavior.Resolve 出牌者身上没有 PlayerHandComponent。");
                return;
            }

            // 1) 下回合出手 +2
            int extraPlays = ResolveExtraPlays(context);
            PlayerTurnComponent turn = user.GetComponent<PlayerTurnComponent>();
            if (turn != null && extraPlays > 0)
            {
                turn.GrantExtraPlaysNextTurn(extraPlays);
                context.Log("大招：下回合出牌次数 +" + extraPlays + "（待生效 " + turn.PendingExtraPlays + "）。");
            }

            // 2) 抽 3 张随机 A/B/C 临时卡
            int tempCount = ResolveTempCardCount();
            int createdTemp = 0;
            for (int i = 0; i < tempCount; i++)
            {
                CardKind kind = RandomBasicKind(context);
                CardInstance temp = library.CreateCard(kind, user, hand.transform);
                if (temp == null)
                {
                    Debug.LogError("[CardGame] UltimateBehavior：临时卡 " + kind + " 生成失败。");
                    continue;
                }

                temp.isTemporary = true;
                temp.owner = user;
                temp.zone = CardZone.Hand;
                if (hand.TryAdd(temp))
                {
                    createdTemp++;
                }
                else
                {
                    context.Log("大招：手牌已满，临时卡 " + temp.DisplayName + " 未能进入手牌。");
                }
            }

            context.Log("大招：获得 " + createdTemp + "/" + tempCount + " 张随机临时卡（A/B/C，回合结束移除）。");

            // 3) 给 1 张卡牌复制器
            int duplicators = ResolveDuplicatorCount();
            int createdDuplicators = 0;
            for (int i = 0; i < duplicators; i++)
            {
                CardInstance duplicator = library.CreateCard(CardKind.Duplicator, user, hand.transform);
                if (duplicator == null)
                {
                    Debug.LogError("[CardGame] UltimateBehavior：复制器生成失败。");
                    continue;
                }

                duplicator.isTemporary = false;
                duplicator.owner = user;
                duplicator.zone = CardZone.Hand;
                if (hand.TryAdd(duplicator))
                {
                    createdDuplicators++;
                }
                else
                {
                    context.Log("大招：手牌已满，复制器未能进入手牌。");
                }
            }

            context.Log("大招：获得 " + createdDuplicators + "/" + duplicators + " 张卡牌复制器。");
        }

        public string Describe()
        {
            int extraPlays = ResolveDisplayExtraPlays();
            int tempCount = tempCardCountOverride >= 0 ? tempCardCountOverride : GameRules.UltimateTempCards;
            int duplicators = duplicatorCountOverride >= 0 ? duplicatorCountOverride : GameRules.UltimateDuplicators;
            return "大招：下回合出手 +" + extraPlays + "，抽 " + tempCount + " 张临时卡，获得 " + duplicators + " 张复制器";
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

        // 随机 A/B/C：必须走 Core.IRandomSource（ARCHITECTURE 第 0 节第 7 条）。
        private static CardKind RandomBasicKind(CardPlayContext context)
        {
            IRandomSource random = context.Battle != null ? context.Battle.Random : null;
            if (random == null)
            {
                Debug.LogError("[CardGame] UltimateBehavior：BattleContext.Random 不可用，退回固定 A 卡。");
                return CardKind.A;
            }

            int roll = random.Range(0, 3);
            if (roll == 0)
            {
                return CardKind.A;
            }

            return roll == 1 ? CardKind.B : CardKind.C;
        }

        private int ResolveExtraPlays(CardPlayContext context)
        {
            if (extraPlaysOverride >= 0)
            {
                return extraPlaysOverride;
            }

            CardIdentityComponent identity = context.Card != null ? context.Card.Identity : null;
            if (identity != null && identity.definition != null && identity.definition.extraPlaysNextTurn > 0)
            {
                return identity.definition.extraPlaysNextTurn;
            }

            return GameRules.UltimateExtraPlays;
        }

        private int ResolveDisplayExtraPlays()
        {
            if (extraPlaysOverride >= 0)
            {
                return extraPlaysOverride;
            }

            CardIdentityComponent identity = GetComponent<CardIdentityComponent>();
            if (identity != null && identity.definition != null && identity.definition.extraPlaysNextTurn > 0)
            {
                return identity.definition.extraPlaysNextTurn;
            }

            return GameRules.UltimateExtraPlays;
        }

        private int ResolveTempCardCount()
        {
            if (tempCardCountOverride >= 0)
            {
                return tempCardCountOverride;
            }

            return GameRules.UltimateTempCards;
        }

        private int ResolveDuplicatorCount()
        {
            if (duplicatorCountOverride >= 0)
            {
                return duplicatorCountOverride;
            }

            return GameRules.UltimateDuplicators;
        }
    }
}
