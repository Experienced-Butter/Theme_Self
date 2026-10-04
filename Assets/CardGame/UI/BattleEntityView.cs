using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.UI
{
    /// <summary>
    /// 战斗里的一个「实体形象」（玩家 / 敌人共用）。
    ///
    /// 目标形态：2D、1080p 电脑端、uGUI。**不使用任何美术资源** —— 全部图形都由程序生成：
    ///   · 圆形头像 / 外圈描边 → <see cref="BattleUiKit.CircleSprite"/>
    ///   · HP 条（底槽、填充、分段刻度）→ <see cref="BattleUiKit.WhiteSprite"/>
    ///   · 全部文字 → <see cref="BattleUiKit.Font"/>
    ///
    /// 节点构成（自下而上，都在本对象的 RectTransform 之下）：
    ///   BackGlow   头像后的柔光（纯程序生成，让形象不再只是「一个扁圆」）
    ///   Outline    外圈描边：稍大的圆被头像盖住中心，露出的部分即描边；高亮时变亮黄并加粗
    ///   Body       圆形头像
    ///   Damage     受击闪白用的覆盖层（平时完全透明，仅在 FlashDamage 的 0.15 秒内可见）
    ///   NameText   名称（居中偏上）
    ///   HpSlot     HP 底槽
    ///   HpFill     HP 填充条（左对齐，宽度按 current/max 变化）
    ///   HpTicks    HP 分段刻度（纯装饰，提升可读性）
    ///   HpText     「当前 / 上限」（居中偏下）
    ///
    /// 交互约束：**本对象及其全部子节点的 Image / Text 都是 raycastTarget = false**。
    /// 实体不吃鼠标事件，否则会挡住 CardWidget 的卡牌拖动；「拖到目标上」的判定由
    /// CardBattleModule 用 BattleUiKit.OverlapArea 按矩形重合面积计算，不依赖射线检测。
    /// </summary>
    public class BattleEntityView : MonoBehaviour
    {
        // ---- 尺寸常量（1080p 参考分辨率下的像素）----
        // 垂直布局：名称钉在框顶、HP 文本钉在框底、血条紧贴 HP 文本之上，
        // 中间剩下的全部留给圆头像 —— 头像必须吃满剩余空间，不能挤在两行文字之间。
        //
        //   ┌─────────────────┐ ← 框顶
        //   │      名称        │  占用 NameAreaHeight
        //   │   ╭─────────╮   │
        //   │   │  头像    │   │  直径 = 框高 − 上下预留（见 ComputeAvatarDiameter）
        //   │   ╰─────────╯   │
        //   │   ▓▓▓▓▓▓▓▓▓▓▓   │  占用 HpBarHeight（在圆外，位于头像下方）
        //   │     当前 / 上限   │  占用 HpTextAreaHeight
        //   └─────────────────┘ ← 框底
        private const float NameAreaHeight = 24f;      // 框顶预留给名称的高度
        private const float HpBarHeight = 12f;         // 血条高度
        private const float HpTextAreaHeight = 22f;    // 框底预留给「当前 / 上限」的高度
        private const float HpBarHpTextGap = 2f;       // 血条与 HP 文本之间的缝
        private const float MinAvatarDiameter = 90f;   // 头像直径下限（框特别小时兜底；本模块只用到 240 / 200 两档，200 以上三个区域都在框内）
        private const int NameFontSize = 24;
        private const int HpFontSize = 20;
        private const float HpBarWidthRatio = 0.94f;   // HP 条宽度 = 实体宽度 × 该比例
        private const int HpTickCount = 4;             // 分段刻度数量
        private const float RingThickness = 4f;        // 常规描边粗细
        private const float RingHighlightExtra = 5f;   // 高亮时额外加粗的厚度（4px → 9px，非常显眼）
        private const float RingFootprint = 4f;        // 描边圆比头像直径大 2×RingFootprint
        private const float GlowExtra = 22f;           // 柔光圆比头像大多少（有上限，避免压到血条）
        private const float DamageFlashDuration = 0.15f;
        private const float HpFillInset = 4f;          // 填充条比底槽小多少
        // 名称 / 血条 与头像**描边外沿**之间的净缝
        private const float LayoutGapToRing = 5f;
        // 名称 / 血条 到「头像圆边界」的距离 = 净缝 + 描边厚度
        private const float LayoutGap = LayoutGapToRing + RingFootprint;

        // ---- 配色 ----
        private static readonly Color HpSlotColor = new Color(0.09f, 0.10f, 0.14f, 0.95f);
        private static readonly Color HpTextColor = new Color(0.94f, 0.97f, 1f, 1f);
        private static readonly Color DeadNameColor = new Color(0.60f, 0.62f, 0.68f, 0.95f);
        private static readonly Color HighlightColor = new Color(1f, 0.93f, 0.20f, 1f);
        private static readonly Color HighlightNameColor = new Color(1f, 0.96f, 0.55f, 1f);
        private static readonly float DeadDimAlpha = 0.55f;

        private static bool s_layoutLogged;       // LogLayout 每个进程只打一次
        private RectTransform _rect;
        private Image _glow;
        private Image _ring;
        private Image _body;
        private Image _damage;
        private Text _nameText;
        private Image _hpSlot;
        private RectTransform _hpFillRect;
        private Image _hpFill;
        private Text _hpText;

        private Color _baseColor = Color.white;   // 实体固有色（创建时传入）
        private string _displayName = string.Empty;
        private bool _alive = true;
        private bool _highlighted;
        private int _hpCurrent;
        private int _hpMax;
        private Coroutine _flashRoutine;

        /// <summary>本形象占据的矩形（外部用它做定位、滑入动画与「投影重合」判定）。</summary>
        public RectTransform Rect
        {
            get
            {
                if (_rect == null)
                {
                    _rect = transform as RectTransform;
                }
                return _rect;
            }
        }

        // ------------------------------------------------------------------
        // 创建
        // ------------------------------------------------------------------

        /// <summary>
        /// 创建一个实体形象。
        /// </summary>
        /// <param name="parent">父节点（通常是 Layer_Entities）。</param>
        /// <param name="displayName">显示名称。</param>
        /// <param name="color">实体固有色，用于头像与外圈描边。</param>
        /// <param name="size">整体尺寸（1080p 参考分辨率下的像素）。</param>
        /// <param name="anchoredPosition">相对父级中心的锚定位置。</param>
        public static BattleEntityView Create(Transform parent, string displayName, Color color,
                                              Vector2 size, Vector2 anchoredPosition)
        {
            GameObject go = new GameObject("Entity_" + displayName, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            BattleEntityView view = go.AddComponent<BattleEntityView>();
            view.Build(displayName, color, size);
            LogLayout(size);
            return view;
        }

        /// <summary>
        /// 打印一次布局实测值（头像直径等）。每个进程只打一次，试玩时不会刷屏。
        /// 实测：size = 240×240 → 头像 162px；size = 200×200 → 头像 122px。
        /// </summary>
        private static void LogLayout(Vector2 size)
        {
            if (s_layoutLogged)
            {
                return;
            }
            s_layoutLogged = true;
            float avatar = ComputeAvatarDiameter(size);
            Debug.Log(string.Format(
                "[CardGame][UI] BattleEntityView 布局实测：框 {0}×{1} → 头像直径 {2}px"
                + "（名称占 {3} 顶 / 血条 {4} + HP 文本 {5} 底，缝 {6}）",
                size.x, size.y, avatar,
                NameAreaHeight, HpBarHeight, HpTextAreaHeight, LayoutGap));
        }

        /// <summary>按给定尺寸搭出全部程序化图形。全部子节点一律 raycastTarget = false。</summary>
        private void Build(string displayName, Color color, Vector2 size)
        {
            _rect = (RectTransform)transform;
            _displayName = displayName ?? string.Empty;
            _baseColor = color;

            // ---- 垂直布局：名称钉框顶、HP 文本钉框底、血条紧贴 HP 文本之上，中间全给头像 ----
            // 每一行都与 ComputeAvatarDiameter 里的 reserved 逐项对应，改常量不会两处漂移。
            float avatar = ComputeAvatarDiameter(size);
            float halfY = size.y * 0.5f;

            // 头像的上下留白各为 halfSpare，因此头像圆正好居中于框
            float reserved = NameAreaHeight + LayoutGap
                             + HpBarHeight + HpBarHpTextGap + HpTextAreaHeight + LayoutGap;
            float halfSpare = (size.y - reserved - avatar) * 0.5f;
            if (halfSpare < 0f)
            {
                halfSpare = 0f;   // 兜底：框过小时不出现负留白
            }

            float nameTop = halfY - halfSpare;
            float nameBottom = nameTop - NameAreaHeight;
            // 名称 / 血条 到「头像圆边界」的距离：净缝 + 描边厚度
            float gapToCircle = LayoutGapToRing + RingFootprint;
            float avatarTop = nameBottom - gapToCircle;
            float avatarBottom = avatarTop - avatar;
            float barTop = avatarBottom - gapToCircle;
            float barBottom = barTop - HpBarHeight;
            float hpTextTop = barBottom - HpBarHpTextGap;
            float hpTextBottom = hpTextTop - HpTextAreaHeight;

            // 头像上下留白相等 → 头像圆正好居中于框内剩余空间
            float avatarCenterY = (avatarTop + avatarBottom) * 0.5f;

            // ① 柔光：比头像稍大的半透明圆，给形象一点体积感。
            // 上限：外沿不能越过名称下沿 / 血条上沿，否则那圈淡色柔光会垫到文字/血条下面。
            float glowMax = (avatarTop - avatarBottom) + 2f * LayoutGapToRing;
            float glow = Mathf.Min(avatar + GlowExtra, glowMax);
            Image glowImage = BattleUiKit.CreateImage("BackGlow", transform,
                new Color(color.r, color.g, color.b, 0.14f),
                new Vector2(glow, glow), new Vector2(0f, avatarCenterY));
            glowImage.sprite = BattleUiKit.CircleSprite;
            glowImage.raycastTarget = false;
            _glow = glowImage;

            // ② 外圈描边：稍大的圆，中心会被头像盖住，露出的环就是描边
            Image ring = BattleUiKit.CreateImage("Outline", transform,
                RingColor(color, false),
                new Vector2(avatar + RingThickness * 2f, avatar + RingThickness * 2f),
                new Vector2(0f, avatarCenterY));
            ring.sprite = BattleUiKit.CircleSprite;
            ring.raycastTarget = false;
            _ring = ring;

            // ③ 圆形头像
            Image body = BattleUiKit.CreateImage("Body", transform, color,
                new Vector2(avatar, avatar), new Vector2(0f, avatarCenterY));
            body.sprite = BattleUiKit.CircleSprite;
            body.raycastTarget = false;
            _body = body;

            // ④ 受击闪白覆盖层：与头像完全重合，平时 alpha = 0
            Image damage = BattleUiKit.CreateImage("Damage", transform, new Color(1f, 1f, 1f, 0f),
                new Vector2(avatar, avatar), new Vector2(0f, avatarCenterY));
            damage.sprite = BattleUiKit.CircleSprite;
            damage.raycastTarget = false;
            _damage = damage;

            // ⑤ 名称（框顶居中；再下移 halfSpare 保证与底部 HP 文本上下对称）
            _nameText = BattleUiKit.CreateText("Name", transform, _displayName, NameFontSize,
                Color.white, TextAnchor.MiddleCenter);
            AnchorTop((RectTransform)_nameText.transform, new Vector2(0f, -halfSpare),
                new Vector2(size.x, NameAreaHeight));
            _nameText.raycastTarget = false;

            // ⑥ HP 条：底槽 + 填充 + 分段刻度（在头像圆下方）
            float barW = Mathf.Max(size.x * HpBarWidthRatio, 40f);
            _hpSlot = BattleUiKit.CreateImage("HpSlot", transform, HpSlotColor,
                new Vector2(barW, HpBarHeight), new Vector2(0f, (barBottom + barTop) * 0.5f));
            _hpSlot.raycastTarget = false;

            Image slotShade = BattleUiKit.CreateImage("SlotShade", _hpSlot.transform,
                new Color(1f, 1f, 1f, 0.06f), new Vector2(barW, HpBarHeight * 0.5f),
                new Vector2(0f, HpBarHeight * 0.25f));
            slotShade.raycastTarget = false;

            Image fill = BattleUiKit.CreateImage("HpFill", _hpSlot.transform, _baseColor,
                new Vector2(barW - HpFillInset, HpBarHeight - HpFillInset), new Vector2(0f, 0f));
            fill.raycastTarget = false;
            _hpFill = fill;
            _hpFillRect = fill.rectTransform;
            _hpFillRect.pivot = new Vector2(0f, 0.5f);
            _hpFillRect.anchorMin = new Vector2(0.5f, 0.5f);
            _hpFillRect.anchorMax = new Vector2(0.5f, 0.5f);
            _hpFillRect.anchoredPosition = new Vector2(-(barW - HpFillInset) * 0.5f, 0f);

            BuildTicks(_hpSlot.transform, barW, HpBarHeight);

            // ⑦ HP 文本「当前 / 上限」（框底居中）
            _hpText = BattleUiKit.CreateText("HpText", transform, "0 / 0", HpFontSize,
                HpTextColor, TextAnchor.MiddleCenter);
            AnchorBottom((RectTransform)_hpText.transform, new Vector2(0f, halfSpare),
                new Vector2(size.x, HpTextAreaHeight));
            _hpText.raycastTarget = false;

            // 创建参数即初始状态：满血、存活、不高亮
            _alive = true;
            _highlighted = false;
            _hpCurrent = 0;
            _hpMax = 0;
            SetName(_displayName);
            SetHp(0, 0, true);
            SetHighlight(false);
        }

        /// <summary>
        /// 头像直径 = 框高 − 顶部预留（名称 + 一条缝）− 底部预留（血条 + 血条与文本的缝 + HP 文本 + 一条缝）。
        ///
        /// 这里的 LayoutGap = LayoutGapToRing + RingFootprint 已经含了描边厚度，所以算出来的
        /// 是「头像圆」的直径；描边圆再往外 2×RingFootprint，描边外沿与名称 / 血条之间还剩
        /// LayoutGapToRing = 5px 净缝（已用独立脚本在 240 与 200 两档逐项核对过，见汇报）。
        /// </summary>
        private static float ComputeAvatarDiameter(Vector2 size)
        {
            float reserved = NameAreaHeight + LayoutGap
                             + HpBarHeight + HpBarHpTextGap + HpTextAreaHeight + LayoutGap;
            float diameter = size.y - reserved;
            return Mathf.Max(Mathf.Min(size.x, diameter), MinAvatarDiameter);
        }

        /// <summary>贴住父级顶部：anchorMin/Max = (0.5, 1)，anchoredPosition.y 为相对顶边的偏移（取负往下）。</summary>
        private static void AnchorTop(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        /// <summary>贴住父级底部：anchorMin/Max = (0.5, 0)，anchoredPosition.y 为相对底边的偏移（取正往上）。</summary>
        private static void AnchorBottom(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        /// <summary>HP 条上的分段刻度（纯装饰，只需要 4 条竖线就能显著提升可读性）。</summary>
        private static void BuildTicks(Transform parent, float barWidth, float barHeight)
        {
            for (int i = 1; i <= HpTickCount; i++)
            {
                float x = -barWidth * 0.5f + barWidth * (i / (float)(HpTickCount + 1));
                Image tick = BattleUiKit.CreateImage("HpTick" + i, parent,
                    new Color(0f, 0f, 0f, 0.28f), new Vector2(2f, barHeight - 4f), new Vector2(x, 0f));
                tick.raycastTarget = false;
            }
        }

        // ------------------------------------------------------------------
        // 公开 API
        // ------------------------------------------------------------------

        /// <summary>设置显示名称（阵亡状态下名称固定显示「已阵亡」，恢复存活后显示这个名字）。</summary>
        public void SetName(string displayName)
        {
            _displayName = displayName ?? string.Empty;
            if (_nameText != null)
            {
                _nameText.text = _alive ? _displayName : "已阵亡";
            }
        }

        /// <summary>
        /// 刷新血量。填充条宽度按 current/max 变化，颜色随比例由深绿 → 黄 → 红插值。
        /// alive = false 时整体压暗、描边转灰、名称显示「已阵亡」。
        /// </summary>
        /// <summary>
        /// 血条填充的**当前末端**（世界坐标）。回复粒子就朝这里飞，命中处产生荧光。
        ///
        /// 用填充自己的 rect 算（pivot 是 (0, 0.5)，所以 rect.xMax 就是右边缘），
        /// 这样不依赖血条槽的 pivot 与锚点设置。
        /// </summary>
        public Vector3 HpBarFillEnd
        {
            get
            {
                if (_hpFillRect == null)
                {
                    return transform.position;
                }
                return _hpFillRect.TransformPoint(new Vector3(_hpFillRect.rect.xMax, 0f, 0f));
            }
        }

        /// <summary>血条槽与填充是否已经建好（回复表现需要它才能定位）。</summary>
        public bool HasHpBar
        {
            get { return _hpFillRect != null && _hpSlot != null; }
        }

        public void SetHp(int current, int max, bool alive)
        {
            _alive = alive;
            _hpMax = Mathf.Max(0, max);
            _hpCurrent = Mathf.Clamp(current, 0, _hpMax);

            float ratio = _hpMax > 0 ? _hpCurrent / (float)_hpMax : 0f;
            bool fade = !alive;
            float posRatio = alive ? ratio : Mathf.Min(ratio, 0.999f);

            if (_nameText != null)
            {
                _nameText.text = alive ? _displayName : "已阵亡";
                _nameText.color = alive
                    ? (_highlighted ? HighlightNameColor : Color.white)
                    : DeadNameColor;
            }

            if (_hpText != null)
            {
                _hpText.text = _hpCurrent + " / " + _hpMax;
                _hpText.color = alive ? HpTextColor : new Color(HpTextColor.r, HpTextColor.g, HpTextColor.b, 0.65f);
            }

            if (_hpSlot != null && _hpFillRect != null && _hpFillRect.parent is RectTransform)
            {
                RectTransform slotRect = (RectTransform)_hpFillRect.parent;
                float innerWidth = slotRect.sizeDelta.x - 4f;
                _hpFillRect.sizeDelta = new Vector2(Mathf.Max(0f, innerWidth * posRatio), _hpFillRect.sizeDelta.y);
                _hpFillRect.anchoredPosition = new Vector2(-innerWidth * 0.5f, 0f);
            }

            if (_hpFill != null)
            {
                _hpFill.color = Dim(BarColor(ratio), fade);
            }

            if (_body != null)
            {
                _body.color = Dim(alive ? _baseColor : Grayscale(_baseColor), fade);
            }
            if (_glow != null)
            {
                Color g = alive ? _baseColor : Grayscale(_baseColor);
                _glow.color = new Color(g.r, g.g, g.b, DimAlpha(0.14f, fade));
            }
            if (_hpSlot != null)
            {
                _hpSlot.color = Dim(HpSlotColor, fade);
            }

            ApplyHighlight(force: true);
        }

        /// <summary>
        /// 「将要交互」的提示高亮：外圈描边变亮黄并加粗。
        /// 拖动卡牌时由 CardBattleModule 在「松手会压到这只实体」时打开，必须一眼可见。
        /// </summary>
        public void SetHighlight(bool on)
        {
            _highlighted = on;
            ApplyHighlight(force: false);
        }

        /// <summary>受击反馈：头像短暂染白再恢复（0.15 秒往返）。编辑模式下不会启动协程，也不会报错。</summary>
        public void FlashDamage()
        {
            if (_body == null)
            {
                return;
            }
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                // 编辑模式 / 未激活：不启动协程（编辑模式启动协程会报错），只保证不留下残留的闪白
                if (_damage != null)
                {
                    _damage.color = new Color(1f, 1f, 1f, 0f);
                }
                return;
            }

            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }
            _flashRoutine = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            float half = DamageFlashDuration * 0.5f;
            try
            {
                if (_damage != null)
                {
                    _damage.color = Color.white;
                }
                yield return new WaitForSeconds(half);

                float t = 0f;
                while (t < half)
                {
                    t += Time.deltaTime;
                    if (_damage != null)
                    {
                        // alpha 0 → 1，颜色恒为白：头像被逐渐「染白」再恢复
                        _damage.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0f, Mathf.Clamp01(t / half)));
                    }
                    yield return null;
                }
            }
            finally
            {
                if (_damage != null)
                {
                    _damage.color = new Color(1f, 1f, 1f, 0f);
                }
                _flashRoutine = null;
            }
        }

        private void OnDisable()
        {
            // 对象被隐藏 / 销毁时不要留下停留的闪白（也让编辑模式的反复修改是幂等的）
            if (_damage != null)
            {
                _damage.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        // ------------------------------------------------------------------
        // 内部绘制细节
        // ------------------------------------------------------------------

        /// <summary>把高亮状态落到描边颜色与粗细上。force = true 时无论是否变化都重刷。</summary>
        private void ApplyHighlight(bool force)
        {
            if (_ring == null)
            {
                return;
            }
            float avatar = _body != null ? _body.rectTransform.sizeDelta.x : 0f;
            if (avatar <= 0f)
            {
                avatar = Mathf.Min(Rect.sizeDelta.x, Rect.sizeDelta.y);
            }

            float thickness = RingThickness + (_highlighted ? RingHighlightExtra : 0f);
            Vector2 target = new Vector2(avatar + thickness * 2f, avatar + thickness * 2f);
            if (force || _ring.rectTransform.sizeDelta != target)
            {
                _ring.rectTransform.sizeDelta = target;
            }

            bool fade = !_alive;
            Color ringBase = _alive ? _baseColor : Grayscale(_baseColor);
            _ring.color = Dim(RingColor(ringBase, _highlighted), fade);
        }

        /// <summary>描边颜色：常规 = 实体固有色提亮；高亮 = 亮黄。</summary>
        private static Color RingColor(Color baseColor, bool highlighted)
        {
            if (highlighted)
            {
                return HighlightColor;
            }
            float lighten = 0.35f;
            return new Color(
                Mathf.Clamp01(baseColor.r + lighten),
                Mathf.Clamp01(baseColor.g + lighten),
                Mathf.Clamp01(baseColor.b + lighten),
                1f);
        }

        /// <summary>
        /// 血条填充色**按阵营区分**：玩家蓝、敌人红（用户口径）。
        /// 取实体固有色 <see cref="_baseColor"/>（模块创建时玩家传蓝、敌人传红），
        /// 残血时略微压暗以保留一点「血量告急」的观感，但不改变色相。
        /// </summary>
        private Color BarColor(float ratio)
        {
            Color bar = _baseColor;
            float dim = Mathf.Lerp(0.62f, 1f, Mathf.Clamp01(ratio));
            return new Color(bar.r * dim, bar.g * dim, bar.b * dim, 1f);
        }

        private static Color Grayscale(Color c)
        {
            float v = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(v, v, v, c.a);
        }

        private static Color Dim(Color c, bool fade)
        {
            if (!fade)
            {
                return c;
            }
            // 压暗：向黑收敛，同时保留一点 alpha 让整体看起来「退了半步」
            return new Color(c.r * DeadDimAlpha, c.g * DeadDimAlpha, c.b * DeadDimAlpha, c.a);
        }

        private static float DimAlpha(float alpha, bool fade)
        {
            return fade ? alpha * DeadDimAlpha : alpha;
        }
    }
}
