using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI;

namespace HundunWorld.Game.Selection
{
    /// <summary>
    /// 世界对象选中服务：鼠标点选可拾取物品 / 可击杀怪物 / NPC / 远程玩家 / 采集点，
    /// Shift+Tab 在同类候选间循环，并在选中目标脚下画光圈。
    /// <para>
    /// <b>选中是粘住的</b>：一旦选定，只有"玩家改选别的对象"或"该实体自身消失"（离开 AOI 收到
    /// Despawn、Actor 被销毁）才会改变；点空地、打攻击、走远都不会把目标清掉。
    /// 距离超出 <see cref="MaxSelectDistance"/> 只影响"还能不能点到它"。
    /// </para>
    /// <para>
    /// <b>点选判据 = 投影轮廓包含</b>（<see cref="PickSelectableAtCursor"/>）：光标落在谁身上就选谁，
    /// 落在轮廓之外什么都不选。两个方向都踩过坑，别再走回头路：
    /// <list type="bullet">
    ///   <item>纯"离光标最近的固定像素半径"会让命中范围远大于角色本身——点角色旁边的空地也选中角色，
    ///   玩家觉得"点不准"；</item>
    ///   <item>纯射线命中在本工程恒空——角色 Prefab 里只有一个无碰撞体的 StaticModel，
    ///   场景里可命中的 Collider 一个也没有，表现为"谁都选不中"。</item>
    /// </list>
    /// 现在精度来自目标自身体型（纵向头到脚、横向身体半宽随透视缩放），不依赖任何碰撞体。
    /// <see cref="CandidateStackRadius"/> 仍用于候选列表收集重叠目标，它不决定谁被选中，所以放宽是安全的。
    /// </para>
    /// <para>
    /// <b>与 <see cref="Combat.TargetSelectionSystem"/> 的分工</b>：本服务是"玩家选中了什么"的事实源；
    /// 选中怪物时把该 Actor 推给 TargetSelectionSystem 作为技能目标，避免仓库里出现两套目标。
    /// Tab 键归第三人称相机（LockOn）不动，目标循环用 Shift+Tab。
    /// </para>
    /// </summary>
    public class WorldSelectionService : Script
    {
        public static WorldSelectionService Instance { get; private set; }

        // 拾取回执与"窗口到期没拿完"的提示：连接线程只置标记，OnUpdate 在主线程弹。 CORPSE-LOOT-PATCH
        private static Horizon.Game.Message.Sync.CorpseLootTakeResultPacket _pendingLootResult;
        private static int _pendingLootExpired;

        /// <summary>连接线程调用：一条拾取回执到达（取到或没取到都来，理由在 Status 里）。</summary>
        public static void NotifyCorpseLootResult(Horizon.Game.Message.Sync.CorpseLootTakeResultPacket packet)
            => System.Threading.Volatile.Write(ref _pendingLootResult, packet);

        /// <summary>连接线程调用：拾取窗口关了但还有东西没拿（IntValue = 没拿走的件数）。</summary>
        public static void NotifyCorpseLootExpired(int remaining)
            => System.Threading.Interlocked.Exchange(ref _pendingLootExpired, remaining);

        /// <summary>可点选的最大世界距离（厘米；Flax 单位是厘米，1 米 = 100）。</summary>
        [Tooltip("可点选的最大世界距离（厘米）")]
        public float MaxSelectDistance = 6000f;

        /// <summary>
        /// 候选栈的屏幕半径（像素）。**只决定"重叠目标列表里列谁"，不参与"谁被选中"**——
        /// 选中一律由射线精确命中决定，所以这里可以放得比较宽，让挤在一起的怪都能进列表。
        /// </summary>
        [Tooltip("候选列表收集重叠目标的屏幕半径（像素），不影响点选精度")]
        public float CandidateStackRadius = 60f;

        /// <summary>
        /// 点选射线的最大距离（厘米）。第三人称相机可拉到数千米外，必须给足，
        /// 否则"拉远镜头就点不中"（Flax 单位是厘米）。
        /// </summary>
        [Tooltip("点选射线的最大距离（厘米）")]
        public float SelectRayDistance = 100000f;

        /// <summary>
        /// 横向命中范围的下限（像素）。远处目标投影只有几个像素宽，不给下限几乎点不中；
        /// 取 8px 只是为了"够得着"，不构成大范围吸附。
        /// </summary>
        private const float MinPickHalfWidth = 8f;

        /// <summary>选中目标脚下高亮环的颜色。</summary>
        public Color HighlightColor = new Color(1.0f, 0.82f, 0.35f, 0.9f);

        /// <summary>
        /// 脚底光圈的 3D Prefab 路径。留空时用 <see cref="FootRingWidget"/> 的屏幕空间投影环
        /// （工程当前 <c>Content/Materials</c> 里只有 AITestMat 与粒子系统、<c>Content/Textures</c>
        /// 全是背景图，没有任何可用的环形贴图和加成材质，所以 3D 光圈资源需要先补）。
        /// 补上资源后把路径填进来即可，无需改代码；加载失败会静默回落到投影环。
        /// </summary>
        [Tooltip("脚底光圈 3D Prefab 路径（留空则用屏幕空间投影环）")]
        public string RingPrefabPath { get; set; } = string.Empty;

        /// <summary>脚底光圈 Prefab 的 GUID；首次使用时按路径解析并缓存（与 FlaxActorSyncSystem 同一策略）。</summary>
        public Guid RingPrefabGuid { get; set; } = Guid.Empty;

        /// <summary>当前选中实体；null = 无选中。</summary>
        public SelectableEntityTag Selected { get; private set; }

        /// <summary>
        /// 最近一次点选命中的候选栈（同屏重叠目标），由 <see cref="SelectionCandidateList"/> 直接读取渲染；
        /// 长度 ≤ 1 时列表自行收起。
        /// </summary>
        private readonly List<SelectableEntityTag> _pendingStack = new();
        private readonly List<SelectableEntityTag> _scratch = new();
        private readonly List<Actor> _skillTargets = new();
        private readonly SelectionCandidateList _candidateList = new();
        private float _lastSkillSyncTime = -1f;
        private const float ClickDragThresholdPixels = 5f;
        private bool _leftClickPending;
        private bool _leftClickDragged;
        private Float2 _leftClickStartPosition;

        // ---- 脚底光圈 ----
        /// <summary>世界圆环采样段数，需与 <see cref="FootRingWidget"/> 的视觉预期一致。</summary>
        private const int RingSegments = 24;

        private readonly Float2[] _ringPoints = new Float2[RingSegments];
        private readonly Float2[] _ringPulsePoints = new Float2[RingSegments];
        private FootRingWidget _footRing;
        private Actor _ringFollower;
        private Prefab _ringPrefab;
        private string _ringPrefabPathLoaded;
        private bool _ringPrefabLoadWarned;

        public override void OnAwake()
        {
            base.OnAwake();
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            _candidateList.Dispose();
            DestroyRingFollower();
            if (_footRing != null)
            {
                _footRing.Dispose();
                _footRing = null;
            }
            ClearSelection();
            base.OnDestroy();
        }

