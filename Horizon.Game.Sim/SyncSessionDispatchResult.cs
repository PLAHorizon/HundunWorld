using Horizon.Game.Message.Sync;

namespace Horizon.Game.Sim;

public sealed class SyncSessionDispatchResult
{
	public SyncPacketKind Kind { get; init; }

	public SnapshotPacket? Snapshot { get; init; }

	public EntityDelta[]? Deltas { get; init; }

	public CorrectionPacket? Correction { get; init; }

	public InputAckPacket? InputAck { get; init; }

	public EventPacket? Event { get; init; }

	public SyncPacket? Raw { get; init; }
}
