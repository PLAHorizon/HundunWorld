using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// 可激活控件输入模式。对应 UE5 ENarrativeWidgetInputMode。
    /// 控制当 UI 激活时输入如何分发给游戏与菜单。
    /// </summary>
    public enum ENarrativeWidgetInputMode
    {
        /// <summary>默认：不修改输入模式。</summary>
        Default,
        /// <summary>同时接收游戏与菜单输入。</summary>
        GameAndMenu,
        /// <summary>仅接收游戏输入。</summary>
        Game,
        /// <summary>仅接收菜单输入。</summary>
        Menu
    }

    /// <summary>
    /// 输入动作绑定句柄。对应 UE5 FInputActionBindingHandle（内部包装 FUIActionBindingHandle）。
    /// </summary>
    [Serializable]
    public struct FInputActionBindingHandle : IEquatable<FInputActionBindingHandle>
    {
        /// <summary>绑定唯一标识（无效时为空字符串）。</summary>
        public string HandleId;

        /// <summary>是否为有效绑定。</summary>
        public bool IsValid() => !string.IsNullOrEmpty(HandleId);

        /// <summary>构造一个新绑定句柄。</summary>
        public FInputActionBindingHandle(string handleId)
        {
            HandleId = handleId ?? string.Empty;
        }

        public bool Equals(FInputActionBindingHandle other) => HandleId == other.HandleId;
        public override bool Equals(object obj) => obj is FInputActionBindingHandle other && Equals(other);
        public override int GetHashCode() => HandleId == null ? 0 : HandleId.GetHashCode();
    }

    /// <summary>
    /// 输入动作执行委托。对应 UE5 FInputActionExecutedDelegate（单参数动态委托）。
    /// 参数为动作名（UE5 中为 FName，Flax 中使用 string）。
    /// </summary>
    /// <param name="actionName">触发的输入动作名。</param>
    public delegate void FInputActionExecutedDelegate(string actionName);

    /// <summary>
    /// Narrative 可激活控件基类。对应 UE5 UNarrativeActivatableWidget（继承 UCommonActivatableWidget）。
    /// <para>
    /// Flax 没有 UMG/CommonUI，因此这里直接以 Flax 的 <see cref="ContainerControl"/> 为基类重建：
    /// 控件本身就是一个 Flax GUI 容器，子控件由调用方 AddChild 进来即可渲染；
    /// 激活/停用、输入配置、输入绑定、焦点这些 UE5 由 CommonUI 提供的行为在此用 Flax API 实现。
    /// </para>
    /// <para>
    /// UE5 的输入绑定走 FDataTableRowHandle + RegisterUIActionBinding（EnhancedInput 动作行）。
    /// Flax 的等价物是命名输入动作（<c>Input.GetActionState(name)</c>），
    /// 因此 RegisterBinding 的 inputAction 参数就是 Flax 的 ActionMapping 名，
    /// 由本控件在 Update 中轮询并做按下边沿检测后回调。
    /// </para>
    /// </summary>
    public class NarrativeActivatableWidget : ContainerControl
    {
        /// <summary>
        /// 如果所有者带有这些标签中的任意一个，则阻止此控件被添加到 GameplayHUD。
        /// 对应 UE5 BlockTags（FGameplayTagContainer）。
        /// </summary>
        public GameplayTagContainer BlockTags = new GameplayTagContainer();

        /// <summary>按下返回键时是否停用此控件。对应 UE5 bDeactivateOnBack。</summary>
        public bool bDeactivateOnBack = false;

        /// <summary>控件激活时是否自动聚焦到 GetDesiredFocusTarget()。对应 UE5 bFocusDesiredTargetOnActivate。</summary>
        public bool bFocusDesiredTargetOnActivate = true;

        /// <summary>
        /// 可选名称 ID，便于在 Tab 切换器中引用此菜单。对应 UE5 OptionalNameID（FName）。
        /// </summary>
        public string OptionalNameID = string.Empty;

        /// <summary>
        /// 可选显示名称，便于在 Tab 切换器中展示。对应 UE5 OptionalDisplayName（FText）。
        /// </summary>
        public string OptionalDisplayName = string.Empty;

        /// <summary>
        /// 此 UI 激活时希望的输入模式（例如是否仍要让按键事件到达游戏/玩家控制器）。
        /// 对应 UE5 InputConfig。
        /// </summary>
        public ENarrativeWidgetInputMode InputConfig = ENarrativeWidgetInputMode.Default;

        /// <summary>控件当前是否处于激活状态。对应 UE5 CommonActivatableWidget::IsActivated。</summary>
        public bool IsActivated { get; private set; }

        /// <summary>已注册输入绑定的句柄集合。对应 UE5 BindingHandles。</summary>
        protected readonly List<FInputActionBindingHandle> BindingHandles = new List<FInputActionBindingHandle>();

        /// <summary>句柄 -> 绑定记录，保存动作名、回调与操作栏显示信息。</summary>
        private readonly Dictionary<string, ActionBindingRecord> _bindings = new Dictionary<string, ActionBindingRecord>();

        /// <summary>单条输入绑定的运行时记录。</summary>
        private class ActionBindingRecord
        {
            public FInputActionBindingHandle Handle;
            public string ActionName;
            public FInputActionExecutedDelegate Callback;
            public string DisplayName;
            public bool bShowInActionBar;
            public bool bWasDown;
        }

        /// <summary>激活时触发。对应 UE5 OnActivated。</summary>
        public event Action<NarrativeActivatableWidget> OnWidgetActivated;

        /// <summary>停用时触发。对应 UE5 OnDeactivated。</summary>
        public event Action<NarrativeActivatableWidget> OnWidgetDeactivated;

        /// <summary>输入绑定集合变化（增删/改名/改显示）时触发，供操作栏刷新。</summary>
        public event Action<NarrativeActivatableWidget> OnActionBindingsChanged;

        /// <summary>构造函数。对应 UE5 UNarrativeActivatableWidget 构造函数的默认值。</summary>
        public NarrativeActivatableWidget()
        {
            bFocusDesiredTargetOnActivate = true;
            bDeactivateOnBack = false;

            // Flax 的锚点约定：非 TopLeft 锚在构造期会按父尺寸 0 重算位置，
            // 因此统一用 TopLeft，尺寸由宿主（HUD 层容器）显式重排。
            AnchorPreset = AnchorPresets.TopLeft;
            Visible = false;
        }

        // =================================================================
        // 激活 / 停用
        // =================================================================

        /// <summary>
        /// 激活此控件：显示、应用输入配置、聚焦目标并注册输入动作。
        /// 对应 UE5 UCommonActivatableWidget::Activate。
        /// </summary>
        public virtual void Activate()
        {
            if (IsActivated) return;

            IsActivated = true;
            Visible = true;
            Enabled = true;

            ApplyDesiredInputConfig();
            NativeOnActivated();
            OnWidgetActivated?.Invoke(this);
        }

        /// <summary>
        /// 停用此控件：注销输入绑定并交还输入配置。
        /// 对应 UE5 UCommonActivatableWidget::Deactivate。
        /// </summary>
        public virtual void Deactivate()
        {
            if (!IsActivated) return;

            NativeOnDeactivated();
            IsActivated = false;
            Visible = false;

            NarrativeInputRouter.ReleaseWidget(this);
            OnWidgetDeactivated?.Invoke(this);
        }

        /// <summary>控件销毁前的清理。对应 UE5 NativeDestruct。</summary>
        public virtual void NativeDestruct()
        {
            foreach (var handle in new List<FInputActionBindingHandle>(BindingHandles))
            {
                if (handle.IsValid())
                {
                    UnregisterBindingInternal(handle);
                }
            }
            BindingHandles.Clear();
            _bindings.Clear();

            if (IsActivated)
            {
                IsActivated = false;
                NarrativeInputRouter.ReleaseWidget(this);
            }
        }

        /// <summary>从父控件移除并销毁。对应 UE5 的 RemoveFromParent。</summary>
        public void Detach()
        {
            NativeDestruct();
            var parent = Parent;
            if (parent != null)
            {
                parent.RemoveChild(this);
            }
        }

        /// <summary>
        /// 返回此控件希望的输入模式。对应 UE5 GetDesiredInputConfig（UE5 该返回值还带
        /// GameMouseCaptureMode；本项目已取消鼠标锁定，故只剩模式一项）。
        /// </summary>
        /// <returns>输入模式，或 null 表示默认（不修改输入模式）。</returns>
        public virtual ENarrativeWidgetInputMode? GetDesiredInputConfig()
        {
            // BP 可覆盖（对应 UE5 的 BP_GetDesiredInputConfig）
            return InputConfig == ENarrativeWidgetInputMode.Default
                ? null
                : (ENarrativeWidgetInputMode?)InputConfig;
        }

        /// <summary>把 GetDesiredInputConfig() 的结果真正下发给输入路由器。对应 UE5 的输入配置切换。</summary>
        protected virtual void ApplyDesiredInputConfig()
        {
            NarrativeInputRouter.PushWidget(this, GetDesiredInputConfig());
        }

        /// <summary>控件被激活时调用。对应 UE5 NativeOnActivated。</summary>
        public virtual void NativeOnActivated()
        {
            if (bFocusDesiredTargetOnActivate)
            {
                var target = NativeGetDesiredFocusTarget();
                if (target != null)
                {
                    target.Focus();
                }
            }

            NativeRegisterActions();
            RegisterActions();
        }

        /// <summary>控件被停用时调用。对应 UE5 NativeOnDeactivated。</summary>
        public virtual void NativeOnDeactivated()
        {
            UnregisterAllBindings();
        }

        // =================================================================
        // 输入绑定
        // =================================================================

        /// <summary>
        /// 注册一个输入动作绑定。对应 UE5 RegisterBinding。
        /// </summary>
        /// <param name="inputAction">Flax 输入动作名（对应 UE5 FDataTableRowHandle 的行名）。</param>
        /// <param name="callback">触发时的回调。</param>
        /// <param name="bindingHandle">输出的绑定句柄。</param>
        /// <param name="overrideDisplayName">在操作栏中显示的覆盖名称。</param>
        /// <param name="bShouldDisplayInActionBar">是否在操作栏中显示。</param>
        public virtual void RegisterBinding(
            string inputAction,
            FInputActionExecutedDelegate callback,
            out FInputActionBindingHandle bindingHandle,
            string overrideDisplayName = "",
            bool bShouldDisplayInActionBar = true)
        {
            var newHandle = new FInputActionBindingHandle(Guid.NewGuid().ToString("N"));

            _bindings[newHandle.HandleId] = new ActionBindingRecord
            {
                Handle = newHandle,
                ActionName = inputAction ?? string.Empty,
                Callback = callback,
                DisplayName = string.IsNullOrEmpty(overrideDisplayName) ? DefaultDisplayNameForAction(inputAction) : overrideDisplayName,
                bShowInActionBar = bShouldDisplayInActionBar,
                bWasDown = false
            };

            BindingHandles.Add(newHandle);
            bindingHandle = newHandle;

            OnActionBindingsChanged?.Invoke(this);
        }

        /// <summary>注销指定绑定。对应 UE5 UnregisterBinding。</summary>
        public virtual void UnregisterBinding(FInputActionBindingHandle bindingHandle)
        {
            if (!bindingHandle.IsValid()) return;

            RemoveActionBindingInternal(bindingHandle);
            BindingHandles.Remove(bindingHandle);
            UnregisterBindingInternal(bindingHandle);

            OnActionBindingsChanged?.Invoke(this);
        }

        /// <summary>注销所有绑定。对应 UE5 UnregisterAllBindings。</summary>
        public virtual void UnregisterAllBindings()
        {
            foreach (var handle in new List<FInputActionBindingHandle>(BindingHandles))
            {
                RemoveActionBindingInternal(handle);
                if (handle.IsValid())
                {
                    UnregisterBindingInternal(handle);
                }
            }
            BindingHandles.Clear();
            _bindings.Clear();

            OnActionBindingsChanged?.Invoke(this);
        }

        /// <summary>设置绑定的显示名称。对应 UE5 SetBindingDisplayName。</summary>
        public virtual void SetBindingDisplayName(FInputActionBindingHandle bindingHandle, string newDisplayName)
        {
            if (!bindingHandle.IsValid()) return;
            if (!_bindings.TryGetValue(bindingHandle.HandleId, out var record)) return;

            record.DisplayName = newDisplayName ?? string.Empty;
            OnActionBindingsChanged?.Invoke(this);
        }

        /// <summary>设置绑定是否显示在操作栏。对应 UE5 SetBindingShowOnActionBar。</summary>
        public virtual void SetBindingShowOnActionBar(FInputActionBindingHandle bindingHandle, bool bShowOnActionBar)
        {
            if (!bindingHandle.IsValid()) return;
            if (!_bindings.TryGetValue(bindingHandle.HandleId, out var record)) return;

            record.bShowInActionBar = bShowOnActionBar;
            OnActionBindingsChanged?.Invoke(this);
        }

        /// <summary>当前应显示在操作栏上的绑定（动作名 / 显示名）。对应 UE5 CommonActivatableWidget 暴露给 action bar 的数据。</summary>
        public List<ActionBarEntry> GetActionBarEntries()
        {
            var result = new List<ActionBarEntry>();
            foreach (var handle in BindingHandles)
            {
                if (!handle.IsValid()) continue;
                if (!_bindings.TryGetValue(handle.HandleId, out var record)) continue;
                if (!record.bShowInActionBar) continue;

                result.Add(new ActionBarEntry
                {
                    ActionName = record.ActionName,
                    DisplayName = record.DisplayName,
                    Handle = record.Handle
                });
            }
            return result;
        }

        /// <summary>操作栏条目。对应 UE5 FUIActionBindingHandle 的显示数据。</summary>
        public struct ActionBarEntry
        {
            /// <summary>Flax 输入动作名。</summary>
            public string ActionName;
            /// <summary>显示名。</summary>
            public string DisplayName;
            /// <summary>绑定句柄。</summary>
            public FInputActionBindingHandle Handle;
        }

        // =================================================================
        // 蓝图可实现事件的 Flax 等价物
        // =================================================================

        /// <summary>
        /// 由子类覆盖实现的动作注册。对应 UE5 RegisterActions（BlueprintImplementableEvent）。
        /// </summary>
        public virtual void RegisterActions()
        {
        }

        /// <summary>原生注册动作。对应 UE5 NativeRegisterActions。</summary>
        protected virtual void NativeRegisterActions()
        {
        }

        /// <summary>
        /// 获取此控件激活时希望聚焦的目标。对应 UE5 NativeGetDesiredFocusTarget。
        /// 默认找第一个可聚焦的子控件；子类可覆盖返回具体控件。
        /// </summary>
        protected virtual Control NativeGetDesiredFocusTarget()
        {
            for (int i = 0; i < ChildrenCount; i++)
            {
                var child = GetChild(i);
                if (child == null || !child.Visible || !child.Enabled) continue;
                // Flax GUI 没有 UMG 的 ButtonBase/ComboBox：可聚焦控件对应为
                // Button（含 CheckBox/Dropdown 等派生）、TextBoxBase（TextBox/RichTextBox）、Slider。
                if (child is Button || child is TextBoxBase || child is Slider || child is Dropdown)
                {
                    return child;
                }
            }
            return null;
        }

        // =================================================================
        // 生命周期
        // =================================================================

        /// <summary>每帧轮询已注册的输入动作并在按下边沿回调。对应 UE5 EnhancedInput 的触发分发。</summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!IsActivated || _bindings.Count == 0) return;

            foreach (var record in _bindings.Values)
            {
                if (string.IsNullOrEmpty(record.ActionName)) continue;

                bool bDown = Input.GetAction(record.ActionName);
                if (bDown && !record.bWasDown)
                {
                    record.Callback?.Invoke(record.ActionName);
                }
                record.bWasDown = bDown;
            }
        }

        /// <summary>返回键（Escape / Backspace）处理。对应 UE5 的 bDeactivateOnBack。</summary>
        public override bool OnKeyDown(KeyboardKeys key)
        {
            if (bDeactivateOnBack && IsActivated &&
                (key == KeyboardKeys.Escape || key == KeyboardKeys.Backspace))
            {
                Deactivate();
                return true;
            }

            return base.OnKeyDown(key);
        }

        // =================================================================
        // 内部辅助
        // =================================================================

        /// <summary>移除指定绑定的内部辅助方法。对应 UE5 RemoveActionBinding。</summary>
        private void RemoveActionBindingInternal(FInputActionBindingHandle handle)
        {
            if (handle.IsValid())
            {
                _bindings.Remove(handle.HandleId);
            }
        }

        /// <summary>注销指定绑定的内部辅助方法。对应 UE5 FUIActionBindingHandle::Unregister。</summary>
        private void UnregisterBindingInternal(FInputActionBindingHandle handle)
        {
            if (handle.IsValid())
            {
                _bindings.Remove(handle.HandleId);
            }
        }

        /// <summary>从动作名推一个可读默认显示名（去掉常见前缀/后缀）。</summary>
        protected static string DefaultDisplayNameForAction(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return string.Empty;

            string name = actionName;
            int lastSlash = name.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < name.Length - 1) name = name.Substring(lastSlash + 1);
            if (name.StartsWith("IA_", StringComparison.Ordinal)) name = name.Substring(3);
            if (name.StartsWith("Action_", StringComparison.Ordinal)) name = name.Substring(7);
            return name;
        }
    }
}
