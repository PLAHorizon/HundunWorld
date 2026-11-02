using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 组队/帮派 Handler 的执行面：必须真的调用 Grain，失败时必须回真实失败，
/// 不能像旧实现那样永远回 Success=true（客户端会以为队伍/帮派已创建）。
/// </summary>
public class SocialHandlerTeamGuildTests
{
    private const ulong LeaderId = 8803UL;

    private static HorizonMessagePacket Packet(MessageUnion body, MessageType type) => new()
    {
        Header = new MessageHeader
        {
            MessageType = type,
            ServiceType = ServiceType.Social,
            CharacterId = (long)LeaderId,
        },
        Body = body,
    };

    private static ulong KeyToId(Guid key) => BitConverter.ToUInt64(key.ToByteArray(), 0);

    [Fact]
    public async Task 创建队伍_调用TeamGrain并回真实TeamInfo()
    {
        var team = new Mock<ITeamGrain>();
        team.Setup(g => g.CreateTeamAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        team.Setup(g => g.GetTeamInfoAsync()).ReturnsAsync(new TeamInfo
        {
            TeamId = 42UL,
            LeaderId = LeaderId,
            TeamName = "测试队",
            Members = new List<TeamMemberInfo> { new() { CharacterId = LeaderId, IsLeader = true } },
        });

        Guid capturedLeader = Guid.Empty;
        team.Setup(g => g.CreateTeamAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<Guid, string, string>((leader, _, _) => capturedLeader = leader)
            .ReturnsAsync(true);

        Guid capturedTeamKey = Guid.Empty;
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ITeamGrain>(It.IsAny<Guid>(), It.IsAny<string>()))
            .Callback<Guid, string>((key, _) => capturedTeamKey = key)
            .Returns(team.Object);

        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new CreateTeamRequest
        {
            LeaderId = LeaderId,
            TeamName = "测试队",
            TeamGoal = "测试目标",
        }, MessageType.Team));

