using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 地图标记导航查询过滤器。适配 UE5 UMapMarkerQueryFilter（继承 UNavigationQueryFilter）。
    /// 自定义查询过滤器，支持更长范围的导航（如跨地图标记寻路）。
    /// Flax 中无 UNavigationQueryFilter 等价物，转为普通 class 保留 InitializeFilter 虚方法作为扩展点，
    /// 待接入 Flax 导航系统后实现实际过滤逻辑。
    /// </summary>
    public class MapMarkerQueryFilter
    {
        /// <summary>是否支持更长范围的导航（对应 UE5 自定义过滤器的核心能力）</summary>
        public bool bSupportsLongerRangeNavigation { get; set; } = true;

        /// <summary>导航搜索半径扩展（与世界单位相同）</summary>
        public float ExtendedSearchRadius { get; set; } = 50000f;

        /// <summary>
        /// 初始化过滤器。适配 UE5 InitializeFilter。
        /// 在路径查询开始前调用，用于配置过滤器的运行时状态。
        /// Flax 中无对应导航数据概念，参数以 object 占位，待接入 Flax 导航系统后细化。
        /// </summary>
        /// <param name="navData">导航数据（Flax 中暂无等价物，使用 object 占位）</param>
        /// <param name="querier">查询发起者（Actor 或 null）</param>
        /// <param name="filterState">过滤器运行时状态（Flax 中暂无等价物，使用 object 占位）</param>
        public virtual void InitializeFilter(object navData, object querier, object filterState)
        {
            // Flax-不兼容: UE5 的 NavigationQueryFilter 在 Flax 无对应物，保留占位。原文 TODO: 接入 Flax 导航系统后实现实际过滤器初始化逻辑
            // UE5 中此处会设置 FNavigationQueryFilter 的各项属性（如区域成本、包含/排除标志等）
            NarrativeLog.Log("[Navigation] MapMarkerQueryFilter.InitializeFilter：Flax 导航系统未接入，使用默认配置");
        }

        /// <summary>是否为有效的查询过滤器。</summary>
        public virtual bool IsValidFilter() => true;
    }
}
