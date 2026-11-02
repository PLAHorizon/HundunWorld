using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 显示 HUD 通知事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_ShowNotification.uasset 符号表）：
    /// 符号含 <c>Default__ArsenalStatics</c> 与 <c>PushHUDNotification</c>、BP 变量
    /// <c>Notification</c>——即 <c>UArsenalStatics::PushHUDNotification(WorldContext, Message, Duration)</c>
    /// （ArsenalStatics.h:170），其内部取玩家 HUD 调
    /// <c>UNarrativeGameplayHUD::ShowNotification</c>（NarrativeGameplayHUD.h:32，默认 5 秒）。
    /// </para>
    /// <para>
    /// 端口通路核对：端口 HUD 已实现堆栈式小提示
    /// （Widgets/NarrativeGameplayHUD.cs:517-518 的 ShowNotification），
    /// 统一静态入口是 NarrativeProPlugin.ShowNotification →
    /// CommonUI/NarrativeRuntimeBootstrap.cs:1332（HUD 未装配时安全空引用跳过）。
    /// </para>
    /// </summary>
    public class ShowNotificationEvent : NarrativeEvent
    {
        /// <summary>通知文本。对应 UE5 <c>Notification</c> / PushHUDNotification 的 Message。</summary>
        public string Notification { get; set; } = "";

        /// <summary>显示时长（秒）。对应 UE5 PushHUDNotification 的 Duration（默认 5）。</summary>
        public float Duration { get; set; } = 5f;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (string.IsNullOrEmpty(Notification))
            {
                NarrativeLog.LogWarning("ShowNotificationEvent: Notification 为空，跳过");
                return;
            }

            NarrativeProPlugin.ShowNotification(Notification, Duration);
        }

        public override string GetGraphDisplayText() => $"Show Notification: {Notification}";
    }
}
