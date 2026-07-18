using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Core.Persistence;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Task C.7.4：持久化层 Save/Load 往返测试。
/// <para>
/// 使用 <see cref="InMemorySceneObjectPersistenceStore"/> 作为 ISceneObjectPersistenceStore 的内存实现，
/// 验证 SaveSingle/SaveWorldState/LoadWorldState 的 upsert 语义、字段保留、分片隔离等行为，
/// 以及与 ZoneShardGrain 集成时的即时落盘路径。
/// </para>
/// </summary>
public class SceneObjectPersistenceTests
{
    // =======================================================================
    // 测试 1: Load_EmptyShard_ReturnsEmptyDict
    // =======================================================================
    /// <summary>
    /// 加载未保存任何状态的分片应返回空字典（非 null）。
    /// </summary>
    [Fact]
    public async Task Load_EmptyShard_ReturnsEmptyDict()
    {
        var store = new InMemorySceneObjectPersistenceStore();

        var loaded = await store.LoadWorldStateAsync(shardKey: 1001);

        Assert.NotNull(loaded);
        Assert.Empty(loaded);
    }

    // =======================================================================
    // 测试 2: SaveSingle_AndLoad_RoundTrip
    // =======================================================================
    /// <summary>
    /// SaveSingleAsync 后 LoadWorldStateAsync 应能取回该条状态，关键字段一致。
    /// </summary>
    [Fact]
    public async Task SaveSingle_AndLoad_RoundTrip()
    {
        var store = new InMemorySceneObjectPersistenceStore();
        var state = new SceneObjectStateData
        {
            ObjectId = 2001,
            ShardKey = 2001,
            ObjectType = SceneObjectType.Chest,
            StateBits = SceneObjectStateBits.Opened,
            CooldownEndTick = 300,
            OwnerCharacterId = 99001,
            UpdatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };

        await store.SaveSingleAsync(shardKey: 2001, state);
        var loaded = await store.LoadWorldStateAsync(shardKey: 2001);

        Assert.Single(loaded);
        Assert.True(loaded.ContainsKey(2001UL));

        var restored = loaded[2001UL];
        Assert.Equal(state.ObjectId, restored.ObjectId);
        Assert.Equal(state.ShardKey, restored.ShardKey);
        Assert.Equal(state.ObjectType, restored.ObjectType);
        Assert.Equal(state.StateBits, restored.StateBits);
        Assert.Equal(state.CooldownEndTick, restored.CooldownEndTick);
        Assert.Equal(state.OwnerCharacterId, restored.OwnerCharacterId);
        Assert.Equal(state.UpdatedAt, restored.UpdatedAt);
    }

    // =======================================================================
    // 测试 3: SaveBatch_AndLoad_RoundTrip
    // =======================================================================
    /// <summary>
    /// SaveWorldStateAsync 批量保存多条状态后 LoadWorldStateAsync 应取回全部。
    /// </summary>
    [Fact]
    public async Task SaveBatch_AndLoad_RoundTrip()
    {
        var store = new InMemorySceneObjectPersistenceStore();
        var shardKey = 3001L;
        var states = new List<SceneObjectStateData>
        {
            new()
            {
                ObjectId = 3001, ShardKey = shardKey, ObjectType = SceneObjectType.Chest,
                StateBits = SceneObjectStateBits.Opened, CooldownEndTick = 300, OwnerCharacterId = 1,
            },
            new()
            {
                ObjectId = 3002, ShardKey = shardKey, ObjectType = SceneObjectType.Door,
                StateBits = SceneObjectStateBits.Activated, CooldownEndTick = 600, OwnerCharacterId = 2,
            },
            new()
            {
                ObjectId = 3003, ShardKey = shardKey, ObjectType = SceneObjectType.Lever,
                StateBits = SceneObjectStateBits.Reset, CooldownEndTick = 0, OwnerCharacterId = 0,
            },
        };

        await store.SaveWorldStateAsync(shardKey, states);
        var loaded = await store.LoadWorldStateAsync(shardKey);

        Assert.Equal(3, loaded.Count);
        Assert.True(loaded.ContainsKey(3001UL));
        Assert.True(loaded.ContainsKey(3002UL));
        Assert.True(loaded.ContainsKey(3003UL));

        Assert.Equal(SceneObjectType.Door, loaded[3002UL].ObjectType);
        Assert.Equal(SceneObjectStateBits.Reset, loaded[3003UL].StateBits);
    }

