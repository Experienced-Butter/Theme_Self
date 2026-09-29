namespace CardGame.Core
{
    /// <summary>
    /// 全局随机数唯一来源。任何随机行为都必须通过本接口，禁止直接使用
    /// UnityEngine.Random / System.Random（SeededRandom 内部实现除外）。
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>当前种子，用于战报复现。</summary>
        int Seed { get; }

        /// <summary>返回 [minInclusive, maxExclusive) 区间内的整数；maxExclusive 不包含。</summary>
        int Range(int minInclusive, int maxExclusive);

        /// <summary>返回 [0,1) 区间内的浮点数。</summary>
        float Value01();

        /// <summary>概率判定：probability&lt;=0 恒为 false；probability&gt;=1 恒为 true。</summary>
        bool Chance(float probability);

        /// <summary>原地洗牌（Fisher-Yates）。</summary>
        void Shuffle<T>(System.Collections.Generic.IList<T> list);
    }
}
