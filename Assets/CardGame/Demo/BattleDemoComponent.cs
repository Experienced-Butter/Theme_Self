using System.Collections;
using System.Collections.Generic;
using CardGame.Bootstrap;
using CardGame.Cards;
using CardGame.Combat;
using CardGame.Controllers;
using CardGame.Core;
using CardGame.Players;
using UnityEngine;

namespace CardGame.Demo
{
    /// <summary>
    /// 一局对局的视觉化演示。
    ///
    /// 核心约定（按需求）：
    ///   · **出牌操作固定** —— 使用本文件里的固定脚本 <see cref="Script"/>，不依赖 AI 策略；
    ///   · **随机数固定** —— 固定种子（默认 20260929）交给 SetupBattle → RandomComponent，
    ///     所以每次演示的抽牌、命中判定完全相同；
    ///   · **每个操作间隔 1.5 秒** —— stepDelay，期间用简单的位置插值把卡牌从手牌飞到出牌区。
    ///
    /// 战斗是真的：走 GameController.PlayCard / TryFuseCards，规则与数值全部来自 Core。
    /// 本组件只负责「摆画面、做动画、显示数字」，不参与任何规则判定。
    ///
    /// 启动：按 Play（autoStartOnPlay），或用编辑器菜单 CardGame/演示一局对局，
    /// 或在本组件右键菜单选「开始演示对局」。
    /// </summary>
    public class BattleDemoComponent : MonoBehaviour
    {
        [Header("演示参数")]
        [Tooltip("演示关卡。")]
        public LevelId level = LevelId.Level1;

        [Tooltip("固定随机种子：保证每次演示的抽牌完全一致。")]
        public int seed = 20260929;

        [Tooltip("每个操作之间的间隔（秒）。")]
        public float stepDelay = 1.5f;

        [Tooltip("单张卡牌移动动画时长（秒），占用 stepDelay 的一部分。")]
        public float moveDuration = 0.5f;

        [Tooltip("进入 Play 时自动开始演示。")]
        public bool autoStartOnPlay = true;

        [Tooltip("演示开始时自动隐藏「卡牌一览」卡墙，避免两套内容叠在一起。")]
        public bool hideGalleryOnStart = true;

        [Header("相机")]
        [Tooltip("演示开始时自动取景，让整个演示台完整入镜。")]
        public bool frameCamera = true;

        [Tooltip("手牌文字朝向（绕 Y 轴角度）。180 = 贴在朝 -Z 的那一面，配合上面的取景才是正读的；" +
                 "发现字反了就改成 0。")]
        public float handCardTextYaw = 180f;

        [Header("战报文字")]
        [Tooltip("战报每行独立成一个对象，行与行之间的间距（世界单位），留足避免互相遮挡。")]
        public float logLineSpacing = 0.46f;

        [Tooltip("每一行战报显示多少秒后自动清除（避免旧行堆积、互相重叠）。")]
        public float logLineLifetime = 6f;

        // ---- 舞台常量（世界单位）----
        private const float StageZ = -1.5f;          // 比用户的 Card_example(z=0.5) 更靠前，避免被它挡住
        private const float HandCardWidth = 1.0f;
        private const float HandCardHeight = 1.4f;
        private const int HandSlots = 6;
        private const int LogSlotCount = 6;             // 战报同时最多显示几行
        private const float LogTopY = 5.72f;            // 第一行战报的高度（让开标题，避免压行）

        private static readonly Vector3 PlayZone = new Vector3(0f, 0.35f, 0f);
        private static readonly Vector3 DeckPosition = new Vector3(8.4f, -3.9f, 0f);
        private static readonly Vector3 HandOrigin = new Vector3(-2.875f, -3.9f, 0f);

        // ---- 演示步骤定义 ----
        private enum DemoAction { Play, Fuse }

        private struct DemoStep
        {
            public DemoAction action;
            public CardKind a;
            public CardKind b;

