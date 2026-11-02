using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using Game.Combat.Skills;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.Combat.Skills;
using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.ActionBar;
using HundunWorld.Game.UI.Ink.Pages;
using HundunWorld.Game.UI.Ink.Pages.Activities;
using HundunWorld.Game.UI.Ink.Pages.Appearance;
using HundunWorld.Game.UI.Ink.Pages.BattlePass;
using HundunWorld.Game.UI.Ink.Pages.Bestiary;
using HundunWorld.Game.UI.Ink.Pages.Casual;
using HundunWorld.Game.UI.Ink.Pages.Character;
using HundunWorld.Game.UI.Ink.Pages.CharacterCreation;
using HundunWorld.Game.UI.Ink.Pages.Combat;
using HundunWorld.Game.UI.Ink.Pages.Crafting;
using HundunWorld.Game.UI.Ink.Pages.Dungeon;
using HundunWorld.Game.UI.Ink.Pages.ElementVision;
using HundunWorld.Game.UI.Ink.Pages.Gacha;
using HundunWorld.Game.UI.Ink.Pages.InkAnim;
using HundunWorld.Game.UI.Ink.Pages.Inventory;
using HundunWorld.Game.UI.Ink.Pages.Livelihood;
using HundunWorld.Game.UI.Ink.Pages.Map;
using HundunWorld.Game.UI.Ink.Pages.MartialRecord;
using HundunWorld.Game.UI.Ink.Pages.Multiplayer;
using HundunWorld.Game.UI.Ink.Pages.MountPet;
using HundunWorld.Game.UI.Ink.Pages.Personal;
using HundunWorld.Game.UI.Ink.Pages.PhotoMode;
using HundunWorld.Game.UI.Ink.Pages.Reward;
using HundunWorld.Game.UI.Ink.Pages.Sect;
using HundunWorld.Game.UI.Ink.Pages.Settings;
using HundunWorld.Game.UI.Ink.Pages.Shop;
using HundunWorld.Game.UI.Ink.Pages.Social;
using HundunWorld.Game.UI.Ink.Pages.Team;
using HundunWorld.Game.UI.Ink.Pages.Timing;
using HundunWorld.Game.UI.StyleSystem;
using HundunWorld.Game;
using NameplateHost = HundunWorld.Game.UI.GameMain.NameplateHost;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 主UI管理器
    /// 统一管理所有UI界面的显示、隐藏和切换
    /// </summary>
    public class MainUIManager : Script
    {
        private static MainUIManager _instance;
        public static MainUIManager Instance => _instance;

        /// <summary>
        /// 从其它系统按 domId 打开一个 Ink 页面（世界交互用，例如点商人开商店）。
        /// 路由未初始化时返回 false，调用方据此提示"界面未就绪"而不是静默失败。
        /// </summary>
        public bool NavigateToInkPage(string domId)
        {
            if (_inkPageRouter == null || string.IsNullOrEmpty(domId)) return false;
            return _inkPageRouter.NavigateTo(domId);
        }

        // UI界面组件
        private AuthenticationUI _authenticationUI;
        
        // UI状态管理
        private UIStateManager _stateManager;
        private Dictionary<SceneType, Script> _uiComponents = new Dictionary<SceneType, Script>();
        
        // 当前活动的UI
        private Script _currentActiveUI;

        // 水墨主题 UI 组件（进入 GameWorld 场景后激活）
        /// <summary>水墨页面外壳，承载背景层/暗角层/内容层/返回按钮</summary>
        private InkPageShell _inkPageShell;
        /// <summary>水墨页面路由器，管理页面栈与导航</summary>
        private InkPageRouter _inkPageRouter;
        /// <summary>承载水墨 UI 的专用 UICanvas</summary>
        private UICanvas _inkCanvas;
        /// <summary>角色头顶名牌宿主容器（血条/名称/等级/称号）：挂在 _inkCanvas.GUI 根下并绘制在最上层。
        /// 各角色 Actor 上的 NameplateUI 组件把自身名牌控件挂到本容器下，由本类按页面统一显隐。</summary>
        private ContainerControl _nameplateHost;

        /// <summary>底部滚轮式技能栏（6 行 × 10 槽）。挂在外壳常驻覆盖层上，跨页面长存，
        /// 因此打开背包/武学面板时也能把图标拖到栏上。</summary>
        private InkActionBarControl _inkActionBar;
        /// <summary>五行循环节指示器（技能栏上方一排相生序节点），与技能栏同生同灭。</summary>
        private InkWuxingCycleIndicator _inkCycleIndicator;
        /// <summary>拖拽幽灵层：画布根最上层，仅拖拽期间可见。</summary>
        private InkDragGhost _inkDragGhost;
        /// <summary>是否已订阅技能/背包变更事件（避免外壳重建时重复挂接）。</summary>
        private bool _actionBarDataEventsSubscribed;

        /// <summary>
        /// 当前是否停留在战斗 HUD 现场页（v1 / 沉浸式 / 传统式三选一）。
        /// 世界层面的输入（点选场景对象、快捷键开关面板）只在这种"人在世界里"的页面放行，
        /// 菜单/弹窗页里一律吞掉，避免点菜单误选中菜单背后的怪物。
        /// </summary>
        public bool IsOnCombatHudPage =>
            _inkPageRouter != null
            && (_inkPageRouter.CurrentPageDomId == InkPageDomIds.CombatHud
                || _inkPageRouter.CurrentPageDomId == InkPageDomIds.CombatHudV2
                || _inkPageRouter.CurrentPageDomId == InkPageDomIds.CombatHudTraditional);

        /// <summary>水墨 UI 根控件（名牌宿主与各页面外壳的共同父级）；未进入游戏世界时为 null。</summary>
        public ContainerControl InkUiRoot => _inkCanvas?.GUI;

        /// <summary>
        /// 当前是否有模态弹层盖在页面上（物品获得/消息/对话确认等）。
        /// 弹层打开时世界侧不接鼠标：弹层遮罩是引擎 Panel，不在可交互控件白名单内，
        /// 不额外拦一层的话点空白会穿透到场景（点选/点击移动）。
        /// </summary>
        public bool IsOverlayOpen => _inkPageRouter != null && _inkPageRouter.HasOverlay;
        /// <summary>玩家手动开关：头顶名牌是否显示（快捷键 "-" 切换），默认显示。</summary>
        private bool _nameplateUserVisible = true;

        // 活跃的效果图标
        private readonly Dictionary<(ulong TargetId, int EffectId), EffectIconEntry> _activeEffectIcons = new();

        // 本地玩家数据绑定缓存
        private CharacterAttributesComponent _cachedAttributes;
        private SkillBase[] _cachedSkills;
        private bool _localPlayerReady;

        // 「自动隐藏UI」：鼠标与本地角色都静止 N 秒后收起常驻技能栏/气息指示（设置项 AutoHideUI）
        private const float UiAutoHideSeconds = 8f;
        private Vector2 _lastUiMousePos;
        private Vector3 _lastUiPlayerPos;
        private float _uiIdleSeconds;
        private bool _selfTestAutoHideRestored;

        /// <summary>拍照模式取景期间隐藏常驻技能栏/气息指示（页面用 OverlayVisibilityRequested 控制）。</summary>
        private bool _photoOverlayHidden;
        private float _rebindLogThrottle;

        /// <summary>自测：进世界后倒计时到点自动打开拍照页（&lt;=0 表示未启用）。</summary>
        private float _selfTestPhotoDelay = -1f;

        /// <summary>自测：到点自动导航到的页面 dom-id（来自 open-page.txt，空表示未启用）。</summary>
        private string _selfTestNavPage;
        private float _selfTestNavDelay = -1f;
        private float _selfTestNavWatchTimer = 0.5f;
        private float _selfTestNavRetryTimer = 3f;
        private int _selfTestNavAttempts;

        /// <summary>自测：等角色列表就绪后自动进入游戏（enter-world.on），每 0.5 秒重试。</summary>
        private bool _selfTestEnterWorldPending;
        private float _selfTestEnterWorldTimer;
        private float _selfTestEnterWorldDelay = 3f;

        /// <summary>自测：ui-shot.on 存在时，导航落地 1 秒后把整屏（含 GUI）抓成 Screenshots/ui-&lt;domId&gt;.png。</summary>
        private string _selfTestShotPage;
        private float _selfTestShotDelay = -1f;
        private int _selfTestEnterWorldAttempts;

        /// <summary>自测回执：运行日志在这台机器上不落盘，只能写文件取证。</summary>
        private static void AppendSelfTestReceipt(string line)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", "ui-selftest.out");
                System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
            }
            catch
            {
                // 回执写不进去不影响功能
            }
        }

        /// <summary>最后已知活动页面 dom-id，用于 Canvas 自愈重建后恢复导航状态</summary>
        private string _lastKnownDomId = InkPageDomIds.CombatHudTraditional;
        // 活动页面引用（用于 OnUpdate 动态刷新）
        private CombatHudPage _activeCombatHud;
        private CombatHudV2Page _activeCombatHudV2;
        // 新增活动页面引用（game-ui-system 设计方案 19 页面中的核心 3 页）
        private CombatHudTraditionalPage _activeCombatHudTraditional;
        private CharacterPanelPage _activeCharacterPanel;
        private SkillTreePage _activeSkillTree;

        /// <summary>Ink 菜单开合 → NarrativePro 输入栈（世界交互提示据此隐藏）。</summary>
        private InkWorldInputGate _inkWorldInputGate;

        /// <summary>上一次观测到的本地玩家死亡态（边沿检测，避免每帧重开阵亡界面）。</summary>
        private bool _localPlayerDead;
        /// <summary>玩家在阵亡界面点了「返回」自行关掉后，本次死亡不再自动弹回。</summary>
        private bool _deathScreenDismissed;

        // 水墨 UI 收敛自愈节流：初始化持续失败时避免逐帧重建风暴，正常态零开销。
        private const double InkRecoverIntervalSeconds = 0.5;
        private double _nextInkRecoverAt = -1.0;

        public override void OnStart()
        {
            InitializeInstance();
            InitializeStateManager();
            InitializeUIComponents();
            SubscribeEvents();
            
            FlaxEngine.Debug.Log("主UI管理器初始化完成");
        }

        /// <summary>
        /// 每帧更新：检测本地玩家就绪状态翻转，刷新 CombatHud 动态数据。
        /// </summary>
        public override void OnUpdate()
        {
            base.OnUpdate();
            float deltaTime = Time.DeltaTime;
            try
            {
                // 0. 水墨 UI 收敛自愈：只要"当前场景应显示水墨 UI"而实际缺失/损坏/被乱序事件隐藏，
                //    就重建或恢复。覆盖三类丢失路径（登录→选角偶发整屏无 UI 的根因）：
                //    a) SceneChanged 恰在 Flax 关卡切换窗口到达，Canvas.GUI 未就绪 → InitializeInkWashUI
                //       提前 return，shell 停留为 null；随后完成回调被 UIStateManager 的
                //       "已在目标场景跳过切换"吞掉，事件不会再补发；
                //    b) 多条 CharacterSelection 切换路径（LoginResponse/CharacterList/Authentication）
                //       与 GameSceneManager 的 _isTransitioning 互斥互相竞争，事件错过订阅窗口；
                //    c) 乱序的旧 SceneChanged 把刚建好的壳 Hide 掉。
                //    节流：每 InkRecoverIntervalSeconds 最多尝试一次，避免初始化持续失败时逐帧重建。
                TryRecoverInkUI();

                // 自动隐藏 UI 的空闲计时（鼠标+角色位移都不动才算空闲）
                TrackUiIdleActivity(deltaTime);

                // 菜单层盖住世界时，把 CommonUI 输入栈标成 Menu，世界交互提示随之隐藏。
                SyncWorldInputGate();

                // 本地玩家阵亡 → 自动弹阵亡界面；复活（IsDead 翻回 false）→ 自动收起。
                WatchLocalPlayerDeath();

                // 自测：进世界后延迟打开拍照页（相机与常驻栏需要几帧才就绪）。
                if (_selfTestPhotoDelay > 0f)
                {
                    _selfTestPhotoDelay -= deltaTime;
                    if (_selfTestPhotoDelay <= 0f)
                    {
                        _selfTestPhotoDelay = -1f;
                        FlaxEngine.Debug.Log("[SelfTest] open-photo.on 命中，自动打开拍照页");
                        NavigateToPage(InkPageDomIds.NavPhotoMode);
                    }
                }

                // 自测：角色列表可能比选角页晚到，等到了再自动进世界。
                if (_selfTestEnterWorldPending)
                {
                    // 选角页刚建好时 GameSceneManager 往往还在 Login→CharacterSelection 的切换窗口里，
                    // 这时请求 GameWorld 会被 TransitionTo 直接拒掉：等切换落定再点。
                    if (_selfTestEnterWorldDelay > 0f)
                    {
                        _selfTestEnterWorldDelay -= deltaTime;
                    }
                    else
                    {
                        _selfTestEnterWorldTimer -= deltaTime;
                        if (_selfTestEnterWorldTimer <= 0f)
                        {
                            _selfTestEnterWorldTimer = 0.5f;
                            AutoEnterWorldForSelfTest();
                        }
                    }
                }

                // 自测：ui-shot.on 存在时，导航落地 1 秒后把整屏（含 GUI）抓下来做视觉走查。
                if (_selfTestShotDelay > 0f)
                {
                    _selfTestShotDelay -= deltaTime;
                    if (_selfTestShotDelay <= 0f)
                    {
                        _selfTestShotDelay = -1f;
                        CaptureSelfTestUiShot();
                    }
                }
                // 自测：open-page.txt 变了就导航（走查运行时页面时不必重启 PIE）。
                _selfTestNavWatchTimer -= deltaTime;
                if (_selfTestNavWatchTimer <= 0f)
                {
                    _selfTestNavWatchTimer = 0.5f;
                    string domId = ReadSelfTestNavPage();
                    if (!string.IsNullOrEmpty(domId) && domId != _selfTestNavPage)
                    {
                        _selfTestNavPage = domId;
                        _selfTestNavDelay = 0.5f;
                        _selfTestNavAttempts = 0;
                        _selfTestNavRetryTimer = 3f;
                    }
                }

                // 导航落空（例如当时还在创角场景）时定期重试，直到真的停在该页。
                if (!string.IsNullOrEmpty(_selfTestNavPage) && _inkPageRouter != null
                    && _inkPageRouter.CurrentPageDomId != _selfTestNavPage && _selfTestNavAttempts < 20)
                {
                    _selfTestNavRetryTimer -= deltaTime;
                    if (_selfTestNavRetryTimer <= 0f)
                    {
                        _selfTestNavRetryTimer = 3f;
                        _selfTestNavAttempts++;
                        bool ok = NavigateToPage(_selfTestNavPage);
                        AppendSelfTestReceipt($"nav-retry {_selfTestNavPage} ok={ok} cur={_inkPageRouter.CurrentPageDomId}");
                    }
                }

                if (_selfTestNavDelay > 0f)
                {
                    _selfTestNavDelay -= deltaTime;
                    if (_selfTestNavDelay <= 0f)
                    {
                        _selfTestNavDelay = -1f;
                        FlaxEngine.Debug.Log($"[SelfTest] open-page 命中，自动导航到 {_selfTestNavPage}");
                        NavigateToPage(_selfTestNavPage);
                    }
                }

                // 1. 检测本地玩家从 null → 非 null 的状态翻转
                if (!_localPlayerReady)
                {
                    if (TryGetLocalPlayerAttributes(out var attr))
                    {
                        _cachedAttributes = attr;
                        _localPlayerReady = true;
                        if (TryGetLocalPlayerSkills(out var skills))
                            _cachedSkills = skills;
                        FlaxEngine.Debug.Log("[MainUIManager] 本地玩家已就绪，下次导航到 combat-hud/nav-character-v2 时将自动绑定");

                        if (HasSelfTestPhotoEntry())
                            _selfTestPhotoDelay = 3f;

                        // 属性加点：把登录时下发的服务端权威分配灌进刚就绪的组件。
                        Services.AttributeAllocationService.Instance.ApplyToComponent();

                        // 重绑 CombatHud（左上角角色信息），确保进入游戏时实时更新
                        if (_activeCombatHudV2 != null)
                        {
                            try { _activeCombatHudV2.BindCharacter(attr); }
                            catch (Exception bindEx) { FlaxEngine.Debug.LogError($"[MainUIManager] CombatHudV2 重绑失败: {bindEx.Message}"); }
                        }
                        if (_activeCombatHudTraditional != null)
                        {
                            try
                            {
                                _activeCombatHudTraditional.BindCharacter(attr);
                            }
                            catch (Exception bindEx) { FlaxEngine.Debug.LogError($"[MainUIManager] CombatHudTraditional 重绑失败: {bindEx.Message}"); }
                        }
                        if (_activeCombatHud != null)
                        {
                            try
                            {
                                _activeCombatHud.BindCharacter(attr);
                                if (_cachedSkills != null) _activeCombatHud.BindSkills(_cachedSkills);
                                FlaxEngine.Debug.Log("[MainUIManager] CombatHud 已重绑本地玩家数据");
                            }
                            catch (Exception bindEx)
                            {
                                FlaxEngine.Debug.LogError($"[MainUIManager] CombatHud 重绑失败: {bindEx.Message}");
                            }
                        }

                        // 新增页面延迟重绑：CombatHudTraditional / CharacterPanel / SkillTree
                        if (_activeCombatHudTraditional != null)
                        {
                            try
                            {
                                _activeCombatHudTraditional.BindCharacter(attr);
                                FlaxEngine.Debug.Log("[MainUIManager] CombatHudTraditional 已重绑本地玩家数据");
                            }
                            catch (Exception bindEx)
                            {
                                FlaxEngine.Debug.LogError($"[MainUIManager] CombatHudTraditional 重绑失败: {bindEx.Message}");
                            }
                        }
                        if (_activeCharacterPanel != null)
                        {
                            try
                            {
                                _activeCharacterPanel.BindCharacter(attr);
                                FlaxEngine.Debug.Log("[MainUIManager] CharacterPanel 已重绑本地玩家数据");
                            }
                            catch (Exception bindEx)
                            {
                                FlaxEngine.Debug.LogError($"[MainUIManager] CharacterPanel 重绑失败: {bindEx.Message}");
                            }
                        }
                        if (_activeSkillTree != null)
                        {
                            try
                            {
                                _activeSkillTree.BindCharacter(attr);
                                FlaxEngine.Debug.Log("[MainUIManager] SkillTree 已重绑本地玩家数据");
                            }
                            catch (Exception bindEx)
                            {
                                FlaxEngine.Debug.LogError($"[MainUIManager] SkillTree 重绑失败: {bindEx.Message}");
                            }
                        }
                    }
                }

                // 1a. 底部技能栏：进世界才显示，并按当前角色载入 60 槽配置
                UpdateActionBar();

                // 2. 持续刷新 CombatHud 动态数据（仅当当前页面为 combat-hud 且玩家就绪时）
                if (_localPlayerReady && _inkPageRouter?.CurrentPageDomId == InkPageDomIds.CombatHud && _activeCombatHud != null)
                {
                    RefreshCombatHudDynamicData();
                }

                // 2a. 持续刷新 CombatHudV2 动态数据（仅当当前页面为 combat-hud-v2 且玩家就绪时）
                if (_localPlayerReady && _inkPageRouter?.CurrentPageDomId == InkPageDomIds.CombatHudV2 && _activeCombatHudV2 != null)
                {
                    RefreshCombatHudV2DynamicData();
                }

                // 2b. 持续刷新 CombatHudTraditional 动态数据（仅当当前页面为 combat-hud-traditional 且玩家就绪时）
                if (_localPlayerReady && _inkPageRouter?.CurrentPageDomId == InkPageDomIds.CombatHudTraditional && _activeCombatHudTraditional != null)
                {
                    RefreshCombatHudTraditionalDynamicData();
                }

                // 2c. 快捷键 C：打开"角色属性"（合并入口 = 角色信息面板，含行囊；与点击头像同一目标；
                //     仅在战斗 HUD（v1/v2 枢纽）激活时生效，避免与菜单内文本输入冲突）
                if (_inkPageRouter != null && Input.GetKeyDown(KeyboardKeys.C) && !IsOverlayOpen
                    && (_inkPageRouter.CurrentPageDomId == InkPageDomIds.CombatHudV2
                        || _inkPageRouter.CurrentPageDomId == InkPageDomIds.CombatHud
                        || _inkPageRouter.CurrentPageDomId == InkPageDomIds.CombatHudTraditional))
                {
                    NavigateToPage(InkPageDomIds.NavInventory);
                }

                // 3. 快捷键 F9 打开 UI 画廊（Debug 菜单），便于查看所有 UI 页面
                if (_inkPageRouter != null && Input.GetKeyDown(KeyboardKeys.F9))
                {
                    try
                    {
                        FlaxEngine.Debug.Log("[MainUIManager] F9 触发：打开 UI 画廊");
                        NavigateToPage(InkPageDomIds.UIGallery);
                    }
                    catch (Exception galleryEx)
                    {
                        FlaxEngine.Debug.LogError($"[MainUIManager] 打开 UI 画廊失败: {galleryEx.Message}");
                    }
                }

                // 快捷键 "-"（数字 0 右侧的减号键）：显示/隐藏角色头顶名牌。
                // 仅在进入游戏世界（水墨 UI 已就绪）时生效，避免在登录/角色选择场景误触发。
                if (_inkPageShell != null && Input.GetKeyDown(KeyboardKeys.Minus))
                {
                    _nameplateUserVisible = !_nameplateUserVisible;
                    if (_nameplateHost != null)
                        _nameplateHost.Visible = _nameplateUserVisible && _nameplateHost.Visible;
                    FlaxEngine.Debug.Log($"[MainUIManager] 头顶名牌显示={_nameplateUserVisible}（快捷键 -）");
                }

                // 快捷键 Esc：一条优先级链——正在打字先退焦点；有弹层先关弹层；其余非 HUD 页面退一层
                // （栈空回战斗 HUD）；停在战斗 HUD 上则打开系统菜单（Modal 叠加层，再按一次即关）。
                // 页面内不再各自处理 Esc；自管返回的页面（创角流/加载/转场）按 OwnBack 标记豁免改派。
                if (_inkPageRouter != null && Input.GetKeyDown(KeyboardKeys.Escape))
                {
                    if (UIMouseCapture.IsTextInputFocused)
                    {
                        // 第一层只退焦点：在输入框里按 Esc 不该顺手把整页关掉。
                        UIMouseCapture.ReleaseTextInputFocus();
                    }
                    else if (_inkPageRouter.HasOverlay)
                    {
                        _inkPageRouter.CloseOverlay();
                    }
                    else if (!IsOnCombatHudPage && !_inkPageRouter.CurrentPageOwnsBackKey)
                    {
                        // 走 Back()：有返回栈就回来源页，否则回战斗 HUD。
                        // 自带返回入口的页面（如创角阶段流）自己处理 Esc，中央不改派。
                        _inkPageRouter.Back();
                    }
                    else if (IsOnCombatHudPage)
                    {
                        NavigateToPage(InkPageDomIds.SystemMenu);
                    }
                }

                // 暂停态跟着"系统菜单是否盖着"走。每帧对账一次：弹层可能被页面自己的关闭按钮、
                // 返回栈等任一路径收掉，只在 Esc 分支里同步会漏，留下一个永远解不开的暂停。
                SyncPauseToSystemMenu();

                // 4. 角色头顶名牌：名牌控件由各角色上的 NameplateUI 组件自行投影定位，
                //    本处仅统一控制宿主容器显隐——需玩家开关打开、处于战斗 HUD 现场页且水墨 UI 健康。
                //    弹层（阵亡界面 / 获得物品 / 留言…）不占页面槽位、CurrentPageDomId 仍是 HUD，
                //    所以这里必须再判一次"没有叠弹层"，否则名牌会浮在弹层之上。
                if (_nameplateHost != null)
                {
                    var pageDom = _inkPageRouter?.CurrentPageDomId;
                    bool inField = pageDom == InkPageDomIds.CombatHud
                                   || pageDom == InkPageDomIds.CombatHudV2
                                   || pageDom == InkPageDomIds.CombatHudTraditional;
                    bool overlayOpen = _inkPageRouter?.HasOverlay ?? false;
                    _nameplateHost.Visible = _nameplateUserVisible && inField && !overlayOpen && IsInkUIHealthy();
                }
            }
            catch (Exception ex)
            {
                _rebindLogThrottle += deltaTime;
                if (_rebindLogThrottle > 5f)
                {
                    _rebindLogThrottle = 0f;
                    FlaxEngine.Debug.LogError($"[MainUIManager] OnUpdate 异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 刷新 CombatHud 的动态数据：小地图玩家朝向 + 技能冷却进度。
        /// </summary>
        private void RefreshCombatHudDynamicData()
        {
            var game = HundunWorldGame.Instance;
            var actor = game?.LocalPlayerActor;
            if (actor == null || _activeCombatHud == null)
                return;

            // 小地图朝向：从 Actor.Orientation 提取 Yaw 角（度，0=正北，顺时针增加）
            // 按 Flax 项目惯例：Y 轴为向上轴，Yaw = 绕 Y 轴旋转 = EulerAngles.Y
            try
            {
                var orient = actor.Orientation;
                var eulerAngles = orient.EulerAngles;
                float yaw = eulerAngles.Y;
                // 归一化到 0-360
                yaw = ((yaw % 360f) + 360f) % 360f;
                _activeCombatHud.MinimapPlayerYaw = yaw;
                _activeCombatHud.NavStripPlayerYaw = yaw;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 刷新小地图朝向异常: {ex.Message}");
            }

            // 技能冷却：遍历 _cachedSkills，反转 GetCooldownProgress (0=刚释放/1=就绪) → CombatHud (0=就绪/1=冷却中)
            try
            {
                if (_cachedSkills != null && _cachedSkills.Length > 0)
                {
                    var cooldowns = new float[_cachedSkills.Length];
                    for (int i = 0; i < _cachedSkills.Length; i++)
                    {
                        float progress = _cachedSkills[i]?.GetCooldownProgress() ?? 1f;
                        cooldowns[i] = 1f - progress;
                    }
                    _activeCombatHud.SkillCooldowns = cooldowns;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 刷新技能冷却异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新 CombatHudV2 的动态数据：小地图玩家朝向。
        /// 技能冷却/状态由 V2 页面自身每帧从绑定组件读取，此处仅刷新朝向（依赖 LocalPlayerActor.Orientation）。
        /// </summary>
        private void RefreshCombatHudV2DynamicData()
        {
            var game = HundunWorldGame.Instance;
            var actor = game?.LocalPlayerActor;
            if (actor == null || _activeCombatHudV2 == null)
                return;

            // 顶部目标框：未选中时整框隐藏，选中时用世界选中服务的数据填
            _activeCombatHudV2.RefreshTargetInfo();

            try
            {
                var orient = actor.Orientation;
                var eulerAngles = orient.EulerAngles;
                float yaw = eulerAngles.Y;
                yaw = ((yaw % 360f) + 360f) % 360f;
                _activeCombatHudV2.MinimapPlayerYaw = yaw;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 刷新 CombatHudV2 小地图朝向异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新 CombatHudTraditional 的动态数据：小地图玩家朝向。
        /// HP/MP/SP/技能冷却由页面自身 Update 方法每帧从绑定组件读取，此处仅刷新朝向（依赖 LocalPlayerActor.Orientation）。
        /// </summary>
        private void RefreshCombatHudTraditionalDynamicData()
        {
            var game = HundunWorldGame.Instance;
            var actor = game?.LocalPlayerActor;
            if (actor == null || _activeCombatHudTraditional == null)
                return;

            try
            {
                var orient = actor.Orientation;
                var eulerAngles = orient.EulerAngles;
                float yaw = eulerAngles.Y;
                yaw = ((yaw % 360f) + 360f) % 360f;
                _activeCombatHudTraditional.MinimapPlayerYaw = yaw;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 刷新 CombatHudTraditional 小地图朝向异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化单例实例
        /// </summary>
        private void InitializeInstance()
        {
            if (_instance == null)
            {
                _instance = this;
                // 确保跨场景持久化
                Actor.SetStaticFlag(StaticFlags.FullyStatic, true);
            }
            else if (_instance != this)
            {
                // 销毁多余的脚本实例（仅销毁脚本，不销毁 Actor，避免级联销毁兄弟脚本）
                Destroy(this);
                return;
            }
        }

        /// <summary>
        /// 初始化状态管理器
        /// </summary>
        private void InitializeStateManager()
        {
            _stateManager = UIStateManager.Instance;
        }

        /// <summary>
        /// 初始化UI组件
        /// </summary>
        private void InitializeUIComponents()
        {
            try
            {
                // 创建认证UI
                var authActor = new EmptyActor();
                authActor.Name = "AuthenticationUI";
                authActor.Parent = Actor;
                _authenticationUI = authActor.AddScript<AuthenticationUI>();
                _uiComponents[SceneType.Login] = _authenticationUI;
                _uiComponents[SceneType.Register] = _authenticationUI; // 认证UI处理登录和注册

                FlaxEngine.Debug.Log($"UI组件初始化完成，共创建{_uiComponents.Count}个UI组件");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"初始化UI组件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeEvents()
        {
            if (_stateManager != null)
            {
                _stateManager.SceneChanged += OnSceneChanged;
                _stateManager.LoadingStateChanged += OnLoadingStateChanged;
                FlaxEngine.Debug.Log("已订阅状态管理器事件");

                // 立即同步当前场景状态，处理脚本启动时场景已是 GameWorld 的情况
                // 避免 OnStart 执行晚于场景切换事件导致 InitializeInkWashUI 永不调用
                OnSceneChanged(SceneType.Start, _stateManager.CurrentScene);
            }

            // 关卡真正加载完成这一"事实"也必须驱动 UI 收敛：
            // 逻辑态可能早已被某条路径置成目标场景，随后的权威完成回调会被
            // UIStateManager.TransitionToScene 的"已在目标场景"分支跳过、事件永不补发
            // （UIStateManager.cs:216），于是偶发"登录后整屏没有界面且不自行恢复"。
            // 订阅 TransitionCompleted 就不依赖逻辑态是否变化。
            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.TransitionCompleted += OnSceneTransitionCompleted;
        }

        /// <summary>
        /// 关卡加载完成（权威时刻）：立刻让水墨 UI 收敛到当前场景，并强制重排内容层。
        /// </summary>
        private void OnSceneTransitionCompleted(SceneType previousScene, SceneType targetScene)
        {
            // 解除自愈节流，本轮收敛不等 0.5 秒
            _nextInkRecoverAt = -1.0;
            TryRecoverInkUI();

            // 外壳可能在 Canvas 尺寸就绪之前建好（内容层按 0 尺寸算过缩放且之后尺寸不再变化，
            // 也就不会再有 OnSizeChanged），这里按当前真实尺寸补一次重排。
            _inkPageShell?.RefreshContentLayout();
        }

        /// <summary>
        /// 场景切换事件处理
        /// </summary>
        private void OnSceneChanged(SceneType previousScene, SceneType newScene)
        {
            FlaxEngine.Debug.Log($"主UI管理器处理场景切换: {previousScene} -> {newScene}");

            try
            {
                // 离开 GameWorld 场景时隐藏水墨 UI（不销毁，保留控件树与页面状态）。
                // 选角 / 创角场景同样要隐藏：水墨壳现在会在进世界之前就被创建，
                // 若切回登录等场景时不收起，行囊页会直接盖在登录界面上。
                // 例外：逻辑状态"倒退到 Start"而真实场景仍停在 CC/世界时不能收 ——
                // 那是 UIStateManager 随 RootScene 重载重建后的假信号，收了屏幕就只剩天空。
                var realScene = GameSceneManager.Instance?.CurrentSceneType ?? newScene;
                bool stateRegressionWhileStillInWorld =
                    newScene == SceneType.Start
                    && (realScene == SceneType.GameWorld || IsCcFlowScene(realScene));

                if (!stateRegressionWhileStillInWorld
                    && (previousScene == SceneType.GameWorld || IsCcFlowScene(previousScene)))
                {
                    HideInkWashUI();
                }

                // 隐藏之前的UI
                HidePreviousUI(previousScene);

                // 显示新的UI
                ShowNewUI(newScene);

                // 进入 GameWorld 场景时初始化并激活水墨 UI（不再激活 GameMainUI）
                if (newScene == SceneType.GameWorld)
                {
                    InitializeInkWashUI();

                    // 水墨外壳若是在选角 / 创角场景里建的，内容层还停在创角页上，
                    // 进世界时要把内容层切回枢纽 HUD。
                    if (_inkPageRouter != null && IsCcFlowScene(previousScene))
                        _inkPageRouter.NavigateToHud();
                }
                else if (IsCcFlowScene(newScene) && UseInkCharacterFlow)
                {
                    // 选角 / 创角改走水墨页面：建壳并直接以角色选择页为首页。
                    EnsureCcFlowSubscriptions();

                    // 自测入口（标记文件控制）：创角链要服务端角色，自动化环境里常常没有，
                    // 存在 %APPDATA%/HundunWorld/open-sculpt.on 时直接落在捏脸页。
                    // 文件不存在时与原来完全一致，不影响任何业务判定。
                    InitializeInkWashUI(HasSelfTestSculptEntry() ? InkPageDomIds.CcFaceSculpt : InkPageDomIds.CcRoleList);
                }

                // 更新当前活动UI
                UpdateCurrentActiveUI(newScene);

                FlaxEngine.Debug.Log($"场景切换处理完成: {newScene}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理场景切换时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 隐藏之前的UI
        /// </summary>
        private void HidePreviousUI(SceneType previousScene)
        {
            if (previousScene == SceneType. Start)
                return; // 初始状态无需隐藏
                
            if (_uiComponents.TryGetValue(previousScene, out var previousUI) && previousUI != null)
            {
                try
                {
                    // 调用相应UI的隐藏方法
                    switch (previousScene)
                    {
                        case SceneType.Login:
                        case SceneType.Register :
                            if (_authenticationUI != null)
                                _authenticationUI.HideAuthenticationUI();
                            break;
                        default:
                            // 对于其他UI组件，它们通过事件监听自动处理显示/隐藏
                            break;
                    }
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"隐藏UI {previousScene} 时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 显示新的UI
        /// </summary>
        private void ShowNewUI(SceneType newScene)
        {
            if (_uiComponents.TryGetValue(newScene, out var newUI) && newUI != null)
            {
                try
                {
                    // 调用相应UI的显示方法
                    switch (newScene)
                    {
                        case SceneType.Login:
                        case SceneType.Register :
                            if (_authenticationUI != null)
                                _authenticationUI.ShowAuthenticationUI();
                            break;
                        default:
                            // 其他UI组件通过事件监听自动处理
                            break;
                    }
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"显示UI {newScene} 时出错: {ex.Message}");
                }
            }
            else
            {
                // 不是故障：_uiComponents 是旧版"每个场景一个 UI 组件"的注册表，
                // 而 Start / CharacterSelection 等场景的界面现在统一由水墨 Ink 页面路由
                // （_inkPageRouter）呈现，这里查不到注册项属于常态。
                FlaxEngine.Debug.Log($"[MainUIManager] 场景 {newScene} 无旧版 UI 注册项，由 Ink 页面路由呈现");
            }
        }

        /// <summary>
        /// 更新当前活动UI
        /// </summary>
        private void UpdateCurrentActiveUI(SceneType newScene)
        {
            if (_uiComponents.TryGetValue(newScene, out var newActiveUI))
            {
                _currentActiveUI = newActiveUI;
            }
        }

        /// <summary>
        /// 加载状态变化事件处理
        /// </summary>
        private void OnLoadingStateChanged(bool isLoading)
        {
            // 可以在这里实现全局的加载状态显示
            FlaxEngine.Debug.Log($"全局加载状态变化: {isLoading}");
        }

        // ===================================================================
        // 水墨主题 UI（InkPageShell + InkPageRouter）
        // =======================================================================

        /// <summary>
        /// 初始化水墨主题 UI（InkPageShell + InkPageRouter + 全部页面注册）。
        /// 在进入 GameWorld 场景时调用，创建专用 UICanvas 承载 Ink UI，
        /// 默认导航到战斗 HUD 页面。所有初始化代码 try/catch 包裹，失败记录日志但不抛异常。
        /// </summary>
        private void InitializeInkWashUI(string initialPageDomId = null)
        {
            try
            {
                // 确保 MainUIManager 处于启用状态，以便 OnUpdate 能正常调用
                // （RootScene 中初始 Enabled=false，进入 GameWorld 后需要启用以驱动动态数据刷新）
                if (!Enabled)
                {
                    Enabled = true;
                    FlaxEngine.Debug.Log("[MainUIManager] 已启用 MainUIManager（进入 GameWorld）");
                }

                // 幂等检测：若水墨 UI 已存在且健康，仅恢复可见性并返回，不重建控件树
                if (_inkPageShell != null && IsInkUIHealthy())
                {
                    _inkPageShell.Visible = true;
                    if (_inkCanvas?.GUI != null)
                        _inkCanvas.GUI.Visible = true;

                    // 复用已有外壳时，内容层还停在上次访问的页面上，需切到调用方期望的页面。
                    if (!string.IsNullOrEmpty(initialPageDomId))
                        NavigateToPage(initialPageDomId);

                    _inkPageShell.RefreshContentLayout();
                    EnsureActionBar();

                    FlaxEngine.Debug.Log("[MainUIManager] 水墨 UI 已存在，仅恢复可见性");
                    return;
                }

                // 1. 查找或创建专用 UICanvas
                _inkCanvas = FindOrCreateInkUICanvas();
                if (_inkCanvas?.GUI == null)
                {
                    FlaxEngine.Debug.LogError("[MainUIManager] Ink UICanvas.GUI 为 null，无法初始化水墨 UI");
                    return;
                }

                // 1b. 创建角色头顶名牌宿主容器：挂在水墨 Canvas 根下，
                //     各角色上的 NameplateUI 组件把名牌控件挂到此处（画布末尾 → 绘制在最上层）。
                if (_nameplateHost == null)
                {
                    _nameplateHost = new ContainerControl
                    {
                        AnchorPreset = AnchorPresets.StretchAll,
                        Visible = false,
                    };
                    _inkCanvas.GUI.AddChild(_nameplateHost);
                    NameplateHost.Current = _nameplateHost;
                }

                // 2. 初始化水墨主题字体
                InkWashTheme.InitializeFonts();

                // 3. 创建 InkPageShell（全屏拉伸），添加到 UICanvas.GUI
                _inkPageShell = new InkPageShell
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Visible = true,
                };
                _inkCanvas.GUI.AddChild(_inkPageShell);

                // 4. 创建 InkPageRouter，添加到 UICanvas.GUI，并关联 Shell
                _inkPageRouter = new InkPageRouter();
                _inkCanvas.GUI.AddChild(_inkPageRouter);
                // 枢纽切换：默认主界面 HUD 使用传统式（含完整系统导航栏/任务追踪）；
                // "combat-hud" 请求与所有 back-hud 出口经路由器别名解析到本枢纽。
                _inkPageRouter.Initialize(_inkPageShell, InkPageDomIds.CombatHudTraditional);

                // 4b. 底部滚轮技能栏 + 拖拽幽灵层（跨页面常驻，菜单页里也能往栏上拖放）
                EnsureActionBar();

                // 5. 关联粒子动效系统到路由器，订阅 PanelShow 事件自动触发墨韵涟漪。
                //    粒子层由 InkPageShell 在构造时挂载（z-index 5，介于内容层与返回按钮之间），
                //    此处仅需建立路由器 → 粒子系统的事件订阅链路。
                if (_inkPageShell.ParticleSystem != null)
                {
                    _inkPageShell.ParticleSystem.Initialize(_inkPageRouter);
                    // 默认开启环境微粒持续飘动，营造水墨江湖氛围。
                    // 战斗 HUD 场景下可由 CombatHudPage 显式调用 StopAmbient 暂停。
                    _inkPageShell.ParticleSystem.StartAmbient();
                    FlaxEngine.Debug.Log("[MainUIManager] 粒子动效系统已初始化并开启环境微粒");
                }
                else
                {
                    FlaxEngine.Debug.LogWarning("[MainUIManager] InkPageShell.ParticleSystem 为 null，粒子动效将不可用");
                }

                // 6. 注册全部 19 个页面
                RegisterInkPages();

                // 7. 默认导航到战斗 HUD；选角 / 创角场景由调用方指定首页，避免先建 HUD 再跳走的闪帧。
                string initialPage = string.IsNullOrEmpty(initialPageDomId) ? InkPageDomIds.CombatHudTraditional : initialPageDomId;
                FlaxEngine.Debug.Log($"[MainUIManager] 准备导航到 {initialPage}...");
                bool navSuccess = NavigateToPage(initialPage); // 默认页：传统式战斗 HUD（主界面）
                if (!navSuccess)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] 首次导航到 {initialPage} 失败，Ink UI 可能仅显示空背景");
                }

                FlaxEngine.Debug.Log($"[MainUIManager] 水墨主题 UI 初始化完成，Canvas.ChildrenCount={_inkCanvas.GUI.ChildrenCount}, 导航结果={navSuccess}");

                // 内容层缩放只在尺寸变化事件里重算。若外壳是在 Canvas 尺寸就绪之前建起来的
                // （InkPageShell.UpdateContentLayout 对 0 尺寸直接 return），而之后尺寸不再"变化"
                // （画布已经是最终值），就不会再有事件来补算 → 表现是界面缺一块/位置不对。
                // 建完立刻按当前真实尺寸补一次，幂等且零成本。
                _inkPageShell?.RefreshContentLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 初始化水墨 UI 时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 建好（或复用）底部滚轮技能栏与拖拽幽灵层。
        /// 技能栏作为外壳的常驻子节点存在（与返回按钮同一手法）：不属于内容层，
        /// 页面导航不会销毁它，所以打开背包/武学面板时依然能把图标拖到栏上。
        /// </summary>
        private void EnsureActionBar()
        {
            try
            {
                if (_inkCanvas?.GUI == null || _inkPageShell == null) return;

                if (_inkActionBar == null || _inkActionBar.Parent == null)
                {
                    _inkActionBar = new InkActionBarControl();
                    _inkActionBar.AttachTo(_inkPageShell);
                    _inkActionBar.Visible = false;
                }

                if (_inkCycleIndicator == null || _inkCycleIndicator.Parent == null)
                {
                    _inkCycleIndicator = new InkWuxingCycleIndicator();
                    _inkCycleIndicator.AttachTo(_inkPageShell);
                    _inkCycleIndicator.Visible = false;
                }

                if (_inkDragGhost == null || _inkDragGhost.Parent == null)
                {
                    // 最后添加 → 画布最上层，拖拽时幽灵不被任何页面遮挡。
                    _inkDragGhost = new InkDragGhost { AnchorPreset = AnchorPresets.TopLeft };
                    _inkCanvas.GUI.AddChild(_inkDragGhost);
                    ActionBarDrag.AttachGhost(_inkDragGhost);
                }

                if (!_actionBarDataEventsSubscribed)
                {
                    Services.SkillCatalogService.Instance.Changed += OnActionBarSourceDataChanged;
                    Services.PlayerInventoryService.Instance.Changed += OnActionBarSourceDataChanged;
                    _actionBarDataEventsSubscribed = true;
                }

                ActionBarInputGate.UiRoot = _inkCanvas.GUI;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 技能栏初始化失败: {ex.Message}");
            }
        }

        /// <summary>技能目录或背包发生变化：槽位上的名称/数量/可绑定列表要跟着重解析。</summary>
        private void OnActionBarSourceDataChanged()
        {
            ActionBarService.Instance.InvalidateDisplays();
        }

        /// <summary>
        /// 技能栏的可见性与布局载入：只在本地玩家就绪（人在世界里）时显示，
        /// 选角/创角页面不出现；按当前角色 id 载入其专属的 60 槽配置。
        /// </summary>
        private void UpdateActionBar()
        {
            if (_inkActionBar == null) return;

            // 滚轮技能栏是战斗 HUD 的常驻件：菜单/整页界面会铺满设计空间，栏压在上面会盖住页面底部
            // （2026-10-29 设置页截图实测）。弹层仍算 HUD 场景——底页还是 HUD 时把栏留着。
            bool hudContext = IsHudPageDomId(_inkPageRouter?.CurrentPageDomId)
                || (IsOverlayOpen && IsHudPageDomId(_lastKnownDomId));
            // 自测触发过一次"恢复"后就不再自动隐藏，便于观察恢复后的稳定态
            bool autoHideOn = !_selfTestAutoHideRestored
                && (HasSelfTestMarker("ui-autohide-test.on")
                    || Services.GameConfigurationService.Instance.GetAutoHideUi());
            float idleLimit = HasSelfTestMarker("ui-autohide-test.on") ? 2f : UiAutoHideSeconds;
            bool idleHidden = autoHideOn && _uiIdleSeconds >= idleLimit;

            bool show = _localPlayerReady && !_photoOverlayHidden && hudContext && !idleHidden;
            if (_inkActionBar.Visible != show)
                _inkActionBar.Visible = show;
            if (_inkCycleIndicator != null && _inkCycleIndicator.Visible != show)
                _inkCycleIndicator.Visible = show;

            if (!show) return;

            ulong characterId = HundunWorldGame.Instance?.PlayerId ?? 0UL;
            if (characterId != 0UL)
                ActionBarLayout.Instance.LoadForCharacter(characterId);
        }

        /// <summary>
        /// 把 Ink 菜单的开合状态同步给 NarrativePro 的 CommonUI 输入栈。
        /// 菜单层在时 <c>bGameInputBlocked</c> 为真，世界侧 <c>[T] 上车</c> 一类交互提示
        /// 与游戏按键随之让位；回到战斗 HUD 且无弹层后恢复。
        /// </summary>
        private void SyncWorldInputGate()
        {
            _inkWorldInputGate ??= new InkWorldInputGate();
            _inkWorldInputGate.Sync(_inkPageRouter);
        }

        /// <summary>
        /// 本地玩家死亡态边沿 → 阵亡界面。
        /// <para>
        /// 死亡是服务端权威状态（同步系统把 <c>CharacterAttributesComponent.IsDead</c> 写进本地玩家），
        /// 所以这里只做"状态翻转 → 弹/收界面"，不自己判定生死。玩家在阵亡界面点「返回」自行关掉后，
        /// 本次死亡不再自动弹回，直到复活后再次阵亡。
        /// </para>
        /// </summary>
        private void WatchLocalPlayerDeath()
        {
            try
            {
                var attrs = HundunWorldGame.Instance?.LocalPlayerActor?
                    .GetScript<CharacterAttributesComponent>();
                bool dead = attrs != null && attrs.IsDead;
                var router = _inkPageRouter;
                if (router == null)
                    return;

                if (dead && !_localPlayerDead)
                {
                    _localPlayerDead = true;
                    _deathScreenDismissed = false;

                    // 只在战斗 HUD 上下文弹：选角/创角/其它全屏页上弹阵亡界面只会打断它们。
                    string current = router.CurrentPageDomId;
                    bool hudContext = !string.IsNullOrEmpty(current)
                        && router.GetFrame(current).Kind == InkPageKind.Hud;
                    if (hudContext && !router.HasOverlay)
                        NavigateToPage(InkPageDomIds.DeathScreen);
                }
                else if (!dead && _localPlayerDead)
                {
                    _localPlayerDead = false;
                    _deathScreenDismissed = false;
                    if (router.CurrentOverlayDomId == InkPageDomIds.DeathScreen)
                        router.CloseOverlay();
                }
                else if (dead && _localPlayerDead
                         && !_deathScreenDismissed
                         && router.CurrentOverlayDomId != InkPageDomIds.DeathScreen)
                {
                    // 玩家自己关掉了阵亡界面（返回/破招后仍在死亡态）：本次死亡不再弹回。
                    _deathScreenDismissed = true;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[MainUIManager] 死亡态监听失败: {ex.Message}");
            }
        }

        /// <summary>空闲判定：鼠标不动 + 本地角色没位移，累加计时；任一动就清零。</summary>
        private void TrackUiIdleActivity(float deltaTime)
        {
            var mouse = FlaxEngine.Input.MousePosition;
            var local = HundunWorldGame.Instance?.LocalPlayerActor;
            Vector3 pos = local != null ? local.Position : Vector3.Zero;

            bool moved = (mouse - _lastUiMousePos).LengthSquared > 1f
                         || (local != null && (pos - _lastUiPlayerPos).LengthSquared > 0.0004f);

            // 自测节拍：2 秒隐藏后，第 4 秒模拟一次"有动作"，验证恢复分支（自动化里动不了游戏鼠标）
            if (HasSelfTestMarker("ui-autohide-test.on") && !_selfTestAutoHideRestored && _uiIdleSeconds >= 4f)
            {
                _selfTestAutoHideRestored = true;
                moved = true;
            }

            _lastUiMousePos = mouse;
            _lastUiPlayerPos = pos;
            _uiIdleSeconds = moved ? 0f : _uiIdleSeconds + deltaTime;
        }

        private static bool IsHudPageDomId(string domId)
        {
            return domId == InkPageDomIds.CombatHudTraditional
                || domId == InkPageDomIds.CombatHudV2
                || domId == InkPageDomIds.CombatHud;
        }

        /// <summary>
        /// 查找或创建承载水墨 UI 的专用 UICanvas。        /// 关键约束：主 UI 必须使用名为 InkWashUICanvas 的独立 Canvas，禁止与其他 UI
        /// （AuthenticationUI 的 MainUICanvas / GameMainUI 的 GameMainUICanvas 等）共用。
        /// 所有查找分支均按名称 InkWashUICanvas 精确匹配，禁止通用 GetScript/GetChild 查找。
        /// </summary>
        /// <returns>UICanvas 实例，失败时返回 null</returns>
        private UICanvas FindOrCreateInkUICanvas()
        {
            const string InkCanvasName = "InkWashUICanvas";
            UICanvas uiCanvas = null;

            // 方式1: 从 Actor 自身查找（仅当 Actor 自身就是 InkWashUICanvas 容器时才返回其 UICanvas）
            if (Actor != null && Actor.Name == InkCanvasName)
            {
                uiCanvas = Actor.GetScript<UICanvas>();
                if (uiCanvas != null)
                {
                    FlaxEngine.Debug.Log($"[MainUIManager] Ink UICanvas 命中方式1（Actor 自身）: {uiCanvas.Name}");
                }
            }

            // 方式2: 从 Actor 子级查找（遍历 Children，仅匹配名为 InkWashUICanvas 的子 Actor）
            if (uiCanvas == null && Actor != null)
            {
                var children = Actor.Children;
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child != null && child.Name == InkCanvasName)
                        {
                            var c = child.GetScript<UICanvas>();
                            if (c != null)
                            {
                                uiCanvas = c;
                                FlaxEngine.Debug.Log($"[MainUIManager] Ink UICanvas 命中方式2（Actor 子级）: {uiCanvas.Name}, Actor={child.Name}");
                                break;
                            }
                        }
                    }
                }
            }

            // 方式3: 从父 Actor 查找（仅当 Parent 自身就是 InkWashUICanvas 容器时才返回其 UICanvas）
            if (uiCanvas == null && Actor?.Parent != null && Actor.Parent.Name == InkCanvasName)
            {
                uiCanvas = Actor.Parent.GetScript<UICanvas>();
                if (uiCanvas != null)
                {
                    FlaxEngine.Debug.Log($"[MainUIManager] Ink UICanvas 命中方式3（父 Actor）: {uiCanvas.Name}");
                }
            }

            // 方式4: 从场景中查找（同时按 Name == InkWashUICanvas 和 Scene == Actor.Scene 过滤，
            // 避免命中子场景中的同名 Canvas 或其他 UI 的 Canvas）
            if (uiCanvas == null && Actor?.Scene != null)
            {
                var sceneCanvases = Level.GetActors<UICanvas>();
                if (sceneCanvases != null)
                {
                    foreach (var c in sceneCanvases)
                    {
                        if (c != null
                            && c.Name == InkCanvasName
                            && c.Scene == Actor.Scene)
                        {
                            uiCanvas = c;
                            FlaxEngine.Debug.Log($"[MainUIManager] Ink UICanvas 命中方式4（场景查找）: {uiCanvas.Name}, Scene={c.Scene?.Name}");
                            break;
                        }
                    }
                }
            }

            // 方式5: 从 Level 全局查找（同方式4过滤条件，不再使用「用第一个」兜底）
            if (uiCanvas == null && Actor?.Scene != null)
            {
                var allCanvases = Level.GetActors<UICanvas>();
                if (allCanvases != null && allCanvases.Length > 0)
                {
                    foreach (var c in allCanvases)
                    {
                        if (c != null
                            && c.Name == InkCanvasName
                            && c.Scene == Actor.Scene)
                        {
                            uiCanvas = c;
                            FlaxEngine.Debug.Log($"[MainUIManager] Ink UICanvas 命中方式5（Level 全局）: {uiCanvas.Name}, Scene={c.Scene?.Name}");
                            break;
                        }
                    }
                }
            }

            // 方式6: 自动创建专用 UICanvas（始终挂到 MainUIManager.Actor 下，确保属于 RootScene）
            if (uiCanvas == null)
            {
                FlaxEngine.Debug.LogWarning("[MainUIManager] 未找到 InkWashUICanvas，自动创建专用 Canvas");

                var canvasActor = new EmptyActor { Name = InkCanvasName };
                // 强制挂载到 MainUIManager.Actor 下，确保属于 RootScene 且随 MainUIManager 生命周期
                canvasActor.Parent = Actor;

                uiCanvas = canvasActor.AddChild<UICanvas>();
                uiCanvas.Name = InkCanvasName;

                // 显式设置 GUI 尺寸，避免首帧尺寸为 0 导致 StretchAll 子控件不可见
                var screenSize = FlaxEngine.Screen.Size;
                if (screenSize.X > 0f && screenSize.Y > 0f)
                {
                    uiCanvas.GUI.Size = screenSize;
                }

                FlaxEngine.Debug.Log($"[MainUIManager] 已创建专用 Ink UICanvas: Actor={canvasActor.Name}, Parent={Actor?.Name}");
            }

            // 确保设置为 ScreenSpace 模式
            if (uiCanvas != null && uiCanvas.RenderMode != CanvasRenderMode.ScreenSpace)
            {
                uiCanvas.RenderMode = CanvasRenderMode.ScreenSpace;
            }

            // 关键修复：无论 UICanvas 是找到的还是新建的，都强制同步 GUI 尺寸为屏幕尺寸。
            // 否则使用旧 Canvas 时 GUI.Size 可能为 0，导致 StretchAll 子控件无法布局。
            if (uiCanvas?.GUI != null)
            {
                var screenSize = FlaxEngine.Screen.Size;
                if (screenSize.X > 0f && screenSize.Y > 0f)
                {
                    uiCanvas.GUI.Size = screenSize;
                }
                uiCanvas.GUI.AnchorPreset = AnchorPresets.StretchAll;
                uiCanvas.GUI.Offsets = Margin.Zero;
                FlaxEngine.Debug.Log($"[MainUIManager] Ink UICanvas 已就绪: GUI.Size={uiCanvas.GUI.Size}, RenderMode={uiCanvas.RenderMode}");
            }

            return uiCanvas;
        }

        /// <summary>
        /// 向 InkPageRouter 注册全部 19 个页面工厂。
        /// 涵盖战斗 HUD、加载页、章节过场、菜单、弹窗、奖励、设置，
        /// 并通过事件订阅打通完整导航链路。
        /// </summary>
        private void RegisterInkPages()
        {
            if (_inkPageRouter == null)
            {
                FlaxEngine.Debug.LogWarning("[MainUIManager] InkPageRouter 为 null，无法注册页面");
                return;
            }

            // 每个页面在注册处声明自己的框架描述符（InkPageFrame）：Hud=战斗HUD（不铺墨底/暗角、无外壳返回）；
            // Immersive=沉浸式全屏页（外壳铺墨底+柔暗角+左上角统一返回）；Modal=模态弹层（不铺全屏压淡，由外壳叠加层承载）。
            // 这是"哪些层给哪些页"的唯一来源，取代了此前按 popup- 前缀猜 + 两张手写白名单的做法。
            _inkPageRouter.RegisterPage(InkPageDomIds.CombatHud, () => CreateCombatHud(), InkPageFrame.Hud);
            _inkPageRouter.RegisterPage(InkPageDomIds.CombatHudTraditional, () => CreateCombatHudTraditional(), InkPageFrame.Hud);
            // 过场页自铺全屏过场图与专属暗角（EdgeOpacity 自定义），只借外壳墨底遮信箱黑边，故按自铺舞台处理。
            _inkPageRouter.RegisterPage(InkPageDomIds.Loading1, () => CreateLoadingPage1(), InkPageFrame.SelfLitImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.Loading2, () => CreateLoadingPage2(), InkPageFrame.SelfLitImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.ChapterTransition, () => CreateChapterTransition(), InkPageFrame.SelfLitImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavQuests, () => CreateMenuQuests(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavShop, () => CreateMenuShop(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupItemAcquired, () => CreatePopupItemAcquired(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupMessage, () => CreatePopupMessage(), InkPageFrame.Modal);
            // 奖励/成就解锁/升级这一族按设计源是浮层模态（yy-reward-system 的 reward-overlay + *-modal，
            // role=dialog aria-modal），不是全屏页，故登记为 Modal 走叠加层。
            _inkPageRouter.RegisterPage(InkPageDomIds.RewardAchievement, () => CreateRewardAchievement(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.RewardQuestComplete, () => CreateRewardQuestComplete(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSettings, () => CreateSettings(), InkPageFrame.ImmersiveOwnBack);
            // Esc 系统菜单：模态叠加层，不进返回栈——再按一次 Esc 就是关掉它。
            _inkPageRouter.RegisterPage(InkPageDomIds.SystemMenu, () => CreateSystemMenu(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.CombatHudV2, () => CreateCombatHudV2(), InkPageFrame.Hud);
            // 角色页三合一：nav-character-v2 / nav-inventory / nav-character-panel 全部指向 CharacterPanelPage，
            // 原 MenuCharAttributesV2Page（V2 属性页）与 MenuCharAttributesPage（V1）已随本次合并删除。
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCharacterV2, () => CreateCharacterPanelPage(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.DeathScreen, () => CreateDeathScreen(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.DialogueConfirm, () => CreateDialogueConfirm(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavBattlePrep, () => CreateMenuBattlePrep(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.Acupoint, () => CreateAcupoint(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.Qte, () => CreateQte(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.RewardLevelUp, () => CreateRewardLevelUp(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupVerification, () => CreatePopupVerification(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupMartialArts, () => CreatePopupMartialArts(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupSkillRealization, () => CreatePopupSkillRealization(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupMartialDetail, () => CreatePopupMartialDetail(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupGuideSide, () => CreatePopupGuideSide(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupBestiarySide, () => CreatePopupBestiarySide(), InkPageFrame.Modal);

            // 注册新增页面（串联所有 UI）
            _inkPageRouter.RegisterPage(InkPageDomIds.NavAppearance, () => CreateMenuAppearance(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavActivities, () => CreateMenuActivities(), InkPageFrame.ImmersiveOwnBack);
            // 邮件入口合并：nav-mail（商店/任务侧栏）与 nav-social-mail（社交枢纽）统一指向 SocialMailPage，
            // 原 MenuMailPage 已随本次合并删除。
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMail, () => CreateSocialMailPage(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSect, () => CreateMenuSect(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavTeam, () => CreateMenuTeam(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavBestiary, () => CreateMenuBestiary(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavBattlePass, () => CreateMenuBattlePass(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavGacha, () => CreateMenuGacha(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavLivelihood, () => CreateMenuLivelihood(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavPersonalInfo, () => CreateMenuPersonalInfo(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMartialRecord, () => CreateMenuMartialRecord(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCasualMode, () => CreateMenuCasualMode(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavTime, () => CreateMenuTime(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavShopRare, () => CreateShopRareItems(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMultiplayer, () => CreateMultiplayer(), InkPageFrame.Immersive);
            // 取景器式相机页自带更强的暗角（EdgeOpacity 0.5）与专属晕染，故按自铺舞台处理。
            _inkPageRouter.RegisterPage(InkPageDomIds.NavPhotoMode, () => CreatePhotoMode(), InkPageFrame.ImmersiveTransparent);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavPhotoAlbum, () => CreatePhotoAlbum(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavElementVision, () => CreateElementVision(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.CcRoleList, () => CreateCcRoleList(), InkPageFrame.SelfLitImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.CcGender, () => CreateCcGender(), InkPageFrame.SelfLitImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.CcFacePreset, () => CreateCcFacePreset(), InkPageFrame.SelfLitImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.CcFaceSculpt, () => CreateCcFaceSculpt(), InkPageFrame.SelfLitImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.CcName, () => CreateCcName(), InkPageFrame.SelfLitImmersiveOwnBack);
            // 音频设置页自带顶栏「返回战斗」入口，声明 OwnBack 以隐藏外壳返回，避免两个返回钮叠在同一角。
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSettingsAudio, () => CreateSettingsAudio(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSettingsRoadSign, () => CreateSettingsRoadSign(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavEquipment, () => CreateMenuEquipment(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.UIGallery, () =>CreateUIGallery(), InkPageFrame.Immersive);
            // 引导模块（三页：引导 HUD / 动作引导 / 武学引导）——此前未注册导致整模块不可达，本次落地。
            _inkPageRouter.RegisterPage(InkPageDomIds.GuideHud, () => CreateGuideHud(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.GuideAction, () => CreateGuideAction(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.GuideMartial, () => CreateGuideMartial(), InkPageFrame.Modal);
            // 统一导航串联：补全未注册页面（奖励全链、小游戏完成、角色属性 V1）。
            _inkPageRouter.RegisterPage(InkPageDomIds.MenuCharAttributes, () => CreateCharacterPanelPage(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.MiniGameComplete, () => CreateMiniGameComplete(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.RewardTeleportUnlock, () => CreateRewardTeleportUnlock(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.RewardMapUnlock, () => CreateRewardMapUnlock(), InkPageFrame.Modal);
            _inkPageRouter.RegisterPage(InkPageDomIds.RewardCongratulations, () => CreateRewardCongratulations(), InkPageFrame.Modal);

            // === 角色与背包子系统（game-ui-system 设计方案要求的 6 个页面） ===
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCharacterPanel, () => CreateCharacterPanelPage(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSkillTree, () => CreateSkillTreePage(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavInventory, () => CreateCharacterPanelPage(), InkPageFrame.Immersive); // 背包已合并进角色面板
            _inkPageRouter.RegisterPage(InkPageDomIds.NavEquipmentEnhance, () => CreateEquipmentEnhancePage(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCrafting, () => CreateCraftingPage(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMountPet, () => CreateMountPetPage(), InkPageFrame.Immersive);

            // === 任务与技能子系统（game-ui-system 设计方案要求的 4 个页面） ===
            // 任务入口合并：nav-quest-log 与 nav-quests 同页（以"任务"页 MenuQuestsPage 为基准），
            // 原 QuestLogPage 已随本次合并删除。
            _inkPageRouter.RegisterPage(InkPageDomIds.NavQuestLog, () => CreateMenuQuests(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavWorldMap, () => CreateWorldMapPage(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCompass, () => CreateCompassPage(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavDungeonEntry, () => CreateDungeonEntryPage(), InkPageFrame.ImmersiveOwnBack);

            // === 社交与商城子系统（game-ui-system 设计方案要求的 7 个页面） ===
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSocialGuild, () => CreateSocialGuildPage(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavFriends, () => CreateFriendsPage(), InkPageFrame.ImmersiveOwnBack);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSocialMail, () => CreateSocialMailPage(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMentor, () => CreateMentorPage(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavLeaderboard, () => CreateLeaderboardPage(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.NavAchievement, () => CreateAchievementPage(), InkPageFrame.Immersive);
            // 商店入口合并：nav-social-shop（社交枢纽）指向 MenuShopPage，原 SocialShopPage 已随本次合并删除。
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSocialShop, () => CreateMenuShop(), InkPageFrame.ImmersiveOwnBack);

            // NPC 对话菜单：模态浮层。点 NPC 时由交互层写入"这位能做什么"，这里只负责摆出来。
            _inkPageRouter.RegisterPage(InkPageDomIds.NavNpcGossip, () => CreateNpcGossip(), InkPageFrame.Modal);

            // === 水墨动画分镜参考（hundun-yy-ui 设计稿 ink-anim-ref-1/2.html） ===
            _inkPageRouter.RegisterPage(InkPageDomIds.InkAnimRefSpread, () => CreateInkAnimRefSpread(), InkPageFrame.Immersive);
            _inkPageRouter.RegisterPage(InkPageDomIds.InkAnimRefBrush, () => CreateInkAnimRefBrush(), InkPageFrame.Immersive);
        }

        /// <summary>
        /// 创建水墨动画分镜参考页 01（水墨晕染，ink-anim-ref-1.html）。
        /// 该页按设计稿规格实时播放松散晕染动画，供逐帧走查验证。
        /// </summary>
        private InkAnimRefSpreadPage CreateInkAnimRefSpread()
        {
            return new InkAnimRefSpreadPage();
        }

        /// <summary>
        /// 创建水墨动画分镜参考页 02（水墨笔触，ink-anim-ref-2.html）。
        /// 该页按设计稿规格实时播放笔触路径动画，供逐帧走查验证。
        /// </summary>
        private InkAnimRefBrushPage CreateInkAnimRefBrush()
        {
            return new InkAnimRefBrushPage();
        }

        /// <summary>
        /// 尝试获取本地玩家的角色属性组件。
        /// null 安全：Instance/LocalPlayerActor/GetScript 任一为 null 时返回 false。
        /// </summary>
        private bool TryGetLocalPlayerAttributes(out CharacterAttributesComponent component)
        {
            component = null;
            try
            {
                var game = HundunWorldGame.Instance;
                var actor = game?.LocalPlayerActor;
                if (actor == null)
                    return false;
                component = actor.GetScript<CharacterAttributesComponent>();
                if (component == null)
                {
                    FlaxEngine.Debug.LogWarning("[MainUIManager] LocalPlayerActor 未挂载 CharacterAttributesComponent");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] TryGetLocalPlayerAttributes 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 挑一条用于武学详情弹窗的招式：优先已学（按 id 升序取第一条），否则取目录首条。
        /// </summary>
        private static bool TryPickMartialForDetail(out SkillConfig cfg, out int learnedLevel)
        {
            cfg = null;
            learnedLevel = 0;
            try
            {
                var catalog = Services.SkillCatalogService.Instance;
                foreach (var pair in catalog.Learned)
                {
                    var candidate = SkillDatabase.GetSkill(pair.Key);
                    if (candidate == null)
                        continue;
                    if (cfg == null || candidate.SkillId < cfg.SkillId)
                    {
                        cfg = candidate;
                        learnedLevel = pair.Value.Level;
                    }
                }
                if (cfg != null)
                    return true;

                var all = SkillDatabase.GetAllSkills();
                if (all == null || all.Count == 0)
                    return false;
                foreach (var candidate in all)
                    if (cfg == null || candidate.SkillId < cfg.SkillId)
                        cfg = candidate;
                return cfg != null;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 武学详情取数失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 尝试获取本地玩家的技能数组。
        /// 使用 GetScripts&lt;SkillBase&gt;() 获取所有挂载的技能。
        /// </summary>
        private bool TryGetLocalPlayerSkills(out SkillBase[] slots)
        {
            slots = null;
            try
            {
                var game = HundunWorldGame.Instance;
                var actor = game?.LocalPlayerActor;
                if (actor == null)
                    return false;
                var arr = actor.GetScripts<SkillBase>();
                if (arr == null || arr.Length == 0)
                    return false;
                slots = arr;
                return true;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] TryGetLocalPlayerSkills 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检测水墨 UI 是否处于健康状态（Canvas 与 Shell 均非 null 且可访问）。
        /// 用于 <see cref="InitializeInkWashUI"/> 的幂等判断与 <see cref="OnUpdate"/> 的自愈触发。
        /// 注意：Flax 的 <see cref="Actor"/> 与 <see cref="Control"/> 不暴露 IsDisposed 属性，
        /// 这里通过关键属性可达性判断（Actor 销毁后 GUI 通常为 null，Control 销毁后访问 IsDisposing 会抛异常），
        /// 因此用 try/catch 兜底，确保任何异常情况都返回 false 触发自愈。
        /// </summary>
        /// <returns>健康返回 true；任一资源为 null 或已释放返回 false</returns>
        /// <summary>
        /// 水墨 UI 收敛自愈：当前场景应当显示水墨 UI（选角/创角/GameWorld），但壳缺失、
        /// 已损坏或被乱序事件误隐藏时，重建或恢复并导航到期望页面。
        /// 登录→选角偶发"整屏无 UI"的根因是事件竞争（详见 OnUpdate 调用点注释），
        /// 事件侧无法保证不丢，这里做**结果侧收敛**：只要场景对、UI 不在，就补建。
        /// 旧自愈只认 "shell != null 且不健康"，覆盖不了 "shell 从未建起来" 与 "被隐藏" 两种形态。
        /// </summary>
        private void TryRecoverInkUI()
        {
            var scene = _stateManager != null ? _stateManager.CurrentScene : SceneType.Start;
            bool wantsInk = scene == SceneType.GameWorld || IsCcFlowScene(scene);
            if (!wantsInk) return;

            bool shellPresent = _inkPageShell != null;
            bool shellHealthy = shellPresent && IsInkUIHealthy();
            if (shellHealthy && _inkPageShell.Visible) return;   // 正常态，零成本返回

            if (Time.GameTime < _nextInkRecoverAt) return;
            _nextInkRecoverAt = Time.GameTime + InkRecoverIntervalSeconds;

            string expectedPage = IsCcFlowScene(scene) ? InkPageDomIds.CcRoleList : null;

            if (shellHealthy)
            {
                // 只是被乱序事件误隐藏：恢复可见并校正页面，不重建控件树。
                FlaxEngine.Debug.LogWarning($"[MainUIManager] 水墨 UI 在场景 {scene} 被误隐藏，已恢复显示");
                _inkPageShell.Visible = true;
                if (_inkCanvas?.GUI != null) _inkCanvas.GUI.Visible = true;
                if (_inkPageRouter != null)
                {
                    if (expectedPage != null) _inkPageRouter.NavigateTo(expectedPage);
                    else if (!string.IsNullOrEmpty(_lastKnownDomId)) _inkPageRouter.NavigateTo(_lastKnownDomId);
                }
            }
            else
            {
                FlaxEngine.Debug.LogWarning($"[MainUIManager] 水墨 UI 在场景 {scene} 缺失/损坏，触发自愈重建");
                if (shellPresent) DestroyInkWashUI();
                InitializeInkWashUI(expectedPage);
                // GameWorld 重建后回到用户最后所在页（与旧自愈语义一致）；CC 场景固定回选角首页。
                if (expectedPage == null && _inkPageRouter != null && !string.IsNullOrEmpty(_lastKnownDomId))
                    _inkPageRouter.NavigateTo(_lastKnownDomId);
                if (_localPlayerReady && _cachedAttributes != null)
                    RebindActivePageData();
            }
        }

        private bool IsInkUIHealthy()
        {
            try
            {
                if (_inkCanvas == null
                    || _inkCanvas.GUI == null
                    || _inkPageShell == null
                    || _inkPageShell.IsDisposing
                    || _inkPageRouter == null)
                {
                    return false;
                }

                // 只判空不够：首次导航失败时外壳照样建起来了，只是内容层是空的
                // （InitializeInkWashUI 里那句"仅显示空背景"就是这个形态）。
                // 这种"看得见壳、没有页面"若算健康，自愈会永远放过它 → 整屏空白不再恢复。
                if (string.IsNullOrEmpty(_inkPageRouter.CurrentPageDomId))
                    return false;

                return _inkPageShell.ContentLayer != null && _inkPageShell.ContentLayer.ChildrenCount > 0;
            }
            catch
            {
                // 访问已销毁对象的关键属性可能抛异常，视为不健康
                return false;
            }
        }

        /// <summary>
        /// 隐藏水墨主题 UI（保留控件树与页面状态，仅设置 Visible = false）。
        /// 在离开 GameWorld 场景时调用，与 <see cref="DestroyInkWashUI"/> 区分：
        /// 本方法不释放任何资源，仅切换可见性，以便重新进入 GameWorld 时通过
        /// <see cref="InitializeInkWashUI"/> 的幂等分支快速恢复显示。
        /// </summary>
        private void HideInkWashUI()
        {
            try
            {
                if (_inkPageShell != null)
                {
                    _inkPageShell.Visible = false;
                }
                if (_inkCanvas?.GUI != null)
                {
                    _inkCanvas.GUI.Visible = false;
                }
                FlaxEngine.Debug.Log("[MainUIManager] 水墨 UI 已隐藏（保留控件树）");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 隐藏水墨 UI 时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 销毁水墨主题 UI（InkPageShell + InkPageRouter），释放资源。
        /// 在离开 GameWorld 场景或重新初始化时调用。
        /// 保留 _inkCanvas 以便复用，不主动销毁。
        /// </summary>
        private void DestroyInkWashUI()
        {
            try
            {
                if (_inkPageShell != null)
                {
                    _inkPageShell.Dispose();
                    _inkPageShell = null;
                }

                if (_inkPageRouter != null)
                {
                    _inkPageRouter.Dispose();
                    _inkPageRouter = null;
                }

                // 先解除静态宿主引用，再销毁容器（容器销毁会一并释放其中所有角色名牌控件）。
                if (ReferenceEquals(NameplateHost.Current, _nameplateHost))
                    NameplateHost.Current = null;
                if (_nameplateHost != null)
                {
                    _nameplateHost.Dispose();
                    _nameplateHost = null;
                }

                // 技能栏随外壳一并销毁；幽灵层挂在画布根上，要单独收。
                if (_actionBarDataEventsSubscribed)
                {
                    Services.SkillCatalogService.Instance.Changed -= OnActionBarSourceDataChanged;
                    Services.PlayerInventoryService.Instance.Changed -= OnActionBarSourceDataChanged;
                    _actionBarDataEventsSubscribed = false;
                }
                _inkActionBar = null;
                _inkCycleIndicator = null;
                if (_inkDragGhost != null)
                {
                    _inkDragGhost.Dispose();
                    _inkDragGhost = null;
                    ActionBarDrag.AttachGhost(null);
                }
                ActionBarInputGate.UiRoot = null;
                _inkWorldInputGate?.Release();
                _inkWorldInputGate = null;

                // 清理活动页面引用，避免悬空指针
                _activeCombatHud = null;
                _activeCombatHudV2 = null;
                _activeCombatHudTraditional = null;
                _activeCharacterPanel = null;
            _activeSkillTree = null;

                FlaxEngine.Debug.Log("[MainUIManager] 水墨主题 UI 已销毁");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 销毁水墨 UI 时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 自愈重建后对当前活动页面重新执行数据绑定。
        /// 根据 _lastKnownDomId 判断页面类型，对 _activeCombatHud/_activeCombatHudV2/_activeCharacterPanel
        /// 调用对应的 BindCharacter/BindSkills。null 安全，try/catch 包裹。
        /// </summary>
        private void RebindActivePageData()
        {
            try
            {
                if (_cachedAttributes == null) return;

                // 根据最后已知 dom-id 判断页面类型并重绑
                if (_lastKnownDomId == InkPageDomIds.CombatHud && _activeCombatHud != null)
                {
                    _activeCombatHud.BindCharacter(_cachedAttributes);
                    if (_cachedSkills != null) _activeCombatHud.BindSkills(_cachedSkills);
                    FlaxEngine.Debug.Log("[MainUIManager] 自愈后已重绑 CombatHud 数据");
                }
                else if (_lastKnownDomId == InkPageDomIds.CombatHudV2 && _activeCombatHudV2 != null)
                {
                    _activeCombatHudV2.BindCharacter(_cachedAttributes);
                    FlaxEngine.Debug.Log("[MainUIManager] 自愈后已重绑 CombatHudV2 数据");
                }
                else if (_lastKnownDomId == InkPageDomIds.CombatHudTraditional && _activeCombatHudTraditional != null)
                {
                    _activeCombatHudTraditional.BindCharacter(_cachedAttributes);
                    FlaxEngine.Debug.Log("[MainUIManager] 自愈后已重绑 CombatHudTraditional 数据");
                }
                else if ((_lastKnownDomId == InkPageDomIds.NavCharacterPanel
                          || _lastKnownDomId == InkPageDomIds.NavInventory
                          || _lastKnownDomId == InkPageDomIds.NavCharacterV2
                          || _lastKnownDomId == InkPageDomIds.MenuCharAttributes)
                         && _activeCharacterPanel != null)
                {
                    _activeCharacterPanel.BindCharacter(_cachedAttributes);
                    FlaxEngine.Debug.Log("[MainUIManager] 自愈后已重绑 CharacterPanel 数据");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] RebindActivePageData 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 统一的页面导航辅助方法：调用路由器 NavigateTo 并在成功后更新 _lastKnownDomId。
        /// 返回值表示导航是否成功（路由器为 null、NavigateTo 返回 false 或抛异常均返回 false）。
        /// </summary>
        /// <param name="domId">目标页面 dom-id</param>
        /// <returns>导航是否成功</returns>
        private bool NavigateToPage(string domId)
        {
            try
            {
                // 统一导航：特殊动作 id 分流（back-hud → 返回战斗 HUD；cta-enter-world → 进入世界）。
                // 必须在此集中处理：InkPageRouter.NavigateTo 只查页面工厂表，back-hud 未注册会直接告警失败。
                // 装备变更通知（equipment-changed）：MenuEquipmentPage 装备后发出的 mock 状态通知，非导航目标——
                // 在此吞掉并返回 true，避免误触发页面跳转与"未注册 domId"告警。
                if (domId == InkPageDomIds.EquipmentChanged)
                {
                    return true;
                }
                // 战斗入场动作（enter-dungeon 进入秘境 / nav-battle-start 出战）：星型拓扑下无独立战斗场景，
                // 终点恒为枢纽战斗 HUD（战斗现场），与 back-hud 同解。
                // 返回上一层（页面自带返回按钮 / 弹层关闭统一走这里）：先弹返回栈，栈空才回枢纽。
                // 与外壳左上返回按钮、Esc 同一实现，避免"自带按钮直跳枢纽、外壳按钮回来源页"两套语义。
                if (domId == InkPageDomIds.BackOneLevel)
                {
                    if (_inkPageRouter != null && _inkPageRouter.Back())
                    {
                        _lastKnownDomId = _inkPageRouter.CurrentPageDomId ?? _lastKnownDomId;
                        return true;
                    }
                    return false;
                }
                if (domId == InkPageDomIds.BackHud
                    || domId == InkPageDomIds.EnterDungeon
                    || domId == InkPageDomIds.NavBattleStart)
                {
                    if (_inkPageRouter != null && _inkPageRouter.NavigateToHud())
                    {
                        _lastKnownDomId = InkPageDomIds.CombatHudTraditional;
                        return true;
                    }
                    return false;
                }
                if (domId == InkPageDomIds.CtaEnterWorld)
                {
                    domId = InkPageDomIds.CombatHudTraditional;
                }
                // 战斗 HUD 变体切换（沉浸式 ⇄ 传统式）：toggle-immersive / toggle-traditional 由 HUD 上
                // 「切换沉浸模式 / 切换传统模式」按钮触发。二者非注册页面，必须在此分流——
                // 据当前页在 CombatHudV2 与 CombatHudTraditional 间往返（两个 id 行为一致，均为对向切换）。
                if (domId == InkPageDomIds.ToggleTraditional || domId == InkPageDomIds.ToggleImmersive)
                {
                    if (_inkPageRouter == null)
                    {
                        return false;
                    }
                    string target = _inkPageRouter.CurrentPageDomId == InkPageDomIds.CombatHudV2
                        ? InkPageDomIds.CombatHudTraditional
                        : InkPageDomIds.CombatHudV2;
                    if (_inkPageRouter.NavigateTo(target))
                    {
                        _lastKnownDomId = target;
                        return true;
                    }
                    return false;
                }
                // 跨页直达动作（game-ui-system §1.2）：action-* 前缀的合法直达路径交由
                // NavigateToAction 解析目标页并触发墨韵涟漪；其余跨页跳转仍须经 HUD 中转。
                if (domId.StartsWith("action-", System.StringComparison.Ordinal))
                {
                    if (_inkPageRouter != null && _inkPageRouter.NavigateToAction(domId))
                    {
                        // 记录解析后的真实目标页（而非 action-* 别名），供分辨率变化时的重绑逻辑匹配。
                        _lastKnownDomId = _inkPageRouter.CurrentPageDomId ?? domId;
                        return true;
                    }
                    return false;
                }
                if (_inkPageRouter != null && _inkPageRouter.NavigateTo(domId))
                {
                    // 记真实落点而非请求 id：别名（combat-hud）会被解析成当前枢纽 HUD，
                    // 弹层则不改页面槽位——画布重建后恢复的必须是底页，不能是弹层。
                    _lastKnownDomId = _inkPageRouter.CurrentPageDomId ?? domId;
                    // HUD 页可能是"玩家就绪之前"建的：每次落地都按当前数据重绑一次，
                    // 否则 V2 顶栏会一直停在 `—`（2026-10-29 截图实测）。
                    RebindActivePageData();
                    ScheduleSelfTestUiShot(_lastKnownDomId);
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppendSelfTestReceipt($"nav {domId} exception {ex.GetType().Name}:{ex.Message}");
                FlaxEngine.Debug.LogError($"[MainUIManager] NavigateToPage({domId}) 失败: {ex.Message}");
            }
            return false;
        }

        // ===================================================================
        // 页面工厂方法（创建页面控件并订阅事件）
        // =======================================================================

        /// <summary>
        /// 创建战斗 HUD 页面并订阅导航请求事件。
        /// 头像按钮、系统导航按钮触发 <see cref="CombatHudPage.NavigationRequested"/> 后，
        /// 由路由器执行 <see cref="InkPageRouter.NavigateTo"/> 跳转到目标子页面。
        /// 同时注入粒子动效系统引用，供按钮点击触发金粉爆发反馈。
        /// </summary>
        /// <returns>CombatHudPage 实例</returns>
        // --- 引导模块工厂（引导 HUD / 动作引导 / 武学引导）---

        /// <summary>创建引导 HUD 页（聚光步骤引导覆盖层），订阅标准导航事件。</summary>
        private HundunWorld.Game.UI.Ink.Pages.Guide.GuideHudPage CreateGuideHud()
        {
            var page = new HundunWorld.Game.UI.Ink.Pages.Guide.GuideHudPage();
            page.NavigationRequested += (domId) =>
            {
                try
                {
                    NavigateToPage(domId);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] GuideHud 导航失败 ({domId}): {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>创建动作引导页，接线关闭→返回 HUD、武学引导→GuideMartial。</summary>
        private HundunWorld.Game.UI.Ink.Pages.Guide.GuideActionPage CreateGuideAction()
        {
            var page = new HundunWorld.Game.UI.Ink.Pages.Guide.GuideActionPage();
            page.GuideClosed += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            page.MartialGuideRequested += () => NavigateToPage(InkPageDomIds.GuideMartial);
            return page;
        }

        /// <summary>创建武学引导页，接线关闭→返回 HUD、多人→多人模式页。</summary>
        private HundunWorld.Game.UI.Ink.Pages.Guide.GuideMartialPage CreateGuideMartial()
        {
            var page = new HundunWorld.Game.UI.Ink.Pages.Guide.GuideMartialPage();
            page.GuideClosed += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            // 演练入口暂无专用页面，返回战斗 HUD（后续有练武页再接入）。
            page.PracticeRequested += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            page.MultiplayerRequested += () => NavigateToPage(InkPageDomIds.NavMultiplayer);
            return page;
        }

        /// <summary>创建小游戏完成页并按设计链路接线：领取→返回 HUD，重玩→重载本页，关闭→引导动作页。</summary>
        private MiniGameCompletePage CreateMiniGameComplete()
        {
            var page = new MiniGameCompletePage();
            page.Claimed += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            page.Replay += () => NavigateToPage(InkPageDomIds.MiniGameComplete);
            // 设计链路：mini-game-complete → link-guide-action → guide-action
            page.Closed += () => NavigateToPage(InkPageDomIds.GuideAction);
            return page;
        }

        /// <summary>创建传送点解锁奖励页并按设计链路接线：确认→地图解锁页；打开地图→世界地图。</summary>
        private RewardTeleportUnlockPage CreateRewardTeleportUnlock()
        {
            var page = new RewardTeleportUnlockPage();
            // 设计链路：reward-teleport-unlock → link-reward-map → reward-map-unlock
            page.Confirmed += () => NavigateToPage(InkPageDomIds.RewardMapUnlock);
            page.OpenMap += () => NavigateToPage(InkPageDomIds.NavWorldMap);
            return page;
        }

        /// <summary>创建地图解锁奖励页并按设计链路接线：查看升级→等级提升页；去探索→返回 HUD。</summary>
        private RewardMapUnlockPage CreateRewardMapUnlock()
        {
            var page = new RewardMapUnlockPage();
            // 设计链路：reward-map-unlock → link-reward-level-up → reward-level-up
            page.ViewLevelUp += () => NavigateToPage(InkPageDomIds.RewardLevelUp);
            page.GoExplore += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            return page;
        }

        /// <summary>创建恭喜奖励页并按设计链路接线：领取→任务完成奖励页；查看背包→角色面板（背包已并入）。</summary>
        private RewardCongratulationsPage CreateRewardCongratulations()
        {
            var page = new RewardCongratulationsPage();
            // 设计链路：reward-congratulations → link-reward-quest-complete → reward-quest-complete
            page.Claimed += () => NavigateToPage(InkPageDomIds.RewardQuestComplete);
            page.ViewBag += () => NavigateToPage(InkPageDomIds.NavInventory);
            return page;
        }

        /// <summary>
        /// 创建战斗 HUD 页面（默认页，全屏沉浸布局）。
        /// 头像按钮、系统导航按钮触发 <see cref="CombatHudPage.NavigationRequested"/> 后由路由跳转；
        /// 同时注入粒子动效系统引用，供按钮点击触发金粉爆发反馈。
        /// </summary>
        /// <returns>CombatHudPage 实例</returns>
        private CombatHudPage CreateCombatHud()
        {
            var page = new CombatHudPage();
            // 注入粒子系统引用，供按钮点击触发金粉反馈
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try
                {
                    NavigateToPage(domId);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] CombatHud 导航失败 ({domId}): {ex.Message}");
                }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                if (TryGetLocalPlayerSkills(out var skills))
                {
                    page.BindSkills(skills);
                    _cachedSkills = skills;
                }
                _localPlayerReady = true;
                FlaxEngine.Debug.Log("[MainUIManager] CombatHud 已即时绑定本地玩家数据");
            }
            else
            {
                FlaxEngine.Debug.Log("[MainUIManager] CombatHud 创建时本地玩家未就绪，等待重绑");
            }
            _activeCombatHud = page;
            return page;
        }

        /// <summary>
        /// 创建传统模式战斗 HUD 页面并订阅导航请求事件。
        /// 与 <see cref="CreateCombatHud"/> 对应，承载更高密度的信息展示
        /// （HP/MP/SP 三条数值条 / 8 格快捷栏 / 10 格技能槽 / 任务追踪面板）。
        /// "沉浸模式"按钮点击触发 <see cref="InkPageDomIds.CombatHud"/> 返回沉浸式 HUD。
        /// 同样注入粒子动效系统引用供按钮金粉反馈。
        /// </summary>
        /// <returns>CombatHudTraditionalPage 实例</returns>
        private CombatHudTraditionalPage CreateCombatHudTraditional()
        {
            var page = new CombatHudTraditionalPage();

            // 门派：创角不再选门派，未加入前显示「无门派」，加入后才显示门派名。
            var selectedRole = global::Game.UI.Character.CharacterManager.Instance?.SelectedCharacter;
            string sect = CcSectNames.Get(selectedRole?.Profession ?? Profession.None);
            page.SetSectName(string.IsNullOrEmpty(sect) ? "无门派" : sect);

            // 注入粒子系统引用
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try
                {
                    NavigateToPage(domId);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] CombatHudTraditional 导航失败 ({domId}): {ex.Message}");
                }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（与 CombatHudPage 同一模式）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                if (TryGetLocalPlayerSkills(out var skills))
                {
                    // 传统 HUD 没有技能栏（技能条是独立的 InkActionBarControl），技能只做缓存供冷却等使用
                    _cachedSkills = skills;
                }
                _localPlayerReady = true;
                FlaxEngine.Debug.Log("[MainUIManager] CombatHudTraditional 已即时绑定本地玩家数据");
            }
            _activeCombatHudTraditional = page;
            return page;
        }

        // ===============================================================
        // 角色与背包子系统：6 个页面工厂（game-ui-system 设计方案）
        // ===============================================================

        /// <summary>
        /// 创建角色面板页面（character-panel.html）。
        /// 注入粒子动效系统引用供按钮金粉反馈，订阅 NavigationRequested 转发至路由器。
        /// </summary>
        private CharacterPanelPage CreateCharacterPanelPage()
        {
            var page = new CharacterPanelPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] CharacterPanel 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
                FlaxEngine.Debug.Log("[MainUIManager] CharacterPanel 已即时绑定本地玩家数据");
            }
            _activeCharacterPanel = page;
            return page;
        }

        /// <summary>
        /// 创建五行技能树页（数据驱动，读服务端技能目录 + skills.json 的阶/境/演化数据）。
        /// </summary>
        private SkillTreePage CreateSkillTreePage()
        {
            var page = new SkillTreePage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] SkillTree 导航失败 ({domId}): {ex.Message}"); }
            };
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            _activeSkillTree = page;
            return page;
        }

        /// <summary>
        /// 创建装备强化页面（equipment-enhance.html）。
        /// </summary>
        private EquipmentEnhancePage CreateEquipmentEnhancePage()
        {
            var page = new EquipmentEnhancePage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] EquipmentEnhance 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（占位 API，待网络层接入后扩展）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            return page;
        }

        /// <summary>
        /// 创建制造技艺页面（crafting.html）。
        /// </summary>
        private CraftingPage CreateCraftingPage()
        {
            var page = new CraftingPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] Crafting 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（占位 API，待网络层接入后扩展）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            return page;
        }

        /// <summary>
        /// 创建坐骑灵兽页面（mount-pet.html）。
        /// </summary>
        private MountPetPage CreateMountPetPage()
        {
            var page = new MountPetPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] MountPet 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（占位 API，待网络层接入后扩展）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            return page;
        }

        // ===============================================================
        // 任务与技能子系统：4 个页面工厂（game-ui-system 设计方案）
        // ===============================================================

        /// <summary>
        /// 创建世界地图页面（world-map.html）。
        /// </summary>
        private WorldMapPage CreateWorldMapPage()
        {
            var page = new WorldMapPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] WorldMap 导航失败 ({domId}): {ex.Message}"); }
            };
            return page;
        }

        /// <summary>
        /// 创建指南针页面（compass.html）。
        /// </summary>
        private CompassPage CreateCompassPage()
        {
            var page = new CompassPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] Compass 导航失败 ({domId}): {ex.Message}"); }
            };
            return page;
        }

        /// <summary>
        /// 创建江湖秘境入口页面（dungeon-entry.html）。
        /// </summary>
        private DungeonEntryPage CreateDungeonEntryPage()
        {
            var page = new DungeonEntryPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] DungeonEntry 导航失败 ({domId}): {ex.Message}"); }
            };
            return page;
        }

        // ===============================================================
        // 社交与商城子系统：7 个页面工厂（game-ui-system 设计方案）
        // ===============================================================

        /// <summary>
        /// 创建江湖门派页面（social-guild.html）。
        /// </summary>
        private SocialGuildPage CreateSocialGuildPage()
        {
            var page = new SocialGuildPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] SocialGuild 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（占位 API，待网络层接入后扩展）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            return page;
        }

        /// <summary>
        /// 创建好友列表页面（friends.html）。
        /// </summary>
        private FriendsPage CreateFriendsPage()
        {
            var page = new FriendsPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] Friends 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（占位 API，待网络层接入后扩展）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            return page;
        }

        /// <summary>
        /// 创建飞鸽传书页面（mail.html）。
        /// </summary>
        private SocialMailPage CreateSocialMailPage()
        {
            var page = new SocialMailPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] SocialMail 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（占位 API，待网络层接入后扩展）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            return page;
        }

        /// <summary>
        /// 创建师徒传承页面（mentor.html）。
        /// </summary>
        private MentorPage CreateMentorPage()
        {
            var page = new MentorPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] Mentor 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（占位 API，待网络层接入后扩展）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            return page;
        }

        /// <summary>
        /// 创建江湖风云榜页面（leaderboard.html）。
        /// </summary>
        private LeaderboardPage CreateLeaderboardPage()
        {
            var page = new LeaderboardPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] Leaderboard 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（占位 API，待网络层接入后扩展）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            return page;
        }

        /// <summary>
        /// 创建江湖百艺录（成就）页面（achievement.html）。
        /// </summary>
        private AchievementPage CreateAchievementPage()
        {
            var page = new AchievementPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] Achievement 导航失败 ({domId}): {ex.Message}"); }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据（占位 API，待网络层接入后扩展）
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
            }
            return page;
        }

        /// <summary>
        /// 创建加载页 1 并订阅进度完成事件。
        /// 进度满后自动推进到加载页 2。
        /// </summary>
        /// <returns>LoadingPage1 实例</returns>
        private LoadingPage1 CreateLoadingPage1()
        {
            var page = new LoadingPage1();
            page.ProgressComplete += () =>
            {
                try
                {
                    NavigateToPage(InkPageDomIds.Loading2);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] LoadingPage1 推进失败: {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建加载页 2 并订阅事件。
        /// 进度满后停留在本页并显示设计稿的「点击继续」提示，用户点击后才推进到章节过场页
        /// （对应 loading-2.html 的可见出口，避免提示闪现）。
        /// </summary>
        /// <returns>LoadingPage2 实例</returns>
        private LoadingPage2 CreateLoadingPage2()
        {
            var page = new LoadingPage2();
            page.ProgressComplete += () =>
            {
                FlaxEngine.Debug.Log("[MainUIManager] LoadingPage2 进度完成，等待玩家点击「点击继续」");
            };
            page.ContinueRequested += () =>
            {
                try
                {
                    NavigateToPage(InkPageDomIds.ChapterTransition);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] LoadingPage2 推进失败: {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建章节过场页并订阅"进入世界"按钮点击事件。
        /// 点击后导航到战斗 HUD。
        /// </summary>
        /// <returns>ChapterTransitionPage 实例</returns>
        private ChapterTransitionPage CreateChapterTransition()
        {
            var page = new ChapterTransitionPage();
            page.EnterWorldClicked += () =>
            {
                try
                {
                    NavigateToPage(InkPageDomIds.CombatHudTraditional);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] ChapterTransition 进入世界失败: {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建任务菜单页面。
        /// </summary>
        /// <returns>MenuQuestsPage 实例</returns>
        private MenuQuestsPage CreateMenuQuests()
        {
            var page = new MenuQuestsPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建商店菜单页面。
        /// </summary>
        /// <returns>MenuShopPage 实例</returns>
        private MenuShopPage CreateMenuShop()
        {
            var page = new MenuShopPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建物品获得弹窗并订阅确认事件。
        /// 确认后返回战斗 HUD。
        /// </summary>
        /// <returns>PopupItemAcquired 实例</returns>
        private PopupItemAcquired CreatePopupItemAcquired()
        {
            var page = new PopupItemAcquired();
            page.Confirmed += () =>
            {
                try
                {
                    // 设计链路：popup-item-acquired → link-popup-skill-realization → popup-skill-realization
                    NavigateToPage(InkPageDomIds.PopupSkillRealization);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] PopupItemAcquired 确认失败: {ex.Message}");
                }
            };
            page.ViewDetail += () =>
            {
                try
                {
                    FlaxEngine.Debug.Log("[MainUIManager] PopupItemAcquired 查看详情");
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] PopupItemAcquired 查看详情失败: {ex.Message}");
                }
            };
            page.Closed += () =>
            {
                try
                {
                    _inkPageRouter?.NavigateToHud();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] PopupItemAcquired 关闭失败: {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建留言弹窗并订阅关闭事件。
        /// 关闭后返回战斗 HUD。
        /// </summary>
        /// <returns>PopupMessage 实例</returns>
        private PopupMessage CreatePopupMessage()
        {
            var page = new PopupMessage();
            page.Closed += () =>
            {
                try
                {
                    _inkPageRouter?.NavigateToHud();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] PopupMessage 关闭失败: {ex.Message}");
                }
            };
            page.Replied += () =>
            {
                try
                {
                    // 设计稿 .replyBtn data-dom-id=link-popup-martial-detail：回复 → 武学详情弹窗。
                    NavigateToPage(InkPageDomIds.PopupMartialDetail);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] PopupMessage 回复失败: {ex.Message}");
                }
            };
            page.Favorited += () =>
            {
                try
                {
                    // 收藏为页内反馈，回退至战斗 HUD。
                    _inkPageRouter?.NavigateToHud();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] PopupMessage 收藏失败: {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建成就奖励弹窗并订阅领取事件。
        /// 领取后返回战斗 HUD。
        /// </summary>
        /// <returns>RewardAchievementPage 实例</returns>
        private RewardAchievementPage CreateRewardAchievement()
        {
            var page = new RewardAchievementPage();
            page.Claimed += () =>
            {
                try
                {
                    // 设计链路：reward-achievement → link-reward-teleport → reward-teleport-unlock
                    NavigateToPage(InkPageDomIds.RewardTeleportUnlock);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] RewardAchievement 领取失败: {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建任务完成奖励弹窗并订阅领取事件。
        /// 领取后返回战斗 HUD。
        /// </summary>
        /// <returns>RewardQuestCompletePage 实例</returns>
        private RewardQuestCompletePage CreateRewardQuestComplete()
        {
            var page = new RewardQuestCompletePage();
            page.Claimed += () =>
            {
                try
                {
                    _inkPageRouter?.NavigateToHud();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] RewardQuestComplete 领取失败: {ex.Message}");
                }
            };
            // 设计链路：reward-quest-complete → shortcut-photo-mode → photo-mode
            page.TakePhoto += () =>
            {
                try
                {
                    NavigateToPage(InkPageDomIds.NavPhotoMode);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] RewardQuestComplete 拍照跳转失败: {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建设置页面。
        /// </summary>
        /// <returns>SettingsPage 实例</returns>
        private SettingsPage CreateSettings()
        {
            var page = new SettingsPage();
            // 设置页左栏/返回战斗/异兽图鉴/音效分类等出口依赖 NavigationRequested 事件
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建 Esc 系统菜单。三项都是真行为：继续游戏＝收掉本层（同时解暂停）；
        /// 设置＝真导航到设置页（导航会顺带关弹层）；退出＝与战斗 HUD「退」钮同一套回选角流程。
        /// </summary>
        /// <summary>
        /// NPC 对话菜单：把"这位能做什么"摆出来，每一项再导航到各自页面（商店/技能/制作/任务）。
        /// </summary>
        private NpcGossipPage CreateNpcGossip()
        {
            var page = new NpcGossipPage();
            if (_inkPageShell != null && _inkPageShell.ParticleSystem != null)
                page.ParticleSystem = _inkPageShell.ParticleSystem;

            HookNavigationRequested(page);
            page.CloseRequested += () => _inkPageRouter?.CloseOverlay();
            return page;
        }

        private SystemMenuPage CreateSystemMenu()        {
            var page = new SystemMenuPage();
            if (_inkPageShell != null && _inkPageShell.ParticleSystem != null)
                page.ParticleSystem = _inkPageShell.ParticleSystem;

            HookNavigationRequested(page);
            page.CloseRequested += () => _inkPageRouter?.CloseOverlay();
            page.ExitRequested += ExitGameToCharacterSelection;
            return page;
        }

        /// <summary>
        /// 退出到角色选择：切场景 + 改全局状态，服务端随后按断线感知完成 Despawn/离线清理。
        /// </summary>
        public void ExitGameToCharacterSelection()
        {
            var sceneManager = GameSceneManager.Instance;
            if (sceneManager != null)
                sceneManager.TransitionTo(Horizon.Game.Message.Enums.SceneType.CharacterSelection);

            var gameState = HundunWorld.Game.Core.GameStateManager.Instance;
            if (gameState != null)
                gameState.ChangeState(HundunWorld.Game.Core.GameState.CharacterSelect);
        }

        /// <summary>
        /// 暂停态与"系统菜单是否盖着"对账。消费者是 <see cref="UIMouseCapture.KeyboardCaptured"/>
        /// （操控输入与快捷键归零）——没有消费者的暂停只是换个标题的假动作。
        /// <para>
        /// 刻意不冻结时间/世界：服务器权威，本地停摆只会把移动同步与插值弄坏。
        /// </para>
        /// </summary>
        private void SyncPauseToSystemMenu()
        {
            var gameState = HundunWorld.Game.Core.GameStateManager.Instance;
            if (gameState == null) return;

            bool menuOpen = _inkPageRouter != null
                            && _inkPageRouter.CurrentOverlayDomId == InkPageDomIds.SystemMenu;
            if (menuOpen)
            {
                if (!gameState.IsPaused) gameState.PauseGame();
            }
            else if (gameState.IsPaused)
            {
                gameState.ResumeGame();
            }
        }

        /// <summary>
        /// 创建战斗 HUD v2 页面并订阅导航请求事件。
        /// </summary>
        /// <returns>CombatHudV2Page 实例</returns>
        private CombatHudV2Page CreateCombatHudV2()
        {
            var page = new CombatHudV2Page();
            page.NavigationRequested += (domId) =>
            {
                try
                {
                    NavigateToPage(domId);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] CombatHudV2 导航失败 ({domId}): {ex.Message}");
                }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
                FlaxEngine.Debug.Log("[MainUIManager] CombatHudV2 已即时绑定本地玩家数据");
            }
            else
            {
                FlaxEngine.Debug.Log("[MainUIManager] CombatHudV2 创建时本地玩家未就绪，等待重绑");
            }
            _activeCombatHudV2 = page;
            return page;
        }

        /// <summary>
        /// 创建阵亡界面并订阅破招与返回事件。
        /// 破招或返回后均返回战斗 HUD。
        /// </summary>
        /// <returns>DeathScreenPage 实例</returns>
        private DeathScreenPage CreateDeathScreen()
        {
            var page = new DeathScreenPage();
            page.ReviveRequested += () =>
            {
                try
                {
                    // 「原地复活」走服务端权威复活（CharacterGrain.ResurrectAsync），
                    // 不是客户端改 IsDead 就完事——服务端血量没回，之后每次施法都会被"已死亡"挡下。
                    var game = HundunWorldGame.Instance;
                    var nm = game?.NetworkManager;
                    if (nm == null || game.PlayerId == 0)
                    {
                        UIHelper.ShowToast("未连接服务器，无法复活", ToastType.Warning);
                        return;
                    }

                    var local = game.LocalPlayerActor;
                    var pos = local != null ? local.Position : Vector3.Zero;
                    _ = nm.SendAsync(new Horizon.Game.Message.Network.ResurrectMessage
                    {
                        ResurrectedId = game.PlayerId,
                        ResurrectPosition = new Horizon.Game.Message.Network.Position { X = pos.X, Y = pos.Y, Z = pos.Z },
                        ResurrectType = 0,
                    });
                    UIHelper.ShowToast("已请求复活…", ToastType.Info);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] DeathScreen 复活请求失败: {ex.Message}");
                }
            };
            page.ReturnRequested += () =>
            {
                try
                {
                    _inkPageRouter?.NavigateToHud();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] DeathScreen 返回失败: {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建 NPC 对话确认页面并订阅对话确认事件。
        /// 无论选择接受/拒绝/询问，均返回战斗 HUD。
        /// </summary>
        /// <returns>DialogueConfirmPage 实例</returns>
        private DialogueConfirmPage CreateDialogueConfirm()
        {
            var page = new DialogueConfirmPage();
            page.DialogueConfirmed += (optionIndex) =>
            {
                try
                {
                    _inkPageRouter?.NavigateToHud();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] DialogueConfirm 确认失败 (选项={optionIndex}): {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建战前备战菜单页面并订阅导航请求事件。
        /// </summary>
        /// <returns>MenuBattlePrepPage 实例</returns>
        private MenuBattlePrepPage CreateMenuBattlePrep()
        {
            var page = new MenuBattlePrepPage();

            // 属性条接真实角色数值（战力推荐值没有数据源，传 0 让页面显示空）
            if (TryGetLocalPlayerAttributes(out var prepAttr))
            {
                // 战力：与角色面板同一占位口径（攻/防/生命合成）；推荐战力没有数据源，传 0 让页面显示「—」
                int combatPower = (int)System.Math.Round(
                    (prepAttr.PhysicalAttack + prepAttr.MagicAttack) * 2f
                    + (prepAttr.PhysicalDefense + prepAttr.MagicDefense) * 1.5f
                    + prepAttr.MaxHealth * 0.2f);
                page.BindCombatPower(combatPower, 0);

                page.BindStatBars(
                    (int)System.Math.Round(prepAttr.CurrentHealth),
                    (int)System.Math.Round(prepAttr.MaxHealth),
                    (int)System.Math.Round(prepAttr.CurrentEnergy),
                    (int)System.Math.Round(prepAttr.MaxEnergy),
                    0, 0);
            }
            page.NavigationRequested += (domId) =>
            {
                try
                {
                    NavigateToPage(domId);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] MenuBattlePrep 导航失败 ({domId}): {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建点穴系统页面并订阅导航请求事件。
        /// </summary>
        /// <returns>AcupointPage 实例</returns>
        private AcupointPage CreateAcupoint()
        {
            var page = new AcupointPage();
            page.NavigationRequested += (domId) =>
            {
                try
                {
                    NavigateToPage(domId);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] Acupoint 导航失败 ({domId}): {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建 QTE 千钧一发页面并订阅失败与成功事件。
        /// 失败或成功后均返回战斗 HUD。
        /// </summary>
        /// <returns>QtePage 实例</returns>
        private QtePage CreateQte()
        {
            var page = new QtePage();
            page.QteFailed += () =>
            {
                try
                {
                    _inkPageRouter?.NavigateToHud();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] Qte 失败处理异常: {ex.Message}");
                }
            };
            // 设计稿 .qte-acupoint：点穴经络可见出口 → acupoint
            page.AcupointRequested += () =>
            {
                try
                {
                    NavigateToPage(InkPageDomIds.Acupoint);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] Qte 点穴经络跳转异常: {ex.Message}");
                }
            };
            page.QteSucceeded += () =>
            {
                try
                {
                    _inkPageRouter?.NavigateToHud();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] Qte 成功处理异常: {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 创建等级提升奖励弹窗并订阅确认事件。
        /// 确认后返回战斗 HUD。
        /// </summary>
        /// <returns>RewardLevelUpPage 实例</returns>
        private RewardLevelUpPage CreateRewardLevelUp()
        {
            var page = new RewardLevelUpPage();
            page.Confirmed += () =>
            {
                try
                {
                    // 设计链路：reward-level-up → link-reward-congratulations → reward-congratulations
                    NavigateToPage(InkPageDomIds.RewardCongratulations);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] RewardLevelUp 确认失败: {ex.Message}");
                }
            };
            return page;
        }

        private PopupVerification CreatePopupVerification()
        {
            var page = new PopupVerification();
            // 设计链路：popup-verification → shortcut-reward-achievement → reward-achievement
            // 注意：本弹窗仅 Confirmed/ViewAchievement 两个事件，无 Closed。
            page.ViewAchievement += () => NavigateToPage(InkPageDomIds.RewardAchievement);
            page.Confirmed += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            return page;
        }

        private PopupMartialArts CreatePopupMartialArts()
        {
            var page = new PopupMartialArts();
            // 设计链路：popup-martial-arts → link-popup-item-acquired → popup-item-acquired
            page.Upgraded += () => NavigateToPage(InkPageDomIds.PopupItemAcquired);
            page.Equipped += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            page.Closed += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            return page;
        }

        private PopupSkillRealization CreatePopupSkillRealization()
        {
            var page = new PopupSkillRealization();
            // 设计链路：popup-skill-realization → link-popup-verification → popup-verification
            page.Confirmed += () => NavigateToPage(InkPageDomIds.PopupVerification);
            page.Verified += () => NavigateToPage(InkPageDomIds.PopupVerification);
            page.Closed += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            return page;
        }

        private PopupMartialDetail CreatePopupMartialDetail()
        {
            var page = new PopupMartialDetail();
            // 真实数据绑定：取技能目录里已学的第一式（无已学时退到目录首条），不再用设计稿样例。
            if (TryPickMartialForDetail(out var martialCfg, out var martialLevel))
                page.BindSkill(martialCfg, martialLevel);
            // 设计链路：popup-martial-detail → link-popup-guide-side → popup-guide-side
            page.Practiced += () => NavigateToPage(InkPageDomIds.PopupGuideSide);
            // 设计稿 .guide-link：修炼指引 → link-popup-guide-side
            page.GuideRequested += () => NavigateToPage(InkPageDomIds.PopupGuideSide);
            page.Closed += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            return page;
        }

        private PopupGuideSide CreatePopupGuideSide()
        {
            var page = new PopupGuideSide();
            // 设计链路：popup-guide-side → link-popup-martial-arts → popup-martial-arts
            page.Started += () => NavigateToPage(InkPageDomIds.PopupMartialArts);
            // 设计稿 .nav-buttons：上一步回到引导 HUD（引导模块的上游页面）
            page.PreviousRequested += () => NavigateToPage(InkPageDomIds.GuideHud);
            page.Skipped += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            page.Closed += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            return page;
        }

        private PopupBestiarySide CreatePopupBestiarySide()
        {
            var page = new PopupBestiarySide();
            // 统一导航：关闭/捕获/收藏→战斗 HUD；留言→popup-message（设计链路 bestiary→message，按稿补全）。
            page.Closed += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            page.Hunt += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            page.Track += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            page.MessageClicked += () => NavigateToPage(InkPageDomIds.PopupMessage);
            return page;
        }

        // ===================================================================
        // 新增页面工厂方法（串联所有 UI）
        // =======================================================================

        /// <summary>
        /// 创建外观菜单页面。
        /// </summary>
        private MenuAppearancePage CreateMenuAppearance()
        {
            var page = new MenuAppearancePage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建活动菜单页面。
        /// </summary>
        private ActivityHubPage CreateMenuActivities()
        {
            // UI 重铸：nav-activities 改为「同游四卡 + 闯荡列表 + 挑战心力」目录页；
            // 旧 MenuActivitiesPage 保留为设计稿参考，不再由路由创建。
            var page = new ActivityHubPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建门派菜单页面。
        /// </summary>
        private MenuSectPage CreateMenuSect()
        {
            var page = new MenuSectPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建组队菜单页面。
        /// </summary>
        private TeamRosterPage CreateMenuTeam()
        {
            // UI 重铸：nav-team 改为「创建/加入 + 当前队伍」，数据走 TeamClientService；
            // 旧 MenuTeamPage 保留为设计稿参考，不再由路由创建。
            var page = new TeamRosterPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建博物志菜单页面。
        /// </summary>
        private CollectionWorkbenchPage CreateMenuBestiary()
        {
            // UI 重铸：nav-bestiary 改为「分类筛选 + 列表 + 详情/进度」工作台；
            // 旧 MenuBestiaryPage 保留为设计稿参考，不再由路由创建。
            var page = new CollectionWorkbenchPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建通行证菜单页面。
        /// </summary>
        private MenuBattlePassPage CreateMenuBattlePass()
        {
            var page = new MenuBattlePassPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建抽卡菜单页面。
        /// </summary>
        private MenuGachaPage CreateMenuGacha()
        {
            var page = new MenuGachaPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建生活技能菜单页面。
        /// </summary>
        private MenuLivelihoodPage CreateMenuLivelihood()
        {
            var page = new MenuLivelihoodPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建个人信息菜单页面。
        /// </summary>
        private MenuPersonalInfoPage CreateMenuPersonalInfo()
        {
            var page = new MenuPersonalInfoPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建武学记录菜单页面。
        /// </summary>
        private MenuMartialRecordPage CreateMenuMartialRecord()
        {
            var page = new MenuMartialRecordPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建休闲模式菜单页面。
        /// </summary>
        private MenuCasualModePage CreateMenuCasualMode()
        {
            var page = new MenuCasualModePage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建时间菜单页面。
        /// </summary>
        private MenuTimePage CreateMenuTime()
        {
            var page = new MenuTimePage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建奇珍阁菜单页面。
        /// </summary>
        private ShopRareItemsPage CreateShopRareItems()
        {
            var page = new ShopRareItemsPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建多人模式菜单页面。
        /// </summary>
        private MultiplayerPage CreateMultiplayer()
        {
            var page = new MultiplayerPage();
            // 设计链路：multiplayer → link-combat-hud-v2 → combat-hud-v2；返回→战斗 HUD。
            // 「开始匹配」按钮在匹配服务接入前保持禁用，因此不再挂 StartMatchRequested。
            page.BackRequested += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            return page;
        }

        /// <summary>
        /// 创建拍照模式菜单页面。
        /// </summary>
        private PhotoModePage CreatePhotoMode()
        {
            var page = new PhotoModePage();
            HookNavigationRequested(page);

            // 取景器期间藏起常驻技能栏/气息指示；退出或页面销毁时恢复。
            page.OverlayVisibilityRequested += hidden =>
            {
                _photoOverlayHidden = hidden;
                UpdateActionBar();
            };
            _photoOverlayHidden = true;
            UpdateActionBar();

            page.ExitRequested += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            // 相册是独立页：列出 Screenshots 里的成片，返回时回到取景页。
            page.AlbumClicked += () => NavigateToPage(InkPageDomIds.NavPhotoAlbum);
            return page;
        }

        private PhotoAlbumPage CreatePhotoAlbum()
        {
            var page = new PhotoAlbumPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建元素视野页面。
        /// </summary>
        private ElementVisionPage CreateElementVision()
        {
            var page = new ElementVisionPage();
            // 统一导航：退出→战斗 HUD；阵亡预览→阵亡界面。
            page.ExitRequested += () => NavigateToPage(InkPageDomIds.BackOneLevel);
            page.DeathScreenRequested += () => NavigateToPage(InkPageDomIds.DeathScreen);
            return page;
        }

        // ===================================================================
        // 选角 / 创角流程（水墨舞台页，取代旧 Flax GUI 面板栈）
        // =======================================================================

        /// <summary>
        /// 选角 / 创角是否由水墨页面承载。
        /// 置 false 可退回 <c>CharacterSceneController</c> 构建的旧 Flax GUI 面板栈；
        /// 新流程确认无回归后即可整体删除旧栈。
        /// </summary>
        public const bool UseInkCharacterFlow = true;

        /// <summary>是否为选角 / 创角流程场景（两者共用 Content/Maps/Character.scene）。</summary>
        private static bool IsCcFlowScene(SceneType scene)
        {
            return scene == SceneType.CharacterSelection || scene == SceneType.CharacterCreation;
        }

        /// <summary>
        /// 角色选择页：进入 / 创建 / 删除三种动作在此发起，页面本身不碰网络。
        /// </summary>
        /// <summary>
        /// 自测标记：<c>%APPDATA%/HundunWorld/open-sculpt.on</c> 存在 ⇒ 创角壳首页直接落在捏脸页。
        /// 只服务自动化验证（完整创角链需要服务端角色数据），正常环境不会有这个文件。
        /// </summary>
        private static bool HasSelfTestSculptEntry()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", "open-sculpt.on");
                return System.IO.File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private CcRoleListPage CreateCcRoleList()
        {
            var page = new CcRoleListPage();
            page.CreateRequested += OnCcCreateRequested;
            page.EnterRequested += OnCcEnterRequested;
            page.DeleteRequested += OnCcDeleteRequested;
            page.LogoutRequested += OnCcLogoutRequested;
            ApplyCcWatermark(page);

            // 自测：运行时页面控件不在场景树里，自动化工具点不到，用标记文件直接走"进入游戏"。
            if (HasSelfTestEnterWorldEntry())
            {
                _selfTestEnterWorldPending = true;
                _selfTestEnterWorldDelay = 3f;
                AppendSelfTestReceipt($"enter-world armed instance={GetHashCode():X8}");
            }

            return page;
        }

        /// <summary>
        /// 自测标记：<c>%APPDATA%/HundunWorld/enter-world.on</c> 存在 ⇒ 登录后自动用第一个角色进入游戏。
        /// 只服务自动化验证（进世界后的 HUD / 拍照模式等没法用编辑器工具点进），正常环境不会有这个文件。
        /// </summary>
        private static bool HasSelfTestEnterWorldEntry()
        {
            return HasSelfTestMarker("enter-world.on");
        }

        /// <summary>
        /// 自测标记：<c>%APPDATA%/HundunWorld/open-photo.on</c> 存在 ⇒ 本地玩家就绪后自动打开拍照页。
        /// </summary>
        private static bool HasSelfTestPhotoEntry()
        {
            return HasSelfTestMarker("open-photo.on");
        }

        /// <summary>自测：导航落地后安排一次整屏抓图（仅在 ui-shot.on 存在时生效）。</summary>
        private void ScheduleSelfTestUiShot(string domId)
        {
            if (string.IsNullOrEmpty(domId) || !HasSelfTestMarker("ui-shot.on"))
                return;
            _selfTestShotPage = domId;
            _selfTestShotDelay = 1f;
        }

        private void CaptureSelfTestUiShot()
        {
            string page = _selfTestShotPage;
            _selfTestShotPage = null;
            if (string.IsNullOrEmpty(page))
                return;

            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (char ch in page)
                    sb.Append(char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_');
                string path = System.IO.Path.Combine(
                    Globals.ProjectFolder, "Screenshots", $"ui-{sb}.png");
                Screenshot.Capture(path);
                AppendSelfTestReceipt($"ui-shot {page} -> {path}");
            }
            catch (Exception ex)
            {
                AppendSelfTestReceipt($"ui-shot {page} failed {ex.GetType().Name}:{ex.Message}");
            }
        }

        private static bool HasSelfTestMarker(string fileName)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", fileName);
                return System.IO.File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 自测：<c>%APPDATA%/HundunWorld/open-page.txt</c> 第一行是页面 dom-id，
        /// 进世界 3 秒后自动导航过去（走查运行时页面用，正常环境没有这个文件）。
        /// </summary>
        private static string ReadSelfTestNavPage()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", "open-page.txt");
                if (!System.IO.File.Exists(path))
                    return null;

                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    string domId = line.Trim();
                    if (!string.IsNullOrEmpty(domId) && !domId.StartsWith("#"))
                        return domId;
                }
            }
            catch
            {
                // 读不到就当没启用
            }
            return null;
        }

        private void AutoEnterWorldForSelfTest()
        {
            var manager = global::Game.UI.Character.CharacterManager.Instance;
            var list = manager?.CharacterList;
            int mgrCount = list == null ? -1 : list.Count;

            // CharacterManager 有时还没收到镜像（它为 0 而页面已经有行），退回状态管理器这份权威列表。
            if (list == null || list.Count == 0)
                list = _stateManager?.CharacterList;
            int stateCount = list == null ? -1 : list.Count;

            _selfTestEnterWorldAttempts++;
            if (_selfTestEnterWorldAttempts <= 5)
                AppendSelfTestReceipt($"enter-world attempt#{_selfTestEnterWorldAttempts} mgr={mgrCount} state={stateCount}");

            if (list == null || list.Count == 0)
                return; // 角色列表还没到，下一轮再试

            if (manager != null && manager.IsProcessing)
                return; // 还在处理角色请求（例如刚进选角），别把这次吞掉

            _selfTestEnterWorldPending = false;
            var first = list[0];
            AppendSelfTestReceipt($"enter-world fire character={first.CharacterId}");
            OnCcEnterRequested(first);
        }

        /// <summary>创角第一步：性别。</summary>
        private CcGenderPage CreateCcGender()
        {
            var page = new CcGenderPage();
            page.BackRequested += () => NavigateToPage(InkPageDomIds.CcRoleList);
            page.ConfirmRequested += () => NavigateToPage(InkPageDomIds.CcFacePreset);
            ApplyCcWatermark(page);
            return page;
        }

        /// <summary>创角第二步：面容风格预设。</summary>
        private CcFacePresetPage CreateCcFacePreset()
        {
            var page = new CcFacePresetPage();
            page.BackRequested += () => NavigateToPage(InkPageDomIds.CcGender);
            page.ConfirmRequested += () => NavigateToPage(InkPageDomIds.CcFaceSculpt);
            ApplyCcWatermark(page);
            return page;
        }

        /// <summary>创角第三步：捏脸。</summary>
        private CcFaceSculptPage CreateCcFaceSculpt()
        {
            var page = new CcFaceSculptPage();
            page.BackRequested += () => NavigateToPage(InkPageDomIds.CcFacePreset);
            // 下一步前先把捏脸快照交给创建流程（不封存）；否则未点「发布」的捏脸值会在建号时丢失。
            page.ConfirmRequested += () =>
            {
                page.CommitPendingLook();
                NavigateToPage(InkPageDomIds.CcName);
            };
            ApplyCcWatermark(page);
            return page;
        }

        /// <summary>创角最后一步：命名（无门派）。</summary>
        private CcNamePage CreateCcName()
        {
            var page = new CcNamePage();
            page.BackRequested += () => NavigateToPage(InkPageDomIds.CcFaceSculpt);
            page.ConfirmRequested += () => RequestCreateCharacter(page.CharacterName);
            ApplyCcWatermark(page);
            return page;
        }

        /// <summary>给流程页统一注入左下角 ID 水印（参考图「ID: 0126998214」）。</summary>
        private static void ApplyCcWatermark(CcStagePage page)
        {
            var network = HundunWorldGame.Instance?.NetworkManager;
            page.WatermarkText = network == null ? string.Empty : $"ID: {network.UserId:D10}";
        }

        /// <summary>选角页「创建角色」：重置本次会话，进入创角第一步。</summary>
        private void OnCcCreateRequested()
        {
            var manager = global::Game.UI.Character.CharacterManager.Instance;
            // 服务端 MAX_CHARACTER_COUNT=5 会直接拒掉第 6 个角色；先挡住，别让流程白跑到命名页才失败。
            if (manager != null && manager.CharacterList.Count >= CcRoleListPage.MaxRoles)
            {
                UIHelper.ShowToast($"角色数量已达上限（{CcRoleListPage.MaxRoles}个），请删除不需要的角色后再创建。", ToastType.Warning);
                return;
            }

            CcSession.Current.Reset();
            NavigateToPage(InkPageDomIds.CcGender);
        }

        /// <summary>选角页确认：选中该角色并请求进入游戏。</summary>
        private async void OnCcEnterRequested(Horizon.Game.Message.Network.CharacterInfo info)
        {
            var manager = global::Game.UI.Character.CharacterManager.Instance;
            if (manager == null || manager.IsProcessing)
            {
                // 正在处理别的角色请求：别把这次自测吞掉，等下一轮再试。
                _selfTestEnterWorldAttempts++;
                if (_selfTestEnterWorldAttempts <= 8)
                    AppendSelfTestReceipt($"enter-world busy#{_selfTestEnterWorldAttempts} mgrNull={manager == null}");
                return;
            }

            // 服务端随角色列表下发了外观 blob：先落到本地仓库，进世界后 MetaHumanCharacter 的读档路径才看得到。
            // Ink 流程不走 CharacterStateManager.SelectCharacter，这条采纳必须显式接（否则换设备仍回默认脸）。
            HundunWorld.Game.Services.MetaHumanAppearanceService.Instance
                .AdoptFromServer((long)info.CharacterId, info);

            if (!manager.SelectCharacter(info))
            {
                FlaxEngine.Debug.LogWarning("[MainUIManager] 选择角色失败，未发起进入游戏");
                AppendSelfTestReceipt("enter-world select=failed");
                return;
            }

            bool entered = await manager.EnterGameAsync();
            AppendSelfTestReceipt($"enter-world enterGame={entered}");
            if (!entered && _selfTestEnterWorldAttempts < 4 && HasSelfTestEnterWorldEntry())
            {
                // 切换窗口冲突这类瞬时失败：隔几秒再试一次，别让走查卡死。
                _selfTestEnterWorldPending = true;
                _selfTestEnterWorldDelay = 2.5f;
            }
        }

        /// <summary>选角页删除角色。</summary>
        private async void OnCcDeleteRequested(Horizon.Game.Message.Network.CharacterInfo info)
        {
            var manager = global::Game.UI.Character.CharacterManager.Instance;
            if (manager == null || manager.IsProcessing)
                return;

            await manager.DeleteCharacterAsync(info.CharacterId);
        }

        /// <summary>
        /// 退出登录：清掉会话与角色列表缓存，再切回登录场景。等价于旧选角界面的「返回登录」
        /// （<c>CharacterSceneController.OnBackBtnClicked</c>），少了它玩家进了选角页就没有回头路。
        /// </summary>
        private void OnCcLogoutRequested()
        {
            // 只切场景不清缓存时，UIStateManager._characterList 与选中项会跨登录存活
            // （ClearUserSession 此前全仓零调用方）；下一次重登若没拿到角色列表回执，
            // 界面就照旧列表渲染，已删除的角色于是「删掉了还在」。
            _stateManager?.ClearUserSession();
            GameSceneManager.Instance?.TransitionTo(SceneType.Login);
        }

        /// <summary>
        /// 命名页确认：以<b>无门派</b>创建角色。
        /// <para>
        /// 门派不再由玩家创角时选择，<see cref="Profession.None"/> 一路传到服务端；
        /// 服务端创建成功后会自动切回选角场景，新角色出现在行囊中，由玩家自行「进入游戏」。
        /// </para>
        /// </summary>
        private async void RequestCreateCharacter(string characterName)
        {
            var manager = global::Game.UI.Character.CharacterManager.Instance;
            if (manager == null || manager.IsProcessing)
                return;

            // 与服务端 ValidateAndCleanCharacterName 的下界一致（trim 后 ≥2 字），
            // 避免把注定被拒的请求发出去后让玩家对着没反应的界面干等。
            string name = (characterName ?? string.Empty).Trim();
            if (name.Length < 2)
            {
                UIHelper.ShowToast("姓名至少需要两个字", ToastType.Warning);
                return;
            }

            int preset = CcSession.Current.FacePresetIndex;
            var appearance = new Horizon.Game.Message.Network.AppearanceInfo
            {
                HairModel = preset,
                FaceModel = preset,
                // 发型：外观副本只留离散下标；真正驱动渲染的是 look 里的 hair.style。
                HairStyle = CcSession.Current.HairStyleIndex,
                Clothing = CcSession.Current.BodyIndex,
                HairColor = CcSession.Current.HairColorIndex,
                EyeColor = CcSession.Current.EyeColorIndex,
                SkinColor = CcSession.Current.SkinColorIndex,
            };

            await manager.CreateCharacterAsync(name, (int)Profession.None, CcSession.Current.Gender, appearance);
        }

        private bool _ccFlowSubscribed;

        /// <summary>
        /// 订阅创建成功事件（只订一次）。
        /// <para>
        /// 创角与选角同处 Content/Maps/Character.scene，服务端创建回执后
        /// <c>UIStateManager.TransitionToScene</c> 对"目标场景与当前场景相同"是直接跳过的，
        /// 因此必须自己把内容层从命名页切回角色选择页，否则玩家会停在最后一步不动。
        /// </para>
        /// </summary>
        private void EnsureCcFlowSubscriptions()
        {
            if (_ccFlowSubscribed)
                return;

            var manager = global::Game.UI.Character.CharacterManager.Instance;
            if (manager == null)
                return;

            manager.CharacterCreated += OnCcCharacterCreated;
            _ccFlowSubscribed = true;
        }

        /// <summary>创建成功：把新角色设为当前选中，并回到角色选择页。</summary>
        private void OnCcCharacterCreated(Horizon.Game.Message.Network.CharacterInfo info)
        {
            if (!UseInkCharacterFlow || info == null)
                return;

            // 捏脸是在还没有角色 Id 的命名页上发布的，回执里才拿到 Id：
            // 这里把那份「待认领」快照落到新角色名下，下次进场就能按它恢复外观。
            // 服务端外观通道（P5 提案）落地前，本地仓库就是唯一的持久化去处。
            if (MetaHuman.Runtime.MetaHumanLookStore.TryTakePendingLook(out var pendingLook))
            {
                string storeError;
                string savedPath = MetaHuman.Runtime.MetaHumanLookStore.Save(
                    info.CharacterId.ToString(), pendingLook, out storeError);
                FlaxEngine.Debug.Log(savedPath != null
                    ? $"[MetaHuman] 已把发布的外观绑到新角色 {info.CharacterId}：{savedPath}"
                    : $"[MetaHuman] 外观落盘失败（角色 {info.CharacterId}）：{storeError}");

                // 本地落盘只是第一站：不提交服务端的话，换设备或清了目录就回到默认脸。
                var lookMeta = MetaHuman.Runtime.MetaHumanLookStore.TakePendingLookMeta();
                if (savedPath != null && lookMeta != null)
                {
                    bool queued = HundunWorld.Game.Services.MetaHumanAppearanceService.Instance
                        .TrySendLook((long)info.CharacterId, pendingLook, lookMeta.TemplateId, lookMeta.TemplateVersion,
                                     lookMeta.TemplateParamCount, lookMeta.EntryCount);
                    if (!queued)
                    {
                        FlaxEngine.Debug.LogWarning($"[MetaHuman] 外观未提交服务端（角色 {info.CharacterId}）："
                            + HundunWorld.Game.Services.MetaHumanAppearanceService.Instance.LastErrorCode);
                    }
                }
                else if (savedPath != null)
                {
                    FlaxEngine.Debug.LogWarning("[MetaHuman] 缺少模板元信息，本次外观只留在本地，未提交服务端");
                }
            }

            global::Game.UI.Character.CharacterManager.Instance?.SelectCharacter(info);
            NavigateToPage(InkPageDomIds.CcRoleList);
        }

        /// <summary>
        /// 创建音频设置子页面。
        /// </summary>
        private SettingsRoadSignPage CreateSettingsRoadSign()
        {
            var page = new SettingsRoadSignPage();
            HookNavigationRequested(page);
            return page;
        }

        private SettingsAudioPage CreateSettingsAudio()
        {
            var page = new SettingsAudioPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建装备管理菜单页面。
        /// </summary>
        private MenuEquipmentPage CreateMenuEquipment()
        {
            var page = new MenuEquipmentPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建 UI 浏览器（Debug 菜单）页面，订阅导航请求事件。
        /// 点击页面卡片后通过路由器跳转到对应页面。
        /// </summary>
        private UIGalleryPage CreateUIGallery()
        {
            var page = new UIGalleryPage();
            page.NavigationRequested += (domId) =>
            {
                try
                {
                    NavigateToPage(domId);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] UIGallery 导航失败 ({domId}): {ex.Message}");
                }
            };
            return page;
        }

        /// <summary>
        /// 通用：为页面的导航请求事件挂钩路由器跳转。
        /// 适用于实现了 <c>NavigationRequested</c> 事件的页面（duck typing）。
        /// </summary>
        /// <summary>
        /// 把页面的 <see cref="IInkNavigationSource.NavigationRequested"/> 接到 <see cref="NavigateToPage"/>。
        /// <para>
        /// 编译期订阅：页面必须显式实现 <see cref="IInkNavigationSource"/>，事件改名/漏实现会直接编译不过。
        /// 旧实现用反射按名字找 <c>"NavigationRequested"</c> 事件，找不到就静默返回——页面漏订时按钮点了
        /// 没反应且日志干净，是"死导航"的一类成因，故改为接口。
        /// </para>
        /// </summary>
        private void HookNavigationRequested(IInkNavigationSource page)
        {
            if (page == null) return;

            page.NavigationRequested += domId =>
            {
                try
                {
                    NavigateToPage(domId);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] {page.GetType().Name} 导航失败 ({domId}): {ex.Message}");
                }
            };
        }

        /// <summary>
        /// 强制刷新所有UI状态
        /// </summary>
        public void RefreshAllUI()
        {
            try
            {
                FlaxEngine.Debug.Log("刷新所有UI状态");
                
                var currentScene = _stateManager.CurrentScene;
                OnSceneChanged(SceneType.Start, currentScene);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"刷新UI状态时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定场景的UI组件
        /// </summary>
        public T GetUIComponent<T>(SceneType sceneType) where T : Script
        {
            if (_uiComponents.TryGetValue(sceneType, out var component))
            {
                return component as T;
            }
            return null;
        }

        /// <summary>
        /// 检查UI组件是否可用
        /// </summary>
        public bool IsUIComponentAvailable(SceneType sceneType)
        {
            return _uiComponents.ContainsKey(sceneType) && _uiComponents[sceneType] != null;
        }

        /// <summary>
        /// 重新初始化UI组件（用于解决编辑器播放模式问题）
        /// </summary>
        public void ReinitializeUIComponents()
        {
            try
            {
                FlaxEngine.Debug.Log("重新初始化UI组件");
                
                // 清理现有组件
                _uiComponents.Clear();
                _currentActiveUI = null;
                
                // 重新初始化
                InitializeUIComponents();
                
                // 刷新UI状态
                RefreshAllUI();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"重新初始化UI组件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示效果图标
        /// </summary>
        public void ShowEffectIcon(ulong targetId, int effectId, string effectName, float duration)
        {
            try
            {
                FlaxEngine.Debug.Log($"显示效果图标: 目标{targetId}, 效果{effectId}({effectName}), 持续时间{duration}秒");

                var key = (targetId, effectId);
                if (_activeEffectIcons.ContainsKey(key))
                {
                    // 刷新已有效果的持续时间
                    var existing = _activeEffectIcons[key];
                    existing.RemainingDuration = duration;
                    existing.EffectName = effectName;
                }
                else
                {
                    // 添加新效果图标记录
                    _activeEffectIcons[key] = new EffectIconEntry
                    {
                        TargetId = targetId,
                        EffectId = effectId,
                        EffectName = effectName,
                        RemainingDuration = duration,
                        TotalDuration = duration
                    };
                    FlaxEngine.Debug.Log($"新增效果图标: {effectName} (ID:{effectId}), 持续{duration}秒");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"显示效果图标时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 移除效果图标
        /// </summary>
        public void RemoveEffectIcon(ulong targetId, int effectId)
        {
            var key = (targetId, effectId);
            if (_activeEffectIcons.Remove(key))
            {
                FlaxEngine.Debug.Log($"移除效果图标: 目标{targetId}, 效果{effectId}");
            }
        }

        /// <summary>
        /// 处理Buff显示消息
        /// </summary>
        public void HandleBuffDisplayMessage(Horizon.Game.Message.Network.BuffDisplayMessage message)
        {
            if (message == null) return;

            try
            {
                switch (message.Operation)
                {
                    case Horizon.Game.Message.Network.BuffOperation.Add:
                    case Horizon.Game.Message.Network.BuffOperation.Refresh:
                    case Horizon.Game.Message.Network.BuffOperation.Stack:
                        ShowEffectIcon(message.TargetId, message.EffectId, message.EffectName, message.Duration);
                        break;
                    case Horizon.Game.Message.Network.BuffOperation.Remove:
                        RemoveEffectIcon(message.TargetId, message.EffectId);
                        break;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理Buff显示消息时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取目标实体上的活跃效果数量
        /// </summary>
        public int GetActiveEffectCount(ulong targetId)
        {
            int count = 0;
            foreach (var entry in _activeEffectIcons.Values)
            {
                if (entry.TargetId == targetId)
                    count++;
            }
            return count;
        }

        public override void OnDestroy()
        {
            // 销毁水墨主题 UI
            DestroyInkWashUI();

            // 取消事件订阅
            if (_stateManager != null)
            {
                _stateManager.SceneChanged -= OnSceneChanged;
                _stateManager.LoadingStateChanged -= OnLoadingStateChanged;
            }

            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.TransitionCompleted -= OnSceneTransitionCompleted;

            // 清理组件引用
            _uiComponents.Clear();
            _currentActiveUI = null;

            // 清理本地玩家数据绑定引用，避免悬空指针
            _cachedAttributes = null;
            _cachedSkills = null;
            _localPlayerReady = false;
            _activeCombatHud = null;
            _activeCombatHudV2 = null;
            _activeCombatHudTraditional = null;
            _activeCharacterPanel = null;
            _activeSkillTree = null;
            _inkWorldInputGate?.Release();
            _inkWorldInputGate = null;

            // 清空单例引用
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }

    /// <summary>
    /// 效果图标条目
    /// </summary>
    public class EffectIconEntry
    {
        public ulong TargetId { get; set; }
        public int EffectId { get; set; }
        public string EffectName { get; set; } = "";
        public float RemainingDuration { get; set; }
        public float TotalDuration { get; set; }
    }
}