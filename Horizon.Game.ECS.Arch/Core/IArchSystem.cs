using System;
using Arch.Core;

namespace Horizon.Game.ECS.Arch.Core;

/// <summary>
/// Arch 世界中的系统接口。
/// 实现请保持无状态/线程安全（Worker 线程组）或显式声明仅主线程（标记 <see cref="ArchSystemAttribute.MainThreadOnly"/>）。
/// </summary>
public interface IArchSystem
{
    /// <summary>系统名称（诊断/调度用）。</summary>
    string Name { get; }

    /// <summary>所属系统组，决定执行阶段。</summary>
    SystemGroup Group { get; }

    /// <summary>同组内执行顺序，数值越小越早执行。</summary>
    int Order { get; }

    /// <summary>仅在主线程执行（通常为接触 UE Actor / UI 的系统）。</summary>
    bool MainThreadOnly { get; }

    /// <summary>世界注册时调用，用于缓存查询、订阅事件。</summary>
    void Initialize(World world) { }

    /// <summary>每帧执行。</summary>
    void Update(World world, TimeSpan deltaTime);

    /// <summary>世界销毁时调用，用于释放资源。</summary>
    void Dispose(World world) { }
}
