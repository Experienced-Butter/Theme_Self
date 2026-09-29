using UnityEngine;

namespace CardGame.Demo
{
    /// <summary>
    /// 卡牌展示用的纯代码视觉件。
    ///
    /// 设计约束（来自需求）：不使用任何美术资源 —— 卡面一律用 Unity 内置图元
    /// （Cube）充当「默认模型」，卡牌数据用 TextMesh 直接绘制在卡面之上。
    ///
    /// 本类只提供「怎么画」，不包含任何规则数值 —— 数值一律来自 Core.CardDefaults。
    /// </summary>
    public static class CardDeckVisuals
    {
        /// <summary>中文字体候选：优先系统字体（内置 Arial 不含中文字形，会把汉字显示成方块）。</summary>
        private static readonly string[] FontCandidates =
        {
            "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun", "宋体", "Arial"
        };

        private static Font s_cachedFont;

        /// <summary>
        /// 解析一个能显示中文的字体。先用系统字体，失败再退回 Unity 内置字体，
        /// 最后返回 null 时由调用方降级（文字可能不显示，但不会抛异常中断展示）。
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
                Debug.LogWarning("[CardGame][Demo] 未找到可用字体：卡面文字可能不显示，但展示结构仍然生成。");
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

        /// <summary>
        /// 创建纯色材质。项目是 URP，优先用 URP/Lit，并兼容内置管线与 Sprites/Default，
        /// 避免因渲染管线不同而出现洋红色（材质丢失）卡面。
        /// </summary>
        public static Material CreateSolidMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            Material material = new Material(shader);
            material.name = "CardDemoMat_" + ColorUtility.ToHtmlStringRGB(color);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.1f);
            }
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.1f);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }
            return material;
        }

        /// <summary>
        /// 创建一个立方体图元（「默认模型」）。会去掉自带的 Collider —— 展示用不到物理，
        /// 留着反而会在场景里产生无意义的碰撞体。
        /// </summary>
        public static GameObject CreateBox(string name, Transform parent, Vector3 localPosition,
                                           Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyObject(collider);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return go;
        }

        /// <summary>
        /// 创建世界空间文字。注意两点：
        /// 1) TextMesh 的正面朝自身 +Z，而卡片正面朝向相机（-Z），所以整体绕 Y 转 180°；
        /// 2) 必须把 MeshRenderer 的材质设成 font.material，否则文字没有字形贴图、什么也看不到。
        /// </summary>
        public static TextMesh CreateWorldText(string name, Transform parent, Vector3 localPosition,
                                               float characterSize, int fontSize, Color color, Font font)
        {
            // 默认朝向 180°：文字正面朝 -Z。
            // 这个默认值必须与卡面文字、页眉、演示台标签保持一致，否则同一画面里会出现
            // 「一半文字正读、一半镜像」的情况（曾经踩过：卡面显式传 180，而这里默认是 0）。
            return CreateWorldText(name, parent, localPosition, characterSize, fontSize, color, font, 180f);
        }

        /// <summary>
        /// 同上，但可指定绕 Y 的朝向：180° = 正面朝 -Z（当前相机取景方向，正读）；0° = 正面朝 +Z。
        /// </summary>
        public static TextMesh CreateWorldText(string name, Transform parent, Vector3 localPosition,
                                               float characterSize, int fontSize, Color color, Font font,
                                               float yawDegrees)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            TextMesh textMesh = go.AddComponent<TextMesh>();
            textMesh.font = font;
            textMesh.fontSize = fontSize;
            textMesh.characterSize = characterSize;
            textMesh.anchor = TextAnchor.UpperCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = color;
            textMesh.richText = false;
            textMesh.lineSpacing = 1.0f;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (font != null)
                {
                    renderer.sharedMaterial = font.material;
                }
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return textMesh;
        }

        /// <summary>同时兼容编辑模式（DestroyImmediate）与运行模式（Destroy）的销毁。</summary>
        public static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>清空一个父节点下的全部子对象（重复展示时避免叠加）。</summary>
        public static void ClearChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                DestroyObject(parent.GetChild(i).gameObject);
            }
        }
    }
}
