// 归属：monsters（ARCHITECTURE.md 第 10 节、第 12 节）。Editor/ 目录下允许引用 UnityEditor。
// 菜单：CardGame/生成模板实体与控制者、CardGame/运行一局模拟。
using System.Collections.Generic;
using CardGame.Bootstrap;
using CardGame.Controllers;
using CardGame.Core;
using CardGame.Templates;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 编辑器场景装配器（ARCHITECTURE 第 10 节）。
    /// 「生成模板实体与控制者」**只创建缺失项**：已有对象一律复用，绝不覆盖用户调整过的字段
    /// （判断逻辑在 SceneTemplateBuilder 内部，编辑器只负责建对象、登记 Undo、标脏保存）。
    /// </summary>
    public static class CardGameSceneBuilder
    {
        private const string BuildObjectsLabel = "CardGame 生成模板与控制者";
        private const string SimulationLevelArg = "支线关";

        /// <summary>在当前场景创建缺失的模板实体与控制者，并标脏保存。</summary>
        [MenuItem("CardGame/生成模板实体与控制者", false, 10)]
        public static void GenerateTemplatesAndController()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[CardGame][Editor] 当前没有已加载的场景，已中止。");
                return;
            }

            // 记录改动前的状态，用于报告「已创建 / 已存在」并登记 Undo。
            bool hadPlayerTemplate = FindFirstInScene<PlayerTemplate>() != null;
            int cardsBefore = FindAllInScene<CardTemplate>().Count;
            bool hadAttackerTemplate = FindMonsterTemplate(MonsterKind.Attacker) != null;
            bool hadSupportTemplate = FindMonsterTemplate(MonsterKind.Support) != null;
            bool hadController = FindFirstInScene<GameController>() != null;
            HashSet<Transform> transformsBefore = CollectTransforms(scene);

            GameObject playerTemplate = SceneTemplateBuilder.BuildPlayerTemplate(null);
            GameObject cardLibrary = SceneTemplateBuilder.BuildCardLibrary(null);
            GameObject attackerTemplate = SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Attacker);
            GameObject supportTemplate = SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Support);
            GameObject controllerObject = SceneTemplateBuilder.BuildController(null);

            int cardsAfter = FindAllInScene<CardTemplate>().Count;
            int createdObjects = RegisterCreatedObjects(scene, transformsBefore);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = false;
            if (!string.IsNullOrEmpty(scene.path))
            {
                saved = EditorSceneManager.SaveScene(scene);
            }

            Debug.Log(
                "[CardGame][Editor] ===== 场景装配完成 =====\n" +
                "玩家模板：" + Describe(hadPlayerTemplate, playerTemplate) + "\n" +
                "卡牌库：" + DescribeCardLibrary(cardsBefore, cardsAfter, cardLibrary) + "\n" +
                "怪1 模板（攻击型）：" + Describe(hadAttackerTemplate, attackerTemplate) + "\n" +
                "怪2 模板（辅助型）：" + Describe(hadSupportTemplate, supportTemplate) + "\n" +
                "控制者：" + Describe(hadController, controllerObject) + "\n" +
                "本次新建 GameObject 数量：" + createdObjects + "\n" +
                "场景：" + (saved ? "已标脏并保存" : "已标脏（场景没有磁盘路径，未保存）"));

            if (controllerObject != null)
            {
                Selection.activeGameObject = controllerObject;
            }
        }

        /// <summary>调用无头模拟跑一局（对齐 RULES 第七节的模拟口径）。</summary>
        [MenuItem("CardGame/运行一局模拟", false, 20)]
        public static void RunOneSimulation()
        {
            // 无头模拟需要场景里的模板实体与控制者，缺什么补什么（只创建缺失项）。
            EnsureSceneObjects();

            Debug.Log("[CardGame][Editor] 开始无头模拟：1 局 / " + SimulationLevelArg + " / seed 0。");

            string report = CardGame.Tests.SimulationHarness.Run(1, LevelId.SideQuest, 0);

            Debug.Log("[CardGame][Editor] ===== 模拟结果（1 局） =====\n" + report);
        }

        /// <summary>
        /// 运行规则自检（ARCHITECTURE 第 11 节 RuleConformanceChecks，验收口径见第 10 节）：
        /// 逐条核对 RULES.md 的规则常量、11 种卡数值、伤害公式、怪物出招顺序与关键签名覆盖率。
        /// 失败项逐条以 error 级打到控制台（便于按 error 过滤）；全部通过时打印契约指定字符串
        /// "RuleConformanceChecks: ALL PASS"。本方法不抛异常、不弹对话框，可安全用于批处理与菜单调用。
        /// </summary>
        [MenuItem("CardGame/运行规则自检", false, 30)]
        public static void RunRuleSelfCheck()
        {
            List<string> failures;
            string report;

            try
            {
                report = CardGame.Tests.RuleConformanceChecks.Report();    // 含 PASS/FAIL 明细与统计的完整报告
                failures = CardGame.Tests.RuleConformanceChecks.RunAll();  // 纯函数，返回失败项清单
            }
            catch (System.Exception exception)
            {
                // 自检本身出错也不能把异常抛回 Unity 菜单调用方（否则批处理/execute_menu_item 会中断）。
                Debug.LogError("[CardGame][Editor] RuleConformanceChecks: 自检执行异常。" + exception);
                return;
            }

            if (failures == null || failures.Count == 0)
            {
                Debug.Log("RuleConformanceChecks: ALL PASS (0 failures)\n" + report);
                return;
            }

            Debug.LogError("[CardGame][Editor] RuleConformanceChecks: FAIL (" + failures.Count + " failures)");
            for (int i = 0; i < failures.Count; i++)
            {
                Debug.LogError("[CardGame][Editor] 规则自检失败项：" + failures[i]);
            }

            Debug.Log("[CardGame][Editor] 规则自检完整报告：\n" + report);
        }

        // 与 GameBootstrap 的运行时补齐同源：Build* 都是幂等的「只创建缺失项」。
        private static void EnsureSceneObjects()
        {
            SceneTemplateBuilder.BuildPlayerTemplate(null);
            SceneTemplateBuilder.BuildCardLibrary(null);
            SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Attacker);
            SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Support);
            SceneTemplateBuilder.BuildController(null);
        }

        // 只给「本次真正新建」的对象登记 Undo：新建层级的根结点登记一次即可，
        // 子结点会随父结点一起被撤销，重复登记会导致撤销时二次销毁。
        private static int RegisterCreatedObjects(Scene scene, HashSet<Transform> transformsBefore)
        {
            HashSet<Transform> transformsAfter = CollectTransforms(scene);
            int created = 0;

            foreach (Transform transform in transformsAfter)
            {
                if (transform == null || transformsBefore.Contains(transform))
                {
                    continue;
                }

                bool parentIsNew = transform.parent != null && !transformsBefore.Contains(transform.parent);
                if (parentIsNew)
                {
                    continue;
                }

                Undo.RegisterCreatedObjectUndo(transform.gameObject, BuildObjectsLabel);
                created++;
            }

            return created;
        }

        private static HashSet<Transform> CollectTransforms(Scene scene)
        {
            HashSet<Transform> result = new HashSet<Transform>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return result;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length; j++)
                {
                    if (all[j] != null)
                    {
                        result.Add(all[j]);
                    }
                }
            }

            return result;
        }

        private static string Describe(bool existedBefore, GameObject created)
        {
            if (created == null)
            {
                return "创建失败";
            }

            return existedBefore
                ? "已存在，未改动（" + created.name + "）"
                : "已创建（" + created.name + "）";
        }

        private static string DescribeCardLibrary(int cardsBefore, int cardsAfter, GameObject libraryRoot)
        {
            if (libraryRoot == null)
            {
                return "创建失败";
            }

            if (cardsBefore == 0)
            {
                return "已创建（新增 " + cardsAfter + " 个卡牌模板：" + libraryRoot.name + "）";
            }

            if (cardsAfter > cardsBefore)
            {
                return "补齐缺失种类（新增 " + (cardsAfter - cardsBefore) + " 个，共 " + cardsAfter + " 个）";
            }

            return "已存在，未改动（共 " + cardsAfter + " 个卡牌模板）";
        }

        private static List<T> FindAllInScene<T>() where T : Component
        {
            List<T> result = new List<T>();
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return result;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                result.AddRange(roots[i].GetComponentsInChildren<T>(true));
            }

            return result;
        }

        private static T FindFirstInScene<T>() where T : Component
        {
            List<T> all = FindAllInScene<T>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null)
                {
                    return all[i];
                }
            }

            return null;
        }

        private static MonsterTemplate FindMonsterTemplate(MonsterKind kind)
        {
            List<MonsterTemplate> all = FindAllInScene<MonsterTemplate>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].kind == kind)
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
