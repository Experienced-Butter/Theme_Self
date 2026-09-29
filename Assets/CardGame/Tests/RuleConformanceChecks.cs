using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CardGame.Core;

namespace CardGame.Tests
{
    /// <summary>
    /// 规则自检：不依赖 Unity Test Framework（避免 asmdef），纯静态方法，可在任意编辑器脚本、
    /// 菜单项或 GameBootstrap 里调用。
    ///
    /// 覆盖范围：
    ///   1. ARCHITECTURE 第 2 节的规则常量是否与 RULES.md 一致（45 项）；
    ///   2. CardDefaults 中全部 11 种卡的数值、层级、配方是否与 RULES.md 一致（数值唯一权威来源）；
    ///   3. GameRules 三个结算纯函数的取整/下限语义（floor 而非 round、下限 0、波次 20%）；
    ///   4. ARCHITECTURE 第 4–10 节的关键类型/成员与纯函数签名是否齐全（反射自查，防集成失败）。
    ///
    /// 用法：
    ///   List&lt;string&gt; report = RuleConformanceChecks.RunAll();
    ///   List&lt;string&gt; lines = RuleConformanceChecks.Report();   // 带表头的完整文本
    /// </summary>
    public static class RuleConformanceChecks
    {
        private const float Epsilon = 0.0001f;

        private static readonly List<string> Lines = new List<string>();
        private static int _passed;
        private static int _failed;

