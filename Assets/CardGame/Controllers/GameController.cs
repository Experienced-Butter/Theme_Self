using System.Collections.Generic;

namespace CardGame.Controllers
{
    /// <summary>
    /// 控制者实体的总装与对外门面。
    /// 契约出处：ARCHITECTURE.md 第 8 节 —— 控制器实体是「一个 GameObject」，上面挂
    ///   RandomComponent / CardLibraryComponent / BattleFactoryComponent / TurnOrderComponent /
    ///   GameFlowComponent / LevelComponent / GameController；
    ///   GameController 提供 `Context / Instance / SetupBattle / PlayCard / TryFuseCards / Hand / Random /
    ///   OnLog / OnBattleEnded`。
    /// PlayCard 与 TryFuseCards 是对外唯一出牌入口：UI、无头模拟与内置自动演示（GameFlowComponent.RunBattle）
    /// 都走这两个方法，保证只有一条结算路径。
    /// </summary>
    public class GameController : UnityEngine.MonoBehaviour
    {
        /// <summary>当前控制者实例。</summary>
        public static GameController Instance { get; private set; }

        /// <summary>当前战斗上下文（未开战时为 null）。</summary>
        public Core.BattleContext Context
        {
            get { return _context; }
        }

        /// <summary>战报回调：BattleContext.Log 与所有流程要点都会经过它。</summary>
        public event System.Action<string> OnLog;

        /// <summary>战斗结束回调：true = 胜利，false = 失败。每局只触发一次。</summary>
        public event System.Action<bool> OnBattleEnded;

        private static readonly List<Cards.CardInstance> EmptyHand = new List<Cards.CardInstance>();

        private readonly List<Combat.Combatant> _enemies = new List<Combat.Combatant>();

        private Core.BattleContext _context;
        private RandomComponent _randomComponent;
        private LevelComponent _levelComponent;
        private CardLibraryComponent _library;
        private BattleFactoryComponent _factory;
        private GameFlowComponent _flow;
        private bool _endedReported;

        /// <summary>全局随机数唯一来源（RandomComponent 提供，禁止 UnityEngine.Random）。</summary>
        public Core.IRandomSource Random
        {
            get
            {
                EnsureRefs();
                return _randomComponent != null ? _randomComponent.Random : null;
            }
        }

