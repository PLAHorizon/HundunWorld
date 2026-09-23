using System;
using Horizon.Game.Gateway.Configuration;
using Microsoft.Extensions.Options;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 可运行时翻转的 <see cref="IOptionsMonitor{T}"/> 测试助手：
/// 替换 <see cref="CurrentValue"/> 即模拟配置热重载（IOptionsMonitor 运行时读取路径）。
/// 用于验证 UseDS 热切换（DsSyncDataPlane / DSRegistryMaintenanceHostedService）。
/// </summary>
public sealed class MutableOptionsMonitor : IOptionsMonitor<GatewayOptions>
{
    public GatewayOptions CurrentValue { get; set; }

    public MutableOptionsMonitor(GatewayOptions initial)
    {
        CurrentValue = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    public GatewayOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<GatewayOptions, string?> listener) => null;
}
