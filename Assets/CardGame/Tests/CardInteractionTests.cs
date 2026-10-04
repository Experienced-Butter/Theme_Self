using System.Collections;
using System.Collections.Generic;
using CardGame.Cards;
using CardGame.Combat;
using CardGame.Controllers;
using CardGame.Core;
using CardGame.Players;
using CardGame.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CardGame.Tests
{
    /// <summary>
    /// 「拖牌 → 掷出 / 融合」这条鼠标交互链的自检 —— **不需要真鼠标**。
    ///
    /// 为什么需要它：MCP 无法模拟点击，编辑器里的拖动交互是唯一无法用鼠标验证的路径。
    /// 本文件的做法是「用代码造 PointerEventData，直接驱动 CardWidget 的
    /// OnPointerDown / OnDrag / OnPointerUp」，再用公开 API（GameController.Hand / Context.Enemies /
    /// 层级父子关系 / 实体 HP）断言判定结果 —— 三个鼠标事件之外的一切都走真实交付代码。
    ///
    /// 覆盖的断言（每条失败都带「期望 vs 实测」）：
    ///   0. 运行环境与图层契约（模块 / 控制者 / 阶段 / 手牌 / 敌人 / 六层顺序 / 遮罩不吃射线 / 卡面吃射线）
    ///   1. 抓起：父节点 → Layer_Above、最后一个兄弟、IsDragging、光标偏移使卡牌同 delta 移动
    ///   2. 掷出：拖到敌人矩形内松手 → 该敌人 HP 下降、卡牌离开手牌、状态行出现「掷出」
    ///   3. 融合：拖到另一张手牌上松手 → 两张材料消失、手牌 −1、产物入手
    ///   4. 无交互：拖到空白处松手 → 回到 Layer_Cards、手牌数不变、不消耗出牌次数
    ///   5. 遮罩层级：预览目标临时提到 Layer_Above（敌人 / 手牌两种目标），松手后各自还原
    ///   6. 投影判定：BattleUiKit.OverlapArea / RectsOverlap 的重合 / 不重合 / 边界相接 / 隐藏对象
    ///
    /// 用法（Play 模式下）：
    ///   List&lt;string&gt; failures = CardInteractionTests.RunAll();     // 空列表 = 全部通过
    ///
    /// 注意：本类只在 Play 模式有意义（入场时序是协程，编辑模式不推进）。编辑器入口见
    /// <see cref="CardInteractionTestRunner"/> 与菜单 `CardGame/运行 UI 交互自检`。
    /// </summary>
    public static class CardInteractionTests
    {
        // 图层名（与 BattleUiKit / CardBattleModule 的文档契约一致）。
        private const string LayerBackground = "Layer_Background";
        private const string LayerEntities = "Layer_Entities";
        private const string LayerCards = "Layer_Cards";
        private const string LayerMask = "Layer_Mask";
        private const string LayerAbove = "Layer_Above";
        private const string LayerOverlay = "Layer_Overlay";

        private static readonly List<string> Lines = new List<string>();
        private static readonly List<string> FailureList = new List<string>();

        private static int _checkCount;

        /// <summary>上一次 RunAll() 的检查总数（含 PASS 与 FAIL），供运行器输出 "ALL PASS (N checks)"。</summary>
        public static int LastCheckCount { get; private set; }

        /// <summary>上一次 RunAll() 的完整清单（PASS/FAIL 逐条），供控制台逐行打印。</summary>
        public static List<string> LastLines { get; private set; } = new List<string>();

        /// <summary>右键探针（<see cref="RunRightButtonProbe"/>）的行，与主断言分开、不参与 ALL PASS 计数。</summary>
        public static List<string> LastProbeLines { get; private set; } = new List<string>();

        /// <summary>上一次右键探针实际执行的检查条数（0 = 现场不完整、探针被跳过）。</summary>
        public static int LastProbeChecks { get; private set; }

        /// <summary>战后观察的行（只打印事实，不参与 PASS/FAIL 计数）。</summary>
        public static List<string> LastObservationLines { get; private set; } = new List<string>();

        // ------------------------------------------------------------------
        // 公开入口
        // ------------------------------------------------------------------

        /// <summary>执行全部交互断言。返回失败项清单；空列表 = 全部通过。</summary>
        public static System.Collections.Generic.List<string> RunAll()
        {
            Lines.Clear();
            FailureList.Clear();
            _checkCount = 0;

            if (!Application.isPlaying)
            {
                Fail("运行环境：自检必须在 Play 模式下运行（编辑模式协程不推进，入场时序走不完）",
                    "Application.isPlaying == true",
                    "false —— 请用菜单 CardGame/运行 UI 交互自检");
                return Finish();
            }

            Env env = GatherEnv();

            CheckEnv(env);
            CheckOverlapPredicate();

            if (env.Ready)
            {
                CheckGrab(env);
                CheckFusion(env);
                CheckThrow(env);
                CheckBlankDrop(env);
            }
            else
            {
                Fail("运行环境：现场足够跑交互断言（模块 / 控制者 / 手牌 ≥ 2 / 存活敌人 / 三个图层）",
                    "全部满足",
                    DescribeEnv(env));
            }

            return Finish();
        }

        /// <summary>
        /// 附加探针：**非左键**（右键/中键）是否也会抓起卡牌、把牌吃掉，以及左键拖动中的右键事件是否会生效。
        ///
        /// 为什么单列一类、不计入 RunAll 的 ALL PASS 计数：主断言覆盖的是左键主链（队长点名的验收口径），
        /// 而这一条探的是「卡牌侧有没有按键判断」。这是**真缺陷**类别，红了不该让主链结论跟着变红。
        ///
        /// 覆盖两组用例：
        ///   ①② 只有右键：右键按下不得进入拖动；右键拖到敌人身上松开不得有副作用。
        ///   ③④⑤ 混合按键：**先用左键抓起**（IsDragging=true），再用右键的 OnDrag / OnPointerUp——
        ///        卡牌不得位移、不得抬起预览目标、拖动状态不得被右键松开结束、不得结算。
        ///        ③④⑤ 是必需的：若只删掉 OnDrag / OnPointerUp 的守卫而保留 OnPointerDown 的，
        ///        ①② 依旧全绿（右键根本抓不起来 → OnDrag/OnPointerUp 会在 `if (!IsDragging) return` 处早退），
        ///        只有「左键抓起 + 右键事件」才照得出那两个守卫是否还在。
        ///
        /// 本探针**不能**证明的：「uGUI 在真机上确实会把右键派发成 pointerDown→drag→up」——那取决于
        /// EventSystem 上的输入模块（InputSystemUIInputModule 的点击/拖动绑定；verifier 已实测该模块绑定了
        /// UI/RightClick 与 UI/MiddleClick），本自检直接调事件，绕过了输入层。
        /// </summary>
        public static System.Collections.Generic.List<string> RunRightButtonProbe()
        {
            List<string> lines = new List<string>();
            List<string> failures = new List<string>();
            LastProbeChecks = 0;

            Env env = GatherEnv();
            if (!env.Ready || env.HandWidgets.Count == 0 || env.FirstAliveEnemy < 0)
            {
                lines.Add("SKIP: 现场不完整（模块 / 手牌 / 存活敌人缺一），右键探针未执行");
                LastProbeLines = lines;
                return failures;
            }

            CardWidget widget = env.HandWidgets[0];
            RectTransform rect = (RectTransform)widget.transform;
            Vector2 home = rect.position;
            Vector2 enemyCenter = RectCenter(env.Enemies[env.FirstAliveEnemy].Rect);

            int handBefore = env.Controller.Hand.Count;
            int playsBefore = PlaysRemaining(env);

            // ① 右键按下：正确实现下不应进入拖动状态。
            LastProbeChecks++;
            PointerEventData down = NewPointerEvent(home, PointerEventData.InputButton.Right);
            widget.OnPointerDown(down);
            string parentAfterDown = ParentName(rect);

            if (widget.IsDragging)
            {
                string line = "FAIL: 右键按下也抓起了卡牌（IsDragging=true，父节点=" + parentAfterDown
                              + "）—— CardWidget 的三个事件都没有判断 eventData.button。"
                              + "修法：三个事件开头加 `if (eventData.button != PointerEventData.InputButton.Left) return;`";
                lines.Add(line);
                failures.Add(line);
            }
            else
            {
                lines.Add("PASS: 右键按下不会抓起卡牌（IsDragging=false，父节点=" + parentAfterDown + "）");
            }

            // ② 右键拖到敌人身上再松开：正确实现下应完全无副作用（不掷出、不融合）。
            LastProbeChecks++;
            PointerEventData move = NewPointerEvent(enemyCenter, PointerEventData.InputButton.Right);
            widget.OnDrag(move);
            PointerEventData up = NewPointerEvent(enemyCenter, PointerEventData.InputButton.Right);
            widget.OnPointerUp(up);

            int handAfter = env.Controller.Hand.Count;
            int playsAfter = PlaysRemaining(env);

            if (handAfter != handBefore || playsAfter != playsBefore)
            {
                string line = "FAIL: 右键拖到敌人身上松开把牌吃掉了（手牌 " + handBefore + " → " + handAfter
                              + "，出牌次数 " + playsBefore + " → " + playsAfter
                              + "）—— 真机表现就是「右键误拖白掉一张牌」";
                lines.Add(line);
                failures.Add(line);
            }
            else
            {
                lines.Add("PASS: 右键拖到敌人身上松开没有副作用（手牌 " + handBefore + " → " + handAfter
                          + "，出牌次数 " + playsBefore + " → " + playsAfter + "）");
            }

            // 收尾（只在缺陷版会触发）：万一卡牌被右键留在拖动层，用**左键**把它放回手牌层 —— 左键松开
            // 在「有守卫」和「无守卫」两种实现下都能走完，右键松开在有守卫时会变成空操作。
            if (widget.IsDragging)
            {
                BlankSpot blank = FindBlankCenter(env, widget, rect);
                Vector2 target = blank.Found ? blank.Center : home;
                PointerEventData release = NewPointerEvent(target, PointerEventData.InputButton.Left);
                widget.OnDrag(release);
                widget.OnPointerUp(release);
                lines.Add("OBS: 探针收尾，卡牌父节点=" + ParentName(rect)
                          + "，IsDragging=" + widget.IsDragging);
            }

            // ③ 混合按键前置条件：左键按下必须抓起卡牌（否则 ④⑤ 无从谈起）。
            LastProbeChecks++;
            Vector2 grabPoint = rect.position;
            PointerEventData leftDown = NewPointerEvent(grabPoint, PointerEventData.InputButton.Left);
            widget.OnPointerDown(leftDown);
            bool grabbed = widget.IsDragging;
            string grabbedParent = ParentName(rect);
            bool grabbedAndRaised = grabbed && grabbedParent == LayerAbove;

            if (!grabbedAndRaised)
            {
                string line = "FAIL: 混合按键用例的前置条件不成立——左键按下应抓起卡牌并挂到 " + LayerAbove
                              + "（实测 IsDragging=" + grabbed + "，父节点=" + grabbedParent
                              + "）。这本身是一个独立缺陷或流程状态问题（例如 _battleEnded 为真时 BeginDrag 会 "
                              + "ReturnHome 早退），**不要**把它读成 OnDrag / OnPointerUp 守卫的问题";
                lines.Add(line);
                failures.Add(line);
            }
            else
            {
                lines.Add("PASS: 混合按键前置——左键按下抓起卡牌并挂到 " + LayerAbove + "（IsDragging=true）");
            }

            if (grabbedAndRaised)
            {
                // ④ 左键抓起后，右键 OnDrag 不得移动卡牌、不得抬起预览目标。
                LastProbeChecks++;
                Vector3 positionBefore = rect.position;
                PointerEventData rightMove = NewPointerEvent(enemyCenter, PointerEventData.InputButton.Right);
                widget.OnDrag(rightMove);
                string enemyParent = ParentName(env.Enemies[env.FirstAliveEnemy].Rect);
                float moved = Vector3.Distance(rect.position, positionBefore);
                bool previewRaised = enemyParent == LayerAbove;

                if (moved > 0.5f || previewRaised)
                {
                    string line = "FAIL: 左键抓起后，右键 OnDrag 仍然生效（卡牌位移 " + moved.ToString("0.##")
                                  + "，敌人父节点=" + enemyParent
                                  + "）—— OnDrag 的非左键守卫缺失或失效";
                    lines.Add(line);
                    failures.Add(line);
                }
                else
                {
                    lines.Add("PASS: 左键抓起后，右键 OnDrag 不移动卡牌也不抬起预览目标（位移 "
                              + moved.ToString("0.##") + "，敌人父节点=" + enemyParent + "）");
                }

                // ⑤ 右键 OnPointerUp 不得结束拖动、不得结算。
                LastProbeChecks++;
                PointerEventData rightUp = NewPointerEvent(enemyCenter, PointerEventData.InputButton.Right);
                widget.OnPointerUp(rightUp);
                string parentAfterRightUp = ParentName(rect);
                int handNow = env.Controller.Hand.Count;
                int playsNow = PlaysRemaining(env);

                if (!widget.IsDragging || parentAfterRightUp != LayerAbove
                    || handNow != handBefore || playsNow != playsBefore)
                {
                    string line = "FAIL: 右键松开结束了左键拖动或发生了结算（IsDragging=" + widget.IsDragging
                                  + "，父节点=" + parentAfterRightUp + "，手牌 " + handBefore + " → " + handNow
                                  + "，出牌次数 " + playsBefore + " → " + playsNow
                                  + "）—— OnPointerUp 的非左键守卫缺失或失效";
                    lines.Add(line);
                    failures.Add(line);
                }
                else
                {
                    lines.Add("PASS: 右键松开既不结束左键拖动也不结算（IsDragging=true，父节点="
                              + parentAfterRightUp + "，手牌 " + handBefore + " → " + handNow
                              + "，出牌次数 " + playsBefore + " → " + playsNow + "）");
                }

                // ⑥ 用左键收尾：拖到空白处松开应正常回位，保证探针不留脏状态。
                LastProbeChecks++;
                BlankSpot spot = FindBlankCenter(env, widget, rect);
                Vector2 drop = spot.Found ? spot.Center : grabPoint;
                PointerEventData leftMove = NewPointerEvent(drop, PointerEventData.InputButton.Left);
                widget.OnDrag(leftMove);
                PointerEventData leftUp = NewPointerEvent(drop, PointerEventData.InputButton.Left);
                widget.OnPointerUp(leftUp);
                string finalParent = ParentName(rect);

                if (widget.IsDragging || finalParent != LayerCards)
                {
                    string line = "FAIL: 左键收尾未能把卡牌放回手牌层（IsDragging=" + widget.IsDragging
                                  + "，父节点=" + finalParent + "）";
                    lines.Add(line);
                    failures.Add(line);
                }
                else
                {
                    lines.Add("PASS: 左键收尾把卡牌放回 " + LayerCards + "（IsDragging=false）");
                }
            }

            LastProbeLines = lines;
            return failures;
        }

        /// <summary>
        /// 附加观察（**只打印事实、不判 PASS/FAIL**）：战斗结束（横幅已弹出）之后，拖动链到底被拦到什么程度。
        ///
        /// 为什么要单开一段：`CardBattleModule._battleEnded` 是私有字段，本自检不反射私有状态（"只读 + 驱动公开 API"
        /// 的设计约束）。但**结束战斗本身有公开路径**：`GameFlowComponent.ResolveVictory()` 是 public（victory 分支），
        /// 它会经 `ReportBattleEnded` → `GameController.NotifyBattleEnded`（internal）→ `OnBattleEnded`
        /// → `CardBattleModule.HandleBattleEnded` 把 `_battleEnded` 置真。
        /// 注意：`ResolveDefeat()` 是 private，defeat 分支没有公开入口 —— 所以这条路是「经 victory 分支注入」；
        /// 对 R5 无影响（HandleBattleEnded 无论 bool 都置同一个 `_battleEnded`）。
        ///
        /// **假阴性防护**：`ResolveVictory()` 开头有 `if (_battleOver) return;`，`GameController.NotifyBattleEnded`
        /// 另有一次性 `_endedReported` 守卫 —— 若任一处已经报过，本次注入就是静默空转，三条 OBS 会打出
        /// 「IsDragging=false / Layer_Cards / 无位移」，看起来像「R5 没复现」，实际是注入没生效。
        /// 因此这里先取公开见证物（`Flow.Phase == Victory` 与 `ResultBanner.activeSelf`），注入不生效就只报 WARN 并返回，
        /// 不留任何可能被误读的 OBS。
        ///
        /// 已知语义（读码结论，本段只复核它）：EndDrag 会拦住结算；但 OnPointerDown 仍会先把 IsDragging 置真，
        /// 之后 BeginDrag 走 `ReturnHome()` 早退，于是随后的 OnDrag 依旧会移动卡牌、并把预览目标抬到 Layer_Above。
        /// 是否算缺陷由验收方判断，所以这里不做断言，只给实测数值。
        /// </summary>
        public static List<string> RunPostBattleObservation()
        {
            List<string> lines = new List<string>();
            LastObservationLines = lines;

            Env env = GatherEnv();
            if (env.Flow == null || env.Controller == null || env.HandWidgets.Count == 0
                || env.FirstAliveEnemy < 0)
            {
                lines.Add("SKIP: 现场不完整（控制器 / 手牌 / 存活敌人缺一），战后观察未执行");
                return lines;
            }

            int handBefore = env.Controller.Hand.Count;

            // 公开 API 结束战斗：这是本自检唯一不反射就能碰到「战斗已结束」状态的路径（victory 分支）。
            env.Flow.ResolveVictory();

            // 公开见证物：注入是否真的生效。
            bool injected = env.Flow.Phase == GamePhase.Victory;
            Transform banner = env.Canvas != null
                ? FindLastByName(env.Canvas.transform, "ResultBanner")
                : null;
            string bannerState = banner != null ? banner.gameObject.activeSelf.ToString() : "(未找到 ResultBanner)";
            lines.Add("OBS: 已调用公开 API GameFlowComponent.ResolveVictory()（victory 分支注入）→ 阶段="
                      + env.Flow.Phase + "，ResultBanner.activeSelf=" + bannerState);

            if (!injected)
            {
                lines.Add("WARN: 注入**未生效**（阶段不是 Victory，说明 _battleOver 或 _endedReported 已经报过），"
                          + "下面三条 OBS 因此全部跳过 —— 否则它们会看起来像「R5 没复现」，那是假阴性");
                return lines;
            }

            CardWidget widget = env.HandWidgets[0];
            RectTransform rect = (RectTransform)widget.transform;
            Vector2 home = rect.position;
            Vector2 enemyCenter = RectCenter(env.Enemies[env.FirstAliveEnemy].Rect);

            PointerEventData down = NewPointerEvent(home, PointerEventData.InputButton.Left);
            widget.OnPointerDown(down);
            lines.Add("OBS: 战后左键按下 → IsDragging=" + widget.IsDragging + "，父节点=" + ParentName(rect)
                      + "（若期望「战斗结束后连抓都不该抓」，这里应为 IsDragging=false）");

            Vector3 positionBefore = rect.position;
            PointerEventData move = NewPointerEvent(enemyCenter, PointerEventData.InputButton.Left);
            widget.OnDrag(move);
            float moved = Vector3.Distance(rect.position, positionBefore);
            string enemyParent = ParentName(env.Enemies[env.FirstAliveEnemy].Rect);
            lines.Add("OBS: 战后左键拖动 → 卡牌位移 " + moved.ToString("0.##") + "，敌人父节点=" + enemyParent
                      + "（父节点为 " + LayerAbove + " 说明预览目标仍会被抬到遮罩之上）");

            PointerEventData up = NewPointerEvent(enemyCenter, PointerEventData.InputButton.Left);
            widget.OnPointerUp(up);
            lines.Add("OBS: 战后左键松开 → 手牌 " + handBefore + " → " + env.Controller.Hand.Count
                      + "，父节点=" + ParentName(rect) + "，IsDragging=" + widget.IsDragging
                      + "（手牌不变且回到 " + LayerCards + " 说明结算确实被 EndDrag 拦住了）");

            return lines;
        }

        /// <summary>启动（或复用）一场卡牌战斗，供运行器在 Start 里调用。</summary>
        public static CardBattleModule EnsureBattleForTests()
        {
            CardBattleModule module = FindBattleModule();
            bool alreadyBuilt = module != null && module.GetComponentInChildren<Canvas>() != null;

            if (alreadyBuilt)
            {
                // 场景里已经有开好的战斗（例如先点过「CardGame/进入卡牌战斗」）：
                // 直接复用。这里刻意不再调 Launch() —— 再 EnterBattle 一次会 BuildUi 出
                // 第二套图层，名字重复会让断言和肉眼观察都变得不可信。
                Debug.Log("[CardInteractionTests] 场景里已有建好 UI 的战斗模块，直接复用（避免重复 BuildUi）。");
                return module;
            }

            if (module == null)
            {
                GameObject go = new GameObject("CardBattle");
                module = go.AddComponent<CardBattleModule>();
            }

            // Launch() 内部会 EnterBattle()；若 autoEnterOnPlay 仍为 true，组件自身的 Start
            // 会在本帧稍后再 EnterBattle 一次 → 两套图层。这里先关掉，保证只有一套 UI。
            module.autoEnterOnPlay = false;
            return CardBattleModule.Launch();
        }

        /// <summary>入场时序的预计总时长（秒），取自模块的公开时序字段。</summary>
        public static float EntrySequenceSeconds(CardBattleModule module)
        {
            if (module == null)
            {
                return 3f;
            }

            return module.backgroundSlideDuration + module.entitySlideDuration
                 + module.sequenceGap + module.cardSlideDuration + 0.3f;
        }

        /// <summary>手牌里是否已经有卡（UI 是否已建出来）。</summary>
        public static bool HasLiveHand(CardBattleModule module)
        {
            GameController controller = GameController.Instance;
            if (module == null || controller == null || controller.Hand.Count == 0)
            {
                return false;
            }

            return CollectLiveHandWidgets(module).Count > 0;
        }

        /// <summary>手牌对应的 CardWidget（已排除待销毁的、正在拖动的、以及已不在手牌里的卡）。</summary>
        public static List<CardWidget> CollectLiveHandWidgets(CardBattleModule module)
        {
            List<CardWidget> result = new List<CardWidget>();
            if (module == null)
            {
                return result;
            }

            GameController controller = GameController.Instance;
            if (controller == null)
            {
                return result;
            }

            IReadOnlyList<CardInstance> hand = controller.Hand;
            CardWidget[] all = module.GetComponentsInChildren<CardWidget>(true);
            for (int i = 0; i < all.Length; i++)
            {
                CardWidget widget = all[i];
                if (widget == null || widget.Card == null || widget.IsDragging)
                {
                    continue;
                }

                if (!HandContains(hand, widget.Card))
                {
                    continue;
                }

                result.Add(widget);
            }

            return result;
        }

        // ------------------------------------------------------------------
        // 环境采集
        // ------------------------------------------------------------------

        /// <summary>一个敌人目标：UI 矩形 + 与之对应的实体（下标一致，都由名字 EnemyN 决定）。</summary>
        private sealed class EnemyTarget
        {
            public string Name;
            public RectTransform Rect;
            public Combatant Enemy;
        }

        /// <summary>一次自检需要的所有现场引用（全部通过公开 API / 层级查找获得，不反射私有字段）。</summary>
        private sealed class Env
        {
            public CardBattleModule Module;
            public GameController Controller;
            public GameFlowComponent Flow;
            public Canvas Canvas;
            public RectTransform LayerAboveRect;
            public RectTransform LayerCardsRect;
            public RectTransform LayerEntitiesRect;
            public RectTransform LayerMaskRect;
            public Text StatusText;
            public List<CardWidget> HandWidgets = new List<CardWidget>();
            public List<EnemyTarget> Enemies = new List<EnemyTarget>();

            public bool Ready;
            public int FirstAliveEnemy = -1;

            public Rect CanvasWorldRect
            {
                get
                {
                    if (Canvas != null)
                    {
                        return BattleUiKit.ScreenRect(Canvas.transform as RectTransform);
                    }

                    return new Rect(0f, 0f, Screen.width, Screen.height);
                }
            }
        }

        private static Env GatherEnv()
        {
            Env env = new Env();
            env.Module = FindBattleModule();
            env.Controller = GameController.Instance;

            if (env.Module == null || env.Controller == null)
            {
                return env;
            }

            // GameFlowComponent / LevelComponent 等挂在**控制器实体**上（与 GameController 同一个 GameObject），
            // 不是挂在 CardBattleModule 的对象上。
            env.Flow = env.Controller.GetComponent<GameFlowComponent>();
            env.Canvas = env.Module.GetComponentInChildren<Canvas>();
            if (env.Canvas == null)
            {
                return env;
            }

            Transform root = env.Canvas.transform;
            env.LayerAboveRect = FindLastByName(root, LayerAbove) as RectTransform;
            env.LayerCardsRect = FindLastByName(root, LayerCards) as RectTransform;
            env.LayerEntitiesRect = FindLastByName(root, LayerEntities) as RectTransform;
            env.LayerMaskRect = FindLastByName(root, LayerMask) as RectTransform;

            Transform status = FindLastByName(root, "Status");
            env.StatusText = status != null ? status.GetComponent<Text>() : null;

            env.HandWidgets = CollectLiveHandWidgets(env.Module);

            // 敌人占位实体由 BattleEntityView.Create 建出来，根节点名形如 "Entity_敌人N"；
            // 第 k 个实体矩形对应 Context.Enemies[k]（CardBattleModule.StartBattleInternal 按同一顺序
            // 登记 _enemyTargets）。这里对命名做三级兜底，命名改了也不会让自检失去目标。
            IReadOnlyList<Combatant> enemies = env.Controller.Context != null ? env.Controller.Context.Enemies : null;
            List<RectTransform> ordered = CollectEnemyRectsByOrder(root);
            for (int i = 0; i < 3; i++)
            {
                if (enemies == null || i >= enemies.Count)
                {
                    break;
                }

                RectTransform rect = FindEnemyRect(root, i, ordered);
                if (rect == null || !rect.gameObject.activeInHierarchy)
                {
                    continue;
                }

                EnemyTarget target = new EnemyTarget();
                target.Name = rect.gameObject.name;   // 现场节点的真实名字（命名变了也能在日志里对上）
                target.Rect = rect;
                target.Enemy = enemies[i];
                env.Enemies.Add(target);

                if (env.FirstAliveEnemy < 0 && target.Enemy != null && target.Enemy.IsAlive)
                {
                    env.FirstAliveEnemy = env.Enemies.Count - 1;
                }
            }

            env.Ready = env.HandWidgets.Count >= 2
                        && env.Enemies.Count > 0
                        && env.FirstAliveEnemy >= 0
                        && env.LayerAboveRect != null
                        && env.LayerCardsRect != null
                        && env.LayerEntitiesRect != null;

            return env;
        }

        private static string DescribeEnv(Env env)
        {
            return "模块=" + (env.Module != null ? "有" : "无")
                 + "，控制者=" + (env.Controller != null ? "有" : "无")
                 + "，Canvas=" + (env.Canvas != null ? "有" : "无")
                 + "，手牌 UI=" + env.HandWidgets.Count
                 + "，敌人矩形=" + env.Enemies.Count
                 + "，存活敌人下标=" + env.FirstAliveEnemy
                 + "，Layer_Above=" + (env.LayerAboveRect != null ? "有" : "无")
                 + "，Layer_Cards=" + (env.LayerCardsRect != null ? "有" : "无")
                 + "，Layer_Entities=" + (env.LayerEntitiesRect != null ? "有" : "无");
        }

        private static void CheckEnv(Env env)
        {
            CheckString("运行环境：场景里存在 CardBattleModule", "存在",
                env.Module != null ? "存在" : "未找到（根对象上没有 CardBattleModule）");
            CheckString("运行环境：GameController.Instance 可用（战斗已初始化）", "非空",
                env.Controller != null ? "非空" : "null");
            if (env.Module == null || env.Controller == null)
            {
                return;
            }

            CheckString("运行环境：战斗上下文已建立（Context 非空）", "非空",
                env.Controller.Context != null ? "非空" : "null");
            CheckString("运行环境：Canvas 已建出（BuildUi 跑过）", "非空",
                env.Canvas != null ? "非空" : "null（入场尚未开始或 BuildUi 失败）");
            CheckString("运行环境：找得到 GameFlowComponent（挂在控制器实体上）", "找到",
                env.Flow != null ? "找到" : "未找到");

            if (env.Flow != null)
            {
                CheckString("运行环境：当前阶段为 PlayerTurn（出牌 / 合成的唯一合法阶段）", "PlayerTurn",
                    env.Flow.Phase.ToString());
            }

            PlayerTurnComponent turn = PlayerComponent<PlayerTurnComponent>(env);
            CheckIntAtLeast("运行环境：本回合剩余出牌次数 ≥ 2（掷出与融合各需 1 次）",
                2, turn != null ? turn.PlaysRemaining : 0);

            CheckIntAtLeast("运行环境：手牌张数 ≥ 2（融合需要两张材料）", 2, env.Controller.Hand.Count);
            CheckInt("运行环境：手牌 UI（CardWidget）数量与手牌张数一致",
                env.Controller.Hand.Count, env.HandWidgets.Count);
            CheckIntAtLeast("运行环境：存在活着的敌人矩形", 1, env.Enemies.Count);

            CheckLayerContract(env);

            if (env.LayerMaskRect != null)
            {
                Image maskImage = env.LayerMaskRect.GetComponent<Image>();
                CheckBool("图层契约：遮罩不吃射线（raycastTarget == false，否则卡牌拖不动）",
                    maskImage != null && !maskImage.raycastTarget,
                    "raycastTarget == false",
                    maskImage == null ? "(缺少 Image)" : maskImage.raycastTarget.ToString());
            }

            if (env.HandWidgets.Count > 0)
            {
                Image cardImage = env.HandWidgets[0].GetComponent<Image>();
                CheckBool("图层契约：卡面吃射线（raycastTarget == true，否则真鼠标按不到卡）",
                    cardImage != null && cardImage.raycastTarget,
                    "raycastTarget == true",
                    cardImage == null ? "(缺少 Image)" : cardImage.raycastTarget.ToString());
            }
        }

        /// <summary>层级顺序：Background &lt; Entities &lt; Cards &lt; Mask &lt; Above &lt; Overlay（靠后的渲染在上层）。</summary>
        private static void CheckLayerContract(Env env)
        {
            Transform root = env.Canvas != null ? env.Canvas.transform : null;
            if (root == null)
            {
                return;
            }

            string[] names = { LayerBackground, LayerEntities, LayerCards, LayerMask, LayerAbove, LayerOverlay };
            int previous = -1;
            bool ordered = true;
            string detail = string.Empty;

            for (int i = 0; i < names.Length; i++)
            {
                Transform layer = FindLastByName(root, names[i]);
                if (layer == null)
                {
                    Fail("图层契约：" + names[i] + " 存在", "存在", "未找到");
                    ordered = false;
                    continue;
                }

                int index = layer.GetSiblingIndex();
                detail += names[i] + "=" + index + " ";
                if (index <= previous)
                {
                    ordered = false;
                }

                previous = index;
            }

            CheckBool("图层契约：六层顺序为 背景 < 实体 < 卡牌 < 遮罩 < 拖动层 < 结算层",
                ordered,
                "严格递增的兄弟序号",
                detail.Trim());
        }

        // ------------------------------------------------------------------
        // 6. 投影判定（矩形重合）
        // ------------------------------------------------------------------

        private static void CheckOverlapPredicate()
        {
            GameObject goA = null;
            GameObject goB = null;
            try
            {
                goA = CreateTempRect("InteractionTest_RectA", new Vector2(100f, 100f), new Vector2(0f, 0f));
                goB = CreateTempRect("InteractionTest_RectB", new Vector2(100f, 100f), new Vector2(0f, 0f));
                RectTransform a = goA.GetComponent<RectTransform>();
                RectTransform b = goB.GetComponent<RectTransform>();

                // ① 有重合：两张 100×100 错开 50×50 → 重叠面积 2500，判定为重合。
                b.position = new Vector3(50f, 50f, 0f);
                CheckFloat("投影判定：错开 50×50 的两张 100×100 重叠面积", 2500f,
                    BattleUiKit.OverlapArea(a, b), 0.5f);
                CheckBool("投影判定：错开 50×50 时 RectsOverlap == true", BattleUiKit.RectsOverlap(a, b),
                    "true", BattleUiKit.RectsOverlap(a, b).ToString());

                // ② 无重合：完全分开 → 面积 0、不重合。
                b.position = new Vector3(400f, 400f, 0f);
                CheckFloat("投影判定：完全分开的重叠面积", 0f, BattleUiKit.OverlapArea(a, b), 0.5f);
                CheckBool("投影判定：完全分开时 RectsOverlap == false", !BattleUiKit.RectsOverlap(a, b),
                    "false", BattleUiKit.RectsOverlap(a, b).ToString());

                // ③ 边界：仅边缘相接（没有正面积）→ 面积 0、不重合。
                b.position = new Vector3(100f, 0f, 0f);
                CheckFloat("投影判定：仅边缘相接的重叠面积", 0f, BattleUiKit.OverlapArea(a, b), 0.5f);
                CheckBool("投影判定：仅边缘相接时 RectsOverlap == false", !BattleUiKit.RectsOverlap(a, b),
                    "false", BattleUiKit.RectsOverlap(a, b).ToString());

                // ④ 隐藏对象：不参与判定（避免选中隐藏的占位敌人 / 已离场的卡）。
                b.position = new Vector3(50f, 50f, 0f);
                goB.SetActive(false);
                CheckBool("投影判定：B 被隐藏后 RectsOverlap == false", !BattleUiKit.RectsOverlap(a, b),
                    "false", BattleUiKit.RectsOverlap(a, b).ToString());
                goB.SetActive(true);
            }
            finally
            {
                DestroyObject(goA);
                DestroyObject(goB);
            }
        }

        // ------------------------------------------------------------------
        // 1. 抓起
        // ------------------------------------------------------------------

        private static void CheckGrab(Env env)
        {
            CardWidget widget = env.HandWidgets[0];
            RectTransform rect = (RectTransform)widget.transform;

            Vector3 home = rect.position;
            // 故意压在卡面内、但不在正中：这样「抓取偏移」是一个非零向量，才真正检验相对位置不变。
            Vector2 press = new Vector2(home.x + 23f, home.y - 19f);

            PointerEventData eventData = NewPointerEvent(press);
            widget.OnPointerDown(eventData);

            CheckString("抓起：按下后卡牌父节点变为拖动层 " + LayerAbove, LayerAbove, ParentName(rect));
            CheckInt("抓起：拖动中的卡牌是拖动层的最后一个兄弟（渲染在预览目标之上）",
                rect.parent != null ? rect.parent.childCount - 1 : -1, rect.GetSiblingIndex());
            CheckBool("抓起：IsDragging == true", widget.IsDragging, "true", widget.IsDragging.ToString());

            Vector2 delta = new Vector2(120f, 80f);
            eventData.position = press + delta;
            widget.OnDrag(eventData);

            Vector3 expected = new Vector3(home.x + delta.x, home.y + delta.y, home.z);
            CheckFloat("抓起：光标移动 delta=(120,80) 后卡牌位置同向移动同样 delta", 0f,
                Vector3.Distance(rect.position, expected), 0.5f);

            Vector2 offsetBefore = new Vector2(home.x - press.x, home.y - press.y);
            Vector2 offsetAfter = new Vector2(rect.position.x - (press.x + delta.x), rect.position.y - (press.y + delta.y));
            CheckFloat("抓起：相对位置（卡牌 − 光标）在整个拖动过程中保持不变", 0f,
                Vector2.Distance(offsetBefore, offsetAfter), 0.5f);

            // 收尾：把卡牌拖到空白处松手，让手牌回到干净状态，供后面的断言使用。
            BlankSpot blank = FindBlankCenter(env, widget, rect);
            if (!blank.Found)
            {
                Fail("抓起（收尾）：能在画面里找到一块空白投放区", "存在", "13×9 采样点全部与敌人或手牌重合");
                return;
            }

            eventData.position = blank.Center;
            widget.OnDrag(eventData);
            eventData.position = blank.Center;
            widget.OnPointerUp(eventData);

            CheckString("抓起（收尾）：空白处松手后卡牌归位到 " + LayerCards, LayerCards, ParentName(rect));
            CheckBool("抓起（收尾）：松手后 IsDragging == false", !widget.IsDragging, "false", widget.IsDragging.ToString());
        }

        // ------------------------------------------------------------------
        // 3. 融合（顺带覆盖 5. 遮罩层级里「手牌作为预览目标」那一半）
        // ------------------------------------------------------------------

        private static void CheckFusion(Env env)
        {
            IReadOnlyList<CardInstance> hand = env.Controller.Hand;

            int indexA = -1;
            int indexB = -1;
            CardKind product = CardKind.A;
            bool enemySafe = false;

            for (int i = 0; i < hand.Count && !enemySafe; i++)
            {
                for (int j = i + 1; j < hand.Count; j++)
                {
                    CardInstance first = hand[i];
                    CardInstance second = hand[j];
                    if (first == null || second == null)
                    {
                        continue;
                    }

                    CardKind recipe;
                    if (!CardFusionComponent.TryGetRecipe(first.Kind, second.Kind, out recipe))
                    {
                        continue;
                    }

                    CardWidget targetWidget = FindWidgetOf(env, second);
                    if (targetWidget == null)
                    {
                        continue;
                    }

                    // 优先挑一对「第二张牌的位置不与任何敌人矩形重合」的材料，
                    // 否则拖过去时最大重合目标可能是敌人，判定方向会变得不明确。
                    bool clean = !OverlapsAnyEnemy(env, (RectTransform)targetWidget.transform);
                    if (indexA < 0 || (!enemySafe && clean))
                    {
                        indexA = i;
                        indexB = j;
                        product = recipe;
                        enemySafe = clean;
                    }

                    if (enemySafe)
                    {
                        break;
                    }
                }
            }

            if (indexA < 0 || indexB < 0)
            {
                Fail("融合：手牌里存在一对可融合的材料", "至少一对合法配方",
                    "手牌 " + hand.Count + " 张（" + HandText(hand) + "）里找不到合法配方");
                return;
            }

            CardInstance materialA = hand[indexA];
            CardInstance materialB = hand[indexB];
            CardWidget widgetA = FindWidgetOf(env, materialA);
            CardWidget widgetB = FindWidgetOf(env, materialB);
            if (widgetA == null || widgetB == null)
            {
                Fail("融合：两张材料都有对应的手牌 UI", "都有 CardWidget",
                    "A=" + (widgetA != null ? "有" : "无") + "，B=" + (widgetB != null ? "有" : "无"));
                return;
            }

            Pass("融合：找到可融合材料 " + materialA.Kind + " + " + materialB.Kind + " → " + product
                 + (enemySafe ? "（目标位置不与敌人重合，判定方向明确）"
                              : "（注意：目标位置与敌人矩形有重合，最大重合目标可能是敌人）"));
            CheckBool("融合：融合前手牌里本来没有 " + product + "（确认产物是新出现的）",
                !HandContainsKind(hand, product), "手牌里没有 " + product,
                "手牌=" + HandText(hand));

            int handBefore = hand.Count;
            int widgetsBefore = CollectLiveHandWidgets(env.Module).Count;
            bool ultimateMayArrive = UltimateMayArrive(env);
            int playsBeforeFusion = PlaysRemaining(env);

            RectTransform rectA = (RectTransform)widgetA.transform;
            RectTransform rectB = (RectTransform)widgetB.transform;
            Vector3 posA = rectA.position;
            Vector3 posB = rectB.position;

            PointerEventData eventData = NewPointerEvent(new Vector2(posA.x, posA.y));
            widgetA.OnPointerDown(eventData);
            CheckString("融合：抓起后材料卡父节点为 " + LayerAbove, LayerAbove, ParentName(rectA));

            eventData.position = new Vector2(posB.x, posB.y);
            widgetA.OnDrag(eventData);
            CheckString("遮罩层级：拖动中压在另一张手牌上时，该手牌被临时提到 " + LayerAbove,
                LayerAbove, ParentName(rectB));

            eventData.position = new Vector2(posB.x, posB.y);
            widgetA.OnPointerUp(eventData);

            CheckString("遮罩层级：融合松手后预览目标还原到 " + LayerCards, LayerCards, ParentName(rectB));
            CheckBool("融合：两张材料都离开手牌",
                !HandContains(hand, materialA) && !HandContains(hand, materialB),
                "两张都不在手牌",
                "A=" + (HandContains(hand, materialA) ? "仍在手牌" : "已离场")
                + "，B=" + (HandContains(hand, materialB) ? "仍在手牌" : "已离场"));
            CheckBool("融合：产物 " + product + " 出现在手牌", HandContainsKind(hand, product),
                "手牌里有 " + product, "手牌=" + HandText(hand));

            if (!ultimateMayArrive && playsBeforeFusion > 1)
            {
                CheckInt("融合：手牌张数 = 融合前 − 1（消耗 2 张、产出 1 张）", handBefore - 1, hand.Count);
                CheckInt("融合：手牌 UI 数量同步减少 1", widgetsBefore - 1,
                    CollectLiveHandWidgets(env.Module).Count);
            }
            else
            {
                Pass("融合：手牌张数断言放宽（充能将被合成打满，或本回合出牌次数用尽会自动进入下一回合补牌）"
                     + "，实测 " + handBefore + " → " + hand.Count);
            }

            CheckInt("融合：拖动层没有遗留的活对象（预览已还原、两张材料已销毁）", 0, CountLiveInLayerAbove(env));
        }

        // ------------------------------------------------------------------
        // 2. 掷出（顺带覆盖 5. 遮罩层级里「敌人作为预览目标」那一半）
        // ------------------------------------------------------------------

        private static void CheckThrow(Env env)
        {
            IReadOnlyList<CardInstance> hand = env.Controller.Hand;

            CardInstance card = null;
            CardWidget widget = null;
            for (int i = 0; i < hand.Count; i++)
            {
                CardInstance candidate = hand[i];
                if (candidate == null || !HasDamage(candidate))
                {
                    continue;
                }

                CardWidget found = FindWidgetOf(env, candidate);
                if (found != null)
                {
                    card = candidate;
                    widget = found;
                    break;
                }
            }

            if (card == null || widget == null)
            {
                Fail("掷出：手牌里有一张能造成伤害的卡（伤害值 > 0）", "至少一张",
                    "手牌 " + hand.Count + " 张（" + HandText(hand) + "）里没有伤害卡，无法用 HP 变化验证掷出");
                return;
            }

            if (env.FirstAliveEnemy < 0 || env.FirstAliveEnemy >= env.Enemies.Count)
            {
                Fail("掷出：有可用的敌人目标", "存在", "FirstAliveEnemy=" + env.FirstAliveEnemy);
                return;
            }

            EnemyTarget target = env.Enemies[env.FirstAliveEnemy];
            Combatant enemy = target.Enemy;
            RectTransform enemyRect = target.Rect;

            int hpBefore = enemy.CurrentHp;
            int handBefore = hand.Count;
            int widgetsBefore = CollectLiveHandWidgets(env.Module).Count;
            int playsBefore = PlaysRemaining(env);

            RectTransform rect = (RectTransform)widget.transform;
            Vector3 home = rect.position;
            Vector2 enemyCenter = RectCenter(enemyRect);

            PointerEventData eventData = NewPointerEvent(new Vector2(home.x, home.y));
            widget.OnPointerDown(eventData);
            CheckString("掷出：抓起后卡牌父节点为 " + LayerAbove, LayerAbove, ParentName(rect));

            eventData.position = enemyCenter;
            widget.OnDrag(eventData);
            CheckString("遮罩层级：拖动中压在敌人身上时，敌人矩形被临时提到 " + LayerAbove,
                LayerAbove, ParentName(enemyRect));

            eventData.position = enemyCenter;
            widget.OnPointerUp(eventData);

            CheckString("遮罩层级：掷出松手后敌人还原到 " + LayerEntities, LayerEntities, ParentName(enemyRect));
            CheckBool("掷出：拖到的敌人 HP 下降（" + EnemyName(enemy) + " / 节点 " + target.Name
                      + " —— 判定落在手上拖的那只身上）",
                enemy.CurrentHp < hpBefore,
                "小于 " + hpBefore, enemy.CurrentHp.ToString());

            int damage = hpBefore - enemy.CurrentHp;
            CardDefinitionData def = card.Identity != null ? card.Identity.definition : null;
            if (def != null)
            {
                CheckBool("掷出：本次伤害在 0 与卡面伤害 " + def.damage + " 之间",
                    damage > 0 && damage <= def.damage,
                    "0 < 伤害 ≤ " + def.damage, damage.ToString());
            }

            CheckBool("掷出：该卡从手牌消失（CardInstance 已离开手牌）", !HandContains(hand, card),
                "已离开手牌", HandContains(hand, card) ? "仍在手牌" : "已离开手牌");

            if (playsBefore > 1)
            {
                CheckInt("掷出：消耗 1 次出牌次数", playsBefore - 1, PlaysRemaining(env));
                CheckInt("掷出：手牌张数 = 掷出前 − 1", handBefore - 1, hand.Count);
                CheckInt("掷出：手牌 UI 数量减少 1", widgetsBefore - 1,
                    CollectLiveHandWidgets(env.Module).Count);
            }
            else
            {
                Pass("掷出：手牌 / 出牌次数断言放宽（本回合只剩 " + playsBefore
                     + " 次出牌，掷出后流程自动推进到下一回合会重置次数并补牌）");
            }

            if (env.StatusText != null)
            {
                string status = env.StatusText.text;
                CheckBool("掷出：状态行出现「掷出」提示", status != null && status.Contains("掷出"),
                    "包含「掷出」", status == null ? "(null)" : status);
            }
            else
            {
                Fail("掷出：能在场景里找到状态行 Text（复核「掷出」提示）", "存在", "未找到名为 Status 的 Text");
            }

            CheckInt("掷出：拖动层没有遗留的活对象（敌人已还原、卡牌已销毁）", 0, CountLiveInLayerAbove(env));
        }

        // ------------------------------------------------------------------
        // 4. 无交互
        // ------------------------------------------------------------------

        private static void CheckBlankDrop(Env env)
        {
            List<CardWidget> widgets = CollectLiveHandWidgets(env.Module);
            if (widgets.Count == 0)
            {
                Fail("无交互：还有手牌可以做空白投放", "至少 1 张", "0 张");
                return;
            }

            CardWidget widget = widgets[0];
            RectTransform rect = (RectTransform)widget.transform;
            Vector3 home = rect.position;

            int handBefore = env.Controller.Hand.Count;
            int playsBefore = PlaysRemaining(env);

            PointerEventData eventData = NewPointerEvent(new Vector2(home.x, home.y));
            widget.OnPointerDown(eventData);

            BlankSpot blank = FindBlankCenter(env, widget, rect);
            if (!blank.Found)
            {
                Fail("无交互：能在画面里找到一块空白投放区", "存在", "13×9 采样点全部与敌人或手牌重合");
                eventData.position = new Vector2(home.x, home.y);
                widget.OnPointerUp(eventData);
                return;
            }

            eventData.position = blank.Center;
            widget.OnDrag(eventData);
            // 注意：拖动期间「被抓起的这张卡」本来就该待在 Layer_Above（需求原文：
            // 「除了抓起卡牌以外的所有东西都置于黑色遮罩下方」），所以这里要把它排除掉，
            // 断言的是「没有把任何**别的**目标提上来」。
            CheckInt("无交互：拖到空白处时没有任何目标被提到 " + LayerAbove + "（被抓起的卡本身除外）",
                0, CountLiveInLayerAboveOtherThan(env, widget));

            eventData.position = blank.Center;
            widget.OnPointerUp(eventData);

            CheckString("无交互：空白处松手后卡牌回到 " + LayerCards, LayerCards, ParentName(rect));
            CheckInt("无交互：手牌张数不变", handBefore, env.Controller.Hand.Count);
            CheckInt("无交互：手牌 UI 数量不变", widgets.Count, CollectLiveHandWidgets(env.Module).Count);
            CheckInt("无交互：不消耗出牌次数", playsBefore, PlaysRemaining(env));
            CheckInt("无交互：拖动层没有残留的活对象", 0, CountLiveInLayerAbove(env));
        }

        // ------------------------------------------------------------------
        // 空白投放点
        // ------------------------------------------------------------------

        private struct BlankSpot
        {
            public bool Found;
            public Vector2 Center;
        }

        /// <summary>
        /// 在画布内按 13×9 采样找一块「卡牌放上去不会与任何敌人 / 其它手牌重合」的空白区，
        /// 优先取画面上方；找不到就返回 Found = false（而不是硬编码一个像素坐标）。
        /// </summary>
        private static BlankSpot FindBlankCenter(Env env, CardWidget dragged, RectTransform draggedRect)
        {
            BlankSpot spot = new BlankSpot();
            spot.Center = new Vector2(Screen.width * 0.5f, Screen.height * 0.8f);

            if (env.Canvas == null || draggedRect == null)
            {
                return spot;
            }

            Rect area = env.CanvasWorldRect;
            Vector2 size = BattleUiKit.ScreenRect(draggedRect).size;
            if (size.x < 1f || size.y < 1f)
            {
                size = CardWidget.CardSize;
            }

            List<Rect> obstacles = new List<Rect>();
            for (int i = 0; i < env.Enemies.Count; i++)
            {
                RectTransform enemy = env.Enemies[i].Rect;
                if (enemy != null && enemy.gameObject.activeInHierarchy)
                {
                    obstacles.Add(BattleUiKit.ScreenRect(enemy));
                }
            }

            List<CardWidget> others = CollectLiveHandWidgets(env.Module);
            for (int i = 0; i < others.Count; i++)
            {
                if (others[i] == null || others[i] == dragged)
                {
                    continue;
                }

                obstacles.Add(BattleUiKit.ScreenRect((RectTransform)others[i].transform));
            }

            const int cols = 13;
            const int rows = 9;
            float xMin = area.xMin + size.x * 0.5f + 8f;
            float xMax = area.xMax - size.x * 0.5f - 8f;
            float yMin = area.yMin + size.y * 0.5f + 8f;
            float yMax = area.yMax - size.y * 0.5f - 8f;
            if (xMax <= xMin || yMax <= yMin)
            {
                return spot;
            }

            for (int row = rows - 1; row >= 0; row--)
            {
                for (int col = 0; col < cols; col++)
                {
                    float cx = Mathf.Lerp(xMin, xMax, (col + 0.5f) / cols);
                    float cy = Mathf.Lerp(yMin, yMax, (row + 0.5f) / rows);
                    Rect candidate = new Rect(cx - size.x * 0.5f, cy - size.y * 0.5f, size.x, size.y);

                    if (candidate.xMin < area.xMin || candidate.xMax > area.xMax
                        || candidate.yMin < area.yMin || candidate.yMax > area.yMax)
                    {
                        continue;
                    }

                    bool clear = true;
                    for (int i = 0; i < obstacles.Count; i++)
                    {
                        if (candidate.Overlaps(obstacles[i]))
                        {
                            clear = false;
                            break;
                        }
                    }

                    if (clear)
                    {
                        spot.Found = true;
                        spot.Center = new Vector2(cx, cy);
                        return spot;
                    }
                }
            }

            return spot;
        }

        private static bool OverlapsAnyEnemy(Env env, RectTransform rect)
        {
            if (rect == null)
            {
                return true;
            }

            Rect self = BattleUiKit.ScreenRect(rect);
            for (int i = 0; i < env.Enemies.Count; i++)
            {
                RectTransform enemy = env.Enemies[i].Rect;
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (self.Overlaps(BattleUiKit.ScreenRect(enemy)))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // 层级 / 状态工具
        // ------------------------------------------------------------------

        /// <summary>Layer_Above 下还「活着」的交互对象数量：活着的敌人矩形 + 仍属于手牌的卡牌。</summary>
        private static int CountLiveInLayerAbove(Env env)
        {
            if (env.LayerAboveRect == null)
            {
                return 0;
            }

            int count = 0;
            Transform layer = env.LayerAboveRect;
            for (int i = 0; i < layer.childCount; i++)
            {
                if (IsLiveInteractive(env, layer.GetChild(i)))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Layer_Above 下「除被抓起的这张卡以外」还活着的交互对象数量。</summary>
    private static int CountLiveInLayerAboveOtherThan(Env env, CardWidget dragged)
    {
        if (env.LayerAboveRect == null)
        {
            return 0;
        }

        int count = 0;
        Transform layer = env.LayerAboveRect;
        for (int i = 0; i < layer.childCount; i++)
        {
            Transform child = layer.GetChild(i);
            if (dragged != null && child == dragged.transform)
            {
                continue;
            }
            if (IsLiveInteractive(env, child))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsLiveInteractive(Env env, Transform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            for (int i = 0; i < env.Enemies.Count; i++)
            {
                RectTransform enemy = env.Enemies[i].Rect;
                if (enemy == null || (Transform)enemy != candidate)
                {
                    continue;
                }

                return enemy.gameObject.activeInHierarchy;
            }

            CardWidget widget = candidate.GetComponent<CardWidget>();
            if (widget == null || widget.Card == null)
            {
                return false;
            }

            return HandContains(env.Controller.Hand, widget.Card);
        }

        private static CardWidget FindWidgetOf(Env env, CardInstance card)
        {
            List<CardWidget> widgets = CollectLiveHandWidgets(env.Module);
            for (int i = 0; i < widgets.Count; i++)
            {
                if (widgets[i] != null && ReferenceEquals(widgets[i].Card, card))
                {
                    return widgets[i];
                }
            }

            return null;
        }

        private static T PlayerComponent<T>(Env env) where T : Component
        {
            return env.Controller != null && env.Controller.Context != null && env.Controller.Context.Player != null
                ? env.Controller.Context.Player.GetComponent<T>()
                : null;
        }

        private static int PlaysRemaining(Env env)
        {
            PlayerTurnComponent turn = PlayerComponent<PlayerTurnComponent>(env);
            return turn != null ? turn.PlaysRemaining : -1;
        }

        /// <summary>本次合成是否会把充能打满（打满时可能额外发一张大招卡，手牌数断言要相应放宽）。</summary>
        private static bool UltimateMayArrive(Env env)
        {
            PlayerChargeComponent charge = PlayerComponent<PlayerChargeComponent>(env);
            if (charge == null)
            {
                return false;
            }

            return charge.charge + CardFusionComponent.ChargePerFusion >= charge.Threshold;
        }

        private static bool HasDamage(CardInstance card)
        {
            CardDefinitionData def = card != null && card.Identity != null ? card.Identity.definition : null;
            return def != null && def.damage > 0;
        }

        private static bool HandContains(IReadOnlyList<CardInstance> hand, CardInstance card)
        {
            if (hand == null || card == null)
            {
                return false;
            }

            for (int i = 0; i < hand.Count; i++)
            {
                if (ReferenceEquals(hand[i], card))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HandContainsKind(IReadOnlyList<CardInstance> hand, CardKind kind)
        {
            if (hand == null)
            {
                return false;
            }

            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i] != null && hand[i].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static string HandText(IReadOnlyList<CardInstance> hand)
        {
            if (hand == null)
            {
                return string.Empty;
            }

            string text = string.Empty;
            for (int i = 0; i < hand.Count; i++)
            {
                if (i > 0)
                {
                    text += "、";
                }

                text += hand[i] != null ? hand[i].Kind.ToString() : "?";
            }

            return text;
        }

        private static string EnemyName(Combatant enemy)
        {
            if (enemy == null)
            {
                return "?";
            }

            return string.IsNullOrEmpty(enemy.displayName) ? enemy.name : enemy.displayName;
        }

        // ------------------------------------------------------------------
        // 事件 / 场景工具
        // ------------------------------------------------------------------

        private static PointerEventData NewPointerEvent(Vector2 position)
        {
            return NewPointerEvent(position, PointerEventData.InputButton.Left);
        }

        private static PointerEventData NewPointerEvent(Vector2 position, PointerEventData.InputButton button)
        {
            PointerEventData eventData = new PointerEventData(EventSystem.current);
            eventData.button = button;
            eventData.position = position;
            return eventData;
        }

        private static string ParentName(Transform target)
        {
            if (target == null)
            {
                return "(对象为空)";
            }

            return target.parent != null ? target.parent.name : "(无父节点)";
        }

        private static Vector2 RectCenter(RectTransform rect)
        {
            Rect r = BattleUiKit.ScreenRect(rect);
            return new Vector2(r.center.x, r.center.y);
        }

        /// <summary>敌人占位实体的节点名前缀（BattleEntityView.Create("敌人N") → "Entity_敌人N"）。</summary>
        private const string EnemyViewNamePrefix = "Entity_敌人";

        /// <summary>
        /// 取第 index 个敌人的实体矩形：先按当前命名找，再按旧命名找，最后按创建顺序兜底。
        /// 三级兜底是为了让自检不绑死在某个版本的节点命名上。
        /// </summary>
        private static RectTransform FindEnemyRect(Transform root, int index, List<RectTransform> orderedFallback)
        {
            RectTransform rect = FindLastByName(root, EnemyViewNamePrefix + (index + 1)) as RectTransform;
            if (rect != null)
            {
                return rect;
            }

            rect = FindLastByName(root, "Enemy" + (index + 1)) as RectTransform;
            if (rect != null)
            {
                return rect;
            }

            if (orderedFallback != null && index >= 0 && index < orderedFallback.Count)
            {
                return orderedFallback[index];
            }

            return null;
        }

        /// <summary>
        /// 兜底顺序：Layer_Entities 下按孩子顺序排列的 BattleEntityView 矩形，跳过最先创建的玩家视图
        /// （CardBattleModule 先 BuildPlayerView 再 BuildEnemyViews，顺序与它自己的 _enemyRects 一致）。
        /// </summary>
        private static List<RectTransform> CollectEnemyRectsByOrder(Transform root)
        {
            List<RectTransform> result = new List<RectTransform>();
            Transform layer = FindLastByName(root, LayerEntities);
            if (layer == null)
            {
                return result;
            }

            bool playerSkipped = false;
            for (int i = 0; i < layer.childCount; i++)
            {
                BattleEntityView view = layer.GetChild(i).GetComponent<BattleEntityView>();
                if (view == null || view.Rect == null)
                {
                    continue;
                }

                if (!playerSkipped)
                {
                    playerSkipped = true;
                    continue;
                }

                result.Add(view.Rect);
            }

            return result;
        }

        /// <summary>按名字做深度优先查找，返回**最后**一个匹配项（同名重复时取最新建出来的那套图层）。</summary>
        private static Transform FindLastByName(Transform root, string name)        {
            if (root == null)
            {
                return null;
            }

            Transform found = root.name == name ? root : null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindLastByName(root.GetChild(i), name);
                if (hit != null)
                {
                    found = hit;
                }
            }

            return found;
        }

        private static CardBattleModule FindBattleModule()
        {
            Scene scene = SceneManager.GetActiveScene();
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

                CardBattleModule found = roots[i].GetComponent<CardBattleModule>();
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject CreateTempRect(string name, Vector2 size, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.position = new Vector3(position.x, position.y, 0f);
            return go;
        }

        private static void DestroyObject(GameObject go)
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
        // 断言累积（PASS/FAIL 逐条，失败项同样收进返回值）
        // ------------------------------------------------------------------

        private static System.Collections.Generic.List<string> Finish()
        {
            LastCheckCount = _checkCount;
            LastLines = new List<string>(Lines);
            return new List<string>(FailureList);
        }

        private static void Pass(string what)
        {
            _checkCount++;
            Lines.Add("PASS: " + what);
        }

        private static void Fail(string what, string expected, string actual)
        {
            _checkCount++;
            string line = "FAIL: " + what + "（期望 " + expected + "，实际 " + actual + "）";
            Lines.Add(line);
            FailureList.Add(line);
        }

        private static void CheckBool(string what, bool condition, string expected, string actual)
        {
            if (condition)
            {
                Pass(what + " [实际 " + actual + "]");
            }
            else
            {
                Fail(what, expected, actual);
            }
        }

        private static void CheckString(string what, string expected, string actual)
        {
            if (expected == actual)
            {
                Pass(what + " = " + actual);
            }
            else
            {
                Fail(what, expected, actual);
            }
        }

        private static void CheckInt(string what, int expected, int actual)
        {
            if (expected == actual)
            {
                Pass(what + " = " + actual);
            }
            else
            {
                Fail(what, expected.ToString(), actual.ToString());
            }
        }

        private static void CheckIntAtLeast(string what, int expected, int actual)
        {
            if (actual >= expected)
            {
                Pass(what + " = " + actual);
            }
            else
            {
                Fail(what, "≥ " + expected, actual.ToString());
            }
        }

        private static void CheckFloat(string what, float expected, float actual, float tolerance)
        {
            if (Mathf.Abs(expected - actual) <= tolerance)
            {
                Pass(what + " [偏差 " + Mathf.Abs(expected - actual).ToString("0.####") + "]");
            }
            else
            {
                Fail(what, expected.ToString("0.####") + "（容差 " + tolerance.ToString("0.####") + "）",
                    actual.ToString("0.####"));
            }
        }
    }

    /// <summary>
    /// Play 模式下的自检运行器：等入场时序走完 → 跑 <see cref="CardInteractionTests.RunAll"/> →
    /// 把 PASS/FAIL 逐条打进控制台，最后输出一行
    /// `CardInteractionTests: ALL PASS (N checks)` 或 `CardInteractionTests: FAIL (N failures)`。
    ///
    /// 为什么必须由运行器驱动：入场时序是协程，编辑模式不推进；而 RunAll() 自身是同步的，
    /// 一帧之内把所有指针事件打完，避免滑入动画的协程在两帧之间挪动卡牌造成误判。
    ///
    /// 由菜单 `CardGame/运行 UI 交互自检` 创建（不保存进场景）。跑完自行销毁，并按
    /// <see cref="cleanupOnFinish"/> 清掉本次运行新建的场景根对象（含 Launch 建出来的战斗对象）。
    /// </summary>
    public class CardInteractionTestRunner : MonoBehaviour
    {
        /// <summary>跑完后销毁本次运行新建的场景根对象（自建对象自行销毁，不留垃圾）。</summary>
        public bool cleanupOnFinish = true;

        /// <summary>等待入场时序（含手牌位置稳定）的上限，秒。</summary>
        public float waitTimeoutSeconds = 30f;

        private IEnumerator Start()
        {
            List<GameObject> rootsBefore = SnapshotRoots();

            CardBattleModule module = CardInteractionTests.EnsureBattleForTests();
            if (module == null)
            {
                Debug.LogError("FAIL: 运行环境：无法启动卡牌战斗（CardBattleModule.Launch 未返回模块）");
                Debug.LogError("CardInteractionTests: FAIL (1 failures)");
                Finish(rootsBefore);
                yield break;
            }

            float expected = CardInteractionTests.EntrySequenceSeconds(module);
            float elapsed = 0f;
            int stableFrames = 0;
            Vector3[] last = new Vector3[0];

            while (elapsed < waitTimeoutSeconds)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                if (elapsed < expected)
                {
                    continue;
                }

                if (!CardInteractionTests.HasLiveHand(module))
                {
                    stableFrames = 0;
                    continue;
                }

                List<CardWidget> widgets = CardInteractionTests.CollectLiveHandWidgets(module);
                Vector3[] now = new Vector3[widgets.Count];
                for (int i = 0; i < widgets.Count; i++)
                {
                    now[i] = widgets[i] != null ? widgets[i].transform.position : Vector3.zero;
                }

                if (SamePositions(last, now))
                {
                    stableFrames++;
                }
                else
                {
                    stableFrames = 0;
                    last = now;
                }

                if (stableFrames >= 4)
                {
                    break;
                }
            }

            // 手动驱动三个鼠标事件，全部走交付代码的公开入口。
            List<string> failures = CardInteractionTests.RunAll();

            List<string> lines = CardInteractionTests.LastLines;
            for (int i = 0; i < lines.Count; i++)
            {
                Debug.Log("[CardInteractionTests] " + lines[i]);
            }

            for (int i = 0; i < failures.Count; i++)
            {
                Debug.LogError("[CardInteractionTests] " + failures[i]);
            }

            if (failures.Count == 0)
            {
                Debug.Log("CardInteractionTests: ALL PASS (" + CardInteractionTests.LastCheckCount + " checks)");
            }
            else
            {
                Debug.LogError("CardInteractionTests: FAIL (" + failures.Count + " failures)"
                               + " —— 共 " + CardInteractionTests.LastCheckCount + " 项检查");
            }

            // 附加探针：右键是否会误触发拖动（单独一类，**不计入**上面的 ALL PASS / FAIL 计数）。
            // 它红了是交付代码的真缺陷，不该污染左键主链的结论。
            List<string> probeFailures = CardInteractionTests.RunRightButtonProbe();
            List<string> probeLines = CardInteractionTests.LastProbeLines;
            for (int i = 0; i < probeLines.Count; i++)
            {
                Debug.Log("[CardInteractionTests/button-guard] " + probeLines[i]);
            }

            if (CardInteractionTests.LastProbeChecks == 0)
            {
                Debug.LogWarning("CardInteractionTests/button-guard: SKIPPED（现场不完整）");
            }
            else if (probeFailures.Count == 0)
            {
                Debug.Log("CardInteractionTests/button-guard: PASS ("
                          + CardInteractionTests.LastProbeChecks + " checks)");
            }
            else
            {
                Debug.LogError("CardInteractionTests/button-guard: FAIL (" + probeFailures.Count + "/"
                               + CardInteractionTests.LastProbeChecks + " checks)"
                               + " —— 与上面的 ALL PASS 计数无关：交付代码 CardWidget 未判断 eventData.button");
            }

            // 战后观察：只打印事实（用公开 API ResolveVictory() 结束战斗），不判 PASS/FAIL。
            // 它必须放在最后 —— 这一段会不可逆地结束战斗。
            List<string> observation = CardInteractionTests.RunPostBattleObservation();
            for (int i = 0; i < observation.Count; i++)
            {
                Debug.Log("[CardInteractionTests/post-battle] " + observation[i]);
            }

            Debug.Log("CardInteractionTests/post-battle: 观察结束（只报事实，不参与 PASS/FAIL 计数）");

            Finish(rootsBefore);
        }

        private void Finish(List<GameObject> rootsBefore)
        {
            if (cleanupOnFinish)
            {
                CleanupNewRoots(rootsBefore);
            }

            Destroy(gameObject);
        }

        private static List<GameObject> SnapshotRoots()
        {
            List<GameObject> roots = new List<GameObject>();
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return roots;
            }

            GameObject[] all = scene.GetRootGameObjects();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                {
                    roots.Add(all[i]);
                }
            }

            return roots;
        }

        /// <summary>销毁本次运行新建的根对象（场景里原本就有的对象一律不动）。</summary>
        private void CleanupNewRoots(List<GameObject> rootsBefore)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return;
            }

            int destroyed = 0;
            GameObject[] all = scene.GetRootGameObjects();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null || go == gameObject || rootsBefore.Contains(go))
                {
                    continue;
                }

                Object.Destroy(go);
                destroyed++;
            }

            if (destroyed > 0)
            {
                Debug.Log("[CardInteractionTests] 清理：已销毁本次运行新建的场景根对象 " + destroyed + " 个。");
            }
        }

        private static bool SamePositions(Vector3[] a, Vector3[] b)
        {
            if (a == null || b == null || a.Length != b.Length || b.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if ((a[i] - b[i]).sqrMagnitude > 0.01f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