        public override void OnUpdate()
        {
            // 拾取回执与到期提示（CORPSE-LOOT-PATCH）：连接线程置标记、主线程弹提示。
            var lootResult = System.Threading.Interlocked.Exchange(ref _pendingLootResult, null);
            if (lootResult != null)
            {
                if (lootResult.Status == 1)
                {
                    if (lootResult.Money > 0) UIHelper.ShowToast($"获得 {lootResult.Money} 铜", ToastType.Loot);
                    else UIHelper.ShowToast($"获得物品 {lootResult.ItemId} × {lootResult.Count}", ToastType.Loot);
                }
                else
                {
                    // 拒绝理由各说各的：太远/过期/已取/越界/不是你的——压成一句"拾取失败"等于把归因扔掉。
                    UIHelper.ShowToast(lootResult.Status switch
                    {
                        2 => "已经被取走了",
                        3 => "太晚了，尸体已经不能取",
                        4 => "离得太远",
                        5 => "这一格没有东西",
                        8 => "这不是你打的怪，取不了",
                        9 => "这件要接了对应任务才能拿",
                        _ => "取不了",
                    });
                }
            }
            var expired = System.Threading.Interlocked.Exchange(ref _pendingLootExpired, 0);
            if (expired > 0) UIHelper.ShowToast($"尸体上还有 {expired} 件没拿，已经不能取了");

            ValidateSelected();

            // 技能可选目标列表半秒同步一次：怪物会移动/死亡，但没到需要每帧重算的量级。
            if (_lastSkillSyncTime < 0f || Time.GameTime - _lastSkillSyncTime > 0.5f)
            {
                _lastSkillSyncTime = Time.GameTime;
                SyncSkillTargets();
            }

            var camera = Camera.MainCamera;
            if (camera == null)
            {
                _leftClickPending = false;
                return;
            }

            if (Input.GetMouseButtonDown(MouseButton.Left))
            {
                _leftClickPending = !IsCursorBlockedByUi() && !Input.GetKey(KeyboardKeys.Alt);
                _leftClickDragged = false;
                _leftClickStartPosition = Input.MousePosition;
            }

            if (_leftClickPending && Input.GetMouseButton(MouseButton.Left)
                && (Input.MousePosition - _leftClickStartPosition).Length > ClickDragThresholdPixels)
            {
                _leftClickDragged = true;
            }

            if (Input.GetMouseButtonUp(MouseButton.Left))
            {
                bool isClick = _leftClickPending && !_leftClickDragged && !IsCursorBlockedByUi();
                _leftClickPending = false;
                if (isClick)
                    TrySelectAtCursor(camera);
            }

            // Shift+Tab 正向循环、Ctrl+Shift+Tab 反向；Tab 单键留给相机 LockOn，不在此抢。
            if (Input.GetKey(KeyboardKeys.Shift) && Input.GetKeyDown(KeyboardKeys.Tab))
            {
                bool backward = Input.GetKey(KeyboardKeys.Control);
                CycleAreaCandidates(backward ? -1 : 1);
            }

            // G = 与选中对象交互（对话/触发）。E/Q 归相机锁定切换、F 归攀爬，这里用 G 避开全部既有绑定。
            if (Input.GetKeyDown(KeyboardKeys.G))
                TryInteractSelected();

            // X = 打断、T = 嘲讽。服务端这两条受理口早就有了（各自还带拒因计数），
            // 但客户端一直没有入口，等于玩家打不到住在 DS 世界里的怪——这里补的就是那一格。
            // 键位选自由字母：Tab/Shift+Tab/Ctrl+Shift+Tab 归相机与目标循环，Alpha0..9 归技能条十槽。
            // 与顶部目标框上那两个按钮调**同一对方法**，判据只有一份（MonsterCombatActionRules）。
            if (Input.GetKeyDown(KeyboardKeys.X))
                TryInterruptSelected();
            if (Input.GetKeyDown(KeyboardKeys.T))
                TryTauntSelected();

            // H = 普通攻击（对选中目标挥一次）。客户端此前**没有**基本攻击入口：
            // `CombatReporter.ReportAttack` 全仓零调用方，玩家只能靠技能栏；技能栏空/全冷却时
            // 表现就是"怎么按都没反应"。服务端仍是权威（射程、目标死活、回位免疫都由它判）。
            // **刻意不用 R**：R 是第三人称相机的复位键（`ThirdPersonCamera.ResetKey`），
            // 同键会在挥刀的同时把镜头拽回去——键位不许抢别人的，这条在契约用例里有对照。
            if (Input.GetKeyDown(KeyboardKeys.H))
                TryBasicAttackSelected();

            // 交互裁决回包在连接线程置标志，提示必须在这一帧的主线程发（与任务追踪同一口径）。
            if (Network.QuestClientService.Instance.TryTakeInteractionResult(out var iOk, out var iReason, out var iAdvanced))
                ShowInteractionToast(iOk, iReason, iAdvanced);

            // 自测驱动（marker：%APPDATA%/HundunWorld/monster-accept.on）：没有键盘注入时，
            // 由它自己选中最近的怪 → 普攻 → 尸体可拾取时按 G 那一套。只写日志与调用**玩家同一个入口**，
            // 不绕过任何判据；标记不在时这一句什么都不做。
            TickAcceptDriver();

            DrawHighlight(camera);
            _candidateList.Tick();
        }

