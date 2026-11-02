using System;
using Xunit;

namespace Horizon.Planet.Tests;

public class PlanetTerrainTests
{
    [Fact]
    public void SurfacePoint_And_Unproject_AreInverses()
    {
        const double everestCm = 884_888;
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, new ConstantHeightSource(everestCm));
        var rng = new Random(4242);

        for (int i = 0; i < 20000; i++)
        {
            PlanetVector3 dir = CubeSphereTests.RandomUnitDirection(rng);
            PlanetVector3 surface = terrain.SurfacePointCm(dir);
            Assert.Equal(PlanetConstants.PlanetRadiusCm + everestCm, surface.Length, 3);

            terrain.UnprojectSurfacePoint(surface, out PlanetVector3 back, out double elevationCm);
            Assert.True(PlanetVector3.Dot(dir, back) > 1.0 - 1e-15, $"方向还原失败：{dir} vs {back}");
            Assert.Equal(everestCm, elevationCm, 3);
        }
    }

    [Fact]
    public void LatLonPath_MatchesDirectionPath()
    {
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, new ConstantHeightSource(1_234));
        var rng = new Random(77);

        for (int i = 0; i < 20000; i++)
        {
            double lat = (rng.NextDouble() * 2 - 1) * (Math.PI / 2);
            double lon = (rng.NextDouble() * 2 - 1) * Math.PI;
            PlanetVector3 byLatLon = terrain.SurfacePointCmByLatLon(lat, lon);
            PlanetVector3 byDir = terrain.SurfacePointCm(Geodetic.DirectionFromLatLon(lat, lon));
            Assert.True((byLatLon - byDir).Length < 1e-6, $"两条路径差了 {(byLatLon - byDir).Length} cm");
        }
    }

    [Fact]
    public void Geodetic_RoundTrips_LatLon()
    {
        var rng = new Random(5150);
        for (int i = 0; i < 50000; i++)
        {
            double lat = (rng.NextDouble() * 2 - 1) * (Math.PI / 2);
            double lon = (rng.NextDouble() * 2 - 1) * Math.PI;
            Geodetic.LatLonFromDirection(Geodetic.DirectionFromLatLon(lat, lon), out double backLat, out double backLon);
            Assert.Equal(lat, backLat, 9);
            Assert.Equal(lon, backLon, 9);
        }
    }

    [Fact]
    public void Geodetic_Poles_AndDateLine_AreWellDefined()
    {
        Geodetic.LatLonFromDirection(PlanetVector3.AxisZ, out double lat, out double lon);
        Assert.Equal(Math.PI / 2, lat, 12);

        Geodetic.LatLonFromDirection(-PlanetVector3.AxisZ, out lat, out _);
        Assert.Equal(-Math.PI / 2, lat, 12);

        // 180° 经线两侧必须连续（atan2 的 ±π 都算同一条线）
        Geodetic.LatLonFromDirection(Geodetic.DirectionFromLatLon(0, Math.PI - 1e-9), out _, out double lonWest);
        Geodetic.LatLonFromDirection(Geodetic.DirectionFromLatLon(0, -Math.PI + 1e-9), out _, out double lonEast);
        Assert.True(Math.Abs(Math.Abs(lonWest) - Math.PI) < 1e-6);
        Assert.True(Math.Abs(Math.Abs(lonEast) - Math.PI) < 1e-6);
    }

    [Fact]
    public void Constructor_RejectsBadRadius()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetTerrain(0, new ConstantHeightSource(0)));
        Assert.Throws<ArgumentNullException>(() => new PlanetTerrain(1, null!));
    }
}
