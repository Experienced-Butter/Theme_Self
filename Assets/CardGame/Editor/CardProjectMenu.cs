using System.Collections.Generic;
using CardGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 验收前的项目整理与运行设置。
    ///
    ///   CardGame/整理/移除旧演示对象        —— 删掉早期「固定套路演示」遗留在场景里的对象
    ///   CardGame/整理/确保按 Play 即开战     —— 场景里必须有且只有一个 CardBattle（autoEnterOnPlay）
    ///   CardGame/整理/允许后台运行          —— 打开 runInBackground，窗口失焦时 Play 也继续推进
    ///   CardGame/整理/一键准备验收          —— 上面三件一起做，然后保存场景
    ///
    /// 「旧演示」指 3D 阶段的固定套路展示：CardGallery（全部卡面平铺）、
    /// BattleDemo（写死的自动对局）以及它们自动补出来的多个 CardGallery_EventSystem。
    /// 现在唯一的入口是 2D 的 CardBattleModule（挂 CardBattle 对象），不再需要这些对象。
    /// </summary>
    public static class CardProjectMenu
    {
        /// <summary>演示对象的根名字（完全匹配）。</summary>
        private static readonly string[] DemoRootNames =
        {
            "CardGallery", "BattleDemo", "CardGallery_EventSystem"
        };

        /// <summary>演示对象的根名前缀（前缀匹配，兜住 CardGallery_UI / CardGallery_EventSystem(1) 之类）。</summary>
        private static readonly string[] DemoRootPrefixes =
        {
            "CardGallery", "BattleDemo"
        };

        private const string BattleObjectName = "CardBattle";

        [MenuItem("CardGame/整理/移除旧演示对象", false, 200)]
        public static void RemoveDemoObjects()
        {
            int removed = RemoveDemoRoots();
            PersistScene();
            Debug.Log("[CardGame][Editor] 旧演示对象清理完成：删除 " + removed + " 个，剩余 EventSystem "
                      + CountEventSystems() + " 个。");
        }

        [MenuItem("CardGame/整理/确保按 Play 即开战", false, 201)]
        public static void EnsureBattleOnPlay()
        {
            CardBattleModule module = EnsureBattleModule();
            PersistScene();
            Debug.Log("[CardGame][Editor] 已就绪：" + BattleObjectName + " 对象存在，autoEnterOnPlay = "
                      + module.autoEnterOnPlay + "（关卡 " + module.level + "）。点 Play 即开始一轮卡牌战斗。");
        }

        [MenuItem("CardGame/整理/允许后台运行", false, 202)]
        public static void EnableRunInBackground()
        {
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[CardGame][Editor] runInBackground = true：编辑器窗口失焦时 Play 仍会继续推进（"
                      + "否则协程会停在原地，自动化自检会卡住）。");
        }

        [MenuItem("CardGame/整理/一键准备验收", false, 1)]
        public static void PrepareForAcceptance()
        {
            // 顺序有讲究：先清掉自检脚手架，再清演示对象，最后才 SaveScene。
            // 「运行 UI 交互自检」是故意不保存场景的（自检运行器是一次性脚手架），
            // 但编辑器里那个对象会一直留在内存里的场景中 —— 只要这里存一次盘，
            // 它就会被写进 SampleScene，之后每次 Play 都会自动重跑自检，给验收添噪音。
            int runners = RemoveTestRunners();
            int removed = RemoveDemoRoots();
            CardBattleModule module = EnsureBattleModule();
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            PersistScene();
            Debug.Log("[CardGame][Editor] 验收环境已准备好：移除自检运行器 " + runners + " 个；"
                      + "移除旧演示对象 " + removed + " 个；"
                      + BattleObjectName + "（autoEnterOnPlay = " + module.autoEnterOnPlay + "，关卡 " + module.level + "）；"
                      + "runInBackground = " + PlayerSettings.runInBackground + "；场景已保存。"
                      + "现在按 Play 即开始一轮卡牌战斗。");
        }

        // ---- 实现 ----

        /// <summary>删掉场景里遗留的 UI 交互自检运行器（它不该被存进场景）。</summary>
        private static int RemoveTestRunners()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return 0;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            List<GameObject> doomed = new List<GameObject>();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                {
                    continue;
                }
                if (root.GetComponent<CardGame.Tests.CardInteractionTestRunner>() != null)
                {
                    doomed.Add(root);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                Undo.DestroyObjectImmediate(doomed[i]);
            }
            return doomed.Count;
        }

        private static int RemoveDemoRoots()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return 0;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            List<GameObject> doomed = new List<GameObject>();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                {
                    continue;
                }
                if (IsDemoRoot(root.name))
                {
                    doomed.Add(root);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                Undo.DestroyObjectImmediate(doomed[i]);
            }
            return doomed.Count;
        }

        private static bool IsDemoRoot(string name)
        {
            for (int i = 0; i < DemoRootNames.Length; i++)
            {
                if (name == DemoRootNames[i])
                {
                    return true;
                }
            }
            for (int i = 0; i < DemoRootPrefixes.Length; i++)
            {
                if (name.StartsWith(DemoRootPrefixes[i], System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountEventSystems()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return 0;
            }
            GameObject[] roots = scene.GetRootGameObjects();
            int count = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].GetComponent<UnityEngine.EventSystems.EventSystem>() != null)
                {
                    count++;
                }
            }
            return count;
        }

        private static CardBattleModule EnsureBattleModule()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] == null)
                    {
                        continue;
                    }
                    CardBattleModule found = roots[i].GetComponent<CardBattleModule>();
                    if (found != null)
                    {
                        found.autoEnterOnPlay = true;
                        return found;
                    }
                }
            }

            GameObject go = new GameObject(BattleObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Create CardBattle");
            CardBattleModule module = go.AddComponent<CardBattleModule>();
            module.autoEnterOnPlay = true;
            return module;
        }

        private static void PersistScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.SaveScene(scene);
            }
        }
    }
}
