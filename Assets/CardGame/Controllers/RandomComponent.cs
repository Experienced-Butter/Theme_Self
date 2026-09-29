namespace CardGame.Controllers
{
    /// <summary>
    /// 全局随机数的唯一来源。
    /// 契约出处：ARCHITECTURE.md 第 0 节第 7 条「所有随机数必须走 IRandomSource，禁止直接使用
    /// UnityEngine.Random / System.Random（SeededRandom 内部除外）」、第 8 节 `Controllers/RandomComponent.cs`。
    /// 用途：洗牌、抽卡、大招随机选卡、怪物命中判定等全部随机行为都通过本组件的 Random 取值，
    ///       useFixedSeed = true 时同种子完全可复现（无头模拟的关键前提）。
    /// </summary>
    public class RandomComponent : UnityEngine.MonoBehaviour
    {
        /// <summary>随机种子。useFixedSeed = true 时由 GameController.SetupBattle 写入。</summary>
        public int seed;

        /// <summary>是否使用固定种子（true = 可复现；false = 每次启动按时间派生一个种子）。</summary>
        public bool useFixedSeed = true;

        private Core.IRandomSource _random;

        /// <summary>
        /// 当前随机数源（延迟构造，保证编辑期与运行期都可用）。
        /// </summary>
        public Core.IRandomSource Random
        {
            get
            {
                if (_random == null)
                {
                    _random = BuildRandom();
                }

                return _random;
            }
        }

        private void Awake()
        {
            _random = BuildRandom();
        }

        private void OnEnable()
        {
            if (_random == null)
            {
                _random = BuildRandom();
            }
        }

        /// <summary>
        /// 用指定种子重建随机数源（开新一局时调用）。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void Reseed(int seed)`。
        /// </summary>
        public void Reseed(int seed)
        {
            this.seed = seed;
            _random = new Core.SeededRandom(seed);
        }

        /// <summary>
        /// 构造随机数源：固定种子直接用 seed；非固定种子用当前时间派生（仍走 SeededRandom，
        /// 不使用 UnityEngine.Random 与 System.Random）。
        /// </summary>
        private Core.IRandomSource BuildRandom()
        {
            if (useFixedSeed)
            {
                return new Core.SeededRandom(seed);
            }

            long ticks = System.DateTime.UtcNow.Ticks;
            int derived = (int)(ticks & 0x7FFFFFFF) ^ seed;
            return new Core.SeededRandom(derived);
        }
    }
}
