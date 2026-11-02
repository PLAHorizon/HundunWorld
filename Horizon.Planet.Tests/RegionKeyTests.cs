using System;
using Xunit;

namespace Horizon.Planet.Tests;

public class RegionKeyTests
{
    [Fact]
    public void Id_RoundTrips_AndStaysNonNegative()
    {
        var rng = new Random(7);
        for (int level = 0; level <= RegionKey.MaxLevel; level++)
        {
            int span = 1 << level;
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int i = 0; i < 200; i++)
                {
                    var key = new RegionKey(face, level, rng.Next(span), rng.Next(span));
                    long id = key.ToId();
                    Assert.True(id >= 0, $"RegionId 出现负值：{key} -> {id}");
                    Assert.Equal(key, RegionKey.FromId(id));
                }
            }
        }
    }

    [Fact]
    public void Ids_OfDifferentKeys_DoNotCollide()
    {
        const int level = 14;
        const int span = 1 << level;
        long? previous = null;
        var seen = new System.Collections.Generic.HashSet<long>();
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int x = 0; x < span; x += 997)
            {
                for (int y = 0; y < span; y += 997)
                {
                    long id = new RegionKey(face, level, x, y).ToId();
                    Assert.True(seen.Add(id), $"RegionId 撞车：{id}（上一个 {previous}）");
                    previous = id;
                }
            }
        }
    }

    [Fact]
    public void UvBounds_TilesPartitionTheFaceExactly()
    {
        const int level = 13;
        int span = 1 << level;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            var first = new RegionKey(face, level, 0, 0).UvBounds();
            var last = new RegionKey(face, level, span - 1, span - 1).UvBounds();
            Assert.Equal(-1.0, first.U0, 12);
            Assert.Equal(-1.0, first.V0, 12);
            Assert.Equal(1.0, last.U1, 12);
            Assert.Equal(1.0, last.V1, 12);

            for (int x = 0; x < span - 1; x++)
            {
                var a = new RegionKey(face, level, x, 0).UvBounds();
                var b = new RegionKey(face, level, x + 1, 0).UvBounds();
                Assert.Equal(a.U1, b.U0, 12);
            }
        }
    }

    [Fact]
    public void FromDirection_LandsInTheTileThatContainsIt()
    {
        var rng = new Random(2026);
        for (int level = 8; level <= 14; level++)
        {
            for (int i = 0; i < 4000; i++)
            {
                PlanetVector3 dir = CubeSphereTests.RandomUnitDirection(rng);
                RegionKey key = RegionKey.FromDirection(dir, level);
                Assert.True(CubeSphere.TryToUv(dir, key.Face, out double u, out double v));
                var (u0, u1, v0, v1) = key.UvBounds();
                Assert.InRange(u, u0 - 1e-9, u1 + 1e-9);
                Assert.InRange(v, v0 - 1e-9, v1 + 1e-9);
            }
        }
    }

    [Fact]
    public void Constructor_RejectsOutOfRangeComponents()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionKey(6, 10, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionKey(-1, 10, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionKey(0, 16, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionKey(0, 3, 8, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionKey(0, 3, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegionKey.FromId(-1));
    }

    [Fact]
    public void TryNeighbour_OnlyInsideTheSameFace()
    {
        Assert.False(new RegionKey(2, 5, 0, 0).TryNeighbour(-1, 0, out _));
        Assert.False(new RegionKey(2, 5, 31, 0).TryNeighbour(1, 0, out _));
        Assert.True(new RegionKey(2, 5, 0, 0).TryNeighbour(1, 1, out RegionKey n));
        Assert.Equal(new RegionKey(2, 5, 1, 1), n);
    }
}
