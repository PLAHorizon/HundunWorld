using System;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Planet;
using Horizon.Planet.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;

namespace PlanetGateProbe
{
    /// <summary>
    /// 对着<em>真在跑的 Silo</em>打一次落位 + 邻区迁移，并把口径/地面两条闸门的激活状态从日志里对出来。
    ///
    /// <para>为什么需要它：`PlanetRegionGrain` 的两条闸门（§6.2 口径、§6.1 地面）是<em>可选注入</em>的，
    /// 漏注册时不会编译失败也不会报错，只会静默不生效；而它们的激活状态只有在**第一次激活**时才写进日志。
    /// 单测用 TestCluster 证过注入，但没有证过"真 Silo + 真产物目录"这一条——那一步要一个客户端。</para>
    /// <para>它做的事和游戏客户端第一次进世界完全一样：用基场算出 35 字节口径 → BindAsync → 对邻区发起
    /// TransferAsync → 检查回执的 Applied 与 ack 三件套（局部厘米 + 原点）。区别只是它不渲染。</para>
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string demRoot = args.Length > 0
                ? args[0]
                : Environment.GetEnvironmentVariable("PLANET_DEM_ROOT") ?? "Data/GEBCO_2026/derived";

            // 1) 客户端这一侧的口径：与游戏客户端同一条装配（manifest→grid→profile→hybrid→sampler）
            if (!PlanetHeightField.TryCreateFromDirectory(demRoot, out PlanetHeightField field, out string fieldWhy))
            {
                Console.WriteLine("FIELD_FAIL " + fieldWhy);
                Console.WriteLine("verdict=FAIL");
                return 2;
            }
            var caliber = field.Caliber;
            var caliberBytes = new byte[PlanetHandshake.Size];
            if (!PlanetHandshake.TryWrite(caliberBytes, caliber, out int written) || written != PlanetHandshake.Size)
            {
                Console.WriteLine("CALIBER_ENCODE_FAIL written=" + written);
                Console.WriteLine("verdict=FAIL");
                return 2;
            }
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "caliber terrainVersion={0:x8} radius={1} level={2} root={3}",
                caliber.TerrainVersion, caliber.PlanetRadiusCm, caliber.RegionLevel, demRoot));

            // 2) 连真 Silo（cluster/service 与 Silo appsettings 一致）
            var builder = Host.CreateApplicationBuilder();
            builder.UseOrleansClient(client => client.UseLocalhostClustering(
                gatewayPort: 30000, serviceId: "BaseService", clusterId: "dev"));
            using var host = builder.Build();
            await host.StartAsync();
            var cluster = host.Services.GetRequiredService<IClusterClient>();
            var grain = cluster.GetGrain<IPlanetRegionGrain>(1);

            // 3) 落位：lat30/lon120 的海平面往上一公里（地面约束只拒"地下"，悬空是合法输入）
            // 角色号与 tick 每轮都换：grain 的状态是持久化的，用固定号会撞上上一轮的归属与时钟
            // （第一版就撞了 tick 倒退，bind=false 而回执 owner 还是上一轮那个区）。
            const int Level = PlanetConstants.DefaultRegionLevel;
            // 第二个参数可以给一个固定角色号：PIE 那边用 marker 的 char= 指定同一个号，
            // 于是"客户端真发的那条"能对着一个已知的绑定被受理（无登录验收的唯一办法）。
            ulong characterId = args.Length > 1
                ? ulong.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture)
                : 700_000UL + (ulong)(DateTime.UtcNow.Ticks % 100_000);
            // tick 必须高于 grain 里已持久化的时钟，否则 bind 会被判倒退。用挂钟毫秒
            // （自公元 1 年起的毫秒 ≈ 6.4e14）而不是帧号：帧号每次会话从 0 重来，跨会话必然倒退。
            long tick = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            var up = Geodetic.DirectionFromLatLon(30.0 * Math.PI / 180.0, 120.0 * Math.PI / 180.0);
            var homePoint = up * (PlanetConstants.PlanetRadiusCm + 100_000);
            var homeEcef = EcefCentimeters.FromVector(homePoint);
            var homeKey = RegionKey.FromDirection(up, Level);

            bool bound = await grain.BindAsync(characterId, homeEcef.X, homeEcef.Y, homeEcef.Z, tick, caliberBytes);
            Console.WriteLine("bind=" + (bound ? 1 : 0) + " character=" + characterId + " tick=" + tick + " home_region=" + homeKey);

            // bind-only：只落位、不迁移。给"客户端真发一条"准备一个停在 home 的受控绑定
            // （迁移过再跑，服务端的归属已经在邻区，客户端那条 from=home 会撞 OwnerMismatch）。
            if (args.Length > 2 && string.Equals(args[2], "bind-only", StringComparison.Ordinal))
            {
                Console.WriteLine("bind_only=1 home_region=" + homeKey);
                await host.StopAsync();
                Console.WriteLine("verdict=" + (bound ? "PASS" : "FAIL"));
                return bound ? 0 : 1;
            }

            // 4) 迁移到共边邻区的中心（邻区由库算，点也在邻区里；不手抄区域号）
            long applied = 0, accepted = 0, ackOk = 0;
            if (PlanetRegions.TryNeighbour(homeKey, RegionEdge.UPlus, out RegionKey eastKey))
            {
                var eastPoint = eastKey.CenterDirection() * (PlanetConstants.PlanetRadiusCm + 100_000);
                var eastEcef = EcefCentimeters.FromVector(eastPoint);
                var request = new PlanetRegionTransferRequest
                {
                    CharacterId = characterId,
                    FromRegionId = homeKey.ToId(),
                    ToRegionId = eastKey.ToId(),
                    GlobalXcm = eastEcef.X,
                    GlobalYcm = eastEcef.Y,
                    GlobalZcm = eastEcef.Z,
                    Tick = tick + 1,
                    PlanetCaliber = caliberBytes,
                };
                PlanetRegionTransferResult result = await grain.TransferAsync(request);
                accepted = result.Accepted ? 1 : 0;
                applied = result.Applied ? 1 : 0;
                // ack 对账：用回执里的局部厘米 + 区域帧还原全局点，应与发出去的点重合（≤1 cm）。
                // 局部点是星球帧的 (东, 上, 南)：目标取邻区中心，所以水平两格接近 0、高度那格约等于 +1 km——
                // "LocalX/LocalZ 非 0" 那种判法会把正确回执判成错（第一版就这么错过）。
                double ackErrCm = double.NaN;
                if (result.Applied && result.NewRegionId == eastKey.ToId())
                {
                    var frame = RegionFrame.Build(eastKey, PlanetConstants.PlanetRadiusCm);
                    var reconstructed = frame.GlobalFromLocalCm(
                        new PlanetVector3(result.LocalXCm, result.LocalYCm, result.LocalZCm));
                    ackErrCm = (reconstructed - eastPoint).Length;
                    ackOk = ackErrCm <= 1.0 ? 1 : 0;
                }
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "transfer accepted={0} applied={1} code={2} new_region={3} server_tick={4} reason={5}",
                    result.Accepted, result.Applied, result.Code, result.NewRegionId, result.ServerTick, result.Reason));
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "ack local=({0},{1},{2}) origin=({3},{4},{5}) err_cm={6:0.####} ack_ok={7}",
                    result.LocalXCm, result.LocalYCm, result.LocalZCm,
                    result.OriginXcm, result.OriginYcm, result.OriginZcm, ackErrCm, ackOk));
            }
            else
            {
                Console.WriteLine("neighbour_unavailable home_region=" + homeKey);
            }

            // 5) 快照（AOI 读路径，也带口径）：服务端应给回订阅集与归属
            PlanetRegionSnapshot snapshot = await grain.SnapshotAsync(characterId, caliberBytes);
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "snapshot accepted={0} code={1} owner={2} subs={3} visible={4} reason={5}",
                snapshot.Accepted, snapshot.Code, snapshot.OwnerRegionId,
                snapshot.SubscribedRegionIds.Length, snapshot.VisibleCharacterIds.Length, snapshot.Reason));

            await host.StopAsync();

            bool pass = bound && accepted == 1 && applied == 1 && ackOk == 1 && snapshot.Accepted;
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "GATE_PROBE bind={0} accepted={1} applied={2} ack_ok={3} snapshot={4}",
                bound ? 1 : 0, accepted, applied, ackOk, snapshot.Accepted ? 1 : 0));
            Console.WriteLine(pass ? "verdict=PASS" : "verdict=FAIL");
            return pass ? 0 : 1;
        }
    }
}
