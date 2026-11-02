using System;
using System.Linq;
using Horizon.Orleans.Grains;
using Horizon.QuestCli;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// 任务奖励发出去**用不了**这一类的守门：物品"算不算秘籍"的判据。
/// <para>
/// 背景（<c>docs/TECH_DEBT.md</c> T9）：服务端与客户端都按"id ≥ 800000 就是秘籍"分流，
/// 而库里有 15 行五行素材/信物正落在这个号段里——其中 5 件信物（910011..910015）带
/// <c>use_effect={"kind":"favor"}</c>。旧顺序下它们先被当成秘籍转投研读，好感效果永远不生效：
/// **任务奖励发出去就是一张点了没反应的纸**。现在判据先问模板声明了什么效果。
/// </para>
/// <para>
/// 这条链路属于"奖励可关联所有游戏物品"的执行末端：下拉里能选到、Grain 能发到账，
/// 但若使用端把它认错档，承诺其实没兑现。所以单列断言，不塞进发放测试里。
/// </para>
/// </summary>
public class QuestRewardRoutingTests
{
    private static bool Enabled => !string.IsNullOrWhiteSpace(ConnectionConfig.Resolve(out _));

    /// <param name="templateId">物品模板 id。</param>
    /// <param name="resolved">是否读到了模板行（读不到时应退回号段旧口径）。</param>
    /// <param name="itemType">模板档位。</param>
    /// <param name="declaresFavor">是否声明了 favor 效果。</param>
    /// <param name="expected">该不该走研读。</param>
    [Theory]
    [InlineData(810101, true, 6, false, true)]     // 真秘籍：段内 + 档位 6 + 无好感 → 研读
    [InlineData(890001, true, 6, false, true)]     // 同上（skill_id 90001 那本）
    [InlineData(910011, true, 2, true, false)]     // 五行信物：段内但声明 favor → 不许当秘籍
    [InlineData(910015, true, 2, true, false)]
    [InlineData(910001, true, 3, false, false)]    // 素材：段内、无效果、档位不是秘籍 → 也不许当秘籍（T9②）
    [InlineData(910010, true, 3, false, false)]
    [InlineData(700001, true, 4, false, false)]    // 材料：段外
    [InlineData(799999, true, 6, false, false)]    // 号段边界外，哪怕档位写着秘籍
    [InlineData(800000, true, 6, false, true)]     // 号段边界内
    [InlineData(910001, false, 0, false, true)]    // 查不到模板：退回旧的号段口径，不许把书全禁掉
    [InlineData(810101, false, 0, false, true)]
    public void 号段只是必要条件_模板档位与声明的效果才是判据(
        int templateId, bool resolved, int itemType, bool declaresFavor, bool expected)
        => Assert.Equal(expected, InventoryGrain.RoutesAsSkillBook(templateId, resolved, itemType, declaresFavor));

    [Fact]
    public void 真库里所有带好感效果的行都不许被当成秘籍()
    {
        if (!Enabled) return;

        var connection = ConnectionConfig.Resolve(out var hint);
        Assert.False(string.IsNullOrWhiteSpace(connection), "连不上 Game 库：" + hint);

        using var store = new QuestStore(connection!);
        var items = store.AllItems();

        var favorRows = items
            .Where(item => !string.IsNullOrWhiteSpace(item.UseEffect) && item.UseEffect!.Contains("\"favor\""))
            .ToArray();
        Assert.True(favorRows.Length >= 5, "库里带好感效果的物品少于 5 件（" + favorRows.Length + "），这条会空转");

        foreach (var row in favorRows)
        {
            Assert.False(InventoryGrain.RoutesAsSkillBook(row.Id, profileResolved: true, row.ItemType, declaresFavorEffect: true),
                          "物品 " + row.Id + " 声明了 favor 效果，却被判成秘籍——使用它不会加好感");
        }

        // 反向：真秘籍一档**不许**声明 favor 效果，否则它会从研读里溜走（那才是这次改动可能引入的新错）。
        var booksWithFavor = favorRows.Where(row => row.ItemType == 6).ToArray();
        Assert.Empty(booksWithFavor);

        // 号段内的非秘籍行仍是那 15 件（T9 现状）；哪天多了新的，这里先红。
        var strays = items.Where(item => item.Id >= 800000 && item.ItemType != 6).ToArray();
        Assert.Equal(15, strays.Length);
        Assert.All(strays, item => Assert.InRange(item.Id, 910001, 910015));

        // 按真库档位逐条走一遍判据：15 件 strays 一个都不该被当秘籍，91 本真秘籍全都该。
        Assert.All(strays, item => Assert.False(
            InventoryGrain.RoutesAsSkillBook(item.Id, profileResolved: true, item.ItemType, declaresFavorEffect: false),
            "物品 " + item.Id + "（档位 " + item.ItemType + "）不该被当成秘籍"));
        Assert.All(items.Where(item => item.Id >= 800000 && item.ItemType == 6),
                   item => Assert.True(
                       InventoryGrain.RoutesAsSkillBook(item.Id, profileResolved: true, item.ItemType, declaresFavorEffect: false),
                       "真秘籍 " + item.Id + " 不该被排除在研读之外"));
    }
}
