using System;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 准星控件。移植自 UE5 NarrativeArsenal: Widgets/CrosshairWidget.h（UCrosshairWidget : UUserWidget，抽象）。
    ///
    /// 简化点：
    /// - Flax 无 UMG 系统，UUserWidget 派生类改为 [Serializable] 占位类。
    /// - 源类无反射 UPROPERTY / UFUNCTION，故仅保留占位类骨架。
    /// - 渲染/绘制相关方法以占位形式保留，需用 Flax UIControl/UICanvas 重新实现。
    /// Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UIControl/UICanvas 重新实现准星渲染逻辑。
    /// </summary>
    [Serializable]
    public abstract class CrosshairWidget
    {
        // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax UI 系统不同于 UMG，准星的绘制/更新逻辑需用 Flax UIControl 重新实现。
    }
}
