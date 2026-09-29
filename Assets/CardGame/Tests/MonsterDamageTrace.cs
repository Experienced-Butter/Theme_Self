using System.Collections.Generic;
using System.Text;
using CardGame.Combat;
using CardGame.Core;
using CardGame.Monsters;
using UnityEngine;

namespace CardGame.Tests
{
    /// <summary>
    /// 敌方伤害经济追踪（玩家 HP 侧完全确定性：伤害=攻击-防御、命中 100%、无随机）。
    /// 目的：把「每回合应承受多少伤害」变成可手算的断言，而不是只看胜率。
    ///
    /// 规则依据：RULES.md 第四节（怪1 攻击 580 / 怪2 攻击 350）、第六节第 5、6 条
    /// （伤害=攻击-防御、固定出招顺序：怪1 普攻→普攻→减防35、怪2 普攻→辅助→治疗）。
    ///
    /// 用法（编辑器菜单/脚本均可调用）：
    ///   Debug.Log(MonsterDamageTrace.Run(LevelId.Level1, 777));
    /// 该追踪**不**驱动玩家出牌，敌人一直打到玩家阵亡为止，因此给出的是「玩家不还手时的
    /// 承伤曲线」：第 1 回合末 = 双怪各 1 次普攻，之后按序列循环。
    /// </summary>
    public static class MonsterDamageTrace
    {
        /// <summary>
        /// 敌方伤害链路是否消耗随机数：每只怪每回合用一只**满血新怪**执行 1 次行动，
        /// 因此不会被玩家死亡截断，序列从索引 0 起严格循环（怪1：攻→攻→减防）。
        /// 预期消耗 = 普攻次数 ×1 + 其他行动 ×0（历史缺陷：玩家出牌曾按目标命中率做判定，已由 t9 移除）。
        /// </summary>
        public static int CountRandomCalls(int monsterCount, int turns, int seed)
        {
            CountingRandom counting = new CountingRandom(seed);
            GameObjects collected = new GameObjects();
            try
            {
                for (int turn = 0; turn < turns; turn++)
                {
                    GameObject playerObject = new GameObject("RngPlayer" + turn);
                    collected.Add(playerObject);
                    Combatant player = BuildPlayer(playerObject, collected);
                    List<Combatant> monsters = new List<Combatant>();
                    for (int m = 0; m < monsterCount; m++)
                    {
                        GameObject monsterObject = new GameObject("RngAttacker" + turn + "_" + m);
                        collected.Add(monsterObject);
                        monsters.Add(BuildMonster(monsterObject, collected, MonsterKind.Attacker, false));
                    }

                    BattleContext battle = new BattleContext(counting, player, monsters, null);
                    for (int i = 0; i < monsters.Count; i++)
                    {
                        Monsters.MonsterActionComponent action = monsters[i].GetComponent<Monsters.MonsterActionComponent>();
                        if (action != null && monsters[i].IsAlive)
                        {
                            action.ExecuteTurn(battle);
                        }
                    }
                }
            }
            finally
            {
                collected.DestroyAll();
            }

            return counting.Calls;
        }

        /// <summary>只统计调用次数的 IRandomSource 包装（不改动被测代码）。</summary>
        private sealed class CountingRandom : IRandomSource
        {
            private readonly IRandomSource _inner;
            public int Calls;

            public CountingRandom(int seed) { _inner = new SeededRandom(seed); }

            public int Seed { get { return _inner.Seed; } }

            public int Range(int minInclusive, int maxExclusive)
            {
                Calls++;
                return _inner.Range(minInclusive, maxExclusive);
            }

            public float Value01()
            {
                Calls++;
                return _inner.Value01();
            }

            public bool Chance(float probability)
            {
                Calls++;
                return _inner.Chance(probability);
            }

            public void Shuffle<T>(System.Collections.Generic.IList<T> list)
            {
                Calls++;
                _inner.Shuffle(list);
            }
        }
        /// <summary>按关卡跑一条逐回合敌方承伤追踪，返回多行文本。</summary>
        public static string Run(Controllers.LevelId level, int seed)
        {
            IRandomSource random = new SeededRandom(seed);
            GameObject playerObject = new GameObject("TracePlayer");
            GameObjects collected = new GameObjects();
            List<Combatant> monsters = new List<Combatant>();
            try
            {
                Combatant player = BuildPlayer(playerObject, collected);
                List<MonsterKind> kinds = WaveKinds(level);
                for (int i = 0; i < kinds.Count; i++)
                {
                    GameObject monsterObject = new GameObject("Trace" + kinds[i]);
                    collected.Add(monsterObject);
                    monsters.Add(BuildMonster(monsterObject, collected, kinds[i], LevelUsesSideHp(level)));
                }

                return Trace(random, player, monsters, level, seed);
            }
            finally
            {
                collected.DestroyAll();
            }
        }

