namespace CardGame.Controllers
{
    /// <summary>
    /// 关卡标识。
    /// 规则出处：RULES.md 第五节「关卡配置」—— 关一（单波战斗）、关二（单波战斗）、支线关（波次制）。
    /// 契约出处：ARCHITECTURE.md 第 8 节 `Controllers/LevelId.cs`。
    /// </summary>
    public enum LevelId
    {
        /// <summary>关一：怪物1(2000血/防0)×2，单波战斗。</summary>
        Level1 = 0,

        /// <summary>关二：怪物2(1000血/防25) + 怪物1(2000血/防0)×2，单波战斗。</summary>
        Level2 = 1,

        /// <summary>支线关：第1波 怪物1(1600血/防0)×2 + 怪物2(1000血/防25)；第2波 怪物1(1600血/防0)×2。</summary>
        SideQuest = 2
    }
}
