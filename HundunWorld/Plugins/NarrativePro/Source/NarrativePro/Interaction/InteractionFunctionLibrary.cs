using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 交互函数库。适配 UE5 UInteractionFunctionLibrary（继承 UBlueprintFunctionLibrary）。
    /// UE5 中为蓝图可调用的静态函数集合，Flax 中转换为 static class。
    /// 当前为占位实现，留待后续按需添加交互相关静态工具函数。
    /// </summary>
    public static class InteractionFunctionLibrary
    {
        // 当前无静态函数。后续可在此添加交互相关的静态工具方法，例如：
        // - 查找指定位置附近最近的可交互对象
        // - 批量启用/禁用交互组件
        // - 交互槽位的快捷查询
        // 对应 UE5 UInteractionFunctionLibrary 的蓝图可调用函数。
    }
}
