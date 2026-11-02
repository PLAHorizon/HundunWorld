using System;
using System.Collections.Generic;
using System.Reflection;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CommonUI;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 游戏玩法 HUD。移植自 UE5 NarrativeArsenal: <c>Widgets/NarrativeGameplayHUD.h/.cpp</c>
    /// （<c>UNarrativeGameplayHUD : UCommonUserWidget</c> —— 分层 UI 容器、菜单打开与通知显示）。
    /// <para>
    /// UE5 用 Lyra 式的 "layer → ActivatableWidgetContainerBase" 技术管理 UI 分层，
    /// Flax 侧的等价物是 <see cref="NarrativeCommonHUD"/>（GUI 根容器）+
    /// <see cref="NarrativeActivatableWidgetContainer"/>（激活栈），本类把它们按层标签组织起来，
    /// 并实现 UE5 的三个真实行为：
    /// <list type="bullet">
    /// <item><description><c>OpenMenu</c>：层的容器存在 → 校验 OwnerPC 有效 →
    /// 菜单 CDO 的 <c>BlockTags</c> 命中玩家标签则拒绝 → 栈顶已是同一菜单类型则拒绝（重复打开守卫）→
    /// <c>AddWidget</c> 入栈；</description></item>
    /// <item><description><c>RegisterLayer</c> / <c>GetLayerContainer</c>：层注册表（UE5 的 IsDesignTime 判定
    /// 在 Flax 运行期脚本里恒为"非设计期"，因此直接写入）；</description></item>
    /// <item><description><c>SetHUDHidden</c>：隐藏各层与通知，但保留
    /// <see cref="EssentialWidgets"/>（过场动画期间仍需显示的控件），
    /// <c>bHideEvenEssentialWidgets</c> 为真时连它们一起隐藏。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// UE5 的 <c>ShowNotification</c> / <c>ShowMajorNotification</c> 是 BlueprintImplementableEvent
    /// （"由 UMG 控件自己实现显示"），Flax 没有 UMG，因此这里用真实 Flax GUI 落地：
    /// 小提示按栈堆叠、到时间自动移除；重大通知占一个独立槽位，由
    /// <c>bOverrideCurrentNotification</c> 决定是否覆盖当前那条。
    /// </para>
    /// </summary>
    public class NarrativeGameplayHUD : Script
    {
        /// <summary>宿主 HUD（提供 GUI 根控件与激活栈）。为空时按 <see cref="NarrativeCommonHUD.Instance"/> 自动解析。</summary>
        public NarrativeCommonHUD HudHost;

        /// <summary>拥有本 HUD 的玩家控制器（对应 UE5 OwnerPC）。为空时扫描场景自动解析一次。</summary>
        public NarrativePlayerController OwnerPC;

        /// <summary>关键控件列表：隐藏 HUD 时仍保持显示（对话选项等）。对应 UE5 EssentialWidgets。</summary>
        public List<Control> EssentialWidgets { get; } = new List<Control>();

        /// <summary>分层 UI 容器（层标签 → 激活栈）。对应 UE5 Layers。</summary>
        public Dictionary<GameplayTag, NarrativeActivatableWidgetContainer> Layers { get; } = new Dictionary<GameplayTag, NarrativeActivatableWidgetContainer>();

        /// <summary>提示堆栈最多同时显示几条（UE5 由 UMG 布局决定，这里给出确定的上限）。</summary>
        public int MaxNotificationStack = 5;

        /// <summary>通知距屏幕右下的边距（像素）。</summary>
        public Float2 NotificationMargin = new Float2(24f, 24f);

        /// <summary>通知条目之间的间距（像素）。</summary>
        public float NotificationSpacing = 6f;

        /// <summary>通知是否可见（SetHUDHidden 的反面，只读）。</summary>
        public bool bHUDHidden { get; private set; }

        /// <summary>当前准星控件（由 <see cref="EnsureDefaultCrosshair"/> 创建，对应 UE5 HUD 蓝图里的准星）。</summary>
        public CrosshairWidget Crosshair { get; private set; }

        /// <summary>默认准星类型（必须是可实例化的具体 <see cref="CrosshairWidget"/>）。</summary>
        public Type DefaultCrosshairType = typeof(NarrativeDefaultCrosshair);

        /// <summary>默认菜单类型（必须是可实例化的具体 <see cref="NarrativeMenu"/>）。</summary>
        public Type DefaultMenuType = typeof(NarrativePauseMenu);

        /// <summary>是否用 Esc 键切换暂停菜单（对应 UE5 的菜单输入绑定）。</summary>
        /// <summary>
        /// 是否由 Esc 拉起默认菜单。<c>false</c>：Esc 归本仓水墨 UI 的中央裁决
        /// （<c>MainUIManager</c>：焦点→弹层→退一层→系统菜单）。开着这项会让插件与
        /// Ink 两套栈同时抢 Esc——插件弹出的 <see cref="NarrativePauseMenu"/> 既不停游戏
        /// （「继续游戏」只是 Close），入口还指向已被合并掉的旧任务日志面板，并整块盖在 HUD 上。
        /// 菜单本身仍可由 <c>NarrativeRuntimeBootstrap.ToggleDefaultMenu()</c> 显式调用。
        /// </summary>
        public bool bOpenMenuOnEscape = false;

        /// <summary>准星扩散衰减速度（像素/秒）。</summary>
        public float CrosshairSpreadDecay = 40f;

        /// <summary>通知层根（由 EnsureRuntimeWidgets 创建）。</summary>
        private ContainerControl _notificationRoot;
        private ContainerControl _majorNotificationRoot;

        /// <summary>准星层根（由 EnsureRuntimeWidgets 创建，铺满 GUI 根）。</summary>
        private ContainerControl _crosshairRoot;

        // ===== 交互提示（affordance）：订阅 PlayerInteractionComponent 的事件 → 渲染 =====

        /// <summary>交互提示根（由 EnsureRuntimeWidgets 创建，居中偏下，随注视/按住事件出现与消失）。</summary>
        private ContainerControl _interactPromptRoot;

        /// <summary>交互提示：目标名称行。</summary>
        private Label _interactPromptNameLabel;

        /// <summary>交互提示：快捷键 + 动词 + 按住进度行。</summary>
        private Label _interactPromptActionLabel;

        /// <summary>当前订阅了注视/按住事件的玩家交互组件（换宿主时重挂）。</summary>
        private NarrativePro.Interaction.PlayerInteractionComponent _promptSource;

        /// <summary>OnFoundInteractable / OnLostInteractable 驱动的目标位。</summary>
        private bool _bPromptHasTarget;

        /// <summary>OnInteractPressed / OnInteractReleased 驱动的按住位。</summary>
        private bool _bPromptHeld;

        /// <summary>通知层的名称记账（Flax Control 无 Name，标识只存在 HUD 自身）。</summary>
        private string _notificationRootName = string.Empty;
        private string _majorNotificationRootName = string.Empty;

        private readonly List<NotificationEntry> _minorStack = new List<NotificationEntry>();
        private NotificationEntry _majorEntry;
        private bool _bResolvedRuntime;
        private bool _bPendingResolve;

        /// <summary>一条运行时通知（文本 + 副文本 + 剩余时长 + 宽度）。</summary>
        private class NotificationEntry
        {
            public Label TextLabel;
            public Label SubtitleLabel;
            public ContainerControl Root;
            public float Remaining;

            /// <summary>通知根控件的名称记账（Flax Control 无 Name，标识存在 HUD 自身）。</summary>
            public string Name;
        }

        // ===== 生命周期（对应 UE5 NativeConstruct） =====

        public override void OnEnable()
        {
            base.OnEnable();

            _bResolvedRuntime = false;
            _bPendingResolve = true;

            // UI 激活栈变化（菜单开合会翻转 bGameInputBlocked）即时刷新交互提示
            NarrativePro.CommonUI.NarrativeInputRouter.OnStackChanged += HandlePromptStackChanged;
        }

        public override void OnDisable()
        {
            NarrativePro.CommonUI.NarrativeInputRouter.OnStackChanged -= HandlePromptStackChanged;
            AttachPromptSource(null);
            ClearNotifications();
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (_bPendingResolve)
            {
                _bPendingResolve = false;
                EnsureRuntimeWidgets();
            }

            TickNotifications(Time.DeltaTime);
            TickCrosshair(Time.DeltaTime);
            TickInteractPrompt();
            TickMenuHotkey();
        }

        /// <summary>
        /// 解析宿主 HUD / OwnerPC 并建立通知层。GUI 只能在主线程创建，故从 OnUpdate 里驱动。
        /// </summary>
        private void EnsureRuntimeWidgets()
        {
            if (_bResolvedRuntime) return;

            if (HudHost == null) HudHost = NarrativeCommonHUD.Instance;

            // 只在宿主尚未初始化时驱动它建根控件（Initialize 会覆盖已设的根，故把现有根原样传回）
            if (HudHost != null && !HudHost.IsInitialized)
            {
                HudHost.Initialize(HudHost.RootControl);
            }

            if (HudHost == null || HudHost.RootControl == null)
            {
                NarrativeLog.LogWarning("NarrativeGameplayHUD：没有可用的 NarrativeCommonHUD 根控件，通知与层容器暂不可用。");
                return;
            }

            OwnerPC ??= ResolvePlayerController();

            // Flax 的 Control 没有名字：层标识保存在 HUD 自身的记账字段里（对应 UE5 的控件命名，
            // 激活栈的按名查找走 NarrativeActivatableWidgetContainer.Name / FindStackByName）
            _notificationRoot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _notificationRootName = "NarrativeNotificationLayer";
            _majorNotificationRoot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _majorNotificationRootName = "NarrativeMajorNotificationLayer";
            _crosshairRoot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            // 交互提示：目标名 + 「[交互键] 动词 按住进度」。挂进 HUD 自身的根层，
            // 因此 SetHUDHidden 的记账（ApplyHiddenState）与 UI 栈屏蔽（NarrativeInputRouter
            // .bGameInputBlocked，菜单打开即屏蔽）都会自然作用到它，无需再造一套平行 UI。
            _interactPromptRoot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Visible = false,
            };
            _interactPromptNameLabel = new Label
            {
                Text = string.Empty,
                AutoWidth = true,
                AnchorPreset = AnchorPresets.TopLeft,
                Wrapping = TextWrapping.NoWrap,
            };
            ApplyLabelStyle(_interactPromptNameLabel, 16f);
            _interactPromptRoot.AddChild(_interactPromptNameLabel);

            _interactPromptActionLabel = new Label
            {
                Text = string.Empty,
                AutoWidth = true,
                AnchorPreset = AnchorPresets.TopLeft,
                Wrapping = TextWrapping.NoWrap,
            };
            ApplyLabelStyle(_interactPromptActionLabel, 13f);
            _interactPromptRoot.AddChild(_interactPromptActionLabel);

            HudHost.RootControl.AddChild(_notificationRoot);
            HudHost.RootControl.AddChild(_majorNotificationRoot);
            HudHost.RootControl.AddChild(_crosshairRoot);
            HudHost.RootControl.AddChild(_interactPromptRoot);
            _interactPromptRoot.BringToFront();
            _majorNotificationRoot.BringToFront();
            _notificationRoot.BringToFront();
            _crosshairRoot.SendToBack();

            LayoutNotificationHosts();
            HudHost.RootControl.SizeChanged += control =>
            {
                LayoutNotificationHosts();
                CenterCrosshair();
            };

            // 已注册的层铺满新的根
            foreach (var layer in Layers.Values)
            {
                LayoutContainer(layer);
            }

            _bResolvedRuntime = true;

            // 准星不再随场景常驻：鼠标锁定模式已取消，屏幕中心由箭头指针指示。
            // 只有装备了带 CrosshairWidgetPath 的武器时才经 SetCrosshair 建出瞄具。
            ApplyHiddenState();
        }

        /// <summary>扫描场景解析本地玩家控制器（对应 UE5 的 GetOwningPlayer + Cast）。</summary>
        private NarrativePlayerController ResolvePlayerController()
        {
            if (Actor != null)
            {
                var own = Actor.GetScript<NarrativePlayerController>();
                if (own != null) return own;
            }

            try
            {
                var actors = Level.GetActors<Actor>(true);
                if (actors != null)
                {
                    for (int i = 0; i < actors.Length; i++)
                    {
                        if (actors[i] == null) continue;
                        var pc = actors[i].GetScript<NarrativePlayerController>();
                        if (pc != null) return pc;
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("NarrativeGameplayHUD：解析玩家控制器失败：" + ex.Message);
            }

            return null;
        }

        // ===== 层管理（对应 UE5 RegisterLayer / GetLayerContainer） =====

        /// <summary>注册一个可供控件压入的层。对应 UE5 RegisterLayer。</summary>
        public virtual NarrativeActivatableWidgetContainer RegisterLayer(GameplayTag layerTag, NarrativeActivatableWidgetContainer layerWidget)
        {
            if (!layerTag.IsValid() || layerWidget == null) return null;

            // UE5 有 IsDesignTime() 守卫；Flax 脚本只在运行期执行，等价于"始终登记"
            Layers[layerTag] = layerWidget;
            AttachLayer(layerWidget);
            return layerWidget;
        }

        /// <summary>
        /// 取层容器。对应 UE5 <c>GetLayerContainer</c>（<c>Layers.FindRef</c>：未注册时返回 null）。
        /// </summary>
        public virtual NarrativeActivatableWidgetContainer GetLayerContainer(GameplayTag layerTag)
        {
            if (!layerTag.IsValid()) return null;
            return Layers.TryGetValue(layerTag, out var container) ? container : null;
        }

        /// <summary>
        /// 取层容器，未注册时按标签名创建一个铺满根的栈。
        /// （Flax 没有 UMG 设计期，UE5 需要在蓝图里预置栈，这里提供等价的按需创建口。）
        /// </summary>
        public NarrativeActivatableWidgetContainer GetOrAddLayerContainer(GameplayTag layerTag)
        {
            var existing = GetLayerContainer(layerTag);
            if (existing != null) return existing;

            EnsureRuntimeWidgets();
            if (HudHost == null || HudHost.RootControl == null) return null;

            var container = new NarrativeActivatableWidgetContainer { Name = layerTag.TagName };
            return RegisterLayer(layerTag, container);
        }

        /// <summary>把层容器挂到 GUI 根上并铺满。</summary>
        private void AttachLayer(NarrativeActivatableWidgetContainer container)
        {
            if (container == null) return;

            if (HudHost != null && HudHost.RootControl != null && container.Parent != HudHost.RootControl)
            {
                HudHost.RootControl.AddChild(container);
            }
            LayoutContainer(container);
            container.BringToFront();
        }

        /// <summary>铺满一层（TopLeft 锚 + 显式尺寸，规避非 TopLeft 锚的构造期错位）。</summary>
        private void LayoutContainer(Control container)
        {
            if (container == null) return;

            Float2 size = HudHost != null && HudHost.RootControl != null ? HudHost.RootControl.Size : Float2.Zero;
            container.AnchorMin = Float2.Zero;
            container.AnchorMax = Float2.Zero;
            container.Location = Float2.Zero;
            container.Size = size;
            if (container is NarrativeActivatableWidgetContainer widgetContainer)
            {
                widgetContainer.RelayoutAll();
            }
        }

        // ===== 打开菜单（对应 UE5 OpenMenu） =====

        /// <summary>
        /// 打开菜单。对应 UE5 <c>UNarrativeGameplayHUD::OpenMenu(TSubclassOf&lt;UNarrativeMenu&gt;, FGameplayTag)</c>：
        /// 层容器存在 → 菜单类与 OwnerPC 有效 → 菜单 BlockTags 未命中玩家标签 → 栈顶不是同一菜单类 → 入栈。
        /// </summary>
        /// <param name="menuClass">菜单类型名（对应 UE5 的 TSubclassOf；支持全名/短名/程序集限定名）。</param>
        /// <param name="layerTag">要在其上打开菜单的层标签（UI.Layer.*）。</param>
        /// <returns>打开的菜单实例；被拦截时返回 null。</returns>
        public virtual NarrativeMenu OpenMenu(string menuClass, GameplayTag layerTag)
        {
            var menuType = ResolveMenuType(menuClass);
            return OpenMenu(menuType, layerTag);
        }

        /// <summary>类型版 OpenMenu（推荐入口，等价 UE5 直接传 TSubclassOf）。</summary>
        public virtual NarrativeMenu OpenMenu(Type menuType, GameplayTag layerTag)
        {
            var containerToUse = GetLayerContainer(layerTag);
            if (containerToUse == null)
            {
                NarrativeLog.LogWarning($"NarrativeGameplayHUD.OpenMenu：层 {layerTag} 未注册容器。");
                return null;
            }

            if (menuType == null || !typeof(NarrativeMenu).IsAssignableFrom(menuType))
            {
                NarrativeLog.LogWarning("NarrativeGameplayHUD.OpenMenu：菜单类型无效（需派生自 NarrativeMenu）。");
                return null;
            }

            if (OwnerPC == null)
            {
                // 与 UE5 一致：没有 OwnerPC 时不打开菜单（BlockTags 门禁无从判定）
                NarrativeLog.LogWarning("NarrativeGameplayHUD.OpenMenu：OwnerPC 未就绪，菜单未打开。");
                return null;
            }

            NarrativeMenu menu;
            try
            {
                menu = Activator.CreateInstance(menuType) as NarrativeMenu;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"NarrativeGameplayHUD.OpenMenu：创建菜单 {menuType.Name} 失败：{ex.Message}");
                return null;
            }

            if (menu == null) return null;

            // BlockTags 门禁（UE5 用 GetDefault&lt;UNarrativeMenu&gt;(MenuClass)-&gt;BlockTags）
            if (menu.BlockTags != null && menu.BlockTags.Count > 0 && OwnerPC.HasAnyMatchingGameplayTags(menu.BlockTags))
            {
                return null;
            }

            // 重复打开守卫：UE5 注释 —— "narrative 里的菜单从不把自己重复加到同一屏"
            var currentWidget = containerToUse.ActiveWidget;
            if (currentWidget != null && currentWidget.GetType() == menuType)
            {
                return null;
            }

            containerToUse.AddWidget(menu, true);
            return menu;
        }

        /// <summary>泛型版 OpenMenu（编译期已知菜单类型时使用）。</summary>
        public NarrativeMenuType OpenMenu<NarrativeMenuType>(GameplayTag layerTag) where NarrativeMenuType : NarrativeMenu, new()
        {
            return OpenMenu(typeof(NarrativeMenuType), layerTag) as NarrativeMenuType;
        }

        /// <summary>把菜单类名解析成 Type。</summary>
        private static Type ResolveMenuType(string menuClass)
        {
            if (string.IsNullOrEmpty(menuClass)) return null;

            var direct = Type.GetType(menuClass, false, true);
            if (direct != null) return direct;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(menuClass, false, false);
                if (type != null) return type;

                // 允许只写短名（"NarrativeMenu" / "Widgets.NarrativeMenu"）
                foreach (var candidate in SafeGetTypes(assembly))
                {
                    if (candidate.FullName != null &&
                        (string.Equals(candidate.FullName, menuClass, StringComparison.Ordinal) ||
                         candidate.FullName.EndsWith("." + menuClass, StringComparison.Ordinal)))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        // ===== HUD 显隐（对应 UE5 SetHUDHidden，BlueprintImplementableEvent） =====

        /// <summary>
        /// 设置 HUD 隐藏状态。对应 UE5 蓝图实现的 <c>SetHUDHidden</c>：
        /// 隐藏所有层与通知，<see cref="EssentialWidgets"/> 除非 bHideEvenEssentialWidgets 否则保留。
        /// </summary>
        public virtual void SetHUDHidden(bool bHideHUD, bool bHideEvenEssentialWidgets)
        {
            bHUDHidden = bHideHUD;
            _bHideEssential = bHideEvenEssentialWidgets;
            ApplyHiddenState();
        }

        private bool _bHideEssential;

        private void ApplyHiddenState()
        {
            foreach (var layer in Layers.Values)
            {
                if (layer != null) layer.Visible = !bHUDHidden;
            }

            if (_notificationRoot != null) _notificationRoot.Visible = !bHUDHidden;
            if (_majorNotificationRoot != null) _majorNotificationRoot.Visible = !bHUDHidden && _majorEntry != null;
            if (_crosshairRoot != null) _crosshairRoot.Visible = !bHUDHidden;

            bool bEssentialsVisible = !bHUDHidden || !_bHideEssential;
            for (int i = 0; i < EssentialWidgets.Count; i++)
            {
                var widget = EssentialWidgets[i];
                if (widget != null) widget.Visible = bEssentialsVisible;
            }

            // 过场/整盘隐藏（SetHUDHidden）同样作用到交互提示
            RefreshInteractPromptVisibility();
        }

        // ===== 通知（对应 UE5 ShowNotification / ShowMajorNotification） =====

        /// <summary>显示一条小提示（堆栈式，到时间自动消失）。对应 UE5 ShowNotification。</summary>
        public virtual void ShowNotification(string notificationText, float duration = 5f)
        {
            EnsureRuntimeWidgets();
            if (_notificationRoot == null || string.IsNullOrEmpty(notificationText)) return;

            var entry = CreateEntry(notificationText, null, _notificationRoot);
            entry.Remaining = duration > 0f ? duration : 5f;

            _minorStack.Add(entry);

            // 超出上限时移除最旧的一条，保持堆栈有界
            while (_minorStack.Count > MaxNotificationStack)
            {
                DestroyEntry(_minorStack[0]);
                _minorStack.RemoveAt(0);
            }

            LayoutNotifications();
        }

        /// <summary>显示一条重大通知（独立槽位）。对应 UE5 ShowMajorNotification。</summary>
        public virtual void ShowMajorNotification(string notificationText, string majorNotificationSubtext, float duration = 5f, bool bOverrideCurrentNotification = true)
        {
            EnsureRuntimeWidgets();
            if (_majorNotificationRoot == null || string.IsNullOrEmpty(notificationText)) return;

            if (_majorEntry != null)
            {
                if (!bOverrideCurrentNotification) return;
                DestroyEntry(_majorEntry);
                _majorEntry = null;
            }

            _majorEntry = CreateEntry(notificationText, majorNotificationSubtext, _majorNotificationRoot);
            _majorEntry.Remaining = duration > 0f ? duration : 5f;

            if (_majorNotificationRoot != null) _majorNotificationRoot.Visible = !bHUDHidden;
            LayoutNotifications();
        }

        /// <summary>立即清空所有通知（过场/切场景用）。</summary>
        public void ClearNotifications()
        {
            for (int i = _minorStack.Count - 1; i >= 0; i--)
            {
                DestroyEntry(_minorStack[i]);
            }
            _minorStack.Clear();

            if (_majorEntry != null)
            {
                DestroyEntry(_majorEntry);
                _majorEntry = null;
            }
        }

        private void TickNotifications(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            bool bRelayout = false;
            for (int i = _minorStack.Count - 1; i >= 0; i--)
            {
                var entry = _minorStack[i];
                entry.Remaining -= deltaTime;
                if (entry.Remaining <= 0f)
                {
                    DestroyEntry(entry);
                    _minorStack.RemoveAt(i);
                    bRelayout = true;
                }
            }

            if (_majorEntry != null)
            {
                _majorEntry.Remaining -= deltaTime;
                if (_majorEntry.Remaining <= 0f)
                {
                    DestroyEntry(_majorEntry);
                    _majorEntry = null;
                    if (_majorNotificationRoot != null) _majorNotificationRoot.Visible = false;
                    bRelayout = true;
                }
            }

            if (bRelayout) LayoutNotifications();
        }

        private NotificationEntry CreateEntry(string text, string subtitle, ContainerControl parent)
        {
            var root = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };

            var titleLabel = new Label
            {
                Text = text,
                AutoWidth = true,
                AnchorPreset = AnchorPresets.TopLeft,
                Wrapping = TextWrapping.NoWrap,
            };
            ApplyLabelStyle(titleLabel, 15f);
            root.AddChild(titleLabel);

            var entry = new NotificationEntry { Root = root, TextLabel = titleLabel, Name = "NarrativeNotification" };

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = new Label
                {
                    Text = subtitle,
                    AutoWidth = true,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Wrapping = TextWrapping.NoWrap,
                };
                ApplyLabelStyle(subtitleLabel, 12f);
                root.AddChild(subtitleLabel);
                entry.SubtitleLabel = subtitleLabel;
            }

            parent?.AddChild(root);
            return entry;
        }

        private void ApplyLabelStyle(Label label, float size)
        {
            if (label.Font != null)
            {
                label.Font.Size = size;
            }

            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings == null) return;

            label.TextColor = settings.bUseLightTextOnPrimary ? Color.White : settings.UIPrimaryColor;
        }

        private void DestroyEntry(NotificationEntry entry)
        {
            if (entry == null) return;

            if (entry.Root != null)
            {
                entry.Root.DisposeChildren();
                entry.Root.Dispose();
            }
        }

        private void LayoutNotificationHosts()
        {
            Float2 size = HudHost != null && HudHost.RootControl != null ? HudHost.RootControl.Size : Float2.Zero;

            if (_notificationRoot != null)
            {
                _notificationRoot.AnchorMin = Float2.Zero;
                _notificationRoot.AnchorMax = Float2.Zero;
                _notificationRoot.Location = Float2.Zero;
                _notificationRoot.Size = size;
            }

            if (_majorNotificationRoot != null)
            {
                _majorNotificationRoot.AnchorMin = Float2.Zero;
                _majorNotificationRoot.AnchorMax = Float2.Zero;
                _majorNotificationRoot.Location = Float2.Zero;
                _majorNotificationRoot.Size = size;
            }

            if (_crosshairRoot != null)
            {
                _crosshairRoot.AnchorMin = Float2.Zero;
                _crosshairRoot.AnchorMax = Float2.Zero;
                _crosshairRoot.Location = Float2.Zero;
                _crosshairRoot.Size = size;
            }

            LayoutNotifications();
            CenterCrosshair();
            LayoutInteractPrompt();
        }

        /// <summary>
        /// 通知排版：小提示自屏幕右下向上堆叠，重大通知居中偏下。
        /// 全部用 TopLeft 锚 + 显式 Location（非 TopLeft 锚在构造期会按父尺寸 0 重算位置）。
        /// </summary>
        private void LayoutNotifications()
        {
            Float2 hostSize = _notificationRoot != null ? _notificationRoot.Size : Float2.Zero;
            if (hostSize.X <= 0f || hostSize.Y <= 0f) return;

            float y = hostSize.Y - NotificationMargin.Y;

            for (int i = _minorStack.Count - 1; i >= 0; i--)
            {
                var entry = _minorStack[i];
                if (entry?.Root == null) continue;

                SizeEntry(entry);
                float height = entry.Root.Height;
                y -= height;

                entry.Root.Location = new Float2(hostSize.X - entry.Root.Width - NotificationMargin.X, y);
                y -= NotificationSpacing;
            }

            if (_majorEntry?.Root != null && _majorNotificationRoot != null)
            {
                SizeEntry(_majorEntry);
                Float2 majorSize = _majorNotificationRoot.Size;
                float x = (majorSize.X - _majorEntry.Root.Width) * 0.5f;
                float my = majorSize.Y * 0.75f - _majorEntry.Root.Height;
                _majorEntry.Root.Location = new Float2(Math.Max(0f, x), Math.Max(0f, my));
            }
        }

        private void SizeEntry(NotificationEntry entry)
        {
            if (entry?.Root == null) return;

            float width = entry.TextLabel != null ? entry.TextLabel.Width : 0f;
            float height = entry.TextLabel != null ? Math.Max(entry.TextLabel.Height, 20f) : 20f;

            if (entry.SubtitleLabel != null)
            {
                width = Math.Max(width, entry.SubtitleLabel.Width);
                entry.SubtitleLabel.Location = new Float2(0f, height);
                height += Math.Max(entry.SubtitleLabel.Height, 16f);
            }

            entry.Root.Size = new Float2(width, height);
        }

        // ===== 准星（对应 UE5 HUD 蓝图里的 CrosshairWidget 挂载 + SetSpread） =====

        /// <summary>
        /// 确保存在一个准星：已有则复用，否则按 <see cref="DefaultCrosshairType"/> 实例化具体子类。
        /// 对应 UE5 里装备武器时 HUD 创建准星蓝图子类的行为；只由装备驱动的
        /// <c>NarrativeRuntimeBootstrap.SetWeaponCrosshair</c> 调用，场景启动时不再自动创建。
        /// </summary>
        public virtual CrosshairWidget EnsureDefaultCrosshair()
        {
            if (Crosshair != null) return Crosshair;

            EnsureRuntimeWidgets();
            if (_crosshairRoot == null) return null;

            var type = DefaultCrosshairType ?? typeof(NarrativeDefaultCrosshair);
            CrosshairWidget crosshair = null;

            try
            {
                crosshair = Activator.CreateInstance(type) as CrosshairWidget;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"NarrativeGameplayHUD：创建准星 {type.Name} 失败：{ex.Message}");
            }

            crosshair ??= new NarrativeDefaultCrosshair();

            _crosshairRoot.AddChild(crosshair);
            crosshair.ApplyTheme();
            crosshair.CenterInParent();
            crosshair.Visible = !bHUDHidden;
            Crosshair = crosshair;
            return Crosshair;
        }

        /// <summary>换一把武器的准星（销毁旧的、按类型新建）。对应 UE5 装备时换准星蓝图。</summary>
        public CrosshairWidget SetCrosshair(Type crosshairType)
        {
            if (Crosshair != null)
            {
                Crosshair.Dispose();
                Crosshair = null;
            }

            DefaultCrosshairType = crosshairType ?? typeof(NarrativeDefaultCrosshair);
            return EnsureDefaultCrosshair();
        }

        /// <summary>按路径字符串换准星（对应 <c>WeaponItem.CrosshairWidgetPath</c>：支持全名/短名）。</summary>
        public CrosshairWidget SetCrosshair(string crosshairClassName)
        {
            var type = ResolveMenuType(crosshairClassName);
            if (type == null || !typeof(CrosshairWidget).IsAssignableFrom(type))
            {
                type = typeof(NarrativeDefaultCrosshair);
            }

            return SetCrosshair(type);
        }

        /// <summary>显示/隐藏准星。</summary>
        public void SetCrosshairVisible(bool bVisible)
        {
            if (_crosshairRoot != null) _crosshairRoot.Visible = bVisible && !bHUDHidden;
            Crosshair?.SetCrosshairVisible(bVisible);
        }

        /// <summary>设置准星扩散（开火/移动时调用）。对应 UE5 蓝图按 spread 改间隙。</summary>
        public void SetCrosshairSpread(float spread)
        {
            Crosshair?.SetSpread(spread);
        }

        /// <summary>把准星重新居中（父尺寸变化后调用）。</summary>
        public void CenterCrosshair()
        {
            Crosshair?.CenterInParent();
        }

        private void TickCrosshair(float deltaTime)
        {
            var crosshair = Crosshair;
            if (crosshair == null || deltaTime <= 0f) return;

            // 扩散自然回落（对应 UE5 的 recoil 恢复）
            if (crosshair.Spread > 0f)
            {
                crosshair.SetSpread(System.Math.Max(0f, crosshair.Spread - CrosshairSpreadDecay * deltaTime));
            }
        }

        // ===== 交互提示（subscribe→render：OnFound/OnLost/OnPressed/OnReleased → 本 HUD 绘制） =====

        /// <summary>UI 激活栈变化：菜单屏蔽游戏输入时交互提示必须同步消失。</summary>
        private void HandlePromptStackChanged(NarrativePro.CommonUI.NarrativeActivatableWidget topWidget)
        {
            RefreshInteractPromptVisibility();
        }

        /// <summary>
        /// 重挂事件订阅源。本地玩家交互组件由 <see cref="NarrativePro.Interaction.PlayerInteractionComponent.LocalPlayerInteraction"/>
        /// 注册表解析（引导可能在 HUD 初始化之后才给宿主挂组件，故每帧对账一次，换宿主自动重挂）。
        /// </summary>
        private void AttachPromptSource(NarrativePro.Interaction.PlayerInteractionComponent next)
        {
            if (_promptSource == next) return;

            if (_promptSource != null)
            {
                _promptSource.OnFoundInteractable -= HandleFoundInteractable;
                _promptSource.OnLostInteractable -= HandleLostInteractable;
                _promptSource.OnInteractPressed -= HandleInteractPressed;
                _promptSource.OnInteractReleased -= HandleInteractReleased;
            }

            _promptSource = next;

            if (next != null)
            {
                next.OnFoundInteractable += HandleFoundInteractable;
                next.OnLostInteractable += HandleLostInteractable;
                next.OnInteractPressed += HandleInteractPressed;
                next.OnInteractReleased += HandleInteractReleased;
            }

            _bPromptHasTarget = next != null && next.ViewedInteractable != null;
            _bPromptHeld = next != null && next.bInteractHeld;
            RefreshInteractPromptVisibility();
        }

        private void HandleFoundInteractable(NarrativePro.Interaction.NarrativeInteractableComponent found)
        {
            _bPromptHasTarget = found != null;

            // 缺陷4：商人 NPC 的 OnTradeBegin 必须有插件内消费者。HUD 在注视时订阅（注视丢失对称退订），
            // BeginTrade 成功即把「店名 + 库存(数量/买价)」打到提示带 —— 复用交互提示所在的通路。
            if (found is NarrativePro.AI.NPCInteractable foundVendor && foundVendor.bOffersTrade)
            {
                foundVendor.OnTradeBegin -= HandleTradeBegin;
                foundVendor.OnTradeBegin += HandleTradeBegin;
            }

            RefreshInteractPromptVisibility();
        }

        private void HandleLostInteractable(NarrativePro.Interaction.NarrativeInteractableComponent lost)
        {
            _bPromptHasTarget = false;
            _bPromptHeld = false;
            if (lost is NarrativePro.AI.NPCInteractable lostVendor)
            {
                lostVendor.OnTradeBegin -= HandleTradeBegin;
                // 货架网格是「看着这位商人」时才开的，注视一走要一起收掉
                CloseShopStockGrid();
            }
            RefreshInteractPromptVisibility();
        }

        /// <summary>
        /// 交易开始（插件内消费者）：把商人库存（名称 x数量(买价)）经 ShowNotification 呈现。
        /// 对应 UE5 由商店 BP 打开的入口；游戏侧要换自己的商店页，订阅同一事件即可
        /// （口径见 NarrativePlayerController.TradeKey 注释）。
        /// </summary>
        private void HandleTradeBegin(FlaxEngine.Actor interactor, NarrativePro.Items.NarrativeInventoryComponent vendorInventory)
        {
            if (vendorInventory == null) return;

            var vendorActor = vendorInventory.Actor;
            var npc = vendorActor != null ? vendorActor.GetScript<NarrativePro.AI.NPCInteractable>() : null;
            string title = npc != null && !string.IsNullOrEmpty(npc.TradeShopName)
                ? npc.TradeShopName
                : (vendorActor != null ? vendorActor.Name : "商店");

            var items = vendorInventory.Items;
            if (items == null || items.Count == 0)
            {
                ShowNotification($"{title}：暂无库存", 6f);
                return;
            }

            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder(128);
            sb.Append(title).Append(" 库存：");
            int shown = 0;
            for (int i = 0; i < items.Count && shown < 6; i++)
            {
                var it = items[i];
                if (it == null || string.IsNullOrEmpty(it.ItemClassId)) continue;
                if (!seen.Add(it.ItemClassId)) continue;
                string disp = string.IsNullOrEmpty(it.DisplayName) ? it.ItemClassId : it.DisplayName;
                int qty = vendorInventory.GetTotalQuantityOfItem(it.ItemClassId);
                sb.Append(' ').Append(disp).Append("x").Append(qty)
                  .Append('(').Append(vendorInventory.GetBuyPrice(it.ItemClassId)).Append("金)");
                shown++;
            }
            sb.Append("　按交易键购买，Shift+交易键卖出");
            ShowNotification(sb.ToString(), 8f);

            // 库存网格（对应 UE5 商店 BP 在 BeginTrade 时打开的那面货架）。
            // 压在游戏层而不是菜单层：交易键要连着按，菜单层会把游戏输入挡掉。
            if (bShowShopStockGrid)
            {
                if (_shopStockWidget == null)
                {
                    _shopStockWidget = NarrativeInventoryWidget.Open(this, vendorInventory,
                        NarrativePro.Core.NarrativeGameplayTags.UI_Layer_Game);
                }
                else
                {
                    _shopStockWidget.BindInventory(vendorInventory);
                }
            }
        }

        /// <summary>商人货架网格的开关（关掉后 <see cref="HandleTradeBegin"/> 只剩文字提示带）。</summary>
        public bool bShowShopStockGrid = true;

        private NarrativeInventoryWidget _shopStockWidget;

        /// <summary>收起商人货架：注视离开商人时对称调用。</summary>
        public void CloseShopStockGrid()
        {
            if (_shopStockWidget == null) return;
            _shopStockWidget.Close();
            _shopStockWidget = null;
        }

        private void HandleInteractPressed(NarrativePro.Interaction.NarrativeInteractionComponent component)
        {
            _bPromptHeld = true;
        }

        private void HandleInteractReleased(NarrativePro.Interaction.NarrativeInteractionComponent component)
        {
            _bPromptHeld = false;
        }

        private void RefreshInteractPromptVisibility()
        {
            if (_interactPromptRoot == null) return;

            // 注视存在 + 整盘 HUD 未隐藏（SetHUDHidden）+ UI 栈未屏蔽游戏输入（菜单打开即隐藏）
            _interactPromptRoot.Visible = _bPromptHasTarget
                                          && !bHUDHidden
                                          && !NarrativePro.CommonUI.NarrativeInputRouter.bGameInputBlocked;
        }

        /// <summary>
        /// 每帧刷新提示内容与排版（主线程，OnUpdate 驱动）。
        /// 事件位决定显隐，组件的 <see cref="NarrativePro.Interaction.PlayerInteractionComponent.ViewedInteractable"/>
        /// 与按住计时决定文案：目标名 + 「[键] 动词」，按住且目标配置了 InteractionTime 时追加进度百分比。
        /// </summary>
        private void TickInteractPrompt()
        {
            if (!_bResolvedRuntime || _interactPromptRoot == null) return;

            AttachPromptSource(NarrativePro.Interaction.PlayerInteractionComponent.LocalPlayerInteraction);

            var source = _promptSource;
            var viewed = source != null ? source.ViewedInteractable : null;
            if (viewed == null)
            {
                if (_bPromptHasTarget)
                {
                    _bPromptHasTarget = false;
                    RefreshInteractPromptVisibility();
                }
                return;
            }

            // 名称：优先交互组件的动态文案，退回宿主 Actor 名
            _bPromptHasTarget = true; // 事件可能在 HUD 未就绪/被屏蔽期间漏跳，以组件实时状态为准
            string nameText = viewed.GetInteractableNameText(source.OwningPawn, source);
            if (string.IsNullOrEmpty(nameText)) nameText = viewed.Actor != null ? viewed.Actor.Name : string.Empty;
            if (string.IsNullOrEmpty(nameText)) nameText = "???";

            // 动词 + 快捷键（键名读控制器的 InteractKey，改键后提示自动跟）
            string actionText = viewed.GetInteractableActionText(source.OwningPawn, source) ?? string.Empty;
            string keyText = OwnerPC != null
                ? NarrativePro.CommonUI.Widgets.NarrativeInputKeyFormatter.Describe(OwnerPC.InteractKey)
                : string.Empty;

            var sb = new System.Text.StringBuilder(32);
            if (!string.IsNullOrEmpty(keyText)) sb.Append('[').Append(keyText).Append("] ");
            sb.Append(actionText);

            // 缺陷4（BuildInteractPrompt 的消费方）：注视可交易 NPC 时复用同一条提示通路，
            // 把交易键与组合文案（BuildInteractPrompt = 动词+名称）追加出来，不改动既有两行布局。
            if (viewed is NarrativePro.AI.NPCInteractable npcVendor && npcVendor.bIsAlive && npcVendor.bOffersTrade)
            {
                string tradeKey = OwnerPC != null
                    ? NarrativePro.CommonUI.Widgets.NarrativeInputKeyFormatter.Describe(OwnerPC.TradeKey)
                    : string.Empty;
                string tradePrompt = NarrativePro.Interaction.InteractionFunctionLibrary.BuildInteractPrompt(npcVendor, source.OwningPawn, source);
                if (string.IsNullOrEmpty(tradePrompt)) tradePrompt = "交易";

                sb.Append("  |  ");
                if (!string.IsNullOrEmpty(tradeKey)) sb.Append('[').Append(tradeKey).Append("] ");
                sb.Append(tradePrompt);
            }

            // 按住进度：组件暴露 RemainingInteractTime（Idle 哨兵 -999 表示未在计时）
            float total = viewed.InteractionTime;
            float remaining = source.RemainingInteractTime;
            if (_bPromptHeld && total > 0f
                && remaining > NarrativePro.Interaction.PlayerInteractionComponent.IdleRemainingInteractTime + 1f)
            {
                float progress = 1f - FlaxEngine.Mathf.Clamp(remaining / total, 0f, 1f);
                int percent = (int)System.Math.Round(progress * 100f);
                sb.Append(percent).Append('%');
            }

            if (_interactPromptNameLabel.Text != nameText) _interactPromptNameLabel.Text = nameText;
            string actionLine = sb.ToString();
            if (_interactPromptActionLabel.Text != actionLine) _interactPromptActionLabel.Text = actionLine;

            if (!_interactPromptRoot.Visible) RefreshInteractPromptVisibility();
            LayoutInteractPrompt();
        }

        /// <summary>
        /// 交互提示排版：TopLeft 锚 + 显式 Location，水平居中、屏幕偏下（准星与通知带之间）。
        /// AutoWidth 标签宽度随文案变化，故每帧内容刷新后重排一次，成本可忽略。
        /// </summary>
        private void LayoutInteractPrompt()
        {
            if (_interactPromptRoot == null || !_interactPromptRoot.Visible) return;

            Float2 hostSize = HudHost != null && HudHost.RootControl != null ? HudHost.RootControl.Size : Float2.Zero;
            if (hostSize.X <= 0f || hostSize.Y <= 0f) return;

            float nameWidth = _interactPromptNameLabel != null ? _interactPromptNameLabel.Width : 0f;
            float nameHeight = _interactPromptNameLabel != null ? Math.Max(_interactPromptNameLabel.Height, 20f) : 0f;
            float actionWidth = _interactPromptActionLabel != null ? _interactPromptActionLabel.Width : 0f;
            float actionHeight = _interactPromptActionLabel != null ? Math.Max(_interactPromptActionLabel.Height, 17f) : 0f;

            if (_interactPromptNameLabel != null) _interactPromptNameLabel.Location = Float2.Zero;
            if (_interactPromptActionLabel != null) _interactPromptActionLabel.Location = new Float2(0f, nameHeight);

            float width = System.Math.Max(nameWidth, actionWidth);
            float height = nameHeight + actionHeight;
            _interactPromptRoot.Size = new Float2(width, height);
            _interactPromptRoot.Location = new Float2(
                System.Math.Max(0f, (hostSize.X - width) * 0.5f),
                System.Math.Max(0f, hostSize.Y * 0.62f - height));
        }

        // ===== 菜单热键（让 OpenMenu / Layers 真正可达） =====

        private bool _bMenuKeyWasDown;

        /// <summary>
        /// 切换默认菜单（<see cref="DefaultMenuType"/>）：栈顶已是同一菜单则关闭，否则压入 Menu 层。
        /// 对应 UE5 的菜单输入绑定 + <c>UNarrativeGameplayHUD::OpenMenu</c>。
        /// </summary>
        public NarrativeMenu ToggleDefaultMenu()
        {
            var layerTag = NarrativePro.Core.NarrativeGameplayTags.UI_Layer_Menu;
            var container = GetLayerContainer(layerTag);
            container ??= GetOrAddLayerContainer(layerTag);
            if (container == null) return null;

            var type = DefaultMenuType ?? typeof(NarrativePauseMenu);

            if (container.ActiveWidget != null && container.ActiveWidget.GetType() == type)
            {
                (container.ActiveWidget as NarrativeMenu)?.Close();
                return null;
            }

            return OpenMenu(type, layerTag);
        }

        /// <summary>关闭当前默认菜单（若栈顶不是菜单则不动别的层）。</summary>
        public void CloseDefaultMenu()
        {
            var container = GetLayerContainer(NarrativePro.Core.NarrativeGameplayTags.UI_Layer_Menu);
            (container?.ActiveWidget as NarrativeMenu)?.Close();
        }

        private void TickMenuHotkey()
        {
            if (!bOpenMenuOnEscape) return;

            bool bDown;
            try
            {
                bDown = Input.GetKey(KeyboardKeys.Escape);
            }
            catch
            {
                return;
            }

            if (bDown && !_bMenuKeyWasDown)
            {
                ToggleDefaultMenu();
            }

            _bMenuKeyWasDown = bDown;
        }
    }
}
