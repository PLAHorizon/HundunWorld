using System.Reflection;

using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Tests.Social;

public sealed class SocialConversationRoutingTests
{
    [Theory]
    [InlineData("123456789", false, false)]
    [InlineData("123456789", true, true)]
    [InlineData("friend-alpha", false, false)]
    public void ShouldUseGroupTransport_FollowsExplicitConversationKind(string receiverId, bool isGroupConversation, bool expected)
    {
        var method = typeof(SocialService).GetMethod("ShouldUseGroupTransport", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var actual = Assert.IsType<bool>(method!.Invoke(null, new object?[] { receiverId, isGroupConversation }));
        Assert.Equal(expected, actual);
    }
}