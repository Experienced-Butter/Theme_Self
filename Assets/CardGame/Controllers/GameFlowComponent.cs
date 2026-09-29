using System.Collections.Generic;

namespace CardGame.Controllers
{
    /// <summary>
    /// 战斗阶段机（控制者实体的流程层）。
    /// 契约出处：ARCHITECTURE.md 第 8 节 `Controllers/GameFlowComponent.cs`
    ///          —— 「阶段机：Setup → TurnStart → PlayerTurn → EnemyTurn → TurnEnd →（WaveTransition）→ … → Victory/Defeat」。
    /// 规则出处：
    ///   RULES.md 第六节第 2 条 先手轮换（初始回合玩家先手，之后交替；AB 使玩家下回合强制先手）；
    ///   RULES.md 第六节第 3 条 波次回复（波次切换时回复 20% 已损血量）；
    ///   RULES.md 第六节第 4 条 每回合结束后抽 4 张、手牌上限 6 张；
    ///   RULES.md 第六节第 1 条 大招临时卡在本回合结束时移除。
    /// 回合结束的固定动作（任务契约）：PlayerTurnComponent.EndTurn → StatusComponent.TickTurnEnd（玩家与所有怪物）
    ///          → 清理临时卡 → 每回合抽 4 张 → 先手轮换 → 判波次 / 胜负。
    ///
    /// 驱动模型（本实现的关键设计）：
    ///   阶段机是**同步回合链**：StartTurn →（先手方行动）→（另一方行动）→ EndTurn → StartTurn → … → Victory/Defeat。
    ///   - 玩家回合是唯一的「挂起点」：外部（UI / GameBootstrap / 无头模拟）在 PlayerTurn 阶段通过
    ///     GameController.PlayCard / TryFuseCards 出牌；GameController 每次结算后回调 NotifyPlayerActionResolved，
    ///     当玩家本回合已无法继续行动时，流程自动打完敌人回合并在下一回合的 PlayerTurn 处再次挂起。
    ///   - RunBattle() 打开自动代打（_autoDrive）：玩家回合由本组件按默认策略代打，一局一路推到胜负，
    ///     供无头模拟与「自动演示」使用；它内部出牌同样调用 GameController.PlayCard / TryFuseCards，只有一条结算路径。
    /// </summary>
    public class GameFlowComponent : UnityEngine.MonoBehaviour
    {
        /// <summary>当前阶段。</summary>
        public Core.GamePhase Phase
        {
            get { return _phase; }
        }

        /// <summary>阶段变化事件。</summary>
        public event System.Action<Core.GamePhase> PhaseChanged;

        /// <summary>当前回合数（从 1 开始），供战报使用。</summary>
        internal int TurnNumber
        {
            get { return _turnNumber; }
        }

        private const int MaxTurnsPerBattle = 500;         // 死循环保护：回合上限
        private const int MaxActionsPerPlayerTurn = 64;    // 死循环保护：单回合出牌/合成动作上限

        private Core.GamePhase _phase = Core.GamePhase.Idle;
        private bool _prepared;
        private bool _autoDrive;
        private bool _battleOver;
        private int _turnNumber;
        private bool _playerActedThisTurn;
        private bool _enemyActedThisTurn;

        private GameController _controller;
        private RandomComponent _random;
        private LevelComponent _level;
        private BattleFactoryComponent _factory;
        private TurnOrderComponent _turnOrder;
        private Players.PlayerTurnComponent _playerTurn;
        private Players.PlayerHandComponent _hand;
        private Players.PlayerDeckComponent _deck;
        private Cards.CardFusionComponent _fusion;
        private Combat.Combatant _player;

        private void Awake()
        {
            EnsureRefs();
        }

        // ---------------------------------------------------------------- 对外 API

        /// <summary>
        /// 战斗驱动入口（契约标注为「协程入口」）。
        /// 打开自动代打后一局推到胜负：玩家回合由本组件按默认策略出牌（走 GameController 的对外入口），
        /// 敌人回合、状态结算、波次切换、胜负判定全部自动完成。
        /// 若战斗已由外部驱动并停在玩家回合，则只把当前玩家回合代打完。
        /// </summary>
        public void RunBattle()
        {
            EnsureRefs();

            if (_controller == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.RunBattle：同一 GameObject 上找不到 GameController。");
                return;
            }

            if (_controller.Context == null)
            {
                LevelId level = _level != null ? _level.currentLevel : LevelId.Level1;
                int seed = _random != null ? _random.seed : 0;
                Log("战斗尚未初始化，RunBattle 自动执行 SetupBattle。");
                _controller.SetupBattle(level, seed);
            }

            if (_controller.Context == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.RunBattle：SetupBattle 未能建立 BattleContext，无法开战。");
                return;
            }

            if (_battleOver)
            {
                Log("战斗已经结束，忽略本次 RunBattle。");
                return;
            }

            _autoDrive = true;

            if (_turnNumber == 0)
            {
                StartTurn();                       // 从 Setup 直接开打：整条回合链会一路推进到胜负
            }
            else if (_phase == Core.GamePhase.PlayerTurn)
            {
                AutoPlayPlayerTurn();              // 外部驱动停在玩家回合：把本回合代打完并继续
            }

            _autoDrive = false;

            if (!_battleOver)
            {
                Log(string.Format("RunBattle：自动代打结束于回合{0}（阶段 {1}）。", _turnNumber, _phase));
            }
        }

