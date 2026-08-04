using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.IM.Message.Network;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

// 应用程序集注册：确保 TestCluster 能发现 ZoneShardGrain 实现与接口契约。
// Horizon.Orleans.Grains — 包含 ZoneShardGrain 实现
// Horizon.Orleans.Interface — 包含 IZoneShardGrain / IZoneShardFanoutObserver 契约
// Horizon.Game.Message — 包含 WorldChunkDiffPacket / InputPacket 等同步消息类型
[assembly: Orleans.ApplicationPart("Horizon.Orleans.Grains")]
[assembly: Orleans.ApplicationPart("Horizon.Orleans.Interface")]
[assembly: Orleans.ApplicationPart("Horizon.Game.Message")]

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// ZoneShardGrain 集成测试专用 Silo 配置器。
/// 使用内存存储（"GameStore" / "Default"）替代 SqlServer，避免集成测试依赖外部数据库。
/// 配置 Orleans 序列化器加载同步消息程序集，确保 WorldChunkDiffPacket / InputPacket 等类型可跨 grain 边界序列化。
/// </summary>
/// <remarks>
/// 与 mock 单元测试（MultiClientSyncPerformanceTests）的差异：
/// - mock 测试：直接 new ZoneShardGrain，FanoutObserver 为同步内存调用，无序列化/RPC 开销
/// - 集成测试：通过 GrainFactory 激活 grain，走完整 Orleans 运行时（调度/序列化/消息路由），
///   observer 通过 CreateObjectReference 创建真实 IGrainObserver 引用
///
/// 序列化器配置与生产环境 Program.cs 对齐（Horizon.Orleans.Silo/Program.cs:ConfigureOrleansServices）：
/// - 注册 Horizon.Game.Message / Horizon.IM.Message / Horizon.Share 程序集
/// - 对 Horizon.Share 命名空间使用 NewtonsoftJson 回退序列化器（这些 DTO 未标注 [GenerateSerializer]）
/// </remarks>
public sealed class ZoneShardTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("GameStore")
            .ConfigureServices(services => ZoneShardTestSerializerConfig.ConfigureSerializer(services));
    }
}

/// <summary>
/// ZoneShardGrain 集成测试专用 Client 配置器。
/// Orleans 序列化器验证器在客户端和 Silo 两侧都会运行，
/// 因此客户端也需要注册相同的序列化器配置，否则启动时会抛出 CodecNotFoundException。
/// </summary>
public sealed class ZoneShardTestClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.ConfigureServices(services => ZoneShardTestSerializerConfig.ConfigureSerializer(services));
    }
}

/// <summary>
/// 共享序列化器配置：Silo 和 Client 两侧复用。
/// 与生产环境 Program.cs 配置对齐（Horizon.Orleans.Silo/Program.cs:ConfigureOrleansServices）。
/// </summary>
internal static class ZoneShardTestSerializerConfig
{
    public static void ConfigureSerializer(IServiceCollection services)
    {
        services.AddSerializer(serializerBuilder =>
        {
            serializerBuilder.AddAssembly(typeof(HorizonMessagePacket).Assembly);    // Horizon.Game.Message
            serializerBuilder.AddAssembly(typeof(IMGroupChatNotifyMessage).Assembly); // Horizon.IM.Message
            serializerBuilder.AddAssembly(typeof(ResultVM<>).Assembly);                // Horizon.Share
            // Horizon.Share 命名空间下的 DTO 未标注 [GenerateSerializer]，
            // 使用 NewtonsoftJson 回退序列化器（与生产环境一致）。
            serializerBuilder.AddNewtonsoftJsonSerializer(
                isSupported: type => type.Namespace != null && type.Namespace.StartsWith("Horizon.Share"));
        });
    }
}
