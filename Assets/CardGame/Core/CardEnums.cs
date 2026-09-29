namespace CardGame.Core
{
    /// <summary>
    /// 基础卡标识。规则：牌库构成 = A×5、B×5、C×5，共 15 张基础卡。
    /// </summary>
    public enum CardId { A = 0, B = 1, C = 2 }

    /// <summary>
    /// 卡牌种类。规则：基础卡 A/B/C；合成卡 AA/BB/CC/AB/AC/BC；大招 Ultimate；卡牌复制器 Duplicator。
    /// </summary>
    public enum CardKind { A, B, C, AA, BB, CC, AB, AC, BC, Ultimate, Duplicator }

    /// <summary>
    /// 卡牌层级。基础卡 = Basic，合成卡 = Fused，大招 = Ultimate，复制器 = Duplicator。
    /// </summary>
    public enum CardTier { Basic, Fused, Ultimate, Duplicator }

    /// <summary>
    /// 阵营。玩家侧 / 敌人侧。
    /// </summary>
    public enum Team { Player, Enemy }

    /// <summary>
    /// 怪物类型。怪1 = 攻击型，怪2 = 辅助型。
    /// </summary>
    public enum MonsterKind { Attacker, Support }

    /// <summary>
    /// 怪物行动。规则：怪1 固定顺序 普攻→普攻→减防35→循环；怪2 固定顺序 普攻→辅助→治疗→循环。
    /// </summary>
    public enum MonsterAction { Attack, DefenseBreak, Support, Heal }

    /// <summary>
    /// 战斗阶段。
    /// </summary>
    public enum GamePhase { Idle, Setup, TurnStart, PlayerTurn, EnemyTurn, TurnEnd, WaveTransition, Victory, Defeat }

    /// <summary>
    /// 状态种类。HitDown = 降命中；DefenseDown = 降防御；Regen = 持续回复；
    /// DamageImmunity = 免疫下次伤害；HandDamageUpBasic/Fused = 手牌 A/AA 强化；FreeFusion = 免费合成次数；
    /// Initiative = 先手标记（AB：下回合获得先手，按 ARCHITECTURE §14.4/§14.6 裁定使用本状态）。
    /// 新成员一律追加到末尾，避免影响已有序列化值。
    /// </summary>
    public enum StatusKind { HitDown, DefenseDown, Regen, DamageImmunity, HandDamageUpBasic, HandDamageUpFused, FreeFusion, Initiative }

    /// <summary>
    /// 伤害来源。用于日志与后续扩展。
    /// </summary>
    public enum DamageSource { Card, Monster, Status }

    /// <summary>
    /// 卡牌所在区域。Resolving = 正在结算中，Removed = 已移除（临时卡回合结束清理）。
    /// </summary>
    public enum CardZone { Deck, Hand, Discard, Resolving, Removed }
}
