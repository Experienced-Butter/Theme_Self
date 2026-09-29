namespace CardGame.Core
{
    /// <summary>
    /// 可复现的随机数源实现。内部使用 System.Random（本文件是唯一允许直接使用它的地方）。
    /// </summary>
    public sealed class SeededRandom : IRandomSource
    {
        private readonly System.Random _random;
        private readonly int _seed;

        /// <summary>用指定种子构造随机数源。</summary>
        public SeededRandom(int seed)
        {
            _seed = seed;
            _random = new System.Random(seed);
        }

        /// <inheritdoc />
        public int Seed
        {
            get { return _seed; }
        }

        /// <inheritdoc />
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new System.ArgumentOutOfRangeException(
                    "maxExclusive", "Range 要求 maxExclusive > minInclusive。");
            }
            return _random.Next(minInclusive, maxExclusive);
        }

        /// <inheritdoc />
        public float Value01()
        {
            return (float)_random.NextDouble();
        }

        /// <inheritdoc />
        public bool Chance(float probability)
        {
            if (probability <= 0f)
            {
                return false;
            }
            if (probability >= 1f)
            {
                return true;
            }
            return (float)_random.NextDouble() < probability;
        }

        /// <inheritdoc />
        public void Shuffle<T>(System.Collections.Generic.IList<T> list)
        {
            if (list == null || list.Count < 2)
            {
                return;
            }
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
