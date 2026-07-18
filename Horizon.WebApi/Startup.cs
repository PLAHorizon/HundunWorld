using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.WebApi.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Swashbuckle.AspNetCore.SwaggerGen;
using log4net;
using System.Net;
using Horizon.Orleans.Interface;
using Newtonsoft.Json;
using Horizon.Core.Abstract;
using System.Text;
using NetCoreServer;
using Horizon.Core.Options;
using Microsoft.Extensions.Options;
using Horizon.WebApi.Identity;
using Horizon.WebApi.Identity.Users;
using Swashbuckle.AspNetCore.Filters;
using Horizon.WebApi.Configs;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.HttpOverrides;
using Horizon.Core.Security;
using Horizon.IoT.MQTT;

namespace Horizon.WebApi
{
    public class Startup
    {
        private static AdoNetOptions _options;
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<SocketEndpoint>(Configuration.GetSection("Socket-EndPoint"));
            services.Configure<AdoNetOptions>(Configuration.GetSection("AdoNetOptions"));
            services.Configure<ClusterOptions>(Configuration.GetSection("ClusterOptions"));
            services.Configure<PassportSecurityOptions>(Configuration.GetSection("PassportSecurityOptions"));

            // 配置转发头，使WebApi在反向代理后仍能获取真实客户端IP
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                // 从配置读取受信任的代理网络，未配置时仅信任回环地址（ASP.NET Core默认行为）
                var trustedNetworks = Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
                var trustedProxies = Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
                if (trustedNetworks != null)
                {
                    options.KnownNetworks.Clear();
                    // 自定义网络配置时，始终保留回环地址信任，确保本机反向代理（如nginx）的X-Forwarded-For能被正确处理
                    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Loopback, 8));
                    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.IPv6Loopback, 128));
                    foreach (var network in trustedNetworks)
                    {
                        var parts = network.Split('/');
                        if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix) && int.TryParse(parts[1], out var prefixLength))
                        {
                            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
                        }
                    }
                }
                if (trustedProxies != null)
                {
                    options.KnownProxies.Clear();
                    // 自定义代理配置时，始终保留回环地址信任
                    options.KnownProxies.Add(IPAddress.Loopback);
                    options.KnownProxies.Add(IPAddress.IPv6Loopback);
                    foreach (var proxy in trustedProxies)
                    {
                        if (IPAddress.TryParse(proxy, out var address))
                        {
                            options.KnownProxies.Add(address);
                        }
                    }
                }
            });

            #region IOC
            services.AddScoped<IPassportCurrentUser, PassportCurrentUser>();

            // 注册网关注册中心（用于客户端发现可用网关）
            services.AddSingleton<Horizon.Strategy.Storage.Redis.GatewayRegistry>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var connectionString = ResolveGatewayRegistryRedisConnectionString(config);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "未配置 Redis 连接字符串：请在 appsettings.json 中设置 Gateway:RedisConnectionString 或 DataBase:RedisMasters");
                }
                var logger = provider.GetRequiredService<ILogger<Horizon.Strategy.Storage.Redis.GatewayRegistry>>();
                return new Horizon.Strategy.Storage.Redis.GatewayRegistry(connectionString, logger: logger);
            });

            // 注册用户鉴权令牌提供器（与Game Gateway / IM Gateway共享同一密钥方案）
            services.AddSingleton<UserAuthTokenProvider>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var logger = provider.GetRequiredService<ILogger<UserAuthTokenProvider>>();
                var secretKey = config["Security:AuthTokenSecret"];
                if (string.IsNullOrWhiteSpace(secretKey))
                {
                    logger.LogWarning("未配置 Security:AuthTokenSecret，使用开发环境临时密钥。生产环境必须配置与 Game Gateway / IM Gateway 相同的密钥！");
                    secretKey = $"HundunWorld-Dev-Only-{Environment.MachineName}";
                }
                return new UserAuthTokenProvider(secretKey, logger);
            });
            #endregion

            services.AddControllers();
            //services.AddApiVersioning(o =>
            //{
            //    o.ReportApiVersions = true;
            //    o.AssumeDefaultVersionWhenUnspecified = true;

            //});

            services.AddSwaggerGen(option =>
            {                //分别注册v1和v2
                option.SwaggerDoc(ApiGroupName.Basic, new OpenApiInfo
                {
                    Version = ApiGroupName.Basic,
                    Title = "地平线基础Api",
                    Description = "地平线基础初始版本",
                    Contact = new OpenApiContact() { Name = "Long", Email = "" }
                });
                option.SwaggerDoc(ApiGroupName.Article, new OpenApiInfo
                {
                    Version = ApiGroupName.Article,
                    Title = "教学文章Api",
                    Description = "教学文章接口",
                    Contact = new OpenApiContact() { Name = "Long", Email = "" },
                }); option.SwaggerDoc(ApiGroupName.Account, new OpenApiInfo
                {
                    Version = ApiGroupName.Account,
                    Title = "用户Api",
                    Description = "用户接口",
                    Contact = new OpenApiContact() { Name = "Long", Email = "" },
                });
                option.SwaggerDoc(ApiGroupName.Games, new OpenApiInfo
                {
                    Version = ApiGroupName.Games,
                    Title = "游戏Api",
                    Description = "游戏管理接口",
                    Contact = new OpenApiContact() { Name = "Long", Email = "" },
                });

                option.DocInclusionPredicate((docName, apiDesc) =>
                {
                    var versions = apiDesc.CustomAttributes()
                        .OfType<ApiGroupAttribute>()
                        .Select(attr => attr.Name);

                    return versions.Any(v => $"{v}" == docName);
                });

                option.OperationFilter<RemoveVersionParameterOperationFilter>();
                option.DocumentFilter<SetVersionInPathDocumentFilter>();                //项目xml文档
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                // 获取xml文件路径
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                // 添加控制器的注释，true表示显示控制器注释
                option.IncludeXmlComments(xmlPath, true);
                var xmlFile2 = $"Horizon.Share.xml";
                // 获取xml文件路径
                var xmlPath2 = Path.Combine(AppContext.BaseDirectory, xmlFile2);
                option.IncludeXmlComments(xmlPath2, true);
                option.OperationFilter<AddResponseHeadersFilter>();
                option.OperationFilter<AppendAuthorizeToSummaryOperationFilter>();
                option.OperationFilter<SecurityRequirementsOperationFilter>();
                option.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
                {
                    Description = "标准的请求头时输入 Bearer，一个空格",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey
                });

            });

            services.AddIdentityServer(Configuration, "Authentications", "Authorizations");


        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // 必须在其他中间件之前调用，确保 RemoteIpAddress 正确反映真实客户端IP
            app.UseForwardedHeaders();

            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(60)
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();
            app.UseMiddleware<Horizon.WebApi.Middleware.AdminApiKeyMiddleware>();
            app.UseIdentityServer()
                .UseAuthentication()
                .UseAuthorization();
            app.UseMiddleware<Horizon.WebApi.Middleware.PaymentCallbackIpWhitelistMiddleware>();
            app.UseSwagger().UseSwaggerUI(s =>
            {
                s.SwaggerEndpoint($"/swagger/{ApiGroupName.Basic}/swagger.json", ApiGroupName.Basic);
                s.SwaggerEndpoint($"/swagger/{ApiGroupName.Article}/swagger.json", ApiGroupName.Article);
                s.SwaggerEndpoint($"/swagger/{ApiGroupName.Account}/swagger.json", ApiGroupName.Account);
                s.SwaggerEndpoint($"/swagger/{ApiGroupName.Games}/swagger.json", ApiGroupName.Games);
                s.RoutePrefix = string.Empty;
                s.EnablePersistAuthorization();
                s.OAuthConfigObject = new Swashbuckle.AspNetCore.SwaggerUI.OAuthConfigObject
                {


                };
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapGet("/mqtt", async context =>
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = await context.WebSockets.AcceptWebSocketAsync("mqtt");
                        var mqttClientProvider = context.RequestServices.GetRequiredService<IMqttClientProvider>();
                        var mqttClient = await mqttClientProvider.GetClientAsync();

                        var receiveTask = Task.Run(async () =>
                        {
                            var buffer = new byte[4096];
                            while (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
                            {
                                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                                if (result.CloseStatus.HasValue)
                                {
                                    await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, context.RequestAborted);
                                    break;
                                }
                            }
                        });

                        await receiveTask;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                });
            });            //配置静态文件
            app.UseStaticFiles();

            //运行时可以访问注册静态资源
            string fileUpload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Aessets");
            if (!Directory.Exists(fileUpload))
            { Directory.CreateDirectory(fileUpload); }
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(fileUpload),
                RequestPath = "/Aessets"
            });
        }

        /// <summary>
        /// 解析用于 GatewayRegistry 的 Redis 连接字符串。
        /// 优先顺序：Gateway:RedisConnectionString → DataBase:RedisMasters[0]。
        /// </summary>
        private static string ResolveGatewayRegistryRedisConnectionString(IConfiguration configuration)
        {
            var configured = configuration["Gateway:RedisConnectionString"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var primary = configuration.GetSection("DataBase:RedisMasters").GetChildren().FirstOrDefault();
            if (primary == null)
            {
                return string.Empty;
            }

            var host = primary["Host"];
            var port = primary["Port"];
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(port))
            {
                return string.Empty;
            }

            var password = primary["Password"];
            return string.IsNullOrWhiteSpace(password)
                ? $"{host}:{port}"
                : $"{host}:{port},password={password}";
        }



    }
}
