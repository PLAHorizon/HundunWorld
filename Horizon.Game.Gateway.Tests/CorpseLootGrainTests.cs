using System.Threading.Tasks;
using Horizon.Orleans.Grains.Corpse;
using Horizon.Orleans.Interface.Corpse;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// <see cref="CorpseLootGrain"/> 的无集群判据。
/// <para>它此前只有"编译通过"这一层证据——而判据真正要钉的是：Grain 只是持有
/// <c>MonsterCorpseLootState</c> 并把它包成线上 DTO，**判据没有在 Grain 里重写一份**。
/// 直接 new Grain（它没有构造依赖、也没有持久化状态），不起集群。</para>
/// <para><c>RemoveAsync</c> 会调 <c>DeactivateOnIdle</c>（需要 Grain 上下文），裸对象上调会抛，
/// 所以这里用"新建一具没登记过的尸体"来表达作废后的语义，而不是去调它。</para>
/// </summary>
public class CorpseLootGrainTests
{
    /// <summary>取的人（角色 id）：这条用例里所有"有资格"的取法都用它。</summary>
    private const ulong Looter = 42UL;

    private static CorpseLootRegistrationWire Registration(ulong corpseId, long expireTick = 1200,
                                                          ulong[]? contributors = null)
        => new()
        {
            CorpseEntityId = corpseId,
            ExpireTick = expireTick,
            PickupDistanceMeters = 5.5f,
            Slots = new[]
            {
                new CorpseLootSlotWire { ItemId = 555, Count = 2 },
                new CorpseLootSlotWire { ItemId = 0, Count = 1, Money = 10 },
            },
            Contributors = contributors ?? System.Array.Empty<ulong>(),
        };

    [Fact]
    public async Task Grain按同一套状态机裁决且没登记过的尸体一律Unknown()
    {
        var grain = new CorpseLootGrain();

        // 没登记过：UnknownCorpse（不是 Empty——"这具尸体不存在"与"它掉了个空"是两件事）
        var unknown = await grain.TakeAsync(Looter, 0, 700, 0f);
        Assert.Equal(7, unknown.Status);

        await grain.RegisterAsync(Registration(42UL));

        // 取走：带回槽内容
        var taken = await grain.TakeAsync(Looter, 0, 700, 0f);
        Assert.Equal(1, taken.Status);
        Assert.NotNull(taken.Slot);
        Assert.Equal(555, taken.Slot!.ItemId);
        Assert.Equal(2, taken.Slot.Count);

        // 重复取同一槽：AlreadyTaken，且**不带**槽内容（不能再发一次）
        var again = await grain.TakeAsync(Looter, 0, 700, 0f);
        Assert.Equal(2, again.Status);
        Assert.Null(again.Slot);

        // 太远 / 到期：状态与 DS 侧同源（同一个 MonsterCorpseLootState 判据）
        Assert.Equal(4, (await grain.TakeAsync(Looter, 1, 700, 100f)).Status);
        Assert.Equal(3, (await grain.TakeAsync(Looter, 1, 1200, 0f)).Status);

        // 金币槽：取走时 Money 带回来
        var money = await grain.TakeAsync(Looter, 1, 700, 5.5f);
        Assert.Equal(1, money.Status);
        Assert.Equal(10, money.Slot!.Money);

        // 越界槽号：InvalidSlot（不是"空"）
        Assert.Equal(5, (await grain.TakeAsync(Looter, 9, 700, 0f)).Status);

        // 取下一件（槽号 -1）：按槽序连取，回执带实际槽号；取空后是 AlreadyTaken 而不是 Empty
        var fresh = new CorpseLootGrain();
        await fresh.RegisterAsync(Registration(43UL));
        var first = await fresh.TakeAsync(Looter, -1, 700, 0f);
        Assert.Equal(1, first.Status);
        Assert.Equal(0, first.SlotIndex);
        Assert.Equal(555, first.Slot!.ItemId);
        var second = await fresh.TakeAsync(Looter, -1, 700, 0f);
        Assert.Equal(1, second.Status);
        Assert.Equal(1, second.SlotIndex);
        Assert.Equal(10, second.Slot!.Money);
        Assert.Equal(2, (await fresh.TakeAsync(Looter, -1, 700, 0f)).Status);   // 取空 = AlreadyTaken

        // 同一实体 id 重新登记 ⇒ 以最后一次为准（重生复用实体 id 的形状）
        await grain.RegisterAsync(Registration(42UL, expireTick: 2400));
        var afterReRegister = await grain.TakeAsync(Looter, 0, 700, 0f);
        Assert.Equal(1, afterReRegister.Status);   // 掩码被重置
        Assert.Equal(3, (await grain.TakeAsync(Looter, 1, 2400, 0f)).Status);   // 新窗口生效
    }

    /// <summary>
    /// 击杀参与者才能取（2026-10-29 拍板，AC `DIDNT_KILL`）。
    /// <para>三种输入各断一条：名单里的能取、名单外的被拒且**不扣槽**、名单为空时谁都能取（环境击杀/历史登记）。
    /// "被拒且不扣槽"必须单独断言——否则实现成"先扣再判"也会全绿，而症状是别人路过一趟就把你的东西取掉了。</para>
    /// </summary>
    [Fact]
    public async Task 只有击杀参与者能取且被拒时不扣槽()
    {
        var grain = new CorpseLootGrain();
        await grain.RegisterAsync(Registration(77UL, contributors: new[] { Looter }));

        // 名单外的人：NotYours（8），不带内容
        var stranger = await grain.TakeAsync(99UL, 0, 700, 0f);
        Assert.Equal(8, stranger.Status);
        Assert.Null(stranger.Slot);

        // 认不出是谁（0）也不能过：漏传身份不能被当成"放行"
        Assert.Equal(8, (await grain.TakeAsync(0UL, 0, 700, 0f)).Status);

        // 被拒之后槽还在：名单里的人照样能取到第一件
        var looter = await grain.TakeAsync(Looter, 0, 700, 0f);
        Assert.Equal(1, looter.Status);
        Assert.Equal(555, looter.Slot!.ItemId);

        // 名单为空 = 不限制（环境击杀的怪没有任何参与者，尸体不该变成打不开的箱子）
        var unrestricted = new CorpseLootGrain();
        await unrestricted.RegisterAsync(Registration(78UL));
        Assert.Equal(1, (await unrestricted.TakeAsync(4444UL, 0, 700, 0f)).Status);
    }
}
