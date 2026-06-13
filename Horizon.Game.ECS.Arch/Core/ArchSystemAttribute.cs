using System;

namespace Horizon.Game.ECS.Arch.Core;

/// <summary>
/// 标注一个 <see cref="IArchSystem"/> 实现的元数据，便于
/// <see cref="SystemRegistry"/> 通过反射自动发现并注册到 <see cref="ArchWorldHost"/>。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ArchSystemAttribute : Attribute
{
    /// <summary>所属系统组（决定执行阶段）。</summary>
    public SystemGroup Group { get; }

    /// <summary>同组内执行顺序，越小越早。</summary>
    public int Order { get; }

    /// <summary>是否仅允许主线程执行（涉及 UE Actor/UI 必须为 true）。</summary>
    public bool MainThreadOnly { get; }

    public ArchSystemAttribute(SystemGroup group, int order = 0, bool mainThreadOnly = false)
    {
        Group = group;
        Order = order;
        MainThreadOnly = mainThreadOnly;
    }
}
