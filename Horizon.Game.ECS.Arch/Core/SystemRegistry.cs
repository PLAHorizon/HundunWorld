using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Horizon.Game.ECS.Arch.Core;

/// <summary>
/// 通过反射扫描 <see cref="ArchSystemAttribute"/> 标注的系统类型并注册到 <see cref="ArchWorldHost"/>。
/// 供 Game Feature 模块 (LyraExampleContent / NarrativePro 等) 在加载时统一挂入主循环。
/// </summary>
public static class SystemRegistry
{
    /// <summary>
    /// 在指定程序集中扫描带 <see cref="ArchSystemAttribute"/> 的 <see cref="IArchSystem"/> 实现，
    /// 用 <paramref name="factory"/> 构造实例并 <see cref="ArchWorldHost.AddSystem"/>。
    /// 默认 factory 使用无参构造函数。
    /// </summary>
    public static IReadOnlyList<IArchSystem> RegisterFromAssembly(
        ArchWorldHost host,
        Assembly assembly,
        Func<Type, IArchSystem?>? factory = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(assembly);

        factory ??= DefaultFactory;

        var added = new List<IArchSystem>();
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IArchSystem).IsAssignableFrom(type)) continue;
            if (type.GetCustomAttribute<ArchSystemAttribute>() is null) continue;

            var instance = factory(type);
            if (instance == null) continue;

            host.AddSystem(instance);
            added.Add(instance);
        }

        return added;
    }

    /// <summary>从多个程序集批量注册。</summary>
    public static IReadOnlyList<IArchSystem> RegisterFromAssemblies(
        ArchWorldHost host,
        IEnumerable<Assembly> assemblies,
        Func<Type, IArchSystem?>? factory = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var all = new List<IArchSystem>();
        foreach (var asm in assemblies)
        {
            all.AddRange(RegisterFromAssembly(host, asm, factory));
        }
        return all;
    }

    private static IArchSystem? DefaultFactory(Type type)
    {
        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor == null) return null;
        return (IArchSystem?)ctor.Invoke(null);
    }
}