    // =======================================================================
    // 测试 4: SaveSingle_OverwritesExisting
    // =======================================================================
    /// <summary>
    /// 同一 (shardKey, objectId) 二次 SaveSingleAsync 应覆盖旧状态（upsert 语义）。
    /// </summary>
    [Fact]
    public async Task SaveSingle_OverwritesExisting()
    {
        var store = new InMemorySceneObjectPersistenceStore();
        var shardKey = 4001L;

        var original = new SceneObjectStateData
        {
            ObjectId = 4001, ShardKey = shardKey, ObjectType = SceneObjectType.Chest,
            StateBits = 0, CooldownEndTick = 0, OwnerCharacterId = 0,
        };
        await store.SaveSingleAsync(shardKey, original);

        var updated = new SceneObjectStateData
        {
            ObjectId = 4001, ShardKey = shardKey, ObjectType = SceneObjectType.Chest,
            StateBits = SceneObjectStateBits.Opened, CooldownEndTick = 300, OwnerCharacterId = 5001,
            UpdatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        await store.SaveSingleAsync(shardKey, updated);

        var loaded = await store.LoadWorldStateAsync(shardKey);
        Assert.Single(loaded);

        var restored = loaded[4001UL];
        Assert.Equal(SceneObjectStateBits.Opened, restored.StateBits);
        Assert.Equal(300, restored.CooldownEndTick);
        Assert.Equal(5001UL, restored.OwnerCharacterId);
        Assert.Equal(updated.UpdatedAt, restored.UpdatedAt);
    }

    // =======================================================================
    // 测试 5: DifferentShards_Isolated
    // =======================================================================
    /// <summary>
    /// 不同 shardKey 的状态应相互隔离：保存到 shard A 的状态不会出现在 shard B 的加载结果中。
    /// </summary>
    [Fact]
    public async Task DifferentShards_Isolated()
    {
        var store = new InMemorySceneObjectPersistenceStore();

        // shard 1 含 1 个对象
        await store.SaveSingleAsync(shardKey: 1, new SceneObjectStateData
        {
            ObjectId = 100, ShardKey = 1, ObjectType = SceneObjectType.Chest,
            StateBits = SceneObjectStateBits.Opened,
        });

        // shard 2 含 2 个对象
        await store.SaveWorldStateAsync(shardKey: 2, new[]
        {
            new SceneObjectStateData
            {
                ObjectId = 200, ShardKey = 2, ObjectType = SceneObjectType.Switch,
                StateBits = SceneObjectStateBits.Activated,
            },
            new SceneObjectStateData
            {
                ObjectId = 201, ShardKey = 2, ObjectType = SceneObjectType.Portal,
                StateBits = SceneObjectStateBits.Opened,
            },
        });

        var loaded1 = await store.LoadWorldStateAsync(shardKey: 1);
        var loaded2 = await store.LoadWorldStateAsync(shardKey: 2);
        var loaded3 = await store.LoadWorldStateAsync(shardKey: 3);

        Assert.Single(loaded1);
        Assert.Equal(100UL, loaded1.First().Key);

        Assert.Equal(2, loaded2.Count);
        Assert.True(loaded2.ContainsKey(200UL));
        Assert.True(loaded2.ContainsKey(201UL));
        Assert.False(loaded2.ContainsKey(100UL));

        Assert.Empty(loaded3);
    }

    // =======================================================================
    // 测试 6: AllFields_PreservedInRoundTrip
    // =======================================================================
    /// <summary>
    /// 全字段往返：Transform（X/Y/Z/Pitch/Yaw/Roll）、ObjectType、StateBits、CooldownEndTick、
    /// OwnerCharacterId、UpdatedAt 等所有字段在 Save→Load 后保持一致。
    /// </summary>
    [Fact]
    public async Task AllFields_PreservedInRoundTrip()
    {
        var store = new InMemorySceneObjectPersistenceStore();
        var shardKey = 6001L;
        var original = new SceneObjectStateData
        {
            ObjectId = 6001,
            ShardKey = shardKey,
            ObjectType = SceneObjectType.Portal,
            StateBits = SceneObjectStateBits.Opened | SceneObjectStateBits.Activated,
            CooldownEndTick = 12345,
            OwnerCharacterId = 99999,
            TransformX = 123.45f,
            TransformY = -67.89f,
            TransformZ = 0.5f,
            TransformPitch = 0.1f,
            TransformYaw = 1.57f,
            TransformRoll = -0.3f,
            UpdatedAt = new DateTime(2026, 6, 28, 15, 30, 45, DateTimeKind.Utc),
        };

        await store.SaveSingleAsync(shardKey, original);
        var loaded = await store.LoadWorldStateAsync(shardKey);

        Assert.Single(loaded);
        var restored = loaded[6001UL];

        Assert.Equal(original.ObjectId, restored.ObjectId);
        Assert.Equal(original.ShardKey, restored.ShardKey);
        Assert.Equal(original.ObjectType, restored.ObjectType);
        Assert.Equal(original.StateBits, restored.StateBits);
        Assert.Equal(original.CooldownEndTick, restored.CooldownEndTick);
        Assert.Equal(original.OwnerCharacterId, restored.OwnerCharacterId);
        Assert.Equal(original.TransformX, restored.TransformX);
        Assert.Equal(original.TransformY, restored.TransformY);
        Assert.Equal(original.TransformZ, restored.TransformZ);
        Assert.Equal(original.TransformPitch, restored.TransformPitch);
        Assert.Equal(original.TransformYaw, restored.TransformYaw);
        Assert.Equal(original.TransformRoll, restored.TransformRoll);
        Assert.Equal(original.UpdatedAt, restored.UpdatedAt);
    }

    // =======================================================================
    // 测试 7: SaveWorldState_OverwritesExistingShardEntries
    // =======================================================================
    /// <summary>
    /// SaveWorldStateAsync 对已存在的 (shardKey, objectId) 应 upsert（覆盖），
    /// 对 shard 内未在批次中出现的旧条目应保留（非 replace 语义）。
    /// 验证实现采用按 (shardKey, objectId) upsert 而非整 shard 替换。
    /// </summary>
    [Fact]
    public async Task SaveWorldState_OverwritesExistingShardEntries()
    {
        var store = new InMemorySceneObjectPersistenceStore();
        var shardKey = 7001L;

        // 初始保存 2 条
        await store.SaveWorldStateAsync(shardKey, new[]
        {
            new SceneObjectStateData { ObjectId = 7001, ShardKey = shardKey, StateBits = 0, ObjectType = SceneObjectType.Chest },
            new SceneObjectStateData { ObjectId = 7002, ShardKey = shardKey, StateBits = 0, ObjectType = SceneObjectType.Switch },
        });

        // 第二次保存：仅含 7001 的更新版本
        await store.SaveWorldStateAsync(shardKey, new[]
        {
            new SceneObjectStateData
            {
                ObjectId = 7001, ShardKey = shardKey, StateBits = SceneObjectStateBits.Opened,
                ObjectType = SceneObjectType.Chest, OwnerCharacterId = 123,
            },
        });

        var loaded = await store.LoadWorldStateAsync(shardKey);

        // 7002 仍应存在（upsert 而非 replace）
        Assert.Equal(2, loaded.Count);
        Assert.True(loaded.ContainsKey(7001UL));
        Assert.True(loaded.ContainsKey(7002UL));

        // 7001 应为更新版本
        Assert.Equal(SceneObjectStateBits.Opened, loaded[7001UL].StateBits);
        Assert.Equal(123UL, loaded[7001UL].OwnerCharacterId);
    }

    // =======================================================================
    // 测试 8: GrainInteract_WithStore_TriggersImmediatePersist
    // =======================================================================
    /// <summary>
    /// 集成测试：注入 InMemorySceneObjectPersistenceStore 到 ZoneShardGrain，
    /// HandleSceneObjectInteract 在 Opened/Activated 位变化时应触发 SaveSingleAsync。
    /// 验证 fire-and-forget 路径在内存 store 同步完成时被观察到。
    /// </summary>
    [Fact]
    public async Task GrainInteract_WithStore_TriggersImmediatePersist()
    {
        var store = new InMemorySceneObjectPersistenceStore();
        var grain = CreateGrain(store);
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        const ulong interactor = 8001;
        const ulong objectId = 8001;

        await grain.HandleSceneObjectInteract(interactor, objectId, SceneObjectStateBits.Opened);

        // fire-and-forget 在 InMemoryStore 同步完成时应已落盘
        var loaded = await store.LoadWorldStateAsync(shardKey: 1);
        Assert.Single(loaded);
        Assert.True(loaded.ContainsKey(objectId));

        var persisted = loaded[objectId];
        Assert.Equal(SceneObjectStateBits.Opened, persisted.StateBits);
        Assert.Equal(interactor, persisted.OwnerCharacterId);
        Assert.Equal(300, persisted.CooldownEndTick);
    }

    // =======================================================================
    // 测试 9: GrainInteract_ResetOnly_DoesNotTriggerImmediatePersist
    // =======================================================================
    /// <summary>
    /// 集成测试：仅 Reset 位（不含 Opened/Activated）不应触发即时落盘。
    /// 验证 InMemoryStore 中无对应记录。
    /// </summary>
    [Fact]
    public async Task GrainInteract_ResetOnly_DoesNotTriggerImmediatePersist()
    {
        var store = new InMemorySceneObjectPersistenceStore();
        var grain = CreateGrain(store);
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new ulong[] { 0 });

        await grain.HandleSceneObjectInteract(interactorId: 9001, objectId: 9001, SceneObjectStateBits.Reset);

        var loaded = await store.LoadWorldStateAsync(shardKey: 1);
        Assert.Empty(loaded);
    }

