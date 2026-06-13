using System.Reflection;

using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Tests.Social;

public sealed class SocialRosterOrderingTests
{
    [Fact]
    public void OrderFriendsForDisplay_PrioritizesAvailabilityThenUsername()
    {
        var method = typeof(SocialViewModel).GetMethod("OrderFriendsForDisplay", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var input = new[]
        {
            new User { Id = "3", Username = "zoe", Status = UserStatus.Offline },
            new User { Id = "4", Username = "amy", Status = UserStatus.Busy },
            new User { Id = "2", Username = "bella", Status = UserStatus.Online },
            new User { Id = "1", Username = "alex", Status = UserStatus.Online },
            new User { Id = "5", Username = "carl", Status = UserStatus.Invisible }
        };

        var ordered = Assert.IsType<List<User>>(method!.Invoke(null, new object?[] { input }));

        Assert.Equal(new[] { "alex", "bella", "amy", "carl", "zoe" }, ordered.Select(user => user.Username).ToArray());
    }

    [Fact]
    public void OrderFriendsForDisplay_PrioritizesUnreadAndRecentActivity()
    {
        var method = typeof(SocialViewModel).GetMethod("OrderFriendsForDisplay", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var input = new[]
        {
            new User { Id = "1", Username = "alex", Status = UserStatus.Online, LastMessageAt = new DateTime(2026, 4, 10, 11, 0, 0), UnreadCount = 0 },
            new User { Id = "2", Username = "bella", Status = UserStatus.Offline, LastMessageAt = new DateTime(2026, 4, 10, 12, 0, 0), UnreadCount = 2 },
            new User { Id = "3", Username = "carl", Status = UserStatus.Online, LastMessageAt = new DateTime(2026, 4, 10, 12, 30, 0), UnreadCount = 0 },
            new User { Id = "4", Username = "dora", Status = UserStatus.Busy, LastMessageAt = new DateTime(2026, 4, 10, 10, 30, 0), UnreadCount = 1 }
        };

        var ordered = Assert.IsType<List<User>>(method!.Invoke(null, new object?[] { input }));

        Assert.Equal(new[] { "bella", "dora", "carl", "alex" }, ordered.Select(user => user.Username).ToArray());
    }

    [Fact]
    public void OrderGroupsForDisplay_PrioritizesUnreadAndRecentActivity()
    {
        var method = typeof(SocialViewModel).GetMethod("OrderGroupsForDisplay", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var input = new[]
        {
            new Group { Id = "1", Name = "alpha", LastMessageAt = new DateTime(2026, 4, 10, 11, 0, 0), UnreadCount = 0 },
            new Group { Id = "2", Name = "beta", LastMessageAt = new DateTime(2026, 4, 10, 12, 0, 0), UnreadCount = 2 },
            new Group { Id = "3", Name = "gamma", LastMessageAt = new DateTime(2026, 4, 10, 12, 30, 0), UnreadCount = 0 }
        };

        var ordered = Assert.IsType<List<Group>>(method!.Invoke(null, new object?[] { input }));

        Assert.Equal(new[] { "beta", "gamma", "alpha" }, ordered.Select(group => group.Name).ToArray());
    }
}
