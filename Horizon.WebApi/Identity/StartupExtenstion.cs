using Horizon.Core.Options;
using IdentityModel.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace Horizon.WebApi.Identity
{
    public static class StartupExtenstion
    {

        /// <summary>
        /// Identity 认证授权配置
        /// </summary>
        /// <param name="service"></param>
        /// <param name="configuration"></param>
        /// <param name="authentication">认证配置字符</param>
        /// <param name="authorization">授权配置字符</param>
        /// <returns></returns>
        public static IServiceCollection AddIdentityServer(this IServiceCollection service, IConfiguration configuration, string authentication, string authorization)
        {

            service.Configure<AuthenticationOptions>(configuration.GetSection(authentication))
                   .Configure<AuthorizationOptions>(configuration.GetSection(authorization));
            var ao = configuration.GetSection(authentication).Get<AuthenticationOptions>();
            var azo = configuration.GetSection(authorization).Get<AuthorizationOptions>();

            service.AddIdentityServer(s =>
            {
                s.IssuerUri = azo.Authority;
            })
                //.AddSigningCredential("iHuaxiaX.rsa")
                .AddDeveloperSigningCredential(true, filename: "iHuaxiaX.rsa")
                .AddInMemoryIdentityResources(Config.GetIdentityResources())
                .AddInMemoryApiScopes(Config.GetScopes())
                .AddInMemoryApiResources(Config.GetApis())
                .AddInMemoryClients(Config.GetClients())
                .AddResourceOwnerValidator<ResourceOwnerPasswordValidator>()
                .AddProfileService<UserProfileService>();

            //context.Services.AddSingleton<IDeviceFlowStore, RedisDeviceFlowStore>();
            //context.Services.AddSingleton<IPersistedGrantStore, RedisPersistedGrantStore>();

            service.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {

                    options.Authority = azo.Authority;
                    options.Audience = azo.Audience;
                    options.RequireHttpsMetadata = true;
                    options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(10);//滑动时间
                });

            service.AddSingleton<IDiscoveryCache>(r =>
            {
                var factory = r.GetRequiredService<IHttpClientFactory>();
                return new DiscoveryCache(azo.Authority, () => factory.CreateClient(), new DiscoveryPolicy { RequireHttps = true });
            });
            return service;
        }


    }
}
