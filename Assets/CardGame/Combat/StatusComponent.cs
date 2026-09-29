using System.Collections.Generic;
using UnityEngine;

namespace CardGame.Combat
{
    /// <summary>
    /// 通用状态组件。玩家与怪物共用，承载命中下降、防御下降、持续回复、伤害免疫等状态。
    /// </summary>
    public class StatusComponent : MonoBehaviour
    {
        private readonly List<Core.StatusEffect> _statuses = new List<Core.StatusEffect>();

        /// <summary>当前全部状态（只读视图）。</summary>
        public IReadOnlyList<Core.StatusEffect> Statuses
        {
            get { return _statuses; }
        }

        /// <summary>
        /// 施加一个状态。
        /// 同类已存在时的合并规则（ARCHITECTURE 第 4 节「MagnitudeOf 同类取最大值」）：
        /// 数值取较大者，剩余回合取较长者，避免新状态削弱或缩短旧状态。
        /// 本规则对全部 StatusKind 一致，不按数值大小猜测类型；
        /// 这与读取端 MagnitudeOf 完全自洽（重复施加同一状态取最大值，而非叠加）。
        /// </summary>
        public void Add(Core.StatusKind kind, float magnitude, int turns, bool fromPlayer)
        {
            if (turns <= 0)
            {
                return;
            }

            Core.StatusEffect existing = Find(kind);
            if (existing == null)
            {
                Core.StatusEffect effect = new Core.StatusEffect(kind, magnitude, turns);
                effect.fromPlayer = fromPlayer;
                _statuses.Add(effect);
                return;
            }

            if (magnitude > existing.magnitude)
            {
                existing.magnitude = magnitude;
            }
            if (turns > existing.remainingTurns)
            {
                existing.remainingTurns = turns;
            }
            existing.fromPlayer = fromPlayer;
        }

        /// <summary>是否存在该类状态（仅计数，不检查剩余回合）。</summary>
        public bool Has(Core.StatusKind kind)
        {
            return Find(kind) != null;
        }

        /// <summary>同类状态取最大值；不存在时返回 0。</summary>
        public float MagnitudeOf(Core.StatusKind kind)
        {
            float best = 0f;
            for (int i = 0; i < _statuses.Count; i++)
            {
                if (_statuses[i].kind == kind && _statuses[i].magnitude > best)
                {
                    best = _statuses[i].magnitude;
                }
            }
            return best;
        }

        /// <summary>移除一层该类状态，返回是否确有可移除的状态。</summary>
        public bool Consume(Core.StatusKind kind)
        {
            int index = IndexOfValue(kind);
            if (index < 0)
            {
                return false;
            }
            _statuses.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// 回合结束时统一结算：所有状态剩余回合 -1，移除到期的，返回移除数量。
        /// </summary>
        public int TickTurnEnd()
        {
            int removed = 0;
            for (int i = _statuses.Count - 1; i >= 0; i--)
            {
                _statuses[i].remainingTurns--;
                if (_statuses[i].remainingTurns <= 0)
                {
                    _statuses.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>清空全部状态。</summary>
        public void Clear()
        {
            _statuses.Clear();
        }

        private int IndexOfValue(Core.StatusKind kind)
        {
            for (int i = 0; i < _statuses.Count; i++)
            {
                if (_statuses[i].kind == kind)
                {
                    return i;
                }
            }
            return -1;
        }

        private Core.StatusEffect Find(Core.StatusKind kind)
        {
            int index = IndexOfValue(kind);
            return index < 0 ? null : _statuses[index];
        }
    }
}
