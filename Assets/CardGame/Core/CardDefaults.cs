namespace CardGame.Core
{
    /// <summary>
    /// 全部 11 种卡牌数值的唯一权威来源（A/B/C/AA/BB/CC/AB/AC/BC/Ultimate/Duplicator）。
    /// 场景生成器、牌库与控制者都从这里取值，禁止在别处硬编码数值。
    /// 数值出处：RULES.md 第二节（基础卡）、第三节（合成卡）、第六节（充能 / 大招 / 复制器）。
    /// </summary>
    public static class CardDefaults
    {
        /// <summary>按卡牌种类生成一份全新的定义（每次调用都返回独立实例）。</summary>
        public static CardDefinitionData Create(CardKind kind)
        {
            switch (kind)
            {
                case CardKind.A:
                    return CreateA();
                case CardKind.B:
                    return CreateB();
                case CardKind.C:
                    return CreateC();
                case CardKind.AA:
                    return CreateAA();
                case CardKind.BB:
                    return CreateBB();
                case CardKind.CC:
                    return CreateCC();
                case CardKind.AB:
                    return CreateAB();
                case CardKind.AC:
                    return CreateAC();
                case CardKind.BC:
                    return CreateBC();
                case CardKind.Ultimate:
                    return CreateUltimate();
                case CardKind.Duplicator:
                    return CreateDuplicator();
                default:
                    throw new System.ArgumentOutOfRangeException(
                        "kind", "未定义的卡牌种类：" + kind);
            }
        }

        // 规则：A（斩刀）基础卡，造成物理伤害 400 点。
        private static CardDefinitionData CreateA()
        {
            CardDefinitionData def = New(CardKind.A, CardTier.Basic, "A·斩刀");
            def.damage = GameRules.CardADamage;
            def.healInstant = GameRules.CardAHeal;
            // 规则：A+A→AA。
            def.fusionRecipe = new CardKind[] { CardKind.A, CardKind.A };
            return def;
        }

        // 规则：B（躲避）基础卡，降低敌方 10% 命中 1 回合。
        private static CardDefinitionData CreateB()
        {
            CardDefinitionData def = New(CardKind.B, CardTier.Basic, "B·躲避");
            def.hitDownPercent = GameRules.HitDownPercent;
            def.hitDownTurns = GameRules.DefaultDebuffTurns;
            // 规则：B+B→BB。
            def.fusionRecipe = new CardKind[] { CardKind.B, CardKind.B };
            return def;
        }

        // 规则：C（治疗）基础卡，治疗自身 300 点。
        private static CardDefinitionData CreateC()
        {
            CardDefinitionData def = New(CardKind.C, CardTier.Basic, "C·治疗");
            def.healInstant = GameRules.CardCHeal;
            // 规则：C+C→CC。
            def.fusionRecipe = new CardKind[] { CardKind.C, CardKind.C };
            return def;
        }

        // 规则：AA（A+A）造成物理伤害 720 点（1.8×400）；
        // 下两回合强化手牌中的 A（1.1×400 = 440）与 AA（1.15×1.8×400 = 828）。
        private static CardDefinitionData CreateAA()
        {
            CardDefinitionData def = New(CardKind.AA, CardTier.Fused, "AA·强化斩");
            def.damage = GameRules.CardAADamage;
            def.handBuffBasic = GameRules.CardAAHandBuffBasic;
            def.handBuffFused = GameRules.CardAAHandBuffFused;
            def.handBuffTurns = GameRules.CardAAHandBuffTurns;
            def.fusionRecipe = new CardKind[] { CardKind.A, CardKind.A };
            return def;
        }

        // 规则：BB（B+B）降低敌方 10% 命中与 10% 防御；
        // ARCHITECTURE 13.2：持续回合默认 1 回合（与 B 一致）。
        private static CardDefinitionData CreateBB()
        {
            CardDefinitionData def = New(CardKind.BB, CardTier.Fused, "BB·双重削弱");
            def.hitDownPercent = GameRules.CardBBHitDownPercent;
            def.hitDownTurns = GameRules.DefaultDebuffTurns;
            def.defenseDownPercent = GameRules.CardBBDefenseDownPercent;
            def.defenseDownFlat = 0;
            def.defenseDownTurns = GameRules.DefaultDebuffTurns;
            def.fusionRecipe = new CardKind[] { CardKind.B, CardKind.B };
            return def;
        }

        // 规则：CC（C+C）回复血量 840 点 = 540 立即 + 150×2 回合（C×1.8 + C×0.5×2）。
        private static CardDefinitionData CreateCC()
        {
            CardDefinitionData def = New(CardKind.CC, CardTier.Fused, "CC·持续回复");
            def.healInstant = GameRules.CardCCInstantHeal;
            def.regenPerTurn = GameRules.CardCCRegenPerTurn;
            def.regenTurns = GameRules.CardCCRegenTurns;
            def.fusionRecipe = new CardKind[] { CardKind.C, CardKind.C };
            return def;
        }

        // 规则：AB（A+B）造成伤害 360 点（0.9×400），下回合获得先手。
        private static CardDefinitionData CreateAB()
        {
            CardDefinitionData def = New(CardKind.AB, CardTier.Fused, "AB·先手斩");
            def.damage = GameRules.CardABDamage;
            def.grantInitiative = true;
            def.fusionRecipe = new CardKind[] { CardKind.A, CardKind.B };
            return def;
        }

        // 规则：AC（A+C）造成伤害 360 点（0.9×400），下回合出牌次数 +1。
        private static CardDefinitionData CreateAC()
        {
            CardDefinitionData def = New(CardKind.AC, CardTier.Fused, "AC·连击斩");
            def.damage = GameRules.CardACDamage;
            def.extraPlaysNextTurn = GameRules.CardACExtraPlays;
            def.fusionRecipe = new CardKind[] { CardKind.A, CardKind.C };
            return def;
        }

        // 规则：BC（B+C）免疫下次伤害，并抽取一张卡牌。
        private static CardDefinitionData CreateBC()
        {
            CardDefinitionData def = New(CardKind.BC, CardTier.Fused, "BC·免疫抽牌");
            def.grantImmunity = true;
            def.drawCount = GameRules.CardBCDrawCount;
            def.fusionRecipe = new CardKind[] { CardKind.B, CardKind.C };
            return def;
        }

        // 规则：大招——下回合增加两次出手机会，抽三张临时卡（ABC 随机），
        // 并获得一个卡牌复制器（仅复制 ABC，获得一次合成次数）。
        // ARCHITECTURE 13.5：充能满 4 点时先获得大招卡进入手牌，打出后才结算本效果。
        private static CardDefinitionData CreateUltimate()
        {
            CardDefinitionData def = New(CardKind.Ultimate, CardTier.Ultimate, "大招·爆发");
            def.extraPlaysNextTurn = GameRules.UltimateExtraPlays;
            def.drawCount = GameRules.UltimateTempCards;
            // 复制器数量由 UltimateBehavior 按 GameRules.UltimateDuplicators 发放。
            def.fusionRecipe = new CardKind[0];
            return def;
        }

        // 规则：卡牌复制器——仅复制 ABC，获得一次合成次数。
        // ARCHITECTURE 13.7：默认复制手牌中第一张 A/B/C；无合法目标时不复制，也不给免费合成。
        private static CardDefinitionData CreateDuplicator()
        {
            CardDefinitionData def = New(CardKind.Duplicator, CardTier.Duplicator, "卡牌复制器");
            def.drawCount = 1;
            def.fusionRecipe = new CardKind[0];
            return def;
        }

        private static CardDefinitionData New(CardKind kind, CardTier tier, string displayName)
        {
            CardDefinitionData def = new CardDefinitionData();
            def.kind = kind;
            def.tier = tier;
            def.displayName = displayName;
            def.fusionRecipe = new CardKind[0];
            return def;
        }
    }
}
