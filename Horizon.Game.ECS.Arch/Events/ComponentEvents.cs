using System;
using System.Collections.Generic;
using Arch.Core;

namespace Horizon.Game.ECS.Arch.Events;

/// <summary>
/// 组件订阅工具：让 UI / 剧情 / 音视频系统可以响应组件的 Add/Set/Remove 而无需轮询。
///
/// 使用模式：
/// <code>
/// ComponentEvents&lt;HealthAuthComponent&gt;.OnSet += (entity, value) => UpdateHealthBar(entity, value);
/// </code>
/// 系统在写入数据后调用 <see cref="RaiseSet"/> / <see cref="RaiseAdd"/> / <see cref="RaiseRemove"/> 触发回调。
/// 这是一个 **协作式** 通知机制（不会自动 hook Arch 内部写入，避免运行时反射开销）。
/// </summary>
/// <typeparam name="TComponent">组件类型。</typeparam>
public static class ComponentEvents<TComponent>
    where TComponent : struct
{
    private static readonly object _lock = new();
    private static Action<Entity, TComponent>? _onAdd;
    private static Action<Entity, TComponent>? _onSet;
    private static Action<Entity, TComponent>? _onRemove;

    /// <summary>组件首次添加到实体时触发。</summary>
    public static event Action<Entity, TComponent> OnAdd
    {
        add { lock (_lock) { _onAdd += value; } }
        remove { lock (_lock) { _onAdd -= value; } }
    }

    /// <summary>组件值更新（含初次写入）时触发。</summary>
    public static event Action<Entity, TComponent> OnSet
    {
        add { lock (_lock) { _onSet += value; } }
        remove { lock (_lock) { _onSet -= value; } }
    }

    /// <summary>组件被移除前触发。</summary>
    public static event Action<Entity, TComponent> OnRemove
    {
        add { lock (_lock) { _onRemove += value; } }
        remove { lock (_lock) { _onRemove -= value; } }
    }

    public static void RaiseAdd(Entity entity, in TComponent component)
    {
        Action<Entity, TComponent>? handler;
        lock (_lock) { handler = _onAdd; }
        handler?.Invoke(entity, component);
    }

    public static void RaiseSet(Entity entity, in TComponent component)
    {
        Action<Entity, TComponent>? handler;
        lock (_lock) { handler = _onSet; }
        handler?.Invoke(entity, component);
    }

    public static void RaiseRemove(Entity entity, in TComponent component)
    {
        Action<Entity, TComponent>? handler;
        lock (_lock) { handler = _onRemove; }
        handler?.Invoke(entity, component);
    }

    /// <summary>移除所有订阅（测试隔离用，生产代码不要随便调用）。</summary>
    public static void ClearForTesting()
    {
        lock (_lock)
        {
            _onAdd = null;
            _onSet = null;
            _onRemove = null;
        }
    }
}
