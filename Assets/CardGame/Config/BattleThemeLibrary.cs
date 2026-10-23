using CardGame.Controllers;
using UnityEngine;

namespace CardGame.Config
{
    /// <summary>
    /// 按关卡取主题配置。
    ///
    /// 为什么走 <see cref="Resources"/>：配置资产放在 `Assets/CardGame/Resources/CardGameThemes/` 下，
    /// 运行时按关卡名直接加载 —— **不需要在场景里拖引用**。这一点对「别人 clone GitHub 仓库」
    /// 很关键：场景文件不会因为缺资产而引用丢失，配置换人接手也不会掉线。
    ///
    /// 找不到配置时返回 null，模块会自动回退到程序生成的占位外观（不会报错、不会白屏）。
    /// </summary>
    public static class BattleThemeLibrary
    {
        /// <summary>Resources 下的相对目录名。</summary>
        public const string ResourceFolder = "CardGameThemes";

        /// <summary>某一关的配置资源路径（不含扩展名）。</summary>
        public static string ResourcePathOf(LevelId level)
        {
            return ResourceFolder + "/Theme_" + level;
        }

        /// <summary>加载某一关的主题；没有就返回 null（调用方负责回退）。</summary>
        public static BattleTheme Load(LevelId level)
        {
            return Resources.Load<BattleTheme>(ResourcePathOf(level));
        }
    }
}
