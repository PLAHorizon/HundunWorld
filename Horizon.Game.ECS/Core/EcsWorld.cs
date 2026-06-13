using System;
using System.Collections.Generic;

namespace Horizon.Game.ECS.Core;

/// <summary>
/// 最小可运行 ECS 世界。
/// </summary>
public sealed class EcsWorld
{
    private readonly List<IEcsSystem> _systems = new();
    private int _nextEntityId = 1;

    public EcsComponentStore Components { get; } = new();

    public EcsEntity CreateEntity()
    {
        return new EcsEntity(_nextEntityId++);
    }

    public void AddSystem(IEcsSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
    }

    public IReadOnlyList<IEcsSystem> GetSystems()
    {
        return _systems;
    }

    public void Tick(TimeSpan deltaTime)
    {
        foreach (var system in _systems)
        {
            system.Execute(this, deltaTime);
        }
    }
}
