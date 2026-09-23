using Horizon.Game.Core.World;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 4.3 — <see cref="ShardRegionResolver"/> 空间分片解析的纯函数测试。<br/>
/// 模型（2D X/Z 网格划分）：Shard (gx, gz) 覆盖 chunkX ∈ [gx*C, (gx+1)*C)、chunkZ ∈ [gz*C, (gz+1)*C)，
/// id(gx, gz) = gz*ShardsPerRowX + gx（末行不满时 id ≥ ShardCount 的单元跳过）；
/// 返回集合 = 位置视口覆盖的网格单元 Shard ∪ {characterId 主 Shard}。
/// </summary>
public class ShardRegionResolverTests
{
    private const int ChunksPerShardX = 100; // 每 Shard 100 chunk = 1600m

    /// <summary>
    /// 创建解析器。默认 <c>shardsPerRowX = shardCount</c>（单行网格 = 1D 带状划分，仅沿 X 轴分片），
    /// 保证既有 1D 语义用例全部成立；2D 用例显式传 <paramref name="shardsPerRowX"/>。
    /// </summary>
    private static ShardRegionResolver CreateResolver(
        int chunksPerShardX = ChunksPerShardX,
        int shardCount = 3,
        int shardsPerRowX = 0,
        int chunksPerShardZ = 0)
    {
        if (shardsPerRowX == 0) shardsPerRowX = shardCount;
        if (chunksPerShardZ == 0) chunksPerShardZ = chunksPerShardX;
        return new ShardRegionResolver(new ZoneBasedShardRouter(shardCount), chunksPerShardX, chunksPerShardZ, shardsPerRowX);
    }