    // ===== 测试辅助：创建 ZoneShardGrain =====
    private static ZoneShardGrain CreateGrain(ISceneObjectPersistenceStore? persistence = null)
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object, persistence);

        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);

        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        return grain;
    }

    private sealed class FakeFanoutObserver : IZoneShardFanoutObserver
    {
        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds) => Task.CompletedTask;
    }
}

/// <summary>
/// Task C.7.4：ISceneObjectPersistenceStore 的内存实现，用于单元测试。
/// <para>
/// 按 (shardKey, objectId) 作为复合键存储，实现 upsert 语义；
/// LoadWorldStateAsync 返回指定 shardKey 下全部条目。
/// </para>
/// <para>
/// 该实现仅用于测试，不保证线程安全；与 <c>SqlServerSceneObjectPersistenceStore</c> 行为对齐：
/// <list type="bullet">
///   <item>SaveSingleAsync = 单条 upsert</item>
///   <item>SaveWorldStateAsync = 批量 upsert（不删除批次外的旧条目）</item>
///   <item>LoadWorldStateAsync = 按 shardKey 过滤返回全部</item>
/// </list>
/// </para>
/// </summary>
internal sealed class InMemorySceneObjectPersistenceStore : ISceneObjectPersistenceStore
{
    private readonly ConcurrentDictionary<(long ShardKey, ulong ObjectId), SceneObjectStateData> _store = new();

