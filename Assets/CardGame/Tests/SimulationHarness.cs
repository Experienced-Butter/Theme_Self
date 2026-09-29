using System.Collections.Generic;
using System.Text;
using CardGame.Bootstrap;
using CardGame.Combat;
using CardGame.Controllers;
using CardGame.Core;
using UnityEngine;

namespace CardGame.Tests
{
    /// <summary>
    /// 无头模拟跑分：不依赖 Unity Test Framework，也不依赖渲染/美术。
    ///
    /// 流程（每局）：临时创建控制者实体 → SetupBattle(关卡, 种子) → GameFlowComponent.RunBattle()
    /// 同步跑完整局 → 统计胜负/回合数/剩余血量 → 销毁控制者实体。
    /// 因为每局种子固定（baseSeed + 局号），同一参数重复运行结果完全一致。
    ///
    /// 用法：
    ///   string report = SimulationHarness.Run(100, LevelId.SideQuest, 12345);
    ///   Debug.Log(report);
    ///
    /// 规则文档第七节的参考跑分（100 次蒙特卡洛）：
    ///   关一 单波 胜率 100% 平均 11.6 回合 平均剩余 HP 2300；
    ///   关二 单波 胜率 88%  平均 15.7 回合 平均剩余 HP 1900；
    ///   支线关 波次 1 胜率 85% 平均 12.8 回合 平均剩余 HP 1950；
    ///   支线关 波次 2 胜率 100%(继承) 平均 10.0 回合 平均剩余 HP 2150；
    ///   支线关 总计 胜率 85% 平均 21.7 回合 平均剩余 HP 2150。
    /// 参考值来自人工解析模型，允许有偏差，本工具只负责给出可复现的实测口径。
    /// </summary>
    public static class SimulationHarness
    {
        /// <summary>单局回合上限：超过即超时判负，防止无头模拟陷入死循环。</summary>
        public const int MaxTurns = 100;

        private sealed class RunResult
        {
            public bool Victory;
            public int Turns;
            public int HpLeft;
            public int MaxHp;
            public int RemainingEnemies;
            public int Seed;
            public string Error;
        }

        /// <summary>
        /// 无头跑 N 局，返回战报文本（胜率 / 平均回合 / 平均剩余 HP）。
        /// </summary>
        /// <param name="runs">模拟局数，&lt;1 时按 1 处理。</param>
        /// <param name="level">关卡（关一 / 关二 / 支线关）。</param>
        /// <param name="seed">基准种子；第 i 局使用 seed + i，保证可复现。</param>
        public static string Run(int runs, LevelId level, int seed)
        {
            if (runs < 1)
            {
                runs = 1;
            }

            // GameController.Instance 只在 Awake 时取第一个实例；场景里若已有别的控制器，
            // 经 Instance 取玩家的逻辑（关卡波次回复等）会指向别的对象，这里直接拒绝跑而不是静默跑错。
            GameController existing = GameController.Instance;
            if (existing != null)
            {
                List<RunResult> blocked = new List<RunResult>(runs);
                for (int i = 0; i < runs; i++)
                {
                    RunResult r = new RunResult();
                    r.Seed = seed + i;
                    r.Error = "场景里已存在 GameController.Instance，无头模拟会与它抢实例（请在空场景运行，或先清掉现有控制者）";
                    blocked.Add(r);
                }
                return BuildReport(runs, level, seed, blocked);
            }

            List<GameObject> created = new List<GameObject>();
            List<RunResult> results = new List<RunResult>(runs);
            try
            {
                GameController controller = BuildControllerForSimulation(created);
                if (controller == null)
                {
                    for (int i = 0; i < runs; i++)
                    {
                        RunResult r = new RunResult();
                        r.Seed = seed + i;
                        r.Error = "装配控制者实体失败（SceneTemplateBuilder 未能建出控制器）";
                        results.Add(r);
                    }
                    return BuildReport(runs, level, seed, results);
                }

                // 多局共用同一个控制器实例：SetupBattle 每局都会重设种子、清空 RuntimeRoot 并重建玩家，
                // 因此局与局之间没有残留状态。
                for (int i = 0; i < runs; i++)
                {
                    results.Add(RunOne(controller, level, seed + i));
                }
            }
            catch (System.Exception ex)
            {
                RunResult r = new RunResult();
                r.Seed = seed;
                r.Error = ex.GetType().Name + ": " + ex.Message;
                results.Add(r);
            }
            finally
            {
                // 只销毁本次模拟创建的对象，不碰运行前场景里已有的实体。
                for (int i = created.Count - 1; i >= 0; i--)
                {
                    DestroySafely(created[i]);
                }
            }

            return BuildReport(runs, level, seed, results);
        }

