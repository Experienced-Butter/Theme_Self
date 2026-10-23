using System.IO;
using CardGame.Config;
using CardGame.Controllers;
using UnityEditor;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 三套关卡美术配置的创建 / 修复工具。
    ///
    ///   CardGame/美术/创建或修复三套关卡配置   —— 幂等：缺什么补什么，已经填好的美术槽位一律不动
    ///   CardGame/美术/在资源管理器里打开美术目录 —— 直接跳到放图的地方
    ///
    /// 为什么要有这个菜单：配置资产（ScriptableObject）必须由 Unity 生成才带有正确的 YAML 与 GUID。
    /// 菜单是幂等的，所以「别人 clone 仓库后发现缺配置」只要点一下就能补齐；
    /// 反过来，重复点也不会把美术已经拖进去的图清掉（只补空槽）。
    /// </summary>
    public static class CardThemeMenu
    {
        private const string ResourcesRoot = "Assets/CardGame/Resources";
        private const string ThemeFolder = ResourcesRoot + "/CardGameThemes";
        private const string ArtRoot = "Assets/CardGame/Art";

        /// <summary>BattleTheme 里 proceduralBackgroundColor 的 C# 初始值（用于判断「还是默认值」）。</summary>
        private static readonly Color DefaultProceduralColor = new Color(0.09f, 0.11f, 0.17f, 1f);

        private static bool ColorsEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g)
                   && Mathf.Approximately(a.b, b.b) && Mathf.Approximately(a.a, b.a);
        }

        [MenuItem("CardGame/美术/创建或修复三套关卡配置", false, 300)]
        public static void CreateOrRepairThemes()
        {
            EnsureFolder("Assets/CardGame", "Resources");
            EnsureFolder(ResourcesRoot, "CardGameThemes");
            EnsureFolder("Assets/CardGame", "Art");

            int created = 0;
            int repaired = 0;

            EnsureTheme(LevelId.Level1, "关一", new Color(0.09f, 0.11f, 0.17f, 1f), ref created, ref repaired);
            EnsureTheme(LevelId.Level2, "关二", new Color(0.13f, 0.09f, 0.18f, 1f), ref created, ref repaired);
            EnsureTheme(LevelId.SideQuest, "支线关", new Color(0.07f, 0.15f, 0.14f, 1f), ref created, ref repaired);

            int folders = EnsureArtFolders();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[CardGame][Editor] 三套关卡配置就绪：新建 " + created + " 个、补全 " + repaired
                      + " 个空槽；美术目录已确认（新增 " + folders + " 个）。"
                      + "配置位置：" + ThemeFolder + "；放图位置：" + ArtRoot + "/Level1|Level2|SideQuest/");
        }

        [MenuItem("CardGame/美术/在资源管理器里打开美术目录", false, 301)]
        public static void RevealArtFolder()
        {
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "../" + ArtRoot));
            if (Directory.Exists(full))
            {
                EditorUtility.RevealInFinder(full);
            }
            else
            {
                Debug.LogWarning("[CardGame][Editor] 美术目录还不存在，先点「创建或修复三套关卡配置」。");
            }
        }

        // ---- 配置 ----

        private static void EnsureTheme(LevelId level, string displayName, Color fallbackBackground,
            ref int created, ref int repaired)
        {
            string path = ThemeFolder + "/Theme_" + level + ".asset";
            BattleTheme theme = AssetDatabase.LoadAssetAtPath<BattleTheme>(path);

            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<BattleTheme>();
                theme.level = level;
                theme.displayName = displayName;
                theme.backgroundTint = Color.white;                 // 背景图默认不变色
                theme.proceduralBackgroundColor = fallbackBackground; // 没图时的纯色底
                theme.playerBarColor = new Color(0.22f, 0.52f, 0.95f, 1f);   // 玩家蓝
                theme.monsterBarColor = new Color(0.85f, 0.32f, 0.28f, 1f);  // 敌人红
                theme.playerSize = new Vector2(260f, 330f);
                theme.monsterSize = new Vector2(220f, 300f);
                theme.monsterSprites = new Sprite[BattleTheme.MaxMonsters];
                theme.monsterTints = new Color[BattleTheme.MaxMonsters];
                for (int i = 0; i < theme.monsterTints.Length; i++)
                {
                    theme.monsterTints[i] = Color.white;   // 每只怪默认不改色
                }
                theme.keepProceduralDecor = true;
                theme.notes = "在 Inspector 里把图片拖进 ②背景 / ③玩家 / ④怪物 的槽位即可。";

                AssetDatabase.CreateAsset(theme, path);
                created++;
                return;
            }

            // 已存在：只补「看起来还是空的」字段，绝不覆盖美术已经填进去的内容
            bool dirty = false;

            if (theme.level != level)
            {
                theme.level = level;
                dirty = true;
            }
            if (string.IsNullOrEmpty(theme.displayName))
            {
                theme.displayName = displayName;
                dirty = true;
            }
            if (theme.playerSize.x < 1f || theme.playerSize.y < 1f)
            {
                theme.playerSize = new Vector2(260f, 330f);
                dirty = true;
            }
            if (theme.monsterSize.x < 1f || theme.monsterSize.y < 1f)
            {
                theme.monsterSize = new Vector2(220f, 300f);
                dirty = true;
            }
            if (theme.playerBarColor.a <= 0f)
            {
                theme.playerBarColor = new Color(0.22f, 0.52f, 0.95f, 1f);
                dirty = true;
            }
            if (theme.monsterBarColor.a <= 0f)
            {
                theme.monsterBarColor = new Color(0.85f, 0.32f, 0.28f, 1f);
                dirty = true;
            }
            if (theme.backgroundTint.a <= 0f)
            {
                theme.backgroundTint = Color.white;
                dirty = true;
            }

            // 迁移（旧 schema → 新 schema）：早期版本把「无图时的纯色底」存在 backgroundTint 里，
            // 结果一接背景图就直接把美术图乘成近乎全黑。
            // 判据不能用「新字段是否为空」—— Unity 在反序列化缺失字段时会填 **C# 初始值**
            // （所以 proceduralBackgroundColor 的 alpha 一上来就是 1），那个判据永远不成立（真机踩到过）。
            // 真正可靠的信号是：**已经有背景图，但 tint 不是白色** ⇒ 一定是旧 schema 留在 tint 里的纯色。
            bool tintIsWhite = Mathf.Approximately(theme.backgroundTint.r, 1f)
                               && Mathf.Approximately(theme.backgroundTint.g, 1f)
                               && Mathf.Approximately(theme.backgroundTint.b, 1f);
            if (theme.backgroundTint.a > 0f && !tintIsWhite)
            {
                if (theme.proceduralBackgroundColor.a <= 0f
                    || ColorsEqual(theme.proceduralBackgroundColor, DefaultProceduralColor))
                {
                    theme.proceduralBackgroundColor = new Color(theme.backgroundTint.r,
                        theme.backgroundTint.g, theme.backgroundTint.b, 1f);
                }
                theme.backgroundTint = Color.white;
                dirty = true;
            }
            if (theme.proceduralBackgroundColor.a <= 0f)
            {
                theme.proceduralBackgroundColor = new Color(fallbackBackground.r, fallbackBackground.g,
                    fallbackBackground.b, 1f);
                dirty = true;
            }
            if (theme.monsterSprites == null || theme.monsterSprites.Length != BattleTheme.MaxMonsters)
            {
                Sprite[] fixedArray = new Sprite[BattleTheme.MaxMonsters];
                if (theme.monsterSprites != null)
                {
                    for (int i = 0; i < fixedArray.Length && i < theme.monsterSprites.Length; i++)
                    {
                        fixedArray[i] = theme.monsterSprites[i];
                    }
                }
                theme.monsterSprites = fixedArray;
                dirty = true;
            }
            if (theme.monsterTints == null || theme.monsterTints.Length != BattleTheme.MaxMonsters)
            {
                Color[] fixedTints = new Color[BattleTheme.MaxMonsters];
                for (int i = 0; i < fixedTints.Length; i++)
                {
                    fixedTints[i] = theme.monsterTints != null && i < theme.monsterTints.Length
                        ? theme.monsterTints[i]
                        : Color.white;
                }
                theme.monsterTints = fixedTints;
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(theme);
                repaired++;
            }
        }

        // ---- 目录 ----

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        /// <summary>
        /// 建「放图的位置」。每关三个子目录 + 一个 README。
        ///
        /// 为什么要放 README：**Git 不跟踪空目录**。没有实际文件，别人 clone 下来这些目录会消失，
        /// 于是「教程里说的目录」在他们机器上根本不存在。所以每个目录里都留一个占位文件。
        /// </summary>
        private static int EnsureArtFolders()
        {
            int created = 0;

            EnsureFolder("Assets/CardGame", "Art");
            WriteIfMissing(ArtRoot + "/README.txt", ArtReadme());
            created++;

            LevelId[] levels = { LevelId.Level1, LevelId.Level2, LevelId.SideQuest };
            string[] levelFolders = { "Level1", "Level2", "SideQuest" };
            string[] levelNames = { "关一", "关二", "支线关" };

            for (int i = 0; i < levels.Length; i++)
            {
                string root = ArtRoot + "/" + levelFolders[i];
                EnsureFolder(ArtRoot, levelFolders[i]);
                created++;

                string[] subs = { "Background", "Player", "Monsters" };
                for (int s = 0; s < subs.Length; s++)
                {
                    EnsureFolder(root, subs[s]);
                    WriteIfMissing(root + "/" + subs[s] + "/README.txt",
                        SubFolderReadme(levelNames[i], subs[s]));
                    created++;
                }
            }

            return created;
        }

        private static void WriteIfMissing(string assetPath, string content)
        {
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "../" + assetPath));
            if (File.Exists(full))
            {
                return;
            }

            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(full, content, new System.Text.UTF8Encoding(false));
        }

        private static string ArtReadme()
        {
            return string.Join("\n", new[]
            {
                "卡牌战斗 · 美术资源放置说明",
                "",
                "目录结构（每关一套，互不影响）：",
                "  Level1\\     关一",
                "  Level2\\     关二",
                "  SideQuest\\  支线关",
                "",
                "每关下面三个子目录：",
                "  Background\\  背景图   建议 1920×1080（16:9），会铺满全屏",
                "  Player\\      玩家形象  建议 512×512 正方形",
                "  Monsters\\    怪物形象  建议 512×512 正方形",
                "",
                "放好图以后要做的事：",
                "  1. 选中 Assets\\CardGame\\Resources\\CardGameThemes 下对应关卡的 Theme_*.asset",
                "  2. 把图片从 Project 窗口直接拖进 Inspector 里对应的槽位",
                "  3. 保存（Ctrl+S）→ 提交时务必连 .meta 文件一起提交",
                "",
                "重要：",
                "  · 图片的 Import 设置请用 Sprite (2D and UI)。Texture Type 不对就不能拖进 Sprite 槽位。",
                "  · 不要移动／重命名已经提交过的资源，那会改变 GUID，导致别人那边的引用全部丢失。",
                "  · 槽位留空是允许的：留空会自动用程序生成的占位外观（纯色背景 + 圆形头像）。",
                ""
            });
        }

        private static string SubFolderReadme(string levelName, string kind)
        {
            string hint;
            switch (kind)
            {
                case "Background":
                    hint = "背景图放这里。建议 1920×1080（16:9）。Import 设置：Sprite (2D and UI)。";
                    break;
                case "Player":
                    hint = "玩家形象放这里。建议 512×512 正方形，透明背景 PNG。";
                    break;
                default:
                    hint = "怪物形象放这里。建议 512×512 正方形；按出场顺序放，最多 3 只。";
                    break;
            }

            return levelName + " · " + kind + "\n\n" + hint
                   + "\n\n这个 README 文件本身是为了让 Git 能保留这个空目录，别删。\n";
        }
    }
}
