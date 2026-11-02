using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Core.Security;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using Horizon.Game.Message.Sync;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 脚本化世界生成（DS 怪物/NPC/特殊事件 + Grain 权威授权）的裁决与执行测试。
/// <para>锁定四条不变量：</para>
/// <list type="number">
/// <item>权限矩阵：<b>只有</b>对应权限位才可通过，且数值判定不因持位而放大；</item>
/// <item>令牌自证：篡改/过期/未来签发/缺密钥一律不通过，DS 侧还挡重放与跨 shard；</item>
/// <item>实体 ID 空间：脚本生成点用负数域，与所有静态刷怪点<b>永不</b>相交；</item>
/// <item>生命周期：TTL 到点后该实体从世界消失并产生 Despawn delta。</item>
/// </list>
/// </summary>
public class ScriptedSpawnAuthorityTests
{
    private static readonly byte[] Key = Enumerable.Repeat((byte)0x5A, 32).ToArray();
    private const long OperatorId = 7001;
    private const long ShardId = 3;

    private static ScriptedSpawnOptions MakeOptions(Action<ScriptedSpawnOptions>? mutate = null)
    {
        var o = new ScriptedSpawnOptions { Enabled = true, SigningKeyBase64 = Convert.ToBase64String(Key) };
        mutate?.Invoke(o);
        return o;
    }

    private static ScriptOperatorState Authorized(uint flags) => new()
    {
        Flags = flags,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
        GrantedAtUtc = DateTime.UtcNow,
        GrantedBy = 1,
    };

    private static GmScriptTicket SpawnTicket(
        ScriptedKind kind = ScriptedKind.Monster,
        string templateKey = "墨麒麟",
        int count = 1,
        bool hasPosition = false,
        float x = 0f, float y = 0f, float z = 0f,
        int ttlSeconds = 0,
        float radius = 0f,
        ScriptTriggerMode triggerMode = ScriptTriggerMode.Once,
        int cooldownSeconds = 0) => new()
        {
            OperatorCharacterId = OperatorId,
            RequestId = 1,
            Op = ScriptOp.Spawn,
            Kind = kind,
            TemplateKey = templateKey,
            Count = count,
            HasPosition = hasPosition,
            X = x,
            Y = y,
            Z = z,
            TtlSeconds = ttlSeconds,
            Radius = radius,
            TriggerMode = triggerMode,
            CooldownSeconds = cooldownSeconds,
            OperatorShardId = ShardId,
        };

    private static ScriptEvalContext Ctx(
        GmScriptTicket ticket,
        float operatorX = 0f, float operatorZ = 0f,
        int shardEntityCount = 0,
        int shardTriggerCount = 0,
        long? nowUnixMs = null)
    {
        var nowMs = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new ScriptEvalContext
        {
            Ticket = ticket,
            OperatorX = operatorX,
            OperatorY = 0f,
            OperatorZ = operatorZ,
            OperatorShardId = ShardId,
            ShardScriptedEntityCount = shardEntityCount,
            ShardTriggerZoneCount = shardTriggerCount,
            NowUtc = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime,
            NowUnixMs = nowMs,
        };
    }

    // ---------- 1. 权限矩阵 ----------

