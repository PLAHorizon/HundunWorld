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
using Horizon.Orleans.Interface.World;
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
/// 网关脚本指令处理器的链路测试。
/// <para>锁两条最要紧的性质：</para>
/// <list type="number">
/// <item><b>操作者身份只能来自已验签的 AuthToken</b>：<see cref="GmScriptCommandMessage"/> 里
/// 压根没有操作者字段，客户端无从"自报"；票据里的角色一律由网关从令牌解出后填入。</item>
/// <item>网关<b>不做权限判断</b>：结论来自 Grain；只在通过时把令牌投给 DS，并且如实回报投递失败。</item>
/// </list>
/// </summary>
public class WorldScriptCommandHandlerTests
{
    private const long TokenCharacterId = 4242;
    private const long Shard = 7;

    private sealed record Dispatched(long ShardId, ScriptGrantToken Token);

    private sealed class FakeDispatcher : IDsScriptDispatcher
    {
        public readonly List<Dispatched> Calls = new();
        public bool Delivered = true;

        public Task<bool> DispatchAsync(long shardId, ScriptGrantToken token)
        {
            Calls.Add(new Dispatched(shardId, token));
            return Task.FromResult(Delivered);
        }
    }

    private sealed class FakeShardRouter : IShardRouter
    {
        public long Resolve(long characterId) => Shard;
        public long Resolve(long zoneId, long characterId) => Shard;
        public int ShardCount => 8;
    }

    private sealed class Harness
    {
        public WorldScriptCommandHandler Handler = null!;
        public Mock<IGmCommandGrain> Grain = null!;
        public FakeDispatcher Dispatcher = new();
        public string AuthToken = string.Empty;
        public GmScriptTicket? CapturedTicket;
    }

    private static Harness Build(
        bool allowed = true,
        bool delivered = true,
        string secretKey = "unit-test-secret-key",
        bool adminGrantAllowed = true,
        string adminGrantError = "")
    {
        var provider = new UserAuthTokenProvider(secretKey, NullLogger<UserAuthTokenProvider>.Instance);
        var harness = new Harness
        {
            AuthToken = provider.GenerateToken("passport-1", "machine-1", TokenCharacterId),
            Dispatcher = { Delivered = delivered },
        };

        var grain = new Mock<IGmCommandGrain>();
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IGmCommandGrain>(0L, It.IsAny<string>())).Returns(grain.Object);

        grain.Setup(g => g.TryConsumeScriptAsync(It.IsAny<GmScriptTicket>()))
            .ReturnsAsync((GmScriptTicket ticket) =>
            {
                harness.CapturedTicket = ticket;
                var verdict = new GmScriptVerdict
                {
                    Allowed = allowed,
                    Reason = allowed ? ScriptRejectReason.None : ScriptRejectReason.InsufficientPermission,
                    Message = allowed ? "已受理。" : "缺少权限位。",
                    RemainingBudget = 59,
                    EffectiveFlags = (uint)ScriptPermissionFlags.SpawnMonster,
                };
                if (allowed)
                {
                    // 真实签发链路由 Grain 完成；这里给一个字段齐备的令牌即可——
                    // 网关不持密钥时按设计跳过自检，由 DS 的强制验签兜底。
                    verdict.Token = new ScriptGrantToken
                    {
                        Nonce = 1,
                        OperatorCharacterId = ticket.OperatorCharacterId,
                        Op = ticket.Op,
                        Kind = ticket.Kind,
                        TemplateId = 1,
                        TargetShardId = ticket.OperatorShardId,
                        X = ticket.X,
                        Y = ticket.Y,
                        Z = ticket.Z,
                        Count = ticket.Count,
                    };
                }
                return verdict;
            });

        // 授权管理分支的三条出口：授予/回收/审计。断言它们被走到、且 DS 通道一次都没碰。
        grain.Setup(g => g.GrantScriptAsync(
                It.IsAny<long>(), It.IsAny<ScriptPermissionFlags>(), It.IsAny<TimeSpan>(), It.IsAny<long>(), It.IsAny<string>()))
            .ReturnsAsync((long target, ScriptPermissionFlags flags, TimeSpan ttl, long caller, string reason) =>
                new GmOperationResult
                {
                    Success = adminGrantAllowed,
                    ErrorMessage = adminGrantAllowed ? string.Empty : adminGrantError,
                    OperationId = adminGrantAllowed ? "op-grant-1" : string.Empty,
                });

