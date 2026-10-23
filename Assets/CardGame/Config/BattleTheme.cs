using CardGame.Controllers;
using UnityEngine;

namespace CardGame.Config
{
    /// <summary>
    /// 一个战斗关卡的主题配置：**所有美术资源的插入位置都在这里**。
    ///
    /// 设计约束（为什么长这样）：
    ///   1. 它是一份 <see cref="ScriptableObject"/> 资产，落在 `Assets/CardGame/Resources/CardGameThemes/` 下，
    ///      随 GitHub 仓库一起同步 —— 别人 clone 下来就自带三份配置，不需要在场景里接线。
    ///   2. **每个美术槽位都可以留空**：留空时自动回退到程序生成的占位外观（纯色背景、圆形头像），
    ///      所以「还没做美术」和「美术已接入」两种状态都能正常跑，不会因为缺图而报错或白屏。
    ///   3. 尺寸 / 血条颜色也放在这里，这样每一关可以有不一样的角色大小与配色，
    ///      而尺寸必须在**创建实体形象之前**读到（内部布局是按尺寸算的），所以模块会在建 UI 之前先解析主题。
    ///
    /// 怎么改：Project 窗口里选中 Theme_Level1/2/SideQuest，在 Inspector 里把图片拖进对应槽位即可，
    /// 详细步骤见交给你的 Word 教程。
    /// </summary>
    [CreateAssetMenu(fileName = "Theme_Level1", menuName = "CardGame/战斗关卡主题配置", order = 0)]
    public class BattleTheme : ScriptableObject
    {
        public const int MaxMonsters = 3;

        [Header("① 关卡标识（由菜单自动填，一般不用改）")]
        [Tooltip("这份配置作用于哪一关。")]
        public LevelId level = LevelId.Level1;

        [Tooltip("给自己看的名字，比如「关一 · 荒野」。只影响 Inspector 里的可读性。")]
        public string displayName = "关一";

        [Header("② 背景")]
        [Tooltip("背景图。留空 = 用下面「无背景图时的纯色」当背景。建议 1920×1080（16:9）。")]
        public Sprite backgroundSprite;

        [Tooltip("背景图的着色。白 = 原图颜色；想整体压暗或调色相才改它。"
                 + "注意：它只作用于背景图，不会影响留空时的纯色背景。")]
        public Color backgroundTint = Color.white;

        [Tooltip("**没有背景图**时使用的纯色背景。填了背景图后这个值就不起作用了。")]
        public Color proceduralBackgroundColor = new Color(0.09f, 0.11f, 0.17f, 1f);

        [Tooltip("有背景图时，是否仍然画程序生成的上下色带与标题。背景图自带标题/装饰就关掉它。")]
        public bool keepProceduralDecor = true;

        [Header("③ 玩家")]
        [Tooltip("玩家形象。留空 = 用程序生成的圆形头像。建议正方形 512×512。")]
        public Sprite playerSprite;

        [Tooltip("玩家形象的着色（白 = 原色）。")]
        public Color playerTint = Color.white;

        [Tooltip("玩家形象的外框尺寸（宽, 高）。预留了名称/血条/HP 文本的空间，头像会自动缩进框内。")]
        public Vector2 playerSize = new Vector2(260f, 330f);

        [Tooltip("玩家血条颜色（需求约定：玩家蓝）。")]
        public Color playerBarColor = new Color(0.22f, 0.52f, 0.95f, 1f);

        [Header("④ 怪物")]
        [Tooltip("按**出场顺序**排列的怪物形象：第 1 个对应本关第 1 只出场的怪。留空 = 圆形头像。"
                 + "每只可以各用一张不同的图。")]
        public Sprite[] monsterSprites = new Sprite[MaxMonsters];

        [Tooltip("每只怪物**单独**的着色（按出场顺序）。白 = 原图颜色；留成默认白就好，"
                 + "想让某只怪整体偏色才改它。")]
        public Color[] monsterTints =
        {
            Color.white, Color.white, Color.white
        };

        [Tooltip("统一着色：上面那三格留成默认白（没说「不填」）时才退回用这个。一般保持白色。")]
        public Color monsterTint = Color.white;

        [Tooltip("怪物形象的外框尺寸（宽, 高）。三只横向排开，间距会按这个宽度自动算。")]
        public Vector2 monsterSize = new Vector2(220f, 300f);

        [Tooltip("怪物血条颜色（需求约定：敌人红）。")]
        public Color monsterBarColor = new Color(0.85f, 0.32f, 0.28f, 1f);

        [Header("⑤ 备注")]
        [Tooltip("写点给自己的备忘，比如「背景待补」「怪物 3 换皮」。不会显示在游戏里。")]
        [TextArea(2, 6)]
        public string notes = string.Empty;

        /// <summary>Inspector 里改数组大小时自动对齐到固定槽位数，避免美术填到一半越界。</summary>
        private void OnValidate()
        {
            if (monsterSprites == null)
            {
                monsterSprites = new Sprite[MaxMonsters];
            }
            else if (monsterSprites.Length != MaxMonsters)
            {
                Sprite[] fixedArray = new Sprite[MaxMonsters];
                for (int i = 0; i < fixedArray.Length && i < monsterSprites.Length; i++)
                {
                    fixedArray[i] = monsterSprites[i];
                }
                monsterSprites = fixedArray;
            }

            if (monsterTints == null)
            {
                monsterTints = new Color[MaxMonsters];
            }
            else if (monsterTints.Length != MaxMonsters)
            {
                Color[] fixedTints = new Color[MaxMonsters];
                for (int i = 0; i < fixedTints.Length; i++)
                {
                    fixedTints[i] = i < monsterTints.Length ? monsterTints[i] : Color.white;
                }
                monsterTints = fixedTints;
            }

            // 尺寸被误清零时兜底，否则实体形象的内部布局会算出 0 高
            if (playerSize.x < 1f || playerSize.y < 1f)
            {
                playerSize = new Vector2(260f, 330f);
            }
            if (monsterSize.x < 1f || monsterSize.y < 1f)
            {
                monsterSize = new Vector2(220f, 300f);
            }
        }
    }
}
