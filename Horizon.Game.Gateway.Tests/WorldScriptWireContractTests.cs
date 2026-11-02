using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using MemoryPack;
using System;
using System.Collections.Generic;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 脚本指令在**客户端 ↔ 网关**这段真实链路上的线格式契约。
/// <para>
/// 网关的收发是 <c>MemoryPackSerializer.Serialize(packet)</c> / <c>Deserialize&lt;HorizonMessagePacket&gt;</c>，
/// 而 <see cref="HorizonMessagePacket.Body"/> 是抽象的 <see cref="MessageUnion"/>：
/// 具体类型靠 **union tag** 识别，本仓库的 tag 是手写登记的（见 <c>MessageUnion.cs</c> 的
/// <c>generated-union-block</c>）。漏登记时表现最差：要么序列化直接抛，要么对端解出别的类型，
/// 而这两者都发生在网络层，客户端只会看到"指令发出去没反应"。
/// 前面几组测试传的都是对象，从来没经过这一层，所以单独钉住。
/// </para>
/// </summary>
public class WorldScriptWireContractTests
{
    private static GmScriptCommandMessage FullCommand() => new()
    {
        RequestId = 4711,
        Op = ScriptOp.Spawn,
        Kind = ScriptedKind.TriggerZone,
        TemplateKey = "墨麒麟",
        Count = 3,
        HasPosition = true,
        X = 12.25f,
        Y = -3.5f,
        Z = 104.75f,
        TtlSeconds = 60,
        Radius = 7.125f,
        TriggerMode = ScriptTriggerMode.Repeat,
        CooldownSeconds = 15,
        TargetEntityId = 0xFFFF_FFFF_0000_0002UL,
        TargetOperatorId = 7002,
        RequestedFlags = (uint)ScriptPermissionFlags.GameMaster,
        TtlHours = 24,
        AuditLimit = 99,
        Type = MessageType.ScriptCommand,
        ServiceType = ServiceType.System,
    };

    [Fact]
    public void 整包上行往返保住具体类型与全部字段()
    {
        // 这就是 WorldScriptClient 提交、网关 ClientPacketPipeline 解出时经过的那一层。
        var packet = new HorizonMessagePacket
        {
            ServiceType = ServiceType.System,
            Header = new MessageHeader
            {
                MessageType = MessageType.ScriptCommand,
                ServiceType = ServiceType.System,
                // 身份唯一来源：没有这个字段，Grain 就无从判断"谁在下单"。
                AuthToken = "eyJhbGciOiJIUzI1NiJ9.wire-contract-test.signature",
            },
            Body = FullCommand(),
        };

        var bytes = MemoryPackSerializer.Serialize(packet);
        var back = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(bytes);

        var cmd = Assert.IsAssignableFrom<GmScriptCommandMessage>(back!.Body);
        Assert.Equal(MessageType.ScriptCommand, cmd.Type);
        Assert.Equal(ServiceType.System, cmd.ServiceType);
        Assert.Equal(4711, cmd.RequestId);
        Assert.Equal(ScriptOp.Spawn, cmd.Op);
        Assert.Equal(ScriptedKind.TriggerZone, cmd.Kind);
        Assert.Equal("墨麒麟", cmd.TemplateKey);
        Assert.Equal(3, cmd.Count);
        Assert.True(cmd.HasPosition);
        Assert.Equal(12.25f, cmd.X);
        Assert.Equal(-3.5f, cmd.Y);
        Assert.Equal(104.75f, cmd.Z);
        Assert.Equal(60, cmd.TtlSeconds);
        Assert.Equal(7.125f, cmd.Radius);
        Assert.Equal(ScriptTriggerMode.Repeat, cmd.TriggerMode);
        Assert.Equal(15, cmd.CooldownSeconds);
        Assert.Equal(0xFFFF_FFFF_0000_0002UL, cmd.TargetEntityId);
        Assert.Equal(7002, cmd.TargetOperatorId);
        Assert.Equal((uint)ScriptPermissionFlags.GameMaster, cmd.RequestedFlags);
        Assert.Equal(24, cmd.TtlHours);
        Assert.Equal(99, cmd.AuditLimit);

        Assert.Equal(packet.Header.AuthToken, back.Header.AuthToken);
    }

    [Fact]
    public void 以基类序列化也必须落回同一个具体类型()
    {
        // union tag 的登记情况只有"按抽象类型序列化"才测得出来：
        // 按具体类型序列化会走普通对象格式，漏登记也不会报错，而线上走的是抽象类型。
        MessageUnion body = FullCommand();

        var bytes = MemoryPackSerializer.Serialize(body);
        var back = MemoryPackSerializer.Deserialize<MessageUnion>(bytes);

        var cmd = Assert.IsType<GmScriptCommandMessage>(back);
        Assert.Equal(4711, cmd.RequestId);
        Assert.Equal(0xFFFF_FFFF_0000_0002UL, cmd.TargetEntityId);
    }

