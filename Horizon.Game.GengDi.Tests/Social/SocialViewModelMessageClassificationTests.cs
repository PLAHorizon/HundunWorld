using System.Reflection;

using Horizon.Game.GengDi.Core.ViewModels;

using ClientMessage = Horizon.Game.GengDi.Models.IMMessage;

namespace Horizon.Game.GengDi.Tests.Social;

public sealed class SocialViewModelMessageClassificationTests
{
    [Fact]
    public void IsGroupMessage_UsesExplicitConversationFlag_InsteadOfNumericReceiverId()
    {
        var method = typeof(SocialViewModel).GetMethod("IsGroupMessage", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var numericPrivateMessage = new ClientMessage
        {
            SenderId = "10001",
            ReceiverId = "20002",
            IsGroupConversation = false
        };

        var numericGroupMessage = new ClientMessage
        {
            SenderId = "10001",
            ReceiverId = "30003",
            IsGroupConversation = true
        };

        var privateResult = Assert.IsType<bool>(method!.Invoke(null, new object?[] { numericPrivateMessage }));
        var groupResult = Assert.IsType<bool>(method.Invoke(null, new object?[] { numericGroupMessage }));

        Assert.False(privateResult);
        Assert.True(groupResult);
    }
}