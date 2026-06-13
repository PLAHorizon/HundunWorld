using Horizon.Game.Message.Network;
using Horizon.Entities;
using Horizon.IM.Message.Network;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Silo;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

using System.IO;

[assembly: Orleans.ApplicationPart("Horizon.Orleans.Grains")]
[assembly: Orleans.ApplicationPart("Horizon.IM.Message")]
[assembly: Orleans.ApplicationPart("Horizon.Orleans.Interface")]

namespace Horizon.IM.Gateway.Tests;

public sealed class IMGatewayTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("GameStore")
            .ConfigureServices(services =>
            {
                services.AddSerializer(serializerBuilder =>
                {
                    serializerBuilder.AddAssembly(typeof(HorizonMessagePacket).Assembly);
                    serializerBuilder.AddAssembly(typeof(IMGroupChatNotifyMessage).Assembly);
                });

                services.AddDbContextPool<IMEntityContext>(options =>
                {
                    options.UseSqlServer(ResolveIMConnectionString());
                });

                services.AddDataServiceProvider();
            });
    }

    private static string ResolveIMConnectionString()
    {
        foreach (var basePath in GetRepositoryConfigBasePaths())
        {
            var repositoryPath = Path.Combine(basePath, "repository.json");
            if (!File.Exists(repositoryPath))
            {
                continue;
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("repository.json", optional: false)
                .Build();

            var connectionString = configuration.GetConnectionString("IMSqlServer");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }
        }

        throw new InvalidOperationException("未找到 IM 数据库连接字符串。请检查 repository.json 配置。");
    }

    private static IEnumerable<string> GetRepositoryConfigBasePaths()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var appBaseDirectory = AppContext.BaseDirectory;

        return new[]
        {
            currentDirectory,
            Path.Combine(currentDirectory, "Horizon.Entities"),
            Path.Combine(currentDirectory, "..", "Horizon.Entities"),
            Path.Combine(currentDirectory, "..", "..", "..", "..", "Horizon.Entities"),
            appBaseDirectory,
            Path.Combine(appBaseDirectory, "Horizon.Entities"),
            Path.Combine(appBaseDirectory, "..", "..", "..", "..", "Horizon.Entities")
        }
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}