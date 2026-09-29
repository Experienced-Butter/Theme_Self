// 归属：monsters（ARCHITECTURE.md 第 10 节、第 12 节）。
// 场景入口：保证「在空场景里直接按 Play 就能打完一局」。
using System.Collections;
using System.Collections.Generic;
using CardGame.Cards;
using CardGame.Combat;
using CardGame.Controllers;
using CardGame.Core;
using CardGame.Players;
using UnityEngine;

namespace CardGame.Bootstrap
{
    /// <summary>
    /// 场景入口 MonoBehaviour（ARCHITECTURE 第 10 节）。
    /// Start 流程：
    ///   1) runOnStart 为 true 时，先用 SceneTemplateBuilder 补齐场景缺失的模板与控制者
    ///      （Builder 是「只创建缺失项」的幂等操作，已有对象一律不动）；
    ///   2) SetupBattle(level, seed) 开战，并注入运行时引用（CardFusionComponent.library 等）；
    ///   3) GameFlowComponent.RunBattle() 打开内置代打，自动打完一局（同步推到胜负）；
    ///      若流程停在玩家回合未判定胜负，则由本组件按简单策略以「外部驱动」的方式兜底推完。
    /// 全程战报由 BattleContext.Log 输出到 Console。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        /// <summary>关卡：默认支线关（RULES 第五节：支线关为波次关）。</summary>
        public LevelId level = LevelId.SideQuest;

        /// <summary>随机种子（交给 SetupBattle → RandomComponent，随机数只走 IRandomSource）。</summary>
        public int seed = 0;

        /// <summary>是否在 Start 时自动开战。</summary>
        public bool runOnStart = true;

        // 保护上限：防止回合流程卡住时无限空转（正常一局 15~25 回合，远小于该值）。
        private const int MaxAutoPlaySteps = 20000;
        private const int StallLimit = 900;

        private GameController controller;
        private GameFlowComponent flow;
        private bool battleEnded;

        private void Start()
        {
            if (!runOnStart)
            {
                Debug.Log("[CardGame][Bootstrap] runOnStart = false，未自动开战（可手动调用 RunBattle）。");
                return;
            }

            RunBattle();
        }

        private void OnDestroy()
        {
            if (controller != null)
            {
                controller.OnBattleEnded -= HandleBattleEnded;
            }
        }

        /// <summary>补齐场景 → 开战 → 自动打完一局。可被编辑器或其它脚本手动调用。</summary>
        public void RunBattle()
        {
            controller = EnsureSceneObjects();
            if (controller == null)
            {
                Debug.LogError("[CardGame][Bootstrap] 场景缺少 GameController 且自动创建失败，已中止。");
                return;
            }

            flow = controller.GetComponent<GameFlowComponent>();
            battleEnded = false;

            controller.OnBattleEnded -= HandleBattleEnded;   // 允许多次调用 RunBattle，避免重复订阅
            controller.OnBattleEnded += HandleBattleEnded;

            Debug.Log(string.Format("[CardGame][Bootstrap] ===== 开战 =====\n关卡：{0}\n随机种子：{1}",
                level, seed));

            controller.SetupBattle(level, seed);

            InjectRuntimeReferences(controller);

            // 自动打完一局：GameFlowComponent.RunBattle 打开内置代打，一路推到胜负（同步完成）。
            // 若它因为任何原因没能判定胜负（流程停在玩家回合），下面的协程兜底再以「外部驱动」
            // 的方式逐张出牌推进——两条路径都只走 GameController.PlayCard / TryFuseCards 这一个出牌入口。
            if (flow != null)
            {
                flow.RunBattle();
            }

            if (!battleEnded)
            {
                StartCoroutine(AutoPlayToEnd());
            }
        }

