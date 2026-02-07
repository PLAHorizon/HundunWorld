using AutoMapper;
using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Entities;
using Horizon.Strategy.Storage.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Orleans.Silo
{
    public static class SiloStartupExtension
    {
        public static IConfiguration? Configuration { get; private set; }
        /// <summary>
        /// 添加数据服务
        /// </summary>
        /// <param name="service"></param>
        public static IServiceCollection AddDataServiceProvider(this IServiceCollection service)
        {
            var methodInfo = typeof(SiloStartupExtension).GetMethod(nameof(AddEntity));
            if (methodInfo == null) return service; // Add null check for methodInfo

            foreach (var item in GetStorageAttributeClassType(DatabaseName.ModelAssembly, DatabaseName.Basic)) //基础数据
            {
                methodInfo.MakeGenericMethod(typeof(BasicEntityContext), item.Key, item.Value).Invoke(service, new object[] { service });
            }
            foreach (var item in GetStorageAttributeClassType(DatabaseName.ModelAssembly, DatabaseName.Game)) //游戏数据
            {
                methodInfo.MakeGenericMethod(typeof(GameEntityContext), item.Key, item.Value).Invoke(service, new object[] { service });
            }
            foreach (var item in GetStorageAttributeClassType(DatabaseName.ModelAssembly, DatabaseName.Article)) //文章数据
            {
                methodInfo.MakeGenericMethod(typeof(ArticleEntityContext), item.Key, item.Value).Invoke(service, new object[] { service });
            }
            foreach (var item in GetStorageAttributeClassType(DatabaseName.ModelAssembly, DatabaseName.Supports)) //点赞数据库
            {
                methodInfo.MakeGenericMethod(typeof(SupportsEntityContext), item.Key, item.Value).Invoke(service, new object[] { service });
            }
            foreach (var item in GetStorageAttributeClassType(DatabaseName.ModelAssembly, DatabaseName.Xingguang)) //星光数据库
            {
                methodInfo.MakeGenericMethod(typeof(XingguangEntityContext), item.Key, item.Value).Invoke(service, new object[] { service });
            }
            foreach (var item in GetStorageAttributeClassType(DatabaseName.ModelAssembly, DatabaseName.Xingguang)) //星光数据库
            {
                methodInfo.MakeGenericMethod(typeof(XingguangEntityContext), item.Key, item.Value).Invoke(null, new object[] { service });
            }
            return service;
        }

        /// <summary>
        /// redis
        /// </summary>
        /// <param name="service"></param>
        /// <returns></returns>
        public static IServiceCollection AddRedisServiceProvider(this IServiceCollection service)
        {
            Task.Run(() =>
            {
                var redis = Configuration?.GetSection("DataBase")?.Get<DataBase>()?.RedisMasters[0] ?? null;
                Cache.Current = redis != null ? new RedisCache($"password={redis.Password}@{redis.Host}:{redis.Port}")
                    : new RedisCache("password=DB65F7F9C@localhost:9379");
                // Cache.Current = new RedisCache("password=DB65F7F9C@localhost:9379");
                service.AddSingleton(Cache.Current);
            });

            return service;
        }

        /// <summary>
        /// 设置数据库
        /// </summary>
        /// <param name="database"></param>
        /// <param name="optionAction"></param>
        public static void SetDbContext(this DbContextOptionsBuilder optionAction, DatabaseInfo? database)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database), "DatabaseInfo cannot be null. Please check your DatabaseOptions configuration.");
            }
            
            if (string.IsNullOrEmpty(database.ConnectionString))
            {
                throw new InvalidOperationException($"ConnectionString for database type '{database.Type}' is null or empty. Please check your DatabaseOptions configuration.");
            }
            
            switch (database.Type)
            {
                default:
                case DataContextType.SqlServer:
                    optionAction.UseSqlServer(database.ConnectionString);
                    break;
                case DataContextType.Oracle:
                    throw new NotSupportedException("Oracle database provider is not currently supported.");
                case DataContextType.Mysql:
                    throw new NotSupportedException("MySQL database provider is not currently supported.");
                case DataContextType.Npgsql:
                    throw new NotSupportedException("Npgsql database provider is not currently supported. Please use SqlServer instead.");
            }
        }

        /// <summary>
        /// 获取配置文件信息
        /// </summary>
        /// <returns></returns>
        public static async Task<IConfiguration> GetConfiguration()
        {
            var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
            
            Configuration = await Task.FromResult(new ConfigurationBuilder()
                             .SetBasePath(basePath)
                             .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                             .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                             .Build());
            return Configuration ?? new ConfigurationBuilder().Build();
        }


        /// <summary>
        /// 配置选项
        /// </summary>
        /// <param name="service"></param>
        /// <returns></returns>
        public static IServiceCollection ConfigureOptions(this IServiceCollection service)
        {
            if (Configuration != null)
            {
                service.Configure<VerificationUserDataOptions>(Configuration.GetSection("VerificationUserDataOptions"));
            }
            return service;
        }

        /// <summary>
        /// 添加数据实体上下文服务实例
        /// </summary>
        /// <typeparam name="IContext"></typeparam>
        /// <typeparam name="TEntity"></typeparam>
        /// <typeparam name="K"></typeparam>
        /// <param name="service"></param>
        /// <returns></returns>
        public static IServiceCollection AddEntity<IContext, TEntity, K>(this IServiceCollection service) where IContext : DbContext where TEntity : BaseModel<K>
        {
            service.AddScoped<IDataContext<IContext, TEntity, K>>(s =>
            {
                var dbContext = s.GetService<IContext>();
                if (dbContext == null) throw new InvalidOperationException("DbContext not found");
                return FastActivator.Create<DataServiceProvide<IContext, TEntity, K>>(isnewInstance: true, dbContext);
            });
            return service;
        }

        /// <summary>  
        /// 获取程序集中的实现类对应的多个接口
        /// </summary>  
        /// <param name="assemblyName">程序集</param>
        /// <param name="storageName">存储数据库名</param>
        public static Dictionary<Type, Type> GetStorageAttributeClassType(string assemblyName, string storageName)
        {
            var result = new Dictionary<Type, Type>();
            if (!string.IsNullOrEmpty(assemblyName))
            {
                try
                {
                    Assembly assembly = Assembly.Load(assemblyName);
                    // 使用安全的方法获取类型
                    Type[] types = assembly.GetTypes();
                    foreach (var item in types.Where(s => !s.IsInterface && s.GetCustomAttribute<EntityStorageAttribute>()?.StorageName == storageName))//一个服务会继承N个接口
                    {
                        if (item.BaseType == null || item.BaseType.GenericTypeArguments.Length == 0)
                            continue;
                        var interfaceType = item.BaseType.GenericTypeArguments[0];
                        result.Add(item, interfaceType);
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 处理ReflectionTypeLoadException异常
                    Console.WriteLine($"[警告] 无法加载程序集 '{assemblyName}' 中的所有类型:");
                    
                    if (ex.LoaderExceptions != null)
                    {
                        foreach (var loaderException in ex.LoaderExceptions)
                        {
                            Console.WriteLine($"  - {loaderException?.Message}");
                        }
                    }
                    
                    // 尝试处理成功加载的类型
                    var loadedTypes = ex.Types.Where(t => t != null).ToArray();
                    foreach (var item in loadedTypes.Where(s => !s.IsInterface && s.GetCustomAttribute<EntityStorageAttribute>()?.StorageName == storageName))
                    {
                        if (item.BaseType == null || item.BaseType.GenericTypeArguments.Length == 0)
                            continue;
                        var interfaceType = item.BaseType.GenericTypeArguments[0];
                        result.Add(item, interfaceType);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 加载程序集 '{assemblyName}' 时发生异常: {ex.Message}");
                }
            }
            return result;
        }

        /// <summary>
        /// 获取服务器运行环境的外网IP地址
        /// </summary>
        /// <returns></returns>
        public static string GetExternalIpAddress()
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    return httpClient.GetStringAsync("https://api.ipify.org").Result;
                }
                catch
                {
                    return "127.0.0.1"; // Fallback IP
                }
            }
        }
        /// <summary>
        /// 获取一个有效的端口
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static int GetAvailablePort(int start, int end)
        {
            for (var port = start; port < end; ++port)
            {
                var listener = TcpListener.Create(port);
                listener.ExclusiveAddressUse = true;
                try
                {
                    listener.Start();
                    return port;
                }
                catch (SocketException)
                {
                    continue;
                }
                finally
                {
                    listener.Stop();
                }
            }

            throw new InvalidOperationException();
        }

        /// <summary>
        /// 添加mapper
        /// </summary>
        /// <param name="service"></param>
        /// <returns></returns>
        public static IServiceCollection AddMappingProfiles(this IServiceCollection service)
        {
            service.AddAutoMapper(config => config.AddMaps("Horizon.Mapper"));
            service.AddSingleton<IMapper>(sp => new AutoMapper.Mapper(sp.GetRequiredService<AutoMapper.IConfigurationProvider>(), sp.GetService));
            return service;
        }
    }
    public class CustomGrainStorageSerializer : IGrainStorageSerializer
    {
        

        BinaryData IGrainStorageSerializer.Serialize<T>(T input)
        {
            
            return BinaryData.FromBytes(MemoryPack.MemoryPackSerializer.Serialize(input));
        }

        public T? Deserialize<T>(BinaryData input)
        {
            if(input!=null)
            return   MemoryPack.MemoryPackSerializer.Deserialize<T>(input.ToArray());
            return default;
        }
    }
}
