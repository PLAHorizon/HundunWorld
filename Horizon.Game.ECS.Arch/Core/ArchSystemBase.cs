using System;
using Arch.Core;

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