        /// <summary>
        /// 与当前选中对象交互：向服务端发一次裁决请求。返回 false = 现在没什么可交互。
        /// <para>
        /// <b>客户端只出 <see cref="SelectableEntityTag.EntityId"/></b>：它是不是真的 NPC、
        /// 玩家够不够近、该推进哪条任务目标，全部由服务端按 DS 上行的实体身份注册表判定
        /// （<c>docs/QUEST_SYSTEM_DESIGN.md</c> §11）。本地 <c>Kind</c> 只决定"要不要发这一枪"，
        /// 绝不参与完成判定——网络种类与 DS 种类是两套来源，判据只能有一份。
        /// </para>
        /// </summary>
        public bool TryInteractSelected()
        {
            var tag = Selected;
            // 判存活要用 ReferenceEquals：Actor 被 Destroy 后 Flax 的 == 已视作 null，
            // 用 == 当守卫会漏掉"刚被销毁的选中对象"，这里就多发一枪到服务端去。
            if (ReferenceEquals(tag, null)) return false;
            // 纯场景对象（EntityId=0）没有服务端身份，发出去只会被判 unknown_entity，本地先挡。
            if (tag.EntityId == 0UL) return false;

            // 尸体拾取（CORPSE-LOOT-PATCH）：Kind 仍是 Monster，可拾取与否读快照状态位。
            // 槽号传 -1 = 取第一个还没被取走的：槽序只有权威侧清楚，客户端不跟踪。
            if (tag.Kind == SelectableKind.Monster && tag.CorpseInteractable)
            {
                var self = HundunWorldGame.Instance;
                if (self == null || self.PlayerId == 0UL) return false;
                HundunWorld.Game.Combat.CombatReporter.ReportCorpseLoot(self.PlayerId, tag.EntityId, -1);
                return true;
            }
            // 只有人/物品/采集点有"交互"语义；怪物和玩家的 G 不该被吃掉（将来另有用途）
            if (tag.Kind != SelectableKind.Npc
                && tag.Kind != SelectableKind.Item
                && tag.Kind != SelectableKind.GatherNode) return false;

            if (!Network.QuestClientService.Instance.ReportInteract(tag.EntityId))
            {
                UIHelper.ShowToast("还没连上服务端，稍后再试", ToastType.Warning);
                return false;
            }

            // 服务位一律走"服务端下发优先、目录兜底"的同一入口：目录是导出快照，
            // 运营在编辑器里刚给 NPC 加上的服务只可能体现在服务端位图里。
            bool Serves(uint bit) => NpcStanceCatalog.HasServiceOrFallback(
                tag.NpcServiceFlags, bit, tag.TemplateId, tag.ModelKey);

            // 换目标先清训练师绑定：旧绑定只对"刚才那个 NPC"有效，留着会让下一次学习被服务端按距离拒掉。
            if (tag.Kind == SelectableKind.Npc
                && !Serves(NpcStanceCatalog.ServiceTrainer))
            {
                Network.TrainerClientService.Instance.Clear();
            }

            // 有对话菜单的 NPC：先摆出"能做什么"，由玩家选。
            // 以前按服务位各自开窗，一个既有货架又教艺的 NPC 会连发三次导航、最后打开的那个赢——
            // 玩家看到的是"铁匠只能买东西"，其余入口没有任何提示。
            if (tag.Kind == SelectableKind.Npc)
            {
                var menu = NpcStanceCatalog.GetGossipOptions(tag.TemplateId);
                if (menu.Length > 0)
                {
                    var attrs = HundunWorldGame.Instance?.LocalPlayerActor?
                        .GetScript<CharacterAttributesComponent>();
                    NpcGossipService.Begin(tag.EntityId, tag.TemplateId,
                        string.IsNullOrEmpty(tag.DisplayName) ? "交谈" : tag.DisplayName,
                        attrs != null ? attrs.Level : 0, menu);
                    HundunWorld.Game.UI.MainUIManager.Instance?.NavigateToInkPage(
                        HundunWorld.Game.UI.Ink.InkPageDomIds.NavNpcGossip);
                    return true;
                }
            }

            // 没有菜单的 NPC（以及物品/采集点）：维持"直接打开唯一相关页"的旧语义。

            // NPC 是训练师时把技能页按"收费模式"打开（教学行与学费全在服务端）。
            if (tag.Kind == SelectableKind.Npc && Serves(NpcStanceCatalog.ServiceTrainer))
            {
                Network.TrainerClientService.Instance.Bind(tag.EntityId, tag.TemplateId);
                HundunWorld.Game.UI.MainUIManager.Instance?.NavigateToInkPage(
                    HundunWorld.Game.UI.Ink.InkPageDomIds.NavSkillTree);
            }

            // 任务发布者：按**真实发布者实体**拉 offer 清单——头顶 !/? 与"跟这个 NPC 说话能看到什么"
            // 都靠这一份；此前客户端从不带 giverEntityId，服务端算好了也没人问。
            if (tag.Kind == SelectableKind.Npc && Serves(NpcStanceCatalog.ServiceQuestGiver))
            {
                Network.QuestClientService.Instance.RequestList(tag.EntityId);
            }

            // NPC 教配方 / 是制作设施时，拉取"这里能做什么"并打开制作页。
            // 清单由服务端算（训练行 + 设施配方的并集），客户端不维护写死的配方表。
            if (tag.Kind == SelectableKind.Npc
                && (Serves(NpcStanceCatalog.ServiceCraftTrainer)
                    || Serves(NpcStanceCatalog.ServiceCraftStation)))
            {
                Network.CraftClientService.Instance.RequestList(tag.EntityId);
                HundunWorld.Game.UI.MainUIManager.Instance?.NavigateToInkPage(
                    HundunWorld.Game.UI.Ink.InkPageDomIds.NavCrafting);
            }

            // NPC 是商人时顺带开商店（WoW：点商人就开窗口）。
            // 判据用服务端下发的服务位（未下发才退回导出目录），
            // 它只决定"要不要弹窗"；**卖什么、多少钱、成不成交仍由服务端裁决**。
            if (tag.Kind == SelectableKind.Npc && Serves(NpcStanceCatalog.ServiceVendor))
            {
                Network.ShopClientService.Instance.RequestOpen(tag.EntityId);
                HundunWorld.Game.UI.MainUIManager.Instance?.NavigateToInkPage(
                    HundunWorld.Game.UI.Ink.InkPageDomIds.NavSocialShop);
            }

            return true;
        }

        /// <summary>
        /// 嘲讽当前选中的怪：向 DS 世界发一次 <c>Taunt</c> 动作。返回 false = 现在没什么可嘲讽
        /// （并把原因念成一句提示，而不是"按下去什么都没发生"）。
        /// <para>客户端<b>什么都不改</b>：换目标这件事由服务端在下一拍 AI 里落进快照下发。
        /// 自己提前把怪"拽过来"，会让"拉住了"与"没拉住"长得一模一样。</para>
        /// </summary>
        public bool TryTauntSelected()
        {
            var tag = Selected;
            // 判存活要用 ReferenceEquals：Actor 被 Destroy 后 Flax 的 == 已视作 null。
            var has = !ReferenceEquals(tag, null) && tag.EntityId != 0UL;
            var isMonster = has && tag.Kind == SelectableKind.Monster;
            var selectable = has && tag.Selectable;
            var unattackable = has && tag.Unattackable;   // 服务端下发的"此刻打不到"（正在回位）

            if (!Combat.MonsterCombatActionRules.CanTaunt(has, isMonster, selectable, unattackable))
            {
                UIHelper.ShowToast(Combat.MonsterCombatActionRules.TauntHint(has, isMonster, selectable, unattackable), ToastType.Warning);
                return false;
            }

            Combat.CombatReporter.ReportTauntById(tag.EntityId);
            return true;
        }

        /// <summary>
        /// 打断当前选中的怪：发一次 <c>Interrupt</c> 动作。返回 false = 现在踢不着（念出原因）。
        /// <para>"踢断了"的确认只看随后那条 <c>MonsterCastStop</c> 事件与收掉的条子，
        /// 客户端不许自己把条子抹掉——否则"没踢动"和"踢动了"在屏幕上一模一样。</para>
        /// </summary>
        public bool TryInterruptSelected()
        {
            var tag = Selected;
            var has = !ReferenceEquals(tag, null) && tag.EntityId != 0UL;
            var isMonster = has && tag.Kind == SelectableKind.Monster;
            var selectable = has && tag.Selectable;
            var unattackable = has && tag.Unattackable;
            var casting = has && tag.IsCasting;
            var interruptible = has && tag.CastInterruptible;

            if (!Combat.MonsterCombatActionRules.CanInterrupt(has, isMonster, selectable, casting, interruptible, unattackable))
            {
                UIHelper.ShowToast(
                    Combat.MonsterCombatActionRules.InterruptHint(has, isMonster, selectable, casting, interruptible, unattackable),
                    ToastType.Warning);
                return false;
            }

            Combat.CombatReporter.ReportInterruptById(tag.EntityId);
            return true;
        }