        // ---------------------------------------------------------------- 追踪主体

        private static string Trace(IRandomSource random, Combatant player, List<Combatant> monsters,
                                    Controllers.LevelId level, int seed)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========== 敌方伤害经济追踪 ==========");
            sb.Append("level=").Append(level).Append("   seed=").Append(seed)
              .Append("   玩家 HP ").Append(player.CurrentHp).Append('/').Append(player.MaxHp)
              .Append("   防御 ").Append(player.GetDefense())
              .Append("   命中 ").Append(player.GetHitRate().ToString("0.00"))
              .AppendLine();
            sb.AppendLine("手算基准: 怪1 普攻 580 - 防御 300 = 280；被减防35 后 580 - 265 = 315");
            sb.AppendLine("--------------------------------------------------");

            BattleContext battle = new BattleContext(random, player, monsters, null);
            int totalDamage = 0;
            const int maxTurns = 40;
            for (int turn = 1; turn <= maxTurns; turn++)
            {
                if (!player.IsAlive)
                {
                    break;
                }

                int hpBefore = player.CurrentHp;
                int defenseBefore = player.GetDefense();
                sb.Append("回合 ").Append(turn)
                  .Append("  开始 HP ").Append(hpBefore)
                  .Append("  防御 ").Append(defenseBefore)
                  .AppendLine();

                for (int i = 0; i < monsters.Count; i++)
                {
                    Combatant monster = monsters[i];
                    if (!monster.IsAlive || !player.IsAlive)
                    {
                        continue;
                    }

                    MonsterActionComponent action = monster.GetComponent<MonsterActionComponent>();
                    string actionName = PeekNextAction(action);
                    int hpBeforeAction = player.CurrentHp;
                    action.ExecuteTurn(battle);
                    int dealt = hpBeforeAction - player.CurrentHp;
                    totalDamage += dealt;

                    sb.Append("    ").Append(monster.displayName)
                      .Append(" 出招 ").Append(actionName)
                      .Append("  -> 玩家扣血 ").Append(dealt)
                      .Append("  (命中率 ").Append(monster.GetHitRate().ToString("0.00")).Append(')')
                      .Append("  剩余 HP ").Append(player.CurrentHp)
                      .Append("  防御 ").Append(player.GetDefense())
                      .AppendLine();
                }

                sb.Append("  回合 ").Append(turn).Append(" 结束 HP ").Append(player.CurrentHp)
                  .Append("  本回合扣血 ").Append(hpBefore - player.CurrentHp)
                  .AppendLine();
                sb.AppendLine();

                if (player.CurrentHp <= 0)
                {
                    sb.Append("玩家在第 ").Append(turn).Append(" 回合阵亡。").AppendLine();
                    break;
                }
            }

            int turnsSurvived = 0;
            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterActionComponent action = monsters[i].GetComponent<MonsterActionComponent>();
                turnsSurvived += NextActionIndex(action);
            }

            sb.AppendLine("--------------------------------------------------");
            sb.Append("累计承受伤害 = ").Append(totalDamage)
              .Append("   玩家剩余 HP = ").Append(player.CurrentHp)
              .Append('/').Append(player.MaxHp)
              .AppendLine();
            sb.AppendLine("说明：本追踪不驱动玩家出牌，所以这是「玩家完全不还手」的承伤曲线；");
            sb.AppendLine("      任何一次普攻都必须是 280（或减防后 315），否则即为机制缺陷。");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 断言

