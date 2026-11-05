using System;
using System.Linq;
using System.Reflection;
using Horizon.Game.Message.Network;
using Horizon.Planet;
using HundunWorld.Game.Planet;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 在飞迁移请求的账（<see cref="PlanetRegionTransferTracker"/>）的判据。
/// <para>这一层存在的理由是一条实机现象：站在区域边界上抖几厘米，换区检查就会每帧发一次请求，
/// 而服务端每次受理都要落盘。压住它靠两条规矩——单条在飞、超时按"没回执"处理。
/// 两条都必须有正向孪生，否则"永远拒"这种实现也是绿的。</para>
/// </summary>
public class PlanetRegionTransferTrackerTests
{
    private const int Level = PlanetConstants.DefaultRegionLevel;
    private static readonly RegionKey Home = new(2, Level, 8192, 8192);
    private static readonly RegionKey East = Neighbour(Home, RegionEdge.UPlus);

    private static RegionKey Neighbour(RegionKey key, RegionEdge edge) =>
        PlanetRegions.TryNeighbour(key, edge, out var n) ? n : throw new InvalidOperationException("闭合面上的区域没有邻居");

    private const long NoneCode = (long)PlanetRegionRejectCode.None;

    [Fact]
    public void 只允许一条在飞_回执之后才能再发()
    {
        var t = new PlanetRegionTransferTracker(timeoutUnits: 120);
        Assert.True(t.TryBegin(requestTick: 100, now: 0, Home, out _));
        Assert.False(t.TryBegin(101, 1, Home, out string why));
        Assert.Contains("上一条还在飞", why);
        Assert.Equal(1, t.Sent);
        Assert.Equal(1, t.RefusedInFlight);

        // 正向孪生：回执落地之后同一条链能再发
        t.OnReceipt(true, accepted: true, applied: true, (int)NoneCode, East.ToId(), now: 5);
        Assert.True(t.TryBegin(102, 6, Home, out _));
        Assert.Equal(2, t.Sent);
    }

    [Fact]
    public void 超时按没回执处理_不改归属也不清等待时钟()
    {
        var t = new PlanetRegionTransferTracker(timeoutUnits: 10);
        Assert.True(t.TryBegin(100, now: 0, Home, out _));

        // 第 11 个单位还没回执：这条按超时清账，然后才允许新的一条
        var action = t.OnReceipt(false, false, false, 0, East.ToId(), 11);
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.Ignore, action);
        Assert.Equal(0, t.ConfirmedRegionId);              // 超时不许把"发过"当成"服务端认了"
        Assert.False(t.NeedsRebind);
        Assert.Equal(1, t.Expired);
        Assert.Equal(0, t.Receipts);
        Assert.False(t.HasPending);

