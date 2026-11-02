using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Selection
{
    /// <summary>
    /// 对话菜单的台词组过滤（纯函数，便于离线测试）。
    /// <para>
    /// <b>为什么需要它</b>：内容表里每个选项带组号（模板也有默认组），以前客户端把**所有组**一次性摆出来，
    /// 于是"白天/夜里两套台词"会同时出现。过滤口径：
    /// <list type="bullet">
    /// <item>活跃组 ≤ 0 → 不过滤（旧表没有组列，行为逐字不变）；</item>
    /// <item>组号 0 的选项是**通用项**，任何组都显示；</item>
    /// <item>其余只留与活跃组相等的项；顺序保持原序（菜单顺序是数据）。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 作息时段切换组需要把"当前组"随快照下发（状态增量），那是下一片；本类只负责给定活跃组后的筛选。
    /// </para>
    /// </summary>
    public static class NpcGossipMenu
    {
        /// <summary>返回应显示的选项下标（升序、原序保持）。活跃组 ≤ 0 时返回全部下标。</summary>
        public static int[] IndicesToShow(IReadOnlyList<int> optionGroups, int activeGroup)
        {
            if (optionGroups == null || optionGroups.Count == 0) return Array.Empty<int>();

            if (activeGroup <= 0)
            {
                var all = new int[optionGroups.Count];
                for (var i = 0; i < all.Length; i++) all[i] = i;
                return all;
            }

            var keep = new List<int>(optionGroups.Count);
            for (var i = 0; i < optionGroups.Count; i++)
            {
                var group = optionGroups[i];
                if (group == 0 || group == activeGroup) keep.Add(i);
            }

            return keep.ToArray();
        }
    }
}