        grain.Setup(g => g.RevokeScriptAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()))
            .ReturnsAsync(new GmOperationResult { Success = true, OperationId = "op-revoke-1" });

        grain.Setup(g => g.QueryScriptAuditAsync(It.IsAny<long?>(), It.IsAny<int>()))
            .ReturnsAsync(new[]
            {
                new GmAuditEntry
                {
                    GmId = TokenCharacterId,
                    OperationType = "Script.Spawn.Monster",
                    TargetCharacterId = TokenCharacterId,
                    Details = "受理:已受理。 Template=墨麒麟 Shard=7",
                    Timestamp = new DateTime(2026, 10, 4, 1, 2, 3, DateTimeKind.Utc),
                },
            });

        harness.Grain = grain;
        harness.Handler = new WorldScriptCommandHandler(
            NullLogger<MessageHandlerBase>.Instance,
            cluster.Object,
            new HorizonMessageAdapter(),
            provider,
            new FakeShardRouter(),
            harness.Dispatcher,
            new ScriptedSpawnOptions { Enabled = true });
        return harness;
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

    private static GmScriptCommandMessage SpawnCommand() => new()
    {
        RequestId = 55,
        Op = ScriptOp.Spawn,
        Kind = ScriptedKind.Monster,
        TemplateKey = "墨麒麟",
        Count = 2,
    };

    /// <summary>
    /// 关键断言：票据里的操作者与 shard 都是网关从**令牌**与**服务端路由**推导的，
    /// 而不是消息体里任何客户端可写字段。
    /// </summary>
    [Fact]
    public async Task 操作者身份与shard由服务端推导()
    {
        var h = Build();

        var (ok, response) = await h.Handler.HandleAsync(null!, Packet(h.AuthToken, SpawnCommand()));

        Assert.NotNull(h.CapturedTicket);
        Assert.Equal(TokenCharacterId, h.CapturedTicket!.OperatorCharacterId);
        Assert.Equal(Shard, h.CapturedTicket.OperatorShardId);
        Assert.True(ok);
        Assert.Single(h.Dispatcher.Calls);
        Assert.Equal(Shard, h.Dispatcher.Calls[0].ShardId);
        Assert.Equal(TokenCharacterId, h.Dispatcher.Calls[0].Token.OperatorCharacterId);
        Assert.True(response is GmScriptResultMessage { Success: true });
    }

    [Fact]
    public async Task 无AuthToken一律拒绝且不触碰DS()
    {
        var h = Build();

        var (ok, response) = await h.Handler.HandleAsync(null!, Packet(string.Empty, SpawnCommand()));

        Assert.False(ok);
        Assert.Null(h.CapturedTicket);      // 连裁决都不该进
        Assert.Empty(h.Dispatcher.Calls);
        Assert.Equal(ScriptRejectReason.InsufficientPermission,
            Assert.IsType<GmScriptResultMessage>(response).Reason);
    }

    [Fact]
    public async Task 他签的令牌不认()
    {
        // 同一角色 ID，但由另一把密钥签发 ⇒ 验签不过 ⇒ 等同无身份。
        var foreign = new UserAuthTokenProvider("a-different-secret", NullLogger<UserAuthTokenProvider>.Instance)
            .GenerateToken("passport-1", "machine-1", TokenCharacterId);

        var h = Build();
        var (ok, _) = await h.Handler.HandleAsync(null!, Packet(foreign, SpawnCommand()));

        Assert.False(ok);
        Assert.Empty(h.Dispatcher.Calls);
    }

    [Fact]
    public async Task Grain拒绝时不下发DS并回传拒绝原因()
    {
        var h = Build(allowed: false);

        var (ok, response) = await h.Handler.HandleAsync(null!, Packet(h.AuthToken, SpawnCommand()));

        Assert.False(ok);
        Assert.Empty(h.Dispatcher.Calls);
        Assert.Equal(ScriptRejectReason.InsufficientPermission,
            Assert.IsType<GmScriptResultMessage>(response).Reason);
    }

    [Fact]
    public async Task DS不可用时如实回错而非谎报受理()
    {
        var h = Build(delivered: false);

        var (ok, response) = await h.Handler.HandleAsync(null!, Packet(h.AuthToken, SpawnCommand()));

        Assert.False(ok);
        Assert.Single(h.Dispatcher.Calls);  // 确实尝试过投递
        var result = Assert.IsType<GmScriptResultMessage>(response);
        Assert.Equal(ScriptRejectReason.Internal, result.Reason);
        Assert.Contains("未生效", result.Message);
    }

    [Fact]
    public async Task Query只查授权不占用DS通道()
    {
        var h = Build();
        var cmd = SpawnCommand();
        cmd.Op = ScriptOp.Query;

        var (ok, response) = await h.Handler.HandleAsync(null!, Packet(h.AuthToken, cmd));

        Assert.True(ok);
        Assert.Empty(h.Dispatcher.Calls);
        Assert.Equal(59, Assert.IsType<GmScriptResultMessage>(response).RemainingBudget);
    }

    [Fact]
    public async Task Grant走Grain且不产生令牌也不下发DS()
    {
        var h = Build();
        var cmd = SpawnCommand();
        cmd.Op = ScriptOp.Grant;
        cmd.TargetOperatorId = 5555;
        cmd.RequestedFlags = (uint)(ScriptPermissionFlags.SpawnMonster | ScriptPermissionFlags.SpawnNpc);
        cmd.TtlHours = 24;

        var (ok, response) = await h.Handler.HandleAsync(null!, Packet(h.AuthToken, cmd));

        Assert.True(ok);
        Assert.Empty(h.Dispatcher.Calls);      // 授权不是世界操作，绝不该碰 DS 通道
        Assert.Null(h.CapturedTicket);         // 也不该走生成类裁决（不占配额）

        var invocation = h.Grain.Invocations
            .First(i => i.Method.Name == nameof(IGmCommandGrain.GrantScriptAsync));
        var args = invocation.Arguments;
        Assert.Equal(5555L, args[0]);                                     // 目标角色
        Assert.Equal((ScriptPermissionFlags)(uint)(ScriptPermissionFlags.SpawnMonster | ScriptPermissionFlags.SpawnNpc), args[1]);
        Assert.Equal(TimeSpan.FromHours(24), args[2]);
        Assert.Equal(TokenCharacterId, args[3]);                          // 调用者 = 令牌里的角色，而非客户端自报
        Assert.True(Assert.IsType<GmScriptResultMessage>(response).Success);
    }

    [Fact]
    public async Task Grant被Grain拒绝时如实回原因且不触碰DS()
    {
        var h = Build(adminGrantAllowed: false, adminGrantError: "调用者无 GmOps 权限。");
        var cmd = SpawnCommand();
        cmd.Op = ScriptOp.Grant;
        cmd.TargetOperatorId = 5555;
        cmd.RequestedFlags = (uint)ScriptPermissionFlags.SpawnMonster;

        var (ok, response) = await h.Handler.HandleAsync(null!, Packet(h.AuthToken, cmd));

        Assert.False(ok);
        Assert.Empty(h.Dispatcher.Calls);
        var result = Assert.IsType<GmScriptResultMessage>(response);
        Assert.Equal(ScriptRejectReason.InsufficientPermission, result.Reason);
        Assert.Contains("调用者无 GmOps 权限", result.Message);
    }

    [Fact]
    public async Task Audit把审计条目作为文本回给操作者且不下发DS()
    {
        var h = Build();
        var cmd = SpawnCommand();
        cmd.Op = ScriptOp.Audit;

        var (ok, response) = await h.Handler.HandleAsync(null!, Packet(h.AuthToken, cmd));

        Assert.True(ok);
        Assert.Empty(h.Dispatcher.Calls);
        var result = Assert.IsType<GmScriptResultMessage>(response);
        Assert.Contains("Script.Spawn.Monster", result.Message);
        Assert.Contains("墨麒麟", result.Message);
    }

    [Fact]
    public async Task Revoke省略目标时作用于本人()
    {
        var h = Build();
        var cmd = SpawnCommand();
        cmd.Op = ScriptOp.Revoke;

        var (ok, _) = await h.Handler.HandleAsync(null!, Packet(h.AuthToken, cmd));

        Assert.True(ok);
        var invocation = h.Grain.Invocations
            .First(i => i.Method.Name == nameof(IGmCommandGrain.RevokeScriptAsync));
        Assert.Equal(TokenCharacterId, invocation.Arguments[0]);   // 目标缺省 = 令牌角色
        Assert.Equal(TokenCharacterId, invocation.Arguments[1]);   // 调用者同样来自令牌
        Assert.Empty(h.Dispatcher.Calls);
    }
}
