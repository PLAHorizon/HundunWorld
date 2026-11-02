namespace Horizon.Planet;

/// <summary>星球常量。改动 RadiusCm / RegionLevel 等口径等于破坏存档与协议兼容，需连带升版本号。</summary>
public static class PlanetConstants
{
    /// <summary>地球平均半径（厘米），仅作换算参照。</summary>
    public const long EarthMeanRadiusCm = 637_100_000L;

    /// <summary>本星球半径：10 倍地球平均半径 = 63,710 km。</summary>
    public const long PlanetRadiusCm = 6_371_000_000L;

    /// <summary>区域层级：cube-sphere 每面在该层级上切 2^level × 2^level 个区域。</summary>
    public const int DefaultRegionLevel = 14;

    /// <summary>RegionId 协议版本；坐标口径变更时递增，握手不一致直接拒入世界。</summary>
    public const int PlanetProtocolVersion = 1;

    /// <summary>Flax Terrain 一个 chunk 边上的 quad 数（与 Terrain.Setup 的 chunkSize 一致）。</summary>
    public const int TerrainChunkQuads = 127;

    /// <summary>Flax Terrain 一个 patch 每边的 chunk 数。</summary>
    public const int TerrainPatchEdgeChunks = 4;

    /// <summary>Flax Terrain 每个几何顶点占的世界单位（cm）。</summary>
    public const int TerrainUnitsPerVertex = 100;

    /// <summary>一个 Flax Terrain patch 的边长（厘米）= 127 × 4 × 100 = 50,800 cm = 508 m。</summary>
    public const int TerrainPatchSizeCm =
        TerrainChunkQuads * TerrainPatchEdgeChunks * TerrainUnitsPerVertex;

    /// <summary>近景流送窗口：以玩家为中心的 patch 数（每边）。</summary>
    public const int NearPatchWindowEdge = 5;

    /// <summary>
    /// 窗口重中心的滞后余量（厘米）：玩家越过 patch 边界不足该值时窗口不动，
    /// 免得站在边界上抖几厘米就整行换 5 块 patch。取 patch 边长的 1/4（127 m）。
    /// </summary>
    public const int PatchWindowHysteresisCm = TerrainPatchSizeCm / 4;

    /// <summary>单次计划最多生成几块 patch（生成成本 ~142 ms/块，一帧只能摊几块）。</summary>
    public const int PatchLoadsPerPlan = 4;

    /// <summary>局部原点重定触发半径（厘米）：玩家偏离当前原点超过该值即换帧。</summary>
    public const int RebaseTriggerCm = 200_000;
}
