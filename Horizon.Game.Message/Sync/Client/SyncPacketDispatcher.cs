using System;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Core.Sim.Client;

/// <summary>
/// 客户端 SyncPacket v2 分派器（P5-a）。<br/>
/// 把未知/已知的 <see cref="SyncPacket"/> 路由到 <see cref="SyncPacketInbox"/> 的各个队列，
/// 不做任何业务判断（版本对齐、drop、合并等留给 ECS 侧）。
/// </summary>
/// <remarks>
/// 设计要点：
/// <list type="bullet">
///   <item>纯 C# 类，可在单测里直接 new；不依赖 Unreal。</item>
///   <item>"未知 SyncPacketKind" 会被丢入 <see cref="UnknownPacketCount"/>，便于灰度观察对端版本差异。</item>
///   <item>Dispatcher 不拥有任何解码；上游保证传进来的 <see cref="SyncPacket"/> 是解码后的对象。</item>
/// </list>
/// </remarks>
public sealed class SyncPacketDispatcher
{
    private readonly SyncPacketInbox _inbox;

    /// <summary>收到的 HandshakePacket 计数（诊断用）。</summary>
    public long HandshakeCount { get; private set; }

    /// <summary>收到的 SnapshotPacket 计数（诊断用）。</summary>
    public long SnapshotCount { get; private set; }

    /// <summary>收到的 EventPacket 计数（诊断用）。</summary>
    public long EventCount { get; private set; }

    /// <summary>分派时未识别的 Kind 总数。</summary>
    public long UnknownPacketCount { get; private set; }

    public SyncPacketDispatcher(SyncPacketInbox inbox)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
    }

    /// <summary>
    /// 分派一个 <see cref="SyncPacket"/>；失败会抛 <see cref="ArgumentNullException"/>。
    /// </summary>
    public void Dispatch(SyncPacket packet)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));
        switch (packet)
        {
            case HandshakePacket:
                HandshakeCount++;
                break;
            case SnapshotPacket:
                SnapshotCount++;
                break;
            case EventPacket:
                EventCount++;
                break;
            case WorldChunkDiffPacket diff:
                _inbox.ChunkDiffs.Enqueue(diff);
                break;
            case WorldPatchManifestPacket manifest:
                _inbox.PatchManifests.Enqueue(manifest);
                break;
            case InputAckPacket ack:
                _inbox.UpdateLatestAck(ack);
                break;
            case ReconnectResumePacket:
                // 客户端不该收到 ReconnectResume（它是客户端 → 服务器的）。
                UnknownPacketCount++;
                break;
            default:
                UnknownPacketCount++;
                break;
        }
    }
}
