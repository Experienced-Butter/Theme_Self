namespace CardGame.Core
{
    /// <summary>
    /// 状态效果。magnitude 的语义（ARCHITECTURE §14.6 正式契约）：
    /// 百分比状态用 0.10 表示 10%（HitDown / DefenseDown）；点数状态（Regen）用每回合点数；
    /// DefenseDown 另用数值区间区分百分比与固定值（≤1 = 百分比，&gt;1 = 固定值，见 Combat/DefenseComponent）。
    /// </summary>
    [System.Serializable]
    public class StatusEffect
    {
        /// <summary>状态种类。</summary>
        public StatusKind kind;

        /// <summary>百分比用 0.10 表示 10%；Regen 用每回合点数。</summary>
        public float magnitude;

        /// <summary>剩余回合数，回合结束 -1，归零即移除。</summary>
        public int remainingTurns;

        /// <summary>施加者阵营，仅用于日志。</summary>
        public bool fromPlayer;

        public StatusEffect() { }

        /// <summary>
        /// 规则：fromPlayer 仅用于日志，不参与结算，故不纳入构造签名，由施加方按需赋值。
        /// </summary>
        public StatusEffect(StatusKind kind, float magnitude, int remainingTurns)
        {
            this.kind = kind;
            this.magnitude = magnitude;
            this.remainingTurns = remainingTurns;
        }
    }
}
