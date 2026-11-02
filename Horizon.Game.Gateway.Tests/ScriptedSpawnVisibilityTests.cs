using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using Horizon.Game.Message.Sync;
using System;
using System.Linq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 脚本生成物在**可见性机制**里的表现（设计方案 §7 承诺：复用刷怪调度 ⇒ AOI 裁剪、
/// 晚加入补发、到期全广播三条都自动成立）。
/// <para>
/// 这三条此前只在静态刷怪点上有测试，脚本实体是"应该也成立"的推断。推断不算证据：
/// 一旦脚本实体走了另一条身份/快照分支，表现就是"晚进场的玩家永远看不见这批怪"
/// 或"怪到期消失了但客户端还留着尸体"，都是玩家能直接看见的事故。
/// </para>
/// </summary>
public class ScriptedSpawnVisibilityTests
{
    private const long OperatorId = 7001;
    private const long ShardId = 0;
    private static readonly byte[] Key = Enumerable.Repeat((byte)0x37, 32).ToArray();

    private static DsWorldSession MakeSession(bool enableAoi, int chunkSize = 16, int radiusChunks = 28)
    {
        var session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = ShardId,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = true,
            ScriptedSigningKey = Key,
            EnableAoiFilter = enableAoi,
            AoiChunkSize = chunkSize,
            AoiRadiusChunks = radiusChunks,
        });
        Assert.True(session.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
        return session;
    }

    private static ScriptGrantToken Token(long nonce, float x, float y, float z, int count = 2, int ttlSeconds = 0)
    {
        var token = new ScriptGrantToken
        {
            Nonce = nonce,
            OperatorCharacterId = OperatorId,
            Op = ScriptOp.Spawn,
            Kind = ScriptedKind.Monster,
            TemplateId = 1,
            TargetShardId = ShardId,
            X = x,
            Y = y,
            Z = z,
            Count = count,
            TtlSeconds = ttlSeconds,
            RequestId = nonce,
        };
        ScriptGrantTokenSigner.Sign(token, Key, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 30);
        return token;
    }

    private static bool IsScripted(ulong entityId) => (int)(entityId >> 32) < 0;

    private static ulong[] ScriptedSpawns(DsTickResult tick) => tick.WorldSnapshot?.Deltas
        .Where(d => d.Kind == EntityDeltaKind.Spawn && IsScripted(d.EntityId))
        .Select(d => d.EntityId).ToArray() ?? Array.Empty<ulong>();

    /// <summary>收集"某会话在某 tick 里被下发的脚本 Spawn 实体"。</summary>
    private static ulong[] ScriptedSpawnsFor(DsTickResult tick, long sessionId)
    {
        var ids = new System.Collections.Generic.HashSet<ulong>();

        void Collect(SnapshotPacket? snapshot, long[] audience)
        {
            if (snapshot?.Deltas is null) return;
            if (!audience.Contains(sessionId)) return;
            foreach (var d in snapshot.Deltas)
            {
                if (d.Kind == EntityDeltaKind.Spawn && IsScripted(d.EntityId)) ids.Add(d.EntityId);
            }
        }

        Collect(tick.WorldSnapshot, tick.AudienceSessionIds);
        foreach (var group in tick.SnapshotGroups ?? Array.Empty<DsSnapshotGroup>())
        {
            Collect(group.Snapshot, group.AudienceSessionIds);
        }
        foreach (var initial in tick.InitialSpawnSnapshots ?? Array.Empty<DsInitialSpawnSnapshot>())
        {
            if (initial.SessionId == sessionId && initial.Snapshot?.Deltas != null)
            {
                foreach (var d in initial.Snapshot.Deltas)
                {
                    if (d.Kind == EntityDeltaKind.Spawn && IsScripted(d.EntityId)) ids.Add(d.EntityId);
                }
            }
        }

        return ids.ToArray();
    }

    [Fact]
    public void 脚本实体同样受AOI裁剪_视野外的不塞给玩家()
    {
        // chunk=16m、半径 1 块 ⇒ 订阅范围约 ±32m。两个独立场景，避免靠猜 ID 分辨远近。
        var near = MakeSession(enableAoi: true, chunkSize: 16, radiusChunks: 1);
        near.Tick();
        Assert.Null(near.EnqueueScriptedCommand(Token(5001, 10f, 0f, 0f, count: 1)));

        var far = MakeSession(enableAoi: true, chunkSize: 16, radiusChunks: 1);
        far.Tick();
        Assert.Null(far.EnqueueScriptedCommand(Token(5002, 600f, 0f, 0f, count: 1)));

        ulong[] nearSeen = Array.Empty<ulong>();
        ulong[] farSeen = Array.Empty<ulong>();
        for (var i = 0; i < 40; i++)
        {
            nearSeen = nearSeen.Concat(ScriptedSpawnsFor(near.Tick(), 1001)).Distinct().ToArray();
            farSeen = farSeen.Concat(ScriptedSpawnsFor(far.Tick(), 1001)).Distinct().ToArray();
        }

        Assert.Single(nearSeen);   // 视野内的正常下发
        Assert.Empty(farSeen);      // 视野外的一个都没进

        // 远处那批确实存在（生成点已建立），只是不被这个玩家看见——
        // 区分"被裁剪"与"根本没生成"，否则 AOI 判错会伪装成生成失败。
        Assert.Equal(1, far.ScriptedSpawnPointCount);
        Assert.True(far.MonsterCount >= 1, $"远处怪应存在于世界，实际 MonsterCount={far.MonsterCount}");
    }

    [Fact]
    public void 晚加入的玩家能补发到已存在的脚本实体()
    {
        var session = MakeSession(enableAoi: false);
        session.Tick();

        Assert.Null(session.EnqueueScriptedCommand(Token(5101, 5f, 5f, 0f, count: 3)));
        ulong[] spawned = Array.Empty<ulong>();
        for (var i = 0; i < 20 && spawned.Length == 0; i++)
        {
            spawned = ScriptedSpawns(session.Tick());
        }
        Assert.Equal(3, spawned.Length);

        // 新人此刻才进场：必须通过"首包补发"拿到已经存在的脚本实体，
        // 否则他会一直看不见这批怪（本仓 DS 通道踩过的老坑）。
        Assert.True(session.AddPlayer(1002, 1002, 0f, 0f, 0f, out var addReason), addReason);
        var joinTick = session.Tick();

        var forNewcomer = Assert.Single(joinTick.InitialSpawnSnapshots!.Where(s => s.SessionId == 1002));
        var catchUp = forNewcomer.Snapshot.Deltas
            .Where(d => d.Kind == EntityDeltaKind.Spawn && IsScripted(d.EntityId))
            .Select(d => d.EntityId)
            .OrderBy(id => id)
            .ToArray();
        Assert.Equal(spawned.OrderBy(id => id).ToArray(), catchUp);
    }

    [Fact]
    public void 到期清除对全体受众全广播_不留客户端尸体()
    {
        var session = MakeSession(enableAoi: false);
        Assert.True(session.AddPlayer(1003, 1003, 100f, 0f, 0f, out var r2), r2);
        session.Tick();

        Assert.Null(session.EnqueueScriptedCommand(Token(5201, 3f, 3f, 0f, count: 2, ttlSeconds: 1)));
        ulong[] spawned = Array.Empty<ulong>();
        for (var i = 0; i < 30 && spawned.Length == 0; i++)
        {
            spawned = ScriptedSpawns(session.Tick());
        }
        Assert.Equal(2, spawned.Length);

        // 存活期内不得提前清除
        var alive = session.Tick();
        Assert.DoesNotContain(alive.WorldSnapshot!.Deltas,
            d => d.Kind == EntityDeltaKind.Despawn && IsScripted(d.EntityId));

        // 1 秒 TTL 到点后，Despawn 走**全广播**通道（不做 AOI 裁剪，项目铁律：漏发就留幽灵），
        // 且只在到期那一个 tick 出现一次，所以必须逐 tick 累积，而不是只看最后一次结果。
        var despawned = new System.Collections.Generic.HashSet<ulong>();
        long[]? despawnAudience = null;
        for (var i = 0; i < 200 && despawned.Count == 0; i++)
        {
            var tick = session.Tick();
            if (tick.DespawnBroadcast?.Deltas is null) continue;
            var hits = tick.DespawnBroadcast.Deltas
                .Where(d => IsScripted(d.EntityId))
                .Select(d => d.EntityId)
                .ToArray();
            if (hits.Length == 0) continue;

            foreach (var id in hits) despawned.Add(id);
            despawnAudience = tick.DespawnBroadcastAudience;
        }

        Assert.Equal(spawned.OrderBy(id => id).ToArray(), despawned.OrderBy(id => id).ToArray());
        // 两位玩家都要收到——包括从没看见过这批怪的那位（否则他的客户端会留永久尸体）。
        Assert.NotNull(despawnAudience);
        Assert.Equal(new[] { 1001L, 1003L }, despawnAudience!.OrderBy(s => s).ToArray());
    }
}
