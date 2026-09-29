using CardGame.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 对局演示的一键入口。
    ///
    /// `CardGame/演示一局对局` 会创建演示对象并**直接进入 Play**，点一下就能看到整局对局动画；
    /// `CardGame/创建对局演示对象` 只创建对象（自己按 Play）。
    /// `CardGame/重新取景（卡牌一览）` 只把相机摆回卡墙正前方，不重建卡墙 ——
    /// 便于「在 Game 视图里右键拖动视角之后」一键复位。
    /// </summary>
    public static class BattleDemoMenu
    {
        private const string RootObjectName = "BattleDemo";

        [MenuItem("CardGame/演示一局对局", false, 50)]
        public static void PlayBattleDemo()
        {
            EnsureDemoObject();
            PersistScene();
            Debug.Log("[CardGame][Editor] 对局演示对象已就绪，正在进入 Play 模式…");
            EditorApplication.isPlaying = true;
        }

        [MenuItem("CardGame/创建对局演示对象", false, 51)]
        public static void CreateBattleDemoObject()
        {
            BattleDemoComponent demo = EnsureDemoObject();
            PersistScene();
            Debug.Log("[CardGame][Editor] 对局演示对象已创建（" + RootObjectName +
                      "，关卡 " + demo.level + "，种子 " + demo.seed + "，间隔 " + demo.stepDelay.ToString("0.0") + "s）。按 Play 开始。");
        }

        [MenuItem("CardGame/重新取景（卡牌一览）", false, 52)]
        public static void ReframeGallery()
        {
            CardGalleryComponent gallery = FindGallery();
            if (gallery == null)
            {
                Debug.Log("[CardGame][Editor] 场景里没有卡牌一览对象，先执行「CardGame/展示全部卡牌」。");
                return;
            }
            gallery.FrameCamera();
            PersistScene();
            Debug.Log("[CardGame][Editor] 相机已重新对准卡墙（若刚才在 Game 视图里拖动过视角，现在应已复位）。");
        }

        private static BattleDemoComponent EnsureDemoObject()
        {
            BattleDemoComponent existing = FindDemo();
            if (existing != null)
            {
                return existing;
            }
            GameObject root = new GameObject(RootObjectName);
            return root.AddComponent<BattleDemoComponent>();
        }

        private static BattleDemoComponent FindDemo()
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
                BattleDemoComponent found = roots[i].GetComponent<BattleDemoComponent>();
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static CardGalleryComponent FindGallery()
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
                CardGalleryComponent found = roots[i].GetComponent<CardGalleryComponent>();
                if (found != null)
                {
                    return found;
                }
            }
            return null;
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
