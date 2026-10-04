using CardGame.Tests;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 「无鼠标跑 UI 交互自检」的编辑器入口。
    ///
    ///   CardGame/运行 UI 交互自检        —— 在当前场景创建自检运行器对象并**直接进入 Play**：
    ///                                      运行器会 Launch 卡牌战斗、等入场时序走完，然后用手造的
    ///                                      PointerEventData 驱动 CardWidget 的三个鼠标事件，断言
    ///                                      「抓起 / 掷出 / 融合 / 无交互 / 遮罩层级 / 投影判定」。
    ///                                      结果在 Console 里：
    ///                                        CardInteractionTests: ALL PASS (N checks)
    ///                                        或逐条 FAIL（带期望 vs 实测）+ FAIL (M failures)
    ///   CardGame/移除 UI 交互自检运行器   —— 把场景里遗留的运行器对象删掉（跑完 Play 后如果不想留）。
    ///
    /// 为什么需要菜单：MCP 不能模拟鼠标点击，编辑器里的「拖牌」这条路径没有任何真鼠标可用，
    /// 只能靠代码构造指针事件把判定链走到底。
    ///
    /// 与其它 CardGame 菜单的区别：本菜单**故意不保存场景**（不调 EditorSceneManager.SaveScene /
    /// MarkSceneDirty）。运行器是一次性测试脚手架，存进场景后每次 Play 都会自动跑一遍，
    /// 会给正常验收带来噪音；需要清理时用「移除 UI 交互自检运行器」。
    /// </summary>
    public static class CardInteractionTestMenu
    {
        private const string RunnerObjectName = "CardInteractionTestRunner";

        [MenuItem("CardGame/运行 UI 交互自检", false, 70)]
        public static void RunInteractionTests()
        {
            CardInteractionTestRunner runner = EnsureRunner();
            if (runner == null)
            {
                // 响亮失败：绝不静默进 Play 却没有运行器（那样控制台里什么都没有，最容易被误读成「跑了但没输出」）。
                Debug.LogError("[CardGame][Editor] UI 交互自检运行器组件挂载失败，**未进入 Play**。"
                               + "请先确认 Assets\\CardGame\\Tests\\CardInteractionTests.cs 已同步进 Unity 且编译通过"
                               + "（本菜单依赖那里的 CardInteractionTestRunner 这个 MonoBehaviour；"
                               + "同步命令：powershell -File tools\\sync-cardgame.ps1）。");
                return;
            }

            Debug.Log("[CardGame][Editor] UI 交互自检运行器已就绪（对象 " + runner.gameObject.name +
                      "，跑完自动销毁）。正在进入 Play…");
            EditorApplication.isPlaying = true;
        }

        [MenuItem("CardGame/移除 UI 交互自检运行器", false, 71)]
        public static void RemoveRunner()
        {
            GameObject runner = FindRunnerObject();
            if (runner == null)
            {
                Debug.Log("[CardGame][Editor] 场景里没有 UI 交互自检运行器。");
                return;
            }

            Object.DestroyImmediate(runner);
            Debug.Log("[CardGame][Editor] 已移除 UI 交互自检运行器（" + RunnerObjectName + "）。");
        }

        private static CardInteractionTestRunner EnsureRunner()
        {
            GameObject root = FindRunnerObject();
            if (root == null)
            {
                root = new GameObject(RunnerObjectName);
            }

            CardInteractionTestRunner runner = root.GetComponent<CardInteractionTestRunner>();
            if (runner == null)
            {
                runner = root.AddComponent<CardInteractionTestRunner>();
            }

            return runner;
        }

        private static GameObject FindRunnerObject()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                if (roots[i].GetComponent<CardInteractionTestRunner>() != null)
                {
                    return roots[i];
                }
            }

            return null;
        }
    }
}
