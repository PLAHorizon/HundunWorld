using System;
using Xunit;

namespace Horizon.Planet.Tests;

public class CubeSphereTests
{
    internal static PlanetVector3 RandomUnitDirection(Random rng)
    {
        while (true)
        {
            var v = new PlanetVector3(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
            double len = v.Length;
            if (len < 1e-3) continue;
            // 避开主轴平分线：那里 FaceOf 的面归属本身不唯一
            double ax = Math.Abs(v.X / len), ay = Math.Abs(v.Y / len), az = Math.Abs(v.Z / len);
            if (Math.Abs(ax - ay) < 1e-3 || Math.Abs(ay - az) < 1e-3 || Math.Abs(ax - az) < 1e-3) continue;
            return v / len;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void FaceAxes_AreRightHandedAndOrthonormal(int face)
    {
        var n = CubeSphere.FaceNormal(face);
        var u = CubeSphere.FaceUAxis(face);
        var v = CubeSphere.FaceVAxis(face);

        Assert.Equal(1.0, n.LengthSquared, 12);
        Assert.Equal(1.0, u.LengthSquared, 12);
        Assert.Equal(1.0, v.LengthSquared, 12);
        Assert.Equal(0.0, PlanetVector3.Dot(n, u), 12);
        Assert.Equal(0.0, PlanetVector3.Dot(n, v), 12);
        Assert.Equal(0.0, PlanetVector3.Dot(u, v), 12);
        var uv = PlanetVector3.Cross(u, v);
        Assert.Equal(n.X, uv.X, 12);
        Assert.Equal(n.Y, uv.Y, 12);
        Assert.Equal(n.Z, uv.Z, 12);
    }

    [Fact]
    public void ToDirection_CenterIsFaceNormal_AndEdgeIsFortyFiveDegrees()
    {
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            var n = CubeSphere.FaceNormal(face);
            var c = CubeSphere.ToDirection(face, 0, 0);
            Assert.Equal(n.X, c.X, 12);
            Assert.Equal(n.Y, c.Y, 12);
            Assert.Equal(n.Z, c.Z, 12);

            double angle = PlanetVector3.AngleBetween(
                CubeSphere.FaceNormal(face), CubeSphere.ToDirection(face, 1, 0));
            Assert.Equal(Math.PI / 4, angle, 12);
        }
    }

    [Fact]
    public void ToFaceUv_Then_ToDirection_RoundTripsEveryDirection()
    {
        var rng = new Random(20261102);
        for (int i = 0; i < 20000; i++)
        {
            PlanetVector3 dir = RandomUnitDirection(rng);
            CubeSphere.ToFaceUv(dir, out int face, out double u, out double v);
            PlanetVector3 back = CubeSphere.ToDirection(face, u, v);
            Assert.True(PlanetVector3.Dot(dir, back) > 1.0 - 1e-12,
                $"第 {i} 个方向 {dir} 往返后偏了（face={face} uv={u},{v}）");
            Assert.InRange(u, -1.0, 1.0);
            Assert.InRange(v, -1.0, 1.0);
        }
    }

    [Fact]
    public void FaceInteriorUv_BelongsToThatFace()
    {
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = -20; i <= 20; i++)
            {
                for (int j = -20; j <= 20; j++)
                {
                    double u = i / 21.0, v = j / 21.0;
                    Assert.Equal(face, CubeSphere.FaceOf(CubeSphere.ToDirection(face, u, v)));
                }
            }
        }
    }

    [Fact]
    public void TryToUv_RejectsDirectionOnOtherFace()
    {
        // 面 4 的中心方向（+Z）不可能落在面 0（+X）的 uv 范围内
        Assert.False(CubeSphere.TryToUv(PlanetVector3.AxisZ, 0, out _, out _));
    }
}
