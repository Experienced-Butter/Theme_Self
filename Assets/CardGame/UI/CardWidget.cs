using CardGame.Cards;
using CardGame.Core;
using CardGame.Templates;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGame.UI
{
    /// <summary>
    /// 卡牌战斗里的一张「可用鼠标拖动的卡牌」。
    ///
    /// 交互（本类的全部职责就是把这几个事件转给 CardBattleModule）：
    ///   · 左键按下  → 卡牌被「抓起」，记录它与光标的相对位置
    ///   · 左键拖动  → 卡牌与光标保持固定相对位置移动，并实时查询「会与谁交互」以做提示
    ///   · 左键松开  → 判定掷出（投影压到敌人）或融合（投影压到其他卡牌）
    ///
    /// 自身不做任何规则判定，也不直接改游戏状态 —— 一律交给 CardBattleModule。
    ///
    /// 生成方式（与项目既有 template→clone 架构一致，见 <see cref="CardWidgetTemplate"/>）：
    ///   本类不再自己 AddComponent 拼卡面，而是由场景里那张**非激活的卡牌模板实体**深拷贝出克隆体。
    ///   克隆体自带完整卡面（底图 / 顶部色条 / 种类 / 名称 / 数值）与一份
    ///   <see cref="EntityInstance"/> 身份标记，卡面数值由模板在 OnAfterClone 里按克隆体自己的
    ///   卡牌实例写入 —— 因此**克隆体之间、克隆体与模板之间不共享任何可变状态**。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CardWidget : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>卡面尺寸（1080p 参考分辨率下的像素）。</summary>
        public static readonly Vector2 CardSize = new Vector2(150f, 210f);

        /// <summary>对应的游戏内卡牌实例。</summary>
        public CardInstance Card { get; private set; }

        /// <summary>正在被拖动。</summary>
        public bool IsDragging { get; private set; }

        /// <summary>本卡牌的交互转发目标（由 <see cref="InitializeFromTemplate(CardBattleModule, CardInstance)"/> 写入；模板上为 null）。</summary>
        public CardBattleModule Owner
        {
            get { return _owner; }
        }

        /// <summary>来源模板的 templateId（模板本身上为 null，克隆体上为模板对象的名称）。</summary>
        public string SourceTemplateId
        {
            get { return _templateId; }
        }

        /// <summary>拖动前的父节点与兄弟序号，松手后据此归位。</summary>
        public Transform HomeParent { get; private set; }
        public int HomeSiblingIndex { get; private set; }

        /// <summary>记录「家」的位置（由 CardBattleModule 在排布手牌时调用）。</summary>
        public void SetHome(Transform parent, int siblingIndex)
        {
            HomeParent = parent;
            HomeSiblingIndex = siblingIndex;
        }

        private CardBattleModule _owner;
        private RectTransform _rect;
        private Vector3 _grabOffset;

        // 模板标识：模板本体为 null，模板在 OnAfterClone 里把 templateId 写进每个克隆体。
        private string _templateId;

        // 卡面图元引用：**每次 ApplyCardFace 都由 ResolveFaceRefs 重取**，字段只是当次刷新的缓存。
        // 之所以不能只依赖字段：这些引用会被 Unity 随 Instantiate 一起拷到克隆体上，
        // 若不重取，克隆体就会指向「模板的那几个 Text/Image」，卡面互相串改。
        private Image _background;
        private Text _kindText;
        private Text _nameText;
        private Text _statsText;

        // 边框 / 能量波动状态（只有高级卡牌会动，Basic 是静态暗边）
        private const float BorderThickness = 6f;
        private Image _borderTop;
        private Image _borderBottom;
        private Image _borderLeft;
        private Image _borderRight;
        private CardTier _tier = CardTier.Basic;
        private bool _energyWave;
        private Color _borderBright = Color.white;
        private Color _borderDim = Color.gray;

        /// <summary>
        /// 创建一张可拖动卡牌：从卡牌模板实体克隆（不再逐个 AddComponent 拼 UI）。
        /// parent 为空时仍会克隆，只是留在场景根 —— 调用方随后可自行 Reparent。
        ///
        /// 模板来源：场景里已有一张模板就复用（保证「一个场景只保留一张模板」），
        /// 否则先 <see cref="CardWidgetTemplate.Create"/> 建一张再克隆。
        /// </summary>
        public static CardWidget Create(Transform parent, CardBattleModule owner, CardInstance card)
        {
            CardWidgetTemplate template = CardWidgetTemplate.FindInScene();
            if (template == null)
            {
                template = CardWidgetTemplate.Create(parent, CardWidgetTemplate.TemplateId);
            }
            if (template == null)
            {
                Debug.LogError("[CardGame][UI] 卡牌模板实体创建失败，无法生成卡牌 UI。");
                return null;
            }

            return template.Clone(parent, owner, card);
        }

        /// <summary>
        /// 由模板克隆出的卡牌在克隆后调用：绑定到「它自己的」卡牌实例与交互宿主，并刷新卡面。
        /// 这是 <see cref="CardWidgetTemplate.Clone"/> 对外的初始化入口。
        /// 模板本体永远不被绑定（模板的 Card 必须保持 null），因此模板不会挂上任何一局的卡牌数据。
        /// </summary>
        public void InitializeFromTemplate(CardBattleModule owner, CardInstance card)
        {
            // 来源模板标识：从同物体上的模板组件取。
            CardWidgetTemplate template = GetComponent<CardWidgetTemplate>();
            string sourceId = template != null ? template.templateId : null;
            InitializeFromTemplate(owner, card, sourceId);
        }

        /// <summary>
        /// 内部重载：额外显式传入来源模板标识，供 <see cref="CardWidgetTemplate.OnAfterClone"/> 使用
        /// （那一刻克隆体身上的模板标记已被基类摘掉，取不到组件）。
        /// </summary>
        internal void InitializeFromTemplate(CardBattleModule owner, CardInstance card, string templateId)
        {
            _owner = owner;
            if (!string.IsNullOrEmpty(templateId))
            {
                _templateId = templateId;
            }
            Card = card;
            _rect = (RectTransform)transform;

            ApplyCardFace();
        }

        /// <summary>
        /// 按当前卡牌实体刷新卡面文字与底色。可重复调用（卡牌升级 / 强化后重绘也走这里）。
        /// Card 为 null 时显示占位卡面（模板默认状态）。
        ///
        /// 引用来源：每次刷新都**按名字在孩子里重新取一遍**卡面节点，而不是沿用字段里的旧引用。
        /// 原因是本类会被整体克隆：Unity 的 Instantiate 会把模板上这些字段一并拷到克隆体，
        /// 若不重取，克隆体就会指向**模板**的 Text/Image，导致所有卡面互相串改 —— 这正是
        /// 「克隆体之间不共享可变状态」必须防住的那一类 bug。
        /// </summary>
        internal void ApplyCardFace()
        {
            ResolveFaceRefs();

            CardDefinitionData def = Card != null && Card.Identity != null ? Card.Identity.definition : null;

            Color tierColor = def != null ? TierColor(def.tier) : new Color(0.25f, 0.25f, 0.25f, 1f);

            if (_background != null)
            {
                _background.color = tierColor;
            }

            // 边框：高级卡牌（非 Basic）走「能量波动」，Basic 只给一圈静态暗边。
            _tier = def != null ? def.tier : CardTier.Basic;
            _energyWave = _tier != CardTier.Basic;
            _borderBright = Brighten(tierColor, 0.45f);
            _borderDim = Brighten(tierColor, -0.25f);

            if (!_energyWave)
            {
                Color still = _borderDim;
                still.a = 0.75f;
                ApplyBorderFlat(still);
            }
            else
            {
                UpdateEnergyWave(0f);
            }

            if (_kindText != null)
            {
                _kindText.text = Card != null ? Card.Kind.ToString() : "?";
            }
            if (_nameText != null)
            {
                _nameText.text = Card != null ? Card.DisplayName : string.Empty;
            }
            if (_statsText != null)
            {
                _statsText.text = BuildStats(def);
            }
        }

        /// <summary>
        /// 把卡面图元引用刷新为「本对象自己的」那些节点。只认自己孩子里的同名节点，
        /// 因此无论本对象是模板还是克隆体，取到的永远是属于自己的那一份。
        /// </summary>
        private void ResolveFaceRefs()
        {
            _background = GetComponent<Image>();
            _kindText = FindChildText("Kind");
            _nameText = FindChildText("Name");
            _statsText = FindChildText("Stats");
            _borderTop = FindChildImage("Border_Top");
            _borderBottom = FindChildImage("Border_Bottom");
            _borderLeft = FindChildImage("Border_Left");
            _borderRight = FindChildImage("Border_Right");
        }

        private Image FindChildImage(string childName)
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].gameObject != null && images[i].gameObject.name == childName)
                {
                    return images[i];
                }
            }
            return null;
        }

        private Text FindChildText(string childName)
        {
            Text[] texts = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].gameObject != null && texts[i].gameObject.name == childName)
                {
                    return texts[i];
                }
            }
            return null;
        }

        /// <summary>卡面：层级底色 + 种类 / 名称 / 关键数值。数值唯一来源是 CardDefaults（经 CardIdentityComponent）。</summary>
        internal void BuildCardFace()
        {
            _rect = (RectTransform)transform;

            _background = gameObject.AddComponent<Image>();
            _background.sprite = BattleUiKit.WhiteSprite;
            _background.raycastTarget = true;   // 必须能接收鼠标事件
            _background.color = new Color(0.25f, 0.25f, 0.25f, 1f);

            BuildBorder();

            // 顶部色条
            Image bar = BattleUiKit.CreateImage("TierBar", transform,
                new Color(1f, 1f, 1f, 0.22f), new Vector2(CardSize.x - 12f, 10f), new Vector2(0f, CardSize.y * 0.5f - 12f));
            bar.raycastTarget = false;

            _kindText = BattleUiKit.CreateText("Kind", transform, "?", 40, Color.white, TextAnchor.MiddleCenter);
            _kindText.rectTransform.anchoredPosition = new Vector2(0f, 30f);

            _nameText = BattleUiKit.CreateText("Name", transform, string.Empty, 20,
                new Color(0.95f, 0.96f, 1f, 1f), TextAnchor.MiddleCenter);
            _nameText.rectTransform.anchoredPosition = new Vector2(0f, -6f);

            _statsText = BattleUiKit.CreateText("Stats", transform, string.Empty, 18,
                new Color(0.88f, 0.92f, 1f, 1f), TextAnchor.UpperCenter);
            _statsText.rectTransform.anchoredPosition = new Vector2(0f, -34f);

            ApplyCardFace();
        }

        /// <summary>
        /// 卡牌边框：四条细边（上/下/左/右），颜色取自卡牌层级的对应色。
        ///
        /// 它同时是「能量波动」特效的载体：高级卡牌（非 <see cref="CardTier.Basic"/>）会让四条边
        /// **按相位依次明暗**（上→右→下→左），看上去就是一圈沿边框流动的能量。
        /// 四条边都不吃射线 —— 卡面的射线由 <see cref="_background"/> 负责，边框绝不参与，
        /// 否则会把拖动事件挡在边上。
        /// </summary>
        private void BuildBorder()
        {
            float halfW = CardSize.x * 0.5f;
            float halfH = CardSize.y * 0.5f;
            float t = BorderThickness;

            _borderTop = CreateBorderBar("Border_Top", new Vector2(CardSize.x, t),
                new Vector2(0f, halfH - t * 0.5f));
            _borderBottom = CreateBorderBar("Border_Bottom", new Vector2(CardSize.x, t),
                new Vector2(0f, -halfH + t * 0.5f));
            // 左右两条避开上下两条的厚度，免得四个角叠成一块更亮的方块
            _borderLeft = CreateBorderBar("Border_Left", new Vector2(t, CardSize.y - t * 2f),
                new Vector2(-halfW + t * 0.5f, 0f));
            _borderRight = CreateBorderBar("Border_Right", new Vector2(t, CardSize.y - t * 2f),
                new Vector2(halfW - t * 0.5f, 0f));
        }

        private Image CreateBorderBar(string name, Vector2 size, Vector2 anchoredPosition)
        {
            Image bar = BattleUiKit.CreateImage(name, transform, Color.white, size, anchoredPosition);
            bar.raycastTarget = false;
            return bar;
        }

        /// <summary>能量波动：四条边各差 1/4 个周期，形成绕边框流动的明暗波。</summary>
        private void UpdateEnergyWave(float time)
        {
            float speed = _tier == CardTier.Ultimate ? 3.6f
                : (_tier == CardTier.Duplicator ? 2.9f : 2.2f);

            ApplyBorderWave(_borderTop, time, speed, 0f);
            ApplyBorderWave(_borderRight, time, speed, 0.25f);
            ApplyBorderWave(_borderBottom, time, speed, 0.5f);
            ApplyBorderWave(_borderLeft, time, speed, 0.75f);
        }

        private void ApplyBorderWave(Image bar, float time, float speed, float phase)
        {
            if (bar == null)
            {
                return;
            }

            float w = 0.5f + 0.5f * Mathf.Sin(time * speed - phase * Mathf.PI * 2f);
            Color c = Color.Lerp(_borderDim, _borderBright, w);
            c.a = Mathf.Lerp(0.55f, 1f, w);
            bar.color = c;
        }

        private void ApplyBorderFlat(Color color)
        {
            if (_borderTop != null) _borderTop.color = color;
            if (_borderBottom != null) _borderBottom.color = color;
            if (_borderLeft != null) _borderLeft.color = color;
            if (_borderRight != null) _borderRight.color = color;
        }

        private void Update()
        {
            if (!_energyWave)
            {
                return;
            }
            UpdateEnergyWave(Time.time);
        }

        private static Color Brighten(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);
        }

        /// <summary>把卡牌的关键数值压缩成几行，直接显示在卡面上。</summary>
        private static string BuildStats(CardDefinitionData d)
        {
            if (d == null)
            {
                return string.Empty;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder(96);
            if (d.damage > 0) sb.Append("伤害 ").Append(d.damage).Append('\n');
            if (d.healInstant > 0) sb.Append("治疗 ").Append(d.healInstant).Append('\n');
            if (d.regenPerTurn > 0) sb.Append("回复 ").Append(d.regenPerTurn).Append('×').Append(d.regenTurns).Append('\n');
            if (d.hitDownPercent > 0f) sb.Append("降命中 ").Append(Mathf.RoundToInt(d.hitDownPercent * 100f)).Append("%\n");
            if (d.defenseDownPercent > 0f) sb.Append("降防御 ").Append(Mathf.RoundToInt(d.defenseDownPercent * 100f)).Append("%\n");
            if (d.drawCount > 0) sb.Append("抽牌 ").Append(d.drawCount).Append('\n');
            if (d.grantImmunity) sb.Append("免疫\n");
            if (d.grantInitiative) sb.Append("先手\n");
            if (d.extraPlaysNextTurn > 0) sb.Append("+").Append(d.extraPlaysNextTurn).Append(" 出手\n");
            if (d.fusionRecipe != null && d.fusionRecipe.Length == 2)
            {
                sb.Append(d.fusionRecipe[0]).Append('+').Append(d.fusionRecipe[1]);
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>层级底色：一眼区分基础卡 / 合成卡 / 大招 / 复制器。</summary>
        public static Color TierColor(CardTier tier)
        {
            switch (tier)
            {
                case CardTier.Basic: return new Color(0.20f, 0.32f, 0.55f, 1f);
                case CardTier.Fused: return new Color(0.55f, 0.40f, 0.14f, 1f);
                case CardTier.Ultimate: return new Color(0.46f, 0.18f, 0.55f, 1f);
                case CardTier.Duplicator: return new Color(0.12f, 0.45f, 0.40f, 1f);
                default: return new Color(0.3f, 0.3f, 0.3f, 1f);
            }
        }

        // ------------------------------------------------------------------
        // 鼠标事件 → 交给 CardBattleModule
        // ------------------------------------------------------------------

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Card == null || _owner == null || _rect == null)
            {
                return;
            }
            // 只有左键能抓起卡牌：uGUI 对左/右/中键都会派发 pointerDown/drag，
            // 不过滤的话右键、中键也能把牌掷出去，与「左键按住抓起」的需求不符。
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            // 战斗已结束（结算横幅已弹出）时禁止抓起：此时松开不会结算任何东西，
            // 让卡牌还能被抓起、还能把敌人抬到遮罩之上，只会让画面看起来像是还能出牌。
            if (!_owner.CanInteractWithCards)
            {
                return;
            }
            HomeParent = transform.parent;
            HomeSiblingIndex = transform.GetSiblingIndex();

            // 保持「卡牌与光标的相对位置不变」：记下按下瞬间的差值，拖动时一直沿用这个差值。
            _grabOffset = _rect.position - (Vector3)eventData.position;
            IsDragging = true;
            _owner.BeginDrag(this, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsDragging || _rect == null)
            {
                return;
            }
            // 只有被左键抓起的牌才跟随光标（右键拖动不得移动卡牌）。
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            _rect.position = (Vector3)eventData.position + _grabOffset;
            _owner.UpdateDrag(this, eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!IsDragging)
            {
                return;
            }
            // 非左键松开不结算：保持「抓起 → 左键松开」这一条完整链路。
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            IsDragging = false;
            _owner.EndDrag(this, eventData);
        }

        /// <summary>把卡牌放回原来的父节点与位置（取消拖动 / 交互结束后调用）。</summary>
        public void ReturnHome()
        {
            if (HomeParent != null)
            {
                transform.SetParent(HomeParent, false);
                transform.SetSiblingIndex(Mathf.Clamp(HomeSiblingIndex, 0, HomeParent.childCount - 1));
            }
        }
    }
}