        /// <summary>
        /// 进入玩家回合：重置本回合出牌次数（并在此时结算上一回合挂起的 +1 / +2 额外出手）。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void StartPlayerTurn()`。
        /// 自动代打模式下本方法会直接把该玩家回合打完；否则把控制权交回外部驱动。
        /// </summary>
        public void StartPlayerTurn()
        {
            EnsurePrepared();
            if (_battleOver)
            {
                return;
            }

            SetPhase(Core.GamePhase.PlayerTurn);
            _playerActedThisTurn = false;

            if (_playerTurn != null)
            {
                _playerTurn.BeginTurn();
            }

            Log(string.Format("回合{0} 玩家回合开始（出牌 {1}/{2}，手牌 {3} 张，充能 {4}/{5}）。",
                              _turnNumber,
                              _playerTurn != null ? _playerTurn.PlaysRemaining : 0,
                              _playerTurn != null ? _playerTurn.playsPerTurn : Core.GameRules.PlaysPerTurn,
                              _hand != null ? _hand.Count : 0,
                              Charge != null ? Charge.charge : 0,
                              Core.GameRules.ChargeThreshold));

            if (_autoDrive)
            {
                AutoPlayPlayerTurn();
                return;
            }

            // 外部驱动模式下，若玩家本回合根本无法行动（无出牌次数 / 手牌为空），直接结束玩家部分，避免流程停滞。
            if (!CanPlayerAct())
            {
                Log("玩家本回合无法行动（出牌次数或手牌不足），自动结束玩家部分。");
                EndPlayerTurn();
            }
        }

        /// <summary>
        /// 结束玩家部分：随后自动推进敌人回合与本回合的 EndTurn，并进入下一回合。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void EndPlayerTurn()`。
        /// </summary>
        public void EndPlayerTurn()
        {
            if (_battleOver)
            {
                return;
            }

            _playerActedThisTurn = true;
            Log(string.Format("回合{0} 玩家回合结束（剩余出牌 {1} 次，手牌 {2} 张）。",
                              _turnNumber,
                              _playerTurn != null ? _playerTurn.PlaysRemaining : 0,
                              _hand != null ? _hand.Count : 0));

            AfterPlayerTurn();
        }

        /// <summary>
        /// 敌人回合：每只存活怪物按 MonsterActionComponent.sequence 的固定顺序行动一次
        /// （规则六第 6 条：怪1 普攻→普攻→减防35；怪2 普攻→辅助→治疗）。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void StartEnemyTurn()`。
        /// </summary>
        public void StartEnemyTurn()
        {
            EnsurePrepared();
            if (_battleOver || _controller == null || _controller.Context == null)
            {
                return;
            }

            SetPhase(Core.GamePhase.EnemyTurn);
            _enemyActedThisTurn = true;
            Log(string.Format("回合{0} 敌人回合开始（存活敌人 {1} 只）。", _turnNumber, CountAliveEnemies()));

            List<Combat.Combatant> enemies = AllEnemies();
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    Combat.Combatant monster = enemies[i];
                    if (monster == null || !monster.IsAlive)
                    {
                        continue;
                    }

                    Monsters.MonsterActionComponent action = monster.GetComponent<Monsters.MonsterActionComponent>();
                    if (action == null)
                    {
                        Log(string.Format("「{0}」缺少 MonsterActionComponent，本次行动跳过。", monster.displayName));
                        continue;
                    }

                    action.ExecuteTurn(_controller.Context);

                    if (PlayerDead())
                    {
                        ResolveDefeat();
                        return;
                    }
                }
            }

            Log(string.Format("回合{0} 敌人回合结束（玩家 HP {1}/{2}）。", _turnNumber, PlayerHp(), PlayerMaxHp()));