            public DemoStep(DemoAction action, CardKind a, CardKind b)
            {
                this.action = action;
                this.a = a;
                this.b = b;
            }
        }

        /// <summary>
        /// 固定的出牌脚本。配合固定种子，整局演示每次完全一致。
        /// 这是「意图」列表：优先打指定卡；若手牌没有该卡，则退化为打出手牌第一张，
        /// 因此每一步都一定有动作，不会出现空转的 1.5 秒。
        /// </summary>
        private static readonly DemoStep[] Script =
        {
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.B, CardKind.B),
            new DemoStep(DemoAction.Play, CardKind.C, CardKind.C),
            new DemoStep(DemoAction.Fuse, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.AA, CardKind.AA),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Fuse, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.AA, CardKind.AA),
            new DemoStep(DemoAction.Play, CardKind.C, CardKind.C),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Fuse, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.AA, CardKind.AA),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A),
            new DemoStep(DemoAction.Play, CardKind.A, CardKind.A)
        };

        // ---- 运行时状态 ----
        private GameObject _stage;
        private TextMesh _playerHpText;
        private TextMesh _enemyHpText;
        private TextMesh _statusText;
        private TextMesh _deckText;

        /// <summary>一行战报：每行占用独立的文字对象，行距固定，互相不遮挡；到期自动清除。</summary>
        private struct LogEntry
        {
            public string text;
            public float bornTime;
        }

        private readonly List<LogEntry> _logEntries = new List<LogEntry>();
        private readonly List<TextMesh> _logSlots = new List<TextMesh>();
        private bool _logDirty;

        private readonly Dictionary<CardInstance, GameObject> _visuals = new Dictionary<CardInstance, GameObject>();
        private readonly List<string> _logLines = new List<string>();
        private GameController _controller;
        private bool _battleEnded;
        private bool _victory;

        private void Start()
        {
            if (autoStartOnPlay)
            {
                StartDemo();
            }
        }

        [ContextMenu("开始演示对局")]
        public void StartDemo()
        {
            StopAllCoroutines();
            StartCoroutine(RunDemo());
        }

        // ------------------------------------------------------------------
        // 主流程
        // ------------------------------------------------------------------

        private IEnumerator RunDemo()
        {
            if (hideGalleryOnStart)
            {
                CardGalleryComponent gallery = FindGallery();
                if (gallery != null)
                {
                    gallery.HideGallery();
                }
            }

            _controller = EnsureController();
            if (_controller == null)
            {
                Debug.LogError("[CardGame][Demo] 无法创建 GameController，演示中止。");
                yield break;
            }

            // 固定种子：随机数全部来自 RandomComponent → SeededRandom
            _controller.SetupBattle(level, seed);

            BuildStage();
            RefreshHud();

            _battleEnded = false;
            _controller.OnLog += HandleLog;
            _controller.OnBattleEnded += HandleBattleEnded;

            _logLines.Clear();
            AddLog("演示开始：关卡 " + level + "，固定种子 " + seed + "，每个操作间隔 " + stepDelay.ToString("0.0") + " 秒");

            RefreshHandVisuals(instant: true);
            yield return new WaitForSeconds(stepDelay);

            for (int i = 0; i < Script.Length; i++)
            {
                if (_battleEnded)
                {
                    break;
                }
                yield return ExecuteStep(Script[i], i + 1);
            }

            if (!_battleEnded)
            {
                AddLog("脚本执行完毕，等待战斗收尾…");
            }

            _controller.OnLog -= HandleLog;
            _controller.OnBattleEnded -= HandleBattleEnded;
            RefreshHud();
            SetStatus(_battleEnded ? (_victory ? "演示结束：胜利" : "演示结束：失败") : "演示结束");
        }

        private IEnumerator ExecuteStep(DemoStep step, int index)
        {
            float used = 0f;

            if (step.action == DemoAction.Play)
            {
                CardInstance card = PickCard(step.a);
                if (card == null)
                {
                    // 退化：打出手牌第一张，保证每一步都有可见动作（固定种子下结果同样确定）
                    card = FirstCard();
                    if (card != null)
                    {
                        AddLog("（手牌无 " + step.a + "，改打 " + card.Kind + "）");
                    }
                }

                if (card != null)
                {
                    GameObject visual = GetVisual(card);
                    // 先推进真实战斗状态，再把它飞向出牌区 —— 动画与规则是一致的
                    _controller.PlayCard(card);
                    AddLog("第 " + index + " 步：打出 " + card.Kind + "（" + card.DisplayName + "）");

                    if (visual != null)
                    {
                        yield return MoveTo(visual.transform, PlayZone, moveDuration);
                        used = moveDuration;
                        CardDeckVisuals.DestroyObject(visual);
                        _visuals.Remove(card);
                    }
                }
                else
                {
                    AddLog("第 " + index + " 步：手牌已空，本步跳过");
                }
            }
            else
            {
                CardInstance first;
                CardInstance second;
                if (!FindPair(step.a, step.b, out first, out second))
                {
                    // 退化：找任意一对同种卡来演示合成
                    FindAnyPair(out first, out second);
                }

                if (first != null && second != null)
                {
                    CardKind productA = first.Kind;
                    CardKind productB = second.Kind;
                    GameObject visualA = GetVisual(first);
                    GameObject visualB = GetVisual(second);
                    _controller.TryFuseCards(first, second);
                    AddLog("第 " + index + " 步：合成 " + productA + "+" + productB + " 并打出");

                    if (visualA != null && visualB != null)
                    {
                        yield return MoveTwoTo(visualA.transform, visualB.transform, PlayZone, moveDuration);
                        used = moveDuration;
                    }
                    if (visualA != null)
                    {
                        CardDeckVisuals.DestroyObject(visualA);
                        _visuals.Remove(first);
                    }
                    if (visualB != null)
                    {
                        CardDeckVisuals.DestroyObject(visualB);
                        _visuals.Remove(second);
                    }
                }
                else
                {
                    // 连一对同种卡都没有：改为打出一张，避免这一步空转
                    CardInstance fallback = FirstCard();
                    if (fallback != null)
                    {
                        GameObject visual = GetVisual(fallback);
                        _controller.PlayCard(fallback);
                        AddLog("第 " + index + " 步：无可合成对，改打 " + fallback.Kind);
                        if (visual != null)
                        {
                            yield return MoveTo(visual.transform, PlayZone, moveDuration);
                            used = moveDuration;
                            CardDeckVisuals.DestroyObject(visual);
                            _visuals.Remove(fallback);
                        }
                    }
                    else
                    {
                        AddLog("第 " + index + " 步：手牌已空，本步跳过");
                    }
                }
            }

            RefreshHandVisuals(instant: false);
            RefreshHud();

            float remain = Mathf.Max(0f, stepDelay - used);
            if (remain > 0f)
            {
                yield return new WaitForSeconds(remain);
            }
        }

        // ------------------------------------------------------------------
        // 手牌选择（脚本 → 具体卡牌）
        // ------------------------------------------------------------------

        private CardInstance PickCard(CardKind preferred)
        {
            IReadOnlyList<CardInstance> hand = _controller.Hand;
            if (hand == null || hand.Count == 0)
            {
                return null;
            }
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i] != null && hand[i].Kind == preferred)
                {
                    return hand[i];
                }
            }
            return null;
        }

        private bool FindPair(CardKind a, CardKind b, out CardInstance first, out CardInstance second)
        {
            first = null;
            second = null;
            IReadOnlyList<CardInstance> hand = _controller.Hand;
            if (hand == null)
            {
                return false;
            }
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i] == null)
                {
                    continue;
                }
                if (first == null && hand[i].Kind == a)
                {
                    first = hand[i];
                    continue;
                }
                if (first != null && second == null && hand[i].Kind == b)
                {
                    second = hand[i];
                }
            }
            return first != null && second != null;
        }

        /// <summary>手牌第一张（脚本缺卡时的兜底，保证每步都有动作）。</summary>
        private CardInstance FirstCard()
        {
            IReadOnlyList<CardInstance> hand = _controller.Hand;
            if (hand == null || hand.Count == 0)
            {
                return null;
            }
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i] != null)
                {
                    return hand[i];
                }
            }
            return null;
        }

        /// <summary>找任意一对同种卡（用于演示合成：A+A / B+B / C+C 都能出合成卡）。</summary>
        private bool FindAnyPair(out CardInstance first, out CardInstance second)
        {
            first = null;
            second = null;
            IReadOnlyList<CardInstance> hand = _controller.Hand;
            if (hand == null)
            {
                return false;
            }
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i] == null)
                {
                    continue;
                }
                for (int j = i + 1; j < hand.Count; j++)
                {
                    if (hand[j] == null)
                    {
                        continue;
                    }
                    if (hand[i].Kind == hand[j].Kind)
                    {
                        first = hand[i];
                        second = hand[j];
                        return true;
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 舞台与 HUD
        // ------------------------------------------------------------------

        private void BuildStage()
        {
            if (_stage != null)
            {
                CardDeckVisuals.DestroyObject(_stage);
            }
            _visuals.Clear();

            _stage = new GameObject("BattleDemoStage");
            _stage.transform.SetParent(transform, false);
            _stage.transform.localPosition = new Vector3(0f, 0f, StageZ);

            Font font = CardDeckVisuals.ResolveFont();
            Color head = new Color(1f, 0.95f, 0.8f, 1f);
            Color body = new Color(0.9f, 0.94f, 1f, 1f);

            // 演示台上的所有文字（标题、标签、血量、战报）统一使用与手牌卡面相同的朝向，
            // 否则会出现「一半文字正读、一半镜像」——这个坑踩过一次，这里显式绑定。
            float textYaw = handCardTextYaw;

            CardDeckVisuals.CreateWorldText("Title", _stage.transform, new Vector3(0f, 6.6f, 0f),
                0.07f, 64, head, font, textYaw).text = "对局演示 · 固定出牌脚本 + 固定随机种子";

            // 战报：一行一个独立文字对象，按固定行距自上而下排开 ——
            // 不同行各有各的位置，不会互相遮挡；显示满 logLineLifetime 秒后自动清空。
            _logSlots.Clear();
            for (int i = 0; i < LogSlotCount; i++)
            {
                TextMesh slot = CardDeckVisuals.CreateWorldText("LogLine" + i, _stage.transform,
                    new Vector3(0f, LogTopY - i * logLineSpacing, 0f),
                    0.042f, 64, body, font, textYaw);
                slot.text = string.Empty;
                _logSlots.Add(slot);
            }

            _statusText = CardDeckVisuals.CreateWorldText("Status", _stage.transform, new Vector3(0f, -0.9f, 0f),
                0.05f, 64, head, font, textYaw);
            _statusText.text = "进行中…";

            // 玩家面板（左）
            CardDeckVisuals.CreateWorldText("PlayerLabel", _stage.transform, new Vector3(-7.6f, 2.9f, 0f),
                0.05f, 64, head, font, textYaw).text = "玩家";
            _playerHpText = CardDeckVisuals.CreateWorldText("PlayerHp", _stage.transform, new Vector3(-7.6f, 2.3f, 0f),
                0.045f, 64, body, font, textYaw);

            // 敌人面板（右）
            CardDeckVisuals.CreateWorldText("EnemyLabel", _stage.transform, new Vector3(6.4f, 2.9f, 0f),
                0.05f, 64, head, font, textYaw).text = "敌方";
            _enemyHpText = CardDeckVisuals.CreateWorldText("EnemyHp", _stage.transform, new Vector3(6.4f, 2.3f, 0f),
                0.045f, 64, body, font, textYaw);

            // 区域标签
            CardDeckVisuals.CreateWorldText("PlayZoneLabel", _stage.transform, new Vector3(0f, -0.15f, 0f),
                0.04f, 64, new Color(0.7f, 0.75f, 0.85f, 1f), font, textYaw).text = "— 出牌区 —";
            CardDeckVisuals.CreateWorldText("HandLabel", _stage.transform, new Vector3(0f, -2.6f, 0f),
                0.04f, 64, new Color(0.7f, 0.75f, 0.85f, 1f), font, textYaw).text = "— 手牌（上限 6 张）—";

            _deckText = CardDeckVisuals.CreateWorldText("DeckInfo", _stage.transform, new Vector3(8.4f, -2.6f, 0f),
                0.04f, 64, body, font, textYaw);

            if (frameCamera)
            {
                FrameCameraForStage();
            }
        }

        private void RefreshHud()
        {
            if (_controller == null || _controller.Context == null)
            {
                return;
            }

            Combatant player = _controller.Context.Player;
            if (player != null && _playerHpText != null)
            {
                _playerHpText.text = "HP " + player.CurrentHp + " / " + player.MaxHp + "\n防御 " + player.GetDefense();
            }

            if (_enemyHpText != null)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
                IReadOnlyList<Combatant> enemies = _controller.Context.Enemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    Combatant e = enemies[i];
                    if (e == null)
                    {
                        continue;
                    }
                    sb.Append(e.displayName).Append(e.IsAlive ? "  HP " : "  已阵亡  ")
                      .Append(e.IsAlive ? e.CurrentHp + " / " + e.MaxHp : "").Append('\n');
                }
                _enemyHpText.text = sb.ToString();
            }

            if (_deckText != null && player != null)
            {
                PlayerDeckComponent deck = player.GetComponent<PlayerDeckComponent>();
                if (deck != null)
                {
                    _deckText.text = "牌库 " + deck.DeckCount + "\n弃牌 " + deck.DiscardCount;
                }
            }
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
            {
                _statusText.text = text;
            }
        }

        // ------------------------------------------------------------------
        // 手牌视觉
        // ------------------------------------------------------------------

        private void RefreshHandVisuals(bool instant)
        {
            if (_controller == null)
            {
                return;
            }

            // 清掉已经不在手牌里的视觉
            List<CardInstance> gone = new List<CardInstance>();
            foreach (KeyValuePair<CardInstance, GameObject> pair in _visuals)
            {
                if (pair.Key == null || !IsInHand(pair.Key))
                {
                    gone.Add(pair.Key);
                }
            }
            for (int i = 0; i < gone.Count; i++)
            {
                GameObject visual = _visuals[gone[i]];
                if (visual != null)
                {
                    CardDeckVisuals.DestroyObject(visual);
                }
                _visuals.Remove(gone[i]);
            }

            // 为新抽到的牌创建视觉
            IReadOnlyList<CardInstance> hand = _controller.Hand;
            if (hand != null)
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    CardInstance card = hand[i];
                    if (card == null || _visuals.ContainsKey(card))
                    {
                        continue;
                    }
                    _visuals[card] = CreateCardVisual(card, DeckPosition);
                }
            }

            // 重排到槽位
            if (hand != null)
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    CardInstance card = hand[i];
                    if (card == null || !_visuals.ContainsKey(card))
                    {
                        continue;
                    }
                    GameObject visual = _visuals[card];
                    if (visual != null)
                    {
                        visual.transform.localPosition = HandSlotPosition(i);
                    }
                }
            }
        }

        private bool IsInHand(CardInstance card)
        {
            IReadOnlyList<CardInstance> hand = _controller.Hand;
            if (hand == null)
            {
                return false;
            }
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i] == card)
                {
                    return true;
                }
            }
            return false;
        }

        private static Vector3 HandSlotPosition(int index)
        {
            int clamped = Mathf.Clamp(index, 0, HandSlots - 1);
            return new Vector3(HandOrigin.x + clamped * (HandCardWidth + 0.15f), HandOrigin.y, 0f);
        }

        private GameObject GetVisual(CardInstance card)
        {
            GameObject visual;
            if (card != null && _visuals.TryGetValue(card, out visual))
            {
                return visual;
            }
            return null;
        }

        private GameObject CreateCardVisual(CardInstance card, Vector3 position)
        {
            GameObject go = new GameObject("HandCard_" + card.Kind);
            go.transform.SetParent(_stage.transform, false);
            go.transform.localPosition = position;

            Color tierColor = CardDisplay.TierColor(card.Tier);
            CardDeckVisuals.CreateBox("Body", go.transform, Vector3.zero,
                new Vector3(HandCardWidth, HandCardHeight, 0.05f),
                CardDeckVisuals.CreateSolidMaterial(tierColor));

            Font font = CardDeckVisuals.ResolveFont();
            string label = card.Kind.ToString();
            // 与卡牌一览一致：只保留一份文字，贴在朝向所指定的那一面，避免正反重影互相干扰。
            bool facesPositiveZ = Mathf.Abs(Mathf.DeltaAngle(handCardTextYaw, 0f)) < 90f;
            float textZ = (facesPositiveZ ? 1f : -1f) * 0.03f;

            CardDeckVisuals.CreateWorldText("Kind", go.transform, new Vector3(0f, 0.18f, textZ),
                0.055f, 64, Color.white, font, handCardTextYaw).text = label;
            CardDeckVisuals.CreateWorldText("Name", go.transform, new Vector3(0f, -0.28f, textZ),
                0.028f, 64, new Color(0.92f, 0.94f, 1f, 1f), font, handCardTextYaw).text = card.DisplayName;
            return go;
        }

        // ------------------------------------------------------------------
        // 动画
        // ------------------------------------------------------------------

        private IEnumerator MoveTo(Transform target, Vector3 destination, float duration)
        {
            if (target == null || duration <= 0f)
            {
                if (target != null)
                {
                    target.localPosition = destination;
                }
                yield break;
            }
            Vector3 start = target.localPosition;
            float t = 0f;
            while (t < duration && target != null)
            {
                t += Time.deltaTime;
                target.localPosition = Vector3.Lerp(start, destination, Mathf.Clamp01(t / duration));
                yield return null;
            }
            if (target != null)
            {
                target.localPosition = destination;
            }
        }

        private IEnumerator MoveTwoTo(Transform a, Transform b, Vector3 destination, float duration)
        {
            if (duration <= 0f)
            {
                if (a != null) a.localPosition = destination;
                if (b != null) b.localPosition = destination;
                yield break;
            }
            Vector3 startA = a != null ? a.localPosition : destination;
            Vector3 startB = b != null ? b.localPosition : destination;
            // 两张卡稍微错开，避免完全重叠看不出是两张
            Vector3 destA = destination + new Vector3(-0.35f, 0f, 0f);
            Vector3 destB = destination + new Vector3(0.35f, 0f, 0f);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                if (a != null) a.localPosition = Vector3.Lerp(startA, destA, k);
                if (b != null) b.localPosition = Vector3.Lerp(startB, destB, k);
                yield return null;
            }
            if (a != null) a.localPosition = destA;
            if (b != null) b.localPosition = destB;
        }

        // ------------------------------------------------------------------
        // 日志与战斗结束
        // ------------------------------------------------------------------

        private void HandleLog(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                AddLog(message);
            }
        }

        private void AddLog(string message)
        {
            LogEntry entry;
            entry.text = message;
            entry.bornTime = Time.time;
            _logEntries.Insert(0, entry);                   // 新行排在最上
            while (_logEntries.Count > LogSlotCount)
            {
                _logEntries.RemoveAt(_logEntries.Count - 1);
            }
            _logDirty = true;

            // 演示自身「第 N 步」的动作同时打到控制台：控制台带时间戳，
            // 可以直接量出相邻两步的间隔是否正好是 stepDelay（规则动作本身已由战斗日志覆盖）。
            if (message.StartsWith("第 "))
            {
                Debug.Log("[CardGame][Demo] " + message);
            }
        }

        private void Update()
        {
            if (_controller == null)
            {
                return;
            }

            // 逐行到期清除：显示满 logLineLifetime 秒的行马上删掉（至少保留最新一行），
            // 这样旧行不会一直堆在画面上造成重叠。
            if (_logEntries.Count > 1)
            {
                float now = Time.time;
                for (int i = _logEntries.Count - 1; i >= 1; i--)
                {
                    if (now - _logEntries[i].bornTime > logLineLifetime)
                    {
                        _logEntries.RemoveAt(i);
                        _logDirty = true;
                    }
                }
            }

            if (_logDirty)
            {
                RenderLog();
                _logDirty = false;
            }
        }

        private void RenderLog()
        {
            for (int i = 0; i < _logSlots.Count; i++)
            {
                TextMesh slot = _logSlots[i];
                if (slot != null)
                {
                    slot.text = i < _logEntries.Count ? _logEntries[i].text : string.Empty;
                }
            }
        }

        private void HandleBattleEnded(bool victory)
        {
            _battleEnded = true;
            _victory = victory;
            AddLog(victory ? "★ 战斗结束：胜利" : "★ 战斗结束：失败");
        }

        // ------------------------------------------------------------------
        // 装配与取景
        // ------------------------------------------------------------------

        private GameController EnsureController()
        {
            if (GameController.Instance != null)
            {
                return GameController.Instance;
            }

            // 场景里没有就按 GameBootstrap 的顺序补齐（模板 → 卡牌库 → 怪物模板 → 控制器）
            SceneTemplateBuilder.BuildPlayerTemplate(null);
            SceneTemplateBuilder.BuildCardLibrary(null);
            SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Attacker);
            SceneTemplateBuilder.BuildMonsterTemplate(null, MonsterKind.Support);
            GameObject controllerObject = SceneTemplateBuilder.BuildController(null);
            if (controllerObject != null)
            {
                GameController created = controllerObject.GetComponent<GameController>();
                if (created != null)
                {
                    return created;
                }
            }
            return GameController.Instance;
        }

        private static CardGalleryComponent FindGallery()
        {
            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }
                CardGalleryComponent found = roots[i].GetComponent<CardGalleryComponent>();
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>按演示台的实际尺寸取景，保证「标题 / 战报 / 手牌 / 双方血量」全部入镜。</summary>
        private void FrameCameraForStage()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            // 演示台在 z=-1.5，内容大致覆盖 x∈[-9,10]、y∈[-4.8,7.0]
            float halfWidth = 9.6f;
            float halfHeight = 6.1f;
            float centerX = 0.4f;
            float centerY = 1.1f;

            float tanHalfFov = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float aspect = cam.aspect > 0.01f ? cam.aspect : (16f / 9f);
            float distance = Mathf.Max(halfHeight / tanHalfFov, halfWidth / (tanHalfFov * aspect)) * 1.05f;

            // 【按指定】与卡牌展示一致：相机绕竖直轴(Y)旋转 180°，从演示台的 +Z 一侧回看。
            cam.transform.position = new Vector3(centerX, centerY, StageZ + distance);
            cam.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Debug.Log("[CardGame][Demo] 对局演示取景（绕 Y 轴 180°）：相机距离 " + distance.ToString("0.00"));
        }
    }
}
