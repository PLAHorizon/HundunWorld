using System;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Planet;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using TouchSocket.Sockets;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// <see cref="PlanetRegionHandler"/> 的判据：网关这一层只管"认包、认连接、把结果发回去"，
/// 但它同时是这条协议的<em>唯一入口</em>，所以三件事必须有判据：
/// ① 拒绝也要把结构化回执发下去（客户端分不开"被拒"和"丢包"就会猜着回滚）；
/// ② 伪造角色号在触碰 grain 之前就被拦下；
/// ③ 基础设施故障走 ErrorMessage 而不是区域码（两者客户端要分别处理：重试 vs 改请求）。
/// </summary>
public class PlanetRegionHandlerTests
{
    private const int Level = PlanetConstants.DefaultRegionLevel;
    private static readonly RegionKey Home = new(2, Level, 8192, 8192);
    private static readonly RegionKey East = PlanetRegions.TryNeighbour(Home, RegionEdge.UPlus, out var e)
        ? e : throw new InvalidOperationException("Home 没有 UPlus 邻居");

    private static PlanetRegionTransferRequest Request(ulong characterId = 7) => new()
    {
        CharacterId = characterId,
        FromRegionId = Home.ToId(),
        ToRegionId = East.ToId(),
        GlobalXcm = 1,
        GlobalYcm = 2,
        GlobalZcm = 3,
        Tick = 100,
    };

    private static HorizonMessagePacket Packet(MessageUnion body)
    {
        var meta = (INetworkMessage)body;
        return new HorizonMessagePacket
        {
            Header = new MessageHeader
            {
                MessageType = meta.Type,
                ServiceType = meta.ServiceType,
                CharacterId = body is PlanetRegionTransferRequest r ? r.CharacterId : 0UL,
                MessageId = "4242",
            },
            ServiceType = meta.ServiceType,
            Body = body,
        };
    }

    private static PlanetRegionTransferResult Result(PlanetRegionRejectCode code, bool accepted, bool applied) => new()
    {
        CharacterId = 7,
        Accepted = accepted,
        Applied = applied,
        Code = code,
        Reason = accepted ? "已受理" : "被拒",
        NewRegionId = accepted ? East.ToId() : Home.ToId(),
        HandoverTick = 100,
        ServerTick = 100,
    };

    private sealed class Harness
    {
        public Harness(Mock<IPlanetRegionGrain>? grain = null, ISessionBindingValidator? validator = null)
        {
            Grain = grain ?? new Mock<IPlanetRegionGrain>();
            var cluster = new Mock<IClusterClient>();
            cluster.Setup(c => c.GetGrain<IPlanetRegionGrain>(It.IsAny<long>(), It.IsAny<string>()))
                   .Returns(Grain.Object);
            Cluster = cluster;
            Client = new Mock<ITcpSessionClient>();
            Client.SetupGet(c => c.Id).Returns("conn-1");
            Handler = new PlanetRegionHandler(NullLogger<MessageHandlerBase>.Instance, cluster.Object,
                new HorizonMessageAdapter(), validator);
            // RouteHandlerAsync 直接调用时不经过 HandleAsync，连接态得自己摆好（会话绑定要读连接号）
            Handler._gameClient = Client.Object;
        }

        public Mock<IPlanetRegionGrain> Grain { get; }
        public Mock<IClusterClient> Cluster { get; }
        public Mock<ITcpSessionClient> Client { get; }
        public PlanetRegionHandler Handler { get; }
    }

    private static ISessionBindingValidator Validator(bool enabled, bool bound)
    {
        var mock = new Mock<ISessionBindingValidator>();
        mock.SetupGet(v => v.IsEnabled).Returns(enabled);
        mock.Setup(v => v.IsCharacterBoundToConnection(It.IsAny<string>(), It.IsAny<long>())).Returns(bound);
        return mock.Object;
    }

