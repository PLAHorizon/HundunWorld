using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using MemoryPack;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// <c>MessageUnion.Type</c> 的**成员隐藏陷阱**：基类在 MemoryPackOrder 255 上有一个 <c>Type</c>，
/// 每个具体消息类又自己声明一个同名 <c>Type</c>（隐藏，不是重写）。对象初始化器只写派生那一个，
/// 所以"用哪种静态类型读"直接决定读到真值还是读到基类的默认 <c>Unknown</c>。
/// <para>
/// 网关路由 <c>CanHandle</c> 走的是 <see cref="INetworkMessage"/> 引用（实测能拿到真值，指令才会被认领）；
/// 而任何按 <c>MessageUnion</c> 静态类型写 <c>x.Type</c> 的代码都会看到 <c>Unknown</c>。
/// 本文件把两件事一起钉住：路由依赖的读法有效，另一种读法无效——免得将来有人改成按基类读，
/// 让全部脚本指令静默"无人认领"。
/// </para>
/// </summary>
public class MessageUnionTypeMemberDispatchTests
{
    private static GmScriptCommandMessage Command() => new() { RequestId = 1 };

    [Fact]
    public void 路由依赖的接口读法拿到真实类型()
    {
        var cmd = Command();

        // 具体类型读与接口读一致 —— ClientPacketPipeline / CanHandle 正是靠这条才认领消息。
        Assert.Equal(MessageType.ScriptCommand, cmd.Type);
        Assert.Equal(MessageType.ScriptCommand, ((INetworkMessage)cmd).Type);

        // 经线格式往返后仍成立（网关收到的就是反序列化出来的对象）。
        var wire = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(
            MemoryPackSerializer.Serialize(new HorizonMessagePacket
            {
                ServiceType = ServiceType.System,
                Header = new MessageHeader { MessageType = MessageType.ScriptCommand, ServiceType = ServiceType.System },
                Body = cmd,
            }))!;

        Assert.Equal(MessageType.ScriptCommand, ((INetworkMessage)wire.Body).Type);
    }

    [Fact]
    public void 基类静态类型读法拿到的是被隐藏的空槽位()
    {
        var cmd = Command();

        // 不是 Unknown 也能用，而是它就是基类那个从未被赋值的属性（默认值）。
        // 想按基类读类型的代码必须改成读接口或先转型。
        Assert.Equal(default, ((MessageUnion)cmd).Type);
        Assert.NotEqual(cmd.Type, ((MessageUnion)cmd).Type);

        var wire = MemoryPackSerializer.Deserialize<MessageUnion>(MemoryPackSerializer.Serialize<MessageUnion>(cmd))!;
        Assert.Equal(default, wire.Type);
        Assert.IsType<GmScriptCommandMessage>(wire);   // 类型识别靠 union tag，与这个槽位无关
    }
}
