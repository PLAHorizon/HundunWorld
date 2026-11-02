using System;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 脚本化世界生成指令的**下行投递**通道（网关 → DS）。
/// <para>
/// 与 <see cref="IDsSessionNotifier"/> 同为"网关主动下发"，复用 DS 已建立的上行 TCP 连接。
/// 抽象成接口是为了让"令牌是否被正确投递到承载该 shard 的 DS"可在单测里断言，不必起真实 socket。
/// </para>
/// </summary>
/// <remarks>
/// <b>本通道不做授权判定</b>：调用方必须传入 Grain 裁决后签发的令牌；DS 侧验签后才执行。
/// </remarks>
public interface IDsScriptDispatcher
{
    /// <summary>
    /// 把已裁决的脚本指令投递给承载 <paramref name="shardId"/> 的 DS。
    /// </summary>
    /// <param name="shardId">目标 shard。</param>
    /// <param name="token">Grain 签发的一次性令牌。</param>
    /// <returns>
    /// 是否**确认可写入 DS 连接**（false = 无可用 DS 或写入失败；调用方应据此回错误给操作者）。
    /// <para>与 <see cref="IDsSessionNotifier.NotifySessionEnded"/> 的"尽力而为"不同，这里必须 await：
    /// 指令是操作者主动发起的，"已受理"必须建立在真实投递结果之上，否则会出现"回包成功但世界没变化"。</para>
    /// </returns>
    System.Threading.Tasks.Task<bool> DispatchAsync(long shardId, ScriptGrantToken token);
}
