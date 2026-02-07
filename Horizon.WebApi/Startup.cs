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

            #region IOC
            services.AddScoped<IPassportCurrentUser, PassportCurrentUser>();
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
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();
            app.UseIdentityServer()
                .UseAuthentication()
                .UseAuthorization();
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



    }
}
