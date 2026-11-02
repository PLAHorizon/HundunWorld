using System.Linq;
using Horizon.Game.Sim.Npcs;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 编号分配（"为 NPC 编号"的入口）。编辑器「新建」先要号再摆草稿，所以分配规则一旦漂了，
/// 表现是"新建两次拿到同一个号"或"号落在装载器会拒的洞外"——前者保存时静默覆盖，后者要到装配才红。
/// </summary>
public class NpcIdAllocatorTests
{
    [Fact]
    public void 空号段从下界开始()
    {
        Assert.Equal(NpcIdSegmentCore.ServiceMin, NpcIdSegmentCore.NextFree(new int[0], NpcIdSegment.Service));
        Assert.Equal(NpcIdSegmentCore.HostileMin, NpcIdSegmentCore.NextFree(new int[0], NpcIdSegment.Hostile));
    }

    [Fact]
    public void 取最大值加一而不是回填空号()
    {
        // 400001 是空号也**不发**：编号写进任务/掉落/日志，回收后会指向另一只 NPC。
        var used = new[] { NpcIdSegmentCore.ServiceMin, NpcIdSegmentCore.ServiceMin + 5 };
        Assert.Equal(NpcIdSegmentCore.ServiceMin + 6, NpcIdSegmentCore.NextFree(used, NpcIdSegment.Service));
    }

    [Fact]
    public void 跨号段的号只在本段计数()
    {
        // 1/2/3 是旧契约号、999999 不在任何号段：都不该把服务号段推走；
        // 500001 是本段（敌对）的号，所以敌对分配必须从它之后发。
        var used = new[] { 1, 2, 3, 500001, 999999 };
        Assert.Equal(NpcIdSegmentCore.ServiceMin, NpcIdSegmentCore.NextFree(used, NpcIdSegment.Service));
        Assert.Equal(500002, NpcIdSegmentCore.NextFree(used, NpcIdSegment.Hostile));
    }

    [Fact]
    public void 号段用尽回零且零不是合法编号()
    {
        var used = new[] { NpcIdSegmentCore.HostileMax };
        Assert.Equal(0, NpcIdSegmentCore.NextFree(used, NpcIdSegment.Hostile));
        Assert.False(NpcIdSegmentCore.IsAllowed(0));
    }

    [Fact]
    public void 旧号段用尽也不外溢()
    {
        // legacy 1–3 不再分配：就算库里有 1/2/3，也不该把 4 发出去（4 不在任何号段内）。
        var used = new[] { 1, 2, 3 };
        Assert.Equal(0, NpcIdSegmentCore.NextFree(used, NpcIdSegment.Legacy));
    }

    [Fact]
    public void 空列表也可安全分配()
    {
        Assert.Equal(NpcIdSegmentCore.ServiceMin, NpcIdSegmentCore.NextFree(null, NpcIdSegment.Service));
        Assert.Equal(0, NpcIdSegmentCore.NextFree(new[] { 400000 }, NpcIdSegment.OutOfRange));
    }

    [Fact]
    public void 分配出来的号必须落在号段内()
    {
        // 用一批"刚好卡边界"的输入扫一遍：分配结果永远通过装载器同一个号段闸门。
        var used = new[] { 400000, 400001, 400002, 400003 };
        for (var i = 0; i < 5; i++)
        {
            var next = NpcIdSegmentCore.NextFree(used, NpcIdSegment.Service);
            Assert.True(next > 0);
            Assert.Equal(NpcIdSegment.Service, NpcIdSegmentCore.SegmentOf(next));
            used = used.Append(next).ToArray();
        }

        Assert.Equal(400009, NpcIdSegmentCore.NextFree(used, NpcIdSegment.Service));
    }
}
