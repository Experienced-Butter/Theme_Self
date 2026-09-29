namespace CardGame.Core
{
    /// <summary>
    /// 一张卡牌的完整数值定义。由 CardDefaults 创建、被模板与牌库深拷贝分发；
    /// 0 / false 表示「无该项效果」。
    /// </summary>
    [System.Serializable]
    public class CardDefinitionData
    {
        /// <summary>卡牌种类。</summary>
        public CardKind kind;

        /// <summary>卡牌层级。</summary>
        public CardTier tier;

        /// <summary>显示名。</summary>
        public string displayName;

        /// <summary>基础伤害，0 = 无。</summary>
        public int damage;

        /// <summary>立即治疗量，0 = 无。</summary>
        public int healInstant;

        /// <summary>每回合持续回复量，CC = 150。</summary>
        public int regenPerTurn;

        /// <summary>持续回复回合数，CC = 2。</summary>
        public int regenTurns;

        /// <summary>降低命中百分比，B/BB = 0.10。</summary>
        public float hitDownPercent;

        /// <summary>降低命中持续回合，B = 1。</summary>
        public int hitDownTurns;

        /// <summary>降低防御百分比，BB = 0.10。</summary>
        public float defenseDownPercent;

        /// <summary>降低防御固定值，默认 0。</summary>
        public int defenseDownFlat;

        /// <summary>降低防御持续回合。</summary>
        public int defenseDownTurns;

        /// <summary>抽牌数量，BC = 1。</summary>
        public int drawCount;

        /// <summary>是否给予「免疫下次伤害」，BC = true。</summary>
        public bool grantImmunity;

        /// <summary>是否给予下回合先手，AB = true。</summary>
        public bool grantInitiative;

        /// <summary>下回合额外出牌次数，AC = 1。</summary>
        public int extraPlaysNextTurn;

        /// <summary>手牌中基础卡 A 的强化倍率，AA = 1.10。</summary>
        public float handBuffBasic;

        /// <summary>手牌中合成卡 AA 的强化倍率，AA = 1.15。</summary>
        public float handBuffFused;

        /// <summary>手牌强化持续回合，AA = 2。</summary>
        public int handBuffTurns;

        /// <summary>合成配方，如 {A,A} / {A,B}；长度为 0 表示不可由合成产生。</summary>
        public CardKind[] fusionRecipe;

        /// <summary>是否为大招抽到的临时卡（回合结束从未使用的手牌中移除）。</summary>
        public bool isTemporary;

        /// <summary>
        /// 深拷贝（含数组），保证克隆体之间不共享可变状态。
        /// </summary>
        public CardDefinitionData Clone()
        {
            CardDefinitionData copy = new CardDefinitionData();
            copy.kind = kind;
            copy.tier = tier;
            copy.displayName = displayName;
            copy.damage = damage;
            copy.healInstant = healInstant;
            copy.regenPerTurn = regenPerTurn;
            copy.regenTurns = regenTurns;
            copy.hitDownPercent = hitDownPercent;
            copy.hitDownTurns = hitDownTurns;
            copy.defenseDownPercent = defenseDownPercent;
            copy.defenseDownFlat = defenseDownFlat;
            copy.defenseDownTurns = defenseDownTurns;
            copy.drawCount = drawCount;
            copy.grantImmunity = grantImmunity;
            copy.grantInitiative = grantInitiative;
            copy.extraPlaysNextTurn = extraPlaysNextTurn;
            copy.handBuffBasic = handBuffBasic;
            copy.handBuffFused = handBuffFused;
            copy.handBuffTurns = handBuffTurns;
            copy.isTemporary = isTemporary;
            if (fusionRecipe == null)
            {
                copy.fusionRecipe = new CardKind[0];
            }
            else
            {
                copy.fusionRecipe = new CardKind[fusionRecipe.Length];
                for (int i = 0; i < fusionRecipe.Length; i++)
                {
                    copy.fusionRecipe[i] = fusionRecipe[i];
                }
            }
            return copy;
        }
    }
}
