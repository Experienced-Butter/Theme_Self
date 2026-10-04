using CardGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 「卡牌战斗」模块的一键入口。
    ///
    ///   CardGame/进入卡牌战斗        —— 创建模块对象并直接进入 Play，开箱即看
    ///   CardGame/创建卡牌战斗对象    —— 只创建对象（自己按 Play）
    /// </summary>
    public static class CardBattleMenu
    {
        private const string RootObjectName = "CardBattle";

        [MenuItem("CardGame/进入卡牌战斗", false, 60)]
        public static void EnterBattle()
        {
            CardBattleModule module = EnsureModule();
            PersistScene();
            Debug.Log("[CardGame][Editor] 卡牌战斗模块已就绪（关卡 " + module.level + "），正在进入 Play…");
            EditorApplication.isPlaying = true;
        }

        [MenuItem("CardGame/创建卡牌战斗对象", false, 61)]
        public static void CreateModuleObject()
        {
            CardBattleModule module = EnsureModule();
            PersistScene();
            Debug.Log("[CardGame][Editor] 卡牌战斗对象已创建（" + RootObjectName + "，关卡 " + module.level + "）。按 Play 进入战斗。");
        }

        private static CardBattleModule EnsureModule()
        {
            CardBattleModule existing = FindModule();
            if (existing != null)
            {
                return existing;
            }
            GameObject root = new GameObject(RootObjectName);
            CardBattleModule module = root.AddComponent<CardBattleModule>();
            module.autoEnterOnPlay = true;
            return module;
        }

        private static CardBattleModule FindModule()
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
                CardBattleModule found = roots[i].GetComponent<CardBattleModule>();
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