        /// <summary>
        /// 对选中目标挥一次普通攻击（服务端权威结算）。返回 false = 这一手没发出去，且已给出人话原因。
        /// <para>与技能栏两条入口的分工：技能栏走 <c>ReportSkillCastById</c>（有冷却/耗能/读条），
        /// 这里是最朴素的近战一挥——它解决的是"没有技能或技能全在冷却时，玩家完全打不死怪"。</para>
        /// </summary>
        public bool TryBasicAttackSelected()
        {
            var tag = Selected;
            var has = !ReferenceEquals(tag, null) && tag.EntityId != 0UL;
            if (!has)
            {
                UIHelper.ShowToast("先选中一个目标（左键点它，或 Shift+Tab 循环）", ToastType.Warning);
                return false;
            }
            var selectable = tag.Selectable;
            var unattackable = tag.Unattackable;
            if (!Combat.MonsterCombatActionRules.CanDamage(true, selectable, unattackable))
            {
                UIHelper.ShowToast(
                    Combat.MonsterCombatActionRules.DamageHint(true, selectable, unattackable),
                    ToastType.Warning);
                return false;
            }
            Combat.CombatReporter.ReportAttack(tag.Actor);
            return true;
        }

        /// <summary>把服务端裁决翻成一句玩家能懂的话（短码不外露，但要在提示里能分辨"太远"和"没任务"）。</summary>
        private static void ShowInteractionToast(bool ok, string reason, int advanced)
        {
            if (ok)
            {
                UIHelper.ShowToast(advanced > 0
                    ? $"已推进 {advanced} 条任务目标"
                    : "他暂时没有交代给你的事",
                    advanced > 0 ? ToastType.Quest : ToastType.Info);
                return;
            }

            string text = reason switch
            {
                "too_far" => "离得太远，走近些再谈",
                "unknown_entity" => "这里没有可对话的对象了",
                "not_interactable" => "这个对象不能对话",
                "no_position" => "位置还没同步，稍后再试",
                "no_registry" => "服务端未开启对象身份校验",
                _ => "未能交互" + (string.IsNullOrEmpty(reason) ? string.Empty : "：" + reason),
            };
            UIHelper.ShowToast(text, ToastType.Warning);
        }

        /// <summary>取消当前选中（目标消失、外部主动调用）。点空地**不**走这里。</summary>
        public void ClearSelection()
        {
            // 必须用 ReferenceEquals 判"逻辑上有选中"：目标离开 AOI 时 FlaxActorSyncSystem 会
            // Destroy 掉 Actor，此时 Flax 的 == 判定已把 Selected 视作 null，用 == 当守卫会
            // 在这里直接 return，让 Selected 永远悬在已销毁的对象上。
            bool hadSelection = !ReferenceEquals(Selected, null);
            if (!hadSelection && _pendingStack.Count == 0)
                return;

            Selected = null;
            _pendingStack.Clear();
            PushToTargetSelection(null);
            _candidateList.Hide();
        }

        /// <summary>候选列表点击某一条时调用：直接选中该候选。</summary>
        public void SelectFromStack(int index)
        {
            if (index < 0 || index >= _pendingStack.Count)
                return;

            Select(_pendingStack[index]);
        }

        private void TrySelectAtCursor(Camera camera)
        {
            // 菜单页整屏拦截；HUD 页只在光标落到可交互控件上时拦截，
            // 否则点一下透明面板就会把世界点击吞掉（HUD 是全屏容器）。
            if (IsCursorBlockedByUi())
                return;

            var mouse = Input.MousePosition;

            // 谁被选中 = 光标落在其投影轮廓内、且离相机最近的那个实体（判据不依赖碰撞体，见下）。
            // 打空（点在角色轮廓之外）**不取消现有选中**：左键在本作里还兼着攻击与交互，
            // 随手一击就把目标清掉等于逼玩家重新找目标。目标只在两种情况下改变——
            // 玩家改选了别的对象，或当前目标自身消失（ValidateSelected）。
            var hit = PickSelectableAtCursor(camera);
            if (hit == null)
                return;

            Select(hit);

            // 列表内容 = 光标附近（含刚点中的这个）互相重叠的目标，按离光标由近及远排。
            CollectScreenCandidates(camera, new Vector2(mouse.X, mouse.Y), _pendingStack, hit);
            _candidateList.Show(_pendingStack, new Vector2(mouse.X, mouse.Y));
        }

        /// <summary>
        /// 返回光标正指着的那个可选中实体；没有则 null。两级判据：
        /// <para>
        /// <b>① 物理射线（首选）</b>：角色 Prefab 已挂 <c>BodyCollider</c> 胶囊（半径 30、
        /// Height 120，加两端半球共 180cm，中心抬到 Y=90 让身体从脚底立到头顶），射线能精确命中。
        /// 本服务挂在本机玩家 Actor 上，自己的胶囊要视为对选中透明——第三人称相机就在角色后上方，
        /// 屏幕中央那条线经常先扫到自己身上。其余无标记的命中物（墙、道具）是真遮挡，遇到即否。
        /// </para>
        /// <para>
        /// <b>② 投影轮廓（兜底）</b>：纵向头顶→脚底、横向身体半宽沿相机右轴取两点投影的半距。
        /// 留着它是必须的而不是保险：掉落物、采集点这类实体目前没有自己的 Collider，
        /// 只靠射线就会重演"谁都选不中"（本工程曾经整场景只有 NarrativePro 的球形 Trigger，
        /// 而射线必须传 <c>hitTriggers:false</c>，那时射线判据恒空）。
        /// 重叠时取离相机最近的——被挡住的那个不是玩家真正看到并点击的那个。
        /// </para>
        /// </summary>
        private SelectableEntityTag PickSelectableAtCursor(Camera camera)
        {
            var mouse = Input.MousePosition;

            var byRay = PickByRay(camera, mouse);
            if (byRay != null)
                return byRay;

            var camPos = camera.Transform.Translation;
            var camForward = camera.Transform.Forward;

            _scratch.Clear();
            _scratch.AddRange(SelectableEntityTag.Active); // 复制再遍历：选中回调里可能增删

            SelectableEntityTag best = null;
            float bestDist = float.MaxValue;

            for (var i = 0; i < _scratch.Count; i++)
            {
                var tag = _scratch[i];
                if (tag == null || !tag.Selectable)
                    continue;

                var actor = tag.Actor;
                if (actor == null || !actor.IsActive)
                    continue;

                var center = actor.Position;
                float dist = Vector3.Distance(center, camPos);
                if (dist < 1f || dist > MaxSelectDistance)
                    continue;

                // 相机身后的点 ProjectPoint 会给镜像坐标，先按前后判掉。
                if (Vector3.Dot(center - camPos, camForward) <= 0f)
                    continue;

                if (!IsCursorOnBody(camera, tag, mouse))
                    continue;

                // 场景以后补上碰撞体，这条才开始真正生效（挡住的不给选）。
                if (!IsVisibleFromCamera(camera, tag))
                    continue;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = tag;
                }
            }

            return best;
        }

