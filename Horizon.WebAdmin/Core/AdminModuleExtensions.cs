using Microsoft.Extensions.DependencyInjection;

namespace Horizon.WebAdmin.Core;

public static class AdminModuleExtensions
{
    public static IServiceCollection AddAdminModule<T>(this IServiceCollection services)
        where T : IAdminModule, new()
    {
        var module = new T();
        module.RegisterServices(services);
        services.AddSingleton<IAdminModule>(module);
        return services;
    }
}
