using System;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Planet.Tests;

/// <summary>
/// 度量报告：cube-sphere 的瓦片度量在面中心和面边缘不同，方案文档里的区域尺寸必须取自这里，不能靠换算估。
/// </summary>
public class RegionMetricReportTests
{
    private readonly ITestOutputHelper _out;

    public RegionMetricReportTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void Report_RegionSizes(int level)
    {
        int span = 1 << level;
        var centre = new RegionKey(4, level, span / 2, span / 2);   // 面中心（北极附近）
        var edge = new RegionKey(4, level, 0, span / 2);             // 靠面边缘
        var corner = new RegionKey(4, level, 0, 0);                  // 面角

        foreach (var (name, key) in new[] { ("面中心", centre), ("面边缘", edge), ("面角", corner) })
        {
            double width = RegionMetrics.CenterWidthCm(key);
            double height = RegionMetrics.CenterHeightCm(key);
            double toCorner = RegionMetrics.CenterToCornerCm(key);
            _out.WriteLine($"L{level} {name}: 宽 {width / 100000.0:F3} km, 高 {height / 100000.0:F3} km, " +
                           $"中心到角 {toCorner / 100000.0:F3} km, 该处曲率下垂 {RegionMetrics.CurvatureSagCm(toCorner) / 100.0:F3} m");
        }

        _out.WriteLine($"L{level} 近景 5×5 patch 窗口 = " +
                       $"{(double)PlanetConstants.NearPatchWindowEdge * PlanetConstants.TerrainPatchSizeCm / 100000.0:F3} km");
    }

    [Fact]
    public void RegionWidth_ShrinksTowardsFaceEdge_AndGrowsWithLevel()
    {
        const int level = PlanetConstants.DefaultRegionLevel;
        int span = 1 << level;

        double atCentre = RegionMetrics.CenterWidthCm(new RegionKey(4, level, span / 2, span / 2));
        double atEdge = RegionMetrics.CenterWidthCm(new RegionKey(4, level, 0, span / 2));
        Assert.True(atCentre > atEdge, $"面中心瓦片应比面边缘瓦片宽：中心 {atCentre}，边缘 {atEdge}");

        // 层级 +1，同位置的瓦片必须更小
        double oneLevelUp = RegionMetrics.CenterWidthCm(new RegionKey(4, level - 1, span / 4, span / 4));
        Assert.True(oneLevelUp > atCentre * 1.5, $"层级减半后跨度没翻倍：{oneLevelUp} vs {atCentre}");
    }
}
