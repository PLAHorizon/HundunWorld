using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 输入发送系统：在 NetworkSend 阶段将本地玩家的输入打包成 <see cref="InputPacket"/> 并放入发送队列。
/// </summary>
/// <remarks>
/// 仅查询拥有 <see cref="PlayerInputComponent"/> + <see cref="NetworkIdentityComponent"/>（IsLocalPlayer=true）
/// + <see cref="PredictedTransformComponent"/> 的实体。
/// 打包后的 InputPacket 通过 <see cref="InputSendQueue"/> 缓存，网络层通过 <see cref="GetPendingInputs"/> 批量取出。
/// <para>
/// Task D.2：冗余重传。维护容量 64 的未确认 input 环形缓冲（<see cref="_pendingAcks"/>），
/// 当 <c>pred.ClientTick - _lastAckedClientTick &gt; 5</c> 时（即服务端已确认的 tick 落后当前超过 5），
/// 将环形缓冲中所有未确认 input 重新 enqueue 到 <see cref="InputSendQueue"/>，用于冗余重传以对抗丢包。
/// 网络层收到 <see cref="InputAckPacket"/> 时调用 <see cref="OnInputAck"/> 清理已确认的 input。
/// </para>
/// </remarks>
[ArchSystem(SystemGroup.NetworkSend, order: 0)]
public sealed class InputSendSystem : ArchSystemBase
{
    /// <summary>未确认 input 环形缓冲容量。</summary>
    private const int PendingAcksCapacity = 64;

    /// <summary>触发冗余重传的落后 tick 阈值：当 ClientTick - LastAckedClientTick &gt; 此值时重传。</summary>
    private const int RetransmitThreshold = 5;

    /// <summary>
    /// 当前已注册的 InputSendSystem 实例（兼容静态调用入口）。
    /// 在 <see cref="Update"/> 第一次执行时设置，供未持有 ArchWorldHost 引用的调用方
    /// （如 ECSUpdateDriver、网络层）以 <c>InputSendSystem.Instance?.XXX()</c> 形式访问。
    /// </summary>
    public static InputSendSystem? Instance { get; private set; }

    /// <summary>
    /// 服务端最近一次确认到的客户端 tick（含）。
    /// 由网络层收到 <see cref="InputAckPacket"/> 后通过 <see cref="OnInputAck"/> 推进。
    /// </summary>
    private long _lastAckedClientTick;

    /// <summary>
    /// 未确认 input 环形缓冲（容量 <see cref="PendingAcksCapacity"/>）。
    /// 存储已发送但尚未被服务端确认的 <see cref="InputPacket"/> 副本，用于冗余重传。
    /// </summary>
    private readonly InputPacket[] _pendingAcks = new InputPacket[PendingAcksCapacity];

    /// <summary>环形缓冲中最旧元素的下标（出队位置）。</summary>
    private int _pendingTail;

    /// <summary>环形缓冲中下一个写入位置。</summary>
    private int _pendingHead;

    /// <summary>环形缓冲中当前元素数量。</summary>
    private int _pendingAcksCount;

    /// <summary>
    /// 保护 <see cref="_pendingAcks"/>/_pendingTail/_pendingHead/_pendingAcksCount/_lastAckedClientTick 的锁。
    /// ECS 线程（Update）与网络 IO 线程（OnInputAck）并发访问，需保证线程安全。
    /// </summary>
    private readonly object _pendingLock = new();

    /// <summary>
    /// 获取所有待发送的输入包列表（网络层应调用此方法消费并清空队列）。
    /// </summary>
    public List<InputPacket> GetPendingInputs()
    {
        var list = new List<InputPacket>();
        while (InputSendQueue.Instance.TryDequeue(out var packet))
        {
            list.Add(packet);
        }
        return list;
    }