        /// <summary>射线判据：命中标记实体就选它；先打到别的实体（地形/墙/道具）就什么都不选。</summary>
        private SelectableEntityTag PickByRay(Camera camera, Float2 mouse)
        {
            var ray = camera.ConvertMouseToRay(mouse);

            // RayCastAll 不保证按距离有序，必须自己排序，否则判断不了"谁挡在谁前面"。
            if (!Physics.RayCastAll(ray.Position, ray.Direction, out RayCastHit[] hits, SelectRayDistance, hitTriggers: false))
                return null;

            Array.Sort(hits, (a, b) => a.Distance.CompareTo(b.Distance));

            for (var i = 0; i < hits.Length; i++)
            {
                var hitActor = hits[i].Collider != null ? hits[i].Collider.Parent : null;

                var tagged = FindTaggedActor(hitActor);
                if (tagged != null)
                {
                    var tag = tagged.GetScript<SelectableEntityTag>();
                    if (tag != null && tag.Selectable)
                        return tag;

                    continue; // 有标记但此刻不可选（已死/已被采集）：别把它当墙
                }

                if (IsSelfOrDescendant(hitActor))
                    continue; // 自己的胶囊不挡选中

                return null;
            }

            return null;
        }

        /// <summary>
        /// 该 Actor（或其父链）是否挂着选中标记。**只覆盖远程角色/怪物/NPC 等"可选中的对象"**，
        /// 不含本机玩家自己。
        /// <para>
        /// 地面/落点射线**不要**用它做过滤——要用 <see cref="IsCharacterBodyCollider"/>：
        /// 本机玩家与远程角色共用 CharacterRoot.prefab，那颗 BodyCollider 胶囊同样会挡在
        /// 向下射线上，而它没有选中标记，漏掉就会把"无地面(NaN)"变成"地面在头顶 1.8 米"，
        /// 表现为角色悬空（见 <c>HundunWorldGame.SampleGroundHeightEcs</c>）。
        /// </para>
        /// </summary>
        public static bool IsPartOfSelectable(Actor actor) => FindTaggedActor(actor) != null;

        /// <summary>
        /// 该 Actor（或其父链）是否是**某个角色的身体**（而非地形）。地面/落点射线应当用它，
        /// 而不是 <see cref="IsPartOfSelectable"/>。
        /// <para>
        /// 本机玩家与远程角色共用 <c>CharacterRoot.prefab</c>，因此同样带着那颗 180cm 的
        /// <c>BodyCollider</c> 胶囊，但本机玩家**不会**挂 <see cref="SelectableEntityTag"/>
        /// （自己不是可选目标）。只按标记过滤，就会把"自己头顶"当成地面：向下射线唯一命中
        /// 自己的胶囊顶，每 tick 把角色抬高一个身位，一路爬到射线起点高度后悬空抖动。
        /// </para>
        /// </summary>
        /// <param name="actor">命中物的碰撞体宿主。</param>
        /// <param name="localPlayerRoot">
        /// 本机玩家 Actor；传 null 表示只按选中标记判定。由调用方显式传入，
        /// 不走 <c>HundunWorldGame.Instance</c>——那个 getter 会在实例缺失时**延迟创建**游戏实例。
        /// </param>
        public static bool IsCharacterBodyCollider(Actor actor, Actor localPlayerRoot = null)
        {
            // 2026-10-23：不能只认"本机根 Actor 引用"和选中标记。地面采样器的调用线程/时机
            // 早于 _localPlayerActor 赋值时（或角色换了根、挂了不同脚本）引用比较会失配，
            // 自己的胶囊顶就被当成"地面"，每 tick 抬高一个身位（Y 连续上涨）。
            // 改成按**角色脚本**识别：本机/远端角色根上都挂着下列脚本之一，任何子节点都能经父链命中。
            for (var current = actor; current != null; current = current.Parent)
            {
                if (current == localPlayerRoot)
                    return true;
                if (current.GetScript<SelectableEntityTag>() != null)
                    return true;
                if (current.GetScript<PlayerController>() != null)
                    return true;
                if (current.GetScript<Character.CharacterAnimationController>() != null)
                    return true;
                if (current.GetScript<RemotePlayerActor>() != null)
                    return true;
                if (current.GetScript<MetaHuman.Runtime.MetaHumanCharacter>() != null)
                    return true;
            }

            return false;
        }

        /// <summary>从命中对象向上找挂了选中标记的 Actor（碰撞体常挂在角色的子 Actor 上）。</summary>
        private static Actor FindTaggedActor(Actor actor)
        {
            var current = actor;
            while (current != null)
            {
                if (current.GetScript<SelectableEntityTag>() != null)
                    return current;
                current = current.Parent;
            }

            return null;
        }

        /// <summary>该 Actor 是否是本服务所挂载的玩家自身或其子对象。</summary>
        private bool IsSelfOrDescendant(Actor actor)
        {
            var current = actor;
            while (current != null)
            {
                if (current == Actor)
                    return true;
                current = current.Parent;
            }

            return false;
        }

        /// <summary>光标是否落在该实体投影到屏幕上的身体范围内（纵向头到脚、横向身体半宽）。</summary>
        private static bool IsCursorOnBody(Camera camera, SelectableEntityTag tag, Float2 mouse)
        {
            var center = tag.Actor.Position;
            var head = center + Vector3.Up * LabelHeight(tag.Kind);

            camera.ProjectPoint(center, out var footScreen);
            camera.ProjectPoint(head, out var headScreen);

            float top = Mathf.Min(footScreen.Y, headScreen.Y);
            float bottom = Mathf.Max(footScreen.Y, headScreen.Y);
            if (mouse.Y < top || mouse.Y > bottom)
                return false;

            // 身体半宽取脚底圈半径的一半：光圈画在身体外围，用它当横向命中范围会明显偏肥。
            float halfWidthWorld = RingRadiusFor(tag.Kind, tag.Actor) * 0.6f;
            var lateral = camera.Transform.Right * halfWidthWorld;
            camera.ProjectPoint(center + lateral, out var rightScreen);
            camera.ProjectPoint(center - lateral, out var leftScreen);

            float halfWidthPixels = Mathf.Abs(rightScreen.X - leftScreen.X) * 0.5f;
            float centerX = (rightScreen.X + leftScreen.X) * 0.5f;

            // 远处目标投影只有几个像素，不给下限就几乎点不中；下限取得很小，不构成"吸附"。
            return Mathf.Abs(mouse.X - centerX) <= Mathf.Max(halfWidthPixels, MinPickHalfWidth);
        }

