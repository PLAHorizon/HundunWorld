using System;
using System.Collections.Generic;
using FlaxEngine;
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
    /// 输入动作绑定句柄。对应 UE5 FInputActionBindingHandle 结构。
    /// UE5 中包装 FUIActionBindingHandle，Flax 无 CommonUI 输入绑定系统，
    /// 这里以字符串 ID 占位标识绑定关系。
    /// </summary>
    [Serializable]
    public struct FInputActionBindingHandle
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
    }

    /// <summary>
    /// 输入动作执行委托。对应 UE5 FInputActionExecutedDelegate（单参数动态委托）。
    /// 参数为动作名（UE5 中为 FName，Flax 中使用 string）。
    /// </summary>
    /// <param name="actionName">触发的输入动作名。</param>
    public delegate void FInputActionExecutedDelegate(string actionName);

    /// <summary>
    /// Narrative 可激活控件基类。对应 UE5 UNarrativeActivatableWidget（继承 UCommonActivatableWidget）。
    ///
    /// 移植简化点：
    /// 1. Flax 完全没有 UMG / CommonUI 系统。这里以 [Serializable] plain class 占位，
    ///    保留 UE5 的类名、字段定义、方法签名，方法实现体以占位形式标注。
    /// 2. UE5 中输入绑定基于 FUIActionBindingHandle / RegisterUIActionBinding，
    ///    Flax 无对应物，绑定集合改为本地 List，调用方需要自行桥接到 Flainput 输入系统。
    /// 3. UE5 中 FGameplayTagContainer → NarrativePro.Items.GameplayTagContainer。
    /// 4. UE5 中 FText/FName → string。FDataTableRowHandle → string（占位路径）。
    /// 5. UI 渲染部分需用 Flax UIControl / UICanvas 重新实现。
    /// </summary>
    [Serializable]
    public class NarrativeActivatableWidget
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

        /// <summary>
        /// 游戏接收输入时鼠标的捕获行为。对应 UE5 GameMouseCaptureMode（EMouseCaptureMode）。
        /// Flax 无 EMouseCaptureMode 枚举，这里以字符串占位（如 "CapturePermanently"）。
        /// </summary>
        public string GameMouseCaptureMode = "CapturePermanently";

        /// <summary>
        /// 当前已注册的输入绑定句柄集合。对应 UE5 BindingHandles（TArray&lt;FUIActionBindingHandle&gt;）。
        /// </summary>
        protected List<FInputActionBindingHandle> BindingHandles = new List<FInputActionBindingHandle>();

        /// <summary>构造函数。对应 UE5 UNarrativeActivatableWidget 构造函数的默认值。</summary>
        public NarrativeActivatableWidget()
        {
            bFocusDesiredTargetOnActivate = true;
            bDeactivateOnBack = false;
        }

        /// <summary>
        /// 控件销毁前的清理。对应 UE5 NativeDestruct。
        /// 在 UE5 中会注销所有输入绑定；Flax 中同样清理本地绑定列表。
        /// </summary>
        public virtual void NativeDestruct()
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax UIControl 生命周期重新实现销毁逻辑。
            foreach (var handle in BindingHandles)
            {
                if (handle.IsValid())
                {
                    UnregisterBindingInternal(handle);
                }
            }
            BindingHandles.Clear();
        }

        /// <summary>
        /// 返回此控件希望的输入配置。对应 UE5 GetDesiredInputConfig。
        /// 返回 null 表示使用默认（不修改输入模式）。
        /// </summary>
        /// <returns>输入配置元组（输入模式, 鼠标捕获模式），或 null 表示默认。</returns>
        public virtual Tuple<ENarrativeWidgetInputMode, string> GetDesiredInputConfig()
        {
            // Flax-不兼容: UE5 的 CommonUI 输入系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonUI 输入系统，需用 Flax 输入系统重新实现。
            // BP 可通过子类覆盖此方法自定义。
            switch (InputConfig)
            {
                case ENarrativeWidgetInputMode.GameAndMenu:
                    return Tuple.Create(ENarrativeWidgetInputMode.GameAndMenu, GameMouseCaptureMode);
                case ENarrativeWidgetInputMode.Game:
                    return Tuple.Create(ENarrativeWidgetInputMode.Game, GameMouseCaptureMode);
                case ENarrativeWidgetInputMode.Menu:
                    return Tuple.Create(ENarrativeWidgetInputMode.Menu, "NoCapture");
                case ENarrativeWidgetInputMode.Default:
                default:
                    return null;
            }
        }

        /// <summary>
        /// 注册一个输入动作绑定。对应 UE5 RegisterBinding。
        /// </summary>
        /// <param name="inputAction">输入动作的数据表行句柄（Flax 中以字符串路径占位）。</param>
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
            // Flax-不兼容: UE5 的 CommonUI 输入绑定系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonUI 输入绑定系统，需桥接到 Flax 输入事件。
            var newHandle = new FInputActionBindingHandle(Guid.NewGuid().ToString("N"));
            BindingHandles.Add(newHandle);
            bindingHandle = newHandle;

            // 记录绑定信息以便后续桥接实现使用
            _ = inputAction;
            _ = callback;
            _ = overrideDisplayName;
            _ = bShouldDisplayInActionBar;
        }

        /// <summary>
        /// 注销指定绑定。对应 UE5 UnregisterBinding。
        /// </summary>
        public virtual void UnregisterBinding(FInputActionBindingHandle bindingHandle)
        {
            // Flax-不兼容: UE5 的 CommonUI 输入绑定系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonUI 输入绑定系统，需桥接到 Flax 输入事件。
            RemoveActionBindingInternal(bindingHandle);

            if (bindingHandle.IsValid())
            {
                BindingHandles.Remove(bindingHandle);
                UnregisterBindingInternal(bindingHandle);
            }
        }

        /// <summary>
        /// 注销所有绑定。对应 UE5 UnregisterAllBindings。
        /// </summary>
        public virtual void UnregisterAllBindings()
        {
            // Flax-不兼容: UE5 的 CommonUI 输入绑定系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonUI 输入绑定系统，需桥接到 Flax 输入事件。
            foreach (var handle in BindingHandles)
            {
                RemoveActionBindingInternal(handle);
                if (handle.IsValid())
                {
                    UnregisterBindingInternal(handle);
                }
            }
            BindingHandles.Clear();
        }

        /// <summary>
        /// 设置绑定的显示名称。对应 UE5 SetBindingDisplayName。
        /// </summary>
        public virtual void SetBindingDisplayName(FInputActionBindingHandle bindingHandle, string newDisplayName)
        {
            // Flax-不兼容: UE5 的 CommonUI 输入绑定系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonUI 输入绑定系统，需桥接到 Flax UI 操作栏。
            if (bindingHandle.IsValid())
            {
                _ = newDisplayName;
            }
        }

        /// <summary>控件被激活时调用。对应 UE5 NativeOnActivated。</summary>
        public virtual void NativeOnActivated()
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，激活流程需用 Flax UIControl 显示/聚焦逻辑重新实现。
            if (bFocusDesiredTargetOnActivate)
            {
                var widget = NativeGetDesiredFocusTarget();
                if (widget != null)
                {
                    // Flax-不兼容: UE5 的 UMG SetFocus 在 Flax 无对应物，保留占位。原文 TODO: 调用 Flax UIControl 的 SetFocus 等价方法。
                    _ = widget;
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

        /// <summary>
        /// 蓝图可实现的事件，用于注册动作。对应 UE5 RegisterActions（BlueprintImplementableEvent）。
        /// Flax 中由子类覆盖实现。
        /// </summary>
        public virtual void RegisterActions()
        {
            // 由子类覆盖。对应 UE5 BlueprintImplementableEvent。
        }

        /// <summary>原生注册动作。对应 UE5 NativeRegisterActions。</summary>
        protected virtual void NativeRegisterActions()
        {
            // 默认空实现，由子类覆盖。
        }

        /// <summary>
        /// 获取此控件激活时希望聚焦的目标。对应 UE5 NativeGetDesiredFocusTarget。
        /// Flax 无 UWidget 基类，这里返回 null 占位，子类可覆盖返回 Flax UIControl。
        /// </summary>
        protected virtual object NativeGetDesiredFocusTarget()
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需返回 Flax UIControl 等价物。
            return null;
        }

        /// <summary>移除指定绑定的内部辅助方法。对应 UE5 RemoveActionBinding。</summary>
        private void RemoveActionBindingInternal(FInputActionBindingHandle handle)
        {
            // Flax-不兼容: UE5 的 CommonUI 输入绑定系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonUI 输入绑定系统，需桥接到 Flax 输入事件。
            _ = handle;
        }

        /// <summary>注销指定绑定的内部辅助方法。对应 UE5 FUIActionBindingHandle::Unregister。</summary>
        private void UnregisterBindingInternal(FInputActionBindingHandle handle)
        {
            // Flax-不兼容: UE5 的 CommonUI 输入绑定系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonUI 输入绑定系统，需桥接到 Flax 输入事件。
            _ = handle;
        }
    }
}
