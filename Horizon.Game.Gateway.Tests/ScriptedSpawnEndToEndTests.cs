using Horizon.Core.Security;
using Horizon.Game.Core;
using Horizon.Game.Core.Adapters;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Security;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Core.World;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Horizon.Orleans.Interface.World;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 脚本化世界生成的<b>三段链路</b>端到端测试（同进程，无网络）。
/// <para>
/// 已有的两组测试各锁一段：<see cref="ScriptedSpawnAuthorityTests"/> 直接驱动裁决核与
/// <see cref="DsWorldSession"/>，<see cref="WorldScriptCommandHandlerTests"/> 用<b>假令牌</b>驱动网关。
/// 中间留了一条缝：网关真正交给 DS 的那枚令牌，是不是 DS 认的那一枚？
/// 规范字节序（<see cref="ScriptGrantTokenSigner.Version"/>）、shard 绑定、模板名→ID 解析、
/// 负 ID 域这些约定只要有一处口径漂移，分段测试会各自全绿而线上全红。
/// </para>
/// <para>
/// 本文件把三段接起来跑：<b>真网关</b>（身份只取 AuthToken）→ <b>真裁决核</b>（真签发）→
/// <b>真 DS 会话</b>（真验签、真出怪、真回执）。只有 Orleans 与 socket 是替身。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class ScriptedSpawnEndToEndTests
{
    private const long OperatorId = 7001;
    private const long Shard = 3;
    private const long RequestId = 9042;

    private static readonly byte[] Key = Enumerable.Repeat((byte)0x5A, 32).ToArray();
    private static readonly string KeyB64 = Convert.ToBase64String(Key);

    private sealed class Chain
    {
        public WorldScriptCommandHandler Handler = null!;
        public DsWorldSession Session = null!;
        public UserAuthTokenProvider Tokens = null!;
        public string AuthToken = string.Empty;

        /// <summary>DS 侧对每条令牌的入队结果（null = 已接受）。</summary>
        public readonly List<string?> EnqueueResults = new();
    }

    private static Chain Build(
        string? gatewayKeyB64 = null,
        string? dsKeyB64 = null,
        bool dsKeyMissing = false,
        bool operatorPositionKnown = true)
    {
        gatewayKeyB64 ??= KeyB64;
        // dsKeyMissing 与 dsKeyB64 分开：默认参数必须是编译常量，"用同一把密钥"只能是函数内的默认。
        var effectiveDsKey = dsKeyMissing ? null : dsKeyB64 ?? KeyB64;
        var spawnOptions = new ScriptedSpawnOptions { Enabled = true, SigningKeyBase64 = gatewayKeyB64 };
        var core = new ScriptAuthorityCore(spawnOptions, DefaultScriptTemplateCatalog.Instance);
        var signingKey = Convert.FromBase64String(gatewayKeyB64);
        var operatorState = new ScriptOperatorState
        {
            Flags = (uint)ScriptPermissionFlags.WorldDirector,
            ExpiresAtUtc = DateTime.MaxValue,
            GrantedAtUtc = DateTime.UtcNow.AddDays(-1),
            GrantedBy = OperatorId,
        };

        var chain = new Chain();

        // --- DS 段：真会话，密钥与 Grain 同源 ---
        chain.Session = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = Shard,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = true,
            ScriptedSigningKey = effectiveDsKey is null ? null : Convert.FromBase64String(effectiveDsKey),
        });
        // 共享快照只在有受众时构建，故留一个在线观众（回执是定向的，不需要操作者也在线）。
        Assert.True(chain.Session.AddPlayer(1001, 1001, 0f, 0f, 0f, out var addReason), addReason);

        // --- Grain 段：真裁决核 + 真签发，外面只套一层接口替身 ---
        var grain = new Mock<IGmCommandGrain>();
        grain.Setup(g => g.TryConsumeScriptAsync(It.IsAny<GmScriptTicket>()))
            .ReturnsAsync((GmScriptTicket ticket) =>
            {
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var ctx = new ScriptEvalContext
                {
                    Ticket = ticket,
                    // 服务端权威位置：(10,20,0)。测试里的操作者就站在这儿。
                    OperatorX = 10f,
                    OperatorY = 20f,
                    OperatorZ = 0f,
                    OperatorPositionKnown = operatorPositionKnown,
                    OperatorShardId = ticket.OperatorShardId,
                    NowUtc = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime,
                    NowUnixMs = nowMs,
                };
                var decision = core.Evaluate(operatorState, ctx, signingKey);
                return new GmScriptVerdict
                {
                    Allowed = decision.Allowed,
                    Reason = decision.Reason,
                    Message = decision.Message,
                    RemainingBudget = decision.RemainingBudget,
                    EffectiveFlags = decision.EffectiveFlags,
                    Token = decision.Token,
                };
            });

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IGmCommandGrain>(0L, It.IsAny<string>())).Returns(grain.Object);

        chain.Tokens = new UserAuthTokenProvider("e2e-secret", NullLogger<UserAuthTokenProvider>.Instance);
        chain.AuthToken = chain.Tokens.GenerateToken("passport-1", "machine-1", OperatorId);

        // --- 网关段：真 handler（含真密钥自检），投递接到真 DS 会话 ---
        chain.Handler = new WorldScriptCommandHandler(
            NullLogger<MessageHandlerBase>.Instance,
            cluster.Object,
            new HorizonMessageAdapter(),
            chain.Tokens,
            new FixedShardRouter(Shard),
            new DsBackedDispatcher(chain),
            spawnOptions);

        return chain;
    }

    /// <summary>
    /// 把 <see cref="IDsScriptDispatcher"/> 接到真实 DS 会话的入队口。
    /// <para>
    /// 忠实还原真实语义：**传输成功 ≠ 执行成功**。网关那条"已受理"建立在帧写出成功之上，
    /// 它无从知道 DS 验签后是否把指令丢进了拒绝计数——所以本替身一律回 true，
    /// 而把 DS 的拒绝原因留在 <see cref="Chain.EnqueueResults"/> 里供断言。
    /// </para>
    /// </summary>
    private sealed class DsBackedDispatcher : IDsScriptDispatcher
    {
        private readonly Chain _chain;
        public DsBackedDispatcher(Chain chain) => _chain = chain;

        public Task<bool> DispatchAsync(long shardId, ScriptGrantToken token)
        {
            _chain.EnqueueResults.Add(_chain.Session.EnqueueScriptedCommand(token));
            return Task.FromResult(true);
        }
    }

    private sealed class FixedShardRouter : IShardRouter
    {
        private readonly long _shard;
        public FixedShardRouter(long shard) => _shard = shard;
        public long Resolve(long characterId) => _shard;
        public long Resolve(long zoneId, long characterId) => _shard;
        public int ShardCount => 8;
    }

    private static HorizonMessagePacket Packet(string authToken, GmScriptCommandMessage body) => new()
    {
        ServiceType = ServiceType.System,
        Header = new MessageHeader
        {
            MessageType = MessageType.ScriptCommand,
            ServiceType = ServiceType.System,
            AuthToken = authToken,
        },
        Body = body,
    };

    private static GmScriptCommandMessage SpawnMessage(
        ScriptedKind kind = ScriptedKind.Monster,
        string templateKey = "墨麒麟",
        int count = 2) => new()
    {
        RequestId = RequestId,
        Op = ScriptOp.Spawn,
        Kind = kind,
        TemplateKey = templateKey,
        Count = count,
    };

    private static async Task<(bool Ok, GmScriptResultMessage? Result)> Submit(Chain chain, GmScriptCommandMessage msg)
    {
        var (ok, response) = await chain.Handler.HandleAsync(null!, Packet(chain.AuthToken, msg));
        return (ok, response as GmScriptResultMessage);
    }

    private static ulong[] ScriptedSpawnIds(DsTickResult tick) => tick.WorldSnapshot!.Deltas
        .Where(d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0)
        .Select(d => d.EntityId)
        .OrderBy(id => id)
        .ToArray();

    [Fact]
    public async Task 网关签发的令牌DS认得并真的出了怪()
    {
        var chain = Build();

        var (ok, ack) = await Submit(chain, SpawnMessage());
        Assert.True(ok);
        Assert.NotNull(ack);
        Assert.True(ack!.Success);
        Assert.Equal(RequestId, ack.RequestId);
        // 受理回包里**不该**有实体 ID：负 ID 域只有 DS 知道，真实 ID 走执行回执。
        Assert.Empty(ack.EntityIds);

        // 令牌必须被 DS 接受（而不是"投递成功但世界没动"）。
        Assert.Null(Assert.Single(chain.EnqueueResults));
        Assert.Equal(string.Empty, chain.Session.LastScriptRejectReason);

        var tick = chain.Session.Tick();
        var scripted = tick.WorldSnapshot!.Deltas
            .Where(d => d.Kind == EntityDeltaKind.Spawn && (int)(d.EntityId >> 32) < 0)
            .ToArray();
        Assert.Equal(2, scripted.Length);

        // 模板名→ID 在服务端解析（客户端只给了名字），昵称与网络类型必须来自目录而非回退名。
        var identities = scripted.Select(d => d.Identity!.Value).ToArray();
        Assert.All(identities, id => Assert.Equal(MonsterTemplateCatalog.Get(1).Name, id.Nickname));
        Assert.All(identities, id => Assert.Equal((int)NetworkEntityType.Monster, id.EntityType));
    }

    [Fact]
    public async Task 执行回执带着真实ID回到操作者名下()
    {
        var chain = Build();
        await Submit(chain, SpawnMessage());

        var tick = chain.Session.Tick();
        var receiptEvents = tick.Events.Where(e =>
            e.Packet is EventPacket ep
            && ep.Events.Any(s => s.Kind == SyncEventKind.ScriptedSpawnResult)).ToArray();

        var receipt = Assert.Single(receiptEvents);
        Assert.Equal(new[] { OperatorId }, receipt.Audience);

        var packet = (EventPacket)receipt.Packet;
        var sync = packet.Events.Single(e => e.Kind == SyncEventKind.ScriptedSpawnResult);
        var payload = MemoryPackSerializer.Deserialize<ScriptedSpawnResultPayload>(sync.Payload!);

        Assert.Equal(RequestId, payload.RequestId);
        Assert.False(payload.Removed);
        Assert.Equal(ScriptedSpawnIds(tick), payload.EntityIds.OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task 网关与DS密钥不同源时DS拒绝且世界里什么都没有()
    {
        // 复现"三份配置抄漏一份"的真实故障形态：网关持正确密钥，DS 持另一把合法格式的密钥。
        var otherKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x11, 32).ToArray());
        var chain = Build(dsKeyB64: otherKey);

        var (ok, _) = await Submit(chain, SpawnMessage());
        // 网关认为自己已投递成功（写入成功即成功），故障只在 DS 侧拒绝原因与回执缺席上暴露——
        // 这正是 LastScriptRejectReason 必须可诊断的理由。
        Assert.True(ok);
        Assert.Equal("signature-mismatch", Assert.Single(chain.EnqueueResults));
        Assert.Equal("signature-mismatch", chain.Session.LastScriptRejectReason);

        var tick = chain.Session.Tick();
        Assert.Empty(ScriptedSpawnIds(tick));
        Assert.Equal(0, chain.Session.ScriptReceiptCount);
    }

    [Fact]
    public async Task DS没配密钥时网关仍报受理但世界里什么都没有()
    {
        // 三进程里只有 DS 少配密钥（网关与 Grain 正确）时的表现：验签无据可依 → 一律拒。
        // 网关那条"已受理"只代表指令已写出，不代表世界里有了东西——这条不对称必须被测试钉住，
        // 否则操作者会以为成功，而唯一线索是 DS 的 LastScriptRejectReason。
        var chain = Build(dsKeyMissing: true);

        var (ok, _) = await Submit(chain, SpawnMessage());

        Assert.True(ok);
        Assert.Equal("missing-signing-key", Assert.Single(chain.EnqueueResults));
        Assert.Equal(0, chain.Session.ScriptReceiptCount);
        Assert.Empty(ScriptedSpawnIds(chain.Session.Tick()));
    }

    [Fact]
    public async Task 未登记的NPC模板被白名单拦在裁决层_DS一次都没碰()
    {
        var chain = Build();
        var (ok, ack) = await Submit(chain, SpawnMessage(ScriptedKind.Npc, "铁匠"));

        Assert.False(ok);
        Assert.Equal(ScriptRejectReason.UnknownTemplate, ack!.Reason);
        Assert.Empty(chain.EnqueueResults);
        Assert.Empty(ScriptedSpawnIds(chain.Session.Tick()));
    }

    [Fact]
    public async Task 触发区域走同一条链路_玩家不走进就不出怪()
    {
        var chain = Build();
        var cmd = SpawnMessage(ScriptedKind.TriggerZone, "墨麒麟", 1);
        cmd.HasPosition = true;
        // 服务端 Z-up：区域心 (12,22,0)，观众 1001 站在原点，半径 8 走不进去。
        cmd.X = 12f;
        cmd.Y = 22f;
        cmd.Z = 0f;
        cmd.Radius = 8f;
        cmd.TtlSeconds = 60;
        cmd.TriggerMode = ScriptTriggerMode.Once;

        var (ok, ack) = await Submit(chain, cmd);
        Assert.True(ok);
        Assert.True(ack!.Success);
        Assert.Null(Assert.Single(chain.EnqueueResults));

        chain.Session.Tick();
        var armed = chain.Session.Tick();
        Assert.Empty(ScriptedSpawnIds(armed));
    }

    [Fact]
    public async Task 服务端没有权威位置时整条链路拒绝且不投递DS()
    {
        // 客户端自报了坐标也不能放行：距离校验的比较基准必须由服务端持有。
        var chain = Build(operatorPositionKnown: false);
        var cmd = SpawnMessage(count: 1);
        cmd.HasPosition = true;
        cmd.X = 10.5f;
        cmd.Y = 20.5f;
        cmd.Z = 0f;

        var (ok, ack) = await Submit(chain, cmd);

        Assert.False(ok);
        Assert.Equal(ScriptRejectReason.InvalidArgument, ack!.Reason);
        Assert.Contains("权威位置", ack.Message, StringComparison.Ordinal);
        Assert.Empty(chain.EnqueueResults);
        Assert.Empty(ScriptedSpawnIds(chain.Session.Tick()));
    }
}
