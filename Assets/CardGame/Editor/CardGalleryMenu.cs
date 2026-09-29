using CardGame.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 卡牌展示的一键入口（编辑器菜单 = 最直观的「按钮」）。
    ///
    /// 与「生成模板实体」那种必须按 Play 才生效的流程不同，这里的展示**在编辑态直接生成**，
    /// 点完菜单就能在 Scene / Game 视图里看到全部卡牌，不需要进入运行模式。
    ///
    /// 菜单：
    ///   CardGame/展示全部卡牌      一键生成并显示卡墙
    ///   CardGame/隐藏卡牌展示      只隐藏，保留已生成对象（再次点「展示」即可恢复）
    ///   CardGame/清除卡牌展示      删除 CardGallery 对象，彻底清理
    /// </summary>
    public static class CardGalleryMenu
    {
        private const string RootObjectName = "CardGallery";

        [MenuItem("CardGame/展示全部卡牌", false, 40)]
        public static void ShowGallery()
        {
            CardGalleryComponent gallery = EnsureGalleryObject();
            gallery.createOnScreenButton = true;
            gallery.ShowGallery();

            PersistScene();
            Debug.Log("[CardGame][Editor] 卡牌展示已生成：" + gallery.LastBuiltCardCount +
                      " 张（根对象 " + RootObjectName + "）。");
        }

        [MenuItem("CardGame/隐藏卡牌展示", false, 41)]
        public static void HideGallery()
        {
            CardGalleryComponent gallery = FindGalleryComponent();
            if (gallery == null)
            {
                Debug.Log("[CardGame][Editor] 当前场景没有卡牌展示对象，无需隐藏。");
                return;
            }
            gallery.HideGallery();
            PersistScene();
            Debug.Log("[CardGame][Editor] 卡牌展示已隐藏（对象保留，可直接再点「展示全部卡牌」）。");
        }

        [MenuItem("CardGame/清除卡牌展示", false, 42)]
        public static void ClearGallery()
        {
            CardGalleryComponent gallery = FindGalleryComponent();
            if (gallery == null)
            {
                Debug.Log("[CardGame][Editor] 当前场景没有卡牌展示对象，无需清除。");
                return;
            }
            Object.DestroyImmediate(gallery.gameObject);
            PersistScene();
            Debug.Log("[CardGame][Editor] 卡牌展示对象已删除，场景恢复干净。");
        }

        [MenuItem("CardGame/恢复相机取景", false, 43)]
        public static void RestoreCamera()
        {
            CardGalleryComponent gallery = FindGalleryComponent();
            if (gallery == null)
            {
                Debug.Log("[CardGame][Editor] 当前场景没有卡牌展示对象，没有可恢复的相机位姿。");
                return;
            }
            gallery.RestoreCamera();
            PersistScene();
            Debug.Log("[CardGame][Editor] 相机已恢复到展示之前的位置。");
        }

        /// <summary>找到场景里已有的 CardGallery，没有就创建一个。</summary>
        private static CardGalleryComponent EnsureGalleryObject()
        {
            CardGalleryComponent existing = FindGalleryComponent();
            if (existing != null)
            {
                return existing;
            }

            GameObject root = new GameObject(RootObjectName);
            CardGalleryComponent gallery = root.AddComponent<CardGalleryComponent>();
            gallery.autoShowOnStart = true;
            return gallery;
        }

        /// <summary>只在当前活动场景的根对象里找，避免误伤其他场景或预制体。</summary>
        private static CardGalleryComponent FindGalleryComponent()
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

        /// <summary>标脏并保存（未命名的场景只标脏，不弹保存对话框以免阻塞批处理调用）。</summary>
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
