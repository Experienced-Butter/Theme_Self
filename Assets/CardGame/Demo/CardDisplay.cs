using CardGame.Core;
using UnityEngine;

namespace CardGame.Demo
{
    /// <summary>
    /// 单张卡牌的展示件：一块图元卡面 + 直接绘制在卡面上的卡牌数据。
    ///
    /// 卡面上的每一个数字都来自 <see cref="CardDefinitionData"/>（由 <see cref="CardDefaults"/> 产出），
    /// 本类不含任何硬编码规则数值 —— 改规则只需改 CardDefaults，展示会自动跟着变。
    /// </summary>
    public class CardDisplay : MonoBehaviour
    {
        /// <summary>
        /// 卡面尺寸（世界单位）。尺寸是按「正文看得清」反推的：
        /// 默认相机在 z=-10、FOV 60，卡墙在 z=3（距离 13，可见高度约 15 世界单位）；
        /// 每张卡正文最多 8 行，行高 ≈ characterSize × 8.24，
        /// 取 characterSize=0.031 → 行高 0.255 → 约占屏高 1.7%，1080p 下约 18px，可读。
        /// </summary>
        public const float CardWidth = 2.2f;
        public const float CardHeight = 3.15f;
        public const float CardDepth = 0.08f;

        /// <summary>卡面文字颜色。</summary>
        private static readonly Color TextColor = new Color(0.96f, 0.97f, 1f, 1f);

        private CardDefinitionData _definition;
        private Transform _visualRoot;

        /// <summary>
        /// 卡面文字的朝向（绕 Y 轴角度）。默认 180°：文字正面朝 -Z，配合「相机绕 Y 轴 180°」
        /// 的取景方式才是正读的。
        /// 这一项做成字段就是为了能直接在 Inspector 里翻转 —— 如果发现字是反的，
        /// 把它改成 0 或 180 并重新执行一次「展示全部卡牌」即可，不需要改代码。
        /// </summary>
        public float textYawDegrees = 180f;

        /// <summary>当前展示的卡牌定义。</summary>
        public CardDefinitionData Definition
        {
            get { return _definition; }
        }

        /// <summary>用给定定义重建卡面；可重复调用（会先清空上一次的卡面）。</summary>
        public void Apply(CardDefinitionData definition)
        {
            _definition = definition;

            if (_visualRoot == null)
            {
                GameObject root = new GameObject("Visual");
                root.transform.SetParent(transform, false);
                _visualRoot = root.transform;
            }
            CardDeckVisuals.ClearChildren(_visualRoot);

            if (definition == null)
            {
                Debug.LogWarning("[CardGame][Demo] CardDisplay.Apply 收到 null 定义，跳过卡面构建。");
                return;
            }

            Font font = CardDeckVisuals.ResolveFont();
            Color tierColor = TierColor(definition.tier);

            // 1) 卡面主体（默认模型：Cube 图元）
            CardDeckVisuals.CreateBox("CardBody", _visualRoot, Vector3.zero,
                new Vector3(CardWidth, CardHeight, CardDepth),
                CardDeckVisuals.CreateSolidMaterial(tierColor));

            // 2) 顶部色条：用层级颜色区分基础卡 / 合成卡 / 大招 / 复制器
            CardDeckVisuals.CreateBox("TierBar", _visualRoot, new Vector3(0f, CardHeight * 0.5f - 0.15f, -0.02f),
                new Vector3(CardWidth - 0.2f, 0.16f, 0.04f),
                CardDeckVisuals.CreateSolidMaterial(TierAccentColor(definition.tier)));

            // 3) 标题与正文：文字贴在它「正面所朝的那一侧」，只保留一份，避免正反重影互相干扰。
            //    朝向由 textYawDegrees 决定；文字所在的那一面跟着朝向走。
            float yaw = textYawDegrees;
            bool facesPositiveZ = Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) < 90f;
            float textZ = (facesPositiveZ ? 1f : -1f) * (CardDepth * 0.5f + 0.02f);

            CardDeckVisuals.CreateWorldText("TitleText", _visualRoot,
                new Vector3(0f, CardHeight * 0.5f - 0.30f, textZ),
                0.038f, 64, TextColor, font, yaw).text = BuildTitleText(definition);

            CardDeckVisuals.CreateWorldText("BodyText", _visualRoot,
                new Vector3(0f, CardHeight * 0.5f - 1.06f, textZ),
                0.031f, 64, TextColor, font, yaw).text = BuildBodyText(definition);

            gameObject.name = "Card_" + definition.kind;
        }

        /// <summary>标题：第一行种类、第二行显示名。</summary>
        public static string BuildTitleText(CardDefinitionData definition)
        {
            if (definition == null)
            {
                return string.Empty;
            }
            string name = string.IsNullOrEmpty(definition.displayName) ? "(未命名)" : definition.displayName;
            return definition.kind + "\n" + name;
        }

