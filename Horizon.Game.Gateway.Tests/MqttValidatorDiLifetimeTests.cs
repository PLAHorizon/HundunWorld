using System;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.IoT.MQTT;
using Horizon.Model.Flower;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// IoT MQTT 连接校验器的 DI 生命周期回归（captive dependency）。
/// <para>
/// <b>背景（实测缺陷）</b>：`MqttConnectionValidator` 被注册为**单例**，却直接注入**作用域**服务
/// `IDataContext&lt;FlowerEntityContext, FlowerIoTDevice, long&gt;`。后果有两个：
/// <list type="number">
///   <item>Silo 在 `Development` 环境下**启动即失败**
///         （`ValidateScopes` 拒绝 "Cannot consume scoped service ... from singleton ..."），
///         只能把环境切成 Production 才起得来——这让本地开发调试链路长期处于"必须绕过校验"的状态；</item>
///   <item>即使绕过校验，单例持有的 DbContext 会被**所有并发 MQTT 连接共享**（比启动失败更隐蔽）。</item>
/// </list>
/// </para>
/// <para>
/// 本用例用 `ValidateScopes + ValidateOnBuild` 复现并锁死该约束：只要有人把作用域服务重新塞回单例，
/// 这里立刻红灯，而不必等到启动 Silo 才发现。
/// </para>
/// </summary>
public class MqttValidatorDiLifetimeTests
{
    [Fact]
    public void MqttConnectionValidator_ResolvesUnderStrictScopeValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();

        // 与 Silo 中一致的注册形态：校验器是单例（由 MqttBrokerService 宿主服务长期持有）
        services.AddSingleton<MqttConnectionValidator>();
        // 被依赖的 IDataContext 是作用域服务
        services.AddScoped(_ => new Mock<IDataContext<FlowerEntityContext, FlowerIoTDevice, long>>().Object);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,   // 禁止单例消费作用域服务
            ValidateOnBuild = true,  // 构建期即校验整张图（与 Development 环境下的启动行为一致）
        });

        var validator = provider.GetRequiredService<MqttConnectionValidator>();
        Assert.NotNull(validator);
    }

    [Fact]
    public void CaptiveDependencyPattern_IsActuallyDetected_ByThisConfiguration()
    {
        // 反假绿：确认上面的校验配置**真的能**拦住 captive dependency——
        // 否则第一条用例可能只是"校验没生效"而恒绿。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddScoped(_ => new Mock<IDataContext<FlowerEntityContext, FlowerIoTDevice, long>>().Object);
        // 故意制造与服务曾经一模一样的缺陷形态：单例直接消费作用域服务
        services.AddSingleton<CaptiveProbe>();

        // ValidateOnBuild 会把校验错误包在 AggregateException 里（内部才是 InvalidOperationException），
        // 因此断言类型放宽到 Exception，但**必须断言消息内容**——否则"任何异常"都能让本探针变绿。
        var ex = Assert.ThrowsAny<Exception>(() =>
            services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            }).GetRequiredService<CaptiveProbe>());

        Assert.Contains("Cannot consume scoped service", ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>用于证明校验有效的探针（模拟修复前的 `MqttConnectionValidator` 形态）。</summary>
    private sealed class CaptiveProbe
    {
        public CaptiveProbe(IDataContext<FlowerEntityContext, FlowerIoTDevice, long> ctx) => _ = ctx;
    }
}
