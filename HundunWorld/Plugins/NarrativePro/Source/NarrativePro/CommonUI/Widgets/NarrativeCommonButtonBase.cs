using System;
using FlaxEngine;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// Narrative CommonUI 按钮基类占位。对应 UE5 UNarrativeCommonButtonBase（继承 UCommonButtonBase，Abstract）。
    /// 在基础 CommonButton 之上提供少量额外功能。
    ///
    /// 移植简化点：
    /// 1. Flax 完全没有 UMG / CommonUI 控件系统。UCommonButtonBase 是 UE5 CommonUI 按钮基类，
    ///    这里以 [Serializable] plain class 占位，保留 UE5 的类名、字段定义、方法签名。
    /// 2. UE5 中 SetSelectedInternal 是 BP Protected 的，ForceSetIsSelected 仅作为包装；
    ///    Flax 中改为直接修改 bIsSelected 字段。
    /// 3. UE5 中 ButtonTextBlock 通过 BindWidgetOptional 与子控件绑定，Flax 中保留为引用字段。
    /// 4. UI 渲染部分需用 Flax UIControl（如 Button）重新实现。
    /// </summary>
    [Serializable]
    public class NarrativeCommonButtonBase
    {
        /// <summary>可选的按钮文本块。对应 UE5 ButtonTextBlock（BindWidgetOptional）。</summary>
        public NarrativeCommonTextBlock ButtonTextBlock;

        /// <summary>按钮上显示的文本。对应 UE5 ButtonText（FText）。</summary>
        public string ButtonText = "Button Text";

        /// <summary>文本对齐方式。对应 UE5 TextJustification（ETextJustify::Type）。</summary>
        public ETextJustify TextJustification = ETextJustify.Left;

        /// <summary>当前是否被选中。对应 UE5 Internal::IsSelected。</summary>
        public bool bIsSelected = false;

        /// <summary>构造函数。对应 UE5 UNarrativeCommonButtonBase 构造函数的默认值。</summary>
        public NarrativeCommonButtonBase()
        {
            TextJustification = ETextJustify.Left;
            ButtonText = "Button Text";
        }

        /// <summary>
        /// 强制设置选中状态。对应 UE5 ForceSetIsSelected。
        /// UE5 中 SetSelectedInternal 是 BP Protected 的，需要包装一下才能在蓝图中调用。
        /// </summary>
        /// <param name="bInSelected">是否选中。</param>
        /// <param name="bAllowSound">是否允许播放声音。</param>
        /// <param name="bBroadcast">是否广播事件。</param>
        public virtual void ForceSetIsSelected(bool bInSelected, bool bAllowSound, bool bBroadcast)
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax Button 控件重新实现选中状态、声音、事件广播。
            SetSelectedInternal(bInSelected, bAllowSound, bBroadcast);
        }

        /// <summary>
        /// 设置按钮文本。对应 UE5 SetButtonText。
        /// </summary>
        public virtual void SetButtonText(string inText)
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax Button + Label 重新实现。
            ButtonText = inText ?? string.Empty;

            if (ButtonTextBlock != null)
            {
                ButtonTextBlock.SetText(ButtonText);
            }
        }

        /// <summary>
        /// 预构造回调。对应 UE5 NativePreConstruct。
        /// UE5 中调用 Super::NativePreConstruct 后设置按钮文本与文本对齐。
        /// </summary>
        public virtual void NativePreConstruct()
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax Button 控件预构造逻辑重新实现。
            SetButtonText(ButtonText);

            if (ButtonTextBlock != null)
            {
                ButtonTextBlock.SetJustification(TextJustification);
            }
        }

        /// <summary>
        /// 当前文本样式变更回调。对应 UE5 NativeOnCurrentTextStyleChanged。
        /// UE5 中调用 Super::NativeOnCurrentTextStyleChanged 后将样式应用到 ButtonTextBlock。
        /// </summary>
        public virtual void NativeOnCurrentTextStyleChanged()
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax 文本样式系统重新实现。
            if (ButtonTextBlock != null)
            {
                ButtonTextBlock.SetStyle(GetCurrentTextStyleClass());
            }
        }

        /// <summary>
        /// 获取当前文本样式。对应 UE5 GetCurrentTextStyleClass。
        /// Flax 无 CommonUI 样式系统，这里返回空字符串占位。
        /// </summary>
        protected virtual string GetCurrentTextStyleClass()
        {
            // Flax-不兼容: UE5 的 CommonUI 样式系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 CommonUI 样式系统，需用 Flax UI 样式重新实现。
            return string.Empty;
        }

        /// <summary>
        /// 内部选中状态设置。对应 UE5 SetSelectedInternal。
        /// </summary>
        protected virtual void SetSelectedInternal(bool bInSelected, bool bAllowSound, bool bBroadcast)
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax Button 控件重新实现声音、事件广播。
            bIsSelected = bInSelected;
            _ = bAllowSound;
            _ = bBroadcast;
        }
    }
}
