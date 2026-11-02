using System;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Moq;
using Orleans;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 玩法事件写入端（`Orleans*Writer`）的**键与回执语义**。
/// <para>
/// 这几个适配器是"DS 的计数 → 玩家身上的真实变化"之间唯一的一跳。链路上它们最容易出的是**静默**的错：
/// 键算错 ⇒ 加进一个没人读的 Grain（返回值照样成功）；回执不当 ⇒ "加了但没进去"被当成成功。
/// 真集群那一跳分别由 TestCluster（钱包/任务/背包）与受 `HORIZON_ORLEANS_REDIS` 门控的 Live 用例覆盖；
/// 这里用替身把**键映射与失败语义**钉死——尤其经验这一条，`CharacterGrain` 依赖 4 个 EF 上下文，
/// TestCluster 里起不来，所以这层替身是它唯一可离线执行的判据。
/// </para>
/// </summary>
public class OrleansWriterAdaptersTests
{
    [Fact]
    public async Task 经验写入端按角色号取Grain且失败不向外抛()
    {
        var grain = new Mock<ICharacterGrain>();
        var cluster = new Mock<IClusterClient>();
        long requestedKey = -1;
        cluster
            .Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string?>()))
            .Callback<long, string?>((k, _) => requestedKey = k)
            .Returns(grain.Object);
        grain.Setup(g => g.AddExpAsync(123L)).ReturnsAsync(new AddExpResult { Success = true });

        var writer = new OrleansExperienceWriter(cluster.Object);

        Assert.True(await writer.AddExpAsync(5UL, 123L, "kill"));
        // 键必须是 (long)characterId：换成别的口径 ⇒ "经验加到了一个没人读的 grain 里"，日志全绿而等级不动。
        Assert.Equal(5L, requestedKey);
        grain.Verify(g => g.AddExpAsync(123L), Times.Once);

        // 非法入参不碰集群（角色 0 / 数量非正）
        Assert.False(await writer.AddExpAsync(0UL, 1L, "kill"));
        Assert.False(await writer.AddExpAsync(5UL, 0L, "kill"));
        cluster.Verify(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string?>()), Times.Once);

        // 回执为空 / Grain 抛异常 ⇒ 都算失败，且**不**把异常抛给调用方（DS 的 tick 不能被它打断）
        grain.Setup(g => g.AddExpAsync(9L)).ReturnsAsync((AddExpResult?)null);
        Assert.False(await writer.AddExpAsync(5UL, 9L, "kill"));
        grain.Setup(g => g.AddExpAsync(10L)).ThrowsAsync(new InvalidOperationException("boom"));
        Assert.False(await writer.AddExpAsync(5UL, 10L, "kill"));
    }
}
