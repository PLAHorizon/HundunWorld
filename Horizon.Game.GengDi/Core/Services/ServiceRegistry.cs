using System;
using Microsoft.Extensions.DependencyInjection;

namespace Horizon.Game.GengDi.Core.Services;

/// <summary>
/// D1 — 客户端组合根（ServiceRegistry）。
/// <para>
/// 手工注册全部客户端服务（<c>App.CreateMainShell</c> 的替代/补充注入通道）。
/// 迁移纪律：现有 <c>Xxx.Instance</c> 静态单例仍可用（双轨期），本注册表把它们登记为
/// 单例便于测试与渐进式迁移；测试用 <see cref="BuildTestProvider(Action{IServiceCollection})"/>
/// 覆盖真实服务。</para>
/// </summary>
public static class ServiceRegistry
{
    /// <summary>生产组合根：全部服务按现有单例登记（新代码经构造注入消费）。</summary>
    public static IServiceProvider BuildProvider() => Configure(new ServiceCollection()).BuildServiceProvider();

    /// <summary>测试组合根：在 <paramref name="overrideRegistrations"/> 中替换服务实现。</summary>
    public static IServiceProvider BuildTestProvider(Action<IServiceCollection> overrideRegistrations)
    {
        var services = new ServiceCollection();
        Configure(services);
        overrideRegistrations(services);
        return services.BuildServiceProvider();
    }

    private static IServiceCollection Configure(IServiceCollection services)
    {
        services.AddSingleton<IUserSession, UserSession>();
        return services;
    }
}