    /// <summary>
    /// 网络层收到 <see cref="InputAckPacket"/> 时调用，推进已确认 tick 并清理环形缓冲中已确认的 input。
    /// </summary>
    /// <param name="lastProcessedClientTick">服务端最近一次处理到的客户端 tick（含）。</param>
    /// <remarks>
    /// 线程安全：可由网络 IO 线程并发调用。
    /// 仅当 lastProcessedClientTick &gt; _lastAckedClientTick 时推进，避免乱序 ACK 导致回退。
    /// </remarks>
    public void OnInputAck(long lastProcessedClientTick)
    {
        lock (_pendingLock)
        {
            if (lastProcessedClientTick <= _lastAckedClientTick)
            {
                return;
            }

            _lastAckedClientTick = lastProcessedClientTick;

            // 从环形缓冲头部连续移除已确认的 input（按 ClientTick 递增顺序写入，可从头部连续移除）。
            while (_pendingAcksCount > 0)
            {
                ref var oldest = ref _pendingAcks[_pendingTail];
                if (oldest.ClientTick > lastProcessedClientTick)
                {
                    break;
                }

                _pendingAcks[_pendingTail] = default;
                _pendingTail = (_pendingTail + 1) % PendingAcksCapacity;
                _pendingAcksCount--;
            }
        }
    }

    /// <summary>
    /// 将 InputPacket 副本写入未确认环形缓冲。
    /// 必须在 <see cref="_pendingLock"/> 内调用。
    /// </summary>
    private void WriteToPendingAcks(InputPacket packet)
    {
        if (_pendingAcksCount >= PendingAcksCapacity)
        {
            // 缓冲已满：覆盖最旧元素（_pendingTail 推进），保留最近的 64 个未确认 input。
            _pendingAcks[_pendingHead] = packet;
            _pendingHead = (_pendingHead + 1) % PendingAcksCapacity;
            _pendingTail = (_pendingTail + 1) % PendingAcksCapacity;
        }
        else
        {
            _pendingAcks[_pendingHead] = packet;
            _pendingHead = (_pendingHead + 1) % PendingAcksCapacity;
            _pendingAcksCount++;
        }
    }

    /// <summary>
    /// 检查冗余重传条件，若落后阈值超 <see cref="RetransmitThreshold"/> 则将所有未确认 input 重新入队。
    /// 必须在 <see cref="_pendingLock"/> 内调用。
    /// </summary>
    /// <param name="currentClientTick">当前帧的客户端 tick。</param>
    private void TryRetransmitUnconfirmed(long currentClientTick)
    {
        if (_pendingAcksCount == 0)
        {
            return;
        }

        if (currentClientTick - _lastAckedClientTick <= RetransmitThreshold)
        {
            return;
        }

        // 冗余重传：将环形缓冲中所有未确认 input 重新 enqueue 到发送队列。
        // 从最旧元素开始按顺序重传，服务端去重（基于 ClientTick）会忽略重复包。
        for (int i = 0; i < _pendingAcksCount; i++)
        {
            var idx = (_pendingTail + i) % PendingAcksCapacity;
            InputSendQueue.Instance.Enqueue(_pendingAcks[idx]);
        }
    }

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        // 首次 Update 时登记全局实例，供未持有 ArchWorldHost 引用的调用方
        // （如 ECSUpdateDriver.FlushInputSendQueue、网络层 OnInputAck 桥接）通过 Instance 访问。
        Instance ??= this;

        var query = new QueryDescription()
            .WithAll<PlayerInputComponent, NetworkIdentityComponent, PredictedTransformComponent>();

        world.Query(in query, (Entity entity, ref PlayerInputComponent input, ref NetworkIdentityComponent netId, ref PredictedTransformComponent pred) =>
        {
            if (!netId.IsLocalPlayer)
            {
                return;
            }

            var packet = new InputPacket
            {
                ClientTick = pred.ClientTick,
                InputBits = input.InputBits,
                LookYaw = input.LookYaw,
                LookPitch = input.LookPitch,
                MoveX = input.MoveX,
                MoveY = input.MoveY,
                CharacterId = netId.EntityId,
                PredictedEndX = pred.X,
                PredictedEndY = pred.Y,
                PredictedEndZ = pred.Z,
            };

            InputSendQueue.Instance.Enqueue(packet);

            // 写入未确认环形缓冲，并检查冗余重传条件。
            // 加锁保证与 OnInputAck（网络线程）的并发安全。
            lock (_pendingLock)
            {
                WriteToPendingAcks(packet);
                TryRetransmitUnconfirmed(pred.ClientTick);
            }
        });
    }
}
