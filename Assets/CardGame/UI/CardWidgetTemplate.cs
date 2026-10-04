using CardGame.Cards;
using CardGame.Templates;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGame.UI
{
    /// <summary>
    /// 卡牌战斗 UI 的「卡牌模板实体」—— 与项目既有的
    /// `Bootstrap/SceneTemplateBuilder` + `Templates/EntityTemplate` 架构完全同构，
    /// 只不过它克隆出来的是**可拖动的卡牌 UI**，而不是战斗数据实体。
    ///
    /// 生命周期与约定（与 ARCHITECTURE 第 5 节的模板约定一致）：
    ///   1) 场景里**恰好一张**模板，且**必须保持 SetActive(false)**（<see cref="CardWidget.Create"/> 负责找或建）；
    ///   2) 模板自身带着完整的卡面结构（底图 / 顶部色条 / 种类 / 名称 / 数值）
    ///      与一个 <see cref="CardWidget"/> 组件，但**不绑定任何卡牌实例**（Card == null）；
    ///   3) 出牌时由 <see cref="Clone"/> 走基类的
    ///      <see cref="EntityTemplate.CreateRuntimeInstance"/>：深拷贝 → 激活 → 摘掉模板标记
    ///      → 补上 <see cref="EntityInstance"/>（全局唯一 instanceId）→ 回调 OnAfterClone；
    ///   4) <see cref="OnAfterClone"/> 在克隆体上把「它自己的卡牌实例 / 交互宿主 / 模板标识」写进
    ///      克隆体的 CardWidget，并按该实例重绘卡面。
    ///
    /// 因此克隆体与模板、克隆体与克隆体之间不存在共享可变状态：卡面文字是克隆体各自 Text 组件的
    /// 字符串（不可变），卡牌定义挂在克隆体自己的 CardInstance 上，RectTransform 也是各自一份。
    ///
    /// 与 Prefab 的关系：本工程不使用 Prefab 资源（卡面完全由代码生成），所以「模板」以
    /// **场景内一个非激活的 GameObject** 的形式存在；Unity 的 Instantiate 对它做的同样是
    /// 一套完整的深拷贝，语义与克隆 Prefab 一致。
    /// </summary>
    public class CardWidgetTemplate : EntityTemplate
    {
        /// <summary>模板对象的默认名称（<see cref="Create"/> 未给名字时使用），也用作 templateId。</summary>
        public const string TemplateId = "CardWidgetTemplate";

        /// <summary>parent 为 null 时的兜底父级名（运行时创建的容器，避免污染场景根）。</summary>
        private const string FallbackRootName = "CardUiTemplates";

        /// <summary>已克隆出的卡牌数量（供自检核对「每次克隆都是全新实例」用）。</summary>
        public int CloneCount { get; private set; }

        /// <summary>模板本身（**非激活**的卡牌对象），便于在 Inspector 里检查。</summary>
        public GameObject TemplateObject
        {
            get { return gameObject; }
        }

        // ---------------------------------------------------------------- 建立 / 查找模板

        /// <summary>
        /// 在 parent 下创建一个「UI 卡牌模板」：一个 inactive 的卡牌对象（含完整卡面结构），并挂上本组件。
        /// templateName 为空时使用默认名 <see cref="TemplateId"/>。
        ///
        /// 本方法是**新建**语义。若希望「整个场景只保留一张模板、重复调用不重复建」，
        /// 请先 <see cref="FindInScene"/> 再决定是否 Create —— <see cref="CardWidget.Create"/> 就是这么做的。
        /// </summary>
        public static CardWidgetTemplate Create(Transform parent, string templateName)
        {
            string name = string.IsNullOrEmpty(templateName) ? TemplateId : templateName;
            Transform root = parent != null ? parent : EnsureFallbackRoot();
            GameObject go = new GameObject(name, typeof(RectTransform));

            // 模板必须是非激活的：先 SetActive(false) 再补组件与卡面，
            // 这样即使后续代码出错，也不会让半成品模板出现在画面上。
            go.SetActive(false);
            if (root != null)
            {
                go.transform.SetParent(root, false);
            }

            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                // 与 CardWidget.Create 时代完全一致的初始矩形：手牌区底部锚点 + 卡面尺寸。
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = CardWidget.CardSize;
                rect.anchoredPosition = Vector2.zero;
            }

            CardWidgetTemplate template = go.AddComponent<CardWidgetTemplate>();
            template.templateId = name;

            // 模板的卡面结构现场搭一次，之后所有克隆体都从这份结构深拷贝得到。
            CardWidget widget = go.AddComponent<CardWidget>();
            widget.BuildCardFace();   // Card == null → 占位卡面（底色灰、"?"、空名称/数值）

            // 再确认一次：AddComponent 不会激活对象，但显式写出来可以让意图一目了然。
            go.SetActive(false);
            return template;
        }

        /// <summary>
        /// 活动场景里的卡牌模板实体（含非激活对象）。找不到时返回 null。
        ///
        /// 只认**真正的模板**：非激活、且 CardWidget 未绑定任何卡牌。
        /// 这条判据很关键 —— 运行时 <c>Destroy</c> 摘除模板标记是**延迟到帧末**的，
        /// 所以同一帧内刚克隆出来的卡牌身上可能暂时还挂着 CardWidgetTemplate 组件；
        /// 此时那个克隆体是**激活**的且 Card != null，用「非激活 + 未绑定 + 非克隆体」即可排除它，
        /// 不会出现「误把克隆体当模板、把一张活卡 SetActive(false)」的事故。
        /// </summary>
        public static CardWidgetTemplate FindInScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
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

                // includeInactive: true —— 模板永远是未激活的，必须能查到。
                CardWidgetTemplate[] found = roots[i].GetComponentsInChildren<CardWidgetTemplate>(true);
                for (int j = 0; j < found.Length; j++)
                {
                    if (IsTemplate(found[j]))
                    {
                        return found[j];
                    }
                }
            }

            return null;
        }

        /// <summary>「是模板而不是克隆体」的判据：非激活 + 不是克隆体 + 未绑定卡牌。</summary>
        private static bool IsTemplate(CardWidgetTemplate candidate)
        {
            if (candidate == null || candidate.gameObject == null)
            {
                return false;
            }
            if (candidate.gameObject.activeSelf)
            {
                return false;   // 克隆体是激活的；模板必须非激活
            }
            if (candidate.GetComponent<EntityInstance>() != null)
            {
                return false;   // 带 EntityInstance 的一定是克隆体
            }

            CardWidget widget = candidate.GetComponent<CardWidget>();
            return widget == null || widget.Card == null;
        }

        /// <summary>parent 为 null 时的兜底容器（把模板放在场景根下的一个专用空物体里，避免污染场景根）。</summary>
        private static Transform EnsureFallbackRoot()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded)
            {
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] != null && roots[i].name == FallbackRootName)
                    {
                        return roots[i].transform;
                    }
                }
            }

            GameObject go = new GameObject(FallbackRootName, typeof(RectTransform));
            return go.transform;
        }

        // ---------------------------------------------------------------- 克隆

        /// <summary>
        /// 从本模板克隆一张**独立**的可拖动卡牌。
        /// 返回值保证：不在模板层级之下、已激活、带 EntityInstance、Card == 传入的 card、
        /// Owner == 传入的 owner、卡面已按该卡刷新。
        /// </summary>
        public CardWidget Clone(Transform parent, CardBattleModule owner, CardInstance card)
        {
            // 先登记「这次要克隆成哪张卡、转发给谁」，OnAfterClone 会在克隆体上取走并绑定。
            SetPendingBinding(owner, card);

            // 交给基类做标准克隆：深拷贝 → SetParent(parent,false) → SetActive(true)
            // → 摘掉 EntityTemplate 标记 → 补 EntityInstance（唯一 instanceId）→ OnAfterClone。
            GameObject clone = CreateRuntimeInstance(parent, card != null ? "Card_" + card.Kind : "Card");

            // 克隆根节点带上与模板一致的锚点与卡面尺寸（初始矩形的权威来源是模板）。
            RectTransform rect = clone.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = clone.AddComponent<RectTransform>();
            }
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = CardWidget.CardSize;

            CardWidget widget = clone.GetComponent<CardWidget>();
            if (widget == null)
            {
                // 理论上不可能（模板上就挂着 CardWidget，深拷贝一定带上）；兜底补齐并现场建卡面，
                // 宁可多花一点时间也不要返回一个没有卡面的空对象。
                widget = clone.AddComponent<CardWidget>();
                widget.BuildCardFace();
            }

            CloneCount++;
            return widget;
        }

        /// <summary>
        /// 克隆完成后的模板善后：把模板参数写入克隆体自己的组件。
        /// 这里不做任何「共享引用」的动作 —— 卡面文字、卡牌实例、宿主都写在克隆体自己身上。
        /// </summary>
        protected override void OnAfterClone(GameObject clone)
        {
            if (clone == null)
            {
                return;
            }

            // 克隆体自带一份 CardWidget（深拷贝而来），它与模板的那份是两个独立实例。
            CardWidget widget = clone.GetComponent<CardWidget>();
            if (widget != null)
            {
                // 关键点：克隆体不能继承模板的「未绑定」状态。这里用模板侧的待绑定信息
                // （由 Clone 在克隆前登记）把它接到自己的卡牌实例上。
                PendingBinding pending = TakePendingBinding();
                widget.InitializeFromTemplate(pending.Owner, pending.Card, templateId);
                return;
            }

            Debug.LogWarning("[CardGame][UI] 卡牌模板 " + templateId +
                             " 的克隆体缺少 CardWidget，卡面无法绑定（克隆体可能被外部裁剪过）。");
        }

        // ---------------------------------------------------------------- 待绑定信息

        // CreateRuntimeInstance 的克隆语义由基类固定（OnAfterClone 不带业务参数），
        // 所以「这次要克隆成哪张卡」在**调用 CreateRuntimeInstance 之前**登记，克隆后由 OnAfterClone 取走。
        //
        // 为什么用 static 而不是实例字段：模板是 MonoBehaviour，Unity 会把它的私有字段当序列化数据，
        // Instantiate 时**一并拷到克隆体**上 —— 实例字段会被复制成「克隆体也持有一份待绑定信息」，
        // 语义上就变成了共享状态。static 字段不参与序列化，也不会被拷贝，反而更严格。
        // 线程/重入前提：建卡只在主线程发生，且 Clone → Instantiate → OnAfterClone 是同一个
        // 调用栈同步跑完，取走（Take）后立刻清空，不存在跨次克隆的残留或串号。
        private static PendingBinding s_pending;

        private static PendingBinding TakePendingBinding()
        {
            PendingBinding pending = s_pending;
            s_pending = null;
            return pending ?? new PendingBinding();
        }

        /// <summary>仅供 <see cref="Clone"/> 使用：登记本次克隆要绑定的卡牌与宿主。</summary>
        private static void SetPendingBinding(CardBattleModule owner, CardInstance card)
        {
            s_pending = new PendingBinding();
            s_pending.Owner = owner;
            s_pending.Card = card;
        }

        private sealed class PendingBinding
        {
            public CardBattleModule Owner;
            public CardInstance Card;
        }
    }
}
