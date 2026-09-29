namespace CardGame.Core
{
    /// <summary>
    /// 全部数值与取整规则的唯一权威来源。规则原文见 RULES.md；
    /// 规则未明确、由方案默认的取值见 ARCHITECTURE.md 第 13 节。
    /// </summary>
    public static class GameRules
    {
        // ---------- 玩家（规则：一、玩家基础参数） ----------
        /// <summary>规则：生命值 2500。</summary>
        public const int PlayerMaxHp = 2500;
        /// <summary>规则：防御力 300，与怪物攻击做减法后计算实际受伤。</summary>
        public const int PlayerDefense = 300;
        /// <summary>规则：命中率 100%，玩家攻击必定命中。</summary>
        public const float PlayerHitRate = 1f;
        /// <summary>规则：每回合出牌次数 4 次。</summary>
        public const int PlaysPerTurn = 4;
        /// <summary>规则：初始手牌 5 张（确保 ABC 各至少 1 张）。</summary>
        public const int OpeningHand = 5;
        /// <summary>规则：每回合抽牌 4 张。</summary>
        public const int DrawPerTurn = 4;
        /// <summary>规则：手牌上限 6 张，超过 6 张时无法抽牌。</summary>
        public const int HandLimit = 6;
        /// <summary>规则：牌库构成 A×5、B×5、C×5，每种基础卡 5 张。</summary>
        public const int DeckCopiesPerBasic = 5;

        // ---------- 充能（规则：六、1. 充能机制） ----------
        /// <summary>规则：累计 4 点充能时可获得大招卡牌。</summary>
        public const int ChargeThreshold = 4;
        /// <summary>规则：大招效果——下回合增加两次出手机会。</summary>
        public const int UltimateExtraPlays = 2;
        /// <summary>规则：大招效果——抽取三张临时卡牌（ABC 随机）。</summary>
        public const int UltimateTempCards = 3;
        /// <summary>规则：大招效果——获得一个卡牌复制器（仅复制 ABC，获得一次合成次数）。</summary>
        public const int UltimateDuplicators = 1;

        // ---------- 基础卡（规则：二、基础卡牌效果） ----------
        /// <summary>规则：A（斩刀）造成物理伤害 400 点。</summary>
        public const int CardADamage = 400;
        /// <summary>规则：A 无治疗量。</summary>
        public const int CardAHeal = 0;
        /// <summary>规则：C（治疗）治疗自身 300 点。</summary>
        public const int CardCHeal = 300;
        /// <summary>规则：B（躲避）降低敌方 10% 命中 1 回合。</summary>
        public const float HitDownPercent = 0.10f;

        // ---------- 合成卡（规则：三、合成卡牌规则） ----------
        /// <summary>规则：AA 造成物理伤害 720 点（1.8×400）。</summary>
        public const int CardAADamage = 720;
        /// <summary>规则：强化 A = 440 点（1.1×400）。</summary>
        public const float CardAAHandBuffBasic = 1.10f;
        /// <summary>规则：强化 AA = 828 点（1.15×1.8×400）。</summary>
        public const float CardAAHandBuffFused = 1.15f;
        /// <summary>规则：AA 强化手牌中的 A 与 AA，持续下两回合。</summary>
        public const int CardAAHandBuffTurns = 2;
        /// <summary>规则：BB 降低敌方 10% 命中。</summary>
        public const float CardBBHitDownPercent = 0.10f;
        /// <summary>规则：BB 降低敌方 10% 防御。</summary>
        public const float CardBBDefenseDownPercent = 0.10f;
        /// <summary>规则：CC 回复血量 840 点（540 立即 + 150×2 回合）。</summary>
        public const int CardCCTotalHeal = 840;
        /// <summary>规则：CC 立即回复 540 点（C×1.8 = 300×1.8）。</summary>
        public const int CardCCInstantHeal = 540;
        /// <summary>规则：CC 获得两回合持续回复，每回合 150 点（C×0.5）。</summary>
        public const int CardCCRegenPerTurn = 150;
        /// <summary>规则：CC 持续回复回合数 2。</summary>
        public const int CardCCRegenTurns = 2;
        /// <summary>规则：AB 造成伤害 360 点（0.9×400），下回合获得先手。</summary>
        public const int CardABDamage = 360;
        /// <summary>规则：AC 造成伤害 360 点（0.9×400），下回合出牌次数 +1。</summary>
        public const int CardACDamage = 360;
        /// <summary>规则：AC 下回合出牌次数 +1。</summary>
        public const int CardACExtraPlays = 1;
        /// <summary>规则：BC 抽取一张卡牌。</summary>
        public const int CardBCDrawCount = 1;
        /// <summary>规则：B 明确为 1 回合；BB（ARCHITECTURE 13.2）沿用 1 回合。</summary>
        public const int DefaultDebuffTurns = 1;

        // ---------- 怪物1（攻击型，规则：四、怪物参数） ----------
        /// <summary>规则：怪1 血量 2000（普通/关一/关二）。</summary>
        public const int Monster1HpNormal = 2000;
        /// <summary>规则：怪1 支线关血量 1600。</summary>
        public const int Monster1HpSide = 1600;
        /// <summary>规则：怪1 防御 0。</summary>
        public const int Monster1Defense = 0;
        /// <summary>规则：怪1 攻击力 580。</summary>
        public const int Monster1Attack = 580;
        /// <summary>规则：怪1 固定顺序第三招减防 35（按固定值理解，非百分比）。</summary>
        public const int Monster1DefenseBreakFlat = 35;
        /// <summary>ARCHITECTURE 13.1：怪1 减防 35 的持续回合默认 2 回合（规则文档未写）。</summary>
        public const int Monster1DefenseBreakTurns = 2;

        // ---------- 怪物2（辅助型，规则：四、怪物参数） ----------
        /// <summary>规则：怪2（含支线关）血量 1000。</summary>
        public const int Monster2Hp = 1000;
        /// <summary>规则：怪2 防御 25。</summary>
        public const int Monster2Defense = 25;
        /// <summary>规则：怪2 攻击力 350。</summary>
        public const int Monster2Attack = 350;
        /// <summary>规则：怪2 治疗——回复己方全体 55 点生命。</summary>
        public const int Monster2HealInstant = 55;
        /// <summary>规则：怪2 治疗——下两回合持续恢复 28 点生命。</summary>
        public const int Monster2HealPerTurn = 28;
        /// <summary>规则：怪2 持续回复回合数 2。</summary>
        public const int Monster2HealTurns = 2;

        // ---------- 波次（规则：六、3. 波次回复机制） ----------
        /// <summary>规则：波次切换时恢复 20% 已损血量。</summary>
        public const float WaveRecoveryPercent = 0.20f;

        /// <summary>
        /// 统一向下取整入口（规则：六、5. 所有伤害向下取整）。
        /// </summary>
        public static int FloorToInt(float value)
        {
            return (int)System.Math.Floor(value);
        }

        /// <summary>
        /// 玩家→怪物伤害：规则 floor(基础伤害 × 强化倍率) - 防御，下限 0（防御为 0 时不减）。
        /// </summary>
        public static int ComputeCardDamage(int baseDamage, float multiplier, int targetDefense)
        {
            int raw = FloorToInt(baseDamage * multiplier);
            int result = raw - targetDefense;
            if (result < 0)
            {
                result = 0;
            }
            return result;
        }

        /// <summary>
        /// 怪物→玩家伤害：规则 怪物攻击数值 - 玩家防御，下限 0（防御可被 debuff 降低）。
        /// </summary>
        public static int ComputeMonsterDamage(int attack, int targetDefense)
        {
            int result = attack - targetDefense;
            if (result < 0)
            {
                result = 0;
            }
            return result;
        }

        /// <summary>
        /// 波次回复：规则 回复 20% 已损血量 = floor((maxHp - currentHp) × 20%)。
        /// </summary>
        public static int ComputeWaveRecovery(int maxHp, int currentHp)
        {
            int lost = maxHp - currentHp;
            if (lost <= 0)
            {
                return 0;
            }
            return FloorToInt(lost * WaveRecoveryPercent);
        }
    }
}
