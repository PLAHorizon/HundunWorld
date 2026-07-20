using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using Game.Combat.Skills;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.UI.Ink;
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
using HundunWorld.Game.UI.Ink.Pages.Inventory;
using HundunWorld.Game.UI.Ink.Pages.Livelihood;
using HundunWorld.Game.UI.Ink.Pages.Mail;
using HundunWorld.Game.UI.Ink.Pages.Map;
using HundunWorld.Game.UI.Ink.Pages.MartialRecord;
using HundunWorld.Game.UI.Ink.Pages.Multiplayer;
using HundunWorld.Game.UI.Ink.Pages.MountPet;
using HundunWorld.Game.UI.Ink.Pages.Personal;
using HundunWorld.Game.UI.Ink.Pages.Quest;
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

        // 活跃的效果图标
        private readonly Dictionary<(ulong TargetId, int EffectId), EffectIconEntry> _activeEffectIcons = new();

        // 本地玩家数据绑定缓存
        private CharacterAttributesComponent _cachedAttributes;
        private SkillBase[] _cachedSkills;
        private bool _localPlayerReady;
        private float _rebindLogThrottle;

        /// <summary>最后已知活动页面 dom-id，用于 Canvas 自愈重建后恢复导航状态</summary>
        private string _lastKnownDomId = InkPageDomIds.CombatHud;
        // 活动页面引用（用于 OnUpdate 动态刷新）
        private CombatHudPage _activeCombatHud;
        private CombatHudV2Page _activeCombatHudV2;
        private MenuCharAttributesV2Page _activeMenuCharAttributesV2;

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
                // 0. 水墨 UI 健康自愈：检测 Canvas/Shell 被意外销毁并自动重建
                //    仅当 _inkPageShell 非 null（已首次创建过）且健康检查失败时触发，
                //    避免在 Login 场景每帧尝试创建
                if (_inkPageShell != null && !IsInkUIHealthy())
                {
                    FlaxEngine.Debug.LogWarning("[MainUIManager] 检测到水墨 UI 已损坏，触发自愈重建");
                    string lastDom = _lastKnownDomId;
                    DestroyInkWashUI();
                    InitializeInkWashUI();
                    if (_inkPageRouter != null && !string.IsNullOrEmpty(lastDom))
                    {
                        _inkPageRouter.NavigateTo(lastDom);
                    }
                    if (_localPlayerReady && _cachedAttributes != null)
                    {
                        RebindActivePageData();
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

                        // 若当前已打开 MenuCharAttributesV2Page 但创建时玩家未就绪（使用 mock 数据），立即重绑真实数据
                        if (_activeMenuCharAttributesV2 != null)
                        {
                            try
                            {
                                _activeMenuCharAttributesV2.BindCharacter(attr);
                                FlaxEngine.Debug.Log("[MainUIManager] MenuCharAttributesV2 已重绑本地玩家数据");
                            }
                            catch (Exception bindEx)
                            {
                                FlaxEngine.Debug.LogError($"[MainUIManager] MenuCharAttributesV2 重绑失败: {bindEx.Message}");
                            }
                        }
                    }
                }

                // 2. 持续刷新 CombatHud 动态数据（仅当当前页面为 combat-hud 且玩家就绪时）
                if (_localPlayerReady && _inkPageRouter?.CurrentPageDomId == InkPageDomIds.CombatHud && _activeCombatHud != null)
                {
                    RefreshCombatHudDynamicData();
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
        }

        /// <summary>
        /// 场景切换事件处理
        /// </summary>
        private void OnSceneChanged(SceneType previousScene, SceneType newScene)
        {
            FlaxEngine.Debug.Log($"主UI管理器处理场景切换: {previousScene} -> {newScene}");

            try
            {
                // 离开 GameWorld 场景时隐藏水墨 UI（不销毁，保留控件树与页面状态）
                if (previousScene == SceneType.GameWorld)
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
                FlaxEngine.Debug.LogWarning($"未找到场景 {newScene} 对应的UI组件");
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
        private void InitializeInkWashUI()
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
                _inkPageRouter.Initialize(_inkPageShell, InkPageDomIds.CombatHud);

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

                // 7. 默认导航到战斗 HUD
                FlaxEngine.Debug.Log("[MainUIManager] 准备导航到 CombatHud...");
                bool navSuccess = NavigateToPage(InkPageDomIds.CombatHud);
                if (!navSuccess)
                {
                    FlaxEngine.Debug.LogError("[MainUIManager] 首次导航到 CombatHud 失败，Ink UI 可能仅显示空背景");
                }

                FlaxEngine.Debug.Log($"[MainUIManager] 水墨主题 UI 初始化完成，Canvas.ChildrenCount={_inkCanvas.GUI.ChildrenCount}, 导航结果={navSuccess}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 初始化水墨 UI 时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 查找或创建承载水墨 UI 的专用 UICanvas。
        /// 关键约束：主 UI 必须使用名为 InkWashUICanvas 的独立 Canvas，禁止与其他 UI
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

            _inkPageRouter.RegisterPage(InkPageDomIds.CombatHud, () => CreateCombatHud());
            _inkPageRouter.RegisterPage(InkPageDomIds.CombatHudTraditional, () => CreateCombatHudTraditional());
            _inkPageRouter.RegisterPage(InkPageDomIds.Loading1, () => CreateLoadingPage1());
            _inkPageRouter.RegisterPage(InkPageDomIds.Loading2, () => CreateLoadingPage2());
            _inkPageRouter.RegisterPage(InkPageDomIds.ChapterTransition, () => CreateChapterTransition());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavQuests, () => CreateMenuQuests());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavShop, () => CreateMenuShop());
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupItemAcquired, () => CreatePopupItemAcquired());
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupMessage, () => CreatePopupMessage());
            _inkPageRouter.RegisterPage(InkPageDomIds.RewardAchievement, () => CreateRewardAchievement());
            _inkPageRouter.RegisterPage(InkPageDomIds.RewardQuestComplete, () => CreateRewardQuestComplete());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSettings, () => CreateSettings());
            _inkPageRouter.RegisterPage(InkPageDomIds.CombatHudV2, () => CreateCombatHudV2());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCharacterV2, () => CreateMenuCharAttributesV2());
            _inkPageRouter.RegisterPage(InkPageDomIds.DeathScreen, () => CreateDeathScreen());
            _inkPageRouter.RegisterPage(InkPageDomIds.DialogueConfirm, () => CreateDialogueConfirm());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavBattlePrep, () => CreateMenuBattlePrep());
            _inkPageRouter.RegisterPage(InkPageDomIds.Acupoint, () => CreateAcupoint());
            _inkPageRouter.RegisterPage(InkPageDomIds.Qte, () => CreateQte());
            _inkPageRouter.RegisterPage(InkPageDomIds.RewardLevelUp, () => CreateRewardLevelUp());
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupVerification, () => CreatePopupVerification());
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupMartialArts, () => CreatePopupMartialArts());
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupSkillRealization, () => CreatePopupSkillRealization());
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupMartialDetail, () => CreatePopupMartialDetail());
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupGuideSide, () => CreatePopupGuideSide());
            _inkPageRouter.RegisterPage(InkPageDomIds.PopupBestiarySide, () => CreatePopupBestiarySide());

            // 注册新增页面（串联所有 UI）
            _inkPageRouter.RegisterPage(InkPageDomIds.NavAppearance, () => CreateMenuAppearance());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavActivities, () => CreateMenuActivities());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMail, () => CreateMenuMail());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSect, () => CreateMenuSect());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavTeam, () => CreateMenuTeam());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavBestiary, () => CreateMenuBestiary());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavBattlePass, () => CreateMenuBattlePass());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavGacha, () => CreateMenuGacha());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavLivelihood, () => CreateMenuLivelihood());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavPersonalInfo, () => CreateMenuPersonalInfo());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMartialRecord, () => CreateMenuMartialRecord());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCasualMode, () => CreateMenuCasualMode());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavTime, () => CreateMenuTime());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavShopRare, () => CreateShopRareItems());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMultiplayer, () => CreateMultiplayer());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavPhotoMode, () => CreatePhotoMode());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavElementVision, () => CreateElementVision());
            _inkPageRouter.RegisterPage(InkPageDomIds.CcFaceCustomize, () => CreateCcFaceCustomize());
            _inkPageRouter.RegisterPage(InkPageDomIds.CcNaming, () => CreateCcNaming());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSettingsAudio, () => CreateSettingsAudio());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavEquipment, () => CreateMenuEquipment());
            _inkPageRouter.RegisterPage(InkPageDomIds.UIGallery, () =>CreateUIGallery());

            // === 角色与背包子系统（game-ui-system 设计方案要求的 6 个页面） ===
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCharacterPanel, () => CreateCharacterPanelPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSkillPanel, () => CreateSkillPanelPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavInventory, () => CreateInventoryPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavEquipmentEnhance, () => CreateEquipmentEnhancePage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCrafting, () => CreateCraftingPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMountPet, () => CreateMountPetPage());

            // === 任务与技能子系统（game-ui-system 设计方案要求的 4 个页面） ===
            _inkPageRouter.RegisterPage(InkPageDomIds.NavQuestLog, () => CreateQuestLogPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavWorldMap, () => CreateWorldMapPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavCompass, () => CreateCompassPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavDungeonEntry, () => CreateDungeonEntryPage());

            // === 社交与商城子系统（game-ui-system 设计方案要求的 7 个页面） ===
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSocialGuild, () => CreateSocialGuildPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavFriends, () => CreateFriendsPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSocialMail, () => CreateSocialMailPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavMentor, () => CreateMentorPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavLeaderboard, () => CreateLeaderboardPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavAchievement, () => CreateAchievementPage());
            _inkPageRouter.RegisterPage(InkPageDomIds.NavSocialShop, () => CreateSocialShopPage());
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
        private bool IsInkUIHealthy()
        {
            try
            {
                return _inkCanvas != null
                    && _inkCanvas.GUI != null
                    && _inkPageShell != null
                    && !_inkPageShell.IsDisposing;
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

                // 清理活动页面引用，避免悬空指针
                _activeCombatHud = null;
                _activeCombatHudV2 = null;
                _activeMenuCharAttributesV2 = null;

                FlaxEngine.Debug.Log("[MainUIManager] 水墨主题 UI 已销毁");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MainUIManager] 销毁水墨 UI 时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 自愈重建后对当前活动页面重新执行数据绑定。
        /// 根据 _lastKnownDomId 判断页面类型，对 _activeCombatHud/_activeCombatHudV2/_activeMenuCharAttributesV2
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
                else if (_lastKnownDomId == InkPageDomIds.NavCharacterV2 && _activeMenuCharAttributesV2 != null)
                {
                    _activeMenuCharAttributesV2.BindCharacter(_cachedAttributes);
                    FlaxEngine.Debug.Log("[MainUIManager] 自愈后已重绑 MenuCharAttributesV2 数据");
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
                if (_inkPageRouter != null && _inkPageRouter.NavigateTo(domId))
                {
                    _lastKnownDomId = domId;
                    return true;
                }
            }
            catch (Exception ex)
            {
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
            return page;
        }

        /// <summary>
        /// 创建武学技能面板页面（skill-panel.html）。
        /// </summary>
        private SkillPanelPage CreateSkillPanelPage()
        {
            var page = new SkillPanelPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] SkillPanel 导航失败 ({domId}): {ex.Message}"); }
            };
            return page;
        }

        /// <summary>
        /// 创建背包行囊页面（inventory.html）。
        /// </summary>
        private InventoryPage CreateInventoryPage()
        {
            var page = new InventoryPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] Inventory 导航失败 ({domId}): {ex.Message}"); }
            };
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
            return page;
        }

        // ===============================================================
        // 任务与技能子系统：4 个页面工厂（game-ui-system 设计方案）
        // ===============================================================

        /// <summary>
        /// 创建江湖任务志页面（quest-log.html）。
        /// </summary>
        private QuestLogPage CreateQuestLogPage()
        {
            var page = new QuestLogPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] QuestLog 导航失败 ({domId}): {ex.Message}"); }
            };
            return page;
        }

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
            return page;
        }

        /// <summary>
        /// 创建江湖商城页面（shop.html）。
        /// </summary>
        private SocialShopPage CreateSocialShopPage()
        {
            var page = new SocialShopPage();
            if (_inkPageShell?.ParticleSystem != null)
            {
                page.ParticleSystem = _inkPageShell.ParticleSystem;
            }
            page.NavigationRequested += (domId) =>
            {
                try { NavigateToPage(domId); }
                catch (Exception ex) { FlaxEngine.Debug.LogError($"[MainUIManager] SocialShop 导航失败 ({domId}): {ex.Message}"); }
            };
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
        /// 创建加载页 2 并订阅进度完成事件。
        /// 进度满后自动推进到章节过场页。
        /// </summary>
        /// <returns>LoadingPage2 实例</returns>
        private LoadingPage2 CreateLoadingPage2()
        {
            var page = new LoadingPage2();
            page.ProgressComplete += () =>
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
                    NavigateToPage(InkPageDomIds.CombatHud);
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
            return new MenuQuestsPage();
        }

        /// <summary>
        /// 创建商店菜单页面。
        /// </summary>
        /// <returns>MenuShopPage 实例</returns>
        private MenuShopPage CreateMenuShop()
        {
            return new MenuShopPage();
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
                    _inkPageRouter?.NavigateToHud();
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
                    FlaxEngine.Debug.Log("[MainUIManager] PopupMessage 回复");
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
                    FlaxEngine.Debug.Log("[MainUIManager] PopupMessage 收藏");
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
                    _inkPageRouter?.NavigateToHud();
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
            return page;
        }

        /// <summary>
        /// 创建设置页面。
        /// </summary>
        /// <returns>SettingsPage 实例</returns>
        private SettingsPage CreateSettings()
        {
            return new SettingsPage();
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
        /// 创建角色属性菜单 v2 页面并订阅导航请求事件。
        /// </summary>
        /// <returns>MenuCharAttributesV2Page 实例</returns>
        private MenuCharAttributesV2Page CreateMenuCharAttributesV2()
        {
            var page = new MenuCharAttributesV2Page();
            page.NavigationRequested += (domId) =>
            {
                try
                {
                    NavigateToPage(domId);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] MenuCharAttributesV2 导航失败 ({domId}): {ex.Message}");
                }
            };
            // 即时绑定：若本地玩家已就绪则直接绑定真实数据
            if (TryGetLocalPlayerAttributes(out var attr))
            {
                page.BindCharacter(attr);
                _cachedAttributes = attr;
                _localPlayerReady = true;
                FlaxEngine.Debug.Log("[MainUIManager] MenuCharAttributesV2 已即时绑定本地玩家数据");
            }
            else
            {
                FlaxEngine.Debug.Log("[MainUIManager] MenuCharAttributesV2 创建时本地玩家未就绪，等待重绑");
            }
            _activeMenuCharAttributesV2 = page;
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
                    _inkPageRouter?.NavigateToHud();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[MainUIManager] DeathScreen 破招失败: {ex.Message}");
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
                    _inkPageRouter?.NavigateToHud();
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
            return new PopupVerification();
        }

        private PopupMartialArts CreatePopupMartialArts()
        {
            return new PopupMartialArts();
        }

        private PopupSkillRealization CreatePopupSkillRealization()
        {
            return new PopupSkillRealization();
        }

        private PopupMartialDetail CreatePopupMartialDetail()
        {
            return new PopupMartialDetail();
        }

        private PopupGuideSide CreatePopupGuideSide()
        {
            return new PopupGuideSide();
        }

        private PopupBestiarySide CreatePopupBestiarySide()
        {
            return new PopupBestiarySide();
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
        private MenuActivitiesPage CreateMenuActivities()
        {
            var page = new MenuActivitiesPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建邮件菜单页面。
        /// </summary>
        private MenuMailPage CreateMenuMail()
        {
            var page = new MenuMailPage();
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
        private MenuTeamPage CreateMenuTeam()
        {
            var page = new MenuTeamPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建博物志菜单页面。
        /// </summary>
        private MenuBestiaryPage CreateMenuBestiary()
        {
            var page = new MenuBestiaryPage();
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
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建拍照模式菜单页面。
        /// </summary>
        private PhotoModePage CreatePhotoMode()
        {
            var page = new PhotoModePage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建元素视野页面。
        /// </summary>
        private ElementVisionPage CreateElementVision()
        {
            var page = new ElementVisionPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建创角捏脸页面。
        /// </summary>
        private CharacterFaceCustomizePage CreateCcFaceCustomize()
        {
            var page = new CharacterFaceCustomizePage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建创角命名页面。
        /// </summary>
        private CharacterNamingPage CreateCcNaming()
        {
            var page = new CharacterNamingPage();
            HookNavigationRequested(page);
            return page;
        }

        /// <summary>
        /// 创建音频设置子页面。
        /// </summary>
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
        private void HookNavigationRequested(object page)
        {
            try
            {
                // 使用反射查找 NavigationRequested 事件
                var type = page?.GetType();
                if (type == null) return;
                var ev = type.GetEvent("NavigationRequested");
                if (ev == null) return;

                // 构造 Action<string> 处理器
                Action<string> handler = (domId) =>
                {
                    try
                    {
                        NavigateToPage(domId);
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[MainUIManager] {type.Name} 导航失败 ({domId}): {ex.Message}");
                    }
                };

                // 将 Action<string> 转换为事件类型
                var delegateType = ev.EventHandlerType;
                var converter = typeof(Action<string>).GetMethod("Invoke");
                var typedHandler = System.Delegate.CreateDelegate(delegateType, handler.Target, handler.Method);
                ev.AddEventHandler(page, typedHandler);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[MainUIManager] HookNavigationRequested 失败: {ex.Message}");
            }
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

            // 清理组件引用
            _uiComponents.Clear();
            _currentActiveUI = null;

            // 清理本地玩家数据绑定引用，避免悬空指针
            _cachedAttributes = null;
            _cachedSkills = null;
            _localPlayerReady = false;
            _activeCombatHud = null;
            _activeCombatHudV2 = null;
            _activeMenuCharAttributesV2 = null;

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