        /// <summary>
        /// 收集光标附近的重叠候选（只用于候选列表），按"屏幕距离 → 世界距离"升序，
        /// 并把刚点中的那个挪到首位，保证列表第一项就是玩家选中的目标。
        /// </summary>
        private void CollectScreenCandidates(Camera camera, Vector2 mouse, List<SelectableEntityTag> output,
            SelectableEntityTag primary)
        {
            output.Clear();
            _scratch.Clear();
            _scratch.AddRange(SelectableEntityTag.Active); // 复制再遍历：选中回调里可能增删

            var camPos = camera.Transform.Translation;

            for (var i = 0; i < _scratch.Count; i++)
            {
                var tag = _scratch[i];
                if (tag == null || !tag.Selectable)
                    continue;

                var actor = tag.Actor;
                if (actor == null || !actor.IsActive)
                    continue;

                var head = actor.Position + Vector3.Up * LabelHeight(tag.Kind);
                float dist = Vector3.Distance(head, camPos);
                if (dist > MaxSelectDistance)
                    continue;

                // 背对相机的不投影：ProjectPoint 对相机身后的点仍会返回镜像屏幕坐标。
                if (Vector3.Dot(head - camPos, camera.Transform.Forward) <= 0f)
                    continue;

                // ProjectPoint 无返回值：身后点由上面的 dot 判据拦掉。
                camera.ProjectPoint(head, out var screen);

                float dx = screen.X - mouse.X;
                float dy = screen.Y - mouse.Y;
                float screenDist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (screenDist > CandidateStackRadius)
                    continue;

                if (tag == primary)
                {
                    output.Insert(0, tag);
                    continue;
                }

                // 排序键借用屏幕距离：先近光标，再近相机。
                float key = screenDist + dist * 0.001f;
                int insertAt = output.Count;
                for (var j = 0; j < output.Count; j++)
                {
                    if (SortKeyOf(camera, output[j]) > key)
                    {
                        insertAt = j;
                        break;
                    }
                }
                output.Insert(insertAt, tag);
            }

            // 遮挡剔除只在候选极少时做，避免每次点击对全场实体跑射线。
            for (var i = output.Count - 1; i >= 0; i--)
            {
                if (!IsVisibleFromCamera(camera, output[i]))
                    output.RemoveAt(i);
            }

            // 玩家常点在角色脚上，而这里按"头顶投影离光标多近"筛选，此时刚选中的目标自己可能不合格。
            // 列表第一项必须就是玩家点中的那个，否则列表与选中状态自相矛盾。
            if (primary != null && !output.Contains(primary))
                output.Insert(0, primary);
        }

