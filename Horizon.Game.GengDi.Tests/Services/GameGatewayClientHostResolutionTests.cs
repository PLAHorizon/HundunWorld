using System;
using System.Reflection;

using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Tests.Services;

public sealed class GameGatewayClientHostResolutionTests
{
    [Fact]
    public void ResolveHost_ReturnsLoopback_WhenEnvironmentVariableIsMissing()
    {
        using var scope = new EnvironmentVariableScope("HUNDUNWORLD_GAME_GATEWAY_HOST", null);

        var host = InvokeResolveHost();

        Assert.Equal("127.0.0.1", host);
    }

    [Fact]
    public void ResolveHost_UsesEnvironmentOverride_WhenProvided()
    {
        using var scope = new EnvironmentVariableScope("HUNDUNWORLD_GAME_GATEWAY_HOST", "10.24.8.16");

        var host = InvokeResolveHost();

        Assert.Equal("10.24.8.16", host);
    }

    private static string InvokeResolveHost()
    {
        var clientType = typeof(GameService).Assembly.GetType(
            "Horizon.Game.GengDi.Core.Services.GameGatewayClient",
            throwOnError: true)!;

        var resolveHostMethod = clientType.GetMethod(
            "ResolveHost",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(resolveHostMethod);

        var result = resolveHostMethod!.Invoke(null, null);
        return Assert.IsType<string>(result);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}