using System;
using System.Buffers;
using Horizon.Game.Core.Utilities;
using Horizon.Game.Message.Sync;
using MemoryPack;

namespace Horizon.Game.Core.Sim.Client;

/// <summary>
/// 客户端 SyncPacket v2 分派器（P5-a）。<br/>
/// 把未知/已知的 <see cref="SyncPacket"/> 路由到 <see cref="SyncPacketInbox"/> 的各个队列，
/// 不做任何业务判断（版本对齐、drop、合并等留给 ECS 侧）。
/// </summary>
/// <remarks>
/// Dispatcher 只负责 v2 新增包的 inbox 路由，旧包（Handshake/Snapshot/Event）由上层 MessageHandler 处理。
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

    /// <summary>收到的 InteractionSyncPacket 计数（诊断用）。</summary>
    public long InteractionSyncCount { get; private set; }

    /// <summary>收到的 SceneObjectSyncPacket 计数（诊断用）。</summary>
    public long SceneObjectSyncCount { get; private set; }

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
        try
        {
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
                    RouteWorldChunkDiff(diff);
                    break;
                case WorldPatchManifestPacket manifest:
                    _inbox.PatchManifests.Enqueue(manifest);
                    break;
                case InputAckPacket ack:
                    _inbox.UpdateLatestAck(ack);
                    break;
                case InteractionSyncPacket interaction:
                    _inbox.InteractionEvents.Enqueue(interaction);
                    InteractionSyncCount++;
                    break;
                case SceneObjectSyncPacket sceneObject:
                    _inbox.SceneObjectEvents.Enqueue(sceneObject);
                    SceneObjectSyncCount++;
                    break;
                case ReconnectResumePacket:
                    // 客户端不该收到 ReconnectResume（它是客户端 → 服务器的）。
                    System.Diagnostics.Debug.WriteLine("[SyncPacketDispatcher] 警告：客户端收到 ReconnectResumePacket（应为客户端→服务器方向），已忽略。");
                    UnknownPacketCount++;
                    break;
                default:
                    System.Diagnostics.Debug.WriteLine($"[SyncPacketDispatcher] 警告：收到未知包类型 Kind={packet.Kind}，已忽略。");
                    UnknownPacketCount++;
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncPacketDispatcher] 分派异常 Kind={packet.Kind}, Exception={ex}");
        }
    }

    /// <summary>
    /// 对 <see cref="WorldChunkDiffPacket.Payload"/> 进行 LZ4 解压（如果需要）。
    /// 修复：原实现直接对 diff.Payload 做 MemoryPack 反序列化，未检查 PayloadCompressed 标志，
    /// 当服务端对 InteractionSync/SceneObjectSync 载荷启用 LZ4 压缩时会导致反序列化崩溃。
    /// 使用与 <see cref="WorldDiffApplier"/> 一致的 <see cref="LZ4Pickler.Unpickle"/> 解压，
    /// 确保与生产端 LZ4Pickler.Pickle 格式兼容（[4 bytes int32 原长度][LZ4 块]）。
    /// </summary>
    private static byte[]? DecompressPayloadIfNeeded(WorldChunkDiffPacket diff)
    {
        if (diff.Payload is null || diff.Payload.Length == 0)
            return null;

        if (!diff.PayloadCompressed)
            return diff.Payload;

        // 与生产端 LZ4Pickler.Pickle 对齐：[4 bytes int32 原长度][LZ4 块]
        return LZ4Pickler.Unpickle(diff.Payload);
    }

    /// <summary>
    /// 根据 <see cref="WorldChunkDiffPacket.PayloadType"/> 路由 WorldChunkDiffPacket（P8-8.3）。
    /// InteractionSync 载荷直接反序列化并投递到 <see cref="SyncPacketInbox.InteractionEvents"/>；
    /// SceneObjectSync 载荷直接反序列化并投递到 <see cref="SyncPacketInbox.SceneObjectEvents"/>；
    /// 其余类型（EntityDelta/Event/Correction/VoxelOp）仍走 <see cref="SyncPacketInbox.ChunkDiffs"/> 由下游消费者按 PayloadType 解码。
    /// </summary>
    private void RouteWorldChunkDiff(WorldChunkDiffPacket diff)
    {
        // 解码 payload（处理 LZ4 压缩）
        var decompressedPayload = DecompressPayloadIfNeeded(diff);
        if (decompressedPayload is null) return;

        switch (diff.PayloadType)
        {
            case WorldChunkDiffPayloadType.InteractionSync:
                var interaction = MemoryPackSerializer.Deserialize<InteractionSyncPacket>(decompressedPayload);
                if (interaction is not null)
                {
                    _inbox.InteractionEvents.Enqueue(interaction);
                    InteractionSyncCount++;
                }
                break;
            case WorldChunkDiffPayloadType.SceneObjectSync:
                var sceneObject = MemoryPackSerializer.Deserialize<SceneObjectSyncPacket>(decompressedPayload);
                if (sceneObject is not null)
                {
                    _inbox.SceneObjectEvents.Enqueue(sceneObject);
                    SceneObjectSyncCount++;
                }
                break;
            default:
                // EntityDelta / Event / Correction / VoxelOp 等载荷仍入 ChunkDiffs 队列，
                // 由 WorldDiffApplier 等下游消费者依据 PayloadType 选择解码方式。
                _inbox.ChunkDiffs.Enqueue(diff);
                break;
        }
    }
}
