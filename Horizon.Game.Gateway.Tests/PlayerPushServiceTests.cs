using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 出站推送的线路层：外观回执必须原样装帧发到目标角色的连接上；
/// 某个连接发送失败只影响它自己，后面的收件人不能跟着丢消息。
/// </summary>
public class PlayerPushServiceTests
{
    [Fact]
    public async Task PushAsync_PacksFrameOncePerTargetAndIsolatesSendFailures()
    {
        var adapter = new HorizonMessageAdapter();

        var ok1 = new Mock<IGameConnection>();
        var boom = new Mock<IGameConnection>();
        var ok2 = new Mock<IGameConnection>();

        var sent1 = new List<byte[]>();
        ok1.Setup(c => c.SendAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(frame => sent1.Add(frame))
            .Returns(Task.CompletedTask);

        boom.Setup(c => c.SendAsync(It.IsAny<byte[]>()))
            .ThrowsAsync(new InvalidOperationException("socket closed"));

        var sent2 = new List<byte[]>();
        ok2.Setup(c => c.SendAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(frame => sent2.Add(frame))
            .Returns(Task.CompletedTask);

        var connections = new Mock<IConnectionManager>();
        connections.Setup(c => c.GetConnectionByCharacterId(11)).Returns(ok1.Object);
        connections.Setup(c => c.GetConnectionByCharacterId(12)).Returns(boom.Object);
        connections.Setup(c => c.GetConnectionByCharacterId(13)).Returns(ok2.Object);

        var service = new PlayerPushService(connections.Object, adapter, NullLogger<PlayerPushService>.Instance);

        var payload = new MetaHumanLookPayload
        {
            TemplateId = "metahuman.face_archetype+metahuman.body_base",
            Blob = new byte[] { 4, 5, 6 },
            BlobChecksum = 0xABCD,
            Published = true,
        };

        await service.PushAsync(new GetCharacterAppearanceResponse
        {
            CharacterId = 77,
            Success = true,
            Look = payload,
            HairColor = 3,
        }, new List<ulong> { 11UL, 12UL, 13UL });

        // 抛异常的那个连接之后，第三个仍然收到 ⇒ try/catch 确实按连接隔离。
        Assert.Single(sent1);
        Assert.Single(sent2);

        var unpacked = adapter.UnpackMessage(sent1[0]);
        var response = Assert.IsType<GetCharacterAppearanceResponse>(unpacked.Body);
        Assert.Equal(77UL, response.CharacterId);
        Assert.True(response.Success);
        Assert.Equal(3, response.HairColor);
        Assert.Equal(new byte[] { 4, 5, 6 }, response.Look.Blob);
        Assert.Equal((uint)0xABCD, response.Look.BlobChecksum);
    }

    [Fact]
    public async Task PushAsync_SkipsTargetsWithoutLiveConnection()
    {
        var adapter = new HorizonMessageAdapter();
        var connections = new Mock<IConnectionManager>();
        // 离线角色：映射已注销，查到 null 不能抛。
        connections.Setup(c => c.GetConnectionByCharacterId(It.IsAny<long>())).Returns((IGameConnection?)null);

        var service = new PlayerPushService(connections.Object, adapter, NullLogger<PlayerPushService>.Instance);

        await service.PushAsync(new GetCharacterAppearanceResponse { CharacterId = 5, Success = true },
            new List<ulong> { 5UL });

        // 没有发送目标时"推成功"不代表发出去过；这里只要求不抛，真正的可达性由上一个用例守。
        connections.Verify(c => c.GetConnectionByCharacterId(5), Times.Once);
    }
}
