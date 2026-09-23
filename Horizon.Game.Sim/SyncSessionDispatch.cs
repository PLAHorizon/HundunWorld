using System;
using Horizon.Game.Message.Sync;
using MemoryPack;

namespace Horizon.Game.Sim;

public static class SyncSessionDispatch
{
	public static SyncSessionDispatchResult Dispatch(SyncPacket packet)
	{
		ArgumentNullException.ThrowIfNull(packet, "packet");
		if (!(packet is SnapshotPacket snapshot))
		{
			if (!(packet is InputAckPacket inputAck))
			{
				if (!(packet is EventPacket eventPacket))
				{
					if (packet is WorldChunkDiffPacket diff)
					{
						return DispatchDiff(diff);
					}
					return new SyncSessionDispatchResult
					{
						Kind = packet.Kind,
						Raw = packet
					};
				}
				return DispatchEvents(eventPacket, packet.Kind);
			}
			return new SyncSessionDispatchResult
			{
				Kind = packet.Kind,
				InputAck = inputAck
			};
		}
		return new SyncSessionDispatchResult
		{
			Kind = packet.Kind,
			Snapshot = snapshot
		};
	}

	private static SyncSessionDispatchResult DispatchDiff(WorldChunkDiffPacket diff)
	{
		switch (diff.PayloadType)
		{
		case WorldChunkDiffPayloadType.EntityDelta:
		{
			EntityDelta[] array = TryDeserialize<EntityDelta[]>(diff.Payload);
			if (array != null)
			{
				return new SyncSessionDispatchResult
				{
					Kind = diff.Kind,
					Deltas = array
				};
			}
			return new SyncSessionDispatchResult
			{
				Kind = diff.Kind,
				Raw = diff
			};
		}
		case WorldChunkDiffPayloadType.Event:
		{
			EventPacket eventPacket = TryDeserialize<EventPacket>(diff.Payload);
			if (eventPacket != null)
			{
				return DispatchEvents(eventPacket, diff.Kind);
			}
			return new SyncSessionDispatchResult
			{
				Kind = diff.Kind,
				Raw = diff
			};
		}
		case WorldChunkDiffPayloadType.Correction:
		{
			CorrectionPacket correctionPacket = TryDeserialize<CorrectionPacket>(diff.Payload);
			if (correctionPacket != null)
			{
				return new SyncSessionDispatchResult
				{
					Kind = diff.Kind,
					Correction = correctionPacket
				};
			}
			return new SyncSessionDispatchResult
			{
				Kind = diff.Kind,
				Raw = diff
			};
		}
		default:
			return new SyncSessionDispatchResult
			{
				Kind = diff.Kind,
				Raw = diff
			};
		}
	}

	private static SyncSessionDispatchResult DispatchEvents(EventPacket eventPacket, SyncPacketKind sourceKind)
	{
		CorrectionPacket correctionPacket = null;
		InputAckPacket inputAckPacket = null;
		bool flag = false;
		SyncEvent[] array = eventPacket.Events ?? Array.Empty<SyncEvent>();
		for (int i = 0; i < array.Length; i++)
		{
			SyncEvent syncEvent = array[i];
			switch (syncEvent.Kind)
			{
			case SyncEventKind.Correction:
			{
				byte[] payload = syncEvent.Payload;
				if (payload == null || payload.Length <= 0)
				{
					break;
				}
				CorrectionPacket correctionPacket2 = TryDeserialize<CorrectionPacket>(syncEvent.Payload);
				if (correctionPacket2 != null)
				{
					if (correctionPacket == null)
					{
						correctionPacket = correctionPacket2;
					}
				}
				else
				{
					flag = true;
				}
				continue;
			}
			case SyncEventKind.InputAck:
			{
				byte[] payload = syncEvent.Payload;
				if (payload == null || payload.Length <= 0)
				{
					break;
				}
				InputAckPacket inputAckPacket2 = TryDeserialize<InputAckPacket>(syncEvent.Payload);
				if (inputAckPacket2 != null)
				{
					if (inputAckPacket == null)
					{
						inputAckPacket = inputAckPacket2;
					}
				}
				else
				{
					flag = true;
				}
				continue;
			}
			}
			flag = true;
		}
		return new SyncSessionDispatchResult
		{
			Kind = sourceKind,
			Correction = correctionPacket,
			InputAck = inputAckPacket,
			Event = (flag ? eventPacket : null)
		};
	}

	private static T? TryDeserialize<T>(byte[] payload) where T : class
	{
		if (payload == null || payload.Length == 0)
		{
			return null;
		}
		try
		{
			return MemoryPackSerializer.Deserialize<T>(payload);
		}
		catch
		{
			return null;
		}
	}
}
