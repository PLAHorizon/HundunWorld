using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Tests.Social;

public sealed class SocialSuggestionFilteringTests
{
    [Fact]
    public void FilterSuggestedFriends_RemovesSelfExistingFriendsAndTemporaryUsers()
    {
        var allUsers = new[]
        {
            new User { Id = "self", Username = "self-player", Email = "self@example.com" },
            new User { Id = "friend-1", Username = "friend-one", Email = "friend@example.com" },
            new User { Id = "temp-1", Username = "im_smoke_alice_20260411", Email = "im_smoke_alice_20260411@local.test" },
            new User { Id = "temp-2", Username = "im_numeric_bob_20260411", Email = "im_numeric_bob_20260411@local.test" },
            new User { Id = "normal-1", Username = "alpha", Email = "alpha@example.com" },
            new User { Id = "normal-2", Username = "beta", Email = "beta@example.com" }
        };

        var filtered = SocialService.FilterSuggestedFriends(allUsers, "self", new[] { "friend-1" });

        Assert.Equal(new[] { "normal-1", "normal-2" }, filtered.Select(user => user.Id).ToArray());
    }
}