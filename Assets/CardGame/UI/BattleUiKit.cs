using UnityEngine;
using UnityEngine.UI;

namespace CardGame.UI
{
    /// <summary>
    /// 卡牌战斗 UI 的公共构件。
    ///
    /// 目标形态：2D、1080p 电脑端 —— 全部用 uGUI（Canvas + RectTransform）搭建，
    /// 不依赖任何 3D 世界对象，也不需要美术资源（图形一律用程序生成的纯色/圆形 Sprite）。
    ///
    /// 图层顺序（Canvas 子节点由上到下，靠后的渲染在上层）：
    ///   ① Layer_Background  游戏背景（固定最下层）
    ///   ② Layer_Entities    玩家（左）与敌人（右）
    ///   ③ Layer_Cards       卡牌（下侧）
    ///   ④ Layer_Mask        35% 不透明度的黑色遮罩
    ///   ⑤ Layer_Above       拖动中的卡牌、以及「将要交互的目标」会临时提到这一层（在遮罩之上）
    ///   ⑥ Layer_Overlay     结算横幅与按钮
    /// </summary>
    public static class BattleUiKit
    {
        /// <summary>中文字体候选：优先系统字体（Unity 内置 Arial/LegacyRuntime 不含中文字形，会把汉字显示成方块）。</summary>
        private static readonly string[] FontCandidates =
        {
            "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun", "宋体", "Arial"
        };

        private static Font s_cachedFont;
        private static Sprite s_white;
        private static Sprite s_circle;

        /// <summary>整套 UI 使用的中文可读字体。</summary>
        public static Font Font
        {
            get { return ResolveFont(); }
        }

        /// <summary>
        /// 解析一个能显示中文的字体。先取系统字体，失败再退回 Unity 内置字体；
        /// 都失败时返回 null，由调用方降级（文字可能不显示，但不会抛异常中断整个画面）。
        /// </summary>
        public static Font ResolveFont()
        {
            if (s_cachedFont != null)
            {
                return s_cachedFont;
            }

            try
            {
                s_cachedFont = Font.CreateDynamicFontFromOSFont(FontCandidates, 64);
            }
            catch (System.Exception)
            {
                s_cachedFont = null;
            }

            if (s_cachedFont == null)
            {
                s_cachedFont = TryBuiltinFont("LegacyRuntime.ttf");
            }
            if (s_cachedFont == null)
            {
                s_cachedFont = TryBuiltinFont("Arial.ttf");
            }
            if (s_cachedFont == null)
            {
                Debug.LogWarning("[CardGame][UI] 未找到可用字体：卡面文字可能不显示，但界面结构仍然生成。");
            }
            return s_cachedFont;
        }

        private static Font TryBuiltinFont(string name)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(name);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>1×1 白色 Sprite：配合 Image.color 即可绘制任意纯色矩形。</summary>
        public static Sprite WhiteSprite
        {
            get
            {
                if (s_white == null)
                {
                    s_white = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f),
                        new Vector2(0.5f, 0.5f), 100f);
                    s_white.name = "BattleUi_White";
                }
                return s_white;
            }
        }

        /// <summary>程序生成的圆形 Sprite（用作玩家/敌人的占位形象，不需要美术资源）。</summary>
        public static Sprite CircleSprite
        {
            get
            {
                if (s_circle == null)
                {
                    const int size = 64;
                    Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    tex.name = "BattleUi_Circle";
                    float r = size * 0.5f - 1f;
                    Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
                    Color32[] pixels = new Color32[size * size];
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                            // 边缘 1.5px 做平滑过渡，避免锯齿
                            float a = Mathf.Clamp01((r - d) / 1.5f);
                            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                        }
                    }
                    tex.SetPixels32(pixels);
                    tex.Apply();
                    tex.wrapMode = TextureWrapMode.Clamp;
                    s_circle = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
                    s_circle.name = "BattleUi_Circle";
                }
                return s_circle;
            }
        }

        /// <summary>创建一个铺满父级的容器（作为图层使用）。</summary>
        public static RectTransform CreateLayer(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);
            return rect;
        }

        /// <summary>让 RectTransform 铺满父级。</summary>
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>创建一个纯色 Image（以父级中心为锚点）。</summary>
        public static Image CreateImage(string name, Transform parent, Color color,
                                        Vector2 size, Vector2 anchoredPosition)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            Image image = go.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>创建一个居中文本。</summary>
        public static Text CreateText(string name, Transform parent, string content, int fontSize,
                                      Color color, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);

            Text text = go.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.text = content;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>在指定父级下挂一个铺满的文本（带自己的一块区域）。</summary>
        public static Text CreateTextBlock(string name, Transform parent, string content, int fontSize,
                                           Color color, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);

            Text text = go.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.text = content;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>创建一个按钮（含文字），返回按钮组件。</summary>
        public static Button CreateButton(string name, Transform parent, string label, int fontSize,
                                          Vector2 size, Vector2 anchoredPosition, Color background)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            Image image = go.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = background;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            button.colors = colors;

            Text text = CreateText(name + "_Label", go.transform, label, fontSize, Color.white, TextAnchor.MiddleCenter);
            text.raycastTarget = false;

            return button;
        }

        /// <summary>
        /// 确保场景里有 EventSystem，并挂上与当前输入系统匹配的输入模块。
        /// 本项目 Active Input Handling = 1（仅新输入系统），旧版 StandaloneInputModule 不工作，
        /// 必须用 InputSystemUIInputModule；这里按类型名反射添加，避免编译期依赖包程序集。
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                return;
            }
            GameObject go = new GameObject("BattleUi_EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();

            System.Type moduleType =
                System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType == null)
            {
                moduleType = System.Type.GetType("UnityEngine.EventSystems.StandaloneInputModule, UnityEngine.UI");
            }
            if (moduleType != null)
            {
                go.AddComponent(moduleType);
            }
            else
            {
                Debug.LogWarning("[CardGame][UI] 未找到可用的 UI 输入模块，鼠标交互可能不生效。");
            }
        }

        /// <summary>两个 RectTransform 的屏幕矩形是否重叠（用于「投影」判定）。</summary>
        public static bool RectsOverlap(RectTransform a, RectTransform b)
        {
            if (a == null || b == null || !a.gameObject.activeInHierarchy || !b.gameObject.activeInHierarchy)
            {
                return false;
            }
            Rect ra = ScreenRect(a);
            Rect rb = ScreenRect(b);
            return ra.Overlaps(rb);
        }

        /// <summary>取 RectTransform 在屏幕坐标系下的矩形。</summary>
        public static Rect ScreenRect(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float minX = corners[0].x;
            float minY = corners[0].y;
            float maxX = corners[0].x;
            float maxY = corners[0].y;
            for (int i = 1; i < 4; i++)
            {
                if (corners[i].x < minX) minX = corners[i].x;
                if (corners[i].y < minY) minY = corners[i].y;
                if (corners[i].x > maxX) maxX = corners[i].x;
                if (corners[i].y > maxY) maxY = corners[i].y;
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>重叠面积（用于「重叠最大者优先」的判定）。</summary>
        public static float OverlapArea(RectTransform a, RectTransform b)
        {
            Rect ra = ScreenRect(a);
            Rect rb = ScreenRect(b);
            float w = Mathf.Min(ra.xMax, rb.xMax) - Mathf.Max(ra.xMin, rb.xMin);
            float h = Mathf.Min(ra.yMax, rb.yMax) - Mathf.Max(ra.yMin, rb.yMin);
            if (w <= 0f || h <= 0f)
            {
                return 0f;
            }
            return w * h;
        }
    }
}
