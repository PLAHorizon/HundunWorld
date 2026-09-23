using System;
using Arch.Core;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.ECS.Arch.Core;

/// <summary>
/// 便捷基类：通过 <see cref="ArchSystemAttribute"/> 自动推导 Group/Order/MainThreadOnly。
/// 派生类只需覆写 <see cref="Update(World, TimeSpan)"/>。
/// </summary>
public abstract class ArchSystemBase : IArchSystem
{
    private readonly ArchSystemAttribute? _attr;

    protected ArchSystemBase()
    {
        _attr = (ArchSystemAttribute?)Attribute.GetCustomAttribute(GetType(), typeof(ArchSystemAttribute));
    }

    /// <summary>
    /// 阶段 5.4.2 — 注入的日志器（可选，实例）。由游戏层在装配系统时注入，
    /// 类别建议为 <c>typeof(T).FullName</c> 或 <c>"Horizon.ECS.Systems"</c>。<br/>
    /// null 时不输出任何日志（零开销，与 <see cref="Diagnostics"/> 语义一致）；
    /// 替换原 <c>System.Diagnostics.Debug.WriteLine</c> 直接打印（Debug 级别，可运行时启用）。
    /// </summary>
    public ILogger? Log { get; set; }


    /// <inheritdoc />
    public virtual string Name => GetType().Name;

    /// <inheritdoc />
    public virtual SystemGroup Group => _attr?.Group ?? SystemGroup.Update;

    /// <inheritdoc />
    public virtual int Order => _attr?.Order ?? 0;

    /// <inheritdoc />
    public virtual bool MainThreadOnly => _attr?.MainThreadOnly ?? false;

    /// <inheritdoc />
    public virtual void Initialize(World world) { }

    /// <inheritdoc />
    public abstract void Update(World world, TimeSpan deltaTime);

    /// <inheritdoc />
    public virtual void Dispose(World world) { }
}