    [Fact]
    public void Disabled_ReturnsOnlyPrimary()
    {
        var resolver = new ShardRegionResolver(new ZoneBasedShardRouter(3)); // 全 0 → 禁用
        // characterId=1 → 主 shard 1；位置在 shard 0 区域也不扩展
        var shards = resolver.ResolveShards(characterId: 1, x: 800f, y: 0f, z: 0f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 1 }, shards);
    }

    [Fact]
    public void SingleShard_ReturnsOnlyZero()
    {
        var resolver = CreateResolver(shardCount: 1);
        var shards = resolver.ResolveShards(characterId: 42, x: 800f, y: 0f, z: 0f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0 }, shards);
    }

    [Fact]
    public void InteriorPosition_ReturnsOnlyPrimary()
    {
        var resolver = CreateResolver();
        // x=800m → chunkX=50（shard 0 范围 [0,100)），视口 [22,78] 全在 shard 0；主=0
        var shards = resolver.ResolveShards(characterId: 0, x: 800f, y: 0f, z: 0f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0 }, shards);
    }

    [Fact]
    public void NearBoundary_ReturnsPrimaryAndAdjacent()
    {
        var resolver = CreateResolver();
        // x=1440m → chunkX=90，视口 [62,118] 跨 shard 0/1 边界（shard 0 覆盖到 chunkX=99）
        var shards = resolver.ResolveShards(characterId: 0, x: 1440f, y: 0f, z: 0f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0, 1 }, shards);
    }

    [Fact]
    public void PrimaryDiffersFromPosition_ReturnsBoth()
    {
        var resolver = CreateResolver();
        // characterId=2（主 shard 2），位置在 shard 0 区域（x=800m → chunkX=50）
        // → 主 shard 恒在集合内（角色永远能看到自己主 shard 的实体）
        var shards = resolver.ResolveShards(characterId: 2, x: 800f, y: 0f, z: 0f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0, 2 }, shards);
    }

    [Fact]
    public void WideViewport_SpansThreeShards()
    {
        var resolver = CreateResolver();
        // chunkX=150（shard 1），视口半径 120 → [30,270] → shard [0,2]；主=1
        var shards = resolver.ResolveShards(characterId: 1, x: 2400f, y: 0f, z: 0f, viewRadiusChunks: 120);
        Assert.Equal(new long[] { 0, 1, 2 }, shards);
    }

    [Fact]
    public void NegativeChunkCoordinate_ClampsToShardZero()
    {
        var resolver = CreateResolver();
        // x=-800m → chunkX=-50（向下取整除法 → -1 → clamp 0）；视口 [-78,-22] 全在 shard 0
        var shards = resolver.ResolveShards(characterId: 0, x: -800f, y: 0f, z: 0f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0 }, shards);
    }

    [Fact]
    public void ZeroViewRadius_ReturnsOnlyCurrentChunkShard()
    {
        var resolver = CreateResolver();
        // x=800m → chunkX=50，半径 0 → [50,50] → shard 0；主=0
        var shards = resolver.ResolveShards(characterId: 0, x: 800f, y: 0f, z: 0f, viewRadiusChunks: 0);
        Assert.Equal(new long[] { 0 }, shards);
    }

    [Fact]
    public void BoundaryChunkEdge_ExactBoundaryBelongsToLeftShard()
    {
        var resolver = CreateResolver();
        // x=1599.9m → chunkX=99（shard 0 右缘）；视口 [71,127] 跨入 shard 1
        var shards = resolver.ResolveShards(characterId: 0, x: 1599.9f, y: 0f, z: 0f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0, 1 }, shards);
    }

    // ── 2D X/Z 网格划分 ───────────────────────────────────────────────

    [Fact]
    public void CornerBoundary_CoversAllFourShards()
    {
        // 2×2 网格（ShardCount=4，ShardsPerRowX=2）：id(0,0)=0, id(1,0)=1, id(0,1)=2, id(1,1)=3
        var resolver = CreateResolver(shardCount: 4, shardsPerRowX: 2);
        // x=z=1600m → chunk(100,100) 恰为四 Shard 角点；视口 [72,128]² 同时覆盖四个单元
        var shards = resolver.ResolveShards(characterId: 0, x: 1600f, y: 0f, z: 1600f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0, 1, 2, 3 }, shards);
    }

    [Fact]
    public void CornerBoundary_PrimaryInAnotherCell_StillCoversAllFour()
    {
        var resolver = CreateResolver(shardCount: 4, shardsPerRowX: 2);
        // characterId=5 → 主 shard 1（与角点位置无关，主 shard 恒在集合内）
        var shards = resolver.ResolveShards(characterId: 5, x: 1600f, y: 0f, z: 1600f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0, 1, 2, 3 }, shards);
    }

    [Fact]
    public void PartialLastRow_SkipsEmptyCell()
    {
        // ShardCount=3、ShardsPerRowX=2 → 2 行网格，末行单元 (1,1)=id 3 ≥ ShardCount → 无 Shard，跳过
        var resolver = CreateResolver(shardCount: 3, shardsPerRowX: 2);
        var shards = resolver.ResolveShards(characterId: 0, x: 1600f, y: 0f, z: 1600f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0, 1, 2 }, shards);
    }

    [Fact]
    public void ZAxisBoundary_SubscribesVerticalNeighbors()
    {
        // 2×2 网格：仅 Z 轴跨边界 → 覆盖 (0,0)=0 与 (0,1)=2
        var resolver = CreateResolver(shardCount: 4, shardsPerRowX: 2);
        // x=800m → chunkX=50（第 0 列）；z=1440m → chunkZ=90，视口 [62,118] 跨行边界
        var shards = resolver.ResolveShards(characterId: 0, x: 800f, y: 0f, z: 1440f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0, 2 }, shards);
    }

    [Fact]
    public void NegativeChunkZ_ClampsToFirstRow()
    {
        var resolver = CreateResolver(shardCount: 4, shardsPerRowX: 2);
        // z=-800m → chunkZ=-50（向下取整 → -1 → clamp 0）；x=0 → 第 0 列 → 仅 shard 0
        var shards = resolver.ResolveShards(characterId: 0, x: 0f, y: 0f, z: -800f, viewRadiusChunks: 28);
        Assert.Equal(new long[] { 0 }, shards);
    }
}