        /// <summary>逐条断言敌方伤害经济（可手算），返回空列表表示全部通过。</summary>
        public static List<string> Verify(Controllers.LevelId level, int seed)
        {
            List<string> failures = new List<string>();
            IRandomSource random = new SeededRandom(seed);

            GameObject playerObject = new GameObject("VerifyPlayer");
            GameObjects collected = new GameObjects();
            List<Combatant> monsters = new List<Combatant>();
            try
            {
                Combatant player = BuildPlayer(playerObject, collected);
                List<MonsterKind> kinds = WaveKinds(level);
                for (int i = 0; i < kinds.Count; i++)
                {
                    GameObject monsterObject = new GameObject("Verify" + kinds[i]);
                    collected.Add(monsterObject);
                    monsters.Add(BuildMonster(monsterObject, collected, kinds[i], LevelUsesSideHp(level)));
                }

                BattleContext battle = new BattleContext(random, player, monsters, null);

                // 1) 玩家基础防御必须是 300，怪1 普攻应扣 280。
                if (player.GetDefense() != GameRules.PlayerDefense)
                {
                    failures.Add("玩家初始有效防御 " + player.GetDefense() + " != " + GameRules.PlayerDefense);
                }
                int cost = GameRules.ComputeMonsterDamage(GameRules.Monster1Attack, player.GetDefense());
                if (cost != 280)
                {
                    failures.Add("怪1 普攻应扣 280，实际公式给出 " + cost);
                }

                // 2) 三只怪全部存在时，第 1 回合的伤害必须等于存活怪物数 × 280/350（怪2 走辅助序列第 1 招也是普攻）。
                int monsterCount = monsters.Count;
                int hpBefore = player.CurrentHp;
                int attacked = 0;
                for (int i = 0; i < monsters.Count; i++)
                {
                    MonsterActionComponent action = monsters[i].GetComponent<MonsterActionComponent>();
                    if (PeekNextAction(action) == MonsterAction.Attack.ToString())
                    {
                        attacked++;
                    }
                }
                for (int i = 0; i < monsters.Count; i++)
                {
                    monsters[i].GetComponent<MonsterActionComponent>().ExecuteTurn(battle);
                }
                int roundOneDamage = hpBefore - player.CurrentHp;
                int expectedRoundOne = 0;
                for (int i = 0; i < monsters.Count; i++)
                {
                    int attack = AttackOf(monsters[i]);
                    expectedRoundOne += GameRules.ComputeMonsterDamage(attack, GameRules.PlayerDefense);
                }
                if (roundOneDamage != expectedRoundOne)
                {
                    failures.Add("第 1 回合双怪普攻应扣 " + expectedRoundOne + "，实际扣 " + roundOneDamage);
                }
                if (attacked != monsterCount)
                {
                    failures.Add("第 1 回合应有 " + monsterCount + " 只怪普攻，实际 " + attacked + " 只");
                }

                // 3) 怪物命中率必须 100%（规则第四节）。
                for (int i = 0; i < monsters.Count; i++)
                {
                    if (monsters[i].GetHitRate() < 0.999f)
                    {
                        failures.Add(monsters[i].displayName + " 命中率应为 100%，实际 " + monsters[i].GetHitRate());
                    }
                }

                // 4b) 随机数消耗口径（回归护栏，t9 的根因就是在伤害路径里多算了概率判定）：
                //     探针每回合用满血新怪，索引恒为 0 → 每回合都是普攻，故 6 回合 × 1 只 = 6 次。
                int probeTurns = 6;
                int rngCalls = CountRandomCalls(1, probeTurns, seed);
                if (rngCalls != probeTurns)
                {
                    failures.Add("单怪 " + probeTurns + " 次普攻的 RNG 调用数应为 " + probeTurns + "（每次普攻恰好掷 1 次），实际 " + rngCalls);
                }
                // 4) 怪1 减防后：防御应降 35，普攻扣血应升到 315。
                Combatant attacker = null;
                for (int i = 0; i < monsters.Count; i++)
                {
                    if (AttackOf(monsters[i]) == GameRules.Monster1Attack)
                    {
                        attacker = monsters[i];
                        break;
                    }
                }
                if (attacker != null)
                {
                    MonsterActionComponent action = attacker.GetComponent<MonsterActionComponent>();
                    // 怪1 序列 普攻→普攻→减防：推进到第 3 招。
                    action.ExecuteTurn(battle);
                    action.ExecuteTurn(battle);
                    int defenseBefore = player.GetDefense();
                    action.ExecuteTurn(battle);
                    int defenseAfter = player.GetDefense();
                    // v1.2 正式契约：同类状态取最大值 → 两只怪同回合各减防 35 只算一次（265 = 300-35，
                    // 而不是 230 = 300-35-35）。这里断言「减 35 且不叠加」，并显式记录这条假设。
                    int expectedDefense = GameRules.PlayerDefense - GameRules.Monster1DefenseBreakFlat;
                    if (defenseAfter != expectedDefense)
                    {
                        failures.Add("怪1 减防后防御应为 " + expectedDefense + "（不叠加），实际 " + defenseAfter);
                    }

                    int hpBeforeAttack = player.CurrentHp;
                    action.ExecuteTurn(battle);   // 回到序列第 1 招（普攻）
                    int damage = hpBeforeAttack - player.CurrentHp;
                    int expected = GameRules.ComputeMonsterDamage(GameRules.Monster1Attack, defenseAfter);
                    if (damage != expected)
                    {
                        failures.Add("减防后怪1 普攻应扣 " + expected + "，实际扣 " + damage);
                    }
                    if (expected != 315)
                    {
                        failures.Add("减防后怪1 普攻手算值应为 315，公式给出 " + expected);
                    }
                }
            }
            finally
            {
                collected.DestroyAll();
            }

            return failures;
        }

        // ---------------------------------------------------------------- 构建与工具

        private sealed class GameObjects
        {
            private readonly List<GameObject> _objects = new List<GameObject>();
            public void Add(GameObject go) { if (go != null) { _objects.Add(go); } }
            public void DestroyAll()
            {
                for (int i = _objects.Count - 1; i >= 0; i--)
                {
                    if (_objects[i] != null)
                    {
                        Object.DestroyImmediate(_objects[i]);
                    }
                }
                _objects.Clear();
            }
        }

