using System;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 通用动作控件。移植自 UE5 NarrativeArsenal: Widgets/NarrativeCommonActionWidget.h（UNarrativeCommonActionWidget : UCommonActionWidget）。
    ///
    /// 简化点：
    /// - Flax 无 UMG / CommonUI 系统，UCommonActionWidget 派生类改为 [Serializable] 占位类。
    /// - 源类无反射 UPROPERTY / UFUNCTION，故仅保留占位类骨架。
    /// - 渲染/绘制相关方法以占位形式保留，需用 Flax UIControl/UICanvas 重新实现。
    /// Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UIControl/UICanvas 重新实现通用动作控件逻辑。
    /// </summary>
    [Serializable]
    public class NarrativeCommonActionWidget
    {
        // Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: Flax UI 系统不同于 UMG/CommonUI，需用 Flax UIControl 重新实现。
    }
}
