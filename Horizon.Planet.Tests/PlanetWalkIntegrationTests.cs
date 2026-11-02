using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Horizon.Planet.Wire;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// P1 那一层的端到端走查：一条跨过许多区域（并跨过一次立方体面边界）的路径，
/// 全程只走"公开入口"——算区域、建帧、量化、编码、解码、迁移、重建位置、生成 patch、落盘、重启读回。
/// <para>各单元单独都有判据，但"跨区不丢实体 / 位置误差有界"是这条链的性质，只能串起来量。
/// 单元测试全绿而这里红，说明缺的不是算法而是接线。</para>
/// </summary>
public class PlanetWalkIntegrationTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;
    /// <summary>这条路总长约 300 km（赤道 0.27°），120 步每步约 2.5 km——比一个区域（3.7 km）窄，
    /// 所以不会整块跳过区域；步长本身由下面的 maxStepCm 断言钉住，不靠这里的注释。</summary>
    private const int TotalSteps = 120;

    /// <summary>量化姿态的基向量偏差绝对上界：Q15 每分量 ≤ 1/(2·32767)=1.53e-5，四个分量合成留 6× 余量。</summary>
    private const double BasisBound = 1e-4;

    /// <summary>赤道上跨面边界的一条路：44.85°E→45.12°E 在 45° 处从 +X 面换到 +Y 面。</summary>
    private static PlanetVector3 PathPoint(double t)
    {
        const double lon0 = 44.85, lon1 = 45.12;
        double lon = (lon0 + (lon1 - lon0) * t) * Math.PI / 180.0;
        return Geodetic.DirectionFromLatLon(0.0, lon);
    }

    private static double Cm(double v) => v / 100.0;   // 厘米 → 米

    [Fact]
    public void Walk_AcrossManyRegions_KeepsPositionWithinOneCentimetre()
    {
        var heights = new ConstantHeightSource(321_000);          // 3.21 km 高原
        var terrain = new PlanetTerrain(Radius, heights);
        var buffer = new byte[PlayerStatePacket.Size];
        RegionKey? previous = null;
        int steps = 0, crossings = 0, faceChanges = 0;
        double maxWireDriftCm = 0, maxPureRebuildCm = 0, maxQuaternionRebuildCm = 0;
        double maxQuaternionBudgetCm = 0, maxStepCm = 0, maxRadiusDriftCm = 0;
        PlanetVector3 previousDir = PlanetVector3.Zero;

        for (int s = 0; s < TotalSteps; s++)
        {
            PlanetVector3 dir = PathPoint((double)s / (TotalSteps - 1));
            if (s > 0) maxStepCm = Math.Max(maxStepCm, CubeSphere.GreatCircleCm(previousDir, dir, Radius));
            previousDir = dir;
            var ecef = EcefCentimeters.FromVector(dir * (Radius + heights.SampleElevationCm(dir)));

            // 权威口径：区域是方向的纯函数
            var key = RegionKey.FromDirection(dir, Level);
            var frame = RegionFrame.Build(key);
            PlanetVector3 local = frame.LocalFromEcef(ecef);

            var message = new PlayerStatePacket
            {
                Tick = s,
                RegionId = key.ToId(),
                LocalXCm = PlayerStatePacket.QuantizeLocalCm(local.X, out double ex),
                LocalYCm = PlayerStatePacket.QuantizeLocalCm(local.Y, out double ey),
                LocalZCm = PlayerStatePacket.QuantizeLocalCm(local.Z, out double ez),
                VelocityXCms = 300,
                Flags = 1,
            };
            Assert.True(ex <= 0.5 && ey <= 0.5 && ez <= 0.5, "量化误差不该超过半个厘米");

            Assert.True(PlayerStatePacket.TryWrite(buffer, message, out int written));
            Assert.Equal(PlayerStatePacket.Size, written);
            Assert.True(PlayerStatePacket.TryRead(buffer, out var decoded, out var status, out int consumed));
            Assert.Equal(PlanetWireStatus.Ok, status);
            Assert.Equal(PlayerStatePacket.Size, consumed);

            // 服务端拿 (regionId, 局部厘米) 重建全局：这就是"另一个进程算出来的同一个点"
            var rebuiltFrame = RegionFrame.Build(RegionKey.FromId(decoded.RegionId));
            var rebuilt = rebuiltFrame.GlobalFromLocalCm(new PlanetVector3(
                decoded.LocalXCm, decoded.LocalYCm, decoded.LocalZCm));
            double drift = new PlanetVector3(rebuilt.X - ecef.X, rebuilt.Y - ecef.Y, rebuilt.Z - ecef.Z).Length;
            maxWireDriftCm = Math.Max(maxWireDriftCm, drift);
            double radiusDrift = Math.Abs(rebuilt.Length - (Radius + heights.SampleElevationCm(dir)));
            maxRadiusDriftCm = Math.Max(maxRadiusDriftCm, radiusDrift);

            // 局部坐标必须在协议半跨之内，否则会被拒收
            Assert.True(Math.Abs(decoded.LocalXCm) <= PlayerStatePacket.MaxAbsLocalCm &&
                        Math.Abs(decoded.LocalZCm) <= PlayerStatePacket.MaxAbsLocalCm);

            if (previous.HasValue && previous.Value != key)
            {
                crossings++;
                if (previous.Value.Face != key.Face) faceChanges++;

                // —— 迁移握手：请求（带全局权威点）→ 服务端 → 确认（新帧）→ 客户端重建 ——
                var request = new RegionTransferRequest
                {
                    FromRegionId = previous.Value.ToId(),
                    ToRegionId = key.ToId(),
                    GlobalCm = ecef,
                    Tick = s,
                };
                var reqBuf = new byte[RegionTransferRequest.Size];
                Assert.True(RegionTransferRequest.TryWrite(reqBuf, request, out _));
                Assert.True(RegionTransferRequest.TryRead(reqBuf, out var decodedReq, out var reqStatus, out _));
                Assert.Equal(PlanetWireStatus.Ok, reqStatus);
                Assert.Equal(ecef, decodedReq.GlobalCm);                  // i64 全局点必须逐位不动
                Assert.Equal(previous.Value.ToId(), decodedReq.FromRegionId);

                var serverFrame = RegionFrame.Build(RegionKey.FromId(decodedReq.ToRegionId));
                PlanetVector3 serverLocal = serverFrame.LocalFromEcef(decodedReq.GlobalCm);
                var ack = new RegionTransferAck
                {
                    RegionId = decodedReq.ToRegionId,
                    LocalXCm = PlayerStatePacket.QuantizeLocalCm(serverLocal.X, out _),
                    LocalYCm = PlayerStatePacket.QuantizeLocalCm(serverLocal.Y, out _),
                    LocalZCm = PlayerStatePacket.QuantizeLocalCm(serverLocal.Z, out _),
                    OriginCm = serverFrame.Origin,
                    Frame = QuaternionQuantized.FromBasis(serverFrame.East, serverFrame.Up, serverFrame.South),
                };
                var ackBuf = new byte[RegionTransferAck.Size];
                Assert.True(RegionTransferAck.TryWrite(ackBuf, ack, out _));
                Assert.True(RegionTransferAck.TryRead(ackBuf, out var decodedAck, out var ackStatus, out _));
                Assert.Equal(PlanetWireStatus.Ok, ackStatus);

                // (a) 接收端按文档口径自己从 RegionId 重算帧：位置误差只该来自量化
                var clientFrame = RegionFrame.Build(RegionKey.FromId(decodedAck.RegionId));
                var pure = clientFrame.GlobalFromLocalCm(new PlanetVector3(
                    decodedAck.LocalXCm, decodedAck.LocalYCm, decodedAck.LocalZCm));
                maxPureRebuildCm = Math.Max(maxPureRebuildCm,
                    new PlanetVector3(pure.X - ecef.X, pure.Y - ecef.Y, pure.Z - ecef.Z).Length);

                // (b) 只用 ack 携带的原点 + 量化姿态重建：这是"缓存了区域元数据"的走法，误差必须更大
                var localVec = new PlanetVector3(decodedAck.LocalXCm, decodedAck.LocalYCm, decodedAck.LocalZCm);
                var viaQuaternion = decodedAck.OriginCm.ToVector() + decodedAck.Frame.Rotate(localVec);
                double quatDrift = new PlanetVector3(viaQuaternion.X - ecef.X, viaQuaternion.Y - ecef.Y,
                                                     viaQuaternion.Z - ecef.Z).Length;
                maxQuaternionRebuildCm = Math.Max(maxQuaternionRebuildCm, quatDrift);

                // 误差预算按力臂推：基向量的偏差有绝对上界（下面三条），线性映射对任意向量的误差
                // ≤ |v|·√3·上界，再加原点取整的 1 cm。力臂在这里是"离区域中心的距离"，
                // 高原上它含 3.2 km 的抬升，所以位置误差会比基偏差大一个量级——这才是"姿态只能当校验"的理由
                double budget = localVec.Length * BasisBound * Math.Sqrt(3) + 1.0;
                maxQuaternionBudgetCm = Math.Max(maxQuaternionBudgetCm, budget);

                // 姿态与纯函数帧必须描述同一个基（它是校验用的，不该跑到前面去）
                double basisProbe = VectorDistance(decodedAck.Frame.Rotate(PlanetVector3.AxisX), clientFrame.East);
                Assert.True(basisProbe < BasisBound, $"Rotate(X) 与 East 差 {basisProbe:E3}");
                Assert.True(VectorDistance(decodedAck.Frame.Rotate(PlanetVector3.AxisY), clientFrame.Up) < BasisBound);
                Assert.True(VectorDistance(decodedAck.Frame.Rotate(PlanetVector3.AxisZ), clientFrame.South) < BasisBound);
                Assert.Equal(serverFrame.Origin, decodedAck.OriginCm);
            }

            previous = key;
            steps++;
        }

        Assert.Equal(TotalSteps, steps);
        Assert.True(maxStepCm < PlanetConstants.TerrainPatchSizeCm * 6,
                    $"每步 {Cm(maxStepCm):F0} m 大到能整块跳过区域，crossings 计数不可信");
        Assert.True(crossings >= 40, $"这条路只换了 {crossings} 个区域，链子根本没被走到（判据空转）");
        Assert.True(faceChanges >= 1, $"没跨过立方体面边界（{faceChanges} 次），轴向翻转没被测到");
        Assert.True(maxWireDriftCm <= 1.0,
                    $"位置上报→重建的漂移 {Cm(maxWireDriftCm):F4} m 超了 1 cm 口径");
        Assert.True(maxPureRebuildCm <= 1.0, $"跨区迁移后位置漂移 {Cm(maxPureRebuildCm):F4} m 超了 1 cm 口径");
        // 离面距离是另一个量：它把"全局点取整"(≤√3/2 cm) 与"上报重建漂移"(≤1 cm) 叠在一起，
        // 上界要从这两项推出来，不能顺手也写成 1 cm
        double radialBudgetCm = Math.Sqrt(3) * 0.5 + 1.0;
        Assert.True(maxRadiusDriftCm <= radialBudgetCm,
                    $"重建点离开表面 {Cm(maxRadiusDriftCm):F4} m，超出推算上界 {Cm(radialBudgetCm):F4} m");

        // 量化姿态只能当校验：5 km 力臂 × 1.5e-5 rad ≈ 7.7 cm，比纯函数路径差一个量级
        Assert.True(maxQuaternionRebuildCm > maxPureRebuildCm,
                    $"姿态重建误差 {Cm(maxQuaternionRebuildCm):F4} m 没比纯函数帧 {Cm(maxPureRebuildCm):F4} m 差，" +
                    "说明量化没生效或判据打空");
        Assert.True(maxQuaternionRebuildCm <= maxQuaternionBudgetCm,
                    $"姿态重建误差 {Cm(maxQuaternionRebuildCm):F4} m 超出力臂推算的上界 " +
                    $"{Cm(maxQuaternionBudgetCm):F4} m（基偏差 × √3 × 力臂 + 原点取整）");
        Assert.True(maxQuaternionBudgetCm < 200.0,
                    $"上界本身涨到 {Cm(maxQuaternionBudgetCm):F2} m，力臂/基偏差的口径该重查");

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "walk steps={0} regions={1} crossings={2} face_changes={3} step_max={4:F0} m " +
            "wire_drift_max={5:F3} cm transfer_pure_max={6:F3} cm transfer_quat_max={7:F1} cm " +
            "quat_budget_max={8:F1} cm radius_drift_max={9:F3} cm",
            steps, crossings + 1, crossings, faceChanges, Cm(maxStepCm), maxWireDriftCm, maxPureRebuildCm,
            maxQuaternionRebuildCm, maxQuaternionBudgetCm, maxRadiusDriftCm));
    }

    private static double VectorDistance(in PlanetVector3 a, in PlanetVector3 b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    [Fact]
    public void PatchCacheSurvivesAProcessRestart_IntoTheNextRegions()
    {
        // 跨区时两边各自的 patch 落盘；"重启"（新实例）必须还能逐位读回，且不同区域不串
        using var dir = new TempCacheDir();
        var terrain = new PlanetTerrain(Radius, new AxisedHeightSource(PlanetVector3.AxisY, 4e7));
        long seed = unchecked((long)0x5A5A5A5A5A5A5A5A);
        uint version = 0x00B0_00B1;
        var cache = new PlanetPatchCache(dir.Root, seed, version);
        var buffer = new float[PlanetPatchSampler.HeightmapLength];

        var walk = new List<(RegionKey Key, PatchKey Patch)>();
        var seen = new HashSet<(long, PatchKey)>();
        for (int s = 0; s < 40; s++)
        {
            PlanetVector3 dirV = PathPoint((double)s / 39);
            var key = RegionKey.FromDirection(dirV, Level);
            var streamer = new PlanetTerrainStreamer(RegionFrame.Build(key), terrain);
            PlanetVector3 local = RegionFrame.Build(key)
                .LocalFromGlobalCm(dirV * (Radius + terrain.ElevationCm(dirV)));
            // 用调度器自己的换算，别在这儿再抄一遍 floor 公式
            var patch = streamer.PatchOf(local.X, local.Z);
            if (seen.Add((key.ToId(), patch))) walk.Add((key, patch));
        }
        Assert.True(walk.Count >= 30, $"40 步只落进 {walk.Count} 个不同 patch，这条路没走开");

        int written = 0;
        foreach (var (key, patch) in walk)
        {
            var streamer = new PlanetTerrainStreamer(RegionFrame.Build(key), terrain, cache);
            Assert.Equal(PatchFillSource.Generated, streamer.FillPatch(patch, buffer));
            written++;
        }

        var reopened = new PlanetPatchCache(dir.Root, seed, version);
        Assert.Equal(written, reopened.EntryCount);
        Assert.Equal(written, Directory.EnumerateFiles(reopened.RootDirectory, "*.hgt",
                                                      SearchOption.AllDirectories).Count());

        // 重启后逐位读回，并且换一个区域读同一个格坐标必须是未命中（不是读到别人的高度）
        var fresh = new float[PlanetPatchSampler.HeightmapLength];
        foreach (var (key, patch) in walk)
        {
            var streamer = new PlanetTerrainStreamer(RegionFrame.Build(key), terrain, reopened);
            Assert.Equal(PatchFillSource.CacheHit, streamer.FillPatch(patch, fresh));
            var reference = new float[PlanetPatchSampler.HeightmapLength];
            PlanetPatchSampler.FillPatchHeights(RegionFrame.Build(key), terrain, patch.OriginXCm, patch.OriginZCm,
                                                reference, null, parallel: false);
            Assert.True(MemoryMarshal.AsBytes(reference.AsSpan())
                        .SequenceEqual(MemoryMarshal.AsBytes(fresh.AsSpan())),
                        $"{key} 的 patch {patch} 重启后读回与重算不一致");
        }

        // 跨区域隔离：找一对"格坐标不同"的记录，拿 A 的区域去读 B 的格坐标，必须什么都没读到
        // （不能随手拿相邻两条：每条路径的 patch 都落在各自帧中心附近，格坐标很容易真的相同，
        //   那时读到东西是正确的，判据就会红在夹具上）
        bool probed = false;
        var probe = new float[PlanetPatchSampler.HeightmapLength];
        for (int i = 0; i < walk.Count && !probed; i++)
        {
            for (int j = 0; j < walk.Count; j++)
            {
                if (walk[i].Key == walk[j].Key || walk[i].Patch == walk[j].Patch) continue;
                Assert.False(reopened.TryRead(walk[i].Key.ToId(), walk[j].Patch.X, walk[j].Patch.Z,
                                              probe, out var reason),
                             $"{walk[i].Key} 竟然读到了 {walk[j].Patch}：跨区串了");
                Assert.Equal(PatchCacheReject.MissingFile, reason);
                probed = true;
                break;
            }
        }
        Assert.True(probed, "没找到格坐标不同的跨区域对，这一半判据没跑到");

        Assert.Equal(0, reopened.CorruptRejects);
        // 计数是"每个实例自己的账"：写发生在第一个实例上，重启后的实例只该有命中
        Assert.Equal(written, cache.Writes);
        Assert.Equal(written, reopened.Hits);
        Assert.Equal(0, reopened.Writes);
        Assert.Equal(1, reopened.Misses);            // 那一次就是上面的跨区域探针
        string stats = reopened.DescribeStats();
        Assert.Contains($"hits={written}", stats);
        Assert.Contains("writes=0", stats);
        Assert.Contains("misses=1", stats);
        Assert.Contains("reject_badcrc=0", stats);
        Assert.DoesNotContain("reject_missingfile=", stats);   // 未命中单列在 misses=，不重复进"坏"账
    }
}
