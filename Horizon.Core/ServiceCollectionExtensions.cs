using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Horizon.Core
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDbContext<TDbContext>(this IServiceCollection services, Func<TDbContext> factory, ServiceLifetime lifetime = ServiceLifetime.Scoped) where TDbContext : class
        {
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddSingleton<TDbContext>(sp => factory());
                    break;
                case ServiceLifetime.Scoped:
                    services.AddScoped<TDbContext>(sp => factory());
                    break;
                case ServiceLifetime.Transient:
                    services.AddTransient<TDbContext>(sp => factory());
                    break;
            }
            return services;
        }

        /// <summary>
        /// 动态注册服务
        /// </summary>
        public static IServiceCollection AddDynamicService<TService, TImplementation>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TService : class
            where TImplementation : class, TService
        {
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddSingleton<TService, TImplementation>();
                    break;
                case ServiceLifetime.Scoped:
                    services.AddScoped<TService, TImplementation>();
                    break;
                case ServiceLifetime.Transient:
                    services.AddTransient<TService, TImplementation>();
                    break;
            }
            return services;
        }

        /// <summary>
        /// 注册配置选项
        /// </summary>
        public static IServiceCollection AddConfigurationOptions<TOptions>(this IServiceCollection services, IConfiguration configuration, string sectionName) where TOptions : class
        {
            services.Configure<TOptions>(c => configuration.GetSection(sectionName));
            return services;
        }
    }
}
