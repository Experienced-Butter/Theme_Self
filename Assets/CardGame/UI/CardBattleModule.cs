using System.Collections;
using System.Collections.Generic;
using CardGame.Bootstrap;
using CardGame.Cards;
using CardGame.Combat;
using CardGame.Controllers;
using CardGame.Core;
using CardGame.Monsters;
using CardGame.Players;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGame.UI
{
    /// <summary>
    /// 「卡牌战斗」模块 —— 可调用的入口。
    ///
    /// 调用的方式：
    ///   1. 在场景里放一个挂本组件的对象，进入 Play 即自动开战（autoEnterOnPlay）；
    ///   2. 编辑器菜单 `CardGame/进入卡牌战斗`（创建对象并直接进入 Play）；
    ///   3. 代码调用 <see cref="EnterBattle"/> 或静态的 <see cref="Launch"/>。
    ///
    /// 画面构成（图层自下而上，见 BattleUiKit 的说明）：
    ///   背景（固定最下层） → 玩家/敌人 → 卡牌（下侧） → 35% 黑色遮罩 → 拖动层 → 结算横幅
    ///
    /// 流程：背景自左侧滑入 → 玩家自左、敌人自右滑入 → 卡牌自下侧滑出 → 开始战斗 →
    ///       一方落败后弹出「xxx 输了」横幅，横幅下方两个按钮：再来一局 / 继续（玩家输了则禁用继续）。
    ///
    /// 卡牌交互（由 CardWidget 转发到本类）：
    ///   按住左键抓起卡牌 → 除该卡外全部处于遮罩之下 → 松开时按「投影重合」判定掷出或融合；
    ///   若松开时将与某目标交互，则该目标会被临时提到遮罩之上作为提示。
    /// </summary>
    public class CardBattleModule : MonoBehaviour
    {
        [Header("入口")]
        public LevelId level = LevelId.Level1;
        public int seed = 20260929;

        [Tooltip("进入 Play 时自动开战。")]
        public bool autoEnterOnPlay = true;

        [Header("入场时序（秒）")]
        public float backgroundSlideDuration = 0.8f;
        public float entitySlideDuration = 0.6f;
        public float cardSlideDuration = 0.6f;
        public float sequenceGap = 0.15f;

        [Header("遮罩")]
        [Range(0f, 1f)] public float maskAlpha = 0.35f;
        public bool maskVisible = true;

        // ---- 图层 ----
        private Canvas _canvas;
        private RectTransform _layerBackground;
        private RectTransform _layerEntities;
        private RectTransform _layerCards;
        private RectTransform _layerMask;
        private RectTransform _layerAbove;
        private RectTransform _layerOverlay;

        private Image _backgroundImage;
        private Image _maskImage;
        private BattleEntityView _playerView;
        private RectTransform _playerRect;
        private Text _statusText;

        private readonly List<RectTransform> _enemyRects = new List<RectTransform>();
        private readonly List<Combatant> _enemyTargets = new List<Combatant>();
        private readonly List<BattleEntityView> _enemyViews = new List<BattleEntityView>();
        private readonly List<int> _lastEnemyHp = new List<int>();
        private readonly List<CardWidget> _cardWidgets = new List<CardWidget>();

        // 表现层订阅的退订动作（怪物普攻 + 双方的生命值事件）。每次开战/重建 UI 时统一退订。
        private readonly List<System.Action> _effectUnsubscribers = new List<System.Action>();

        // 血条「显示值」缓动状态（每个实体一份）
        private sealed class HpDisplay
        {
            public float shown = float.NaN;
            public float holdUntil;
        }

        private HpDisplay _playerHpDisplay = new HpDisplay();
        private readonly List<HpDisplay> _enemyHpDisplay = new List<HpDisplay>();

        // 表现参数
        private const float HpRiseSeconds = 0.35f;      // 血条涨满整条所需秒数
        private const float ParticleTravel = 0.55f;     // 单颗粒子飞行时间

        private GameController _controller;
        private RectTransform _previewTarget;          // 当前被提到遮罩之上的提示目标
        private CardBattleMaskToggle _maskToggle;
        private GameObject _bannerRoot;
        private Text _bannerText;
        private Button _againButton;
        private Button _continueButton;
        private bool _battleEnded;
        private bool _playerWon;
        private bool _running;
        private bool _interacting;                     // 是否有卡牌正被抓起（决定遮罩显不显示）
        private int _lastPlayerHp = int.MinValue;

        /// <summary>
        /// 现在是否还接受卡牌交互。
        ///
        /// 战斗结束（横幅已弹出）后一律为 false：此时拖牌不会产生任何结算，
        /// 若还允许「抓起 + 抬起预览目标」，画面看起来像是还能出牌，属于误导。
        /// CardWidget 在 pointerDown 之前查这里，结束后连抓都抓不起来。
        /// </summary>
        public bool CanInteractWithCards
        {
            get { return _running && !_battleEnded; }
        }

        private const float CardSlotSpacing = 168f;
        private const float CardRowY = 150f;
        private const float PlayerX = -620f;
        private const float EnemyX = 620f;

        private void Start()
        {
            if (autoEnterOnPlay)
            {
                EnterBattle();
            }
        }

        /// <summary>静态入口：在场景里找到或创建模块并开战。</summary>
        public static CardBattleModule Launch()
        {
            CardBattleModule module = FindModule();
            if (module == null)
            {
                GameObject go = new GameObject("CardBattle");
                module = go.AddComponent<CardBattleModule>();
            }
            module.EnterBattle();
            return module;
        }

        private static CardBattleModule FindModule()
        {
            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null)
                {
                    CardBattleModule found = roots[i].GetComponent<CardBattleModule>();
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return null;
        }

        [ContextMenu("进入卡牌战斗")]
        public void EnterBattle()
        {
            StopAllCoroutines();
            _battleEnded = false;
            _running = true;

            BuildUi();
            StartCoroutine(EnterSequence());
        }

        // ------------------------------------------------------------------
        // 搭建 UI
        // ------------------------------------------------------------------

        /// <summary>
        /// 清掉上一局建出来的整套 UI，并把所有缓存引用归零。
        ///
        /// 「再来一局 / 继续」走的是 <see cref="RestartBattle"/> → <see cref="EnterBattle"/> → 本类 BuildUi，
        /// 而 BuildUi 是「往 Canvas 下再追加一套图层」。不先清一遍的话，每开一局就会多叠
        /// 6 个图层 + 玩家/敌人视图（旧的那套只是被新的盖住，对象一直都在）。
        /// </summary>
        private void ClearBattleUi()
        {
            _previewTarget = null;
            _layerBackground = null;
            _layerEntities = null;
            _layerCards = null;
            _layerMask = null;
            _layerAbove = null;
            _layerOverlay = null;
            _backgroundImage = null;
            _maskImage = null;
            _playerView = null;
            _playerRect = null;
            _statusText = null;
            _maskToggle = null;
            _bannerRoot = null;
            _bannerText = null;
            _againButton = null;
            _continueButton = null;
            _lastPlayerHp = int.MinValue;
            _interacting = false;
            _playerHpDisplay = new HpDisplay();

            UnsubscribeBattleEffects();

            _enemyRects.Clear();
            _enemyViews.Clear();
            _enemyTargets.Clear();
            _lastEnemyHp.Clear();
            _cardWidgets.Clear();

            if (_canvas == null)
            {
                return;
            }

            Transform root = _canvas.transform;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                SafeDestroy(root.GetChild(i).gameObject);
            }
        }

        private void BuildUi()
        {
            BattleUiKit.EnsureEventSystem();

            _canvas = GetComponentInChildren<Canvas>();
            if (_canvas != null)
            {
                ClearBattleUi();
            }
            if (_canvas == null)
            {
                GameObject canvasGo = new GameObject("BattleCanvas", typeof(RectTransform));
                canvasGo.transform.SetParent(transform, false);
                _canvas = canvasGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 100;

                CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);   // 目标 1080p
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                canvasGo.AddComponent<GraphicRaycaster>();
            }

            Transform root = _canvas.transform;

            // ① 背景（固定最下层）
            _layerBackground = BattleUiKit.CreateLayer(root, "Layer_Background");
            _backgroundImage = _layerBackground.gameObject.AddComponent<Image>();
            _backgroundImage.sprite = BattleUiKit.WhiteSprite;
            _backgroundImage.color = new Color(0.09f, 0.11f, 0.17f, 1f);
            _backgroundImage.raycastTarget = false;
            BuildBackgroundDecor(_layerBackground);

            // ② 玩家（左）与敌人（右）
            _layerEntities = BattleUiKit.CreateLayer(root, "Layer_Entities");
            BuildPlayerView(_layerEntities);
            BuildEnemyViews(_layerEntities);

            // ③ 卡牌（下侧）
            _layerCards = BattleUiKit.CreateLayer(root, "Layer_Cards");

            // ④ 黑色遮罩（默认 35% 不透明度）
            //
            // 遮罩**不是**一层常驻在画面上的黑幕，而是「盖住所有不参与互动的物体」：
            //    · 平时（没有卡牌被抓起）不显示 —— 否则整局画面都被压暗，与需求不符；
            //    · 左键抓起卡牌时显示：被抓起的卡与「将要交互的目标」都被提到 Layer_Above
            //      （遮罩之上），其余一切都留在遮罩之下 ⇒ 恰好就是「不参与互动的物体被盖住」。
            _layerMask = BattleUiKit.CreateLayer(root, "Layer_Mask");
            _maskImage = _layerMask.gameObject.AddComponent<Image>();
            _maskImage.sprite = BattleUiKit.WhiteSprite;
            _maskImage.color = new Color(0f, 0f, 0f, maskAlpha);
            _maskImage.raycastTarget = false;      // 遮罩不吃事件，否则会挡住卡牌拖动
            _layerMask.gameObject.SetActive(false);

            // ⑤ 拖动层：抓起卡牌与「将要交互的目标」临时提到这里（位于遮罩之上）
            _layerAbove = BattleUiKit.CreateLayer(root, "Layer_Above");

            // ⑥ 结算横幅层
            _layerOverlay = BattleUiKit.CreateLayer(root, "Layer_Overlay");
            BuildOverlay(_layerOverlay);
        }

        private void BuildBackgroundDecor(RectTransform parent)
        {
            // 纯程序生成的装饰（不依赖美术资源）：上下两条色带 + 标题
            BattleUiKit.CreateImage("TopBand", parent, new Color(1f, 1f, 1f, 0.04f),
                new Vector2(1920f, 8f), new Vector2(0f, 420f)).raycastTarget = false;
            BattleUiKit.CreateImage("FloorBand", parent, new Color(0.15f, 0.18f, 0.26f, 1f),
                new Vector2(1920f, 300f), new Vector2(0f, -390f)).raycastTarget = false;

            Text title = BattleUiKit.CreateText("Title", parent, "卡牌战斗", 56,
                new Color(1f, 0.96f, 0.85f, 0.9f), TextAnchor.UpperCenter);
            title.rectTransform.anchoredPosition = new Vector2(0f, -60f);
        }

        private void BuildPlayerView(RectTransform parent)
        {
            // 玩家实体形象：圆形头像 + 名称 + 血条，内部布局由 BattleEntityView 负责
            _playerView = BattleEntityView.Create(parent, "玩家", new Color(0.22f, 0.52f, 0.95f, 1f),
                new Vector2(260f, 330f), new Vector2(PlayerX, -50f));
            _playerRect = _playerView.Rect;
        }

        private void BuildEnemyViews(RectTransform parent)
        {
            _enemyRects.Clear();
            _enemyViews.Clear();
            _lastEnemyHp.Clear();
            _enemyHpDisplay.Clear();

            // 先按关卡放置「最多 3 个」占位敌人，开战后按实际敌人数量启用/停用并刷新数值
            int count = 3;
            for (int i = 0; i < count; i++)
            {
                float y = count == 1 ? -50f : (150f - i * 210f);
                BattleEntityView view = BattleEntityView.Create(parent, "敌人" + (i + 1),
                    new Color(0.85f, 0.32f, 0.28f, 1f), new Vector2(220f, 300f), new Vector2(EnemyX, y));

                _enemyRects.Add(view.Rect);
                _enemyViews.Add(view);
                _lastEnemyHp.Add(int.MinValue);
                _enemyHpDisplay.Add(new HpDisplay());
            }
        }

        private void BuildOverlay(RectTransform parent)
        {
            // 遮罩开关：常驻右上角，方便随时切换遮罩显隐
            Button toggle = BattleUiKit.CreateButton("MaskToggle", parent, "遮罩：开", 22,
                new Vector2(160f, 52f), new Vector2(0f, 0f), new Color(0.2f, 0.24f, 0.34f, 1f));
            RectTransform toggleRect = (RectTransform)toggle.transform;
            toggleRect.anchorMin = new Vector2(1f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(1f, 1f);
            toggleRect.anchoredPosition = new Vector2(-24f, -24f);
            _maskToggle = toggle.gameObject.AddComponent<CardBattleMaskToggle>();
            _maskToggle.Bind(this, toggle.GetComponentInChildren<Text>());

            // 状态行（回合 / 充能等提示）
            _statusText = BattleUiKit.CreateText("Status", parent, string.Empty, 24,
                new Color(0.85f, 0.9f, 1f, 0.95f), TextAnchor.UpperCenter);
            RectTransform statusRect = _statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0.5f, 1f);
            statusRect.anchorMax = new Vector2(0.5f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.sizeDelta = new Vector2(900f, 60f);
            statusRect.anchoredPosition = new Vector2(0f, -24f);

            // 结算横幅（默认隐藏）
            _bannerRoot = new GameObject("ResultBanner", typeof(RectTransform));
            RectTransform bannerRect = _bannerRoot.GetComponent<RectTransform>();
            bannerRect.SetParent(parent, false);
            bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
            bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.sizeDelta = new Vector2(1000f, 320f);
            bannerRect.anchoredPosition = Vector2.zero;

            Image bannerBg = BattleUiKit.CreateImage("BannerBg", _bannerRoot.transform,
                new Color(0.06f, 0.07f, 0.11f, 0.96f), new Vector2(1000f, 320f), Vector2.zero);
            bannerBg.raycastTarget = true;

            _bannerText = BattleUiKit.CreateText("BannerText", _bannerRoot.transform, string.Empty, 64,
                new Color(1f, 0.95f, 0.85f, 1f), TextAnchor.MiddleCenter);
            _bannerText.rectTransform.anchoredPosition = new Vector2(0f, 60f);

            _againButton = BattleUiKit.CreateButton("AgainButton", _bannerRoot.transform, "再来一局", 30,
                new Vector2(280f, 84f), new Vector2(-160f, -80f), new Color(0.18f, 0.45f, 0.85f, 1f));
            _againButton.onClick.AddListener(RestartBattle);

            _continueButton = BattleUiKit.CreateButton("ContinueButton", _bannerRoot.transform, "继续", 30,
                new Vector2(280f, 84f), new Vector2(160f, -80f), new Color(0.2f, 0.55f, 0.35f, 1f));
            _continueButton.onClick.AddListener(ContinueBattle);

            _bannerRoot.SetActive(false);
        }

        // ------------------------------------------------------------------
        // 入场时序
        // ------------------------------------------------------------------

        private IEnumerator EnterSequence()
        {
            _battleEnded = false;

            // 0) 先把战斗装配跑完，拿到本关真实的敌人数量与手牌。
            //
            // 为什么必须放在滑入之前：BuildEnemyViews 固定建 3 个占位敌人，真正该显示几只
            // 要等 SetupBattle 之后才知道（StartBattleInternal 会 SetActive 掉多余的）。
            // 如果照旧在第 3 步才开战，关一（2 只怪）会先让 3 只一起滑进来，然后第 3 只
            // 凭空消失 —— 玩家一眼就能看到这个瑕疵。
            StartBattleInternal();

            // 1) 背景自画面左侧滑入
            RectTransform bg = _layerBackground;
            yield return SlideAnchored(bg, new Vector2(-1920f, 0f), Vector2.zero, backgroundSlideDuration);

            // 2) 玩家自左、敌人自右滑入（只滑真正出场的那些）
            if (_playerRect != null)
            {
                StartCoroutine(SlideAnchored(_playerRect, new Vector2(PlayerX - 900f, -40f),
                    new Vector2(PlayerX, -40f), entitySlideDuration));
            }
            for (int i = 0; i < _enemyRects.Count; i++)
            {
                RectTransform enemy = _enemyRects[i];
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    continue;   // 本关没用到的占位敌人：不参与滑入
                }
                Vector2 target = enemy.anchoredPosition;
                StartCoroutine(SlideAnchored(enemy, new Vector2(EnemyX + 900f, target.y), target, entitySlideDuration));
            }
            yield return new WaitForSeconds(entitySlideDuration);

            // 3) 卡牌自下侧滑出
            yield return new WaitForSeconds(sequenceGap);
            RefreshHand();
            for (int i = 0; i < _cardWidgets.Count; i++)
            {
                RectTransform rect = (RectTransform)_cardWidgets[i].transform;
                Vector2 target = rect.anchoredPosition;
                StartCoroutine(SlideAnchored(rect, new Vector2(target.x, -420f), target, cardSlideDuration));
            }
            yield return new WaitForSeconds(cardSlideDuration);

            _running = true;
        }

        /// <summary>把 RectTransform 的 anchoredPosition 从 from 平滑移到 to。</summary>
        private IEnumerator SlideAnchored(RectTransform rect, Vector2 from, Vector2 to, float duration)
        {
            if (rect == null)
            {
                yield break;
            }
            rect.anchoredPosition = from;
            if (duration <= 0f)
            {
                rect.anchoredPosition = to;
                yield break;
            }
            float t = 0f;
            while (t < duration && rect != null)
            {
                t += Time.deltaTime;
                rect.anchoredPosition = Vector2.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            if (rect != null)
            {
                rect.anchoredPosition = to;
            }
        }

        // ------------------------------------------------------------------
        // 战斗装配
        // ------------------------------------------------------------------

        private void StartBattleInternal()
        {
            _controller = EnsureController();
            if (_controller == null)
            {
                Debug.LogError("[CardGame][UI] 无法创建 GameController，卡牌战斗无法开始。");
                return;
            }

            _controller.SetupBattle(level, seed);
            _battleEnded = false;
            _controller.OnBattleEnded -= HandleBattleEnded;
            _controller.OnBattleEnded += HandleBattleEnded;

            _enemyTargets.Clear();
            IReadOnlyList<Combatant> enemies = _controller.Context != null ? _controller.Context.Enemies : null;
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    _enemyTargets.Add(enemies[i]);
                }
            }

            // 按本关实际敌人数量启用/停用占位敌人，并把真实名字写上去（不再显示「敌人1/2」占位名）
            for (int i = 0; i < _enemyRects.Count; i++)
            {
                bool used = i < _enemyTargets.Count;
                _enemyRects[i].gameObject.SetActive(used);
                if (used && i < _enemyViews.Count && _enemyViews[i] != null && _enemyTargets[i] != null)
                {
                    _enemyViews[i].SetName(_enemyTargets[i].displayName);
                }
            }

            SubscribeBattleEffects();

            if (_bannerRoot != null)
            {
                _bannerRoot.SetActive(false);
            }
            RefreshHud();
        }

        // ------------------------------------------------------------------
        // 敌人攻击的「攻击动作」表现
        // ------------------------------------------------------------------

        /// <summary>
        /// 订阅本场的表现事件：敌人普攻（红粒子 → 玩家）、玩家出牌命中（橙粒子 → 敌人）、
        /// 以及双方回血（淡蓝荧光粒子 → 各自血条末端）。
        /// 每次开战（含「再来一局」）都重新订阅，先统一退订上一波 —— 上一波的怪物可能已销毁。
        /// </summary>
        private void SubscribeBattleEffects()
        {
            UnsubscribeBattleEffects();

            // 玩家回血：粒子从玩家身上飞向玩家血条末端
            Combatant player = _controller != null && _controller.Context != null ? _controller.Context.Player : null;
            if (player != null)
            {
                SubscribeHeal(player, _playerView, _playerHpDisplay);
            }

            for (int i = 0; i < _enemyTargets.Count; i++)
            {
                Combatant enemy = _enemyTargets[i];
                if (enemy == null)
                {
                    continue;
                }

                int index = i;        // 闭包捕获：记住是「第几只」
                BattleEntityView view = index < _enemyViews.Count ? _enemyViews[index] : null;
                HpDisplay display = index < _enemyHpDisplay.Count ? _enemyHpDisplay[index] : null;

                // 敌人普攻玩家 → 红色粒子从它身上飞向玩家
                MonsterAttackComponent attack = enemy.GetComponent<MonsterAttackComponent>();
                if (attack != null)
                {
                    MonsterAttackComponent captured = attack;
                    System.Action<Combatant, int> handler = delegate(Combatant target, int damage)
                    {
                        HandleEnemyAttacked(index, damage);
                    };
                    captured.OnAttacked += handler;
                    _effectUnsubscribers.Add(delegate
                    {
                        if (captured != null)
                        {
                            captured.OnAttacked -= handler;
                        }
                    });
                }

                // 玩家出牌打到它 → 橙色粒子从玩家飞向它
                SubscribeCardDamage(enemy, index, view);

                // 它自己回血 → 淡蓝粒子飞向它的血条末端
                SubscribeHeal(enemy, view, display);
            }
        }

        private void UnsubscribeBattleEffects()
        {
            for (int i = 0; i < _effectUnsubscribers.Count; i++)
            {
                _effectUnsubscribers[i]();
            }
            _effectUnsubscribers.Clear();
        }

        /// <summary>敌人受到「卡牌」伤害 ⇒ 橙色粒子从玩家角色飞向它（数量与光强随伤害增加）。</summary>
        private void SubscribeCardDamage(Combatant enemy, int index, BattleEntityView view)
        {
            HealthComponent health = enemy.GetComponent<HealthComponent>();
            if (health == null || view == null)
            {
                return;
            }

            HealthComponent captured = health;
            BattleEntityView capturedView = view;
            System.Action<int, DamageSource> handler = delegate(int amount, DamageSource source)
            {
                if (source != DamageSource.Card)
                {
                    return;   // 只表现「被玩家打」；状态类伤害不飞粒子
                }
                HandlePlayerHitEnemy(capturedView, amount);
            };

            captured.OnDamaged += handler;
            _effectUnsubscribers.Add(delegate
            {
                if (captured != null)
                {
                    captured.OnDamaged -= handler;
                }
            });
        }

        /// <summary>回血 ⇒ 淡蓝荧光粒子从该角色飞向它自己的血条末端，命中后血条才开始上涨。</summary>
        private void SubscribeHeal(Combatant owner, BattleEntityView view, HpDisplay display)
        {
            if (owner == null || view == null)
            {
                return;
            }

            HealthComponent health = owner.GetComponent<HealthComponent>();
            if (health == null)
            {
                return;
            }

            HealthComponent captured = health;
            BattleEntityView capturedView = view;
            HpDisplay capturedDisplay = display;
            System.Action<int> handler = delegate(int amount)
            {
                HandleHealed(capturedView, capturedDisplay, amount);
            };

            captured.OnHealed += handler;
            _effectUnsubscribers.Add(delegate
            {
                if (captured != null)
                {
                    captured.OnHealed -= handler;
                }
            });
        }

        /// <summary>某只怪物普攻了玩家 → 让红色粒子从它身上飞向玩家角色。</summary>
        private void HandleEnemyAttacked(int enemyIndex, int damage)
        {
            if (enemyIndex < 0 || enemyIndex >= _enemyViews.Count)
            {
                return;
            }

            BattleEntityView attacker = _enemyViews[enemyIndex];
            if (attacker == null || _playerRect == null || _layerAbove == null)
            {
                return;
            }

            StartCoroutine(ParticleStream(attacker.Rect, _playerRect.position,
                new Color(0.95f, 0.22f, 0.18f, 0.95f), 12, damage, 0));
        }

        /// <summary>玩家出牌命中了这只敌人 → 橙色粒子从玩家角色飞向它。</summary>
        private void HandlePlayerHitEnemy(BattleEntityView target, int damage)
        {
            if (target == null || _playerRect == null || _layerAbove == null)
            {
                return;
            }

            StartCoroutine(ParticleStream(_playerRect, target.Rect.position,
                new Color(1f, 0.58f, 0.12f, 0.98f), 16, damage, 0));
        }

        /// <summary>
        /// 回血：淡蓝荧光粒子从角色身上飞向它血条填充的末端；命中时在末端炸出一团荧光，
        /// 并解除「延时上涨」让血条开始涨。
        /// </summary>
        private void HandleHealed(BattleEntityView view, HpDisplay display, int amount)
        {
            if (view == null || _layerAbove == null || !view.HasHpBar)
            {
                return;
            }

            Vector3 barEnd = view.HpBarFillEnd;

            // 先按住显示值：等粒子飞到血条末端，血条再开始涨
            if (display != null)
            {
                display.holdUntil = Time.time + ParticleTravel;
            }

            StartCoroutine(ParticleStream(view.Rect, barEnd,
                new Color(0.62f, 0.88f, 1f, 0.95f), 12, amount, 0));
            StartCoroutine(BarEndGlow(barEnd));
        }

        /// <summary>血条末端的荧光爆点：一小团淡蓝光斑快速放大并淡出。</summary>
        private IEnumerator BarEndGlow(Vector3 position)
        {
            const float duration = 0.32f;
            const float maxSize = 74f;

            Image glow = BattleUiKit.CreateImage("HealGlow", _layerAbove,
                new Color(0.66f, 0.90f, 1f, 0.85f), new Vector2(18f, 18f), Vector2.zero);
            if (glow == null)
            {
                yield break;
            }
            glow.sprite = BattleUiKit.CircleSprite;
            glow.raycastTarget = false;
            RectTransform rect = (RectTransform)glow.transform;
            rect.position = position;

            float t = 0f;
            while (t < duration && rect != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);

                float size = Mathf.Lerp(18f, maxSize, k);
                rect.sizeDelta = new Vector2(size, size);

                Color c = glow.color;
                c.a = Mathf.Lerp(0.85f, 0f, k);
                glow.color = c;

                yield return null;
            }

            if (rect != null)
            {
                SafeDestroy(rect.gameObject);
            }
        }

        /// <summary>
        /// 一簇粒子从 <paramref name="fromRect"/> 飞向世界坐标 <paramref name="to"/>。
        /// 图形全部由程序生成的圆形 Sprite 充当（不需要任何美术资源）。
        /// 粒子挂在 Layer_Above 上 —— 它在遮罩之上，因此无论遮罩开没开都看得见。
        ///
        /// **数量与光强随 <paramref name="damage"/> 增长**（用户口径：伤害越高，粒子越多、越亮）：
        ///   · 数量  baseCount+4 → baseCount+22，按 0…900 伤害线性增长；
        ///   · 光强  用「亮核」实现 —— 每颗粒子额外叠一层更大更淡的同色光晕，
        ///           光晕的尺寸与不透明度同样随伤害增长，看上去就是一点点眩光。
        /// </summary>
        private IEnumerator ParticleStream(RectTransform fromRect, Vector3 to, Color color,
            int baseCount, int damage, float extraSpread)
        {
            if (fromRect == null)
            {
                yield break;
            }

            float weight = Mathf.Clamp01(damage / 900f);
            int count = Mathf.RoundToInt(Mathf.Lerp(baseCount, baseCount + 12f, weight));
            float travel = ParticleTravel;
            float stagger = 0.35f;
            float size = Mathf.Lerp(14f, 22f, weight);
            float haloScale = Mathf.Lerp(1.9f, 2.9f, weight);      // 光晕相对粒子的尺寸
            float haloAlpha = Mathf.Lerp(0.18f, 0.42f, weight);    // 光晕不透明度 = 光强
            float spread = 46f + extraSpread;

            Vector3 from = fromRect.position;

            List<RectTransform> dots = new List<RectTransform>(count);
            List<RectTransform> halos = new List<RectTransform>(count);
            List<Vector3> starts = new List<Vector3>(count);
            List<Vector3> controls = new List<Vector3>(count);
            List<float> delays = new List<float>(count);

            for (int i = 0; i < count; i++)
            {
                // 起点在攻击者身上散开，避免所有粒子重合在圆心
                Vector2 jitter = Random.insideUnitCircle * spread;
                Vector3 start = from + new Vector3(jitter.x, jitter.y, 0f);

                // 轨迹走二次贝塞尔：中点沿垂直方向随机偏移，让它是一道弧而不是直线
                Vector3 mid = (start + to) * 0.5f;
                Vector3 dir = (to - start).sqrMagnitude > 0.0001f
                    ? (to - start).normalized
                    : Vector3.right;
                Vector3 perp = new Vector3(-dir.y, dir.x, 0f);
                Vector3 control = mid + perp * Random.Range(-90f, 90f);

                // 眩光：先放光晕，再放亮核，亮核自然压在光晕中心
                Image halo = BattleUiKit.CreateImage("ParticleHalo", _layerAbove,
                    new Color(color.r, color.g, color.b, haloAlpha),
                    new Vector2(size * haloScale, size * haloScale), Vector2.zero);
                if (halo != null)
                {
                    halo.sprite = BattleUiKit.CircleSprite;
                    halo.raycastTarget = false;
                    ((RectTransform)halo.transform).position = start;
                }

                Image dot = BattleUiKit.CreateImage("ParticleCore", _layerAbove, color,
                    new Vector2(size, size), Vector2.zero);
                if (dot == null)
                {
                    if (halo != null)
                    {
                        SafeDestroy(halo.gameObject);
                    }
                    continue;
                }
                dot.sprite = BattleUiKit.CircleSprite;
                dot.raycastTarget = false;          // 表现层绝不能吃掉鼠标事件
                RectTransform rect = (RectTransform)dot.transform;
                rect.position = start;

                dots.Add(rect);
                halos.Add(halo != null ? (RectTransform)halo.transform : null);
                starts.Add(start);
                controls.Add(control);
                delays.Add(Random.Range(0f, stagger));
            }

            float total = stagger + travel;
            float elapsed = 0f;
            while (elapsed < total)
            {
                elapsed += Time.deltaTime;

                for (int i = 0; i < dots.Count; i++)
                {
                    RectTransform dot = dots[i];
                    if (dot == null)
                    {
                        continue;
                    }

                    float t = Mathf.Clamp01((elapsed - delays[i]) / travel);
                    Vector3 position = QuadraticBezier(starts[i], controls[i], to, t);
                    dot.position = position;

                    float scale = Mathf.Lerp(1f, 0.4f, t);
                    dot.localScale = new Vector3(scale, scale, 1f);

                    Image core = dot.GetComponent<Image>();
                    if (core != null)
                    {
                        Color c = color;
                        c.a = Mathf.Lerp(color.a, 0.1f, t);
                        core.color = c;
                    }

                    // 光晕跟在同一位置，飞行中逐渐散掉
                    RectTransform halo = halos[i];
                    if (halo != null)
                    {
                        halo.position = position;
                        float hs = Mathf.Lerp(1f, 1.5f, t);
                        halo.localScale = new Vector3(hs, hs, 1f);

                        Image haloImage = halo.GetComponent<Image>();
                        if (haloImage != null)
                        {
                            Color hc = haloImage.color;
                            hc.a = Mathf.Lerp(haloAlpha, 0f, t);
                            haloImage.color = hc;
                        }
                    }
                }

                yield return null;
            }

            for (int i = 0; i < dots.Count; i++)
            {
                if (dots[i] != null)
                {
                    SafeDestroy(dots[i].gameObject);
                }
                if (i < halos.Count && halos[i] != null)
                {
                    SafeDestroy(halos[i].gameObject);
                }
            }
        }

        private static Vector3 QuadraticBezier(Vector3 a, Vector3 control, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * control + t * t * b;
        }

        private GameController EnsureController()
        {
            if (GameController.Instance != null)
            {
                return GameController.Instance;
            }
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

        // ------------------------------------------------------------------
        // 手牌 UI
        // ------------------------------------------------------------------

        private void RefreshHand()
        {
            if (_controller == null || _layerCards == null)
            {
                return;
            }

            IReadOnlyList<CardInstance> hand = _controller.Hand;

            // 销毁已不在手牌里的卡牌 UI
            for (int i = _cardWidgets.Count - 1; i >= 0; i--)
            {
                CardWidget widget = _cardWidgets[i];
                if (widget == null || widget.Card == null || !ContainsCard(hand, widget.Card))
                {
                    if (widget != null)
                    {
                        if (_previewTarget == (RectTransform)widget.transform)
                        {
                            _previewTarget = null;
                        }
                        SafeDestroy(widget.gameObject);
                    }
                    _cardWidgets.RemoveAt(i);
                }
            }

            // 为新抽到的牌建 UI
            if (hand != null)
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    if (hand[i] == null || HasWidget(hand[i]))
                    {
                        continue;
                    }
                    // 卡牌 UI 走「模板实体 → 克隆」：CardWidget.Create 从场景里的非激活卡牌模板
                    // （CardWidgetTemplate）深拷贝出独立克隆体，克隆体自带 EntityInstance 身份。
                    CardWidget created = CardWidget.Create(_layerCards, this, hand[i]);
                    if (created == null)
                    {
                        // 模板克隆失败时只跳过这张牌的 UI，不能让 null 进列表（否则排布时会空引用）。
                        Debug.LogError("[CardGame][UI] 卡牌 UI 生成失败，已跳过：" + hand[i].Kind);
                        continue;
                    }
                    _cardWidgets.Add(created);
                }
            }

            LayoutHand();
            RefreshHud();
        }

        private void LayoutHand()
        {
            int count = _cardWidgets.Count;
            for (int i = 0; i < count; i++)
            {
                CardWidget widget = _cardWidgets[i];
                if (widget == null || widget.IsDragging)
                {
                    continue;
                }
                RectTransform rect = (RectTransform)widget.transform;
                rect.SetParent(_layerCards, false);
                float x = (i - (count - 1) * 0.5f) * CardSlotSpacing;
                rect.anchoredPosition = new Vector2(x, CardRowY);
                widget.SetHome(_layerCards, rect.GetSiblingIndex());
            }
        }

        private static bool ContainsCard(IReadOnlyList<CardInstance> hand, CardInstance card)
        {
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

        private bool HasWidget(CardInstance card)
        {
            for (int i = 0; i < _cardWidgets.Count; i++)
            {
                if (_cardWidgets[i] != null && _cardWidgets[i].Card == card)
                {
                    return true;
                }
            }
            return false;
        }

        private static void SafeDestroy(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(go);
            }
            else
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------
        // 拖动：抓起 / 提示 / 判定
        // ------------------------------------------------------------------

        /// <summary>左键按下：抓起卡牌 —— 把它提到遮罩之上，其余一切仍留在遮罩之下。</summary>
        public void BeginDrag(CardWidget widget, PointerEventData eventData)
        {
            if (widget == null || _layerAbove == null)
            {
                return;
            }
            // 战斗已结束（横幅已弹出）就不再允许出牌
            if (_battleEnded)
            {
                widget.ReturnHome();
                return;
            }
            widget.transform.SetParent(_layerAbove, true);
            widget.transform.SetAsLastSibling();

            // 互动开始：此时才盖上遮罩 —— 被抓起这张卡在 Layer_Above，其余一切都在遮罩之下。
            _interacting = true;
            RefreshMask();

            SetStatus("拖动中：松开时压在敌人上＝掷出，压在另一张卡上＝融合");
        }

        /// <summary>拖动中：实时提示本次松开会与谁交互（把该目标临时提到遮罩之上）。</summary>
        public void UpdateDrag(CardWidget widget, PointerEventData eventData)
        {
            RectTransform candidate = FindBestTarget(widget);
            if (candidate == _previewTarget)
            {
                return;
            }

            // 先把上一个提示目标还原到遮罩之下
            ClearPreview();

            if (candidate != null)
            {
                _previewTarget = candidate;
                // 提到拖动层，但排在被抓起的那张卡下面，避免盖住手里的卡
                candidate.SetParent(_layerAbove, true);
                candidate.SetAsFirstSibling();

                // 同时给实体加高亮，让「松手就会打它」更明显
                BattleEntityView view = FindEntityView(candidate);
                if (view != null)
                {
                    view.SetHighlight(true);
                }
            }
        }

        /// <summary>找出某个 RectTransform 对应的实体形象（玩家或敌人）。</summary>
        private BattleEntityView FindEntityView(RectTransform rect)
        {
            if (rect == null)
            {
                return null;
            }
            if (_playerView != null && _playerView.Rect == rect)
            {
                return _playerView;
            }
            for (int i = 0; i < _enemyViews.Count; i++)
            {
                if (_enemyViews[i] != null && _enemyViews[i].Rect == rect)
                {
                    return _enemyViews[i];
                }
            }
            return null;
        }

        /// <summary>左键松开：按投影重合判定「掷出」或「融合」。</summary>
        public void EndDrag(CardWidget widget, PointerEventData eventData)
        {
            ClearPreview();

            // 互动结束：收起遮罩（无论后面走哪条分支，都要收）。
            _interacting = false;
            RefreshMask();

            // 战斗已结束：卡牌直接归位，不再结算任何操作
            if (_battleEnded)
            {
                ReturnWidgetHome(widget);
                return;
            }

            RectTransform target = FindBestTarget(widget);
            if (target == null || widget == null || widget.Card == null)
            {
                // 既没压在敌人上、也没压在另一张卡上 ⇒ 不做任何操作：卡牌原样回到它原来的位置，
                // 不消耗出牌次数、不改动手牌。这是需求里的「不符合条件就无操作」分支。
                ReturnWidgetHome(widget);
                SetStatus(string.Empty);
                return;
            }

            if (_enemyRects.Contains(target))
            {
                ThrowCardAt(widget, target);
            }
            else
            {
                FuseWith(widget, target);
            }
        }

        /// <summary>找出与当前拖动卡「投影重合面积最大」的实体：敌人或另一张卡。</summary>
        private RectTransform FindBestTarget(CardWidget widget)
        {
            if (widget == null)
            {
                return null;
            }
            RectTransform cardRect = (RectTransform)widget.transform;

            RectTransform best = null;
            float bestArea = 0f;

            for (int i = 0; i < _enemyRects.Count; i++)
            {
                RectTransform enemy = _enemyRects[i];
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    continue;
                }
                // 已阵亡的敌人不作为落点：尸体仍然留在场上（只是被压暗、名字显示「已阵亡」），
                // 不排除的话把牌拖上去会判成「掷出」——牌被消耗、出牌次数被扣，却不产生任何伤害。
                if (i < _enemyTargets.Count && (_enemyTargets[i] == null || !_enemyTargets[i].IsAlive))
                {
                    continue;
                }
                float area = BattleUiKit.OverlapArea(cardRect, enemy);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = enemy;
                }
            }

            for (int i = 0; i < _cardWidgets.Count; i++)
            {
                CardWidget other = _cardWidgets[i];
                if (other == null || other == widget || other.IsDragging)
                {
                    continue;
                }
                RectTransform otherRect = (RectTransform)other.transform;
                float area = BattleUiKit.OverlapArea(cardRect, otherRect);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = otherRect;
                }
            }

            return best;
        }

        /// <summary>
        /// 把一张没被用掉的卡牌**放回它原来的位置**（原父节点 + 原兄弟序号），再重排手牌。
        ///
        /// 为什么不能只 SetParent(_layerCards) + LayoutHand()：卡牌被抓起时已经离开了 Layer_Cards，
        /// 重新挂回去会被追加到**末尾**，于是 LayoutHand 把它排到手的最后一格 —— 位置变了，
        /// 不是玩家说的「返回原位」。
        /// </summary>
        private void ReturnWidgetHome(CardWidget widget)
        {
            if (widget == null)
            {
                return;
            }

            widget.ReturnHome();
            if (widget.transform.parent != _layerCards)
            {
                widget.transform.SetParent(_layerCards, true);   // 兜底：HomeParent 缺失时至少回到手牌层
            }
            LayoutHand();
        }

        private void ClearPreview()
        {
            if (_previewTarget == null)
            {
                return;
            }
            // 取消高亮
            BattleEntityView view = FindEntityView(_previewTarget);
            if (view != null)
            {
                view.SetHighlight(false);
            }

            // 还原回原图层（敌人回实体层、卡牌回手牌层）
            if (_enemyRects.Contains(_previewTarget))
            {
                _previewTarget.SetParent(_layerEntities, true);
            }
            else
            {
                _previewTarget.SetParent(_layerCards, true);
            }
            _previewTarget = null;
        }

        private void ThrowCardAt(CardWidget widget, RectTransform enemyRect)
        {
            int index = _enemyRects.IndexOf(enemyRect);
            Combatant target = (index >= 0 && index < _enemyTargets.Count) ? _enemyTargets[index] : null;

            // 让引擎按「玩家拖到的那只」结算：PlayerPlayComponent.targetPolicy 是公开字段，
            // 临时换成固定目标策略，打完再还原。
            PlayerPlayComponent play = _controller.Context != null && _controller.Context.Player != null
                ? _controller.Context.Player.GetComponent<PlayerPlayComponent>()
                : null;
            ITargetPolicy previous = play != null ? play.targetPolicy : null;
            if (play != null && target != null)
            {
                play.targetPolicy = new FixedTargetPolicy(target);
            }

            CardInstance card = widget.Card;
            SetStatus("掷出：" + card.Kind + " → " + (target != null ? target.displayName : "敌人"));
            _controller.PlayCard(card);

            if (play != null)
            {
                play.targetPolicy = previous;
            }

            SafeDestroy(widget.gameObject);
            _cardWidgets.Remove(widget);
            RefreshHand();
        }

        private void FuseWith(CardWidget widget, RectTransform otherRect)
        {
            CardWidget other = null;
            for (int i = 0; i < _cardWidgets.Count; i++)
            {
                if (_cardWidgets[i] != null && (RectTransform)_cardWidgets[i].transform == otherRect)
                {
                    other = _cardWidgets[i];
                    break;
                }
            }
            if (other == null || other.Card == null)
            {
                widget.transform.SetParent(_layerCards, true);
                LayoutHand();
                return;
            }

            SetStatus("融合：" + widget.Card.Kind + " + " + other.Card.Kind);
            bool fused = _controller.TryFuseCards(widget.Card, other.Card);
            if (!fused)
            {
                SetStatus("这两张无法融合（无对应配方）");
                ReturnWidgetHome(widget);
                return;
            }

            SafeDestroy(widget.gameObject);
            _cardWidgets.Remove(widget);
            RefreshHand();
        }

        // ------------------------------------------------------------------
        // HUD / 结算
        // ------------------------------------------------------------------

        private void RefreshHud()
        {
            if (_controller == null || _controller.Context == null)
            {
                return;
            }

            Combatant player = _controller.Context.Player;
            if (player != null && _playerView != null)
            {
                _playerView.SetHp(EasedHp(_playerHpDisplay, player.CurrentHp, player.MaxHp, player.IsAlive),
                    player.MaxHp, player.IsAlive);
                if (_lastPlayerHp != int.MinValue && player.CurrentHp < _lastPlayerHp)
                {
                    _playerView.FlashDamage();
                }
            }
            if (player != null)
            {
                _lastPlayerHp = player.CurrentHp;
            }

            for (int i = 0; i < _enemyTargets.Count && i < _enemyViews.Count; i++)
            {
                Combatant enemy = _enemyTargets[i];
                BattleEntityView view = _enemyViews[i];
                if (enemy == null || view == null)
                {
                    continue;
                }

                HpDisplay display = i < _enemyHpDisplay.Count ? _enemyHpDisplay[i] : null;
                if (display == null)
                {
                    display = new HpDisplay();
                    _enemyHpDisplay.Add(display);
                }

                view.SetHp(EasedHp(display, enemy.CurrentHp, enemy.MaxHp, enemy.IsAlive),
                    enemy.MaxHp, enemy.IsAlive);

                // HP 下降时闪一下，让「刚挨了一下」看得见
                if (_lastEnemyHp[i] != int.MinValue && enemy.CurrentHp < _lastEnemyHp[i])
                {
                    view.FlashDamage();
                }
                _lastEnemyHp[i] = enemy.CurrentHp;
            }
        }

        /// <summary>
        /// 血条的「显示值」缓动：让涨血/掉血是**滑过去**的，而不是一帧跳到位。
        ///
        /// 回复还有个额外的「延时上涨」：治疗粒子飞到血条末端大约需要 0.55 s，这段时间里
        /// 显示值被按住不动（<see cref="HpDisplay.holdUntil"/>），粒子命中后才开始上涨 ——
        /// 这样「碰到血条末端 → 荧光 → 血条上涨」在画面上才是先后发生，而不是同时。
        /// 掉血不受这个 hold 影响（立刻反映），否则会看起来像没打中。
        /// </summary>
        private int EasedHp(HpDisplay display, int current, int max, bool alive)
        {
            if (display == null)
            {
                return current;
            }

            if (float.IsNaN(display.shown))
            {
                display.shown = current;
                return current;
            }

            if (!alive)
            {
                display.shown = 0f;      // 阵亡：立刻归零，别拖着动画
                return 0;
            }

            float delta = current - display.shown;
            if (delta < 0f)
            {
                // 掉血：取消 hold，立刻开始下降
                display.holdUntil = 0f;
            }
            else if (delta > 0f && display.holdUntil > 0f && Time.time < display.holdUntil)
            {
                return Mathf.RoundToInt(display.shown);   // 等粒子飞到血条末端
            }
            else
            {
                display.holdUntil = 0f;
            }

            float speed = Mathf.Max(1f, max) / HpRiseSeconds;
            display.shown = Mathf.MoveTowards(display.shown, current, speed * Time.deltaTime);
            return Mathf.RoundToInt(display.shown);
        }

        private void HandleBattleEnded(bool victory)
        {
            _battleEnded = true;
            _playerWon = victory;
            RefreshHud();
            ShowResult(victory);
        }

        private void ShowResult(bool playerWon)
        {
            if (_bannerRoot == null)
            {
                return;
            }
            _bannerRoot.SetActive(true);
            // 横幅是**对玩家**说话的第二人称口径：赢了写「你赢了」，输了写「你输了」。
            _bannerText.text = playerWon ? "你赢了" : "你输了";

            // 玩家输了 → 「继续」不可点击
            if (_continueButton != null)
            {
                _continueButton.interactable = playerWon;
                Text label = _continueButton.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.color = playerWon ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                }
            }
            SetStatus(string.Empty);
        }

        /// <summary>「再来一局」：同一关卡重新开战。</summary>
        public void RestartBattle()
        {
            StopAllCoroutines();
            if (_bannerRoot != null)
            {
                _bannerRoot.SetActive(false);
            }
            ClearAllCards();
            EnterBattle();
        }

        /// <summary>
        /// 「继续」被点击时触发，供**上层游戏流程**接管。
        ///
        /// 语义：本模块将来会被嵌进一个完整的游戏流程里当作其中一个环节，「继续」的含意是
        /// 「进入流程的下一个环节」—— 它**不再自己开一轮新对局**（开新对局是「再来一局」的职责）。
        /// 上层要接管，订阅本事件即可；当前没有上层流程时，只把横幅收起来并给出提示。
        /// </summary>
        public event System.Action ContinueRequested;

        /// <summary>「继续」：把控制权交回上层游戏流程（本模块不再开新的一局）。</summary>
        public void ContinueBattle()
        {
            if (!_playerWon)
            {
                return;   // 玩家输了时「继续」本就不可点击，这里再兜一道
            }

            Debug.Log("[CardGame][UI] 「继续」：请求进入游戏流程的下一个环节（本模块不再开新的一局）。");

            if (ContinueRequested != null)
            {
                ContinueRequested();
                return;
            }

            // 还没有上层流程接管：收起横幅，并明确告诉玩家「继续」不是重开一局。
            if (_bannerRoot != null)
            {
                _bannerRoot.SetActive(false);
            }
            SetStatus("「继续」已请求进入下一个环节（当前没有上层流程接管，本模块不会自行开新的一局）");
        }

        private void ClearAllCards()
        {
            for (int i = 0; i < _cardWidgets.Count; i++)
            {
                if (_cardWidgets[i] != null)
                {
                    SafeDestroy(_cardWidgets[i].gameObject);
                }
            }
            _cardWidgets.Clear();
            _previewTarget = null;
        }

        /// <summary>切换黑色遮罩的显隐（控制「互动时压暗非参与者」这个效果本身要不要生效）。</summary>
        public void ToggleMask()
        {
            maskVisible = !maskVisible;
            RefreshMask();
            if (_maskToggle != null)
            {
                _maskToggle.RefreshLabel();
            }
        }

        /// <summary>
        /// 按当前状态决定遮罩显不显示：**只有正在互动（有卡被抓起）时才盖**，
        /// 因为遮罩的职责是「盖住所有不参与互动的物体」，没有互动就没有需要盖住的对象。
        /// </summary>
        private void RefreshMask()
        {
            if (_layerMask == null)
            {
                return;
            }
            _layerMask.gameObject.SetActive(maskVisible && _interacting);
        }

        private void SetStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }
            RefreshHud();
        }

        /// <summary>固定目标策略：让「拖到哪只怪」就结算到哪只怪。</summary>
        private class FixedTargetPolicy : ITargetPolicy
        {
            private readonly Combatant _target;

            public FixedTargetPolicy(Combatant target)
            {
                _target = target;
            }

            public Combatant SelectTarget(BattleContext battle, CardInstance card)
            {
                return _target;
            }
        }
    }

    /// <summary>遮罩开关按钮的行为（单独一个组件，避免把按钮逻辑塞进模块主体）。</summary>
    public class CardBattleMaskToggle : MonoBehaviour
    {
        private CardBattleModule _module;
        private Text _label;

        public void Bind(CardBattleModule module, Text label)
        {
            _module = module;
            _label = label;
            RefreshLabel();
        }

        public void RefreshLabel()
        {
            if (_label != null && _module != null)
            {
                _label.text = _module.maskVisible ? "遮罩：开" : "遮罩：关";
            }
        }

        private void Start()
        {
            Button button = GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(OnClick);
            }
        }

        private void OnClick()
        {
            if (_module != null)
            {
                _module.ToggleMask();
            }
            RefreshLabel();
        }
    }
}
