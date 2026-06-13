using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Tests.Persistence;

public sealed class GameInfoTests
{
    [Fact]
    public void CanInstall_IsEnabledAgain_AfterFailedInstall()
    {
        var game = new GameInfo
        {
            IsRecommended = true,
            State = GameLifecycleState.Failed
        };

        Assert.True(game.CanInstall);
    }

    [Fact]
    public void HasLastOperationError_ReflectsStoredFailureReason()
    {
        var game = new GameInfo
        {
            LastOperationError = "磁盘空间不足"
        };

        Assert.True(game.HasLastOperationError);

        game.LastOperationError = string.Empty;

        Assert.False(game.HasLastOperationError);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "99+")]
    [InlineData(1000, "999+")]
    [InlineData(10000, "9k+")]
    public void OnlinePlayerCountText_UsesLauncherThresholds(int onlinePlayerCount, string expected)
    {
        var game = new GameInfo
        {
            OnlinePlayerCount = onlinePlayerCount
        };

        Assert.Equal(expected, game.OnlinePlayerCountText);
    }

    [Fact]
    public void TotalPlayCount_UsesAverageOfPassportAndCharacterEntries()
    {
        var game = new GameInfo
        {
            PassportLoginCount = 101,
            CharacterEnterCount = 200
        };

        Assert.Equal(151, game.TotalPlayCount);
        Assert.Equal("151", game.TotalPlayCountText);
    }
}