        private static Combatant BuildPlayer(GameObject go, GameObjects collected)
        {
            collected.Add(go);
            Combatant combatant = go.AddComponent<Combatant>();
            combatant.team = Team.Player;
            combatant.displayName = "玩家";
            HealthComponent health = go.AddComponent<HealthComponent>();
            DefenseComponent defense = go.AddComponent<DefenseComponent>();
            HitRateComponent hitRate = go.AddComponent<HitRateComponent>();
            go.AddComponent<StatusComponent>();
            health.Initialize(GameRules.PlayerMaxHp);
            defense.baseDefense = GameRules.PlayerDefense;
            hitRate.baseHitRate = GameRules.PlayerHitRate;
            return combatant;
        }

        private static Combatant BuildMonster(GameObject go, GameObjects collected, MonsterKind kind, bool sideLevel)
        {
            collected.Add(go);
            bool attacker = kind == MonsterKind.Attacker;
            int maxHp = attacker
                ? (sideLevel ? GameRules.Monster1HpSide : GameRules.Monster1HpNormal)
                : GameRules.Monster2Hp;
            int attack = attacker ? GameRules.Monster1Attack : GameRules.Monster2Attack;

            Combatant combatant = go.AddComponent<Combatant>();
            combatant.team = Team.Enemy;
            combatant.displayName = attacker ? "怪物1（攻击型）" : "怪物2（辅助型）";
            HealthComponent health = go.AddComponent<HealthComponent>();
            DefenseComponent defense = go.AddComponent<DefenseComponent>();
            HitRateComponent hitRate = go.AddComponent<HitRateComponent>();
            go.AddComponent<StatusComponent>();
            health.Initialize(maxHp);
            defense.baseDefense = attacker ? GameRules.Monster1Defense : GameRules.Monster2Defense;
            hitRate.baseHitRate = 1f;

            MonsterAttackComponent attackComponent = go.AddComponent<MonsterAttackComponent>();
            attackComponent.attack = attack;
            MonsterActionComponent action = go.AddComponent<MonsterActionComponent>();
            action.sequence = attacker
                ? new MonsterAction[] { MonsterAction.Attack, MonsterAction.Attack, MonsterAction.DefenseBreak }
                : new MonsterAction[] { MonsterAction.Attack, MonsterAction.Support, MonsterAction.Heal };

            if (attacker)
            {
                MonsterDefenseBreakComponent defenseBreak = go.AddComponent<MonsterDefenseBreakComponent>();
                defenseBreak.flatReduction = GameRules.Monster1DefenseBreakFlat;
                defenseBreak.turns = GameRules.Monster1DefenseBreakTurns;
                go.AddComponent<MonsterHealComponent>();
            }
            else
            {
                MonsterHealComponent heal = go.AddComponent<MonsterHealComponent>();
                heal.instant = GameRules.Monster2HealInstant;
                heal.perTurn = GameRules.Monster2HealPerTurn;
                heal.turns = GameRules.Monster2HealTurns;
                go.AddComponent<MonsterSupportComponent>();
            }

            return combatant;
        }

        private static List<MonsterKind> WaveKinds(Controllers.LevelId level)
        {
            switch (level)
            {
                case Controllers.LevelId.Level1:
                    return new List<MonsterKind> { MonsterKind.Attacker, MonsterKind.Attacker };
                case Controllers.LevelId.Level2:
                    return new List<MonsterKind> { MonsterKind.Support, MonsterKind.Attacker, MonsterKind.Attacker };
                default:
                    return new List<MonsterKind> { MonsterKind.Attacker, MonsterKind.Attacker, MonsterKind.Support };
            }
        }

        private static bool LevelUsesSideHp(Controllers.LevelId level)
        {
            return level == Controllers.LevelId.SideQuest;
        }

        private static int AttackOf(Combatant monster)
        {
            MonsterAttackComponent attack = monster.GetComponent<MonsterAttackComponent>();
            return attack != null ? attack.attack : 0;
        }

        private static string PeekNextAction(MonsterActionComponent action)
        {
            if (action == null || action.sequence == null || action.sequence.Length == 0)
            {
                return "(无序列)";
            }
            int index = NextActionIndex(action);
            return action.sequence[index].ToString();
        }

        private static int NextActionIndex(MonsterActionComponent action)
        {
            if (action == null || action.sequence == null || action.sequence.Length == 0)
            {
                return 0;
            }
            System.Reflection.FieldInfo field = typeof(MonsterActionComponent).GetField(
                "actionIndex",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
            int index = field != null ? (int)field.GetValue(action) : 0;
            if (index < 0 || index >= action.sequence.Length)
            {
                index = 0;
            }
            return index;
        }


    }
}