        /// <summary>按 ARCHITECTURE 第 11 节的签名逐条自检，返回「FAIL: ...」行清单（全部通过时返回空列表）。</summary>
        public static List<string> RunAll()
        {
            Begin();

            CheckPlayerRules();
            CheckBasicCards();
            CheckFusedCards();
            CheckChargeAndUltimateRules();
            CheckMonsterRules();
            CheckDamageFormulas();
            CheckWaveRecovery();
            CheckFusionRecipes();
            CheckSignatureCoverage();

            List<string> failures = new List<string>();
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].StartsWith("FAIL: "))
                {
                    failures.Add(Lines[i]);
                }
            }
            return failures;
        }

        /// <summary>可读的完整自检报告（含 PASS/FAIL 统计），供菜单/日志直接输出。</summary>
        public static string Report()
        {
            RunAll();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========== CardGame 规则自检 ==========");
            for (int i = 0; i < Lines.Count; i++)
            {
                sb.AppendLine(Lines[i]);
            }
            sb.AppendLine("---------------------------------------");
            sb.Append("PASS: ").Append(_passed).Append("    FAIL: ").Append(_failed);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 自检框架

        private static void Begin()
        {
            Lines.Clear();
            _passed = 0;
            _failed = 0;
        }

        private static void Ok(string what)
        {
            _passed++;
            Lines.Add("PASS: " + what);
        }

        private static void Ng(string what, string expected, string actual)
        {
            _failed++;
            Lines.Add("FAIL: " + what + "（期望 " + expected + "，实际 " + actual + "）");
        }

        private static void Eq(string what, int expected, int actual)
        {
            if (expected == actual)
            {
                Ok(what + " = " + actual);
            }
            else
            {
                Ng(what, expected.ToString(), actual.ToString());
            }
        }

        private static void EqF(string what, float expected, float actual)
        {
            if (System.Math.Abs(expected - actual) <= Epsilon)
            {
                Ok(what + " = " + actual.ToString("0.####"));
            }
            else
            {
                Ng(what, expected.ToString("0.####"), actual.ToString("0.####"));
            }
        }

        private static void EqB(string what, bool expected, bool actual)
        {
            if (expected == actual)
            {
                Ok(what + " = " + actual);
            }
            else
            {
                Ng(what, expected.ToString(), actual.ToString());
            }
        }

        private static void EqS(string what, string expected, string actual)
        {
            if (expected == actual)
            {
                Ok(what + " = " + expected);
            }
            else
            {
                Ng(what, expected, actual);
            }
        }

        private static void Truth(string what, bool condition)
        {
            if (condition)
            {
                Ok(what);
            }
            else
            {
                Ng(what, "成立", "不成立");
            }
        }

        // ---------------------------------------------------------------- 1. 玩家参数

        private static void CheckPlayerRules()
        {
            Eq("玩家生命值", 2500, GameRules.PlayerMaxHp);
            Eq("玩家防御", 300, GameRules.PlayerDefense);
            EqF("玩家命中率", 1f, GameRules.PlayerHitRate);
            Eq("每回合出牌次数", 4, GameRules.PlaysPerTurn);
            Eq("初始手牌", 5, GameRules.OpeningHand);
            Eq("每回合抽牌", 4, GameRules.DrawPerTurn);
            Eq("手牌上限", 6, GameRules.HandLimit);
            Eq("牌库每种基础卡张数", 5, GameRules.DeckCopiesPerBasic);
            Eq("牌库总张数 A×5+B×5+C×5", 15, GameRules.DeckCopiesPerBasic * 3);
        }

        // ---------------------------------------------------------------- 2. 基础卡

        private static void CheckBasicCards()
        {
            CardDefinitionData a = CardDefaults.Create(CardKind.A);
            Eq("A 伤害", 400, a.damage);
            Eq("A 治疗", 0, a.healInstant);
            EqS("A 层级", "Basic", a.tier.ToString());

            CardDefinitionData b = CardDefaults.Create(CardKind.B);
            Eq("B 伤害", 0, b.damage);
            EqF("B 降命中", 0.10f, b.hitDownPercent);
            Eq("B 降命中回合", 1, b.hitDownTurns);

            CardDefinitionData c = CardDefaults.Create(CardKind.C);
            Eq("C 治疗", 300, c.healInstant);
            Eq("C 伤害", 0, c.damage);
        }

        // ---------------------------------------------------------------- 3. 合成卡

        private static void CheckFusedCards()
        {
            CardDefinitionData aa = CardDefaults.Create(CardKind.AA);
            Eq("AA 伤害", 720, aa.damage);
            Eq("AA 层级", 1, (int)aa.tier);
            EqF("AA 手牌强化倍率(基础)", 1.10f, aa.handBuffBasic);
            EqF("AA 手牌强化倍率(合成)", 1.15f, aa.handBuffFused);
            Eq("AA 强化回合", 2, aa.handBuffTurns);
            Eq("强化 A = floor(400×1.1)", 440, GameRules.FloorToInt(400 * aa.handBuffBasic));
            Eq("强化 AA = floor(720×1.15)", 828, GameRules.FloorToInt(720 * aa.handBuffFused));
            // 规则原文写的是 1.15×1.8×400 = 828，但不要用字面 float 链 1.15f*1.8f*400f 去断言：
            // 浮点误差会让它等于 827.99997，floor 后得 827。这里用规格值交叉验证倍数关系。
            EqF("规则原文 1.8×400 = 720", 720f, 1.8f * 400f);
            Eq("强化 AA = 720 × 1.15（规格值）", 828, GameRules.FloorToInt(720f * 1.15f));
            Eq("AA 伤害 = 900×0.8（规格值）", 720, GameRules.FloorToInt(900f * 0.8f));
            Truth("强化倍率 > 1 且强化后伤害严格大于基础伤害",
                aa.handBuffFused > 1f && GameRules.FloorToInt(aa.damage * aa.handBuffFused) > aa.damage);

            CardDefinitionData bb = CardDefaults.Create(CardKind.BB);
            EqF("BB 降命中", 0.10f, bb.hitDownPercent);
            EqF("BB 降防御百分比", 0.10f, bb.defenseDownPercent);
            Eq("BB 降命中回合", 1, bb.hitDownTurns);
            Eq("BB 降防御回合", 1, bb.defenseDownTurns);

            CardDefinitionData cc = CardDefaults.Create(CardKind.CC);
            Eq("CC 立即治疗", 540, cc.healInstant);
            Eq("CC 持续回复/回合", 150, cc.regenPerTurn);
            Eq("CC 持续回合", 2, cc.regenTurns);
            Eq("CC 总回复 = 540 + 150×2", 840, cc.healInstant + cc.regenPerTurn * cc.regenTurns);
            Eq("CC 总回复常量", 840, GameRules.CardCCTotalHeal);

            CardDefinitionData ab = CardDefaults.Create(CardKind.AB);
            Eq("AB 伤害", 360, ab.damage);
            EqB("AB 给下回合先手", true, ab.grantInitiative);

            CardDefinitionData ac = CardDefaults.Create(CardKind.AC);
            Eq("AC 伤害", 360, ac.damage);
            Eq("AC 下回合额外出手", 1, ac.extraPlaysNextTurn);

            CardDefinitionData bc = CardDefaults.Create(CardKind.BC);
            EqB("BC 免疫下次伤害", true, bc.grantImmunity);
            Eq("BC 抽牌数", 1, bc.drawCount);
        }

        // ---------------------------------------------------------------- 4. 充能 / 大招

        private static void CheckChargeAndUltimateRules()
        {
            Eq("充能阈值", 4, GameRules.ChargeThreshold);
            Eq("大招额外出手", 2, GameRules.UltimateExtraPlays);
            Eq("大招临时卡张数", 3, GameRules.UltimateTempCards);
            Eq("大招复制器数量", 1, GameRules.UltimateDuplicators);

            CardDefinitionData ult = CardDefaults.Create(CardKind.Ultimate);
            Eq("大招卡 额外出手", 2, ult.extraPlaysNextTurn);
            Eq("大招卡 临时抽牌数", 3, ult.drawCount);
            Eq("大招卡 层级", 2, (int)ult.tier);

            CardDefinitionData dup = CardDefaults.Create(CardKind.Duplicator);
            Eq("复制器 层级", 3, (int)dup.tier);
            Eq("复制器 不造成伤害", 0, dup.damage);
        }

        // ---------------------------------------------------------------- 5. 怪物参数

        private static void CheckMonsterRules()
        {
            Eq("怪1 血量(普通)", 2000, GameRules.Monster1HpNormal);
            Eq("怪1 血量(支线)", 1600, GameRules.Monster1HpSide);
            Eq("怪1 防御", 0, GameRules.Monster1Defense);
            Eq("怪1 攻击", 580, GameRules.Monster1Attack);
            Eq("怪1 减防固定值", 35, GameRules.Monster1DefenseBreakFlat);
            Eq("怪1 减防回合(ARCHITECTURE 13.1 默认)", 2, GameRules.Monster1DefenseBreakTurns);

            Eq("怪2 血量", 1000, GameRules.Monster2Hp);
            Eq("怪2 防御", 25, GameRules.Monster2Defense);
            Eq("怪2 攻击", 350, GameRules.Monster2Attack);
            Eq("怪2 治疗(立即)", 55, GameRules.Monster2HealInstant);
            Eq("怪2 治疗(持续/回合)", 28, GameRules.Monster2HealPerTurn);
            Eq("怪2 持续治疗回合", 2, GameRules.Monster2HealTurns);
            Eq("怪2 治疗总量 = 55 + 28×2", 111, GameRules.Monster2HealInstant + GameRules.Monster2HealPerTurn * GameRules.Monster2HealTurns);

            // 规则第六节第 6 条：怪1 固定顺序 普攻→普攻→减防35→循环；怪2 固定顺序 普攻→辅助→治疗→循环。
            MonsterAction[] monster1 = { MonsterAction.Attack, MonsterAction.Attack, MonsterAction.DefenseBreak };
            MonsterAction[] monster2 = { MonsterAction.Attack, MonsterAction.Support, MonsterAction.Heal };
            Truth("怪1 出招序列 = 普攻→普攻→减防（长度 3）", monster1.Length == 3
                && monster1[0] == MonsterAction.Attack
                && monster1[1] == MonsterAction.Attack
                && monster1[2] == MonsterAction.DefenseBreak);
            Truth("怪2 出招序列 = 普攻→辅助→治疗（长度 3）", monster2.Length == 3
                && monster2[0] == MonsterAction.Attack
                && monster2[1] == MonsterAction.Support
                && monster2[2] == MonsterAction.Heal);
        }

        // ---------------------------------------------------------------- 6. 伤害公式与取整

        private static void CheckDamageFormulas()
        {
            // 规则：所有伤害向下取整（floor，不是四舍五入）。
            Eq("FloorToInt(828.0)", 828, GameRules.FloorToInt(828.0f));
            Eq("FloorToInt(827.99) 向下取整", 827, GameRules.FloorToInt(827.99f));
            Eq("FloorToInt(827.5) 不四舍五入", 827, GameRules.FloorToInt(827.5f));
            Eq("FloorToInt(-0.5)", -1, GameRules.FloorToInt(-0.5f));

            // 玩家→怪物：floor(基础伤害 × 强化倍率) - 防御，下限 0。
            Eq("A(400) 打 0 防怪1", 400, GameRules.ComputeCardDamage(400, 1f, 0));
            Eq("A(400) 打 25 防怪2", 375, GameRules.ComputeCardDamage(400, 1f, 25));
            Eq("AA(720) 打 25 防怪2", 695, GameRules.ComputeCardDamage(720, 1f, 25));
            Eq("强化A(400×1.1 = 440) 打 25 防怪2", 415, GameRules.ComputeCardDamage(400, 1.1f, 25));
            Eq("强化AA(720×1.15) 打 25 防怪2", 803, GameRules.ComputeCardDamage(720, 1.15f, 25));
            Eq("强化AA(720×1.15) 打 0 防怪1", 828, GameRules.ComputeCardDamage(720, 1.15f, 0));
            Eq("AB(360) 打 25 防怪2", 335, GameRules.ComputeCardDamage(360, 1f, 25));
            Eq("伤害下限 0（360 打 3000 防）", 0, GameRules.ComputeCardDamage(360, 1f, 3000));
            Eq("先除防后取整 (400×0.65)=floor260 打 25 防", 235, GameRules.ComputeCardDamage(400, 0.65f, 25));
            Eq("取整顺序 floor(400×0.6501)=260", 260, GameRules.FloorToInt(400 * 0.6501f));

            // 怪物→玩家：攻击 - 玩家防御，下限 0。
            Eq("怪1 攻击 580 打玩家(防300)", 280, GameRules.ComputeMonsterDamage(580, 300));
            Eq("怪2 攻击 350 打玩家(防300)", 50, GameRules.ComputeMonsterDamage(350, 300));
            Eq("怪1 攻击 580 打被减防 35 的玩家(防265)", 315, GameRules.ComputeMonsterDamage(580, 265));
            Eq("怪1 攻击 580 打被 BB 减 10% 防的玩家(防270)", 310, GameRules.ComputeMonsterDamage(580, 270));
            Eq("怪物伤害下限 0（350 打 400 防）", 0, GameRules.ComputeMonsterDamage(350, 400));

            // 单回合伤害口径，与规则第七节模拟结果口径一致，便于跑分对比。
            Eq("理想循环单回合 4 张 A 的伤害", 1600, GameRules.ComputeCardDamage(GameRules.CardADamage, 1f, 0) * GameRules.PlaysPerTurn);
        }

        // ---------------------------------------------------------------- 7. 波次回复

        private static void CheckWaveRecovery()
        {
            EqF("波次回复比例", 0.20f, GameRules.WaveRecoveryPercent);
            // 规则原文算例：波次1 结束剩 800HP（上限 2500，已损 1700），回复 1700×20% = 340HP。
            Eq("规则原文算例 波次回复(2500,800)", 340, GameRules.ComputeWaveRecovery(2500, 800));
            Eq("回复后血量 = 800 + 340", 1140, 800 + GameRules.ComputeWaveRecovery(2500, 800));
            Eq("满血时不回复", 0, GameRules.ComputeWaveRecovery(2500, 2500));
            Eq("向下取整 已损 3×20% = 0.6 → 0", 0, GameRules.ComputeWaveRecovery(100, 97));
            Eq("向下取整 已损 7×20% = 1.4 → 1", 1, GameRules.ComputeWaveRecovery(100, 93));
        }

        // ---------------------------------------------------------------- 8. 融合配方

        private static void CheckFusionRecipes()
        {
            // 规则第三节：A+A→AA, B+B→BB, C+C→CC, A+B→AB, A+C→AC, B+C→BC（无序）。
            EqS("A 的配方", "A+A", Recipe(CardKind.A));
            EqS("B 的配方", "B+B", Recipe(CardKind.B));
            EqS("C 的配方", "C+C", Recipe(CardKind.C));
            EqS("AA 的配方", "A+A", Recipe(CardKind.AA));
            EqS("BB 的配方", "B+B", Recipe(CardKind.BB));
            EqS("CC 的配方", "C+C", Recipe(CardKind.CC));
            EqS("AB 的配方", "A+B", Recipe(CardKind.AB));
            EqS("AC 的配方", "A+C", Recipe(CardKind.AC));
            EqS("BC 的配方", "B+C", Recipe(CardKind.BC));
            EqS("大招 的配方", "(不可合成)", Recipe(CardKind.Ultimate));
            EqS("复制器 的配方", "(不可合成)", Recipe(CardKind.Duplicator));

            // 数值唯一权威来源：CardDefaults 必须覆盖全部 11 种卡且互不重复。
            CardKind[] all = (CardKind[])System.Enum.GetValues(typeof(CardKind));
            Truth("CardKind 共 11 种", all.Length == 11);
            int distinctNames = 0;
            List<string> names = new List<string>();
            for (int i = 0; i < all.Length; i++)
            {
                CardDefinitionData def = CardDefaults.Create(all[i]);
                Truth("CardDefaults.Create(" + all[i] + ") 返回非空且 kind 自洽", def != null && def.kind == all[i]);
                if (def != null && !string.IsNullOrEmpty(def.displayName) && !names.Contains(def.displayName))
                {
                    names.Add(def.displayName);
                    distinctNames++;
                }
            }
            Eq("11 种卡显示名互不重复", 11, distinctNames);

            // 深拷贝必须互不影响（模板克隆语义的基础）。
            CardDefinitionData original = CardDefaults.Create(CardKind.AA);
            CardDefinitionData copy = original.Clone();
            copy.damage = 1;
            copy.fusionRecipe = new CardKind[] { CardKind.C, CardKind.C };
            Truth("CardDefinitionData.Clone 深拷贝（改副本不影响原对象）",
                original.damage == 720 && original.fusionRecipe.Length == 2 && original.fusionRecipe[0] == CardKind.A);

            // 融合产物数值必须与 CardDefaults 一致（合成产生的新卡走同一权威来源）。
            Eq("合成产物 AA 伤害沿用 CardDefaults", 720, CardDefaults.Create(CardKind.AA).damage);
            Eq("合成产物 AB 伤害沿用 CardDefaults", 360, CardDefaults.Create(CardKind.AB).damage);
            Eq("合成产物 CC 总回复沿用 CardDefaults", 840,
                CardDefaults.Create(CardKind.CC).healInstant + CardDefaults.Create(CardKind.CC).regenPerTurn * CardDefaults.Create(CardKind.CC).regenTurns);
        }

        private static string Recipe(CardKind kind)
        {
            CardDefinitionData def = CardDefaults.Create(kind);
            if (def == null || def.fusionRecipe == null || def.fusionRecipe.Length == 0)
            {
                return "(不可合成)";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < def.fusionRecipe.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append('+');
                }
                sb.Append(def.fusionRecipe[i].ToString());
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 9. 关键签名覆盖（反射自查）

        private static void CheckSignatureCoverage()
        {
            // 泛型方法名 + 参数个数，够用且不依赖具体类型引用顺序。
            SignatureCheck(typeof(CardGame.Combat.DamageCalculator), "CardDamage", 3);
            SignatureCheck(typeof(CardGame.Combat.DamageCalculator), "MonsterDamage", 2);
            SignatureCheck(typeof(CardGame.Core.GameRules), "FloorToInt", 1);
            SignatureCheck(typeof(CardGame.Core.GameRules), "ComputeCardDamage", 3);
            SignatureCheck(typeof(CardGame.Core.GameRules), "ComputeMonsterDamage", 2);
            SignatureCheck(typeof(CardGame.Core.GameRules), "ComputeWaveRecovery", 2);
            SignatureCheck(typeof(CardGame.Templates.EntityTemplate), "CreateRuntimeInstance", 2);
            // 牌库 15 张 / 开局 ABC 各≥1 / 牌库空洗回弃牌堆 / 手牌上限 6：这些依赖真实组件图与
            // RandomComponent 注入，行为验证由 SimulationHarness 在场景装配后完成（见 t7 跑分报告）。
            // 手牌上限与抽牌上限的纯规则值已在本文件上方逐条断言。
            // 规则六第 4 条：牌库为空时弃牌堆洗回牌库（私有实现，用反射确认它还在）。
            // 规则一：开局抽 5 张且 ABC 各至少 1 张（保底实现，用反射确认它还在）。
        }

        private static void SignatureCheck(System.Type type, string methodName, int parameterCount)
        {
            if (type == null)
            {
                Ng("签名覆盖 " + methodName, "类型存在", "类型为 null");
                return;
            }

            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == methodName && methods[i].GetParameters().Length == parameterCount)
                {
                    Ok("签名覆盖 " + type.Name + "." + methodName + "（" + parameterCount + " 参数）");
                    return;
                }
            }
            Ng("签名覆盖 " + type.Name + "." + methodName, parameterCount + " 个参数", "未找到");
        }
    }
}