        /// <summary>
        /// 正文：把 CardDefinitionData 里所有「有值」的项逐行列出，
        /// 无该项效果一律不显示（0 / false 即无效果）。
        /// </summary>
        public static string BuildBodyText(CardDefinitionData d)
        {
            if (d == null)
            {
                return string.Empty;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
            sb.Append(TierLabel(d.tier)).Append('\n');
            sb.Append("----------").Append('\n');

            if (d.damage > 0)
            {
                sb.Append("伤害 ").Append(d.damage).Append('\n');
            }
            if (d.healInstant > 0)
            {
                sb.Append("治疗 ").Append(d.healInstant).Append('\n');
            }
            if (d.regenPerTurn > 0)
            {
                sb.Append("回复 ").Append(d.regenPerTurn).Append("×").Append(d.regenTurns).Append("回合").Append('\n');
            }
            if (d.hitDownPercent > 0f)
            {
                sb.Append("降命中 ").Append(Percent(d.hitDownPercent)).Append(" ").Append(d.hitDownTurns).Append("回合").Append('\n');
            }
            if (d.defenseDownPercent > 0f)
            {
                sb.Append("降防御 ").Append(Percent(d.defenseDownPercent)).Append(" ").Append(d.defenseDownTurns).Append("回合").Append('\n');
            }
            if (d.defenseDownFlat > 0)
            {
                sb.Append("降防御 ").Append(d.defenseDownFlat).Append(" ").Append(d.defenseDownTurns).Append("回合").Append('\n');
            }
            if (d.drawCount > 0)
            {
                sb.Append("抽牌 ").Append(d.drawCount).Append('\n');
            }
            if (d.grantImmunity)
            {
                sb.Append("免疫下次伤害").Append('\n');
            }
            if (d.grantInitiative)
            {
                sb.Append("下回合先手").Append('\n');
            }
            if (d.extraPlaysNextTurn > 0)
            {
                sb.Append("下回合 +").Append(d.extraPlaysNextTurn).Append(" 出手").Append('\n');
            }
            if (d.handBuffBasic > 1f)
            {
                sb.Append("强化A ×").Append(d.handBuffBasic.ToString("0.00")).Append('\n');
            }
            if (d.handBuffFused > 1f)
            {
                sb.Append("强化AA ×").Append(d.handBuffFused.ToString("0.00")).Append('\n');
            }
            if (d.handBuffTurns > 0 && (d.handBuffBasic > 1f || d.handBuffFused > 1f))
            {
                sb.Append("  （").Append(d.handBuffTurns).Append("回合）").Append('\n');
            }
            if (d.isTemporary)
            {
                sb.Append("临时卡").Append('\n');
            }

            sb.Append("----------").Append('\n');
            if (d.fusionRecipe != null && d.fusionRecipe.Length == 2)
            {
                sb.Append("配方 ").Append(d.fusionRecipe[0]).Append("+").Append(d.fusionRecipe[1]);
            }
            else
            {
                sb.Append("不可合成");
            }
            return sb.ToString();
        }

        /// <summary>层级中文标签。</summary>
        public static string TierLabel(CardTier tier)
        {
            switch (tier)
            {
                case CardTier.Basic: return "基础卡";
                case CardTier.Fused: return "合成卡";
                case CardTier.Ultimate: return "大招卡";
                case CardTier.Duplicator: return "复制器";
                default: return tier.ToString();
            }
        }

        /// <summary>层级底色：一眼区分基础 / 合成 / 大招 / 复制器。</summary>
        public static Color TierColor(CardTier tier)
        {
            switch (tier)
            {
                case CardTier.Basic: return new Color(0.16f, 0.22f, 0.34f, 1f);
                case CardTier.Fused: return new Color(0.42f, 0.30f, 0.10f, 1f);
                case CardTier.Ultimate: return new Color(0.34f, 0.12f, 0.40f, 1f);
                case CardTier.Duplicator: return new Color(0.08f, 0.32f, 0.28f, 1f);
                default: return new Color(0.25f, 0.25f, 0.25f, 1f);
            }
        }

        /// <summary>顶部色条颜色（底色提亮版）。</summary>
        public static Color TierAccentColor(CardTier tier)
        {
            Color c = TierColor(tier);
            return new Color(Mathf.Min(1f, c.r + 0.45f), Mathf.Min(1f, c.g + 0.45f), Mathf.Min(1f, c.b + 0.45f), 1f);
        }

        /// <summary>0.1 → "10%"。</summary>
        private static string Percent(float value)
        {
            return (value * 100f).ToString("0.#") + "%";
        }
    }
}
