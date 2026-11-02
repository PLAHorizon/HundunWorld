using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.Games;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using TouchSocket.Sockets;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 外观落库后的出站推送：远端玩家不必重新进场就能看到对方的新外观。
/// 判据用"回读的权威副本"而不是"客户端提交的那份"，同时守住两条出口：
/// 成功保存要推给其他在线角色（且不含本人）；被服务端拒绝时一条都不许推。
/// </summary>
public class MetaHumanAppearancePushTests
{
    private const ulong OwnerId = 1001UL;

    [Fact]
    public async Task SuccessfulSave_PushesStoredLookToOtherOnlineCharactersOnly()
    {
        var harness = new Harness(appearanceAccepted: true);
        await harness.DriveUpdateAsync();

        // 正向夹具先证明收集器会记账，后面的"没推"才有意义。
        var push = Assert.Single(harness.Push.Pushes);
        var response = Assert.IsType<GetCharacterAppearanceResponse>(push.Message);
        Assert.True(response.Success);
        Assert.Equal(OwnerId, response.CharacterId);
        Assert.Equal(7, response.HairColor);

        // 推的是 grain 回读的权威副本（Blob 9,8,7），不是客户端提交的 1,2,3。
        Assert.NotNull(response.Look);
        Assert.Equal(new byte[] { 9, 8, 7 }, response.Look.Blob);
        Assert.True(response.Look.Published);
        Assert.Equal(new[] { 2002UL, 3003UL }, push.Targets.OrderBy(id => id));

        Assert.DoesNotContain(OwnerId, push.Targets);
        AssertAuthoritativeRead(harness);
    }

    [Fact]
    public async Task RejectedSave_PushesNothing()
    {
        var harness = new Harness(appearanceAccepted: false);
        await harness.DriveUpdateAsync();

        Assert.Empty(harness.Push.Pushes);
        // 被拒时连回读都不该发生：拒绝分支里任何"顺手推一下"的改动都会在这里变红。
        harness.Grain.Verify(g => g.GetAppearanceAsync(), Times.Never);
    }

    private static void AssertAuthoritativeRead(Harness harness)
    {
        // 推送必须回读权威副本：少这一步就可能把"客户端说存了什么"当成"服务端真存了什么"。
        harness.Grain.Verify(g => g.GetAppearanceAsync(), Times.Once);
        harness.Grain.Verify(g => g.UpdateAppearanceAsync(It.IsAny<MetaHumanLookPayload>()), Times.Once);
    }

    private sealed class Harness
    {
        public Mock<ICharacterGrain> Grain { get; } = new();
        public RecordingPushService Push { get; } = new();
        private readonly HorizonMessageAdapter _adapter = new();
        private readonly CharacterHandler _handler;
        private readonly Mock<ITcpSessionClient> _client = new();

        public Harness(bool appearanceAccepted)
        {
            var stored = new MetaHumanLookPayload
            {
                TemplateId = "metahuman.face_archetype+metahuman.body_base",
                Blob = new byte[] { 9, 8, 7 },
                BlobChecksum = 0x1234,
                Published = true,
            };

            Grain.Setup(g => g.UpdateAppearanceAsync(It.IsAny<MetaHumanLookPayload>()))
                .ReturnsAsync(new UpdateCharacterAppearanceResponse
                {
                    Success = appearanceAccepted,
                    Error = appearanceAccepted ? "" : "外观已发布，不可再改",
                    StoredRevision = 5,
                });
            Grain.Setup(g => g.GetAppearanceAsync()).ReturnsAsync(stored);
            Grain.Setup(g => g.GetCharacterInfoAsync(It.IsAny<GameQueryDto>()))
                .ReturnsAsync(new CharacterInfo { Appearance = new AppearanceInfo { HairColor = 7 } });

            var cluster = new Mock<IClusterClient>();
            cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>()))
                .Returns(Grain.Object);

            Push.Online.Add(OwnerId);
            Push.Online.Add(2002UL);
            Push.Online.Add(3003UL);

            _handler = new CharacterHandler(
                NullLogger<MessageHandlerBase>.Instance,
                cluster.Object,
                _adapter,
                pushService: Push);

            _client.SetupGet(c => c.Online).Returns(true);
            _client.Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public async Task DriveUpdateAsync()
        {
            var submitted = new MetaHumanLookPayload
            {
                TemplateId = "metahuman.face_archetype+metahuman.body_base",
                Blob = new byte[] { 1, 2, 3 },
                BlobChecksum = 0x999,
            };

            var packet = _adapter.CreateHorizonMessage(new UpdateCharacterAppearanceRequest
            {
                CharacterId = OwnerId,
                Look = submitted,
            });
            packet.Header.MessageId = "appearance-push-001";
            packet.Header.GameId = 1;
            packet.Header.ZoneId = 2;

            var (isSuccess, response) = await _handler.HandleAsync(_client.Object, packet);
            Assert.True(isSuccess);
            Assert.IsType<UpdateCharacterAppearanceResponse>(response);
        }
    }

    private sealed class RecordingPushService : IPlayerPushService
    {
        public List<ulong> Online { get; } = new();
        public List<(MessageUnion Message, List<ulong> Targets)> Pushes { get; } = new();

        public Task<IReadOnlyCollection<ulong>> GetOnlineCharacterIdsAsync()
        {
            return Task.FromResult<IReadOnlyCollection<ulong>>(Online.ToList());
        }

        public Task PushAsync<T>(T message, IReadOnlyCollection<ulong> targetCharacterIds)
            where T : MessageUnion, INetworkMessage
        {
            Pushes.Add((message, targetCharacterIds.ToList()));
            return Task.CompletedTask;
        }
    }
}