        /// <summary>跑一局并返回是否胜利（单局便捷入口，每次自建 / 自毁控制器）。</summary>
        public static bool RunOnce(LevelId level, int seed)
        {
            if (GameController.Instance != null)
            {
                return false;
            }

            List<GameObject> created = new List<GameObject>();
            try
            {
                GameController controller = BuildControllerForSimulation(created);
                return RunOne(controller, level, seed).Victory;
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; i--)
                {
                    DestroySafely(created[i]);
                }
            }
        }

        // ---------------------------------------------------------------- 装配

        /// <summary>
        /// 按 SceneTemplateBuilder 的设计顺序装配：先建模板实体（玩家 / 卡牌库 / 两种怪物），
        /// 再 BuildController 做总装与引用注入 —— BuildController 只会从场景里找模板来接线，
        /// 不建模板，所以顺序不能颠倒。
        /// </summary>
        private static GameController BuildControllerForSimulation(List<GameObject> created)
        {
            GameObject playerTemplate = SceneTemplateBuilder.BuildPlayerTemplate(null);
            created.Add(playerTemplate);

            GameObject cardLibrary = SceneTemplateBuilder.BuildCardLibrary(null);
            created.Add(cardLibrary);

            created.Add(SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Attacker));
            created.Add(SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Support));

            GameObject controllerRoot = SceneTemplateBuilder.BuildController(null);
            created.Add(controllerRoot);

            return controllerRoot != null ? controllerRoot.GetComponent<GameController>() : null;
        }

        // ---------------------------------------------------------------- 单局

        private static RunResult RunOne(GameController controller, LevelId level, int seed)
        {
            RunResult result = new RunResult();
            result.Seed = seed;

            if (controller == null)
            {
                result.Error = "控制器为 null";
                return result;
            }

            GameFlowComponent flow = controller.GetComponent<GameFlowComponent>();
            if (flow == null)
            {
                result.Error = "控制者实体缺少 GameFlowComponent";
                return result;
            }

            try
            {
                int playerTurns = 0;
                bool turnLimitHit = false;
                System.Action<GamePhase> onPhase = delegate (GamePhase phase)
                {
                    if (phase != GamePhase.PlayerTurn)
                    {
                        return;
                    }

                    playerTurns++;

                    // 回合上限保护：超过 MaxTurns 视为超时判负，并主动收尾，
                    // 避免双方都无法击杀对方时无头模拟陷入死循环（任务要求 100 回合上限）。
                    if (playerTurns > MaxTurns)
                    {
                        turnLimitHit = true;
                        flow.EndPlayerTurn();
                        flow.StartEnemyTurn();
                    }
                };
                System.Action<bool> onEnded = delegate (bool victory)
                {
                    result.Victory = victory;
                };

                flow.PhaseChanged += onPhase;
                controller.OnBattleEnded += onEnded;
                try
                {
                    controller.SetupBattle(level, seed);
                    flow.RunBattle();

                    result.Turns = playerTurns;
                    if (turnLimitHit)
                    {
                        result.Error = "超过 " + MaxTurns + " 回合仍未分出胜负（超时判负）";
                    }

                    if (controller.Context != null && controller.Context.Player != null)
                    {
                        result.HpLeft = controller.Context.Player.CurrentHp;
                        result.MaxHp = controller.Context.Player.MaxHp;

                        List<Combatant> alive = controller.Context.AliveEnemies();
                        result.RemainingEnemies = alive.Count;
                    }
                    else
                    {
                        result.Error = "SetupBattle 之后 Context 仍为空";
                    }
                }
                finally
                {
                    flow.PhaseChanged -= onPhase;
                    controller.OnBattleEnded -= onEnded;
                }
            }
            catch (System.Exception ex)
            {
                result.Error = ex.GetType().Name + ": " + ex.Message;
            }

            return result;
        }

        // ---------------------------------------------------------------- 汇总

        private static string BuildReport(int runs, LevelId level, int seed, List<RunResult> results)
        {
            int wins = 0;
            int valid = 0;
            int turns = 0;
            int hpLeft = 0;
            int maxHp = 0;
            int hpRatioSample = 0;
            List<string> errors = new List<string>();

            for (int i = 0; i < results.Count; i++)
            {
                RunResult r = results[i];
                if (!string.IsNullOrEmpty(r.Error))
                {
                    errors.Add("第 " + i + " 局（seed " + r.Seed + "）：" + r.Error);
                    continue;
                }

                valid++;
                if (r.Victory)
                {
                    wins++;
                }
                turns += r.Turns;
                hpLeft += r.HpLeft;
                maxHp += r.MaxHp;
                if (r.MaxHp > 0)
                {
                    hpRatioSample += (int)(100L * r.HpLeft / r.MaxHp);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========== CardGame 无头模拟 ==========");
            sb.Append("关卡: ").Append(LevelName(level))
              .Append("    局数: ").Append(runs)
              .Append("    基准种子: ").Append(seed)
              .AppendLine();
            sb.AppendLine();

            if (valid == 0)
            {
                sb.AppendLine("[FAIL] 没有一局成功跑完，无法给出跑分。");
            }
            else
            {
                double winRate = 100.0 * wins / valid;
                double avgTurns = (double)turns / valid;
                double avgHp = (double)hpLeft / valid;
                double avgHpRatio = (double)hpRatioSample / valid;

                sb.AppendLine(string.Format("有效局数: {0}/{1}", valid, runs));
                sb.AppendLine(string.Format("胜率:        {0:0.0}%  （{1}/{2}）", winRate, wins, valid));
                sb.AppendLine(string.Format("平均回合数:  {0:0.0}", avgTurns));
                sb.AppendLine(string.Format("平均剩余HP:  {0:0.0} / {1:0}  （{2:0.0}%）", avgHp, valid > 0 ? (double)maxHp / valid : 0.0, avgHpRatio));
                sb.AppendLine();
                sb.AppendLine("规则文档第七节参考值（人工解析模型, 100 局）: ");
                sb.AppendLine("  关一(Level1)     胜率 100.0%  平均 11.6 回合  平均剩余 HP 2300");
                sb.AppendLine("  关二(Level2)     胜率  88.0%  平均 15.7 回合  平均剩余 HP 1900");
                sb.AppendLine("  支线关(SideQuest) 胜率  85.0%  平均 21.7 回合  平均剩余 HP 2150");
            }

            if (errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("异常局数: " + errors.Count);
                int show = errors.Count < 5 ? errors.Count : 5;
                for (int i = 0; i < show; i++)
                {
                    sb.Append("  ").AppendLine(errors[i]);
                }
            }

            return sb.ToString();
        }

        private static string LevelName(LevelId level)
        {
            switch (level)
            {
                case LevelId.Level1:
                    return "关一(Level1)";
                case LevelId.Level2:
                    return "关二(Level2)";
                default:
                    return "支线关(SideQuest)";
            }
        }

        // ---------------------------------------------------------------- 小工具

        /// <summary>编辑器下 Destroy 不生效，必须用 DestroyImmediate。</summary>
        private static void DestroySafely(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
