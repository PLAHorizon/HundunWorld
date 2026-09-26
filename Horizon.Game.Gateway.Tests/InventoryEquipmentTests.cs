using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 角色装备的权威存储回归测试。
/// <para>
/// 历史缺陷：<c>InventoryState.EquippedItems</c> 只记 槽位→物品ID，装备即从背包字典移除，
/// 卸下时凭 ID 重建 <c>ItemInfo{Count=1}</c> —— 一叠 5 件穿再卸就只剩 1 件（物品数量丢失）。
/// 现由 <c>EquippedItemDetails</c> 保存完整条目，本组测试锁死该行为。
/// </para>
/// <para>
/// 不依赖 Orleans 运行时：<see cref="InventoryGrain"/> 的方法只读写注入的状态对象，
/// 直接 mock <see cref="IPersistentState{T}"/> 即可驱动（与 BaselineResyncE2ETests 同一手法）。
/// </para>
/// </summary>
public class InventoryEquipmentTests
{
    private static InventoryGrain CreateGrain(InventoryState state)
    {
        var logger = new Mock<ILogger<InventoryGrain>>();
        var persistent = new Mock<IPersistentState<InventoryState>>();
        persistent.SetupGet(s => s.State).Returns(state);
        return new InventoryGrain(logger.Object, persistent.Object);
    }

    private static ItemInfo Item(long id, int count, int templateId = 100, string name = "物品")
        => new() { ItemId = id, Count = count, TemplateId = templateId, Name = name };

    private static InventoryState StateWith(params ItemInfo[] items)
    {
        var state = new InventoryState { Capacity = 50 };
        foreach (var i in items) state.Items[i.ItemId] = i;
        return state;
    }

    [Fact]
    public async Task Equip_ThenUnequip_PreservesFullItemInfo()
    {
        var state = StateWith(Item(7001, count: 5, templateId: 42, name: "金疮药"));
        var grain = CreateGrain(state);

        Assert.True(await grain.EquipItemAsync(7001, 3));
        Assert.False(state.Items.ContainsKey(7001), "装备后物品应离开背包字典");

        Assert.True(await grain.UnequipItemAsync(3));

        Assert.True(state.Items.TryGetValue(7001, out var restored));
        Assert.Equal(5, restored!.Count);          // 核心断言：数量不丢
        Assert.Equal(42, restored.TemplateId);
        Assert.Equal("金疮药", restored.Name);
    }

    [Fact]
    public async Task Equip_OntoOccupiedSlot_ReturnsOldItemIntact()
    {
        var state = StateWith(Item(1, count: 3), Item(2, count: 2));
        var grain = CreateGrain(state);

        Assert.True(await grain.EquipItemAsync(1, 5));
        Assert.True(await grain.EquipItemAsync(2, 5));   // 同槽换装，把 1 号换回背包

        Assert.True(state.Items.TryGetValue(1, out var swappedBack));
        Assert.Equal(3, swappedBack!.Count);              // 被换下的那件也不丢数量
        Assert.Equal(2, state.EquippedItems[5]);
    }

    [Fact]
    public async Task GetEquippedItemInfos_ReturnsFullDetails()
    {
        var state = StateWith(Item(10, count: 7), Item(11, count: 1));
        var grain = CreateGrain(state);

        Assert.True(await grain.EquipItemAsync(10, 0));
        Assert.True(await grain.EquipItemAsync(11, 1));

        var equipped = await grain.GetEquippedItemInfosAsync();

        Assert.Equal(2, equipped.Count);
        Assert.Contains(equipped, x => x.ItemId == 10 && x.Count == 7);
        Assert.Contains(equipped, x => x.ItemId == 11 && x.Count == 1);
    }

