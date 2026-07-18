using System;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// Narrative 菜单。移植自 UE5 NarrativeArsenal: Widgets/NarrativeMenu.h（UNarrativeMenu : UNarrativeActivatableWidget）。
    ///
    /// 简化点：
    /// - Flax 无 UMG / CommonUI 系统，UNarrativeActivatableWidget 派生类改为 [Serializable] 占位类。
    /// - 源类无反射 UPROPERTY / UFUNCTION，故仅保留占位类骨架。
    /// - 渲染/激活/停用相关方法以占位形式保留，需用 Flax UIControl/UICanvas 重新实现。
    /// Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UIControl/UICanvas 重新实现菜单逻辑。
    /// </summary>
    [Serializable]
    public class NarrativeMenu
    {
        // Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: Flax UI 系统不同于 UMG/CommonUI，菜单的打开/关闭/导航逻辑需用 Flax UIControl 重新实现。
    }
}
