using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// 字幕处理委托。对应 UE5 FNarrativeHandleSubtitle（动态多播委托，单参数 FText）。
    /// 参数为字幕文本（UE5 中为 FText，Flax 中使用 string）。
    /// </summary>
    /// <param name="subtitleText">字幕文本。</param>
    public delegate void FNarrativeHandleSubtitle(string subtitleText);

    /// <summary>
    /// Narrative CommonUI 子系统。对应 UE5 UNarrativeCommonUISubsystem（继承 UGameInstanceSubsystem）。
    /// 当前主要用于高效访问 Narrative HUD，未来可能扩展更多功能。
    ///
    /// 移植简化点：
    /// 1. UE5 中为 GameInstanceSubsystem，Flax 无对应物，使用单例 Script 模式（参考 NarrativeMusicSubsystem）。
    /// 2. UE5 中 Initialize 监听 FSubtitleManager::OnSetSubtitleText，Flax 无 SubtitleManager，
    ///    这里保留 OnSetSubtitle 方法签名和 OnHandleSubtitle 事件，由外部桥接调用。
    /// 3. CommonHUD 字段保留为 NarrativeCommonHUD 类型引用（占位）。
    /// </summary>
    public class NarrativeCommonUISubsystem : Script
    {
        private static NarrativeCommonUISubsystem _instance;

        /// <summary>
        /// 当前实例。Flax 中以单例模式等价 GameInstanceSubsystem 的全局访问。
        /// </summary>
        public static NarrativeCommonUISubsystem Instance => _instance;

        /// <summary>
        /// 缓存的通用 HUD 实例。对应 UE5 CommonHUD（UPROPERTY BlueprintReadOnly）。
        /// </summary>
        public NarrativeCommonHUD CommonHUD;

        /// <summary>
        /// 字幕处理事件。对应 UE5 OnHandleSubtitle（BlueprintAssignable）。
        /// 允许蓝图/外部处理 UE5 字幕。
        /// </summary>
        public event FNarrativeHandleSubtitle OnHandleSubtitle;

        /// <summary>子系统初始化。对应 UE5 Initialize。</summary>
        public override void OnEnable()
        {
            base.OnEnable();
            _instance = this;

            // Flax-不兼容: UE5 的 SubtitleManager 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 SubtitleManager，需要外部桥接调用 OnSetSubtitle 来分发字幕。
            // UE5 中：FSubtitleManager::GetSubtitleManager()->OnSetSubtitleText().AddUObject(this, &UNarrativeCommonUISubsystem::OnSetSubtitle);
            NarrativeLog.Log("NarrativeCommonUISubsystem 已初始化。");
        }

        /// <summary>子系统反初始化。对应 UE5 Deinitialize。</summary>
        public override void OnDisable()
        {
            // Flax-不兼容: UE5 的 SubtitleManager 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 SubtitleManager，无需解绑监听。
            CommonHUD = null;
            OnHandleSubtitle = null;

            if (_instance == this) _instance = null;
            base.OnDisable();
        }

        /// <summary>
        /// 字幕设置回调。对应 UE5 OnSetSubtitle。
        /// UE5 中由 SubtitleManager 调用；Flax 中需由外部桥接器调用。
        /// </summary>
        /// <param name="subtitleText">字幕文本。</param>
        public void OnSetSubtitle(string subtitleText)
        {
            OnHandleSubtitle?.Invoke(subtitleText);
        }

        // TODO [待源码]: 获取 UE5 源 UNarrativeCommonUISubsystem.cpp 后补全 OpenMenu 实现。当 NarrativeMenu 原生化后，可能添加 OpenMenu 方法（对应 UE5 注释掉的 OpenMenu）。
        // public void OpenMenu(string notificationText, float duration = 5f) { }
    }
}
