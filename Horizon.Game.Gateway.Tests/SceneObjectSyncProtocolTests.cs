using Horizon.Game.Core.Sim.Client;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 场景对象同步协议层单元测试（阶段 C）。
/// 覆盖 SceneObjectSyncPacket 的 MemoryPack 序列化往返（含/不含 Transform、多态）、
/// SceneObjectStateAuthComponent / SceneObjectTransformComponent 序列化往返、
/// SceneObjectStateBits 常量与辅助方法、SyncPacketDispatcher 路由、SyncPacketInbox 队列 FIFO 与计数。
/// </summary>
public class SceneObjectSyncProtocolTests
{
    #region Task C.7.1 - SceneObjectSyncPacket MemoryPack 序列化往返

    [Fact]
    public void SceneObjectSyncPacket_MemoryPack_RoundTrip_WithTransform_PreservesAllFields()
    {
        // Arrange - 含 Transform
        var original = new SceneObjectSyncPacket
        {
            ObjectId = 0xABCDEF1234UL,
            StateBits = SceneObjectStateBits.Opened | SceneObjectStateBits.Locked,
            CooldownEndTick = 999999L,
            OwnerCharacterId = 0x567890ABCDEFUL,
            HasTransform = true,
            TransformX = 1.5f,
            TransformY = -2.25f,
            TransformZ = 3.75f,
            TransformPitch = 0.1f,
            TransformYaw = 1.57f,
            TransformRoll = -0.5f,
            ServerTick = 123456L,
        };

        // Act - 序列化
        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SceneObjectSyncPacket>(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        // Act - 反序列化
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectSyncPacket>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.ObjectId, restored!.ObjectId);
        Assert.Equal(original.StateBits, restored.StateBits);
        Assert.Equal(original.CooldownEndTick, restored.CooldownEndTick);
        Assert.Equal(original.OwnerCharacterId, restored.OwnerCharacterId);
        Assert.Equal(original.HasTransform, restored.HasTransform);
        Assert.Equal(original.TransformX, restored.TransformX);
        Assert.Equal(original.TransformY, restored.TransformY);
        Assert.Equal(original.TransformZ, restored.TransformZ);
        Assert.Equal(original.TransformPitch, restored.TransformPitch);
        Assert.Equal(original.TransformYaw, restored.TransformYaw);
        Assert.Equal(original.TransformRoll, restored.TransformRoll);
        Assert.Equal(original.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void SceneObjectSyncPacket_MemoryPack_RoundTrip_WithoutTransform_PreservesAllFields()
    {
        // Arrange - 不含 Transform（静态场景对象，如普通宝箱/开关）
        var original = new SceneObjectSyncPacket
        {
            ObjectId = 42UL,
            StateBits = SceneObjectStateBits.Activated,
            CooldownEndTick = 0L,
            OwnerCharacterId = 0UL,
            HasTransform = false,
            TransformX = 0f,
            TransformY = 0f,
            TransformZ = 0f,
            TransformPitch = 0f,
            TransformYaw = 0f,
            TransformRoll = 0f,
            ServerTick = 100L,
        };

        // Act
        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SceneObjectSyncPacket>(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectSyncPacket>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.ObjectId, restored!.ObjectId);
        Assert.Equal(original.StateBits, restored.StateBits);
        Assert.Equal(original.CooldownEndTick, restored.CooldownEndTick);
        Assert.Equal(original.OwnerCharacterId, restored.OwnerCharacterId);
        Assert.False(restored.HasTransform);
        Assert.Equal(original.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void SceneObjectSyncPacket_MemoryPack_RoundTrip_ZeroValues()
    {
        var original = new SceneObjectSyncPacket
        {
            ObjectId = 0UL,
            StateBits = 0u,
            CooldownEndTick = 0L,
            OwnerCharacterId = 0UL,
            HasTransform = false,
            ServerTick = 0L,
        };

        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SceneObjectSyncPacket>(original);
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectSyncPacket>(bytes);

        Assert.NotNull(restored);
        Assert.Equal(0UL, restored!.ObjectId);
        Assert.Equal(0u, restored.StateBits);
        Assert.Equal(0L, restored.CooldownEndTick);
        Assert.Equal(0UL, restored.OwnerCharacterId);
        Assert.False(restored.HasTransform);
        Assert.Equal(0L, restored.ServerTick);
    }

    [Fact]
    public void SceneObjectSyncPacket_MemoryPack_RoundTrip_MaxValues()
    {
        var original = new SceneObjectSyncPacket
        {
            ObjectId = ulong.MaxValue,
            StateBits = uint.MaxValue,
            CooldownEndTick = long.MaxValue,
            OwnerCharacterId = ulong.MaxValue,
            HasTransform = true,
            TransformX = float.MaxValue,
            TransformY = float.MaxValue,
            TransformZ = float.MaxValue,
            TransformPitch = float.MaxValue,
            TransformYaw = float.MaxValue,
            TransformRoll = float.MaxValue,
            ServerTick = long.MaxValue,
        };

        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SceneObjectSyncPacket>(original);
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectSyncPacket>(bytes);

        Assert.NotNull(restored);
        Assert.Equal(ulong.MaxValue, restored!.ObjectId);
        Assert.Equal(uint.MaxValue, restored.StateBits);
        Assert.Equal(long.MaxValue, restored.CooldownEndTick);
        Assert.Equal(ulong.MaxValue, restored.OwnerCharacterId);
        Assert.True(restored.HasTransform);
        Assert.Equal(float.MaxValue, restored.TransformX);
        Assert.Equal(float.MaxValue, restored.TransformY);
        Assert.Equal(float.MaxValue, restored.TransformZ);
        Assert.Equal(float.MaxValue, restored.TransformPitch);
        Assert.Equal(float.MaxValue, restored.TransformYaw);
        Assert.Equal(float.MaxValue, restored.TransformRoll);
        Assert.Equal(long.MaxValue, restored.ServerTick);
    }

    [Fact]
    public void SceneObjectSyncPacket_Kind_IsSceneObjectSync()
    {
        var packet = new SceneObjectSyncPacket();
        Assert.Equal(SyncPacketKind.SceneObjectSync, packet.Kind);
    }

    [Fact]
    public void SyncPacketKind_SceneObjectSync_HasExpectedValue()
    {
        // 确认枚举值为 10（紧跟 InteractionSync=9 之后递增）
        Assert.Equal(10, (int)SyncPacketKind.SceneObjectSync);
    }

    #endregion

    #region Task C.7.2 - SceneObjectSyncPacket 多态序列化（作为 SyncPacket 基类）

    [Fact]
    public void SceneObjectSyncPacket_Polymorphic_RoundTrip_AsSyncPacket_WithTransform()
    {
        // 验证作为基类 SyncPacket 的多态序列化往返（含 Transform）
        var original = new SceneObjectSyncPacket
        {
            ObjectId = 100UL,
            StateBits = SceneObjectStateBits.Opened,
            CooldownEndTick = 5000L,
            OwnerCharacterId = 200UL,
            HasTransform = true,
            TransformX = 10.5f,
            TransformY = 20.5f,
            TransformZ = 30.5f,
            TransformPitch = 0.1f,
            TransformYaw = 0.2f,
            TransformRoll = 0.3f,
            ServerTick = 9999L,
        };

        // 作为基类序列化（多态）
        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SyncPacket>(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        // 作为基类反序列化
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SyncPacket>(bytes);

        // Assert
        Assert.NotNull(restored);
        Assert.IsType<SceneObjectSyncPacket>(restored);
        var typed = (SceneObjectSyncPacket)restored!;
        Assert.Equal(original.ObjectId, typed.ObjectId);
        Assert.Equal(original.StateBits, typed.StateBits);
        Assert.Equal(original.CooldownEndTick, typed.CooldownEndTick);
        Assert.Equal(original.OwnerCharacterId, typed.OwnerCharacterId);
        Assert.Equal(original.HasTransform, typed.HasTransform);
        Assert.Equal(original.TransformX, typed.TransformX);
        Assert.Equal(original.TransformY, typed.TransformY);
        Assert.Equal(original.TransformZ, typed.TransformZ);
        Assert.Equal(original.TransformPitch, typed.TransformPitch);
        Assert.Equal(original.TransformYaw, typed.TransformYaw);
        Assert.Equal(original.TransformRoll, typed.TransformRoll);
        Assert.Equal(original.ServerTick, typed.ServerTick);
        Assert.Equal(SyncPacketKind.SceneObjectSync, typed.Kind);
    }

    [Fact]
    public void SceneObjectSyncPacket_Polymorphic_RoundTrip_AsSyncPacket_WithoutTransform()
    {
        // 验证作为基类 SyncPacket 的多态序列化往返（不含 Transform）
        var original = new SceneObjectSyncPacket
        {
            ObjectId = 7UL,
            StateBits = SceneObjectStateBits.Reset,
            CooldownEndTick = 0L,
            OwnerCharacterId = 0UL,
            HasTransform = false,
            ServerTick = 42L,
        };

        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SyncPacket>(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SyncPacket>(bytes);

        Assert.NotNull(restored);
        Assert.IsType<SceneObjectSyncPacket>(restored);
        var typed = (SceneObjectSyncPacket)restored!;
        Assert.Equal(original.ObjectId, typed.ObjectId);
        Assert.Equal(original.StateBits, typed.StateBits);
        Assert.Equal(original.CooldownEndTick, typed.CooldownEndTick);
        Assert.Equal(original.OwnerCharacterId, typed.OwnerCharacterId);
        Assert.False(typed.HasTransform);
        Assert.Equal(original.ServerTick, typed.ServerTick);
    }

    #endregion

    #region Task C.7.3 - SceneObjectStateAuthComponent 序列化往返

    [Fact]
    public void SceneObjectStateAuthComponent_MemoryPack_RoundTrip_PreservesAllFields()
    {
        // Arrange
        var original = new SceneObjectStateAuthComponent
        {
            ObjectId = 0x1234567890ABCDEFUL,
            ObjectType = SceneObjectType.Door,
            StateBits = SceneObjectStateBits.Opened | SceneObjectStateBits.Activated,
            CooldownEndTick = 888888L,
            OwnerCharacterId = 0xFEDCBA0987654321UL,
            ServerTick = 777777L,
        };

        // Act
        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SceneObjectStateAuthComponent>(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectStateAuthComponent>(bytes);

        // Assert
        Assert.Equal(original.ObjectId, restored.ObjectId);
        Assert.Equal(original.ObjectType, restored.ObjectType);
        Assert.Equal(original.StateBits, restored.StateBits);
        Assert.Equal(original.CooldownEndTick, restored.CooldownEndTick);
        Assert.Equal(original.OwnerCharacterId, restored.OwnerCharacterId);
        Assert.Equal(original.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void SceneObjectStateAuthComponent_MemoryPack_RoundTrip_AllObjectTypes()
    {
        // 验证所有 SceneObjectType 枚举值的序列化往返
        foreach (SceneObjectType type in Enum.GetValues(typeof(SceneObjectType)))
        {
            var original = new SceneObjectStateAuthComponent
            {
                ObjectId = 1UL,
                ObjectType = type,
                StateBits = 0u,
                CooldownEndTick = 0L,
                OwnerCharacterId = 0UL,
                ServerTick = 0L,
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize<SceneObjectStateAuthComponent>(original);
            var restored = MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectStateAuthComponent>(bytes);

            Assert.Equal(type, restored.ObjectType);
        }
    }

    #endregion

    #region Task C.7.4 - SceneObjectTransformComponent 序列化往返

    [Fact]
    public void SceneObjectTransformComponent_MemoryPack_RoundTrip_PreservesAllFields()
    {
        // Arrange
        var original = new SceneObjectTransformComponent
        {
            ObjectId = 0xDEADBEEFUL,
            X = 100.5f,
            Y = -200.25f,
            Z = 300.75f,
            Pitch = 0.123f,
            Yaw = 1.57f,
            Roll = -3.14f,
            ServerTick = 55555L,
        };

        // Act
        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SceneObjectTransformComponent>(original);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectTransformComponent>(bytes);

        // Assert
        Assert.Equal(original.ObjectId, restored.ObjectId);
        Assert.Equal(original.X, restored.X);
        Assert.Equal(original.Y, restored.Y);
        Assert.Equal(original.Z, restored.Z);
        Assert.Equal(original.Pitch, restored.Pitch);
        Assert.Equal(original.Yaw, restored.Yaw);
        Assert.Equal(original.Roll, restored.Roll);
        Assert.Equal(original.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void SceneObjectTransformComponent_MemoryPack_RoundTrip_ZeroValues()
    {
        var original = new SceneObjectTransformComponent
        {
            ObjectId = 0UL,
            X = 0f,
            Y = 0f,
            Z = 0f,
            Pitch = 0f,
            Yaw = 0f,
            Roll = 0f,
            ServerTick = 0L,
        };

        var bytes = MemoryPack.MemoryPackSerializer.Serialize<SceneObjectTransformComponent>(original);
        var restored = MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectTransformComponent>(bytes);

        Assert.Equal(0UL, restored.ObjectId);
        Assert.Equal(0f, restored.X);
        Assert.Equal(0f, restored.Y);
        Assert.Equal(0f, restored.Z);
        Assert.Equal(0f, restored.Pitch);
        Assert.Equal(0f, restored.Yaw);
        Assert.Equal(0f, restored.Roll);
        Assert.Equal(0L, restored.ServerTick);
    }

    #endregion

    #region Task C.7.5 - SceneObjectStateBits 常量与辅助方法正确性

    [Fact]
    public void SceneObjectStateBits_Constants_HaveExpectedValues()
    {
        // 验证常量值（按 spec：Opened=0x01/Activated=0x02/Locked=0x04/Reset=0x08）
        Assert.Equal(0x01u, SceneObjectStateBits.Opened);
        Assert.Equal(0x02u, SceneObjectStateBits.Activated);
        Assert.Equal(0x04u, SceneObjectStateBits.Locked);
        Assert.Equal(0x08u, SceneObjectStateBits.Reset);
        Assert.Equal(0x0Fu, SceneObjectStateBits.StateMask);
    }

    [Fact]
    public void SceneObjectStateBits_Constants_AreDistinctPowersOfTwo()
    {
        // 验证各状态位互不重叠（每个都是独立的 bit）
        var allBits = SceneObjectStateBits.Opened | SceneObjectStateBits.Activated |
                      SceneObjectStateBits.Locked | SceneObjectStateBits.Reset;
        Assert.Equal(SceneObjectStateBits.StateMask, allBits);

        // 验证每个位独立
        Assert.Equal(0u, SceneObjectStateBits.Opened & SceneObjectStateBits.Activated);
        Assert.Equal(0u, SceneObjectStateBits.Opened & SceneObjectStateBits.Locked);
        Assert.Equal(0u, SceneObjectStateBits.Opened & SceneObjectStateBits.Reset);
        Assert.Equal(0u, SceneObjectStateBits.Activated & SceneObjectStateBits.Locked);
        Assert.Equal(0u, SceneObjectStateBits.Activated & SceneObjectStateBits.Reset);
        Assert.Equal(0u, SceneObjectStateBits.Locked & SceneObjectStateBits.Reset);
    }

    [Fact]
    public void SceneObjectStateBits_HasOpened_DetectsBit()
    {
        Assert.True(SceneObjectStateBits.HasOpened(SceneObjectStateBits.Opened));
        Assert.True(SceneObjectStateBits.HasOpened(0xFFu)); // 全 1
        Assert.False(SceneObjectStateBits.HasOpened(0u));
        Assert.False(SceneObjectStateBits.HasOpened(SceneObjectStateBits.Activated));
        Assert.False(SceneObjectStateBits.HasOpened(SceneObjectStateBits.Locked));
        Assert.False(SceneObjectStateBits.HasOpened(SceneObjectStateBits.Reset));
    }

    [Fact]
    public void SceneObjectStateBits_HasActivated_DetectsBit()
    {
        Assert.True(SceneObjectStateBits.HasActivated(SceneObjectStateBits.Activated));
        Assert.True(SceneObjectStateBits.HasActivated(0xFFu));
        Assert.False(SceneObjectStateBits.HasActivated(0u));
        Assert.False(SceneObjectStateBits.HasActivated(SceneObjectStateBits.Opened));
        Assert.False(SceneObjectStateBits.HasActivated(SceneObjectStateBits.Locked));
        Assert.False(SceneObjectStateBits.HasActivated(SceneObjectStateBits.Reset));
    }

    [Fact]
    public void SceneObjectStateBits_HasLocked_DetectsBit()
    {
        Assert.True(SceneObjectStateBits.HasLocked(SceneObjectStateBits.Locked));
        Assert.True(SceneObjectStateBits.HasLocked(0xFFu));
        Assert.False(SceneObjectStateBits.HasLocked(0u));
        Assert.False(SceneObjectStateBits.HasLocked(SceneObjectStateBits.Opened));
        Assert.False(SceneObjectStateBits.HasLocked(SceneObjectStateBits.Activated));
        Assert.False(SceneObjectStateBits.HasLocked(SceneObjectStateBits.Reset));
    }

    [Fact]
    public void SceneObjectStateBits_HasReset_DetectsBit()
    {
        Assert.True(SceneObjectStateBits.HasReset(SceneObjectStateBits.Reset));
        Assert.True(SceneObjectStateBits.HasReset(0xFFu));
        Assert.False(SceneObjectStateBits.HasReset(0u));
        Assert.False(SceneObjectStateBits.HasReset(SceneObjectStateBits.Opened));
        Assert.False(SceneObjectStateBits.HasReset(SceneObjectStateBits.Activated));
        Assert.False(SceneObjectStateBits.HasReset(SceneObjectStateBits.Locked));
    }

    [Fact]
    public void SceneObjectStateAuthComponent_Properties_ReadAndWriteStateBits()
    {
        // 验证 IsOpened/IsActivated/IsLocked/IsReset 属性的 get/set（参考 EntityStateAuthComponent.IsDead 模式）
        var comp = new SceneObjectStateAuthComponent
        {
            ObjectId = 1UL,
            ObjectType = SceneObjectType.Chest,
        };

        // 初始为 0
        Assert.Equal(0u, comp.StateBits);
        Assert.False(comp.IsOpened);
        Assert.False(comp.IsActivated);
        Assert.False(comp.IsLocked);
        Assert.False(comp.IsReset);

        // 设置 Opened
        comp.IsOpened = true;
        Assert.True(comp.IsOpened);
        Assert.Equal(SceneObjectStateBits.Opened, comp.StateBits);

        // 设置 Activated
        comp.IsActivated = true;
        Assert.True(comp.IsActivated);
        Assert.Equal(SceneObjectStateBits.Opened | SceneObjectStateBits.Activated, comp.StateBits);

        // 设置 Locked
        comp.IsLocked = true;
        Assert.True(comp.IsLocked);
        Assert.Equal(SceneObjectStateBits.Opened | SceneObjectStateBits.Activated | SceneObjectStateBits.Locked, comp.StateBits);

        // 设置 Reset
        comp.IsReset = true;
        Assert.True(comp.IsReset);
        Assert.Equal(SceneObjectStateBits.StateMask, comp.StateBits);

        // 清除 Opened
        comp.IsOpened = false;
        Assert.False(comp.IsOpened);
        Assert.Equal(SceneObjectStateBits.Activated | SceneObjectStateBits.Locked | SceneObjectStateBits.Reset, comp.StateBits);

        // 清除全部
        comp.IsActivated = false;
        comp.IsLocked = false;
        comp.IsReset = false;
        Assert.Equal(0u, comp.StateBits);
        Assert.False(comp.IsOpened);
        Assert.False(comp.IsActivated);
        Assert.False(comp.IsLocked);
        Assert.False(comp.IsReset);
    }

    [Fact]
    public void SceneObjectType_Enum_HasExpectedValues()
    {
        // 验证枚举值（按 spec：Chest=0/Switch=1/Door=2/Lever=3/Portal=4）
        Assert.Equal(0, (int)SceneObjectType.Chest);
        Assert.Equal(1, (int)SceneObjectType.Switch);
        Assert.Equal(2, (int)SceneObjectType.Door);
        Assert.Equal(3, (int)SceneObjectType.Lever);
        Assert.Equal(4, (int)SceneObjectType.Portal);
    }

    #endregion

    #region Task C.7.6 - SyncPacketDispatcher 路由 SceneObjectSyncPacket 到 SceneObjectEvents 队列

    [Fact]
    public void SyncPacketDispatcher_Routes_SceneObjectSyncPacket_ToInbox()
    {
        // Arrange
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var packet = new SceneObjectSyncPacket
        {
            ObjectId = 42UL,
            StateBits = SceneObjectStateBits.Opened,
            CooldownEndTick = 1000L,
            OwnerCharacterId = 99UL,
            HasTransform = true,
            TransformX = 1.5f,
            TransformY = 2.5f,
            TransformZ = 3.5f,
            TransformPitch = 0.1f,
            TransformYaw = 0.2f,
            TransformRoll = 0.3f,
            ServerTick = 555L,
        };

        // Act - 分派
        dispatcher.Dispatch(packet);

        // Assert - 应路由到 SceneObjectEvents 队列
        Assert.True(inbox.SceneObjectEvents.TryDequeue(out var restored));
        Assert.NotNull(restored);
        Assert.Equal(packet.ObjectId, restored!.ObjectId);
        Assert.Equal(packet.StateBits, restored.StateBits);
        Assert.Equal(packet.CooldownEndTick, restored.CooldownEndTick);
        Assert.Equal(packet.OwnerCharacterId, restored.OwnerCharacterId);
        Assert.Equal(packet.HasTransform, restored.HasTransform);
        Assert.Equal(packet.TransformX, restored.TransformX);
        Assert.Equal(packet.TransformY, restored.TransformY);
        Assert.Equal(packet.TransformZ, restored.TransformZ);
        Assert.Equal(packet.TransformPitch, restored.TransformPitch);
        Assert.Equal(packet.TransformYaw, restored.TransformYaw);
        Assert.Equal(packet.TransformRoll, restored.TransformRoll);
        Assert.Equal(packet.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void SyncPacketDispatcher_Routes_SceneObjectSyncPacket_WithoutTransform_ToInbox()
    {
        // Arrange - 不含 Transform 的场景对象
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var packet = new SceneObjectSyncPacket
        {
            ObjectId = 7UL,
            StateBits = SceneObjectStateBits.Activated,
            CooldownEndTick = 0L,
            OwnerCharacterId = 0UL,
            HasTransform = false,
            ServerTick = 100L,
        };

        // Act
        dispatcher.Dispatch(packet);

        // Assert
        Assert.True(inbox.SceneObjectEvents.TryDequeue(out var restored));
        Assert.NotNull(restored);
        Assert.Equal(packet.ObjectId, restored!.ObjectId);
        Assert.Equal(packet.StateBits, restored.StateBits);
        Assert.False(restored.HasTransform);
        Assert.Equal(packet.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void SyncPacketDispatcher_SceneObjectSyncCount_Increments()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);
        var initialCount = dispatcher.SceneObjectSyncCount;

        dispatcher.Dispatch(new SceneObjectSyncPacket { ObjectId = 1UL });
        dispatcher.Dispatch(new SceneObjectSyncPacket { ObjectId = 2UL });
        dispatcher.Dispatch(new SceneObjectSyncPacket { ObjectId = 3UL });

        Assert.Equal(initialCount + 3, dispatcher.SceneObjectSyncCount);
    }

    [Fact]
    public void SyncPacketDispatcher_Routes_SceneObjectSyncPacket_DoesNotAffectOtherQueues()
    {
        // 验证 SceneObjectSyncPacket 路由不会污染其他队列
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);

        dispatcher.Dispatch(new SceneObjectSyncPacket { ObjectId = 1UL });

        Assert.Equal(0, inbox.InteractionEvents.Count);
        Assert.Equal(0, inbox.ChunkDiffs.Count);
        Assert.Equal(0, inbox.PatchManifests.Count);
        Assert.Equal(1, inbox.SceneObjectEvents.Count);
    }

    [Fact]
    public void SyncPacketDispatcher_NullPacket_ThrowsArgumentNullException()
    {
        var inbox = new SyncPacketInbox();
        var dispatcher = new SyncPacketDispatcher(inbox);

        Assert.Throws<ArgumentNullException>(() => dispatcher.Dispatch(null!));
    }

    #endregion

    #region Task C.7.7 - SyncPacketInbox.SceneObjectEvents 队列 FIFO 与计数

    [Fact]
    public void SyncPacketInbox_SceneObjectEvents_EnqueueDequeue()
    {
        // Arrange
        var inbox = new SyncPacketInbox();
        var packet = new SceneObjectSyncPacket
        {
            ObjectId = 10UL,
            StateBits = SceneObjectStateBits.Locked,
            CooldownEndTick = 200L,
            OwnerCharacterId = 30UL,
            HasTransform = false,
            ServerTick = 400L,
        };

        // Act - 入队
        inbox.SceneObjectEvents.Enqueue(packet);

        // Assert - 出队
        Assert.True(inbox.SceneObjectEvents.TryDequeue(out var restored));
        Assert.NotNull(restored);
        Assert.Equal(packet.ObjectId, restored!.ObjectId);
        Assert.Equal(packet.StateBits, restored.StateBits);
        Assert.Equal(packet.CooldownEndTick, restored.CooldownEndTick);
        Assert.Equal(packet.OwnerCharacterId, restored.OwnerCharacterId);
        Assert.Equal(packet.HasTransform, restored.HasTransform);
        Assert.Equal(packet.ServerTick, restored.ServerTick);
    }

    [Fact]
    public void SyncPacketInbox_SceneObjectEvents_EmptyQueue_ReturnsFalse()
    {
        var inbox = new SyncPacketInbox();
        Assert.False(inbox.SceneObjectEvents.TryDequeue(out _));
    }

    [Fact]
    public void SyncPacketInbox_SceneObjectEvents_FifoOrder()
    {
        // 验证 FIFO 顺序
        var inbox = new SyncPacketInbox();
        var p1 = new SceneObjectSyncPacket { ObjectId = 1UL, ServerTick = 100L };
        var p2 = new SceneObjectSyncPacket { ObjectId = 2UL, ServerTick = 200L };
        var p3 = new SceneObjectSyncPacket { ObjectId = 3UL, ServerTick = 300L };

        inbox.SceneObjectEvents.Enqueue(p1);
        inbox.SceneObjectEvents.Enqueue(p2);
        inbox.SceneObjectEvents.Enqueue(p3);

        Assert.True(inbox.SceneObjectEvents.TryDequeue(out var r1));
        Assert.Equal(1UL, r1!.ObjectId);
        Assert.Equal(100L, r1.ServerTick);

        Assert.True(inbox.SceneObjectEvents.TryDequeue(out var r2));
        Assert.Equal(2UL, r2!.ObjectId);
        Assert.Equal(200L, r2.ServerTick);

        Assert.True(inbox.SceneObjectEvents.TryDequeue(out var r3));
        Assert.Equal(3UL, r3!.ObjectId);
        Assert.Equal(300L, r3.ServerTick);

        // 队列应已空
        Assert.False(inbox.SceneObjectEvents.TryDequeue(out _));
    }

    [Fact]
    public void SyncPacketInbox_SceneObjectEvents_Count_TracksQueueDepth()
    {
        // 验证 Count 反映队列深度
        var inbox = new SyncPacketInbox();
        Assert.Equal(0, inbox.SceneObjectEvents.Count);

        inbox.SceneObjectEvents.Enqueue(new SceneObjectSyncPacket { ObjectId = 1UL });
        Assert.Equal(1, inbox.SceneObjectEvents.Count);

        inbox.SceneObjectEvents.Enqueue(new SceneObjectSyncPacket { ObjectId = 2UL });
        inbox.SceneObjectEvents.Enqueue(new SceneObjectSyncPacket { ObjectId = 3UL });
        Assert.Equal(3, inbox.SceneObjectEvents.Count);

        inbox.SceneObjectEvents.TryDequeue(out _);
        Assert.Equal(2, inbox.SceneObjectEvents.Count);
    }

    [Fact]
    public void SyncPacketInbox_Snapshot_Reports_SceneObjectEventCount()
    {
        // 验证 Snapshot() 包含 PendingSceneObjectEventCount
        var inbox = new SyncPacketInbox();
        inbox.SceneObjectEvents.Enqueue(new SceneObjectSyncPacket { ObjectId = 1UL });
        inbox.SceneObjectEvents.Enqueue(new SceneObjectSyncPacket { ObjectId = 2UL });

        var snapshot = inbox.Snapshot();

        Assert.Equal(2, snapshot.PendingSceneObjectEventCount);
    }

    [Fact]
    public void SyncPacketInbox_SceneObjectEvents_IndependentFromInteractionEvents()
    {
        // 验证 SceneObjectEvents 与 InteractionEvents 队列相互独立
        var inbox = new SyncPacketInbox();

        inbox.SceneObjectEvents.Enqueue(new SceneObjectSyncPacket { ObjectId = 1UL });
        inbox.InteractionEvents.Enqueue(new InteractionSyncPacket { SlotIdx = 1 });

        Assert.Equal(1, inbox.SceneObjectEvents.Count);
        Assert.Equal(1, inbox.InteractionEvents.Count);

        // 出队互不影响
        Assert.True(inbox.SceneObjectEvents.TryDequeue(out var sceneObj));
        Assert.Equal(1UL, sceneObj!.ObjectId);
        Assert.Equal(0, inbox.SceneObjectEvents.Count);
        Assert.Equal(1, inbox.InteractionEvents.Count);

        Assert.True(inbox.InteractionEvents.TryDequeue(out var interaction));
        Assert.Equal(1, interaction!.SlotIdx);
        Assert.Equal(0, inbox.InteractionEvents.Count);
    }

    #endregion
}