    [Fact]
    public async Task 受理的迁移由grain定夺并回结构化回执()
    {
        var h = new Harness();
        h.Grain.Setup(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()))
               .ReturnsAsync(Result(PlanetRegionRejectCode.None, accepted: true, applied: true));

        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(Request()));

        Assert.True(success);
        var body = Assert.IsType<PlanetRegionTransferResult>(packet.Body);
        Assert.True(body.Accepted);
        Assert.Equal(East.ToId(), body.NewRegionId);
        // 请求原样递给 grain：区域号与坐标都不许在网关被改写（改了服务端的反查就不再对着客户端那份事实）
        h.Grain.Verify(g => g.TransferAsync(It.Is<PlanetRegionTransferRequest>(
            r => r.FromRegionId == Home.ToId() && r.ToRegionId == East.ToId()
                 && r.GlobalXcm == 1 && r.Tick == 100)), Times.Once);
    }

    [Fact]
    public async Task 被拒的迁移同样下发回执_只是IsSuccess为false()
    {
        var h = new Harness();
        h.Grain.Setup(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()))
               .ReturnsAsync(Result(PlanetRegionRejectCode.NotAdjacent, accepted: false, applied: false));

        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(Request()));

        Assert.False(success);
        var body = Assert.IsType<PlanetRegionTransferResult>(packet.Body);
        Assert.Equal(PlanetRegionRejectCode.NotAdjacent, body.Code);
        Assert.False(string.IsNullOrWhiteSpace(body.Reason));
        Assert.Equal(Home.ToId(), body.NewRegionId);
        // 关键是"回执非 null"：基类 HandleAsync 只在包为 null 时跳过发送，
        // 这里返回 null 就等于把一次明确的拒绝变成客户端眼里的丢包
        Assert.NotNull(packet);
    }

    [Fact]
    public async Task 伪造角色号在触碰grain之前就被拦下()
    {
        var h = new Harness(validator: Validator(enabled: true, bound: false));
        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(Request(999)));

        Assert.False(success);
        var body = Assert.IsType<PlanetRegionTransferResult>(packet.Body);
        Assert.Equal(PlanetRegionRejectCode.SessionMismatch, body.Code);
        Assert.False(string.IsNullOrWhiteSpace(body.Reason));
        Assert.Equal(0, body.NewRegionId);                           // 网关不知道真归属，绝不照客户端的声明回
        h.Grain.Verify(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()), Times.Never);
        h.Cluster.Verify(c => c.GetGrain<IPlanetRegionGrain>(It.IsAny<long>(), It.IsAny<string>()), Times.Never);

        // 正向孪生：绑定对得上时同一条请求正常进 grain
        var ok = new Harness(validator: Validator(enabled: true, bound: true));
        ok.Grain.Setup(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()))
                .ReturnsAsync(Result(PlanetRegionRejectCode.None, accepted: true, applied: true));
        var (okSuccess, _) = await ok.Handler.RouteHandlerAsync(Packet(Request(999)));
        Assert.True(okSuccess);
        ok.Grain.Verify(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()), Times.Once);
    }

    [Fact]
    public async Task 校验器缺失或灰度关闭时如实放行()
    {
        // 与输入热路径同一口径：没有校验器（或未开启）不做"假装校验过"，也不因此拒绝
        foreach (var validator in new ISessionBindingValidator?[] { null, Validator(enabled: false, bound: false) })
        {
            var h = new Harness(validator: validator);
            h.Grain.Setup(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()))
                   .ReturnsAsync(Result(PlanetRegionRejectCode.None, accepted: true, applied: true));
            var (success, _) = await h.Handler.RouteHandlerAsync(Packet(Request()));
            Assert.True(success);
            h.Grain.Verify(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()), Times.Once);
        }
    }

    [Fact]
    public async Task 拿不到连接号时判不了就不假装校验过()
    {
        var validator = new Mock<ISessionBindingValidator>();
        validator.SetupGet(v => v.IsEnabled).Returns(true);
        validator.Setup(v => v.IsCharacterBoundToConnection(It.IsAny<string>(), It.IsAny<long>())).Returns(true);
        var h = new Harness(validator: validator.Object);
        h.Client.SetupGet(c => c.Id).Returns(string.Empty);          // 连接已散但 AsyncLocal 里还留着引用
        h.Grain.Setup(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()))
               .ReturnsAsync(Result(PlanetRegionRejectCode.None, accepted: true, applied: true));

        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(Request()));

        Assert.True(success);                                        // 放行，但理由是"判不了"，不是"校验过了"
        Assert.IsType<PlanetRegionTransferResult>(packet.Body);
        // 空连接号不该被拿去比对：拿空串问绑定关系会得到一个假失败，把正常玩家挡在门外
        validator.Verify(v => v.IsCharacterBoundToConnection(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task 包体类型不对时回400而不是硬造区域回执()
    {
        var h = new Harness();
        var (success, packet) = await h.Handler.RouteHandlerAsync(
            Packet(new PlanetRegionTransferResult { CharacterId = 7, Accepted = true }));

        Assert.False(success);
        var error = Assert.IsType<ErrorMessage>(packet.Body);
        Assert.Equal(400, error.ErrorCode);
        h.Grain.Verify(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()), Times.Never);
    }

    [Fact]
    public async Task grain故障走ErrorMessage而不是区域码()
    {
        var h = new Harness();
        h.Grain.Setup(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()))
               .ThrowsAsync(new TimeoutException("silo 不可达"));

        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(Request()));

        Assert.False(success);
        var error = Assert.IsType<ErrorMessage>(packet.Body);
        Assert.Equal(500, error.ErrorCode);
        Assert.Equal("4242", error.RelatedMessageId);
        // 正向孪生：同一条请求在 grain 正常时回的是区域回执而不是 ErrorMessage
        var ok = new Harness();
        ok.Grain.Setup(g => g.TransferAsync(It.IsAny<PlanetRegionTransferRequest>()))
               .ReturnsAsync(Result(PlanetRegionRejectCode.None, accepted: true, applied: true));
        var (_, okPacket) = await ok.Handler.RouteHandlerAsync(Packet(Request()));
        Assert.IsType<PlanetRegionTransferResult>(okPacket.Body);
    }

    [Fact]
    public void 处理器按网关扫描规则可被路由()
    {
        // Program.cs 用"实现 IMessageHandler 的具体类"扫 Horizon.Game.Core 程序集自动注册；
        // 这三条里任何一条不对，消息就会在网关静默没人接（客户端只等到超时）
        Assert.True(typeof(IMessageHandler).IsAssignableFrom(typeof(PlanetRegionHandler)));
        Assert.Equal(typeof(PlanetRegionHandler).Assembly, typeof(MessageHandlerBase).Assembly);
        var handler = new PlanetRegionHandler(NullLogger<MessageHandlerBase>.Instance,
            new Mock<IClusterClient>().Object, new HorizonMessageAdapter());
        Assert.Contains(MessageType.PlanetRegionTransfer, handler.MessageTypes);
        Assert.Equal(ServiceType.Game, handler.ServiceType);
        Assert.True(handler.CanHandle(Packet(Request())));
        Assert.False(handler.CanHandle(Packet(new PlanetRegionTransferResult())));   // 回执不是上行
        Assert.True(handler.ValidateMessage(Packet(Request())));
    }

    [Fact]
    public void 两条星球消息在union常量表里各占一格且指向正确类型()
    {
        Assert.True(UnionKinds.All.ContainsKey(256), "请求未登记 union tag 256");
        Assert.True(UnionKinds.All.ContainsKey(257), "回执未登记 union tag 257");
        Assert.Contains("PlanetRegionTransferRequest", UnionKinds.All[256].TypeName);
        Assert.Contains("PlanetRegionTransferResult", UnionKinds.All[257].TypeName);
        Assert.Equal((int)ServiceType.Game, UnionKinds.All[256].Service);
        Assert.Equal((int)ServiceType.Game, UnionKinds.All[257].Service);
        // 号段不许撞：这两格之前是空的（注册表曾经满过，才有 union tag 上界那次修正）
        Assert.NotEqual(UnionKinds.All[256].TypeName, UnionKinds.All[257].TypeName);
    }
    // ── §6.4 / §13.55 的只读快照那一支 ──────────────────────────────────────────

    private static PlanetRegionSnapshot Snapshot(bool accepted, PlanetRegionRejectCode code, string reason) => new()
    {
        CharacterId = 7,
        Accepted = accepted,
        Code = code,
        Reason = reason,
        OwnerRegionId = accepted ? Home.ToId() : 0,
        SubscribedRegionIds = accepted ? new[] { Home.ToId(), East.ToId() } : Array.Empty<long>(),
        VisibleCharacterIds = accepted ? new ulong[] { 9 } : Array.Empty<ulong>(),
        SubscribedBoundaryCells = accepted
            ? new[] { new PlanetCellKey { RegionId = East.ToId(), Cx = 3, Cz = -4 } }
            : Array.Empty<PlanetCellKey>(),
        ServerTick = 100,
    };

    private static PlanetRegionSnapshotRequest SnapshotRequest(byte[]? caliber = null) => new()
    {
        CharacterId = 7,
        PlanetCaliber = caliber ?? Array.Empty<byte>(),
    };

    [Fact]
    public async Task 快照请求带着口径原样进grain并回结构化回执()
    {
        var caliber = new byte[] { 1, 2, 3, 4, 5 };
        var h = new Harness();
        h.Grain.Setup(g => g.SnapshotAsync(It.IsAny<ulong>(), It.IsAny<byte[]>()))
               .ReturnsAsync(Snapshot(true, PlanetRegionRejectCode.None, "已受理：regions=2 visible=1 boundary_cells=1"));

        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(SnapshotRequest(caliber)));

        Assert.True(success);
        var body = Assert.IsType<PlanetRegionSnapshotResponse>(packet.Body);
        Assert.True(body.Accepted);
        Assert.Equal(PlanetRegionRejectCode.None, body.Code);
        // 摊平必须是逐字段的：客户端拿三个数组去加载邻区，少一个就静默加载不全
        Assert.Equal(new[] { Home.ToId(), East.ToId() }, body.SubscribedRegionIds);
        Assert.Equal(new ulong[] { 9 }, body.VisibleCharacterIds);
        var cell = Assert.Single(body.SubscribedBoundaryCells);
        Assert.Equal(East.ToId(), cell.RegionId);
        Assert.Equal(3, cell.Cx);
        Assert.Equal(-4, cell.Cz);

        // 口径字节不许在网关被动过：闸门在 grain，网关只有"转发"的权力
        h.Grain.Verify(g => g.SnapshotAsync(7, It.Is<byte[]>(b => b.SequenceEqual(caliber))), Times.Once);
    }

    [Fact]
    public async Task 快照被拒也发结构化回执_空数组与拒绝分得开()
    {
        // 这条是整个回执形状存在的理由：只回"空数组"的话，客户端分不清"周围没人"和"你没通过闸门"，
        // 而它给这两件事的正确处置完全不同（继续加载 vs 停手报警）
        var h = new Harness();
        h.Grain.Setup(g => g.SnapshotAsync(It.IsAny<ulong>(), It.IsAny<byte[]>()))
               .ReturnsAsync(Snapshot(false, PlanetRegionRejectCode.CaliberMismatch,
                   "客户端和服务端不是同一颗星球（口径不符，拒绝进入世界）：terrainVersion 1 != 2"));

        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(SnapshotRequest()));

        Assert.False(success);
        var body = Assert.IsType<PlanetRegionSnapshotResponse>(packet.Body);
        Assert.False(body.Accepted);
        Assert.Equal(PlanetRegionRejectCode.CaliberMismatch, body.Code);
        Assert.Contains("口径不符", body.Reason, StringComparison.Ordinal);
        Assert.NotEmpty(body.Reason);                       // 拒绝不许没有理由
        Assert.Empty(body.SubscribedRegionIds);             // 被拒时一格订阅都不带出去
        Assert.Empty(body.VisibleCharacterIds);
        Assert.Empty(body.SubscribedBoundaryCells);
    }

    [Fact]
    public async Task 快照越权在网关就拦下_不进grain()
    {
        // 快照回的是"谁在你附近"。伪造角色号来读它不是读到别人的数据那么简单——
        // 那是把别人的邻区清单拿走，所以这条安全判据必须在触碰 grain 之前。
        var h = new Harness(validator: Validator(enabled: true, bound: false));

        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(SnapshotRequest()));

        Assert.False(success);
        var body = Assert.IsType<PlanetRegionSnapshotResponse>(packet.Body);
        Assert.False(body.Accepted);
        Assert.Equal(PlanetRegionRejectCode.SessionMismatch, body.Code);
        Assert.Contains("角色号", body.Reason, StringComparison.Ordinal);
        h.Cluster.Verify(c => c.GetGrain<IPlanetRegionGrain>(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task 快照的基础设施故障走ErrorMessage而不是区域码()
    {
        // "silo 不可达"与"这条请求不该受理"要分开：前者客户端该重试，后者改请求也没用。
        // 混进区域码段里，客户端的处置策略就只能二选一。
        var h = new Harness();
        h.Grain.Setup(g => g.SnapshotAsync(It.IsAny<ulong>(), It.IsAny<byte[]>()))
               .Throws(new InvalidOperationException("silo 掉线"));

        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(SnapshotRequest()));

        Assert.False(success);
        var error = Assert.IsType<ErrorMessage>(packet.Body);
        Assert.Equal(500, error.ErrorCode);
        Assert.Contains("快照", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 两类上行共用一个处理器_包体不匹配仍走400()
    {
        // 共用一个处理器是为了让"会话绑定"这条安全判据只有一份实现；
        // 但共用不能把包体类型检查放宽——错类型的包必须还是 400，而不是静默走某一支。
        Assert.Contains(MessageType.PlanetRegionSnapshot, new PlanetRegionHandler(
            NullLogger<MessageHandlerBase>.Instance, new Mock<IClusterClient>().Object,
            new HorizonMessageAdapter()).MessageTypes);
        Assert.Contains(MessageType.PlanetRegionTransfer, new PlanetRegionHandler(
            NullLogger<MessageHandlerBase>.Instance, new Mock<IClusterClient>().Object,
            new HorizonMessageAdapter()).MessageTypes);

        var h = new Harness();
        var (success, packet) = await h.Handler.RouteHandlerAsync(Packet(new PlanetRegionTransferResult()));
        Assert.False(success);
        Assert.Equal(400, Assert.IsType<ErrorMessage>(packet.Body).ErrorCode);
        h.Cluster.Verify(c => c.GetGrain<IPlanetRegionGrain>(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

}