        private float SortKeyOf(Camera camera, SelectableEntityTag tag)
        {
            if (tag == null || tag.Actor == null)
                return float.MaxValue;

            var camPos = camera.Transform.Translation;
            var head = tag.Actor.Position + Vector3.Up * LabelHeight(tag.Kind);
            camera.ProjectPoint(head, out var screen);
            var mouse = Input.MousePosition;
            float dx = screen.X - mouse.X;
            float dy = screen.Y - mouse.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy) + Vector3.Distance(head, camPos) * 0.001f;
        }

        /// <summary>相机到候选头顶之间是否无阻挡（隔墙不可选）。</summary>
        private bool IsVisibleFromCamera(Camera camera, SelectableEntityTag tag)
        {
            var camPos = camera.Transform.Translation;
            var target = tag.Actor.Position;
            var toTarget = target - camPos;
            float length = toTarget.Length;
            if (length < 1f)
                return true;

            if (!Physics.RayCast(camPos, toTarget / length, out RayCastHit hit, length - 50f, hitTriggers: false))
                return true;

            var owner = hit.Collider != null ? hit.Collider.Parent : null;
            while (owner != null)
            {
                // 候选自身与"我自己的身体"都不算遮挡：相机在角色后上方，朝眼前目标画线常先命中自己胶囊。
                if (owner == tag.Actor || owner == Actor)
                    return true;
                owner = owner.Parent;
            }
            return false;
        }

        /// <summary>
        /// Shift+Tab 循环：在"附近所有与当前选中同类"的实体间轮换。
        /// <para>
        /// 按种类而不是按点选栈循环，是为了满足"选中一只怪后 Tab 在怪之间切"——点选栈只覆盖
        /// 同一屏位置的重叠目标，而怪物群通常分散在几步范围内。
        /// </para>
        /// </summary>
        private void CycleAreaCandidates(int direction)
        {
            var kind = Selected != null ? Selected.Kind : SelectableKind.Monster;
            var playerPos = PlayerAnchorPosition();

            _scratch.Clear();
            _scratch.AddRange(SelectableEntityTag.Active);

            var area = new List<SelectableEntityTag>();
            for (var i = 0; i < _scratch.Count; i++)
            {
                var tag = _scratch[i];
                if (tag == null || !tag.Selectable || tag.Kind != kind)
                    continue;
                if (tag.Actor == null || !tag.Actor.IsActive)
                    continue;
                if (Vector3.Distance(tag.Actor.Position, playerPos) > MaxSelectDistance)
                    continue;
                area.Add(tag);
            }

            if (area.Count == 0)
                return;

            area.Sort((a, b) => Vector3.Distance(a.Actor.Position, playerPos)
                              .CompareTo(Vector3.Distance(b.Actor.Position, playerPos)));

            int current = Selected != null ? area.IndexOf(Selected) : -1;
            int next = current < 0
                ? 0
                : ((current + direction) % area.Count + area.Count) % area.Count;

            _pendingStack.Clear();
            _pendingStack.AddRange(area);

            Select(area[next]);
            // 循环时不弹列表：玩家已经在"逐个点过去"的节奏里，再压一层 UI 只会挡视野。
        }

        private void Select(SelectableEntityTag tag)
        {
            // 候选列表里的条目可能已经随目标离开 AOI 而失效（列表最长停留 4 秒）。
            if (tag == null || tag.Actor == null || !tag.Actor.IsActive)
                return;

            if (tag == Selected)
                return;

            Selected = tag;
            PushToTargetSelection(tag);
        }

        /// <summary>
        /// 把选中的怪物同步给技能目标系统，保持"玩家看到的选中"与"技能打在谁身上"一致，
        /// 并把附近的怪列表作为技能可选目标发布出去（供 AOE/最近目标查询用）。
        /// 非敌对种类（玩家/物品/采集点）不清空既有技能目标——选中玩家不等于放弃手上的怪。
        /// </summary>
        private void PushToTargetSelection(SelectableEntityTag tag)
        {
            var targetSelection = Combat.TargetSelectionSystem.Instance;
            if (targetSelection == null)
                return;

            if (tag != null && tag.Kind == SelectableKind.Monster && tag.Actor != null)
                targetSelection.SetTarget(tag.Actor);
            else if (tag == null)
                targetSelection.DeselectTarget();
        }

        /// <summary>发布"玩家附近所有存活怪物"为技能可选目标（按离玩家距离升序）。</summary>
        private void SyncSkillTargets()
        {
            var targetSelection = Combat.TargetSelectionSystem.Instance;
            if (targetSelection == null)
                return;

            var playerPos = Actor.Position;
            _skillTargets.Clear();

            for (var i = 0; i < SelectableEntityTag.Active.Count; i++)
            {
                var tag = SelectableEntityTag.Active[i];
                if (tag == null || !tag.Selectable || tag.Kind != SelectableKind.Monster || tag.Actor == null)
                    continue;
                if (Vector3.Distance(tag.Actor.Position, playerPos) > MaxSelectDistance)
                    continue;
                _skillTargets.Add(tag.Actor);
            }

            _skillTargets.Sort((a, b) => Vector3.Distance(a.Position, playerPos)
                                      .CompareTo(Vector3.Distance(b.Position, playerPos)));

            targetSelection.SetSelectableTargets(_skillTargets);
        }

        private void ValidateSelected()
        {
            // 选中是"粘住"的：除了玩家改选、点空地，就只有目标实体本身消失才丢目标。
            // 消失 = 离开 AOI 后服务端下发 Despawn、Actor 被销毁，Flax 的 == 判定认得这种对象，
            // 所以先用 ReferenceEquals 区分"本来没选中"与"选中对象已随 Actor 销毁"。
            if (ReferenceEquals(Selected, null))
                return;

            if (Selected == null || Selected.Actor == null || !Selected.Actor.IsActive || !Selected.Selectable)
                ClearSelection();
        }

        // ---------------- 自测驱动（marker 开关；不属于玩法） ----------------
        private static readonly string AcceptMarker = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HundunWorld", "monster-accept.on");
        // 回执**自己写盘**：Flax 的磁盘日志会滞后（内存里有、文件里还没刷），验收脚本读日志会读到旧内容。
        private static readonly string AcceptReceipt = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HundunWorld", "monster-accept.log");
        private double _acceptNextAt;

        /// <summary>
        /// 自测驱动的普攻距离（**世界单位＝米**，与 DS 的 <c>PlayerAttackRange=3</c> 同一套数）：
        /// 留 0.5 米余量。曾经按"厘米"写成 250，结果 65 米外照样开砍、63 刀全落 OutOfRange。
        /// </summary>
        private const float AcceptAttackDistance = 2.5f;

        private static void AcceptLog(string line)
        {
            try { System.IO.File.AppendAllText(AcceptReceipt, line + System.Environment.NewLine); }
            catch { /* 自测回执写不进去不该影响玩法 */ }
        }

        /// <summary>
        /// 自动验收驱动：存在 <c>monster-accept.on</c> 时每秒做一次——
        /// 选中最近的怪；活怪就调 <see cref="TryBasicAttackSelected"/>（与按 H 同一入口），
        /// 尸体可拾取就调 <see cref="TryInteractSelected"/>（与按 G 同一入口）。
        /// 每一步都打一行 <c>[MonsterAccept]</c>：验收脚本按这行判"到底有没有贴身怪、动作发没发出去"。
        /// </summary>
        private void TickAcceptDriver()
        {
            if (!System.IO.File.Exists(AcceptMarker)) return;
            if (Time.GameTime < _acceptNextAt) return;
            // 1.25 秒：DS 的 `MinCombatActionIntervalTicks=60`（60Hz ⇒ 恰好 1 秒）会吃掉贴着边界的动作，
            // 自测驱动刻意留出余量，免得把"限频"误读成"攻击没接线"。
            _acceptNextAt = Time.GameTime + 1.25;

            var playerPos = PlayerAnchorPosition();
            // 选法（自测专用）：12 米内**血量最低**的那只优先——测试怪被刻意调成 hp30，
            // 而场景里可能有 NPC/旧实体（hp 更高、DS 也不认），按"最近"会去打它们、白打不结算。
            SelectableEntityTag best = null;
            var bestDist = float.MaxValue;
            var bestHp = float.MaxValue;
            // 12 米内**有尸体就先去拾取**：尸体窗口会到期（6 秒），先砍下一只会让这具尸体白白过期
            // ——实测一轮下来 `到期提示=6`、`尸体取走=0`，拾取那一跳就没被真跑过。
            var lootPriority = false;
            foreach (var t in SelectableEntityTag.Active)
            {
                if (t == null || !t.Selectable || t.Kind != SelectableKind.Monster) continue;
                if (t.Actor == null || !t.Actor.IsActive) continue;
                var d = Vector3.Distance(t.Actor.Position, playerPos);
                var h = t.Actor.GetScript<global::Game.Character.Attributes.CharacterAttributesComponent>()?.CurrentHealth ?? 9999f;
                if (t.CorpseInteractable && d <= 1200f)
                {
                    if (!lootPriority || d < bestDist) { best = t; bestDist = d; bestHp = h; lootPriority = true; }
                    continue;
                }
                if (lootPriority) continue;
                var better = d <= 1200f
                    ? (h < bestHp || (Mathf.Approximately(h, bestHp) && d < bestDist))
                    : (bestHp == float.MaxValue && d < bestDist);
                if (!better) continue;
                best = t; bestDist = d; bestHp = h;
            }

            if (best == null)
            {
                AcceptLog($"action=none nearest=0 dist=-1");
                FlaxEngine.Debug.Log("[MonsterAccept] no-monster");
                return;
            }
            var hp = best.Actor.GetScript<global::Game.Character.Attributes.CharacterAttributesComponent>()?.CurrentHealth ?? -1f;
            if (best != Selected) Select(best);

            // 回执要能分辨"贴脸"到底是谁的脸：锚点（本脚本宿主＝本地玩家 Actor）与
            // HundunWorldGame 记录的本地玩家 Actor 可能不是同一个东西，坐标按 F1 原样念出来。
            var lp = HundunWorldGame.Instance?.LocalPlayerActor;
            var lpPos = lp != null ? lp.Position : new Vector3(float.NaN, 0f, float.NaN);
            var posTag = $"p=({playerPos.X:F1},{playerPos.Z:F1}) lp=({lpPos.X:F1},{lpPos.Z:F1})";

            if (bestDist > AcceptAttackDistance)
            {
                // 超出普攻/拾取距离（服务端 PlayerAttackRange=3 米）：先走过去再做事。
                // 不走路而去砍 60 米外的怪，服务端只会回 OutOfRange（实测 58/58 刀全落这一档）。
                var pc = Actor?.GetScript<global::HundunWorld.Game.PlayerController>();
                var started = pc != null && pc.TryStartClickToMove(best.Actor.Position);
                AcceptLog($"action=walk entity={best.EntityId} dist={bestDist:F1} started={(started ? 1 : 0)} at=({best.Actor.Position.X:F1},{best.Actor.Position.Z:F1}) {posTag}");
                FlaxEngine.Debug.Log($"[MonsterAccept] walk entity={best.EntityId} dist={(int)bestDist} started={started}");
            }
            else if (best.CorpseInteractable)
            {
                var ok = TryInteractSelected();
                AcceptLog($"action=loot entity={best.EntityId} dist={bestDist:F1} hp={(int)hp} ok={(ok ? 1 : 0)} at=({best.Actor.Position.X:F1},{best.Actor.Position.Z:F1}) {posTag}");
                FlaxEngine.Debug.Log($"[MonsterAccept] loot entity={best.EntityId} dist={(int)bestDist} ok={ok}");
            }
            else
            {
                var targetId = Combat.CombatReporter.ResolveNetworkTarget(best.Actor);
                var hasSender = HundunWorldGame.Instance?.GuardSyncSender != null;
                var ok = TryBasicAttackSelected();
                AcceptLog($"action=attack entity={best.EntityId} targetId={targetId} sender={(hasSender ? 1 : 0)} dist={bestDist:F1} hp={(int)hp} ok={(ok ? 1 : 0)} at=({best.Actor.Position.X:F1},{best.Actor.Position.Z:F1}) {posTag}");
                FlaxEngine.Debug.Log($"[MonsterAccept] attack entity={best.EntityId} dist={(int)bestDist} ok={ok}");
            }
        }

        private Vector3 PlayerAnchorPosition()
        {
            // 必须用**本地玩家 Actor**，不能用本脚本的宿主 Actor：宿主一旦不是玩家本体，
            // 距离就会按另一个物体算（实测宿主停在 (24,-6)、玩家在 (127.1,52.2)，
            // 于是"离怪 1 厘米"其实是 118 米外，打出去全是超射程）。
            var lp = HundunWorldGame.Instance?.LocalPlayerActor;
            return lp != null ? lp.Position : Actor.Position;
        }

        /// <summary>
        /// 脚底光圈半径（厘米）：以种类体型为基准，包围盒只在基准的 0.6~1.6 倍之间起修正作用。
        /// <para>
        /// 原实现直接吃 <c>Actor.Box</c> 的 XZ 最大值，而远程实体的 Box 会把子对象范围、
        /// 占位胶囊甚至挂载物一起算进去，结果光圈大出角色好几圈，看着像"选中了一整块地面"。
        /// </para>
        /// </summary>
        private static float RingRadiusFor(SelectableKind kind, Actor actor)
        {
            float baseRadius = kind switch
            {
                SelectableKind.Item => 35f,
                SelectableKind.GatherNode => 70f,
                SelectableKind.Npc => 60f,
                SelectableKind.Monster => 80f,
                _ => 55f,
            };

            var bounds = actor.Box;
            float fromBounds = Math.Max(bounds.Size.X, bounds.Size.Z) * 0.5f;
            if (fromBounds > 1f)
                baseRadius = Mathf.Clamp(fromBounds, baseRadius * 0.6f, baseRadius * 1.6f);

            return baseRadius;
        }

        /// <summary>不同种类实体的头顶信息卡高度（厘米）。</summary>
        private static float LabelHeight(SelectableKind kind)
        {
            switch (kind)
            {
                case SelectableKind.Item: return 70f;
                case SelectableKind.GatherNode: return 150f;
                default: return 215f;
            }
        }

        /// <summary>
        /// 鼠标是否归 UI。裁决统一放在 <see cref="UIMouseCapture"/>，与点击移动、
        /// 技能指示器、相机旋转/缩放共用同一套口径，避免各系统自己写一份判定后互相打架。
        /// </summary>
        private static bool IsCursorBlockedByUi() => UIMouseCapture.Captured;

        private void DrawHighlight(Camera camera)
        {
            var actor = Selected?.Actor;
            if (actor == null)
            {
                if (_footRing != null)
                    _footRing.HideRing();
                DestroyRingFollower();
                return;
            }

            var center = actor.Position + Vector3.Up * 3f; // 略抬离地，避免贴地描边被地面吞掉
            float radius = RingRadiusFor(Selected.Kind, actor);

            // 有 3D 光圈 Prefab 就跟着目标走；投影环同时收起，避免两层叠加看起来像双圈。
            if (UpdateRingFollower(center, actor))
            {
                if (_footRing != null)
                    _footRing.HideRing();
                return;
            }

            DestroyRingFollower();
            EnsureFootRing();
            if (_footRing == null)
                return;

            if (!ProjectRing(camera, center, radius, _ringPoints))
            {
                _footRing.HideRing();
                return;
            }

            // 副环只做轻微呼吸：半径变化过大会让"选中"看起来在抖。
            float pulseRadius = radius * (1f + 0.09f * Mathf.Sin(Time.GameTime * 3f));
            int pulseCount = ProjectRing(camera, center, pulseRadius, _ringPulsePoints) ? RingSegments : 0;

            var pulseColor = new Color(HighlightColor.R, HighlightColor.G, HighlightColor.B, HighlightColor.A * 0.45f);
            _footRing.SetRing(_ringPoints, RingSegments, _ringPulsePoints, pulseCount,
                HighlightColor, pulseColor, 2f);
        }

        /// <summary>把世界水平圆环逐点投影成屏幕折线；任一点落到相机身后即整体判为不可见。</summary>
        private bool ProjectRing(Camera camera, Vector3 center, float radius, Float2[] outPoints)
        {
            var camPos = camera.Transform.Translation;
            var forward = camera.Transform.Forward;
            var step = Mathf.Pi * 2f / RingSegments;

            for (var i = 0; i < RingSegments; i++)
            {
                var angle = i * step;
                var world = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                if (Vector3.Dot(world - camPos, forward) <= 0f)
                    return false;

                camera.ProjectPoint(world, out var screen);
                outPoints[i] = screen;
            }

            return true;
        }

        /// <summary>取（并缓存）脚底光圈 Prefab；未配置或加载失败返回 null，由投影环兜底。</summary>
        private Prefab LoadRingPrefab()
        {
            if (string.IsNullOrEmpty(RingPrefabPath))
                return null;

            if (RingPrefabGuid == Guid.Empty &&
                HundunWorldGame.TryGetAssetGuidFromPath(RingPrefabPath, out var realGuid))
            {
                RingPrefabGuid = realGuid;
            }

            if (_ringPrefab != null && _ringPrefabPathLoaded == RingPrefabPath)
                return _ringPrefab;

            _ringPrefab = HundunWorldGame.LoadAssetFullyLoaded<Prefab>(RingPrefabGuid, RingPrefabPath);
            _ringPrefabPathLoaded = RingPrefabPath;

            if (_ringPrefab == null && !_ringPrefabLoadWarned)
            {
                _ringPrefabLoadWarned = true;
                Debug.LogWarning($"[WorldSelectionService] 脚底光圈 Prefab 加载失败，回落到投影环: {RingPrefabPath}");
            }

            return _ringPrefab;
        }

        /// <summary>把 3D 光圈跟随到目标脚下；返回 false 表示没有可用 Prefab，应改用投影环。</summary>
        private bool UpdateRingFollower(Vector3 center, Actor target)
        {
            var prefab = LoadRingPrefab();
            if (prefab == null)
                return false;

            if (_ringFollower == null)
            {
                _ringFollower = PrefabManager.SpawnPrefab(prefab, center, Quaternion.Identity);
                // 与目标同场景：光圈若落在常驻 RootScene 上，切场景不卸载就会跨会话堆积。
                if (_ringFollower != null && target.Scene != null && _ringFollower.Scene != target.Scene)
                    Level.SpawnActor(_ringFollower, target.Scene);
            }
            else
            {
                _ringFollower.Position = center;
            }

            return _ringFollower != null;
        }

        private void DestroyRingFollower()
        {
            if (_ringFollower == null)
                return;

            Destroy(_ringFollower);
            _ringFollower = null;
        }

        /// <summary>投影环宿主与名牌同一层（不参与设计空间缩放），懒创建：进世界前 NameplateHost 还是 null。</summary>
        private void EnsureFootRing()
        {
            if (_footRing != null && _footRing.Parent != null)
                return;

            var host = UI.GameMain.NameplateHost.Current;
            if (host == null)
                return;

            _footRing = new FootRingWidget { Parent = host };
        }
    }
}