        // 补齐场景：Build* 都是幂等的「只创建缺失项」，因此这里既能在空场景里从零装配，
        // 也能在用户已经摆好的场景里原样复用。
        private static GameController EnsureSceneObjects()
        {
            SceneTemplateBuilder.BuildPlayerTemplate(null);
            SceneTemplateBuilder.BuildCardLibrary(null);
            SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Attacker);
            SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Support);

            GameObject controllerObject = SceneTemplateBuilder.BuildController(null);
            if (controllerObject == null)
            {
                return null;
            }

            return controllerObject.GetComponent<GameController>();
        }

        // ARCHITECTURE 第 6.2 节：CardFusionComponent.library 由 Bootstrap 注入。
        // 模板上已注入过一次（克隆体继承），这里再对运行时玩家实体做一次兜底；
        // GameController.WirePlayer 也会做同样的注入，本方法只填空引用，重复调用无副作用。
        private static void InjectRuntimeReferences(GameController gameController)
        {
            if (gameController == null)
            {
                return;
            }

            CardLibraryComponent library = gameController.GetComponent<CardLibraryComponent>();
            BattleContext context = gameController.Context;
            if (library == null || context == null || context.Player == null)
            {
                return;
            }

            CardFusionComponent fusion = context.Player.GetComponent<CardFusionComponent>();
            if (fusion != null && fusion.library == null)
            {
                fusion.library = library;
            }

            PlayerDeckComponent deck = context.Player.GetComponent<PlayerDeckComponent>();
            if (deck != null && deck.library == null)
            {
                deck.library = library;
            }
        }

        // 兜底驱动：仅当 RunBattle 未能判定胜负时才启动。
        // 每帧只做 1 次玩家动作（合成或出牌），动作结束后由流程的 NotifyFlowPlayerActionResolved
        // 自行推进敌人回合与下一回合，直到 OnBattleEnded 或触发保护上限。
        private IEnumerator AutoPlayToEnd()
        {
            int steps = 0;
            int stall = 0;
            string signature = null;

            while (!battleEnded && steps < MaxAutoPlaySteps)
            {
                steps++;

                if (controller != null && controller.Context != null)
                {
                    TryAutoPlayStep(controller);

                    string current = ProgressSignature(controller);
                    if (current == signature)
                    {
                        stall++;
                    }
                    else
                    {
                        stall = 0;
                        signature = current;
                    }

                    if (stall >= StallLimit)
                    {
                        Debug.LogWarning("[CardGame][Bootstrap] 连续 " + StallLimit +
                                         " 帧无任何状态变化，判定回合流程已停滞，停止自动演示（战斗未判定胜负）。");
                        yield break;
                    }
                }

                yield return null;
            }

            if (!battleEnded)
            {
                Debug.LogWarning("[CardGame][Bootstrap] 达到自动演示上限（" + MaxAutoPlaySteps +
                                 " 帧）仍未分出胜负，已停止。");
            }
        }

        // 简单代打策略：玩家回合内优先凑合成（充能 +1），否则打出手牌中收益最高的一张。
        // 若回合流程自己就在代打玩家回合，这里会因为「已无出牌次数」或阶段推进而自然停手。
        private void TryAutoPlayStep(GameController gameController)
        {
            if (flow != null && flow.Phase != GamePhase.PlayerTurn)
            {
                return;
            }

            BattleContext context = gameController.Context;
            Combatant player = context != null ? context.Player : null;
            if (player == null || !player.IsAlive)
            {
                return;
            }

            PlayerTurnComponent turn = player.GetComponent<PlayerTurnComponent>();
            if (turn != null && !turn.CanPlay)
            {
                return;
            }

            IReadOnlyList<CardInstance> hand = gameController.Hand;
            if (hand == null || hand.Count == 0)
            {
                return;
            }

            // 1) 能凑齐配方就先合成（规则六第 1 条：合成消耗 1 次出牌并获得 1 点充能）。
            for (int i = 0; i < hand.Count; i++)
            {
                for (int j = i + 1; j < hand.Count; j++)
                {
                    CardInstance a = hand[i];
                    CardInstance b = hand[j];
                    if (a == null || b == null)
                    {
                        continue;
                    }

                    CardKind product;
                    if (!CardFusionComponent.TryGetRecipe(a.Kind, b.Kind, out product))
                    {
                        continue;
                    }

                    if (gameController.TryFuseCards(a, b))
                    {
                        return;
                    }
                }
            }

            // 2) 否则打出一张牌。
            CardInstance chosen = ChooseCardToPlay(hand);
            if (chosen != null)
            {
                gameController.PlayCard(chosen);
            }
        }

        private static CardInstance ChooseCardToPlay(IReadOnlyList<CardInstance> hand)
        {
            CardInstance best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < hand.Count; i++)
            {
                CardInstance card = hand[i];
                if (card == null)
                {
                    continue;
                }

                int score = ScoreOf(card.Kind);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = card;
                }
            }

            return best;
        }

        // 出牌优先级（仅演示用）：大招 > 合成伤害卡 > 基础伤害卡 > 功能性卡 > 复制器。
        private static int ScoreOf(CardKind kind)
        {
            switch (kind)
            {
                case CardKind.Ultimate:
                    return 90;
                case CardKind.AA:
                    return 80;
                case CardKind.AB:
                    return 70;
                case CardKind.AC:
                    return 60;
                case CardKind.A:
                    return 50;
                case CardKind.BC:
                    return 40;
                case CardKind.CC:
                    return 30;
                case CardKind.BB:
                    return 20;
                case CardKind.B:
                    return 15;
                case CardKind.C:
                    return 10;
                case CardKind.Duplicator:
                    return 5;
                default:
                    return 0;
            }
        }

        // 状态指纹：用于判断回合流程是否还在推进（阶段 / 双方血量 / 手牌数 / 剩余出牌次数）。
        private static string ProgressSignature(GameController gameController)
        {
            BattleContext context = gameController.Context;
            if (context == null)
            {
                return "no-context";
            }

            int enemyHp = 0;
            IReadOnlyList<Combatant> enemies = context.Enemies;
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i] != null)
                    {
                        enemyHp += enemies[i].CurrentHp;
                    }
                }
            }

            int playerHp = context.Player != null ? context.Player.CurrentHp : -1;
            IReadOnlyList<CardInstance> hand = gameController.Hand;
            int handCount = hand != null ? hand.Count : -1;

            PlayerTurnComponent turn = context.Player != null
                ? context.Player.GetComponent<PlayerTurnComponent>()
                : null;
            int playsLeft = turn != null ? turn.PlaysRemaining : -1;

            return string.Format("{0}|{1}|{2}|{3}", playerHp, enemyHp, handCount, playsLeft);
        }

        private void HandleBattleEnded(bool victory)
        {
            battleEnded = true;
            Debug.Log(BuildFinalReport(victory));
        }

        private string BuildFinalReport(bool victory)
        {
            BattleContext context = controller != null ? controller.Context : null;
            string playerHp = "未知";
            if (context != null && context.Player != null)
            {
                playerHp = string.Format("{0}/{1}", context.Player.CurrentHp, context.Player.MaxHp);
            }

            int wave = 0;
            LevelComponent levelComponent = controller != null
                ? controller.GetComponent<LevelComponent>()
                : null;
            if (levelComponent != null)
            {
                wave = levelComponent.CurrentWave;
            }

            return string.Format(
                "[CardGame][Bootstrap] ===== 战斗结束：{0} =====\n关卡：{1}（seed {2}）\n玩家剩余生命：{3}\n结束时波次：{4}",
                victory ? "胜利" : "失败", level, seed, playerHp, wave);
        }
    }
}