    /// <inheritdoc />
    public Task<Dictionary<ulong, SceneObjectStateData>> LoadWorldStateAsync(long shardKey)
    {
        var result = new Dictionary<ulong, SceneObjectStateData>();
        foreach (var kv in _store)
        {
            if (kv.Key.ShardKey != shardKey) continue;
            // 深拷贝避免外部修改污染内部状态
            result[kv.Value.ObjectId] = Clone(kv.Value);
        }
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task SaveWorldStateAsync(long shardKey, IEnumerable<SceneObjectStateData> states)
    {
        foreach (var state in states)
        {
            var key = (shardKey, state.ObjectId);
            _store[key] = Clone(state);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveSingleAsync(long shardKey, SceneObjectStateData state)
    {
        var key = (shardKey, state.ObjectId);
        _store[key] = Clone(state);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 深拷贝 SceneObjectStateData（值类型字段直接复制，避免外部修改测试断言）。
    /// </summary>
    private static SceneObjectStateData Clone(SceneObjectStateData s) => new()
    {
        ObjectId = s.ObjectId,
        ShardKey = s.ShardKey,
        ObjectType = s.ObjectType,
        StateBits = s.StateBits,
        CooldownEndTick = s.CooldownEndTick,
        OwnerCharacterId = s.OwnerCharacterId,
        TransformX = s.TransformX,
        TransformY = s.TransformY,
        TransformZ = s.TransformZ,
        TransformPitch = s.TransformPitch,
        TransformYaw = s.TransformYaw,
        TransformRoll = s.TransformRoll,
        UpdatedAt = s.UpdatedAt,
    };
}
