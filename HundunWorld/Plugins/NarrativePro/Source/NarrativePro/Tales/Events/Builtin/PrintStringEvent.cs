using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 打印调试字符串事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_PrintString.uasset 符号表）：
    /// CallFunc 含 <c>Default__KismetSystemLibrary</c> + 符号 <c>PrintString</c>，
    /// 即蓝图节点 Print String（KismetSystemLibrary.h：bPrintToScreen 画到屏幕、
    /// bPrintToLog 进日志，仅开发环境生效）。
    /// </para>
    /// <para>
    /// 端口差异：Flax 的 <c>Debug.Log</c> 在无头/专用服务器下会抛异常
    /// （插件统一约束，见 Core/NarrativeLog.cs），因此本事件的诚实替身就是端口日志器
    /// <c>NarrativeLog</c>，不做屏幕叠字——需要屏幕提示请改用
    /// <see cref="ShowNotificationEvent"/>。
    /// </para>
    /// </summary>
    public class PrintStringEvent : NarrativeEvent
    {
        /// <summary>要打印的文本。对应 UE5 PrintString 节点的 InString。</summary>
        public string Text { get; set; } = "";

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (string.IsNullOrEmpty(Text))
            {
                NarrativeLog.LogWarning("PrintStringEvent: Text 为空，跳过");
                return;
            }

            NarrativeLog.Log($"[PrintString] {Text}");
        }

        public override string GetGraphDisplayText() => $"Print String: {Text}";
    }
}