        // 正向孪生：超时之后可以重发（不是永久卡死）
        Assert.True(t.TryBegin(101, now: 12, Home, out _));
        Assert.Equal(2, t.Sent);
    }

    [Fact]
    public void 超时预算用尽前不许重发_用尽后必须能重发()
    {
        var t = new PlanetRegionTransferTracker(timeoutUnits: 10);
        Assert.True(t.TryBegin(100, now: 0, Home, out _));
        Assert.False(t.TryBegin(101, now: 10, Home, out _));     // 正好卡在预算上：还算在飞
        Assert.True(t.TryBegin(102, now: 11, Home, out _));      // 越过去就是超时
        Assert.Equal(1, t.Expired);
        Assert.Equal(2, t.Sent);
    }

    [Fact]
    public void tick倒退被拒后要等过服务端时钟()
    {
        var t = new PlanetRegionTransferTracker(timeoutUnits: 120);
        Assert.True(t.TryBegin(100, now: 0, Home, out _));
        var action = t.OnReceipt(true, accepted: false, applied: false,
            (int)PlanetRegionRejectCode.TickRegression, Home.ToId(), now: 500);
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.WaitClock, action);
        Assert.Equal(500, t.WaitUntilServerTick);

        Assert.False(t.TryBegin(500, 600, Home, out string why));   // 等于服务端时钟仍然不行（倒退的定义就是"不更新"）
        Assert.Contains("还没越过服务端时钟", why);
        Assert.Equal(1, t.RefusedWaitingClock);

        // 正向孪生：越过那个值之后照发，而且等待标记随之清掉
        Assert.True(t.TryBegin(501, 601, Home, out _));
        t.OnReceipt(true, true, true, (int)NoneCode, East.ToId(), 602);
        Assert.Equal(0, t.WaitUntilServerTick);
        Assert.Equal(East.ToId(), t.ConfirmedRegionId);
    }

    [Fact]
    public void 各处置各自更新账()
    {
        var t = new PlanetRegionTransferTracker();
        Assert.True(t.TryBegin(100, 0, Home, out _));
        t.OnReceipt(true, accepted: true, applied: true, (int)NoneCode, East.ToId(), 1);
        Assert.Equal(East.ToId(), t.ConfirmedRegionId);
        Assert.False(t.NeedsRebind);

        Assert.True(t.TryBegin(101, 2, Home, out _));
        t.OnReceipt(true, false, false, (int)PlanetRegionRejectCode.SameRegion, Home.ToId(), 3);
        Assert.Equal(Home.ToId(), t.ConfirmedRegionId);            // 对账成功：确认的就是我们这一块

        Assert.True(t.TryBegin(102, 4, Home, out _));
        t.OnReceipt(true, false, false, (int)PlanetRegionRejectCode.UnknownEntity, 0, 5);
        Assert.True(t.NeedsRebind);
        Assert.Equal(0, t.ConfirmedRegionId);                      // 服务端没归属：确认值必须回到"没有"
        Assert.True(t.ConsumeRebind());
        Assert.False(t.ConsumeRebind());                           // 消费一次即清，不许每次换区都重发落位
    }

    [Fact]
    public void 迟到的旧回执一律忽略且不动账()
    {
        var t = new PlanetRegionTransferTracker();
        Assert.True(t.TryBegin(100, 0, Home, out _));
        t.OnReceipt(true, accepted: true, applied: true, (int)NoneCode, East.ToId(), 1);
        long confirmed = t.ConfirmedRegionId;

        // 没有新的在飞请求却收到回执：可能是超时后服务端才回的旧条，一律 Ignore
        var late = t.OnReceipt(true, accepted: false, applied: false,
            (int)PlanetRegionRejectCode.NotAdjacent, Home.ToId(), 2);
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.Ignore, late);
        Assert.Equal(confirmed, t.ConfirmedRegionId);
        Assert.Equal(1, t.Receipts);                               // 那条迟到的没进账
    }

    [Fact]
    public void 计数器账目自洽()
    {
        var t = new PlanetRegionTransferTracker(timeoutUnits: 10);
        // 发 6 次：其中 2 次被在飞挡住、1 次超时、3 次拿到回执
        Assert.True(t.TryBegin(1, 0, Home, out _));
        Assert.False(t.TryBegin(2, 1, Home, out _));
        t.OnReceipt(hasReceipt: false, false, false, 0, 0, 11);
        Assert.True(t.TryBegin(3, 12, Home, out _));
        Assert.False(t.TryBegin(4, 13, Home, out _));
        t.OnReceipt(true, true, true, (int)NoneCode, East.ToId(), 14);
        Assert.True(t.TryBegin(5, 15, Home, out _));
        t.OnReceipt(true, false, false, (int)PlanetRegionRejectCode.TickRegression, Home.ToId(), 16);
        Assert.False(t.TryBegin(6, 17, Home, out _));                    // 等时钟，没发出去

        Assert.Equal(3, t.Sent);
        Assert.Equal(2, t.RefusedInFlight);
        Assert.Equal(1, t.RefusedWaitingClock);
        Assert.Equal(1, t.Expired);
        Assert.Equal(2, t.Receipts);
        Assert.Contains("sent=3", t.Describe());
        Assert.Contains("expired=1", t.Describe());
    }

    [Fact]
    public void 超时预算不合法时当场拒绝()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetRegionTransferTracker(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetRegionTransferTracker(-5));
        // 正向孪生：最小可用值是 1
        Assert.Equal(1, new PlanetRegionTransferTracker(1).TimeoutUnits);
    }

    // ==================== 会话之间的清账 ====================
    //
    // 引擎里这份账挂在静态上（PlanetRegionTransfers.Tracker），而 Flax 的脚本装配跨 PIE 会话存活。
    // 于是"上一轮没回来的在飞请求"会变成"这一轮第一次换区被自己挡下"，屏幕上没有任何异常。
    // 下面这几条钉的是 Reset 本身，接线那一条在 PlanetRegionSessionResetGateTests 里。

    private static PropertyInfo[] ObservableProperties() =>
        typeof(PlanetRegionTransferTracker)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

    /// <summary>一份"换会话时最可能留下来的账"：一条还在飞 + 每个计数器都非零 + 有确认归属 + 在等时钟。</summary>
    private static PlanetRegionTransferTracker PendingFixture()
    {
        var t = new PlanetRegionTransferTracker(20);
        Assert.True(t.TryBegin(100, 0, Home, out _));
        t.OnReceipt(true, accepted: true, applied: true, (int)NoneCode, East.ToId(), 1);       // ConfirmedRegionId
        Assert.True(t.TryBegin(101, 2, Home, out _));
        t.OnReceipt(false, false, false, 0, 0, 15);                                            // Expired
        Assert.True(t.TryBegin(102, 16, Home, out _));
        t.OnReceipt(true, false, false, (int)PlanetRegionRejectCode.TickRegression, East.ToId(), 17);  // WaitClock
        Assert.False(t.TryBegin(17, 18, Home, out _));                                         // RefusedWaitingClock
        Assert.True(t.TryBegin(103, 19, Home, out _));                                          // 留下一条在飞
        Assert.False(t.TryBegin(104, 20, Home, out _));                                         // RefusedInFlight
        Assert.True(t.HasPending);
        return t;
    }

    /// <summary>另一份：<c>NeedsRebind</c>。它和 <c>ConfirmedRegionId</c> 不可能同时脏——
    /// Rebind 那一支会把确认值清零——所以"每一项都被盯过"必须靠两份夹具的并集。</summary>
    private static PlanetRegionTransferTracker RebindFixture()
    {
        var t = new PlanetRegionTransferTracker(20);
        Assert.True(t.TryBegin(100, 0, Home, out _));
        t.OnReceipt(true, false, false, (int)PlanetRegionRejectCode.UnknownEntity, 0, 1);
        Assert.True(t.NeedsRebind);
        return t;
    }

    [Fact]
    public void Reset把每项账倒回刚构造时的值()
    {
        // 判据不手写属性清单：手写那份会跟着账本增长而失效——日后加一个状态位忘了在 Reset 里清，
        // 清单里没有它，这条照样绿。改成反射取全部可读实例属性，加什么就被自动盯上什么。
        var props = ObservableProperties();
        Assert.True(props.Length >= 12,
            $"反射只找到 {props.Length} 个可读属性：账本大概不再是属性了，这条判据现在盯不住任何状态");
        var names = props.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("TimeoutUnits", names);        // 排除的那一格确实排除了一个存在的名字

        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in new[] { PendingFixture(), RebindFixture() })
        {
            var fresh = new PlanetRegionTransferTracker(t.TimeoutUnits);
            foreach (var p in props)
                if (!Equals(p.GetValue(t), p.GetValue(fresh))) covered.Add(p.Name);

            t.Reset();
            foreach (var p in props.Where(p => p.Name != "TimeoutUnits"))
                Assert.True(Equals(p.GetValue(t), p.GetValue(fresh)),
                    $"{p.Name} 在 Reset 之后还是 {p.GetValue(t)}，而刚构造时是 {p.GetValue(fresh)}");
        }

        // 反空转：除了 TimeoutUnits（正向孪生单独钉），每一项状态都至少被一份夹具盯过。
        // 少了这一段，"两份夹具其实都没把某项弄脏"会让那一项永远不会被这条判据看到。
        foreach (var p in props.Where(p => p.Name != "TimeoutUnits"))
            Assert.True(covered.Contains(p.Name), $"{p.Name} 在两份脏夹具里都与干净态相同——这条判据盯不到它，需要新夹具");
    }

    [Fact]
    public void Reset只清状态不清配置_预算留下且下一条账按它走()
    {
        var t = PendingFixture();
        t.Reset();
        Assert.Equal(20, t.TimeoutUnits);                       // 机制层：配置没被当状态清掉

        // 效果层：Reset 之后那份预算还在干活。清成 0 的话下一条永远被判"已超时"，
        // 表现是每条换区都发得出去（在飞保护形同虚设），而那只会把 grain 打满。
        Assert.True(t.TryBegin(500, 100, Home, out _));
        Assert.False(t.TryBegin(501, 101, Home, out string why));
        Assert.Contains("上一条还在飞", why);
    }

    [Fact]
    public void 不清账时新会话第一次换区被上一轮挡下_清账后必须发得出去()
    {
        // 这条是 Reset 存在的理由本身。缺了它，"Reset 里什么都没清"也能让上面两条绿
        // （属性都还是刚构造时的值？不——它们本来就因为同一个原因相等：见反证）。
        var t = PendingFixture();
        Assert.False(t.TryBegin(500, 10, Home, out string why));    // 上一轮的在飞还没过预算就换了会话
        Assert.Contains("上一条还在飞", why);

        t.Reset();
        Assert.True(t.TryBegin(500, 10, Home, out _));
        Assert.Equal(1, t.Sent);        // 计数器一起归零：回执文件里的账要对得上"这一次"会话
    }

    [Fact]
    public void 干净账上清账是空操作_且清完不是永久拒绝()
    {
        // 正向孪生：Reset 不许把账弄成"比刚构造更坏"的状态（比如把预算清成 0、把在飞标记置真）。
        var t = new PlanetRegionTransferTracker(20);
        var props = ObservableProperties();
        var before = props.ToDictionary(p => p.Name, p => p.GetValue(t));
        t.Reset();
        foreach (var p in props)
            Assert.True(Equals(p.GetValue(t), before[p.Name]), $"{p.Name} 在干净账上被 Reset 改动了");
        Assert.True(t.TryBegin(1, 0, Home, out _));
        Assert.Equal(1, t.Sent);
    }
}
