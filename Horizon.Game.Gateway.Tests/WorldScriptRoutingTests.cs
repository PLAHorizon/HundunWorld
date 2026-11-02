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
using System.Linq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 脚本指令在网关**路由层**的命中规则。
/// <para>
/// <c>ClientPacketPipeline.ProcessMessageAsync</c> 是"遍历 handler，取**第一个**
/// <c>CanHandle</c> 且 <c>ValidateMessage</c> 通过的处理器"——首个匹配即胜出，
/// 所以两处出错都不会有报错，只会静默走错门：
/// ① 有第二个处理器也声明 <see cref="MessageType.ScriptCommand"/>；
/// ② 命中判定被 Header 里的自报字段带跑。
/// 本文件把这两点钉住，顺带钉住"服务端下行类型不会被客户端伪造进来"。
/// </para>
/// </summary>
public class WorldScriptRoutingTests
{
    private sealed class NoopDispatcher : IDsScriptDispatcher
    {
        public System.Threading.Tasks.Task<bool> DispatchAsync(long shardId, ScriptGrantToken token)
            => System.Threading.Tasks.Task.FromResult(true);
    }

    private static WorldScriptCommandHandler BuildHandler()
    {
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IGmCommandGrain>(0L, It.IsAny<string>()))
            .Returns(new Mock<IGmCommandGrain>().Object);

        return new WorldScriptCommandHandler(
            NullLogger<MessageHandlerBase>.Instance,
            cluster.Object,
            new HorizonMessageAdapter(),
            new UserAuthTokenProvider("routing-test-secret", NullLogger<UserAuthTokenProvider>.Instance),
            new SingleShardRouter(),
            new NoopDispatcher(),
            new ScriptedSpawnOptions { Enabled = true });
    }

    private sealed class SingleShardRouter : IShardRouter
    {
        public long Resolve(long characterId) => 1;
        public long Resolve(long zoneId, long characterId) => 1;
        public int ShardCount => 8;
    }

    private static HorizonMessagePacket Packet(MessageType headerType, MessageUnion body) => new()
    {
        ServiceType = body.ServiceType,
        Header = new MessageHeader { MessageType = headerType, ServiceType = body.ServiceType },
        Body = body,
    };

    [Fact]
    public void 脚本处理器只声明ScriptCommand且属系统服务()
    {
        var handler = BuildHandler();

        Assert.Equal(new[] { MessageType.ScriptCommand }, handler.MessageTypes.ToArray());
        Assert.Equal(ServiceType.System, handler.ServiceType);
    }

    [Fact]
    public void 正常脚本指令命中处理器()
    {
        var handler = BuildHandler();
        var packet = Packet(MessageType.ScriptCommand, new GmScriptCommandMessage { Op = ScriptOp.Spawn, Kind = ScriptedKind.Monster });

        Assert.True(handler.CanHandle(packet));
        Assert.True(handler.ValidateMessage(packet));
    }

    [Fact]
    public void Header冒充脚本指令但Body是别的类型_不命中()
    {
        // Header.MessageType 是客户端可自报的字段；命中判定必须以 Body 自带的权威 Type 为准，
        // 否则任何客户端都能靠改包头把消息塞进别人的处理器（或把别的消息伪装成脚本指令）。
        var handler = BuildHandler();
        var forged = Packet(MessageType.ScriptCommand, new GmScriptResultMessage { RequestId = 1, Success = true });

        Assert.False(handler.CanHandle(forged));
        Assert.False(handler.ValidateMessage(forged));
    }

    [Fact]
    public void 客户端伪造服务端下行结果类型_不命中()
    {
        // ScriptResult 是**服务端 → 客户端**的方向。客户端若原样发一份回来，
        // 不能被脚本处理器当成指令处理。
        var handler = BuildHandler();
        var packet = Packet(MessageType.ScriptResult, new GmScriptResultMessage { RequestId = 1, Success = true });

        Assert.False(handler.CanHandle(packet));
    }

    [Fact]
    public void Body服务类别不是系统_不命中()
    {
        // 同一个 MessageType 落在别的服务类别下也不该命中（路由是 (类型, 服务) 二元组）。
        var handler = BuildHandler();
        var body = new GmScriptCommandMessage { Op = ScriptOp.Query, Kind = ScriptedKind.None };
        body.ServiceType = ServiceType.Account;

        var packet = new HorizonMessagePacket
        {
            ServiceType = ServiceType.Account,
            Header = new MessageHeader { MessageType = MessageType.ScriptCommand, ServiceType = ServiceType.Account },
            Body = body,
        };

        Assert.False(handler.CanHandle(packet));
    }

    [Fact]
    public void Body缺失时才回退Header路由()
    {
        var handler = BuildHandler();
        var headerOnly = new HorizonMessagePacket
        {
            ServiceType = ServiceType.System,
            Header = new MessageHeader { MessageType = MessageType.ScriptCommand, ServiceType = ServiceType.System },
            Body = null!,
        };

        Assert.True(handler.CanHandle(headerOnly));
    }

    [Fact]
    public void 脚本授权查询不再占用独立消息类型()
    {
        // /scriptperm 走的是 ScriptCommand + Op=Query：一个入口一套校验。
        // 曾另立过 ScriptQuery=1111 却无人使用——死契约面会让人以为有第二条门。
        Assert.Null(System.Enum.GetNames(typeof(MessageType)).FirstOrDefault(n => n == "ScriptQuery"));
    }
}
