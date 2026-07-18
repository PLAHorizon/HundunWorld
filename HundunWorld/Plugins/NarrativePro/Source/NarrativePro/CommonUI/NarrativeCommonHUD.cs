using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// Narrative 通用 HUD 占位。对应 UE5 UNarrativeCommonHUD（继承 UCommonUserWidget）。
    ///
    /// 移植简化点：
    /// 1. Flax 无 AHUD / UCommonUserWidget 基类。这里以 Script 派生类占位，
    ///    可挂载到 Actor 上作为 HUD 容器，UI 渲染需用 Flax UICanvas / UIControl 重新实现。
    /// 2. UE5 中 NativeConstruct 用于初始化子控件，Flax 中改为 OnEnable。
    /// 3. UE5 中此 HUD 实例由 NarrativeCommonUISubsystem 缓存（CommonHUD 字段）。
    /// </summary>
    public class NarrativeCommonHUD : Script
    {
        /// <summary>
        /// 控件构造时的初始化。对应 UE5 NativeConstruct。
        /// UE5 中调用 Super::NativeConstruct() 后做一些子控件初始化；
        /// Flax 中改为 OnEnable，UI 渲染部分需用 Flax UIControl 重新实现。
        /// </summary>
        public override void OnEnable()
        {
            base.OnEnable();

            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，子控件初始化需用 Flax UIControl/UICanvas 重新实现。
            // 当前仅注册到子系统缓存。
            if (NarrativeCommonUISubsystem.Instance != null)
            {
                NarrativeCommonUISubsystem.Instance.CommonHUD = this;
            }
            else
            {
                NarrativeLog.LogWarning("NarrativeCommonHUD 启用时 NarrativeCommonUISubsystem 尚未初始化，无法注册 CommonHUD。");
            }
        }

        /// <summary>控件销毁时清理。对应 UE5 NativeDestruct。</summary>
        public override void OnDisable()
        {
            // 解除子系统缓存引用
            if (NarrativeCommonUISubsystem.Instance != null &&
                NarrativeCommonUISubsystem.Instance.CommonHUD == this)
            {
                NarrativeCommonUISubsystem.Instance.CommonHUD = null;
            }

            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，子控件销毁需用 Flax UIControl 重新实现。
            base.OnDisable();
        }
    }
}
