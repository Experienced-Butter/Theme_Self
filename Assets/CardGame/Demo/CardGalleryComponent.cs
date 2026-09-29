using CardGame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Demo
{
    /// <summary>
    /// 卡牌展示流程：把全部 11 种卡牌排成一面「卡墙」，每张卡用 Unity 默认图元当卡面、
    /// 数据直接绘制在卡面上。演示用途，不参与战斗逻辑，也不依赖任何美术资源。
    ///
    /// 三种启动方式（都很直观）：
    ///   1. 编辑器菜单 `CardGame/展示全部卡牌` —— 编辑态一键生成，无需按 Play 就能看到；
    ///   2. 直接按 Play —— autoShowOnStart 为 true 时自动展示；
    ///   3. 运行时的屏幕按钮（左下角「展示 / 隐藏全部卡牌」）—— 由本组件自动创建。
    ///
    /// 也可以在本组件的 Inspector 右键菜单里选「展示全部卡牌 / 隐藏卡牌展示 / 切换卡牌展示」。
    /// </summary>
    public class CardGalleryComponent : MonoBehaviour
    {
        [Header("启动方式")]
        [Tooltip("运行（Play）时自动展示卡牌。")]
        public bool autoShowOnStart = true;

        [Tooltip("是否创建屏幕上的「展示 / 隐藏」按钮。")]
        public bool createOnScreenButton = true;

        [Header("布局")]
        [Tooltip("卡墙中心位置（世界坐标）。默认相机在 (0,1,-10) 朝 +Z，所以放在 z=3 正对相机。")]
        public Vector3 gridOrigin = new Vector3(0f, 0.4f, 3f);

        [Tooltip("每行张数。")]
        public int columns = 4;

        [Tooltip("卡牌水平间距。")]
        public float gapX = 0.3f;

        [Tooltip("卡牌垂直间距。")]
        public float gapY = 0.35f;

        [Header("卡面文字")]
        [Tooltip("卡面文字朝向（绕 Y 轴角度）。180 = 贴在朝 -Z 的那一面，配合本组件的相机取景是正读的；" +
                 "若发现字是反的，把这里改成 0 再点一次「展示全部卡牌」即可换面，不需要改代码。")]
        public float cardTextYaw = 180f;

        [Header("相机取景")]
        [Tooltip("展示时自动把相机移到正对卡墙、且刚好框住整面墙的位置。")]
        public bool frameCameraOnShow = true;

        [Tooltip("要调整的相机；留空则自动使用 Camera.main（场景里 tag 为 MainCamera 的那个）。")]
        public Camera targetCamera;

        [Tooltip("取景时在计算出的距离上再乘的安全系数（>1 更远、留白更多）。")]
        public float cameraDistancePadding = 1.06f;

        // 原始相机位姿：**序列化**保存，这样域重载（重新编译）之后「恢复相机」仍然可用。
        [SerializeField] private bool hasSavedCameraPose;
        [SerializeField] private Vector3 savedCameraPosition;
        [SerializeField] private Vector3 savedCameraEuler;
        [SerializeField] private float savedCameraFov = 60f;

        // 最近一次构建出的内容尺寸（用于相机取景）
        private float _contentWidth;
        private float _contentHeight;

        /// <summary>展示顺序：按规则文档第二节／第三节的阅读顺序排列。</summary>
        private static readonly CardKind[] DisplayOrder =
        {
            CardKind.A, CardKind.B, CardKind.C,
            CardKind.AA, CardKind.BB, CardKind.CC,
            CardKind.AB, CardKind.AC, CardKind.BC,
            CardKind.Ultimate, CardKind.Duplicator
        };

        private GameObject _gridRoot;
        private GameObject _canvasRoot;

        /// <summary>最近一次构建出的卡牌数量（供自检／脚本核对）。</summary>
        public int LastBuiltCardCount { get; private set; }

        /// <summary>当前是否处于展示状态。</summary>
        public bool IsVisible
        {
            get { return _gridRoot != null && _gridRoot.activeSelf; }
        }

        private void Start()
        {
            if (autoShowOnStart)
            {
                ShowGallery();
            }
        }

        [ContextMenu("展示全部卡牌")]
        public void ShowGallery()
        {
            BuildGridIfNeeded();
            if (_gridRoot != null)
            {
                _gridRoot.SetActive(true);
            }
            SetButtonLabel("隐藏全部卡牌");
            if (frameCameraOnShow)
            {
                FrameCamera();
            }
        }

        [ContextMenu("隐藏卡牌展示")]
        public void HideGallery()
        {
            if (_gridRoot != null)
            {
                _gridRoot.SetActive(false);
            }
            SetButtonLabel("展示全部卡牌");
            RestoreCamera();
        }

        [ContextMenu("切换卡牌展示")]
        public void ToggleGallery()
        {
            if (IsVisible)
            {
                HideGallery();
            }
            else
            {
                ShowGallery();
            }
        }

        /// <summary>清除已生成的卡墙（编辑器菜单「清除卡牌展示」会调用）。</summary>
        public void ClearGallery()
        {
            if (_gridRoot == null)
            {
                _gridRoot = FindExistingChild("CardGrid");
            }
            if (_gridRoot != null)
            {
                CardDeckVisuals.DestroyObject(_gridRoot);
                _gridRoot = null;
            }
            LastBuiltCardCount = 0;
        }

        /// <summary>
        /// 在自身子节点里按名字找已存在的对象。
        /// 必要性：_gridRoot / _canvasRoot 是私有字段、不参与序列化，**重新编译（域重载）后会变回 null**，
        /// 而场景里上一轮生成的对象还在。若不做这层查找，编辑态再点一次「展示」就会生成第二份卡墙。
        /// </summary>
        private GameObject FindExistingChild(string childName)
        {
            Transform found = transform.Find(childName);
            return found != null ? found.gameObject : null;
        }

        /// <summary>统计已生成卡墙里的卡牌张数（供复用路径如实报告数量）。</summary>
        private int CountCardChildren()
        {
            if (_gridRoot == null)
            {
                return 0;
            }
            int count = 0;
            Transform root = _gridRoot.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i).name.StartsWith("Card_"))
                {
                    count++;
                }
            }
            return count;
        }

        // ------------------------------------------------------------------
        // 相机取景
        // ------------------------------------------------------------------

        /// <summary>
        /// 把相机移到「正对卡墙、并刚好框住整面墙（含上方标题行）」的位置。
        /// 距离按垂直与水平两个方向各算一次取较大者，因此宽屏／窄屏都不会裁掉卡牌。
        /// 首次调用会先记录相机原始位姿，供 <see cref="RestoreCamera"/> 还原。
        /// </summary>
        public void FrameCamera()
        {
            Camera cam = ResolveCamera();
            if (cam == null)
            {
                Debug.LogWarning("[CardGame][Demo] 场景里找不到相机，已跳过自动取景（可手动把相机放到卡墙正前方）。");
                return;
            }

            if (!hasSavedCameraPose)
            {
                savedCameraPosition = cam.transform.position;
                savedCameraEuler = cam.transform.eulerAngles;
                savedCameraFov = cam.fieldOfView;
                hasSavedCameraPose = true;
            }

            // 若卡墙已存在但还没记录过尺寸（例如域重载后直接点菜单），用常量反推一次
            EnsureContentSize();

            float halfWidth = _contentWidth * 0.5f + 0.8f;
            // 上方要容纳标题块（顶部在 HeaderTopOffset 处，文字向下生长）再留 0.5 边距；
            // 下方只留一点边距。这样标题永远不会被裁掉。
            float topExtent = _contentHeight * 0.5f + HeaderTopOffset + 0.5f;
            float bottomExtent = _contentHeight * 0.5f + 0.2f;
            float halfHeight = (topExtent + bottomExtent) * 0.5f;
            float centerOffsetY = (topExtent - bottomExtent) * 0.5f;

            float tanHalfFov = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float aspect = cam.aspect > 0.01f ? cam.aspect : (16f / 9f);

            float distanceForHeight = halfHeight / tanHalfFov;
            float distanceForWidth = halfWidth / (tanHalfFov * aspect);
            float distance = Mathf.Max(distanceForHeight, distanceForWidth) * Mathf.Max(1f, cameraDistancePadding);

            Vector3 gridWorldCenter = transform.TransformPoint(gridOrigin);
            // 【按指定】相机绕竖直轴(Y)旋转 180°：朝向世界 -Z，从卡墙的 +Z 一侧回看。
            // 因此相机要放在卡墙 z 坐标的「外侧」(+distance)，而不是原来的 -distance。
            // 卡面正反两面都画了文字，从这一侧看到的是 yaw=0 的背面文字，阅读方向正确。
            Vector3 cameraPosition = new Vector3(
                gridWorldCenter.x,
                gridWorldCenter.y + centerOffsetY,
                gridWorldCenter.z + distance);

            cam.transform.position = cameraPosition;
            cam.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Debug.Log("[CardGame][Demo] 相机已取景（绕 Y 轴 180°，自 +Z 侧回看）：位置 (" +
                      cameraPosition.x.ToString("0.00") + ", " + cameraPosition.y.ToString("0.00") + ", " + cameraPosition.z.ToString("0.00") +
                      ")，距离 " + distance.ToString("0.00") + "，FOV " + cam.fieldOfView.ToString("0.#"));
        }

        /// <summary>把相机还原到首次展示之前的位姿。</summary>
        public void RestoreCamera()
        {
            if (!hasSavedCameraPose)
            {
                return;
            }
            Camera cam = ResolveCamera();
            if (cam != null)
            {
                cam.transform.position = savedCameraPosition;
                cam.transform.rotation = Quaternion.Euler(savedCameraEuler);
                cam.fieldOfView = savedCameraFov;
                Debug.Log("[CardGame][Demo] 相机已还原到展示前的位置 (" +
                          savedCameraPosition.x.ToString("0.00") + ", " + savedCameraPosition.y.ToString("0.00") + ", " + savedCameraPosition.z.ToString("0.00") + ")。");
            }
            hasSavedCameraPose = false;
        }

        private Camera ResolveCamera()
        {
            if (targetCamera != null)
            {
                return targetCamera;
            }
            return Camera.main;
        }

        // ------------------------------------------------------------------
        // 卡墙构建
        // ------------------------------------------------------------------

        private void BuildGridIfNeeded()
        {
            // 域重载后私有字段会丢，但场景里的对象还在 —— 先按名字认领，避免重复生成。
            if (_gridRoot == null)
            {
                _gridRoot = FindExistingChild("CardGrid");
            }
            if (_gridRoot != null)
            {
                // 复用已有卡墙（域重载后私有字段会丢失）：顺手把张数查回来，
                // 否则日志会误报「已生成 0 张」，看起来像生成失败。
                if (LastBuiltCardCount <= 0)
                {
                    LastBuiltCardCount = CountCardChildren();
                }
                return;
            }

            _gridRoot = new GameObject("CardGrid");
            _gridRoot.transform.SetParent(transform, false);
            _gridRoot.transform.localPosition = gridOrigin;

            BuildHeader();

            float cardW = CardDisplay.CardWidth;
            float cardH = CardDisplay.CardHeight;
            int total = DisplayOrder.Length;
            int cols = Mathf.Max(1, columns);
            int rows = Mathf.CeilToInt((float)total / cols);

            float totalWidth = cols * cardW + (cols - 1) * gapX;
            float totalHeight = rows * cardH + (rows - 1) * gapY;
            _contentWidth = totalWidth;
            _contentHeight = totalHeight;
            float startX = -(totalWidth - cardW) * 0.5f;
            float startY = (totalHeight - cardH) * 0.5f;

            int built = 0;
            for (int i = 0; i < total; i++)
            {
                int row = i / cols;
                int col = i % cols;

                // 每行的实际张数（最后一行可能不满），让末行居中而不是左对齐
                int itemsInRow = Mathf.Min(cols, total - row * cols);
                float rowWidth = itemsInRow * cardW + (itemsInRow - 1) * gapX;
                float rowStartX = -(rowWidth - cardW) * 0.5f;

                Vector3 localPos = new Vector3(
                    rowStartX + col * (cardW + gapX),
                    startY - row * (cardH + gapY),
                    0f);

                GameObject cardGo = new GameObject("Card_" + DisplayOrder[i]);
                cardGo.transform.SetParent(_gridRoot.transform, false);
                cardGo.transform.localPosition = localPos;
                cardGo.transform.localRotation = Quaternion.identity;

                CardDisplay display = cardGo.AddComponent<CardDisplay>();
                display.textYawDegrees = cardTextYaw;   // 朝向必须在 Apply 之前设好（Apply 会立刻生成文字）
                // 数据源头唯一：CardDefaults。展示层不硬编码任何数值。
                display.Apply(CardDefaults.Create(DisplayOrder[i]));
                built++;
            }

            LastBuiltCardCount = built;
            Debug.Log("[CardGame][Demo] 卡牌展示已构建：" + built + " 张（数据源 CardDefaults，卡面为 Unity 默认图元）。");

            if (createOnScreenButton)
            {
                BuildOnScreenButton();
            }
        }

        /// <summary>标题顶部相对卡墙顶边再往上留出的距离。</summary>
        private const float HeaderTopOffset = 2.05f;

        /// <summary>
        /// TextMesh 行高经验系数：行高 ≈ characterSize × 该系数
        /// （fontSize = 64、lineSpacing = 1 时实测得出）。
        /// 标题是多行、锚点又是上对齐（文字向下生长），必须按这个高度把下一行让开，
        /// 否则会出现「启动方式」那行压在标题块里的重叠。
        /// </summary>
        private const float TextLineHeightFactor = 8.24f;

        private void BuildHeader()
        {
            Font font = CardDeckVisuals.ResolveFont();
            EnsureContentSize();

            float headerTop = _contentHeight * 0.5f + HeaderTopOffset;
            const float headerCharSize = 0.058f;
            const int headerLines = 2;
            float headerHeight = headerLines * headerCharSize * TextLineHeightFactor;
            // 提示行让到标题块下方，并留 0.15 的间隙
            float hintTop = headerTop - headerHeight - 0.15f;

            // 朝向必须与卡面文字一致（都用 cardTextYaw），否则会出现半正半反。
            CardDeckVisuals.CreateWorldText("HeaderText", _gridRoot.transform,
                new Vector3(0f, headerTop, 0f),
                headerCharSize, 64, new Color(1f, 0.95f, 0.8f, 1f), font, cardTextYaw).text =
                "卡牌一览 · 共 " + DisplayOrder.Length + " 张\n" +
                "卡面为 Unity 默认图元，数据直接来自 CardDefaults";

            CardDeckVisuals.CreateWorldText("HintText", _gridRoot.transform,
                new Vector3(0f, hintTop, 0f),
                0.034f, 64, new Color(0.8f, 0.85f, 0.95f, 1f), font, cardTextYaw).text =
                "启动方式：菜单 CardGame/展示全部卡牌  ·  或按 Play  ·  或点左下角按钮";
        }

        /// <summary>内容尺寸只在构建时算一次；相机取景也要用同一组数值，避免两处各算一套。</summary>
        private void EnsureContentSize()
        {
            if (_contentWidth > 0.01f && _contentHeight > 0.01f)
            {
                return;
            }
            int cols = Mathf.Max(1, columns);
            int rows = Mathf.CeilToInt((float)DisplayOrder.Length / cols);
            _contentWidth = cols * CardDisplay.CardWidth + (cols - 1) * gapX;
            _contentHeight = rows * CardDisplay.CardHeight + (rows - 1) * gapY;
        }

        // ------------------------------------------------------------------
        // 屏幕按钮（uGUI）
        // ------------------------------------------------------------------

        private void BuildOnScreenButton()
        {
            if (_canvasRoot == null)
            {
                _canvasRoot = FindExistingChild("CardGallery_UI");
            }
            if (_canvasRoot != null)
            {
                return;
            }

            _canvasRoot = new GameObject("CardGallery_UI", typeof(RectTransform));
            _canvasRoot.transform.SetParent(transform, false);

            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRoot.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();

            GameObject buttonGo = new GameObject("ShowCardsButton", typeof(RectTransform));
            buttonGo.transform.SetParent(_canvasRoot.transform, false);

            Image image = buttonGo.AddComponent<Image>();
            image.color = new Color(0.15f, 0.45f, 0.85f, 0.95f);

            Button button = buttonGo.AddComponent<Button>();
            button.targetGraphic = image;

            RectTransform rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(40f, 40f);
            rect.sizeDelta = new Vector2(360f, 84f);

            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(buttonGo.transform, false);
            Text label = labelGo.AddComponent<Text>();
            label.font = CardDeckVisuals.ResolveFont();
            label.fontSize = 30;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = IsVisible ? "隐藏全部卡牌" : "展示全部卡牌";

            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            button.onClick.AddListener(ToggleGallery);

            // 记录按钮上的文字组件，便于展示/隐藏时同步更新标签
            _buttonLabel = label;
        }

        private Text _buttonLabel;

        private void SetButtonLabel(string text)
        {
            if (_buttonLabel != null)
            {
                _buttonLabel.text = text;
            }
        }

        /// <summary>
        /// 确保场景里有 EventSystem，并挂上与当前输入系统匹配的输入模块。
        /// 本项目 Active Input Handling = 1（仅新输入系统），旧版 StandaloneInputModule
        /// 在新输入下不工作，必须用 InputSystemUIInputModule；这里按类型名反射添加，
        /// 这样无论项目切成旧／新／两者都能正常工作，也不会给编译期引入包依赖。
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystemGo = new GameObject("CardGallery_EventSystem");
            eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();

            System.Type moduleType =
                System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType == null)
            {
                moduleType = System.Type.GetType("UnityEngine.EventSystems.StandaloneInputModule, UnityEngine.UI");
            }

            if (moduleType != null)
            {
                eventSystemGo.AddComponent(moduleType);
            }
            else
            {
                Debug.LogWarning("[CardGame][Demo] 未找到可用的 UI 输入模块，屏幕按钮可能无法点击；" +
                                 "请改用菜单 CardGame/展示全部卡牌 或按 Play。");
            }
        }
    }
}