    [Fact]
    public void 下行结果包往返保住实体ID清单与配额()
    {
        MessageUnion body = new GmScriptResultMessage
        {
            RequestId = 4711,
            Success = true,
            Reason = ScriptRejectReason.None,
            Message = "已受理 3 只墨麒麟。",
            EntityIds = new List<ulong> { 0xFFFF_FFFF_0000_0001UL, 0xFFFF_FFFF_0000_0002UL, 0xFFFF_FFFF_0000_0003UL },
            RemainingBudget = 57,
            PermissionFlags = (uint)ScriptPermissionFlags.WorldDirector,
            Type = MessageType.ScriptResult,
            ServiceType = ServiceType.System,
        };

        var back = Assert.IsType<GmScriptResultMessage>(
            MemoryPackSerializer.Deserialize<MessageUnion>(MemoryPackSerializer.Serialize(body)));

        Assert.Equal(4711, back.RequestId);
        Assert.True(back.Success);
        Assert.Equal("已受理 3 只墨麒麟。", back.Message);
        Assert.Equal(3, back.EntityIds.Count);
        Assert.Equal(0xFFFF_FFFF_0000_0002UL, back.EntityIds[1]);
        Assert.Equal(57, back.RemainingBudget);
        Assert.Equal((uint)ScriptPermissionFlags.WorldDirector, back.PermissionFlags);
        Assert.Equal(MessageType.ScriptResult, back.Type);
    }

    [Fact]
    public void 脚本消息不能混用同一union登记位()
    {
        // 上行 238 / 下行 239 是两个方向；若哪天有人把同一个 tag 登记给两个类型，
        // 后一个会静默覆盖前一个的解析结果。这里用"解出的实际类型"来验。
        var up = MemoryPackSerializer.Deserialize<MessageUnion>(
            MemoryPackSerializer.Serialize<MessageUnion>(FullCommand()));
        var down = MemoryPackSerializer.Deserialize<MessageUnion>(
            MemoryPackSerializer.Serialize<MessageUnion>(new GmScriptResultMessage { RequestId = 1 }));

        Assert.IsType<GmScriptCommandMessage>(up);
        Assert.IsType<GmScriptResultMessage>(down);
    }

    [Fact]
    public void 客户端伪造服务端下行类型时网关仍按下行类型解析_不会当成指令()
    {
        // 方向由 Body 的实际 CLR 类型 + union tag 决定：路由层据此拒绝（见 WorldScriptRoutingTests）。
        // 这里保证线格式层面它确实是另一种类型，而不是指令的别名。
        var asResult = MemoryPackSerializer.Deserialize<MessageUnion>(
            MemoryPackSerializer.Serialize<MessageUnion>(new GmScriptResultMessage { RequestId = 1, Success = true }));

        Assert.IsNotType<GmScriptCommandMessage>(asResult);
        // 读类型要用接口或具体类型；按 MessageUnion 静态类型读 .Type 拿到的是被隐藏的基类空槽位
        // （详见 MessageUnionTypeMemberDispatchTests），这也是本条以前写成 asResult.Type 时失败的原因。
        Assert.Equal(MessageType.ScriptResult, ((INetworkMessage)asResult!).Type);
        Assert.Equal(default, asResult!.Type);
    }

    /// <summary>
    /// 裁决码是**线上整数**（<c>GmScriptResultMessage.Reason</c>），编号必须冻结。
    /// <para>
    /// 8 号（原 <c>WrongShard</c>）已删除：跨 shard 的判定在 DS 侧以字符串 reason 落日志，
    /// 从不作为 Grain 裁决码回给操作者——挂着一个永不产生的码，就会让人以为"分片不匹配会告诉操作者"。
    /// 删除时**不重排**后面的编号：陈旧客户端把 12 读成 9 比读到未知码更难查。
    /// </para>
    /// </summary>
    [Fact]
    public void 裁决码编号冻结且8号为已废弃空位()
    {
        Assert.Equal(0, (int)ScriptRejectReason.None);
        Assert.Equal(1, (int)ScriptRejectReason.Disabled);
        Assert.Equal(2, (int)ScriptRejectReason.NoGrant);
        Assert.Equal(3, (int)ScriptRejectReason.InsufficientPermission);
        Assert.Equal(4, (int)ScriptRejectReason.Expired);
        Assert.Equal(5, (int)ScriptRejectReason.UnknownTemplate);
        Assert.Equal(6, (int)ScriptRejectReason.InvalidArgument);
        Assert.Equal(7, (int)ScriptRejectReason.TooFar);
        Assert.Equal(9, (int)ScriptRejectReason.Cooldown);
        Assert.Equal(10, (int)ScriptRejectReason.QuotaExhausted);
        Assert.Equal(11, (int)ScriptRejectReason.EntityBudgetExceeded);
        Assert.Equal(12, (int)ScriptRejectReason.Internal);

        // 底层类型是 byte（本条第一次跑就把这点顶了出来：IsDefined 要求实参与底层类型同型）。
        Assert.False(Enum.IsDefined(typeof(ScriptRejectReason), (byte)8));
    }
}