    [Fact]
    public async Task Equip_UnknownItem_IsRejected()
    {
        var grain = CreateGrain(StateWith(Item(1, count: 1)));
        Assert.False(await grain.EquipItemAsync(9999, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(15)]          // 合法槽位为 0..14（14=客户端 EquipmentSlot.Face）
    public async Task Equip_InvalidSlot_IsRejected(int slot)
    {
        var grain = CreateGrain(StateWith(Item(1, count: 1)));
        Assert.False(await grain.EquipItemAsync(1, slot));
    }

    [Fact]
    public async Task Unequip_EmptySlot_IsRejected()
    {
        var grain = CreateGrain(StateWith());
        Assert.False(await grain.UnequipItemAsync(2));
    }

    /// <summary>
    /// 旧存档兼容：槽位有装备但没有对应明细（改动前存下的数据）时，
    /// 卸下不得抛异常，且物品必须回到背包（保真度退化为 1 件，但不能丢东西）。
    /// </summary>
    [Fact]
    public async Task LegacySlot_WithoutDetail_StillReturnsItemOnUnequip()
    {
        var state = new InventoryState { Capacity = 50 };
        state.EquippedItems[4] = 5001;            // 明细字典为空 = 旧数据
        var grain = CreateGrain(state);

        Assert.True(await grain.UnequipItemAsync(4));
        Assert.True(state.Items.ContainsKey(5001));
        Assert.Equal(1, state.Items[5001].Count);
    }

    /// <summary>
    /// 客户端全程使用装备定义 ID（TemplateId 编号空间），不掌握背包实例 ID。
    /// 服务端必须能按模板把装备找出来；同模板多件时取实例 ID 最小的一件，保证确定性。
    /// </summary>
    [Fact]
    public async Task EquipByTemplate_ResolvesInstanceDeterministically()
    {
        var state = new InventoryState { Capacity = 50 };
        state.Items[9] = Item(9, count: 1, templateId: 10001);   // 同模板两件
        state.Items[3] = Item(3, count: 1, templateId: 10001);
        var grain = CreateGrain(state);

        Assert.True(await grain.EquipByTemplateAsync(10001, 4));
        Assert.Equal(3, state.EquippedItems[4]);                 // 取最小实例 ID
    }

    [Fact]
    public async Task EquipByTemplate_UnknownTemplate_IsRejected()
    {
        var grain = CreateGrain(StateWith(Item(1, count: 1, templateId: 10001)));
        Assert.False(await grain.EquipByTemplateAsync(99999, 4));
    }

    /// <summary>
    /// 面饰在客户端枚举里是 14（<c>EquipmentSlot.Face</c>）。
    /// 服务端上界曾硬编码 13，导致面饰永远装不上——这里锁死 14 可用、15 仍被拒。
    /// </summary>
    [Theory]
    [InlineData(14, true)]
    [InlineData(15, false)]
    public async Task Equip_SlotUpperBound_MatchesClientEnum(int slot, bool expected)
    {
        var grain = CreateGrain(StateWith(Item(1, count: 1)));
        Assert.Equal(expected, await grain.EquipItemAsync(1, slot));
    }

    #region 背包物品发放（本地清单迁移的服务端落点）

    /// <summary>
    /// 迁移是按 TemplateId 逐条调 AddItemAsync 的。若同模板被建成多个实例，
    /// 玩家背包格子会被无意义占满；这里锁死"同模板堆叠成一个实例"。
    /// </summary>
    [Fact]
    public async Task AddItem_SameTemplate_StacksIntoSingleInstance()
    {
        var state = new InventoryState { Capacity = 50 };
        var grain = CreateGrain(state);

        Assert.True(await grain.AddItemAsync(10001, 3, 0));
        Assert.True(await grain.AddItemAsync(10001, 2, 0));

        Assert.Single(state.Items);
        Assert.Equal(5, Assert.Single(state.Items).Value.Count);
    }

    [Fact]
    public async Task AddItem_RejectsInvalidTemplateOrQuantity()
    {
        var grain = CreateGrain(new InventoryState { Capacity = 50 });

        Assert.False(await grain.AddItemAsync(0, 1, 0));
        Assert.False(await grain.AddItemAsync(10001, 0, 0));
        Assert.False(await grain.AddItemAsync(10001, -5, 0));
    }

    /// <summary>
    /// 容量满必须拒绝而不是无限增长——迁移上传的条目数来自玩家本地历史数据，不受服务端约束。
    /// </summary>
    [Fact]
    public async Task AddItem_WhenFull_IsRejected()
    {
        var state = new InventoryState { Capacity = 1 };
        var grain = CreateGrain(state);

        Assert.True(await grain.AddItemAsync(10001, 1, 0));
        Assert.False(await grain.AddItemAsync(10002, 1, 0));
        Assert.Single(state.Items);
    }

    /// <summary>
    /// 不同品质视为不同条目：品质是服务端堆叠键的一部分（TemplateId+Quality）。
    /// </summary>
    [Fact]
    public async Task AddItem_DifferentQuality_CreatesSeparateEntry()
    {
        var state = new InventoryState { Capacity = 50 };
        var grain = CreateGrain(state);

        Assert.True(await grain.AddItemAsync(10001, 1, 0));
        Assert.True(await grain.AddItemAsync(10001, 1, 2));

        Assert.Equal(2, state.Items.Count);
    }

    #endregion

    /// <summary>
    /// 背包满时卸下必须被拒绝，且**不能**先把装备从槽位移掉——
    /// 否则装备既不在背包也不在身上，直接凭空消失。
    /// </summary>
    [Fact]
    public async Task Unequip_WhenInventoryFull_KeepsEquippedItem()
    {
        var state = new InventoryState { Capacity = 1 };
        state.Items[1] = Item(1, count: 1);
        var grain = CreateGrain(state);

        Assert.True(await grain.EquipItemAsync(1, 0));
        state.Items[2] = Item(2, count: 1);       // 把唯一的格子占满

        Assert.False(await grain.UnequipItemAsync(0));
        Assert.True(state.EquippedItems.ContainsKey(0), "卸下失败时装备必须仍在身上");
    }
}