    [Theory]
    [InlineData((uint)ScriptPermissionFlags.SpawnMonster, true)]
    [InlineData((uint)ScriptPermissionFlags.SpawnNpc, false)]
    [InlineData((uint)ScriptPermissionFlags.Query, false)]
    [InlineData((uint)(ScriptPermissionFlags.SpawnMonster | ScriptPermissionFlags.Query), true)]
    public void 怪物生成只在持SpawnMonster位时通过(uint flags, bool expected)
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var verdict = core.Evaluate(Authorized(flags), Ctx(SpawnTicket()), Key);
        Assert.Equal(expected, verdict.Allowed);
        if (!expected) Assert.Equal(ScriptRejectReason.InsufficientPermission, verdict.Reason);
    }

    /// <summary>
    /// 清除操作的权限位按**目标归属**分档：按 ID 清除可能是别人的生成物，必须持 DespawnAny；
    /// 只清自己的走 DespawnMine，持 DespawnOwned 即可。两档互换必须都不通过，
    /// 否则"拿到清自己的运营能把别人摆的内容清空"就成了事实。
    /// </summary>
    [Theory]
    [InlineData(ScriptOp.Despawn, (uint)ScriptPermissionFlags.DespawnOwned, false)]
    [InlineData(ScriptOp.Despawn, (uint)ScriptPermissionFlags.DespawnAny, true)]
    [InlineData(ScriptOp.DespawnMine, (uint)ScriptPermissionFlags.DespawnAny, false)]
    [InlineData(ScriptOp.DespawnMine, (uint)ScriptPermissionFlags.DespawnOwned, true)]
    public void 清除按归属分档_按ID需要DespawnAny(ScriptOp op, uint flags, bool expected)
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var ticket = new GmScriptTicket
        {
            OperatorCharacterId = OperatorId,
            RequestId = 1,
            Op = op,
            OperatorShardId = ShardId,
        };
        if (op == ScriptOp.Despawn) ticket.TargetEntityId = 0xFFFFFFFF_00000002UL;

        var verdict = core.Evaluate(Authorized(flags), Ctx(ticket), Key);
        Assert.Equal(expected, verdict.Allowed);
        if (!expected) Assert.Equal(ScriptRejectReason.InsufficientPermission, verdict.Reason);
    }

    /// <summary>
    /// 特殊事件要哪一位由**服务端目录里的形态**决定，不由指令怎么写决定：
    /// 只放表现的要 FireEvent，会往世界里放怪群的要 SpawnEvent。
    /// 否则一个清屏特效和一场世界事件共用同一档权限。
    /// </summary>
    [Theory]
    [InlineData("天空异象", (uint)ScriptPermissionFlags.FireEvent, true)]
    [InlineData("天空异象", (uint)ScriptPermissionFlags.SpawnEvent, false)]
    [InlineData("墨麒麟降世", (uint)ScriptPermissionFlags.FireEvent, false)]
    [InlineData("墨麒麟降世", (uint)ScriptPermissionFlags.SpawnEvent, true)]
    public void 事件所需权限位由目录形态决定(string templateKey, uint flags, bool expected)
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var ticket = SpawnTicket(kind: ScriptedKind.Event, templateKey: templateKey, ttlSeconds: 30);

        var verdict = core.Evaluate(Authorized(flags), Ctx(ticket), Key);
        Assert.Equal(expected, verdict.Allowed);
        if (!expected) Assert.Equal(ScriptRejectReason.InsufficientPermission, verdict.Reason);
    }

    [Fact]
    public void 目录解析不出事件时不降档到FireEvent_权限不因目录缺失而变松()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var ticket = SpawnTicket(kind: ScriptedKind.Event, templateKey: "尚未登记的事件", ttlSeconds: 30);

        // 只持 FireEvent：形态未知 ⇒ 按"会建实体"的较高档要求，先拒权限而不是放行后再认模板。
        Assert.Equal(ScriptRejectReason.InsufficientPermission,
            core.Evaluate(Authorized((uint)ScriptPermissionFlags.FireEvent), Ctx(ticket), Key).Reason);
        // 持 SpawnEvent 时才走到第 5 步，报出操作者真正该修的东西。
        Assert.Equal(ScriptRejectReason.UnknownTemplate,
            core.Evaluate(Authorized((uint)ScriptPermissionFlags.SpawnEvent), Ctx(ticket), Key).Reason);
    }

    [Fact]
    public void 权限位不足的回执点名缺哪一位()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var verdict = core.Evaluate(
            Authorized((uint)ScriptPermissionFlags.SpawnMonster),
            Ctx(new GmScriptTicket
            {
                OperatorCharacterId = OperatorId,
                RequestId = 1,
                Op = ScriptOp.Despawn,
                TargetEntityId = 0xFFFFFFFF_00000002UL,
                OperatorShardId = ShardId,
            }), Key);

        Assert.False(verdict.Allowed);
        Assert.Contains("DespawnAny", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 无授权记录直接拒绝()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var verdict = core.Evaluate(new ScriptOperatorState(), Ctx(SpawnTicket()), Key);
        Assert.False(verdict.Allowed);
        Assert.Equal(ScriptRejectReason.NoGrant, verdict.Reason);
    }

    [Fact]
    public void 总开关关闭一律拒绝()
    {
        var core = new ScriptAuthorityCore(MakeOptions(o => o.Enabled = false), DefaultScriptTemplateCatalog.Instance);
        Assert.Equal(ScriptRejectReason.Disabled, core.Evaluate(Authorized((uint)ScriptPermissionFlags.GameMaster), Ctx(SpawnTicket()), Key).Reason);
    }

    [Fact]
    public void 缺密钥一律拒绝_不得跳过验签()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var verdict = core.Evaluate(Authorized((uint)ScriptPermissionFlags.GameMaster), Ctx(SpawnTicket()), null);
        Assert.False(verdict.Allowed);
        Assert.Equal(ScriptRejectReason.Internal, verdict.Reason);
    }

    [Fact]
    public void 授权过期拒绝()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var state = Authorized((uint)ScriptPermissionFlags.GameMaster);
        state.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        Assert.Equal(ScriptRejectReason.Expired, core.Evaluate(state, Ctx(SpawnTicket()), Key).Reason);
    }

    [Fact]
    public void 数量大于1需要MultiEntity位()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var verdict = core.Evaluate(
            Authorized((uint)ScriptPermissionFlags.SpawnMonster),
            Ctx(SpawnTicket(count: 3)),
            Key);
        Assert.Equal(ScriptRejectReason.InsufficientPermission, verdict.Reason);

        var withMulti = core.Evaluate(
            Authorized((uint)(ScriptPermissionFlags.SpawnMonster | ScriptPermissionFlags.MultiEntity)),
            Ctx(SpawnTicket(count: 3)),
            Key);
        Assert.True(withMulti.Allowed);
        Assert.Equal(3, withMulti.Token!.Count);
    }

    [Fact]
    public void 单次数量受硬顶裁剪()
    {
        var core = new ScriptAuthorityCore(MakeOptions(o => o.MaxCountPerRequest = 5), DefaultScriptTemplateCatalog.Instance);
        var verdict = core.Evaluate(
            Authorized((uint)(ScriptPermissionFlags.SpawnMonster | ScriptPermissionFlags.MultiEntity)),
            Ctx(SpawnTicket(count: 999)),
            Key);
        Assert.True(verdict.Allowed);
        Assert.Equal(5, verdict.Token!.Count);
    }

    [Theory]
    [InlineData("墨麒麟", true)]
    [InlineData("mo_qilin", true)]
    [InlineData("1", true)]
    [InlineData("不存在的怪", false)]
    [InlineData("", false)]
    public void 模板白名单只认已登记目录(string key, bool expected)
    {
        Assert.Equal(expected, DefaultScriptTemplateCatalog.Instance.TryResolve(ScriptedKind.Monster, key, out var id));
        if (expected) Assert.True(id >= 1);
    }

    [Fact]
    public void 未登记模板的指令被拒()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var verdict = core.Evaluate(
            Authorized((uint)ScriptPermissionFlags.SpawnMonster),
            Ctx(SpawnTicket(templateKey: "世界BOSS_虚空")),
            Key);
        Assert.Equal(ScriptRejectReason.UnknownTemplate, verdict.Reason);
    }

    [Fact]
    public void NPC目录未登记前一律拒绝_不给未登记内容开门()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var verdict = core.Evaluate(
            Authorized((uint)ScriptPermissionFlags.SpawnNpc),
            Ctx(SpawnTicket(kind: ScriptedKind.Npc, templateKey: "铁匠")),
            Key);
        Assert.Equal(ScriptRejectReason.UnknownTemplate, verdict.Reason);
    }

    [Fact]
    public void 超距拒绝_持BypassDist可越()
    {
        var core = new ScriptAuthorityCore(MakeOptions(o => o.MaxDistanceMetres = 50f), DefaultScriptTemplateCatalog.Instance);
        var far = Ctx(SpawnTicket(hasPosition: true, x: 400f, z: 0f), operatorX: 0f);
        Assert.Equal(ScriptRejectReason.TooFar, core.Evaluate(Authorized((uint)ScriptPermissionFlags.SpawnMonster), far, Key).Reason);

        var bypass = Authorized((uint)(ScriptPermissionFlags.SpawnMonster | ScriptPermissionFlags.BypassDist));
        Assert.True(core.Evaluate(bypass, far, Key).Allowed);
    }

    [Fact]
    public void 服务端无权威位置时拒绝生成_距离基准不得由客户端供给()
    {
        var core = new ScriptAuthorityCore(MakeOptions(o => o.MaxDistanceMetres = 50f), DefaultScriptTemplateCatalog.Instance);

        // 客户端自报一个"离世界原点很近"的点：若位置缺失时把基准当 (0,0,0)，这条指令就会被误放行。
        var ctx = Ctx(SpawnTicket(hasPosition: true, x: 1f, y: 0f, z: 1f), operatorX: 0f);
        ctx.OperatorPositionKnown = false;

        var verdict = core.Evaluate(Authorized((uint)ScriptPermissionFlags.SpawnMonster), ctx, Key);

        Assert.False(verdict.Allowed);
        Assert.Equal(ScriptRejectReason.InvalidArgument, verdict.Reason);
        Assert.Contains("权威位置", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 缺位置的拒绝不消耗冷却与配额()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var state = Authorized((uint)ScriptPermissionFlags.SpawnMonster);
        var ctx = Ctx(SpawnTicket());
        ctx.OperatorPositionKnown = false;

        Assert.False(core.Evaluate(state, ctx, Key).Allowed);
        Assert.Equal(0L, state.LastAcceptedUnixMs);
        Assert.Equal(0, state.QuotaWindowUsed);
        Assert.Equal(0L, state.IssuedTokenCounter);
    }

    [Fact]
    public void 缺位置只拦生成类操作_查询与按ID清除不受影响()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);

        var queryCtx = Ctx(new GmScriptTicket
        {
            OperatorCharacterId = OperatorId,
            RequestId = 1,
            Op = ScriptOp.Query,
            OperatorShardId = ShardId,
        });
        queryCtx.OperatorPositionKnown = false;
        Assert.True(core.Evaluate(Authorized((uint)ScriptPermissionFlags.Query), queryCtx, Key).Allowed);

        var despawnCtx = Ctx(new GmScriptTicket
        {
            OperatorCharacterId = OperatorId,
            RequestId = 1,
            Op = ScriptOp.Despawn,
            TargetEntityId = 0xFFFFFFFF_00000002UL,
            OperatorShardId = ShardId,
        });
        despawnCtx.OperatorPositionKnown = false;
        // 持齐清除位：这里要的是"走到裁决末尾并放行"，而不是"停在权限位检查上假绿"。
        var verdict = core.Evaluate(
            Authorized((uint)(ScriptPermissionFlags.DespawnOwned | ScriptPermissionFlags.DespawnAny)),
            despawnCtx, Key);
        Assert.True(verdict.Allowed);
        Assert.DoesNotContain("权威位置", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 冷却内二次指令被拒()
    {
        var core = new ScriptAuthorityCore(MakeOptions(o => o.CooldownSeconds = 5), DefaultScriptTemplateCatalog.Instance);
        var state = Authorized((uint)ScriptPermissionFlags.SpawnMonster);
        var t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.True(core.Evaluate(state, Ctx(SpawnTicket(), nowUnixMs: t0), Key).Allowed);
        var second = core.Evaluate(state, Ctx(SpawnTicket(), nowUnixMs: t0 + 1000), Key);
        Assert.Equal(ScriptRejectReason.Cooldown, second.Reason);

        Assert.True(core.Evaluate(state, Ctx(SpawnTicket(), nowUnixMs: t0 + 6000), Key).Allowed);
    }

    [Fact]
    public void 分钟配额用尽后拒绝_窗口滚动后恢复()
    {
        var core = new ScriptAuthorityCore(MakeOptions(o => { o.QuotaPerMinute = 3; o.CooldownSeconds = 0; }), DefaultScriptTemplateCatalog.Instance);
        var state = Authorized((uint)ScriptPermissionFlags.SpawnMonster);
        var t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (var i = 0; i < 3; i++)
            Assert.True(core.Evaluate(state, Ctx(SpawnTicket(), nowUnixMs: t0 + i), Key).Allowed);

        Assert.Equal(ScriptRejectReason.QuotaExhausted, core.Evaluate(state, Ctx(SpawnTicket(), nowUnixMs: t0 + 3), Key).Reason);
        Assert.True(core.Evaluate(state, Ctx(SpawnTicket(), nowUnixMs: t0 + 61_000), Key).Allowed);
    }

    /// <summary>
    /// 分片实体上限是<b>硬顶</b>：<c>BypassDist</c> 只管距离，不能顺手把防洪闸关掉
    /// （触发区域上限同样无条件，两处口径一致）。
    /// </summary>
    [Theory]
    [InlineData((uint)ScriptPermissionFlags.SpawnMonster)]
    [InlineData((uint)(ScriptPermissionFlags.SpawnMonster | ScriptPermissionFlags.BypassDist))]
    public void shard实体预算上限拒绝_持BypassDist也不例外(uint flags)
    {
        var core = new ScriptAuthorityCore(MakeOptions(o => o.MaxScriptedEntitiesPerShard = 200), DefaultScriptTemplateCatalog.Instance);
        var verdict = core.Evaluate(
            Authorized(flags),
            Ctx(SpawnTicket(), shardEntityCount: 200),
            Key);
        Assert.Equal(ScriptRejectReason.EntityBudgetExceeded, verdict.Reason);
    }

    // ---------- 触发区域（特殊事件第三态）的裁决口径 ----------

    [Fact]
    public void 触发区域需PlaceTrigger位且半径与TTL受限()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var ticket = SpawnTicket(kind: ScriptedKind.TriggerZone, ttlSeconds: 120, radius: 10f);

        Assert.Equal(ScriptRejectReason.InsufficientPermission,
            core.Evaluate(Authorized((uint)ScriptPermissionFlags.SpawnMonster), Ctx(ticket), Key).Reason);

        var flags = (uint)(ScriptPermissionFlags.PlaceTrigger | ScriptPermissionFlags.SpawnMonster);

        // NPC/事件目录尚未登记，但触发区域走同一目录：先只用"参数校验"路径验证边界。
        Assert.Equal(ScriptRejectReason.InvalidArgument,
            core.Evaluate(Authorized(flags), Ctx(SpawnTicket(kind: ScriptedKind.TriggerZone, radius: 0f, ttlSeconds: 120)), Key).Reason);
        Assert.Equal(ScriptRejectReason.InvalidArgument,
            core.Evaluate(Authorized(flags), Ctx(SpawnTicket(kind: ScriptedKind.TriggerZone, radius: 999f, ttlSeconds: 120)), Key).Reason);
        // 永久触发区域 = 隐形刷怪机器，必须拒。
        Assert.Equal(ScriptRejectReason.InvalidArgument,
            core.Evaluate(Authorized(flags), Ctx(SpawnTicket(kind: ScriptedKind.TriggerZone, radius: 10f, ttlSeconds: 0)), Key).Reason);
        // Repeat 冷却为 0 会被边界横跳刷爆。
        Assert.Equal(ScriptRejectReason.InvalidArgument,
            core.Evaluate(Authorized(flags),
                Ctx(SpawnTicket(kind: ScriptedKind.TriggerZone, radius: 10f, ttlSeconds: 120, triggerMode: ScriptTriggerMode.Repeat, cooldownSeconds: 0)),
                Key).Reason);
    }

    // ---------- 2. 令牌自证 ----------

    private static ScriptGrantToken Modify(ScriptGrantToken token, Action<ScriptGrantToken> mutate)
    {
        mutate(token);
        return token;
    }

    private static ScriptGrantToken MakeToken(long issuedMs, int ttlSeconds = 5, long nonce = 12345)
    {
        var token = new ScriptGrantToken
        {
            Nonce = nonce,
            OperatorCharacterId = OperatorId,
            Op = ScriptOp.Spawn,
            Kind = ScriptedKind.Monster,
            TemplateId = 1,
            TargetShardId = ShardId,
            X = 1f,
            Y = 0f,
            Z = 2f,
            Count = 2,
            TtlSeconds = 60,
        };
        ScriptGrantTokenSigner.Sign(token, Key, issuedMs, ttlSeconds);
        return token;
    }

    [Fact]
    public void 令牌签名可验_篡改即失效()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var token = MakeToken(now);
        Assert.True(ScriptGrantTokenSigner.Validate(token, Key, now + 1000, out _));

        token.Count = 999; // 签发后改数量
        Assert.False(ScriptGrantTokenSigner.Validate(token, Key, now + 1000, out var reason));
        Assert.Equal("signature-mismatch", reason);
    }

    [Fact]
    public void 令牌过期与未来签发均拒绝()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expired = MakeToken(now - 60_000, ttlSeconds: 5);
        Assert.False(ScriptGrantTokenSigner.Validate(expired, Key, now, out var r1));
        Assert.Equal("expired", r1);

        var future = MakeToken(now + 600_000);
        Assert.False(ScriptGrantTokenSigner.Validate(future, Key, now, out var r2));
        Assert.Equal("issued-in-future", r2);
    }

    [Fact]
    public void 密钥不同则验签失败_跨环境令牌不可复用()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var token = MakeToken(now);
        Assert.False(ScriptGrantTokenSigner.Validate(token, Enumerable.Repeat((byte)0x11, 32).ToArray(), now + 100, out var reason));
        Assert.Equal("signature-mismatch", reason);
    }

    [Fact]
    public void 无密钥时fail_closed()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.False(ScriptGrantTokenSigner.Validate(MakeToken(now), Array.Empty<byte>(), now + 100, out var reason));
        Assert.Equal("missing-key", reason);
    }

    // ---------- 3+4. DS 侧执行：ID 空间与生命周期 ----------

    private static (DsWorldSession session, byte[] key) MakeDsSession(long shard = ShardId)
    {
        var session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = shard,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = true,
            ScriptedSigningKey = Key,
        });

        // 快照只在有受众时才构建（一份字节多受众的既有不变量），故测试里必须至少有一个在线会话。
        Assert.True(session.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
        return (session, Key);
    }

    /// <summary>
    /// 签发后再改字段必然 signature-mismatch——这正是防篡改要拦的事。
    /// 测试需要变体令牌时，改完必须重新签发。
    /// </summary>
    private static ScriptGrantToken Resign(ScriptGrantToken token, long nowMs)
    {
        ScriptGrantTokenSigner.Sign(token, Key, nowMs, 5);
        return token;
    }

    [Fact]
    public void 脚本指令入队并在同一tick出怪()
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.Null(session.EnqueueScriptedCommand(MakeToken(now)));

        var result = session.Tick();
        Assert.True(result.SnapshotDue);
        // 只统计"脚本域"的新增实体：在线玩家自身也会以 Spawn 出现，但它落在正数/零域。
        var spawned = result.WorldSnapshot!.Deltas
            .Where(d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0)
            .ToArray();
        Assert.Equal(2, spawned.Length); // Count=2
        Assert.Equal(1, session.ScriptedAcceptedCount); // 计数单位是"指令条数"，不是实体数

        // 实体 ID 落在负数域：高 32 位为 (uint)(-serial)
        foreach (var d in spawned)
            Assert.True((int)(d.EntityId >> 32) < 0, $"脚本实体 {d.EntityId} 未落在负数 spawnPointId 域");
    }

    [Fact]
    public void 脚本实体ID与静态刷怪点永不相交()
    {
        // 静态点用正 ID；脚本点用负 ID。同一表达式产出的两个域不得重叠。
        for (var sp = 1; sp <= 50; sp++)
        {
            for (var slot = 0; slot < 4; slot++)
            {
                var staticId = Horizon.Game.Sim.SpawnDirectorCore.ComputeEntityId(sp, slot);
                var scriptedId = Horizon.Game.Sim.SpawnDirectorCore.ComputeEntityId(-sp, slot);
                Assert.NotEqual(staticId, scriptedId);
                Assert.True((int)(staticId >> 32) > 0);
                Assert.True((int)(scriptedId >> 32) < 0);
            }
        }
    }

    [Fact]
    public void DS承载多shard时非首片令牌也应受理_未声明的片仍拒()
    {
        // 一个 DS 进程可向注册表声明承载多片（多片共用同一个世界实例），
        // 此时只认 ShardIds[0] 会把除首片以外的一切合法指令判成 wrong-shard 全量拒绝。
        var session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 1,
            HostedShardIds = new long[] { 1, 2, 3 },
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = true,
            ScriptedSigningKey = Key,
        });
        Assert.True(session.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var secondShard = MakeToken(now, nonce: 8101);
        secondShard.TargetShardId = 2;
        secondShard.Signature = ScriptGrantTokenSigner.ComputeSignature(secondShard, Key);
        Assert.Null(session.EnqueueScriptedCommand(secondShard));

        var undeclared = MakeToken(now, nonce: 8102);
        undeclared.TargetShardId = 4;
        undeclared.Signature = ScriptGrantTokenSigner.ComputeSignature(undeclared, Key);
        Assert.Equal("wrong-shard:4", session.EnqueueScriptedCommand(undeclared));

        // "受理"必须落到世界里，而不是只进队列。
        var result = session.Tick();
        Assert.Contains(result.WorldSnapshot!.Deltas,
            d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0);
    }

    [Fact]
    public void DS拒绝跨shard与重放与未签名指令()
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 跨 shard
        var wrongShard = MakeToken(now);
        wrongShard.TargetShardId = 99;
        wrongShard.Signature = ScriptGrantTokenSigner.ComputeSignature(wrongShard, Key); // 合法签名，仅 shard 不符
        Assert.Equal("wrong-shard:99", session.EnqueueScriptedCommand(wrongShard));

        // 未签名
        var unsigned = MakeToken(now, nonce: 555);
        unsigned.Signature = Array.Empty<byte>();
        Assert.Equal("bad-signature-length", session.EnqueueScriptedCommand(unsigned));

        // 重放：同一 Nonce 第二次必须被拒
        var first = MakeToken(now, nonce: 777);
        Assert.Null(session.EnqueueScriptedCommand(first));
        var replay = MakeToken(now, nonce: 777);
        Assert.Equal("replayed", session.EnqueueScriptedCommand(replay));

        Assert.True(session.ScriptedRejectedCount >= 3);
    }

    [Fact]
    public void DS开关关闭时拒绝一切脚本指令()
    {
        var session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = ShardId,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = false,
            ScriptedSigningKey = Key,
        });
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.Equal("scripted-disabled", session.EnqueueScriptedCommand(MakeToken(now)));
    }

    [Fact]
    public void TTL到点后实体从世界消失并产生Despawn()
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var token = Resign(Modify(MakeToken(now), t => t.TtlSeconds = 1), now); // TTL 1 秒
        Assert.Null(session.EnqueueScriptedCommand(token));

        var first = session.Tick();
        var scriptedSpawns = first.WorldSnapshot!.Deltas
            .Where(d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0)
            .Select(d => d.EntityId)
            .ToArray();
        Assert.Equal(2, scriptedSpawns.Length);   // Spawn 只在首个 tick 出现一次
        Assert.Equal(1, session.ScriptedSpawnPointCount);

        // 推进超过 1 秒（60 tick/s）。Despawn 走**全广播**通道（DsTickResult.DespawnBroadcast，
        // 不做 AOI 裁剪——项目铁律：漏发就留幽灵），且只在到期那一个 tick 出现一次，
        // 因此逐 tick 累积，而不是只看最后一次结果。
        var despawned = new HashSet<ulong>();
        for (var i = 0; i < 64; i++)
        {
            var r = session.Tick();
            if (r.DespawnBroadcast?.Deltas == null) continue;
            foreach (var d in r.DespawnBroadcast.Deltas)
            {
                if ((int)(d.EntityId >> 32) < 0) despawned.Add(d.EntityId);
            }
        }

        Assert.Equal(0, session.ScriptedSpawnPointCount);
        Assert.Equal(2, despawned.Count);
        Assert.True(despawned.SetEquals(scriptedSpawns));
    }

    [Fact]
    public void 静态刷怪点实体不被脚本清除()
    {
        // DS 无从复核 DespawnAny 权限，因此只承认脚本域——宁可清不掉，不可越权清。
        var session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = ShardId,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = true,
            ScriptedSigningKey = Key,
        });
        Assert.True(session.AddPlayer(1001, 1001, 0f, 0f, 0f, out _), "静态点测试需要一个在线受众");

        var cfg = Horizon.Game.Sim.SpawnPointConfig.Default;
        cfg.SpawnPointId = 1;
        cfg.ShardId = ShardId;
        cfg.MaxAlive = 1;
        cfg.MonsterTemplateId = 1;
        session.ConfigureSpawnPoints(new[] { cfg });

        // 静态怪的 Spawn 只在"首次下发"那一个 tick 出现（之后全是 Update），必须从第一次结果里取。
        var first = session.Tick();
        var staticIds = new List<ulong>();
        foreach (var d in first.WorldSnapshot!.Deltas)
        {
            if (d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) > 0) staticIds.Add(d.EntityId);
        }
        Assert.NotEmpty(staticIds);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var despawn = Resign(Modify(MakeToken(now), t =>
        {
            t.Op = ScriptOp.Despawn;
            t.TargetEntityId = staticIds[0];
        }), now);
        Assert.Null(session.EnqueueScriptedCommand(despawn));

        // 到期/清除一律看全广播通道；静态点实体不应出现在其中。
        var despawnedIds = new HashSet<ulong>();
        for (var i = 0; i < 4; i++)
        {
            var r = session.Tick();
            if (r.DespawnBroadcast?.Deltas != null)
                foreach (var d in r.DespawnBroadcast.Deltas) despawnedIds.Add(d.EntityId);
        }
        Assert.DoesNotContain(staticIds[0], despawnedIds);
    }

    [Fact]
    public void despawn_mine清除本人全部脚本生成点()
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Null(session.EnqueueScriptedCommand(MakeToken(now, nonce: 1)));
        Assert.Null(session.EnqueueScriptedCommand(MakeToken(now, nonce: 2)));
        session.Tick();
        Assert.Equal(2, session.ScriptedSpawnPointCount);

        var mine = Resign(Modify(MakeToken(now, nonce: 3), t => t.Op = ScriptOp.DespawnMine), now);
        Assert.Null(session.EnqueueScriptedCommand(mine));
        session.Tick();

        Assert.Equal(0, session.ScriptedSpawnPointCount);
    }
    // ---------- 5. NPC 与玩法型事件 ----------

    [Theory]
    [InlineData("铸剑师", true)]
    [InlineData("npc_gatekeeper", true)]
    [InlineData("2", true)]
    [InlineData("不存在的NPC", false)]
    public void NPC白名单按自己的目录解析(string key, bool expected)
    {
        Assert.Equal(expected, DefaultScriptTemplateCatalog.Instance.TryResolve(ScriptedKind.Npc, key, out var id));
        if (expected) Assert.True(id >= 1);
    }

    [Fact]
    public void 生成NPC需要SpawnNpc位_怪物位不够()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var ticket = SpawnTicket(kind: ScriptedKind.Npc, templateKey: "铸剑师");

        Assert.Equal(ScriptRejectReason.InsufficientPermission,
            core.Evaluate(Authorized((uint)ScriptPermissionFlags.SpawnMonster), Ctx(ticket), Key).Reason);
        Assert.True(core.Evaluate(Authorized((uint)ScriptPermissionFlags.SpawnNpc), Ctx(ticket), Key).Allowed);
    }

    [Fact]
    public void 脚本NPC以Npc实体类型下发且站定不动()
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var token = Resign(Modify(MakeToken(now, nonce: 9001), t =>
        {
            t.Kind = ScriptedKind.Npc;
            t.TemplateId = 1; // 铸剑师
            t.Count = 1;
        }), now);
        Assert.Null(session.EnqueueScriptedCommand(token));

        var first = session.Tick();
        var npc = Assert.Single(first.WorldSnapshot!.Deltas.Where(d =>
            d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0));
        Assert.Equal(DsWorldSession.EntityTypeNpc, npc.Identity.Value.EntityType);
        Assert.Equal("铸剑师", npc.Identity.Value.Nickname);

        // 多跑若干 tick：NPC 不该移动（MoveSpeed=0 / 无仇恨），也不该被当成怪追人。
        for (var i = 0; i < 30; i++) session.Tick();
        var later = session.Tick();
        // EntityDelta 是结构体：用"有没有这条 delta"判断，而不是与 null 比较。
        var followUp = later.WorldSnapshot!.Deltas.FirstOrDefault(d => d.EntityId == npc.EntityId);
        if (followUp.EntityId == npc.EntityId)
            Assert.Equal(npc.Transform.Value.Y, followUp.Transform!.Value.Y); // Y-up 高度不变
    }

    [Fact]
    public void 玩法型事件按怪群生成并在TTL到期清除()
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 目录里 TemplateId=1 = "墨麒麟降世"（MonsterGroup，模板 1，3 只）。
        var token = Resign(Modify(MakeToken(now, nonce: 9100), t2 =>
        {
            t2.Kind = ScriptedKind.Event;
            t2.TemplateId = 1;
            t2.TtlSeconds = 2;
            t2.Count = 9; // 客户端报的数量必须被目录覆盖
        }), now);
        Assert.Null(session.EnqueueScriptedCommand(token));

        var first = session.Tick();
        var spawned = first.WorldSnapshot!.Deltas
            .Where(d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0)
            .ToArray();
        Assert.Equal(3, spawned.Length); // 数量取自目录的 SpawnCount，不是令牌 Count
        Assert.All(spawned, d => Assert.Equal(DsWorldSession.EntityTypeMonster, d.Identity.Value.EntityType));

        var despawned = new HashSet<ulong>();
        for (var i = 0; i < 140; i++)
        {
            var r = session.Tick();
            if (r.DespawnBroadcast?.Deltas != null)
                foreach (var d in r.DespawnBroadcast.Deltas) despawned.Add(d.EntityId);
        }
        Assert.Equal(0, session.ScriptedSpawnPointCount);
        Assert.Equal(3, despawned.Count);
    }

    [Fact]
    public void 事件形态与数量由服务端目录决定()
    {
        // 事件标识在两个目录里都存在时，Event 走事件目录而非怪物目录。
        Assert.True(DefaultScriptTemplateCatalog.Instance.TryResolve(ScriptedKind.Event, "天空异象", out var presId));
        Assert.Equal(3, presId);
        Assert.True(DefaultScriptTemplateCatalog.Instance.TryResolve(ScriptedKind.Event, "1", out var groupId));
        Assert.Equal(1, groupId);

        // 怪物名不是事件名：不能靠 /event 墨麒麟 绕过事件白名单刷怪。
        Assert.False(DefaultScriptTemplateCatalog.Instance.TryResolve(ScriptedKind.Event, "墨麒麟", out _));
    }

    [Fact]
    public void 表现型事件走Vfx通道且不建实体()
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var monstersBefore = session.MonsterCount;

        // TemplateId=3 = "天空异象"（Presentation）
        var token = Resign(Modify(MakeToken(now, nonce: 9200), t2 =>
        {
            t2.Kind = ScriptedKind.Event;
            t2.TemplateId = 3;
            t2.TtlSeconds = 4;
        }), now);
        Assert.Null(session.EnqueueScriptedCommand(token));

        var r = session.Tick();
        Assert.Equal(1, session.PresentationEventCount);
        Assert.Equal(monstersBefore, session.MonsterCount);       // 不建实体
        Assert.Equal(0, session.ScriptedSpawnPointCount);          // 也不建生成点

        var evt = Assert.Single(r.Events);
        var packet = Assert.IsType<Horizon.Game.Message.Sync.EventPacket>(evt.Packet);
        var sync = Assert.Single(packet.Events);
        Assert.Equal(Horizon.Game.Message.Sync.SyncEventKind.Vfx, sync.Kind);
        Assert.Equal(3, sync.IntValue);
        Assert.Equal(4f, sync.FloatValue);

        var payload = MemoryPack.MemoryPackSerializer.Deserialize<Horizon.Game.Message.Sync.ScriptedEffectPayload>(
            sync.Payload!);
        Assert.Equal("天空异象", payload.Text);
        Assert.Equal(4f, payload.DurationSeconds);
        Assert.NotEmpty(evt.Audience); // 只发给在线会话
    }

    [Fact]
    public void 无人在线时表现型事件不投递()
    {
        var session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = ShardId,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = true,
            ScriptedSigningKey = Key,
        });
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var token = Resign(Modify(MakeToken(now, nonce: 9300), t2 =>
        {
            t2.Kind = ScriptedKind.Event;
            t2.TemplateId = 3;
        }), now);

        Assert.Null(session.EnqueueScriptedCommand(token));
        session.Tick();

        Assert.Equal(0, session.PresentationEventCount);
        Assert.Equal("no-audience", session.LastScriptRejectReason);
    }

    [Fact]
    public void 触发区域绑定对象走怪物目录且需PlaceTrigger位()
    {
        var core = new ScriptAuthorityCore(MakeOptions(), DefaultScriptTemplateCatalog.Instance);
        var ticket = SpawnTicket(kind: ScriptedKind.TriggerZone, radius: 10f, ttlSeconds: 120);

        // 只持怪物位不足以放区域：区域是"长期自动刷怪装置"，风险等级更高。
        Assert.Equal(ScriptRejectReason.InsufficientPermission,
            core.Evaluate(Authorized((uint)ScriptPermissionFlags.SpawnMonster), Ctx(ticket), Key).Reason);

        var ok = core.Evaluate(
            Authorized((uint)(ScriptPermissionFlags.PlaceTrigger | ScriptPermissionFlags.SpawnMonster)),
            Ctx(ticket), Key);
        Assert.True(ok.Allowed);
        Assert.Equal(10f, ok.Token!.Radius);
        Assert.Equal(ScriptTriggerMode.Once, ok.Token!.TriggerMode);
    }

    // ---------- 6. 触发区域执行（DS 侧第三态） ----------

    private static DsWorldSession MakeZoneSession(int maxZones = 32)
    {
        var session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = ShardId,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = true,
            ScriptedSigningKey = Key,
            MaxScriptedTriggerZones = maxZones,
        });
        return session;
    }

    private static ScriptGrantToken ZoneToken(
        long nonce, float x, float radius = 10f, int ttl = 60,
        ScriptTriggerMode mode = ScriptTriggerMode.Once, int cooldownSeconds = 0, int count = 2)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var token = MakeToken(now, nonce: nonce);
        token.Op = ScriptOp.Spawn;
        token.Kind = ScriptedKind.TriggerZone;
        token.X = x;
        token.Y = 0f;
        token.Z = 0f;
        token.Radius = radius;
        token.TtlSeconds = ttl;
        token.TriggerMode = mode;
        token.CooldownSeconds = cooldownSeconds;
        token.Count = count;
        ScriptGrantTokenSigner.Sign(token, Key, now, 5);
        return token;
    }

    [Fact]
    public void 踏入触发区域才召唤_区域外不动()
    {
        var session = MakeZoneSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.Null(session.EnqueueScriptedCommand(ZoneToken(2001, x: 500f)));
        session.Tick();
        Assert.Equal(1, session.TriggerZoneCount);
        Assert.Equal(0, session.TriggerFiredCount);

        // 玩家落在区域外 → 无进入沿，不该召唤。
        Assert.True(session.AddPlayer(2001, 2001, 0f, 0f, 0f, out _));
        session.Tick();
        Assert.Equal(0, session.TriggerFiredCount);

        // 进入区域的玩家出现 → 本 tick 召唤，且同一 tick 的快照里就能看到怪。
        Assert.True(session.AddPlayer(2002, 2002, 500f, 0f, 0f, out _));
        var r = session.Tick();
        Assert.Equal(1, session.TriggerFiredCount);
        var summoned = r.WorldSnapshot!.Deltas
            .Count(d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0 && d.EntityId != 0);
        Assert.True(summoned >= 2, $"召唤出的怪群应进入本 tick 快照，实际 {summoned}");

        // Once：触发即失效，不会变成常驻刷怪机器。
        Assert.Equal(0, session.TriggerZoneCount);
    }

    [Fact]
    public void Repeat区域在冷却内不重复召唤()
    {
        var session = MakeZoneSession();
        Assert.Null(session.EnqueueScriptedCommand(
            ZoneToken(2101, x: 500f, mode: ScriptTriggerMode.Repeat, cooldownSeconds: 2)));
        session.Tick();

        Assert.True(session.AddPlayer(2101, 2101, 500f, 0f, 0f, out _));
        session.Tick();
        Assert.Equal(1, session.TriggerFiredCount);

        // 冷却窗口内第二个玩家进入：不叠加召唤（边界横跳风暴防护）。
        Assert.True(session.AddPlayer(2102, 2102, 500f, 0f, 0f, out _));
        session.Tick();
        Assert.Equal(1, session.TriggerFiredCount);
        Assert.Equal(1, session.TriggerZoneCount); // Repeat 不因触发而失效

        // 冷却（2s = 120 tick）过后，再进入的玩家可再次触发。
        for (var i = 0; i < 125; i++) session.Tick();
        Assert.True(session.AddPlayer(2103, 2103, 500f, 0f, 0f, out _));
        session.Tick();
        Assert.Equal(2, session.TriggerFiredCount);
    }

    [Fact]
    public void 区域到期后不再触发且召唤出的怪群按自身规则存续()
    {
        var session = MakeZoneSession();
        Assert.Null(session.EnqueueScriptedCommand(ZoneToken(2201, x: 500f, ttl: 1, count: 2)));
        session.Tick();

        Assert.True(session.AddPlayer(2201, 2201, 500f, 0f, 0f, out _));
        session.Tick();
        Assert.Equal(1, session.TriggerFiredCount);

        // Once 已在触发时失效；即便用 Repeat 也应被 TTL 摘掉。推进超过 1 秒确认区域数归零。
        for (var i = 0; i < 70; i++) session.Tick();
        Assert.Equal(0, session.TriggerZoneCount);

        // 怪群不受区域到期影响（区域只是"触发器"，不是召唤物的宿主）。
        Assert.True(session.MonsterCount >= 2);
    }

    [Fact]
    public void 区域数受DS硬顶约束()
    {
        var session = MakeZoneSession(maxZones: 2);
        Assert.Null(session.EnqueueScriptedCommand(ZoneToken(2301, x: 100f)));
        Assert.Null(session.EnqueueScriptedCommand(ZoneToken(2302, x: 200f)));
        Assert.Null(session.EnqueueScriptedCommand(ZoneToken(2303, x: 300f)));
        session.Tick();

        Assert.Equal(2, session.TriggerZoneCount);
        Assert.Equal("zone-budget", session.LastScriptRejectReason);
    }

    [Fact]
    public void despawn_mine同时清除区域与生成点()
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Null(session.EnqueueScriptedCommand(ZoneToken(2401, x: 500f, ttl: 300)));
        Assert.Null(session.EnqueueScriptedCommand(MakeToken(now, nonce: 2402)));
        session.Tick();
        Assert.Equal(1, session.TriggerZoneCount);
        Assert.Equal(1, session.ScriptedSpawnPointCount);

        var mine = Resign(Modify(MakeToken(now, nonce: 2403), t => t.Op = ScriptOp.DespawnMine), now);
        Assert.Null(session.EnqueueScriptedCommand(mine));
        session.Tick();

        Assert.Equal(0, session.TriggerZoneCount);
        Assert.Equal(0, session.ScriptedSpawnPointCount);
    }

    // ---------- 7. 实体 ID 回执（/spawn 后操作者拿得到真 ID） ----------

    [Fact]
    public void 生成回执携带真实实体ID且只发给操作者()
    {
        // 在线受众是另一个角色（1001），操作者是 7001：回执必须定向，不能顺着快照广播出去。
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var token = Resign(Modify(MakeToken(now, nonce: 3001), t2 => t2.RequestId = 4711), now);
        Assert.Null(session.EnqueueScriptedCommand(token));

        var r = session.Tick();
        Assert.Equal(1, session.ScriptReceiptCount);

        var receiptEvents = r.Events.Where(e =>
            e.Packet is Horizon.Game.Message.Sync.EventPacket ep
            && ep.Events.Any(s2 => s2.Kind == Horizon.Game.Message.Sync.SyncEventKind.ScriptedSpawnResult)).ToArray();
        var receipt = Assert.Single(receiptEvents);
        Assert.Equal(new[] { OperatorId }, receipt.Audience);   // 只发给操作者自己

        var packet = (Horizon.Game.Message.Sync.EventPacket)receipt.Packet;
        var sync = packet.Events.Single(e2 => e2.Kind == Horizon.Game.Message.Sync.SyncEventKind.ScriptedSpawnResult);
        var payload = MemoryPack.MemoryPackSerializer
            .Deserialize<Horizon.Game.Message.Sync.ScriptedSpawnResultPayload>(sync.Payload!);

        Assert.Equal(4711, payload.RequestId);
        Assert.Equal(3001, payload.Nonce);
        Assert.False(payload.Removed);

        // 回执里的 ID 必须与本 tick 快照里真正出现的脚本实体**完全一致**——不预估、不谎报。
        var snapshotIds = r.WorldSnapshot!.Deltas
            .Where(d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0)
            .Select(d => d.EntityId)
            .OrderBy(id => id)
            .ToArray();
        Assert.Equal(snapshotIds, payload.EntityIds.OrderBy(id => id).ToArray());
        Assert.Equal(2, payload.EntityIds.Length);
        Assert.Equal(2, sync.IntValue);
    }

    [Fact]
    public void 清除回执报告实际被清的ID()
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Null(session.EnqueueScriptedCommand(MakeToken(now, nonce: 3101)));
        var spawn = session.Tick();
        var created = spawn.WorldSnapshot!.Deltas
            .Where(d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0)
            .Select(d => d.EntityId)
            .OrderBy(id => id)
            .ToArray();

        var despawn = Resign(Modify(MakeToken(now, nonce: 3102), t2 =>
        {
            t2.Op = ScriptOp.DespawnMine;
            t2.RequestId = 4712;
        }), now);
        Assert.Null(session.EnqueueScriptedCommand(despawn));

        var r = session.Tick();
        var receipt = Assert.Single(r.Events.Where(e =>
            e.Packet is Horizon.Game.Message.Sync.EventPacket ep
            && ep.Events.Any(s2 => s2.Kind == Horizon.Game.Message.Sync.SyncEventKind.ScriptedSpawnResult)));
        var sync = ((Horizon.Game.Message.Sync.EventPacket)receipt.Packet).Events
            .Single(e2 => e2.Kind == Horizon.Game.Message.Sync.SyncEventKind.ScriptedSpawnResult);
        var payload = MemoryPack.MemoryPackSerializer
            .Deserialize<Horizon.Game.Message.Sync.ScriptedSpawnResultPayload>(sync.Payload!);

        Assert.True(payload.Removed);
        Assert.Equal(4712, payload.RequestId);
        Assert.Equal(created, payload.EntityIds.OrderBy(id => id).ToArray());
    }

    [Fact]
    public void 回执在无任何实体产生时也如实为空()
    {
        // DespawnMine 但本人没有生成物：回执应为空列表，而不是不发（操作者需要知道"确实执行了"）。
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var mine = Resign(Modify(MakeToken(now, nonce: 3201), t2 =>
        {
            t2.Op = ScriptOp.DespawnMine;
            t2.RequestId = 4713;
        }), now);
        Assert.Null(session.EnqueueScriptedCommand(mine));

        var r = session.Tick();
        var payload = MemoryPack.MemoryPackSerializer
            .Deserialize<Horizon.Game.Message.Sync.ScriptedSpawnResultPayload>(
                ((Horizon.Game.Message.Sync.EventPacket)Assert.Single(r.Events.Where(e =>
                    e.Packet is Horizon.Game.Message.Sync.EventPacket ep
                    && ep.Events.Any(s2 => s2.Kind == Horizon.Game.Message.Sync.SyncEventKind.ScriptedSpawnResult)))
                .Packet).Events
                .Single(e2 => e2.Kind == Horizon.Game.Message.Sync.SyncEventKind.ScriptedSpawnResult).Payload!);

        Assert.Empty(payload.EntityIds);
        Assert.True(payload.Removed);
    }

    // ---------- 8. 确定性：结果只能由输入决定，不能由字典/到达顺序决定 ----------

    /// <summary>跑一遍脚本命令序列，返回逐 tick 的可比较指纹。</summary>
    private static string RunScriptedSequence(long[] noncesInArrivalOrder, int ticksToRun, int ttlSeconds)
    {
        var (session, _) = MakeDsSession();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var log = new System.Text.StringBuilder();

        // 同 tick 内以"到达顺序"入队；内核按 Nonce 升序消费，因此不同到达顺序必须得到同一结果。
        foreach (var nonce in noncesInArrivalOrder)
        {
            var token = Resign(Modify(MakeToken(now, nonce: nonce), t2 =>
            {
                t2.TtlSeconds = ttlSeconds;
                t2.X = 0f;
                t2.Y = 0f;
                t2.Z = 20f + nonce;   // 错开落点，保证不同指令确实生成不同实体
                t2.Count = 2;
                t2.RequestId = nonce * 10;
            }), now);
            Assert.Null(session.EnqueueScriptedCommand(token));
        }

        for (var tick = 0; tick < ticksToRun; tick++)
        {
            var r = session.Tick();

            log.Append("tick").Append(tick).Append(':');
            if (r.WorldSnapshot?.Deltas != null)
            {
                foreach (var d in r.WorldSnapshot.Deltas)
                    log.Append(d.Kind).Append('#').Append(d.EntityId).Append(';');
            }
            if (r.DespawnBroadcast?.Deltas != null)
            {
                log.Append("|despawn:");
                foreach (var d in r.DespawnBroadcast.Deltas) log.Append(d.EntityId).Append(',');
            }
            foreach (var e in r.Events)
            {
                if (e.Packet is not Horizon.Game.Message.Sync.EventPacket ep) continue;
                foreach (var ev in ep.Events)
                {
                    if (ev.Kind != Horizon.Game.Message.Sync.SyncEventKind.ScriptedSpawnResult) continue;
                    var payload = MemoryPack.MemoryPackSerializer
                        .Deserialize<Horizon.Game.Message.Sync.ScriptedSpawnResultPayload>(ev.Payload!);
                    log.Append("|receipt:").Append(payload.RequestId).Append('=');
                    foreach (var id in payload.EntityIds.OrderBy(x => x)) log.Append(id).Append(',');
                }
            }
            log.Append('\n');
        }

        return log.ToString();
    }

    [Fact]
    public void 同tick内的指令到达顺序不影响世界结果()
    {
        var arrival = new long[] { 5001, 5002, 5003 };
        var shuffled = new long[] { 5003, 5001, 5002 };

        var a = RunScriptedSequence(arrival, ticksToRun: 90, ttlSeconds: 1);
        var b = RunScriptedSequence(shuffled, ticksToRun: 90, ttlSeconds: 1);

        Assert.Equal(a, b);

        // 前提自查：这一序列确实产生了脚本实体与到期清除，否则上面的相等就是空断言。
        Assert.Contains("|despawn:", a);
        Assert.Contains("|receipt:", a);
    }

    /// <summary>
    /// 直接断言不变量，而不是只看"两次跑结果相同"：后者可能因字典遍历序恰好等于
    /// 插入序而假通过。到期注销列表已显式排序，所以同一 tick 内脚本实体的 Despawn
    /// 排列必须升序——这条性质与 .NET 字典实现细节无关。
    /// </summary>
    [Fact]
    public void 同tick内脚本实体清除顺序单调递增()
    {
        var fingerprint = RunScriptedSequence(new long[] { 6001, 6002, 6003 }, ticksToRun: 95, ttlSeconds: 1);

        var checkedTicks = 0;
        foreach (var line in fingerprint.Split('\n'))
        {
            var at = line.IndexOf("|despawn:", StringComparison.Ordinal);
            if (at < 0) continue;

            var ids = line[(at + "|despawn:".Length)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                // 只取脚本域：全广播里也可能混有离线玩家等其它 Despawn。
                .Select(ulong.Parse)
                .Where(id => (int)(id >> 32) < 0)
                .ToArray();

            if (ids.Length == 0) continue;

            checkedTicks++;
            Assert.True(ids.SequenceEqual(ids.OrderBy(id => id)),
                $"脚本实体清除顺序未升序：{string.Join(",", ids)}");
        }

        Assert.True(checkedTicks > 0, "序列里没有观察到脚本实体被清除，断言将沦为空转");
    }

    [Fact]
    public void 同tick到期的清除结果与指令到达顺序无关()
    {
        var a = RunScriptedSequence(new long[] { 6001, 6002, 6003 }, ticksToRun: 95, ttlSeconds: 1);
        var b = RunScriptedSequence(new long[] { 6003, 6002, 6001 }, ticksToRun: 95, ttlSeconds: 1);

        Assert.Equal(a, b);
        Assert.Contains("|despawn:", a);   // 前提自查：确实发生了同 tick 多点到期
    }

    // ---------- 9. 黄金值：在真实 DsWorldSession 上钉死一条脚本生成轨迹 ----------
    //
    // 为什么不在 GameplayGoldenTrace 里加脚本语法：那个文件自带一套手写的 MonsterAiCore /
    // SpawnDirectorCore 重放器，并不驱动 DsWorldSession。往里再加一条"脚本生成"分支等于把 DS
    // 的生成规则抄第二份，两份迟早漂移，黄金值反而变成假的安全感。所以这里直接驱动真实会话。

    /// <summary>指纹与逐 tick 文本（文本用于自查轨迹非空）。</summary>
    private static (string Hash, string Trace) ScriptedGoldenTrace()
    {
        var session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = ShardId,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = true,
            ScriptedSigningKey = Key,
        });

        // 签发时刻必须是"现在"：DS 按墙钟校验令牌过期（设计上的 5 秒短凭证），
        // 用历史固定时间戳会让四条指令全部被拒，黄金值就锁在了一条空轨迹上。
        // 轨迹内容本身不含任何墙钟量（Nonce 手写、位置与 tick 全确定），因此仍可复现。
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var log = new System.Text.StringBuilder();

        void LogTick(int label)
        {
            var r = session.Tick();
            log.Append('t').Append(label).Append(' ');

            if (r.WorldSnapshot?.Deltas != null)
            {
                foreach (var d in r.WorldSnapshot.Deltas)
                {
                    log.Append("S ").Append(d.Kind).Append(',').Append(d.EntityId);
                    if (d.Identity is { } id)
                        log.Append(',').Append(id.EntityType).Append(',').Append(id.Nickname);
                    if (d.Transform is { } tr)
                        log.Append(',').Append(tr.X.ToString("R")).Append(',')
                            .Append(tr.Y.ToString("R")).Append(',').Append(tr.Z.ToString("R"));
                    log.Append(';');
                }
            }

            if (r.DespawnBroadcast?.Deltas != null)
                foreach (var d in r.DespawnBroadcast.Deltas)
                    log.Append("|X ").Append(d.EntityId).Append(';');

            foreach (var e in r.Events)
            {
                if (e.Packet is not Horizon.Game.Message.Sync.EventPacket ep) continue;
                foreach (var ev in ep.Events)
                {
                    log.Append("|E ").Append(ev.Kind).Append(',').Append(ev.IntValue);
                    if (ev.Payload is { Length: > 0 } pl)
                        log.Append(',').Append(Convert.ToHexString(pl));
                    log.Append(';');
                }
            }

            log.AppendLine();
        }

        void Enqueue(ScriptGrantToken token)
        {
            var reject = session.EnqueueScriptedCommand(token);
            log.Append("enqueue ").Append(token.Nonce).Append(' ').Append(reject ?? "ok").AppendLine();
        }

        ScriptGrantToken Make(long nonce, ScriptedKind kind, int templateId, int count, int ttl,
                              float x, float z, float radius = 0f,
                              ScriptTriggerMode mode = ScriptTriggerMode.Once, int cooldown = 0)
        {
            var token = new ScriptGrantToken
            {
                Nonce = nonce,
                OperatorCharacterId = OperatorId,
                RequestId = nonce * 3,
                Op = ScriptOp.Spawn,
                Kind = kind,
                TemplateId = templateId,
                TargetShardId = ShardId,
                X = x,
                Y = 0f,
                Z = z,
                Count = count,
                TtlSeconds = ttl,
                Radius = radius,
                TriggerMode = mode,
                CooldownSeconds = cooldown,
            };
            ScriptGrantTokenSigner.Sign(token, Key, now, 5);
            return token;
        }

        // 固定编排：怪群(永久) / NPC(永久) / 玩法型事件(TTL 2s) / 触发区域(Repeat+冷却, TTL 60s)。
        void EnqueueAll()
        {
            Enqueue(Make(11, ScriptedKind.Monster, 1, 3, 0, 10f, 10f));
            Enqueue(Make(12, ScriptedKind.Npc, 1, 1, 0, 20f, 20f));
            Enqueue(Make(13, ScriptedKind.Event, 1, 1, 2, 30f, 30f));
            Enqueue(Make(14, ScriptedKind.TriggerZone, 2, 2, 60, 40f, 40f, 8f, ScriptTriggerMode.Repeat, 1));
        }

        // 受众必须先存在：无在线会话时 DS 不构建共享快照，那一 tick 的 Spawn 只会被
        // "按会话补发"通道带走，轨迹就看不见它们（这是取证口径问题，不是产品缺陷）。
        session.AddPlayer(1001, 1001, 0f, 0f, 0f, out _);
        LogTick(0);                       // 玩家 1001 入场

        EnqueueAll();                     // 四条指令入队
        LogTick(1);                       // 脚本实体生成 + 三条回执，都在这一 tick

        // 第二个玩家落在触发区域内，构成"进入沿"，检验召唤路径。
        session.AddPlayer(1002, 1002, 40f, 0f, 40f, out _);

        for (var tick = 2; tick < 202; tick++) LogTick(tick);

        log.Append("counts ")
            .Append(session.MonsterCount).Append(',')
            .Append(session.ScriptedSpawnPointCount).Append(',')
            .Append(session.TriggerZoneCount).Append(',')
            .Append(session.TriggerFiredCount).Append(',')
            .Append(session.ScriptReceiptCount).Append(',')
            .Append(session.ScriptedAcceptedCount).Append(',')
            .Append(session.ScriptedRejectedCount).Append(',')
            .Append(session.PresentationEventCount)
            .AppendLine();

        var trace = log.ToString();
        using var sha = System.Security.Cryptography.SHA256.Create();
        return (Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(trace))), trace);
    }

    [Fact]
    public void 脚本生成轨迹黄金值()
    {
        var (hash, trace) = ScriptedGoldenTrace();

        // 自查一：轨迹必须覆盖四条真实路径——空轨迹的哈希同样稳定，锁住的却什么都不是。
        Assert.Contains(",3,", trace);                       // 有 NPC 实体类型下发
        Assert.Contains(",4,", trace);                       // 有怪物/事件实体类型下发
        Assert.Contains("|X ", trace);                       // 有到期清除（走全广播）
        Assert.Contains("|E ScriptedSpawnResult", trace);    // 有实体 ID 回执

        // 自查二：四条指令零拒绝，末尾计数锁住最终世界形态
        // (存活怪, 脚本生成点, 触发区域, 触发次数, 回执数, 受理数, 拒绝数, 表现型事件数)
        // trace 以换行收尾，直接与不含换行的期望比较必然失败——先裁掉行尾换行。
        Assert.EndsWith("counts 6,3,1,1,4,4,0,0", trace.TrimEnd((char)10, (char)13), StringComparison.Ordinal);

        // 逐 tick 全量指纹（含实体 ID 分配、下发类型、昵称、坐标量化、清除与回执顺序）。
        // 任何改变脚本生成结果或顺序的改动都会撞这里——包括"看起来无害"地换个字典遍历。
        // 时钟不入指纹：令牌签发用真实当前时间（DS 按墙钟校验过期），但轨迹内容本身全是确定量。
        Assert.Equal(
            "079F2FC0F193396696579ED35591B6702BEA9C0545DB573C6A503C85969BEFCD",
            hash);
    }
}
