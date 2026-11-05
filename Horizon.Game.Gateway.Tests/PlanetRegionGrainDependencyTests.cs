using System;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Planet;
using Horizon.Planet.Wire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 两个星球来源是<em>可选</em>构造参数，因此"注册了但没注入"这一类错不会编译失败、不会激活失败，
/// 只会让两条闸门（§6.2 口径、§6.1 地面）在 production 里静默不生效。
/// <para>已有的文本扫描只能证明 <c>Program.cs</c> 里写了那两行 <c>AddSingleton</c>；
/// 证明不了 Orleans 的激活器真的把它们递进了构造函数。这里用真集群跑一次：
/// 装上替身 → 三条判定都该生效（口径不符拒、地下点拒、地上点放行）；
/// 什么都不装 → 同一条请求必须<em>放行</em>（那才是"没挂就不判"的本来面目）。
/// 两个方向都要有，否则"全拒的实现"也是绿的。</para>
/// </summary>
public class PlanetRegionGrainDependencyTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;

    private static readonly RegionKey Home = new(2, Level, 8192, 8192);

    private static PlanetVector3 Surface() => Home.CenterDirection() * Radius;

    private static PlanetHandshake Caliber() => new()
    {
        WireProtocol = PlanetHandshake.ProtocolVersion,
        PlanetRadiusCm = Radius,
        RegionLevel = (byte)Level,
        TerrainSeed = unchecked((long)0x0B1E_5EED_1234_5678UL),
        TerrainVersion = 0x5A5A_1234,
        BasefieldVersion = 1,
        BasefieldHashHi = unchecked((long)0x7777_8888_9999_AAAAUL),
    };

    private static byte[] CaliberBytes(PlanetHandshake caliber)
    {
        var bytes = new byte[PlanetHandshake.Size];
        Assert.True(PlanetHandshake.TryWrite(bytes, caliber, out int written));
        Assert.Equal(PlanetHandshake.Size, written);
        return bytes;
    }

    private sealed class StubCaliber : IPlanetCaliberSource
    {
        public bool TryGetCaliber(out PlanetHandshake caliber)
        {
            caliber = Caliber();
            return true;
        }
    }

    /// <summary>地面就按海平面算：低于海平面 1 m 以上的点必须被 §6.1 判成穿模。</summary>
    private sealed class StubGround : IPlanetGroundSource
    {
        public bool TryGetGroundElevationCm(in PlanetVector3 globalCm, out double elevationCm)
        {
            elevationCm = 0.0;
            return true;
        }

        public uint? TerrainVersion => Caliber().TerrainVersion;
        public string Describe() => "stub ground（海平面）";
    }

    /// <summary>把两个来源装进测试 silo；序列化配置与 <c>ZoneShard</c> 那套一致，否则启动就报 CodecNotFound。
    /// TestClusterBuilder 只接受<em>类型</em>（无参构造），所以这两个配置器不能带构造参数。</summary>
    public sealed class WithSources : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) => siloBuilder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("GameStore")
            .ConfigureServices(services =>
            {
                ZoneShardTestSerializerConfig.ConfigureSerializer(services);
                services.AddSingleton<IPlanetCaliberSource>(new StubCaliber());
                services.AddSingleton<IPlanetGroundSource>(new StubGround());
            });
    }

    public sealed class WithoutSources : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) => siloBuilder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("GameStore")
            .ConfigureServices(services => ZoneShardTestSerializerConfig.ConfigureSerializer(services));
    }

    public sealed class ClientConfig : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
            clientBuilder.ConfigureServices(services => ZoneShardTestSerializerConfig.ConfigureSerializer(services));
    }

    private static async Task<TestCluster> StartAsync<TSilo>() where TSilo : ISiloConfigurator, new()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TSilo>();
        builder.AddClientBuilderConfigurator<ClientConfig>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    [Fact]
    public async Task 装了两个来源之后_口径与地面都真的注进了grain()
    {
        var cluster = await StartAsync<WithSources>();
        try
        {
            var grain = cluster.GrainFactory.GetGrain<IPlanetRegionGrain>(1);
            var good = CaliberBytes(Caliber());
            var ecef = EcefCentimeters.FromVector(Surface());

            // 正向孪生：口径对上、点在海平面上（埋深 0）⇒ 必须放行。
            // 没有这一条，"实现把所有请求都拒掉"也是绿的。
            Assert.True(await grain.BindAsync(7, ecef.X, ecef.Y, ecef.Z, 100, good),
                        "口径对上且站在地面上却被拒：来源注入之后的判定不对");

            // 口径换一个字节都不许一样：来源没注进 grain 时这一步会照样放行（那就是本条判据要抓的错）
            var other = Caliber();
            other.TerrainVersion ^= 0x1;
            Assert.False(await grain.BindAsync(8, ecef.X, ecef.Y, ecef.Z, 101, CaliberBytes(other)),
                        "口径来源没被注入：grain 照收了别的星球的握手");

            // 地下 50 m：§6.1 的地面来源若没注入，这里会照样放行
            var below = Home.CenterDirection() * (Radius - 5_000);
            var belowEcef = EcefCentimeters.FromVector(below);
            Assert.False(await grain.BindAsync(9, belowEcef.X, belowEcef.Y, belowEcef.Z, 102, good),
                        "地面来源没被注入：地下点被收进了权威表");

            // 被拒的两条不许在表里留下痕迹：快照只该看得见那个合法落位的
            var snap = await grain.SnapshotAsync(9, good);
            Assert.True(!snap.Accepted || snap.OwnerRegionId == 0,
                        "被地面约束拒掉的落位在表里留下了归属：" + snap.Reason);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            cluster.Dispose();
        }
    }

    [Fact]
    public async Task 什么都没装的部署_同一个地下点照样放行_不拿0当地面()
    {
        // 与上一条成对：那条证明"装了会判"，这条证明"没装不判"。
        // 少了这条，把来源硬编码成"永远返回海平面"的实现也能让上一条全绿，
        // 而线上没配基场数据的部署会被它拒掉所有站在山上的人。
        var cluster = await StartAsync<WithoutSources>();
        try
        {
            var grain = cluster.GrainFactory.GetGrain<IPlanetRegionGrain>(2);
            var below = Home.CenterDirection() * (Radius - 5_000);
            var ecef = EcefCentimeters.FromVector(below);
            Assert.True(await grain.BindAsync(7, ecef.X, ecef.Y, ecef.Z, 100, Array.Empty<byte>()),
                        "没挂来源时该不判：拒收说明把取不到的海拔当成了 0");

            // 也不许带口径拒人：没来源就没有比的对象
            var other = CaliberBytes(Caliber());
            Assert.True(await grain.BindAsync(8, ecef.X, ecef.Y, ecef.Z, 101, other));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            cluster.Dispose();
        }
    }
}
