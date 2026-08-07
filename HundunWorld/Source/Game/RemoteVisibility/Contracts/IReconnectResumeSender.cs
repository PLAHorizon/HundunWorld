namespace HundunWorld.Game.RemoteVisibility.Contracts;

/// <summary>
/// 重连恢复协商发送结果：以真实送达为唯一成功依据（spec 6.4 规则 1）。
/// </summary>
public readonly record struct ResumeSendResult(bool Delivered, ResumeFailReason? Reason = null);

/// <summary>
/// 重连恢复协商发送失败原因（spec 6.3 可追溯性）。
/// </summary>
public enum ResumeFailReason
{
    /// <summary>连接未就绪（客户端未连接 / 连接状态非 Connected）。</summary>
    ConnectionNotReady,

    /// <summary>发送通道异常（编码/发送抛出异常）。</summary>
    ChannelError,

    /// <summary>被上行兜底守卫拦截（修复连接层豁免后不应出现）。</summary>
    GuardRejected,

    /// <summary>有限次重试全部失败。回退全量握手。</summary>
    RetryExhausted,

    /// <summary>观测基线重建失败（快照应用/解码异常等）。</summary>
    BaselineRebuildFailed,
}

/// <summary>
/// 重连恢复协商发送器：以 NetworkManager.SendAsync 返回值为真实送达依据，
/// 成功日志仅在真实送达后输出（spec 5.6.1 规则 1/6 禁止假成功）。
/// </summary>
public interface IReconnectResumeSender
{
    /// <summary>以真实送达为依据发送重连恢复协商消息；成功才返回 Delivered=true（杜绝假成功日志）。</summary>
    System.Threading.Tasks.Task<ResumeSendResult> SendResumeAsync(ulong characterId, long lastAppliedServerTick);

    /// <summary>当前是否处于可发送状态（连接就绪 + 非重连窗口期空闲）。</summary>
    bool CanSendNow { get; }
}