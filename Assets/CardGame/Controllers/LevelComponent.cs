using System.Collections.Generic;

namespace CardGame.Controllers
{
    /// <summary>
    /// 关卡与波次。
    /// 规则出处：RULES.md 第五节「关卡配置」、第六节第 3 条「波次回复机制（仅波次关卡生效）：
    ///          当完成一个波次进入下一波时，恢复 20% 已损血量」。
    /// 三张关卡表：
    ///   关一  Level1    第1波：怪物1(2000血/防0)×2，单波战斗；
    ///   关二  Level2    第1波：怪物2(1000血/防25) + 怪物1(2000血/防0)×2，单波战斗；
    ///   支线关 SideQuest 第1波：怪物1(1600血/防0)×2 + 怪物2(1000血/防25)；
    ///                   第2波：怪物1(1600血/防0)×2，波次制。
    /// 契约出处：ARCHITECTURE.md 第 8 节 `Controllers/LevelComponent.cs`。
    /// </summary>
    public class LevelComponent : UnityEngine.MonoBehaviour
    {
        /// <summary>当前关卡。契约中的公开字段。</summary>
        public LevelId currentLevel = LevelId.Level1;

        private int _currentWave = 1;

        /// <summary>当前波次（从 1 开始）。</summary>
        public int CurrentWave
        {
            get { return _currentWave; }
        }

        /// <summary>当前关卡的总波次数（关一/关二 = 1，支线关 = 2）。</summary>
        internal int WaveCount
        {
            get { return WaveCountOf(currentLevel); }
        }

        /// <summary>是否还有下一波（决定「全灭 → 波次切换」还是「全灭 → 胜利」）。</summary>
        public bool HasNextWave
        {
            get { return _currentWave < WaveCountOf(currentLevel); }
        }

        /// <summary>
        /// 载入关卡并回到第 1 波。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void LoadLevel(LevelId id)`。
        /// </summary>
        public void LoadLevel(LevelId id)
        {
            currentLevel = id;
            _currentWave = 1;
            Log(string.Format("载入关卡：{0}（共 {1} 波）。", LevelNameOf(id), WaveCountOf(id)));
        }

        /// <summary>
        /// 推进到下一波，并按规则六第 3 条回复玩家 20% 已损血量：
        /// 回复量 = GameRules.ComputeWaveRecovery(maxHp, currentHp) = floor((maxHp - currentHp) × 20%)。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `void AdvanceWave()`；方案默认见第 13 节第 9 条。
        /// </summary>
        public void AdvanceWave()
        {
            if (!HasNextWave)
            {
                Log(string.Format("{0} 已经是最后一波，无法继续推进波次。", LevelNameOf(currentLevel)));
                return;
            }

            _currentWave++;

            Combat.Combatant player = ResolvePlayer();
            if (player == null)
            {
                Log("波次切换：找不到玩家实体，本次波次回复未生效。");
                return;
            }

            int maxHp = player.MaxHp;
            int currentHp = player.CurrentHp;
            int lost = maxHp - currentHp;
            if (lost < 0)
            {
                lost = 0;
            }

            int recovery = Core.GameRules.ComputeWaveRecovery(maxHp, currentHp);
            int healed = 0;
            if (recovery > 0)
            {
                healed = player.Heal(recovery);
            }

            Log(string.Format("波次切换：进入第 {0}/{1} 波，玩家回复 {2} 点生命（已损 {3} × {4}%），当前 {5}/{6}。",
                              _currentWave, WaveCountOf(currentLevel), healed, lost,
                              (int)(Core.GameRules.WaveRecoveryPercent * 100f), player.CurrentHp, player.MaxHp));
        }

        /// <summary>
        /// 返回指定波次的怪物种类表（按出场顺序）。
        /// 契约出处：ARCHITECTURE.md 第 8 节 `List&lt;MonsterKind&gt; BuildWave(int wave)`。
        /// 怪物1 = MonsterKind.Attacker，怪物2 = MonsterKind.Support。
        /// </summary>
        public List<Core.MonsterKind> BuildWave(int wave)
        {
            List<Core.MonsterKind> result = new List<Core.MonsterKind>();

            switch (currentLevel)
            {
                case LevelId.Level1:
                    // 规则：关一 怪物1(2000血/防0)×2，单波战斗。
                    if (wave == 1)
                    {
                        result.Add(Core.MonsterKind.Attacker);
                        result.Add(Core.MonsterKind.Attacker);
                    }
                    break;

                case LevelId.Level2:
                    // 规则：关二 怪物2(1000血/防25) + 怪物1(2000血/防0)×2，单波战斗。
                    if (wave == 1)
                    {
                        result.Add(Core.MonsterKind.Support);
                        result.Add(Core.MonsterKind.Attacker);
                        result.Add(Core.MonsterKind.Attacker);
                    }
                    break;

                case LevelId.SideQuest:
                    // 规则：支线关第1波 怪物1(1600血/防0)×2 + 怪物2(1000血/防25)。
                    if (wave == 1)
                    {
                        result.Add(Core.MonsterKind.Attacker);
                        result.Add(Core.MonsterKind.Attacker);
                        result.Add(Core.MonsterKind.Support);
                    }
                    // 规则：支线关第2波 怪物1(1600血/防0)×2。
                    else if (wave == 2)
                    {
                        result.Add(Core.MonsterKind.Attacker);
                        result.Add(Core.MonsterKind.Attacker);
                    }
                    break;
            }

            return result;
        }

        private static int WaveCountOf(LevelId id)
        {
            return id == LevelId.SideQuest ? 2 : 1;
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

        private static Combat.Combatant ResolvePlayer()
        {
            GameController controller = GameController.Instance;
            if (controller == null || controller.Context == null)
            {
                return null;
            }

            return controller.Context.Player;
        }

        /// <summary>战报输出：优先走 BattleContext（同时写入 OnLog 与 Unity 控制台）。</summary>
        private void Log(string message)
        {
            GameController controller = GameController.Instance;
            if (controller != null && controller.Context != null)
            {
                controller.Context.Log(message);
                return;
            }

            UnityEngine.Debug.Log("[CardGame] " + message);
        }
    }
}
