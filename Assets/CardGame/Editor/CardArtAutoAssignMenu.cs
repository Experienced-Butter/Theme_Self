using System.Collections.Generic;
using System.IO;
using CardGame.Config;
using CardGame.Controllers;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 按「目录约定」把 Art 目录里的图自动装进三套关卡配置。
    ///
    ///   CardGame/美术/按目录自动装图（Art → Theme）   —— 幂等，可反复点
    ///
    /// 约定（一个目录只放一张图，目录里第一张 = 该槽位的图）：
    ///   Art/&lt;Level&gt;/Background/ → theme.backgroundSprite
    ///   Art/&lt;Level&gt;/Player/     → theme.playerSprite
    ///   Art/&lt;Level&gt;/Monsters/   → theme.monsterSprites[0..2]（按文件名排序）
    ///
    /// 顺带处理最容易把人卡住的问题：
    ///   1) 图片的 Texture Type 不是 Sprite ⇒ 根本拖不进 Sprite 槽位。这里会自动改成 Sprite 并重导入。
    ///   2) PNG 没勾 Alpha Is Transparency ⇒ 透明边缘会出现黑边。这里会勾上。
    ///
    /// **不**去动 maxTextureSize：背景按 1920×1080 全屏显示，2048 的最大尺寸已经超过屏幕像素，
    /// 再往上调只会白吃显存（4096×2304 的 RGBA 是 ~37MB），1080p 下看不出任何区别。
    /// 所以「图很大」不是问题，别为此改导入设置。
    ///
    /// 什么时候不该用这个菜单：你想手工挑图 / 一格里放多张。那就直接在 Inspector 里拖，本菜单不参与。
    /// </summary>
    public static class CardArtAutoAssignMenu
    {
        private const string ArtRoot = "Assets/CardGame/Art";
        private const string ThemeFolder = "Assets/CardGame/Resources/CardGameThemes";

        private static readonly LevelId[] Levels = { LevelId.Level1, LevelId.Level2, LevelId.SideQuest };
        private static readonly string[] LevelFolders = { "Level1", "Level2", "SideQuest" };

        [MenuItem("CardGame/美术/按目录自动装图（Art → Theme）", false, 302)]
        public static void AutoAssign()
        {
            AssignAll(false);
        }

        /// <summary>
        /// 把所有 Art 目录里的图按约定装进三套配置。
        ///
        /// 这个方法是「即插即用」的公共入口：<see cref="CardArtDropIn"/> 检测到有人往 Art 里
        /// 丢/换/删了图，就会自动调它 —— 所以正常使用**完全不需要点菜单**。菜单只是手动重跑一遍的保险。
        /// silent = true 时不打 Log（导入回调里安静一点）。
        /// </summary>
        public static void AssignAll(bool silent)
        {
            int assigned = 0;
            int fixedImport = 0;
            List<string> notes = new List<string>();

            for (int i = 0; i < Levels.Length; i++)
            {
                LevelId level = Levels[i];
                string folder = ArtRoot + "/" + LevelFolders[i];
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    notes.Add("跳过 " + LevelFolders[i] + "：目录不存在");
                    continue;
                }

                BattleTheme theme = AssetDatabase.LoadAssetAtPath<BattleTheme>(
                    ThemeFolder + "/Theme_" + level + ".asset");
                if (theme == null)
                {
                    notes.Add("跳过 " + LevelFolders[i] + "：缺少 Theme_" + level + "，先点「创建或修复三套关卡配置」");
                    continue;
                }

                bool dirty = false;

                // 背景
                Sprite background = FirstSpriteIn(folder + "/Background", true, ref fixedImport, notes, LevelFolders[i] + "/背景");
                if (background != null && theme.backgroundSprite != background)
                {
                    theme.backgroundSprite = background;
                    assigned++;
                    dirty = true;
                    notes.Add(LevelFolders[i] + " 背景 ← " + Path.GetFileName(AssetDatabase.GetAssetPath(background)));
                }
                else if (background == null && CameFrom(theme.backgroundSprite, folder + "/Background"))
                {
                    // 图被删了/挪走了：必须把槽位清空，否则会留下一条指向已不存在资源的悬空引用
                    theme.backgroundSprite = null;
                    assigned++;
                    dirty = true;
                    notes.Add(LevelFolders[i] + " 背景 ← （已清空：源文件不在了）");
                }

                // 玩家
                Sprite player = FirstSpriteIn(folder + "/Player", false, ref fixedImport, notes, LevelFolders[i] + "/玩家");
                if (player != null && theme.playerSprite != player)
                {
                    theme.playerSprite = player;
                    assigned++;
                    dirty = true;
                    notes.Add(LevelFolders[i] + " 玩家 ← " + Path.GetFileName(AssetDatabase.GetAssetPath(player)));
                }
                else if (player == null && CameFrom(theme.playerSprite, folder + "/Player"))
                {
                    theme.playerSprite = null;
                    assigned++;
                    dirty = true;
                    notes.Add(LevelFolders[i] + " 玩家 ← （已清空：源文件不在了）");
                }

                // 怪物：按文件名排序依次填 0..2
                List<Sprite> monsters = AllSpritesIn(folder + "/Monsters", 3, ref fixedImport, notes, LevelFolders[i] + "/怪物");
                if (theme.monsterSprites == null || theme.monsterSprites.Length != BattleTheme.MaxMonsters)
                {
                    Sprite[] padded = new Sprite[BattleTheme.MaxMonsters];
                    if (theme.monsterSprites != null)
                    {
                        for (int k = 0; k < padded.Length && k < theme.monsterSprites.Length; k++)
                        {
                            padded[k] = theme.monsterSprites[k];
                        }
                    }
                    theme.monsterSprites = padded;
                    dirty = true;
                }

                for (int m = 0; m < theme.monsterSprites.Length; m++)
                {
                    Sprite wanted = m < monsters.Count ? monsters[m] : null;
                    if (wanted != null)
                    {
                        if (theme.monsterSprites[m] != wanted)
                        {
                            theme.monsterSprites[m] = wanted;
                            assigned++;
                            dirty = true;
                            notes.Add(LevelFolders[i] + " 怪物[" + m + "] ← " +
                                      Path.GetFileName(AssetDatabase.GetAssetPath(wanted)));
                        }
                    }
                    else if (CameFrom(theme.monsterSprites[m], folder + "/Monsters"))
                    {
                        // 源文件不在了：清空，避免悬空引用
                        theme.monsterSprites[m] = null;
                        assigned++;
                        dirty = true;
                        notes.Add(LevelFolders[i] + " 怪物[" + m + "] ← （已清空：源文件不在了）");
                    }
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(theme);
                }
            }

            AssetDatabase.SaveAssets();

            if (!silent)
            {
                Debug.Log("[CardGame][Editor] 自动装图完成：装了 " + assigned + " 个槽位，修正导入设置 " + fixedImport
                          + " 张。\n" + string.Join("\n", notes.ToArray()));
            }
            else if (assigned > 0 || fixedImport > 0)
            {
                // 即插即用路径：安静但要有痕，方便别人从 Console 反查「我刚丢的图怎么被接上的」
                Debug.Log("[CardGame][Editor] 检测到美术文件变化，已自动接入 " + assigned + " 个槽位"
                          + (fixedImport > 0 ? "、修正导入设置 " + fixedImport + " 张" : "") + "。");
            }
        }

        // ---- 工具 ----

        /// <summary>
        /// 这个 Sprite 是不是来自指定目录（或它指向的资源已经不存在了）。
        /// 用来判断「现在这个槽位是不是由目录约定管着的」—— 只有这种情况下才允许自动清空，
        /// 避免把别人手工在 Inspector 里指派的、来自别处的图给清掉。
        /// </summary>
        private static bool CameFrom(Sprite sprite, string folder)
        {
            if (sprite == null)
            {
                return false;   // 本来就是空的，没什么可清
            }

            string path = AssetDatabase.GetAssetPath(sprite);
            if (string.IsNullOrEmpty(path))
            {
                return true;    // 资源已被删除：必须清，否则是悬空引用
            }

            return path.StartsWith(folder + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        private static Sprite FirstSpriteIn(string folder, bool isBackground, ref int fixedImport,
            List<string> notes, string label)
        {
            List<Sprite> found = AllSpritesIn(folder, 1, ref fixedImport, notes, label, isBackground);
            return found.Count > 0 ? found[0] : null;
        }

        /// <summary>取目录里前 maxCount 张 Sprite，按文件名排序（保证「第 1 只怪」稳定对应同一个文件）。</summary>
        private static List<Sprite> AllSpritesIn(string folder, int maxCount, ref int fixedImport,
            List<string> notes, string label, bool isBackground = false)
        {
            List<Sprite> result = new List<Sprite>();
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return result;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            List<string> paths = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }
                // 只认图片本身，不认子资源路径
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                {
                    continue;
                }
                paths.Add(path);
            }

            paths.Sort(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < paths.Count && result.Count < maxCount; i++)
            {
                string path = paths[i];

                if (EnsureSpriteImport(path, isBackground))
                {
                    fixedImport++;
                    notes.Add(label + " 修正导入设置：" + Path.GetFileName(path) + " → Texture Type = Sprite"
                              + (isBackground ? "，maxSize = 4096" : string.Empty));
                }

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    notes.Add(label + " 跳过（不是 Sprite，无法装入）: " + Path.GetFileName(path));
                    continue;
                }

                result.Add(sprite);
            }

            return result;
        }

        /// <summary>把 Texture Type 改成 Sprite（必要时补 alpha 透明与双线性过滤）。返回是否做过改动。</summary>
        private static bool EnsureSpriteImport(string path, bool isBackground)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            bool changed = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }
            if (!importer.alphaIsTransparency && path.ToLowerInvariant().EndsWith(".png"))
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }
            if (importer.filterMode != FilterMode.Bilinear)
            {
                importer.filterMode = FilterMode.Bilinear;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }

            return changed;
        }
    }
}
