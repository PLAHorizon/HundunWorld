using System;
using HundunWorld.Game.Selection;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 对话菜单的台词组过滤：活跃组、通用项（组号 0）与原序保持。
/// <para>
/// 以前客户端把内容表里**所有组**的选项一次摆出来，"白天/夜里两套台词"会同时出现；
/// 这份判据也是模板 <c>gossip_group_id</c> 唯一的读者。
/// </para>
/// </summary>
public class NpcGossipMenuTests
{
    [Fact]
    public void 活跃组为零时不过滤_保持旧行为()
    {
        var indices = NpcGossipMenu.IndicesToShow(new[] { 2, 9, 0 }, 0);
        Assert.Equal(new[] { 0, 1, 2 }, indices);
    }

    [Fact]
    public void 只留活跃组与通用项_且保持原序()
    {
        var indices = NpcGossipMenu.IndicesToShow(new[] { 2, 9, 0, 2 }, 2);
        Assert.Equal(new[] { 0, 2, 3 }, indices);
    }

    [Fact]
    public void 一项都不匹配时回空表_由调用方退回直接开窗()
    {
        Assert.Empty(NpcGossipMenu.IndicesToShow(new[] { 1, 3 }, 2));
        Assert.Empty(NpcGossipMenu.IndicesToShow(Array.Empty<int>(), 2));
        Assert.Empty(NpcGossipMenu.IndicesToShow(null, 2));
    }
}