        /// <summary>当前手牌（未开战时为空列表）。</summary>
        public System.Collections.Generic.IReadOnlyList<Cards.CardInstance> Hand
        {
            get
            {
                Players.PlayerHandComponent hand = PlayerHand;
                if (hand == null || hand.Cards == null)
                {
                    return EmptyHand;
                }

                return hand.Cards;
            }
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            EnsureRefs();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 初始化一局战斗：写种子 → 载入关卡 → 清场 → 克隆玩家 → 建立 BattleContext → 接线 → 进入 Setup 阶段。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void SetupBattle(LevelId level, int seed)`。
        /// 同种子完全可复现（洗牌、抽卡、随机选卡、命中判定全部走本局唯一的 IRandomSource）。
        /// </summary>
        public void SetupBattle(LevelId level, int seed)
        {
            EnsureRefs();

            _endedReported = false;

            if (_randomComponent == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameController.SetupBattle：控制者实体缺少 RandomComponent。");
                return;
            }

            _randomComponent.useFixedSeed = true;
            _randomComponent.seed = seed;
            _randomComponent.Reseed(seed);

            if (_levelComponent != null)
            {
                _levelComponent.LoadLevel(level);
            }
            else
            {
                UnityEngine.Debug.LogError("[CardGame] GameController.SetupBattle：控制者实体缺少 LevelComponent。");
            }

            if (_flow != null)
            {
                _flow.ResetBattleState();   // 先让上一局的驱动器停下来
            }

            if (_factory == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameController.SetupBattle：控制者实体缺少 BattleFactoryComponent。");
                return;
            }

            ClearRuntimeRoot();   // 连续模拟多局时清理上一局的克隆体
            _enemies.Clear();
            _context = null;

            Combat.Combatant player = _factory.CreatePlayer(_factory.RuntimeRoot);
            if (player == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameController.SetupBattle：玩家实体创建失败（检查 PlayerTemplate）。");
                return;
            }

            _context = new Core.BattleContext(_randomComponent.Random, player, _enemies, this);
            _context.Logger = HandleBattleLog;
            WirePlayer(player);

            if (_flow != null)
            {
                _flow.PrepareBattle();   // Setup 阶段：先手重置 → 牌库/开局手牌 → 第 1 波出场
                _flow.BeginBattle();     // 进入第 1 回合；外部驱动模式下停在 PlayerTurn 等待出牌
            }
            else
            {
                UnityEngine.Debug.LogError("[CardGame] GameController.SetupBattle：控制者实体缺少 GameFlowComponent。");
            }

            Log(string.Format("=== 初始化战斗：{0}，随机种子 {1} ===", LevelNameOf(level), seed));
        }

        /// <summary>
        /// 打出一张手牌（对外入口：UI / 无头模拟 / 内置自动演示共用）。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void PlayCard(CardInstance card)`。
        /// </summary>
        public void PlayCard(Cards.CardInstance card)
        {
            if (card == null)
            {
                Log("出牌失败：卡牌为空。");
                return;
            }

            if (_context == null)
            {
                Log("出牌失败：战斗尚未初始化（请先调用 SetupBattle）。");
                return;
            }

            Players.PlayerPlayComponent play = PlayerPlay;
            if (play == null)
            {
                Log("出牌失败：玩家实体缺少 PlayerPlayComponent。");
                return;
            }

            if (_flow != null && _flow.Phase != Core.GamePhase.PlayerTurn)
            {
                Log(string.Format("出牌失败：当前阶段为 {0}，只能在玩家回合出牌。", _flow.Phase));
                return;
            }

            List<HpSnapshot> before = CaptureHp();
            bool played = play.TryPlayCard(_context, card);
            if (!played)
            {
                Log(string.Format("出牌失败：「{0}」未能打出（出牌次数不足或卡牌不可结算）。", CardName(card)));
                NotifyFlowPlayerActionResolved();
                return;
            }

            // 战报：回合N 玩家 出牌XX → 怪物1 受720伤害，剩余1280（数值取自本次结算前后的生命值差）。
            Log(string.Format("回合{0} 玩家 出牌{1} → {2}", TurnText(), CardName(card), DescribeHpDeltas(before)));

            NotifyFlowPlayerActionResolved();
        }

        /// <summary>
        /// 合成两张手牌（对外入口：UI / 无头模拟 / 内置自动演示共用）。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `bool TryFuseCards(CardInstance a, CardInstance b)`。
        /// 规则：合成消耗 1 次出牌、给 1 点充能（规则六第 1 条）；充能满 4 点时由控制者发放大招卡（第 13 节第 5 条）。
        /// </summary>
        public bool TryFuseCards(Cards.CardInstance a, Cards.CardInstance b)
        {
            if (a == null || b == null)
            {
                Log("合成失败：材料卡牌为空。");
                return false;
            }

            if (_context == null)
            {
                Log("合成失败：战斗尚未初始化（请先调用 SetupBattle）。");
                return false;
            }

            Cards.CardFusionComponent fusion = PlayerFusion;
            if (fusion == null)
            {
                Log("合成失败：玩家实体缺少 CardFusionComponent。");
                return false;
            }

            if (_flow != null && _flow.Phase != Core.GamePhase.PlayerTurn)
            {
                Log(string.Format("合成失败：当前阶段为 {0}，只能在玩家回合合成。", _flow.Phase));
                return false;
            }

            List<HpSnapshot> before = CaptureHp();
            Cards.CardInstance product = fusion.Fuse(_context.Player, a, b);
            if (product == null)
            {
                Log(string.Format("合成失败：「{0}」+「{1}」不构成有效配方，或本回合已无出牌次数。", CardName(a), CardName(b)));
                NotifyFlowPlayerActionResolved();
                return false;
            }

            Players.PlayerChargeComponent charge = PlayerCharge;
            Log(string.Format("回合{0} 玩家 合成「{1}」+「{2}」→「{3}」（充能 {4}/{5}，剩余出牌 {6} 次）",
                              TurnText(), CardName(a), CardName(b), CardName(product),
                              charge != null ? charge.charge : 0, Core.GameRules.ChargeThreshold,
                              PlayerTurn != null ? PlayerTurn.PlaysRemaining : 0));
            LogHpDeltasIfAny(before, "合成");

            GrantUltimateIfReady();
            NotifyFlowPlayerActionResolved();
            return true;
        }

        /// <summary>战斗结束时由流程回调（每局只上报一次）。</summary>
        internal void NotifyBattleEnded(bool victory)
        {
            if (_endedReported)
            {
                return;
            }

            _endedReported = true;

            System.Action<bool> handler = OnBattleEnded;
            if (handler != null)
            {
                handler(victory);
            }
        }

        /// <summary>
        /// 每次出牌 / 合成结算后回调流程：外部驱动模式下，玩家本回合已无法继续行动时，
        /// 由流程自动推进敌人回合、回合结束结算与下一回合（停留在下一个 PlayerTurn）。
        /// 自动代打模式（GameFlowComponent.RunBattle）内部直接返回，不会重入。
        /// </summary>
        private void NotifyFlowPlayerActionResolved()
        {
            if (_flow != null)
            {
                _flow.NotifyPlayerActionResolved();
            }
        }

        // ---------------------------------------------------------------- 内部实现

        /// <summary>
        /// 充能满 4 点 → 克隆大招卡并放入手牌，随后清零充能。
        /// 规则出处：RULES.md 第六节第 1 条 + ARCHITECTURE 第 13 节第 5 条
        ///          「充能到 4 点时获得大招卡并进入手牌，打出它才结算」。
        /// 手牌已满（上限 6 张）时暂缓发放并保留充能，下次合成后再发放。
        /// </summary>
        private void GrantUltimateIfReady()
        {
            Players.PlayerChargeComponent charge = PlayerCharge;
            Players.PlayerHandComponent hand = PlayerHand;
            if (charge == null || hand == null || !charge.IsReady)
            {
                return;
            }

            if (hand.IsFull)
            {
                Log(string.Format("充能 {0}/{1} 已满，但手牌已达上限 {2} 张，大招卡暂缓发放。",
                                  charge.charge, charge.Threshold, hand.handLimit));
                return;
            }

            Cards.CardInstance ultimate = CreateCard(Core.CardKind.Ultimate);
            if (ultimate == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameController：大招卡克隆失败，本次未发放。");
                return;
            }

            if (!hand.TryAdd(ultimate))
            {
                Log("大招卡未能进入手牌（手牌空间不足），充能保留。");
                return;
            }

            charge.Consume();
            Log(string.Format("充能 {0} 点达成 → 获得大招卡「{1}」并进入手牌（打出它才结算大招效果）。",
                              Core.GameRules.ChargeThreshold, ultimate.DisplayName));
        }

        /// <summary>统一走 CardLibraryComponent 的模板克隆，绝不返回共享实例。</summary>
        private Cards.CardInstance CreateCard(Core.CardKind kind)
        {
            EnsureRefs();
            if (_library == null || _context == null || _context.Player == null)
            {
                return null;
            }

            UnityEngine.Transform parent = _factory != null ? _factory.RuntimeRoot : null;
            return _library.CreateCard(kind, _context.Player, parent);
        }

        /// <summary>接线：把牌库、归属者、手牌、随机源注入玩家侧组件（Bootstrap 之外由控制者兜底注入）。</summary>
        private void WirePlayer(Combat.Combatant player)
        {
            if (player == null)
            {
                return;
            }

            EnsureRefs();

            Players.PlayerDeckComponent deck = player.GetComponent<Players.PlayerDeckComponent>();
            if (deck != null)
            {
                if (deck.library == null)
                {
                    deck.library = _library;
                }

                if (deck.owner == null)
                {
                    deck.owner = player;
                }

                if (deck.hand == null)
                {
                    deck.hand = player.GetComponent<Players.PlayerHandComponent>();
                }

                if (deck.randomSource == null)
                {
                    deck.randomSource = _randomComponent != null ? _randomComponent.Random : null;
                }
            }

            Cards.CardFusionComponent fusion = player.GetComponent<Cards.CardFusionComponent>();
            if (fusion != null && fusion.library == null)
            {
                fusion.library = _library;
            }

            Players.PlayerPlayComponent play = player.GetComponent<Players.PlayerPlayComponent>();
            if (play != null && play.owner == null)
            {
                play.owner = player;
            }
        }

        /// <summary>清掉上一局留在 RuntimeRoot 下的全部克隆体（连续模拟多局必需）。</summary>
        private void ClearRuntimeRoot()
        {
            if (_factory == null)
            {
                return;
            }

            UnityEngine.Transform root = _factory.RuntimeRoot;
            if (root == null)
            {
                return;
            }

            List<UnityEngine.GameObject> children = new List<UnityEngine.GameObject>();
            for (int i = 0; i < root.childCount; i++)
            {
                children.Add(root.GetChild(i).gameObject);
            }

            for (int i = 0; i < children.Count; i++)
            {
                UnityEngine.GameObject go = children[i];
                if (go == null)
                {
                    continue;
                }

                if (UnityEngine.Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(go);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        private void EnsureRefs()
        {
            if (_randomComponent == null)
            {
                _randomComponent = GetComponent<RandomComponent>();
            }

            if (_levelComponent == null)
            {
                _levelComponent = GetComponent<LevelComponent>();
            }

            if (_library == null)
            {
                _library = GetComponent<CardLibraryComponent>();
            }

            if (_factory == null)
            {
                _factory = GetComponent<BattleFactoryComponent>();
            }

            if (_flow == null)
            {
                _flow = GetComponent<GameFlowComponent>();
            }
        }

        /// <summary>BattleContext.Log 会同时写 Unity 控制台并转发到这里。</summary>
        private void HandleBattleLog(string message)
        {
            System.Action<string> handler = OnLog;
            if (handler != null)
            {
                handler(message);
            }
        }

        private void Log(string message)
        {
            if (_context != null)
            {
                _context.Log(message);
                return;
            }

            System.Action<string> handler = OnLog;
            if (handler != null)
            {
                handler(message);
            }

            UnityEngine.Debug.Log("[CardGame] " + message);
        }

        private Players.PlayerHandComponent PlayerHand
        {
            get
            {
                return _context != null && _context.Player != null
                    ? _context.Player.GetComponent<Players.PlayerHandComponent>()
                    : null;
            }
        }

        private Players.PlayerTurnComponent PlayerTurn
        {
            get
            {
                return _context != null && _context.Player != null
                    ? _context.Player.GetComponent<Players.PlayerTurnComponent>()
                    : null;
            }
        }

        private Players.PlayerChargeComponent PlayerCharge
        {
            get
            {
                return _context != null && _context.Player != null
                    ? _context.Player.GetComponent<Players.PlayerChargeComponent>()
                    : null;
            }
        }

        private Players.PlayerPlayComponent PlayerPlay
        {
            get
            {
                return _context != null && _context.Player != null
                    ? _context.Player.GetComponent<Players.PlayerPlayComponent>()
                    : null;
            }
        }

        private Cards.CardFusionComponent PlayerFusion
        {
            get
            {
                return _context != null && _context.Player != null
                    ? _context.Player.GetComponent<Cards.CardFusionComponent>()
                    : null;
            }
        }

        // ---- 战报辅助：结算前后快照对比，输出「谁受了多少伤害/回了多少血」 ----

        private sealed class HpSnapshot
        {
            public Combat.Combatant Who;
            public string Name;
            public int Hp;
        }

        private List<HpSnapshot> CaptureHp()
        {
            List<HpSnapshot> snapshots = new List<HpSnapshot>();
            if (_context == null)
            {
                return snapshots;
            }

            AddSnapshot(snapshots, _context.Player, 0);

            if (_context.Enemies != null)
            {
                for (int i = 0; i < _context.Enemies.Count; i++)
                {
                    AddSnapshot(snapshots, _context.Enemies[i], i + 1);
                }
            }

            return snapshots;
        }

        private static void AddSnapshot(List<HpSnapshot> snapshots, Combat.Combatant who, int index)
        {
            if (who == null)
            {
                return;
            }

            HpSnapshot snapshot = new HpSnapshot();
            snapshot.Who = who;
            snapshot.Name = !string.IsNullOrEmpty(who.displayName)
                ? who.displayName
                : (index == 0 ? "玩家" : "敌人" + index);
            snapshot.Hp = who.CurrentHp;
            snapshots.Add(snapshot);
        }

        /// <summary>仅在本次动作确实改变了生命值时补一条结算说明（合成本身通常不产生生命值变化）。</summary>
        private void LogHpDeltasIfAny(List<HpSnapshot> before, string action)
        {
            if (before == null)
            {
                return;
            }

            string described = DescribeHpDeltas(before);
            if (described.StartsWith("（状态/辅助效果", System.StringComparison.Ordinal))
            {
                return;
            }

            Log(string.Format("回合{0} 玩家 {1}「{2}」", TurnText(), action, described));
        }

        private static string DescribeHpDeltas(List<HpSnapshot> before)
        {            if (before == null || before.Count == 0)
            {
                return "（无生命值变化记录）";
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < before.Count; i++)
            {
                HpSnapshot snapshot = before[i];
                if (snapshot.Who == null)
                {
                    continue;
                }

                int now = snapshot.Who.CurrentHp;
                int damage = snapshot.Hp - now;
                if (damage > 0)
                {
                    parts.Add(string.Format("{0} 受{1}伤害，剩余{2}", snapshot.Name, damage, now));
                }
                else if (damage < 0)
                {
                    parts.Add(string.Format("{0} 回复{1}，剩余{2}", snapshot.Name, -damage, now));
                }
            }

            if (parts.Count == 0)
            {
                return "（状态/辅助效果，无生命值变化）";
            }

            return string.Join("；", parts.ToArray());
        }

        private string TurnText()
        {
            return _flow != null ? _flow.TurnNumber.ToString() : "?";
        }

        private static string CardName(Cards.CardInstance card)
        {
            if (card == null)
            {
                return "?";
            }

            string name = card.DisplayName;
            return string.IsNullOrEmpty(name) ? card.name : name;
        }

        private static string LevelNameOf(LevelId id)
        {
            switch (id)
            {
                case LevelId.Level1:
                    return "关一";
                case LevelId.Level2:
                    return "关二";
                case LevelId.SideQuest:
                    return "支线关";
            }

            return id.ToString();
        }
    }
}
