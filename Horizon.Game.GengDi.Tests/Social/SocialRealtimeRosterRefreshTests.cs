using System.Reflection;

using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

namespace Horizon.Game.GengDi.Tests.Social;

public sealed class SocialRealtimeRosterRefreshTests
{
    [Fact]
    public async Task HandleIncomingPrivateMessageAsync_ReordersFriendListAndUpdatesPrompt()
    {
        using var scope = new SocialDbScope();

        var viewModel = new SocialViewModel("20002");
        viewModel.Friends.Add(new User { Id = "10002", Username = "Bravo", Status = UserStatus.Online });
        viewModel.Friends.Add(new User { Id = "10001", Username = "Alpha", Status = UserStatus.Offline });

        var shouldAnimate = await viewModel.HandleIncomingPrivateMessageAsync(new IMPrivateChatNotifyMessage
        {
            ServerMessageId = "msg-private-1",
            SenderId = 10001,
            SenderName = "Alpha",
            ReceiverId = 20002,
            Content = "hello realtime",
            ContentType = IMContentType.Text,
            Timestamp = new DateTimeOffset(2026, 4, 13, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds()
        });

        Assert.True(shouldAnimate);
        Assert.Equal("10001", viewModel.Friends[0].Id);
        Assert.Equal(1, viewModel.Friends[0].UnreadCount);
        Assert.Equal("hello realtime", viewModel.Friends[0].RecentMessagePreview);
        Assert.Contains("Alpha", viewModel.ActionStatusMessage);
    }

    [Fact]
    public async Task HandleIncomingGroupMessageAsync_ReordersGroupListAndUpdatesPrompt()
    {
        using var scope = new SocialDbScope();

        var viewModel = new SocialViewModel("20002");
        viewModel.Groups.Add(new Group { Id = "50002", Name = "后勤群" });
        viewModel.Groups.Add(new Group { Id = "50001", Name = "先锋群" });

        var shouldAnimate = await viewModel.HandleIncomingGroupMessageAsync(new IMGroupChatNotifyMessage
        {
            ServerMessageId = "msg-group-1",
            SenderId = 10001,
            SenderName = "Alpha",
            GroupId = 50001,
            GroupName = "先锋群",
            Content = "group realtime",
            ContentType = IMContentType.Text,
            Timestamp = new DateTimeOffset(2026, 4, 13, 10, 5, 0, TimeSpan.Zero).ToUnixTimeMilliseconds()
        });

        Assert.True(shouldAnimate);
        Assert.Equal("50001", viewModel.Groups[0].Id);
        Assert.Equal(1, viewModel.Groups[0].UnreadCount);
        Assert.Equal("group realtime", viewModel.Groups[0].RecentMessagePreview);
        Assert.Contains("先锋群", viewModel.ActionStatusMessage);
    }

    [Fact]
    public async Task SendMessageAsync_DuplicateExplicitMessageId_UpdatesExistingLocalMessage()
    {
        using var scope = new SocialDbScope();

        var service = new SocialService();
        var timestamp = new DateTime(2026, 4, 13, 10, 10, 0, DateTimeKind.Local);

        var first = await service.SendMessageAsync(
            "10001",
            "20002",
            "first copy",
            MessageType.Text,
            isGroupConversation: false,
            messageIdOverride: "msg-duplicate-1",
            timestampOverride: timestamp,
            isReadOverride: true);

        var second = await service.SendMessageAsync(
            "10001",
            "20002",
            "updated copy",
            MessageType.Text,
            isGroupConversation: false,
            messageIdOverride: "msg-duplicate-1",
            timestampOverride: timestamp.AddSeconds(5),
            isReadOverride: true);

        var messages = await service.GetMessagesAsync("10001", "20002", 20);

        var stored = Assert.Single(messages);
        Assert.Equal("msg-duplicate-1", first.Id);
        Assert.Equal("msg-duplicate-1", second.Id);
        Assert.Equal("updated copy", stored.Content);
        Assert.Equal(timestamp.AddSeconds(5), stored.Timestamp);
        Assert.True(stored.IsRead);
    }

    [Fact]
    public void MessageRepository_Add_DuplicateId_MergesInsteadOfThrowing()
    {
        using var scope = new SocialDbScope();

        var repository = new MessageRepository();
        var original = new Horizon.Game.GengDi.Models.IMMessage
        {
            Id = "msg-repo-duplicate-1",
            SenderId = "10001",
            ReceiverId = "20002",
            Content = "first copy",
            Timestamp = new DateTime(2026, 4, 13, 10, 20, 0, DateTimeKind.Local),
            IsRead = false,
            Type = MessageType.Text
        };

        var duplicate = new Horizon.Game.GengDi.Models.IMMessage
        {
            Id = "msg-repo-duplicate-1",
            SenderId = "10001",
            ReceiverId = "20002",
            Content = "merged copy",
            Timestamp = original.Timestamp.AddSeconds(3),
            IsRead = true,
            Type = MessageType.Text
        };

        repository.Add(original);
        repository.Add(duplicate);

        var stored = repository.GetById("msg-repo-duplicate-1");

        Assert.NotNull(stored);
        Assert.Equal("merged copy", stored.Content);
        Assert.Equal(original.Timestamp.AddSeconds(3), stored.Timestamp);
        Assert.True(stored.IsRead);
    }

    [Fact]
    public async Task SelectFriendAsync_AwaitsListAndChatRefresh()
    {
        using var scope = new SocialDbScope();

        var service = new SocialService();
        await service.SendMessageAsync(
            "10001",
            "20002",
            "async refresh",
            MessageType.Text,
            isGroupConversation: false,
            messageIdOverride: "msg-select-friend-1",
            timestampOverride: new DateTime(2026, 4, 13, 10, 15, 0, DateTimeKind.Local));

        var viewModel = new SocialViewModel("20002");
        viewModel.Friends.Add(new User { Id = "10001", Username = "Alpha", Status = UserStatus.Online });

        await viewModel.SelectFriendAsync(viewModel.Friends[0]);

        Assert.Equal("10001", viewModel.SelectedFriendId);
        Assert.Equal("Alpha", viewModel.ActiveConversationState.Title);
        Assert.Equal("async refresh", viewModel.Friends[0].RecentMessagePreview);
        Assert.Equal(0, viewModel.Friends[0].UnreadCount);

        var messageItem = Assert.Single(viewModel.ActiveConversationState.Messages);
        Assert.Equal("async refresh", messageItem.Text);
    }

    private sealed class SocialDbScope : IDisposable
    {
        private static readonly FieldInfo InstanceTagField = typeof(SocialViewModel).Assembly
            .GetType("Horizon.Game.GengDi.Core.Services.ClientRuntimeContext", throwOnError: true)!
            .GetField("_instanceTag", BindingFlags.Static | BindingFlags.NonPublic)!;

        private readonly string _originalInstanceTag;
        private readonly string _directoryPath;

        public SocialDbScope()
        {
            DatabaseManager.CloseConnection();

            _originalInstanceTag = InstanceTagField.GetValue(null) as string ?? string.Empty;
            var instanceTag = $"tests-social-{Guid.NewGuid():N}";
            InstanceTagField.SetValue(null, instanceTag);

            _directoryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HundunWorld",
                "Instances",
                instanceTag);

            if (Directory.Exists(_directoryPath))
            {
                Directory.Delete(_directoryPath, true);
            }
        }

        public void Dispose()
        {
            DatabaseManager.CloseConnection();
            InstanceTagField.SetValue(null, _originalInstanceTag);

            if (Directory.Exists(_directoryPath))
            {
                try
                {
                    Directory.Delete(_directoryPath, true);
                }
                catch
                {
                }
            }
        }
    }
}