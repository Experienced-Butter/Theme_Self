using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// **即插即用**：把图片丢进 `Assets/CardGame/Art/&lt;关卡&gt;/&lt;槽位&gt;/` 就直接生效，
    /// 不需要点任何菜单、不需要拖任何引用。
    ///
    /// 它做两件事：
    ///   1. <see cref="OnPreprocessTexture"/>（导入**之前**）：把图片的 Texture Type 设成 Sprite。
    ///      这一步很关键 —— 默认导入的 jpg/png 是 Default，永远拖不进 Sprite 槽位，
    ///      是「图放好了但用不了」最常见的原因。
    ///   2. <see cref="OnPostprocessAllAssets"/>（导入**之后**）：调
    ///      <see cref="CardArtAutoAssignMenu.AssignAll"/> 把图装进对应关卡的 Theme 配置。
    ///      用 <c>EditorApplication.delayCall</c> 延后一帧执行 —— 在导入回调里直接改资产不安全。
    ///
    /// 为什么不会死循环：装图只会改动 Theme_*.asset，而那些文件不在 Art 目录下，
    /// 所以不会再次触发本类。
    ///
    /// 约定了哪些目录才处理：只有 `Art/<Level1|Level2|SideQuest>/<Background|Player|Monsters>/`
    /// 下面的图片才会被理；放在别处的图一律不动。
    /// </summary>
    public class CardArtDropIn : AssetPostprocessor
    {
        /// <summary>只有这个前缀下的资源才归本类管。</summary>
        public const string ArtRoot = "Assets/CardGame/Art/";

        /// <summary>导入前统一改成 Sprite，顺带把透明通道与过滤模式设成适合 UI 的值。</summary>
        private void OnPreprocessTexture()
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith(ArtRoot))
            {
                return;
            }

            TextureImporter importer = assetImporter as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;

            string lower = assetPath.ToLowerInvariant();
            if (lower.EndsWith(".png") || lower.EndsWith(".tga") || lower.EndsWith(".psd"))
            {
                importer.alphaIsTransparency = true;
            }

            importer.filterMode = FilterMode.Bilinear;
        }

        /// <summary>导入后自动装配。只在「确实有 Art 下的资源被导入/移动/删除」时才动作。</summary>
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!HasArtChange(importedAssets) && !HasArtChange(deletedAssets)
                && !HasArtChange(movedAssets) && !HasArtChange(movedFromAssetPaths))
            {
                return;
            }

            // 进 Play 期间 Unity 不允许改资产；等退出后再装
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EditorApplication.delayCall += delegate
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }
                CardArtAutoAssignMenu.AssignAll(true);
            };
        }

        private static bool HasArtChange(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }
                if (!path.StartsWith(ArtRoot))
                {
                    continue;
                }
                if (path.EndsWith(".meta"))
                {
                    continue;   // meta 变化不算内容变化
                }
                if (path.EndsWith("README.txt"))
                {
                    continue;   // 占位文件不参与装图
                }
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 即插即用的**兜底保险**：即将进入 Play 之前，无条件再跑一遍装图。
    ///
    /// 为什么需要它：<see cref="AssetPostprocessor"/> 的 <c>delayCall</c> 依赖编辑器 update 循环的时序，
    /// 在「刚编译完 / 编辑器未聚焦 / 正在导入」这些情况下可能被推迟甚至丢弃（本项目实测踩到过：
    /// 图已经按约定放好、导入设置也对，但槽位仍是空的）。
    /// 而「按 Play」是美术必然会做的动作，挂在这里就能保证：
    ///   **把图丢进目录 → 按 Play → 一定生效**，不依赖任何菜单，也不依赖回调时序。
    /// </summary>
    [InitializeOnLoad]
    public static class CardArtDropInPlayHook
    {
        static CardArtDropInPlayHook()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // ExitingEditMode：还在编辑模式、马上要进 Play —— 此刻改资产是合法的
            if (state != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            CardArtAutoAssignMenu.AssignAll(true);
        }
    }
}
