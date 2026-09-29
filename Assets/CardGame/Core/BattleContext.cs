using CardGame.Combat;

namespace CardGame.Core
{
    /// <summary>
    /// 一场战斗的共享上下文：随机数源、参战双方、控制器与日志出口。
    /// 由 GameController 创建，卡牌行为、怪物行为都通过它访问战场。
    /// </summary>
    public sealed class BattleContext
    {
        private readonly System.Collections.Generic.IReadOnlyList<Combatant> _enemies;

        /// <summary>随机数唯一来源。</summary>
        public IRandomSource Random { get; private set; }

        /// <summary>玩家战斗单位。</summary>
        public Combatant Player { get; private set; }

        /// <summary>场上全部敌人（含已阵亡者，存活判断请用 AliveEnemies）。</summary>
        public System.Collections.Generic.IReadOnlyList<Combatant> Enemies
        {
            get { return _enemies; }
        }

        /// <summary>控制者实体，用于触发抽牌、充能、回合推进等跨层操作。</summary>
        public Controllers.GameController Controller { get; private set; }

        /// <summary>战报转发出口，由 GameController 挂接。</summary>
        public System.Action<string> Logger { get; set; }

        public BattleContext(IRandomSource random, Combatant player,
                             System.Collections.Generic.IReadOnlyList<Combatant> enemies,
                             Controllers.GameController controller)
        {
            if (random == null)
            {
                throw new System.ArgumentNullException("random");
            }
            if (player == null)
            {
                throw new System.ArgumentNullException("player");
            }
            Random = random;
            Player = player;
            _enemies = enemies ?? new System.Collections.Generic.List<Combatant>();
            Controller = controller;
        }

        /// <summary>当前存活的敌人，按 Enemies 原顺序。</summary>
        public System.Collections.Generic.List<Combatant> AliveEnemies()
        {
            System.Collections.Generic.List<Combatant> result =
                new System.Collections.Generic.List<Combatant>();
            for (int i = 0; i < _enemies.Count; i++)
            {
                Combatant enemy = _enemies[i];
                if (enemy != null && enemy.IsAlive)
                {
                    result.Add(enemy);
                }
            }
            return result;
        }

        /// <summary>who 的同阵营单位（玩家侧只有玩家自己；敌人侧为其余敌人，含已阵亡者）。</summary>
        public System.Collections.Generic.List<Combatant> AlliesOf(Combatant who)
        {
            System.Collections.Generic.List<Combatant> result =
                new System.Collections.Generic.List<Combatant>();
            if (who == null)
            {
                return result;
            }
            if (IsPlayerSide(who))
            {
                result.Add(Player);
                return result;
            }
            for (int i = 0; i < _enemies.Count; i++)
            {
                Combatant enemy = _enemies[i];
                if (enemy != null && !ReferenceEquals(enemy, who))
                {
                    result.Add(enemy);
                }
            }
            return result;
        }

        /// <summary>who 的敌对阵营单位（玩家侧为全部敌人，含已阵亡者；敌人侧为玩家）。</summary>
        public System.Collections.Generic.List<Combatant> EnemiesOf(Combatant who)
        {
            System.Collections.Generic.List<Combatant> result =
                new System.Collections.Generic.List<Combatant>();
            if (IsPlayerSide(who))
            {
                for (int i = 0; i < _enemies.Count; i++)
                {
                    Combatant enemy = _enemies[i];
                    if (enemy != null)
                    {
                        result.Add(enemy);
                    }
                }
                return result;
            }
            if (Player != null)
            {
                result.Add(Player);
            }
            return result;
        }

        /// <summary>转发给 Logger，同时输出到 Unity 控制台，便于直接 Play 观察战报。</summary>
        public void Log(string message)
        {
            if (Logger != null)
            {
                Logger(message);
            }
            UnityEngine.Debug.Log("[CardGame] " + message);
        }

        private bool IsPlayerSide(Combatant who)
        {
            if (who == null || Player == null)
            {
                return false;
            }
            if (ReferenceEquals(who, Player))
            {
                return true;
            }
            return who.team == Team.Player;
        }
    }
}