        var response = Assert.IsType<CreateTeamResponse>(packet.Body);
        Assert.True(response.Success);
        Assert.Equal(42UL, response.TeamInfo.TeamId);
        Assert.Equal(LeaderId, KeyToId(capturedLeader));
        Assert.NotEqual(Guid.Empty, capturedTeamKey);
        Assert.NotEqual(0UL, KeyToId(capturedTeamKey));
    }

    [Fact]
    public async Task 创建队伍_Grain拒绝时不能回成功()
    {
        var team = new Mock<ITeamGrain>();
        team.Setup(g => g.CreateTeamAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ITeamGrain>(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(team.Object);

        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new CreateTeamRequest
        {
            LeaderId = LeaderId,
            TeamName = "重名队",
        }, MessageType.Team));

        var response = Assert.IsType<CreateTeamResponse>(packet.Body);
        Assert.False(response.Success);
        Assert.Contains("创建队伍失败", response.Message);
    }

    [Fact]
    public async Task 加入队伍_TeamId可逆地映射回Grain键()
    {
        const ulong teamId = 123456789UL;
        var team = new Mock<ITeamGrain>();
        team.Setup(g => g.JoinTeamAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        team.Setup(g => g.GetTeamInfoAsync()).ReturnsAsync(new TeamInfo { TeamId = teamId, LeaderId = LeaderId });

        Guid capturedTeamKey = Guid.Empty;
        Guid capturedMember = Guid.Empty;
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ITeamGrain>(It.IsAny<Guid>(), It.IsAny<string>()))
            .Callback<Guid, string>((key, _) => capturedTeamKey = key)
            .Returns(team.Object);
        team.Setup(g => g.JoinTeamAsync(It.IsAny<Guid>()))
            .Callback<Guid>(member => capturedMember = member)
            .ReturnsAsync(true);

        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new JoinTeamRequest
        {
            RequesterId = LeaderId,
            TeamId = teamId,
        }, MessageType.Team));

        var response = Assert.IsType<CreateTeamResponse>(packet.Body);
        Assert.True(response.Success);
        Assert.Equal(TeamOperation.Join, response.Operation);
        Assert.Equal(teamId, KeyToId(capturedTeamKey));
        Assert.Equal(LeaderId, KeyToId(capturedMember));
    }

    [Fact]
    public async Task 创建帮派_调用GuildGrain并回可寻址GuildId()
    {
        var guild = new Mock<IGuildGrain>();
        guild.Setup(g => g.CreateGuildAsync(It.IsAny<string>(), It.IsAny<Guid>())).ReturnsAsync(true);
        guild.Setup(g => g.GetGuildInfoAsync()).ReturnsAsync(new GuildInfo
        {
            GuildName = "测试帮",
            LeaderId = LeaderId,
            MemberCount = 1,
            MaxMembers = 50,
        });

        Guid capturedKey = Guid.Empty;
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IGuildGrain>(It.IsAny<Guid>(), It.IsAny<string>()))
            .Callback<Guid, string>((key, _) => capturedKey = key)
            .Returns(guild.Object);

        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new CreateGuildRequest
        {
            CreatorId = LeaderId,
            GuildName = "测试帮",
        }, MessageType.Guild));

        var response = Assert.IsType<CreateGuildResponse>(packet.Body);
        Assert.True(response.Success);
        Assert.True(response.GuildInfo.GuildId > 0);
        Assert.Equal((ulong)(uint)response.GuildInfo.GuildId, KeyToId(capturedKey));
    }

    [Fact]
    public async Task 申请入帮_GuildId可逆地映射回Grain键()
    {
        const int guildId = 1234;
        var guild = new Mock<IGuildGrain>();
        guild.Setup(g => g.ApplyToJoinAsync(It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
        guild.Setup(g => g.GetGuildInfoAsync()).ReturnsAsync(new GuildInfo { GuildName = "测试帮" });

        Guid capturedKey = Guid.Empty;
        Guid capturedMember = Guid.Empty;
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<IGuildGrain>(It.IsAny<Guid>(), It.IsAny<string>()))
            .Callback<Guid, string>((key, _) => capturedKey = key)
            .Returns(guild.Object);
        guild.Setup(g => g.ApplyToJoinAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .Callback<Guid, string>((member, _) => capturedMember = member)
            .ReturnsAsync(true);

        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new JoinGuildRequest
        {
            RequesterId = LeaderId,
            GuildId = guildId,
            ApplicationReason = "测试",
        }, MessageType.Guild));

        var response = Assert.IsType<JoinGuildResponse>(packet.Body);
        Assert.True(response.Success);
        Assert.Equal((ulong)(uint)guildId, KeyToId(capturedKey));
        Assert.Equal(LeaderId, KeyToId(capturedMember));
    }

    [Fact]
    public async Task 创建队伍_角色ID为0时不调用Grain()
    {
        var cluster = new Mock<IClusterClient>();
        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new CreateTeamRequest
        {
            LeaderId = 0UL,
            TeamName = "无效队",
        }, MessageType.Team));

        var response = Assert.IsType<CreateTeamResponse>(packet.Body);
        Assert.False(response.Success);
        cluster.Verify(c => c.GetGrain<ITeamGrain>(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task 申请入帮_帮派ID无效时不调用Grain()
    {
        var cluster = new Mock<IClusterClient>();
        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new JoinGuildRequest
        {
            RequesterId = LeaderId,
            GuildId = 0,
        }, MessageType.Guild));

        var response = Assert.IsType<JoinGuildResponse>(packet.Body);
        Assert.False(response.Success);
        cluster.Verify(c => c.GetGrain<IGuildGrain>(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task 离开队伍_按Operation分派到LeaveTeamAsync()
    {
        const ulong teamId = 777UL;
        var team = new Mock<ITeamGrain>();
        team.Setup(g => g.LeaveTeamAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ITeamGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(team.Object);

        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new CreateTeamRequest
        {
            Operation = TeamOperation.Leave,
            LeaderId = LeaderId,
            TeamId = teamId,
        }, MessageType.Team));

        var response = Assert.IsType<CreateTeamResponse>(packet.Body);
        Assert.True(response.Success);
        Assert.Equal(TeamOperation.Leave, response.Operation);
        team.Verify(g => g.LeaveTeamAsync(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task 踢人_按Operation分派并回新成员表()
    {
        const ulong teamId = 778UL;
        const ulong targetId = 99001UL;
        var team = new Mock<ITeamGrain>();
        team.Setup(g => g.KickMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(true);
        team.Setup(g => g.GetTeamInfoAsync()).ReturnsAsync(new TeamInfo { TeamId = teamId, LeaderId = LeaderId });
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ITeamGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(team.Object);

        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new CreateTeamRequest
        {
            Operation = TeamOperation.Kick,
            LeaderId = LeaderId,
            TeamId = teamId,
            TargetId = targetId,
        }, MessageType.Team));

        var response = Assert.IsType<CreateTeamResponse>(packet.Body);
        Assert.True(response.Success);
        Assert.Equal(teamId, response.TeamInfo.TeamId);
        team.Verify(g => g.KickMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task 转移队长_按Operation分派并回新队长()
    {
        const ulong teamId = 779UL;
        const ulong newLeader = 99002UL;
        var team = new Mock<ITeamGrain>();
        team.Setup(g => g.TransferLeaderAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(true);
        team.Setup(g => g.GetTeamInfoAsync()).ReturnsAsync(new TeamInfo { TeamId = teamId, LeaderId = newLeader });
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ITeamGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(team.Object);

        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new CreateTeamRequest
        {
            Operation = TeamOperation.Transfer,
            LeaderId = LeaderId,
            TeamId = teamId,
            TargetId = newLeader,
        }, MessageType.Team));

        var response = Assert.IsType<CreateTeamResponse>(packet.Body);
        Assert.True(response.Success);
        Assert.Equal(newLeader, response.TeamInfo.LeaderId);
        team.Verify(g => g.TransferLeaderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task 解散队伍_按Operation分派到DisbandTeamAsync()
    {
        const ulong teamId = 780UL;
        var team = new Mock<ITeamGrain>();
        team.Setup(g => g.DisbandTeamAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ITeamGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(team.Object);

        var handler = new SocialHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter());

        var (_, packet) = await handler.RouteHandlerAsync(Packet(new CreateTeamRequest
        {
            Operation = TeamOperation.Disband,
            LeaderId = LeaderId,
            TeamId = teamId,
        }, MessageType.Team));

        var response = Assert.IsType<CreateTeamResponse>(packet.Body);
        Assert.True(response.Success);
        Assert.Equal(TeamOperation.Disband, response.Operation);
        team.Verify(g => g.DisbandTeamAsync(It.IsAny<Guid>()), Times.Once);
    }
}