            AfterEnemyTurn();
        }

        /// <summary>
        /// 回合结束结算，随后推进到下一回合。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void EndTurn()`；
        /// 回合末固定次序出自 ARCHITECTURE 第 14.2 / 14.6 节（v1.1 修订、v1.2 改写 §14.2）：
        ///   ① PlayerTurnComponent.EndTurn()
        ///   ② 先手轮换（TurnOrderComponent.AdvanceTurn，消费 AB 的先手状态）
        ///   ③ 状态回合结算：先 SettleRegen（CC / 怪2 的持续回复），再 PlayerStatusComponent.TickStatuses()
        ///      + 所有怪物 StatusComponent.TickTurnEnd()（顺序不可颠倒，见 SettleRegen 注释）
        ///   ④ Cards.CardBuffComponent.TickHandBuffs(_player)  ← 缺此步 AA 强化永不失效（§14.6 第 3 步）
        ///   ⑤ PlayerHandComponent.RemoveTemporaryCards()
        ///   ⑥ PlayerDeckComponent.DrawCards(drawPerTurn)（规则六第 4 条）
        /// 先手轮换被放在状态 tick 之前：§14.4 规定先手标记是 StatusKind.Initiative（remainingTurns = 1），
        /// 若先 tick 再轮换，1 回合的先手状态会在被读取前就到期消失，AB 将彻底失效。
        /// </summary>
        public void EndTurn()
        {
            if (_battleOver)
            {
                return;
            }

            EnsurePrepared();
            if (_controller == null || _controller.Context == null)
            {
                return;
            }

            SetPhase(Core.GamePhase.TurnEnd);

            // ① 玩家回合结束（出牌次数作废、额外次数不跨回合；内部也会清一次临时卡，重复调用无副作用）。
            if (_playerTurn != null)
            {
                _playerTurn.EndTurn();
            }

            // ② 先手轮换：AB 的先手状态在此被读取并消费（必须在状态 tick 之前，见方法注释）。
            if (_turnOrder != null)
            {
                _turnOrder.AdvanceTurn();
            }

            // ③ 状态回合结算：先结算本回合的持续回复（Regen），再递减回合数。
            //    顺序不可颠倒：2 回合的 Regen 必须在本回合末与下一回合末各生效一次
            //    （规则三 CC = 540 立即 + 150×2 = 840；规则四 怪2 = 55 立即 + 28×2）。
            TickPlayerStatuses();
            List<Combat.Combatant> enemies = AllEnemies();
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    TickMonsterStatuses(enemies[i]);
                }
            }

            // ④ 手牌卡强化（AA 的 2 回合倒计时）：ARCHITECTURE 第 14.2 / 14.6 节必修项。
            TickHandCardBuffs();

            // ⑤ 清理本回合未使用的大招临时卡（规则六第 1 条 + ARCHITECTURE 第 13 节第 6 条）。
            if (_hand != null)
            {
                _hand.RemoveTemporaryCards();
            }

            // ⑥ 每回合结束后抽 4 张（受手牌上限 6 张限制）。
            if (_deck != null)
            {
                int drawn = _deck.DrawCards(_deck.drawPerTurn);
                Log(string.Format("回合{0} 结束抽牌：{1} 张（手牌 {2} 张，牌库剩余 {3} 张）。",
                                  _turnNumber, drawn, _hand != null ? _hand.Count : 0, _deck.DeckCount));
            }

            // 玩家死亡 → 失败。
            if (PlayerDead())
            {
                ResolveDefeat();
                return;
            }

            // 本波全灭：还有下一波则切换波次，否则胜利。
            if (WaveCleared())
            {
                if (_level != null && _level.HasNextWave)
                {
                    DoWaveTransition();
                }
                else
                {
                    ResolveVictory();
                    return;
                }
            }

            // 8) 下一回合。
            StartTurn();
        }

        /// <summary>
        /// 胜利结算：怪物全灭且没有下一波。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void ResolveVictory()`。
        /// </summary>
        public void ResolveVictory()
        {
            if (_battleOver)
            {
                return;
            }

            _battleOver = true;
            SetPhase(Core.GamePhase.Victory);
            Log(string.Format("★ 胜利：{0} 全部波次清空（共 {1} 回合，玩家剩余 HP {2}/{3}）。",
                              LevelName(), _turnNumber, PlayerHp(), PlayerMaxHp()));
            ReportBattleEnded(true);
        }

        // ---------------------------------------------------------------- 内部装配与回合链

        /// <summary>开新一局前重置流程状态（由 GameController.SetupBattle 调用）。</summary>
        internal void ResetBattleState()
        {
            _prepared = false;
            _autoDrive = false;
            _battleOver = false;
            _turnNumber = 0;
            _playerActedThisTurn = false;
            _enemyActedThisTurn = false;
            _player = null;
            _playerTurn = null;
            _hand = null;
            _deck = null;
            _fusion = null;
            _turnOrder = null;
            SetPhase(Core.GamePhase.Idle);
        }

        /// <summary>
        /// Setup 阶段（幂等）：解析玩家实体 → 重置先手 → 构建牌库与开局手牌 → 生成第 1 波怪物。
        /// 规则：牌库 A×5、B×5、C×5 共 15 张；开局抽 5 张且保证 ABC 各至少 1 张（规则六第 4 条）。
        /// </summary>
        internal void PrepareBattle()
        {
            EnsureRefs();
            if (_prepared)
            {
                return;
            }

            if (_controller == null || _controller.Context == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.PrepareBattle：BattleContext 尚未建立，无法进入 Setup 阶段。");
                return;
            }

            _prepared = true;
            SetPhase(Core.GamePhase.Setup);

            _player = _controller.Context.Player;
            ResolvePlayerComponents();

            if (_player == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.PrepareBattle：玩家实体为空。");
                return;
            }

            Log(string.Format("战斗准备：{0} HP {1}/{2}，防御 {3}，命中率 {4}。",
                              _player.displayName, _player.CurrentHp, _player.MaxHp,
                              _player.GetDefense(), (int)(_player.GetHitRate() * 100f)));

            if (_turnOrder != null)
            {
                _turnOrder.Reset();   // 规则六第 2 条：初始回合玩家先手
            }
            else
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.PrepareBattle：控制者实体缺少 TurnOrderComponent。");
            }

            if (_deck != null)
            {
                // BuildFromLibrary 内部按规则 A×5/B×5/C×5 构建并洗牌（洗牌走 IRandomSource）。
                _deck.BuildFromLibrary();
                _deck.OpeningDraw();
                Log(string.Format("开局手牌（{0} 张）：{1}", _hand != null ? _hand.Count : 0, HandText()));
            }
            else
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.PrepareBattle：玩家实体缺少 PlayerDeckComponent。");
            }

            SpawnWave();
        }

        /// <summary>
        /// 正式开战：Setup 之后进入第 1 回合（由 GameController.SetupBattle 调用）。
        /// 外部驱动模式下会停在 PlayerTurn，等待外部通过 GameController.PlayCard / TryFuseCards 出牌。
        /// </summary>
        internal void BeginBattle()
        {
            EnsurePrepared();
            if (_battleOver || _turnNumber > 0)
            {
                return;
            }

            Log(string.Format("════ 战斗开始：{0}（共 {1} 波）════", LevelName(), _level != null ? _level.WaveCount : 1));
            StartTurn();
        }

        /// <summary>GameController 每次出牌/合成结算后回调：外部驱动模式下推进回合。</summary>
        internal void NotifyPlayerActionResolved()
        {
            if (_battleOver || _autoDrive)
            {
                return;   // 自动代打模式由 AutoPlayPlayerTurn 负责推进
            }

            if (_phase != Core.GamePhase.PlayerTurn)
            {
                return;
            }

            if (CanPlayerAct())
            {
                return;   // 本回合还能继续行动：等待外部继续出牌
            }

            EndPlayerTurn();
        }

        /// <summary>回合起点：TurnStart，然后由先手方开始行动。</summary>
        private void StartTurn()
        {
            if (_battleOver)
            {
                return;
            }

            if (_turnNumber >= MaxTurnsPerBattle)
            {
                Log(string.Format("回合数达到上限（{0}），战斗强制结束。", MaxTurnsPerBattle));
                if (WaveCleared() && (_level == null || !_level.HasNextWave))
                {
                    ResolveVictory();
                }
                else
                {
                    ResolveDefeat();
                }
                return;
            }

            _turnNumber++;
            _playerActedThisTurn = false;
            _enemyActedThisTurn = false;

            SetPhase(Core.GamePhase.TurnStart);
            bool playerFirst = _turnOrder == null || _turnOrder.PlayerActsFirst;
            Log(string.Format("—— 回合{0} 开始（先手：{1}）——", _turnNumber, playerFirst ? "玩家" : "敌人"));

            if (playerFirst)
            {
                StartPlayerTurn();
            }
            else
            {
                StartEnemyTurn();
            }
        }

        /// <summary>玩家部分结束后：敌人尚未行动则打敌人回合，否则收尾本回合。</summary>
        private void AfterPlayerTurn()
        {
            if (_battleOver)
            {
                return;
            }

            if (!_enemyActedThisTurn)
            {
                if (CountAliveEnemies() > 0)
                {
                    StartEnemyTurn();
                    return;
                }

                _enemyActedThisTurn = true;   // 已无存活敌人，敌人回合空过
            }

            EndTurn();
        }

        /// <summary>敌人回合结束后：玩家尚未行动则进入玩家回合（外部驱动模式下在此挂起），否则收尾本回合。</summary>
        private void AfterEnemyTurn()
        {
            if (_battleOver)
            {
                return;
            }

            if (!_playerActedThisTurn)
            {
                StartPlayerTurn();
                return;
            }

            EndTurn();
        }

        private void EnsurePrepared()
        {
            EnsureRefs();
            if (!_prepared)
            {
                PrepareBattle();
            }
        }

        private void EnsureRefs()
        {
            if (_controller == null)
            {
                _controller = GetComponent<GameController>();
            }

            if (_random == null)
            {
                _random = GetComponent<RandomComponent>();
            }

            if (_level == null)
            {
                _level = GetComponent<LevelComponent>();
            }

            if (_factory == null)
            {
                _factory = GetComponent<BattleFactoryComponent>();
            }

            if (_turnOrder == null)
            {
                _turnOrder = GetComponent<TurnOrderComponent>();
            }
        }

        private void ResolvePlayerComponents()
        {
            if (_player == null)
            {
                _playerTurn = null;
                _hand = null;
                _deck = null;
                _fusion = null;
                return;
            }

            _playerTurn = _player.GetComponent<Players.PlayerTurnComponent>();
            _hand = _player.GetComponent<Players.PlayerHandComponent>();
            _deck = _player.GetComponent<Players.PlayerDeckComponent>();
            _fusion = _player.GetComponent<Cards.CardFusionComponent>();
        }

        // ---------------------------------------------------------------- 波次

        /// <summary>生成本波怪物（清空上一波残留的槽位后按 LevelComponent.BuildWave 出场）。</summary>
        private void SpawnWave()
        {
            if (_controller == null || _controller.Context == null || _factory == null || _level == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.SpawnWave：缺少 BattleContext / BattleFactoryComponent / LevelComponent。");
                return;
            }

            List<Combat.Combatant> slots = WaveSlots();
            if (slots == null)
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.SpawnWave：BattleContext.Enemies 不是可写列表，无法登记怪物。");
                return;
            }

            slots.Clear();

            bool sideLevel = _level.currentLevel == LevelId.SideQuest;
            List<Core.MonsterKind> kinds = _level.BuildWave(_level.CurrentWave);
            if (kinds == null || kinds.Count == 0)
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.SpawnWave：" + _level.currentLevel +
                                           " 第 " + _level.CurrentWave + " 波没有配置怪物。");
                return;
            }

            List<string> names = new List<string>();
            for (int i = 0; i < kinds.Count; i++)
            {
                Combat.Combatant monster = _factory.CreateMonster(kinds[i], sideLevel, _factory.RuntimeRoot);
                if (monster == null)
                {
                    Log(string.Format("第 {0} 波的 {1} 生成失败。", _level.CurrentWave, kinds[i]));
                    continue;
                }

                slots.Add(monster);
                names.Add(string.Format("{0}(HP {1}/防 {2})", monster.displayName, monster.MaxHp, monster.GetDefense()));
            }

            Log(string.Format("第 {0}/{1} 波出场（{2} 只）：{3}",
                              _level.CurrentWave, _level.WaveCount, names.Count, string.Join("、", names.ToArray())));

            if (names.Count == 0)
            {
                UnityEngine.Debug.LogError("[CardGame] GameFlowComponent.SpawnWave：本波没有任何怪物出场（请检查 BattleFactoryComponent 的怪物模板）。");
            }
        }

        /// <summary>
        /// 波次切换（WaveTransition）：清掉上一波阵亡者 → 按规则六第 3 条回复 20% 已损血量 → 生成下一波。
        /// </summary>
        private void DoWaveTransition()
        {
            SetPhase(Core.GamePhase.WaveTransition);

            List<Combat.Combatant> slots = WaveSlots();
            if (slots != null)
            {
                for (int i = slots.Count - 1; i >= 0; i--)
                {
                    Combat.Combatant monster = slots[i];
                    if (monster == null)
                    {
                        slots.RemoveAt(i);
                        continue;
                    }

                    if (!monster.IsAlive)
                    {
                        DestroyEntity(monster.gameObject);
                        slots.RemoveAt(i);
                    }
                }
            }

            if (_level != null)
            {
                _level.AdvanceWave();   // 内部完成 20% 已损血量回复与战报
            }

            SpawnWave();
        }

        // ---------------------------------------------------------------- 玩家自动代打

        /// <summary>
        /// 玩家回合自动代打：反复调用 GameController.TryFuseCards / PlayCard（对外入口）直到无法再行动。
        /// 说明：规则文档未规定玩家策略（第七节的模拟结果按「策略管理」给出），这里采用本方案的默认策略：
        ///   1) 手牌中能合成时优先合成（合成给 1 点充能，是达成大招的主要途径，规则六第 1 条）；
        ///   2) 生命值低于 60% 时优先打治疗卡（C/CC），否则按 大招 &gt; 复制器 &gt; AA &gt; AB &gt; AC &gt; CC &gt; BC &gt; A &gt; C &gt; B &gt; BB。
        /// 每次动作后校验状态是否推进，避免无效动作导致死循环；敌人全灭即停止出牌。
        /// </summary>
        private void AutoPlayPlayerTurn()
        {
            if (_controller == null)
            {
                return;
            }

            int guard = 0;
            while (!_battleOver && guard < MaxActionsPerPlayerTurn)
            {
                guard++;

                if (!CanPlayerAct())
                {
                    break;
                }

                if (!TryAutoAction())
                {
                    break;
                }

                if (WaveCleared())
                {
                    break;   // 本波已清空：不再浪费出牌次数
                }
            }

            if (!_battleOver)
            {
                EndPlayerTurn();
            }
        }

        private bool CanPlayerAct()
        {
            if (_controller == null || _controller.Context == null)
            {
                return false;
            }

            if (_phase != Core.GamePhase.PlayerTurn)
            {
                return false;
            }

            if (_player == null || !_player.IsAlive)
            {
                return false;
            }

            if (_playerTurn != null && !_playerTurn.CanPlay)
            {
                return false;
            }

            if (_hand == null || _hand.Count == 0)
            {
                return false;
            }

            return true;
        }

        private bool TryAutoAction()
        {
            int playsBefore = _playerTurn != null ? _playerTurn.PlaysRemaining : 0;
            int handBefore = _hand != null ? _hand.Count : 0;
            int hpBefore = _player != null ? _player.CurrentHp : 0;
            int chargeBefore = Charge != null ? Charge.charge : 0;

            if (TryAutoFuse())
            {
                return true;
            }

            Cards.CardInstance card = ChooseBestCard();
            if (card == null)
            {
                return false;
            }

            _controller.PlayCard(card);

            int playsAfter = _playerTurn != null ? _playerTurn.PlaysRemaining : 0;
            int handAfter = _hand != null ? _hand.Count : 0;
            int hpAfter = _player != null ? _player.CurrentHp : 0;
            int chargeAfter = Charge != null ? Charge.charge : 0;

            // 任一族值变化即视为动作生效，避免无效动作导致死循环。
            return playsAfter != playsBefore || handAfter != handBefore || hpAfter != hpBefore || chargeAfter != chargeBefore;
        }

        /// <summary>自动合成：在手牌里挑一对权重最高的可合成材料，交给 GameController.TryFuseCards。</summary>
        private bool TryAutoFuse()
        {
            if (_controller == null || _hand == null || _fusion == null)
            {
                return false;
            }

            System.Collections.Generic.IReadOnlyList<Cards.CardInstance> cards = _hand.Cards;
            if (cards == null || cards.Count < 2)
            {
                return false;
            }

            if (_playerTurn != null && !_playerTurn.CanPlay)
            {
                return false;   // 合成消耗 1 次出牌（规则六第 1 条）
            }

            int bestI = -1;
            int bestJ = -1;
            int bestScore = 0;

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null)
                {
                    continue;
                }

                for (int j = i + 1; j < cards.Count; j++)
                {
                    if (cards[j] == null)
                    {
                        continue;
                    }

                    Core.CardKind product;
                    if (!Cards.CardFusionComponent.TryGetRecipe(cards[i].Kind, cards[j].Kind, out product))
                    {
                        continue;
                    }

                    int score = FusionScore(product);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestI = i;
                        bestJ = j;
                    }
                }
            }

            if (bestI < 0 || bestJ < 0)
            {
                return false;
            }

            return _controller.TryFuseCards(cards[bestI], cards[bestJ]);
        }

        /// <summary>合成优先级：AA（伤害 + 手牌强化）&gt; CC（治疗）&gt; AB（先手）&gt; AC（额外出手）&gt; BB &gt; BC。</summary>
        private static int FusionScore(Core.CardKind product)
        {
            switch (product)
            {
                case Core.CardKind.AA:
                    return 60;
                case Core.CardKind.CC:
                    return 50;
                case Core.CardKind.AB:
                    return 40;
                case Core.CardKind.AC:
                    return 35;
                case Core.CardKind.BB:
                    return 20;
                case Core.CardKind.BC:
                    return 10;
            }

            return 0;
        }

        private Cards.CardInstance ChooseBestCard()
        {
            if (_hand == null || _hand.Cards == null)
            {
                return null;
            }

            System.Collections.Generic.IReadOnlyList<Cards.CardInstance> cards = _hand.Cards;
            Cards.CardInstance best = null;
            int bestScore = -1;
            float hpRatio = PlayerHpRatio();

            for (int i = 0; i < cards.Count; i++)
            {
                Cards.CardInstance card = cards[i];
                if (card == null)
                {
                    continue;
                }

                int score = PlayScore(card.Kind, hpRatio);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = card;
                }
            }

            return best;
        }

        /// <summary>出牌优先级（方案默认策略，规则文档未规定）。</summary>
        private static int PlayScore(Core.CardKind kind, float hpRatio)
        {
            if (hpRatio < 0.6f)
            {
                // 低血量优先治疗：C = 300 立即治疗；CC = 540 立即 + 150×2 持续回复。
                if (kind == Core.CardKind.CC)
                {
                    return 100;
                }

                if (kind == Core.CardKind.C)
                {
                    return 90;
                }
            }

            switch (kind)
            {
                case Core.CardKind.Ultimate:
                    return 95;   // 大招：下回合 +2 出手、3 张临时卡、1 张复制器
                case Core.CardKind.Duplicator:
                    return 85;
                case Core.CardKind.AA:
                    return 80;
                case Core.CardKind.AB:
                    return 70;
                case Core.CardKind.AC:
                    return 65;
                case Core.CardKind.CC:
                    return 60;
                case Core.CardKind.BC:
                    return 55;
                case Core.CardKind.A:
                    return 50;
                case Core.CardKind.C:
                    return 45;
                case Core.CardKind.B:
                    return 30;
                case Core.CardKind.BB:
                    return 20;
            }

            return 0;
        }

        // ---------------------------------------------------------------- 判定与工具

        /// <summary>玩家死亡 → 失败（怪物全灭且无下一波 → 胜利，见 ResolveVictory）。</summary>
        private void ResolveDefeat()
        {
            if (_battleOver)
            {
                return;
            }

            _battleOver = true;
            SetPhase(Core.GamePhase.Defeat);
            Log(string.Format("✖ 失败：玩家在第 {0}/{1} 波阵亡（共 {2} 回合，{3}）。",
                              _level != null ? _level.CurrentWave : 0,
                              _level != null ? _level.WaveCount : 0,
                              _turnNumber,
                              LevelName()));
            ReportBattleEnded(false);
        }

        private void ReportBattleEnded(bool victory)
        {
            if (_controller != null)
            {
                _controller.NotifyBattleEnded(victory);
            }
        }

        /// <summary>本波是否已被清空（要求至少登记过 1 只怪物，避免空波误判为胜利）。</summary>
        private bool WaveCleared()
        {
            List<Combat.Combatant> enemies = AllEnemies();
            if (enemies == null || enemies.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                Combat.Combatant monster = enemies[i];
                if (monster != null && monster.IsAlive)
                {
                    return false;
                }
            }

            return true;
        }

        private bool PlayerDead()
        {
            return _player == null || !_player.IsAlive;
        }

        private int CountAliveEnemies()
        {
            List<Combat.Combatant> enemies = AllEnemies();
            if (enemies == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] != null && enemies[i].IsAlive)
                {
                    count++;
                }
            }

            return count;
        }

        private List<Combat.Combatant> AllEnemies()
        {
            if (_controller == null || _controller.Context == null)
            {
                return null;
            }

            return _controller.Context.Enemies as List<Combat.Combatant>;
        }

        private List<Combat.Combatant> WaveSlots()
        {
            return AllEnemies();
        }

        private static void TickStatuses(Combat.Combatant combatant)
        {
            if (combatant == null || combatant.Statuses == null)
            {
                return;
            }

            // 规则：状态持续回合在回合结束时结算（递减，不结算回复量）。
            combatant.Statuses.TickTurnEnd();
        }

        /// <summary>
        /// 结算一次持续回复（Regen）的治疗量。
        /// 必须在 TickTurnEnd() **之前**调用：Regen 的 remainingTurns 表示「还要生效几次」，
        /// 若先递减再结算，2 回合的持续回复只会生效 1 次（CC 会变成 690 而不是 840）。
        /// 规则出处：RULES.md 第三节 CC「回复血量 840 点（C×1.8 + C×0.5×2 = 540 + 300）」、
        ///           第四节 怪2「回复己方全体 55 点生命，下两回合持续恢复 28 点生命」。
        /// 亡者不结算：HealthComponent.ApplyHeal 没有死亡判定，给尸体回血会把它治活。
        /// </summary>
        private void SettleRegen(Combat.Combatant combatant)
        {
            if (combatant == null || !combatant.IsAlive)
            {
                return;
            }

            Combat.StatusComponent statuses = combatant.Statuses;
            if (statuses == null || !statuses.Has(Core.StatusKind.Regen))
            {
                return;
            }

            int regen = Core.GameRules.FloorToInt(statuses.MagnitudeOf(Core.StatusKind.Regen));
            if (regen <= 0)
            {
                return;
            }

            int healed = combatant.Heal(regen);
            if (healed > 0)
            {
                Log(string.Format("回合{0} {1} 持续回复 {2} 点（剩余 HP {3}/{4}）。",
                                  _turnNumber, DisplayNameOf(combatant), healed,
                                  combatant.CurrentHp, combatant.MaxHp));
            }
        }

        /// <summary>
        /// 玩家的状态回合结算：先结算 Regen，再按 ARCHITECTURE 第 14.2 节走
        /// PlayerStatusComponent.TickStatuses()（薄封装，内部转调同一个 Combat.StatusComponent，
        /// 绝不可与它重复调用造成双跳）；玩家实体缺少该封装时才退回 Combat.StatusComponent。
        /// </summary>
        private void TickPlayerStatuses()
        {
            if (_player == null)
            {
                return;
            }

            SettleRegen(_player);

            Players.PlayerStatusComponent playerStatuses = _player.GetComponent<Players.PlayerStatusComponent>();
            if (playerStatuses != null)
            {
                playerStatuses.TickStatuses();
                return;
            }

            TickStatuses(_player);
        }

        /// <summary>怪物的状态回合结算：先结算 Regen，再走 Combat.StatusComponent.TickTurnEnd()。</summary>
        private void TickMonsterStatuses(Combat.Combatant monster)
        {
            if (monster == null)
            {
                return;
            }

            SettleRegen(monster);
            TickStatuses(monster);
        }

        /// <summary>
        /// 手牌卡强化倒计时（AA 的 2 回合强化）。
        /// 契约出处：ARCHITECTURE 第 14.2 节（v1.1 必修项）+ 第 14.6 节（v1.2 改写：改用
        /// Cards.CardBuffComponent.TickHandBuffs —— 契约已撤回 PlayerHandComponent.TickCardBuffs，
        /// 本方法只是调用它的私有封装，与那个被撤回的 API 无关）。缺这一步手牌里的 A 会永久按 440 结算。
        /// </summary>
        private void TickHandCardBuffs()
        {
            if (_player == null)
            {
                return;
            }

            Cards.CardBuffComponent.TickHandBuffs(_player);
        }

        private static string DisplayNameOf(Combat.Combatant combatant)
        {
            if (combatant == null)
            {
                return "?";
            }

            return string.IsNullOrEmpty(combatant.displayName) ? combatant.name : combatant.displayName;
        }

        private Players.PlayerChargeComponent Charge
        {
            get
            {
                return _player != null ? _player.GetComponent<Players.PlayerChargeComponent>() : null;
            }
        }

        private float PlayerHpRatio()
        {
            if (_player == null || _player.MaxHp <= 0)
            {
                return 1f;
            }

            return (float)_player.CurrentHp / (float)_player.MaxHp;
        }

        private int PlayerHp()
        {
            return _player != null ? _player.CurrentHp : 0;
        }

        private int PlayerMaxHp()
        {
            return _player != null ? _player.MaxHp : 0;
        }

        private string HandText()
        {
            if (_hand == null || _hand.Cards == null)
            {
                return string.Empty;
            }

            List<string> names = new List<string>();
            for (int i = 0; i < _hand.Cards.Count; i++)
            {
                Cards.CardInstance card = _hand.Cards[i];
                if (card == null)
                {
                    continue;
                }

                names.Add(card.DisplayName);
            }

            return string.Join("、", names.ToArray());
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

        private string LevelName()
        {
            return _level != null ? LevelNameOf(_level.currentLevel) : "未知关卡";
        }

        private void SetPhase(Core.GamePhase phase)
        {
            if (_phase == phase)
            {
                return;
            }

            _phase = phase;

            System.Action<Core.GamePhase> handler = PhaseChanged;
            if (handler != null)
            {
                handler(_phase);
            }
        }

        private static void DestroyEntity(UnityEngine.GameObject go)
        {
            if (go == null)
            {
                return;
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

        /// <summary>战报输出：优先走 BattleContext（同时写入 OnLog 与 Unity 控制台）。</summary>
        private void Log(string message)
        {
            if (_controller != null && _controller.Context != null)
            {
                _controller.Context.Log(message);
                return;
            }

            UnityEngine.Debug.Log("[CardGame] " + message);
        }
    }
}
