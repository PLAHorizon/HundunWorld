using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Security;
using Horizon.Game.Gateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 配置绑定与依赖注入注册面的守卫。
/// <para>
/// 网关与 Silo 的脚本配置是这样来的：<c>Configuration.GetSection("ScriptedSpawn").Get&lt;ScriptedSpawnOptions&gt;()</c>，
/// 而文档让用户设 <c>ScriptedSpawn__Enabled=1</c>。这两者是否真能接上、"1" 这种非 <c>true</c> 字面量
/// 能否绑定到 <c>bool</c>、数组 <c>__0/__1</c> 能否绑进 <c>long[]</c>——都不在裁决逻辑里，
/// 一旦不对，表现是"开关明明设了却全程被拒"，而日志只会说功能未开启。本文件把它钉住。
/// </para>
/// </summary>
[Collection(ScriptedSpawnEnvironmentCollection.Name)]
public class ScriptedSpawnConfigurationBindingTests
{
    private static readonly string KeyB64 = Convert.ToBase64String(Enumerable.Repeat((byte)0x5A, 32).ToArray());

    private static IConfigurationRoot BindFromEnvironment(Dictionary<string, string?> values)
    {
        var names = new[]
        {
            "ScriptedSpawn__Enabled", "ScriptedSpawn__SigningKeyBase64",
            "ScriptedSpawn__QuotaPerMinute", "ScriptedSpawn__MaxDistanceMetres",
            "ScriptedSpawn__BootstrapAdminIds__0", "ScriptedSpawn__BootstrapAdminIds__1",
        };
        foreach (var name in names) Environment.SetEnvironmentVariable(name, null);
        foreach (var pair in values) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        try
        {
            // 与网关/Silo 完全同一条路径：AddEnvironmentVariables + GetSection(...).Get<T>()。
            return new ConfigurationBuilder().AddEnvironmentVariables().Build();
        }
        finally
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void 环境变量能绑成脚本配置对象()
    {
        var config = BindFromEnvironment(new Dictionary<string, string?>
        {
            ["ScriptedSpawn__Enabled"] = "true",
            ["ScriptedSpawn__SigningKeyBase64"] = KeyB64,
            ["ScriptedSpawn__QuotaPerMinute"] = "7",
            ["ScriptedSpawn__MaxDistanceMetres"] = "35.5",
            ["ScriptedSpawn__BootstrapAdminIds__0"] = "123",
            ["ScriptedSpawn__BootstrapAdminIds__1"] = "456",
        });

        var bound = config.GetSection(ScriptedSpawnOptions.SectionName).Get<ScriptedSpawnOptions>()
            ?? new ScriptedSpawnOptions();

        Assert.True(bound.Enabled);
        Assert.Equal(KeyB64, bound.SigningKeyBase64);
        Assert.Equal(7, bound.QuotaPerMinute);
        Assert.Equal(35.5f, bound.MaxDistanceMetres);
        Assert.Equal(new long[] { 123, 456 }, bound.BootstrapAdminIds);
    }

    [Fact]
    public void 数字字面量1也必须能当成开关_因为文档就是这么写的()
    {
        // .NET 配置绑定 bool 只认真实字面量；"1" 会让绑定器**抛异常**（见下一条），
        // 而这段代码在 ConfigureServices 里，抛出 = 网关/Silo 启动失败。
        // DS 侧自己解析环境变量时接受 1/true/on/yes（ScriptedSpawnDiagnostics.FromEnvironment）。
        // 所以"同一对变量三个进程"必须共用一个开关口径，否则就是两种命运。
        var config = BindFromEnvironment(new Dictionary<string, string?>
        {
            ["ScriptedSpawn__Enabled"] = "1",
            ["ScriptedSpawn__SigningKeyBase64"] = KeyB64,
        });

        var options = ScriptedSpawnConfiguration.Bind(config, out var rawEnabledValue);

        Assert.True(options.Enabled);
        Assert.Equal("1", rawEnabledValue);
        Assert.Equal(KeyB64, options.SigningKeyBase64);
    }

    [Fact]
    public void 记录裸绑定器遇到1会抛异常这一事实()
    {
        // 这条不是为测试而测试：它是"为什么必须有 ScriptedSpawnConfiguration"的证据。
        // 哪天有人把 Bind 换回 section.Get<ScriptedSpawnOptions>()，这条会提醒他：
        // 一个 ScriptedSpawn__Enabled=1 就能让两个后端进程起不来。
        var config = BindFromEnvironment(new Dictionary<string, string?>
        {
            ["ScriptedSpawn__Enabled"] = "1",
        });

        var boom = Assert.Throws<InvalidOperationException>(() =>
            config.GetSection(ScriptedSpawnOptions.SectionName).Get<ScriptedSpawnOptions>());
        Assert.Contains("Boolean", boom.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 数值写错不崩进程且保留默认值()
    {
        var config = BindFromEnvironment(new Dictionary<string, string?>
        {
            ["ScriptedSpawn__Enabled"] = "true",
            ["ScriptedSpawn__QuotaPerMinute"] = "abc",
            ["ScriptedSpawn__MaxDistanceMetres"] = "五十",
            ["ScriptedSpawn__BootstrapAdminIds__0"] = "not-a-number",
            ["ScriptedSpawn__BootstrapAdminIds__1"] = "456",
        });

        var options = ScriptedSpawnConfiguration.Bind(config, out _);

        Assert.True(options.Enabled);
        Assert.Equal(60, options.QuotaPerMinute);          // 看不懂 → 用类内默认
        Assert.Equal(50f, options.MaxDistanceMetres);
        Assert.Equal(new long[] { 456 }, options.BootstrapAdminIds);  // 坏元素跳过，好元素留下
    }

    [Fact]
    public void 显式关闭值不会被当成开启()
    {
        var config = BindFromEnvironment(new Dictionary<string, string?>
        {
            ["ScriptedSpawn__Enabled"] = "0",
        });

        Assert.False(ScriptedSpawnConfiguration.Bind(config, out _).Enabled);
    }

    [Fact]
    public void 完全没有配置节时退化为关闭态默认值()
    {
        var config = BindFromEnvironment(new Dictionary<string, string?>());
        var options = ScriptedSpawnConfiguration.Bind(config, out var rawEnabledValue);

        Assert.Null(rawEnabledValue);
        Assert.False(options.Enabled);
        Assert.Equal(string.Empty, options.SigningKeyBase64);
        // 限额用类内默认值：漏配一项不该让裁决变严或变松到不可预期。
        Assert.Equal(60, options.QuotaPerMinute);
        Assert.Equal(10, options.MaxCountPerRequest);
    }

    [Fact]
    public void 网关的扫描式注册会把脚本处理器登记上去()
    {
        // ClientPacketPipeline 只认 IEnumerable<IMessageHandler>；注册走程序集扫描。
        // 扫描条件一改（比如换成按命名后缀筛），脚本指令就会在网关里"无人认领"，
        // 客户端只看到一条"无 handler 匹配消息"的告警。
        var services = new ServiceCollection();
        services.AddAllMessageHandlers(typeof(MessageHandlerBase).Assembly);

        var registered = services
            .Where(d => d.ServiceType == typeof(IMessageHandler))
            .Select(d => d.ImplementationType)
            .ToList();

        Assert.Contains(typeof(WorldScriptCommandHandler), registered);
    }
}
