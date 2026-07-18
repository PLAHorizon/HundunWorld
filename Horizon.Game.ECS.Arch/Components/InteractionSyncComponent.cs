using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Components;

/// <summary>
/// 交互槽同步组件（Arch ECS 侧）：由 InteractionApplySystem
/// 从 <see cref="Horizon.Game.Message.Sync.InteractionSyncPacket"/> 写入，
/// 驱动客户端交互表现（占用 / 进行中 / 结束 / 被抢占）。
/// 与协议层 <see cref="Horizon.Game.Message.Sync.Components.InteractionSyncComponent"/> 字段对齐，
/// 但本结构为纯 Arch 组件，不携带序列化特性。
/// </summary>
public struct InteractionSyncComponent
{
    /// <summary>承载该交互槽的实体 NetworkId。</summary>
    public long NetworkId;

    /// <summary>交互槽索引（同一 InteractableId 下可有多个槽位）。</summary>
    public int SlotIdx;

    /// <summary>可交互对象的 NetworkId。</summary>
    public long InteractableId;

    /// <summary>交互者（玩家）的 NetworkId。</summary>
    public long InteractorId;

    /// <summary>交互状态位标志（占用 / 进行中 / 结束 / 被抢占等）。</summary>
    public byte StateBits;

    /// <summary>采样时的服务器 tick（用于 reconciliation / 插值排序）。</summary>
    public long ServerTick;

    /// <summary>是否已占用/开始（StateBits 包含 Start 位）。</summary>
    public bool IsOccupied => (StateBits & InteractionStateBits.Start) != 0;

    /// <summary>是否进行中（已开始且未结束）。</summary>
    public bool IsInProgress => (StateBits & InteractionStateBits.Start) != 0 && (StateBits & InteractionStateBits.End) == 0;

    /// <summary>是否已结束（StateBits 包含 End 位）。</summary>
    public bool IsCompleted => (StateBits & InteractionStateBits.End) != 0;

    /// <summary>是否被抢占（StateBits 包含 Stolen 位）。</summary>
    public bool IsStolen => (StateBits & InteractionStateBits.Stolen) != 0;
}
