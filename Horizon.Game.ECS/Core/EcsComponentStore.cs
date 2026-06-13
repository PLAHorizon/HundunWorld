using System;
using System.Collections.Generic;

namespace Horizon.Game.ECS.Core;

/// <summary>
/// 组件存储容器，按组件类型分桶。
/// </summary>
public sealed class EcsComponentStore
{
    private readonly Dictionary<Type, object> _typedStores = new();

    public void Set<TComponent>(EcsEntity entity, TComponent component)
        where TComponent : notnull
    {
        if (!entity.IsValid)
        {
            throw new ArgumentException("entity is invalid", nameof(entity));
        }

        GetTypedStore<TComponent>()[entity.Id] = component;
    }

    public bool TryGet<TComponent>(EcsEntity entity, out TComponent component)
        where TComponent : notnull
    {
        component = default!;
        if (!entity.IsValid)
        {
            return false;
        }

        var store = GetTypedStore<TComponent>();
        return store.TryGetValue(entity.Id, out component);
    }

    public bool Remove<TComponent>(EcsEntity entity)
        where TComponent : notnull
    {
        if (!entity.IsValid)
        {
            return false;
        }

        return GetTypedStore<TComponent>().Remove(entity.Id);
    }

    public IReadOnlyDictionary<int, TComponent> ReadAll<TComponent>()
        where TComponent : notnull
    {
        return GetTypedStore<TComponent>();
    }

    private Dictionary<int, TComponent> GetTypedStore<TComponent>()
        where TComponent : notnull
    {
        if (_typedStores.TryGetValue(typeof(TComponent), out var existing))
        {
            return (Dictionary<int, TComponent>)existing;
        }

        var created = new Dictionary<int, TComponent>();
        _typedStores[typeof(TComponent)] = created;
        return created;
    }
}
