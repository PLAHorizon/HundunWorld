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

    /// <summary>
    /// 默认构造工厂：优先真·无参构造；否则回退到"全部参数均可选"的最少参数构造
    /// （以 <see cref="Type.Missing"/> 触发默认值绑定）。
    /// <para>
    /// ⚠ 不能用"带可选参数的构造"冒充无参构造：<see cref="Type.GetConstructor(Type)"/> 只匹配
    /// 真正的无参签名，全可选参数构造会被漏掉。历史上 <c>SnapshotApplySystem</c> 正因此被
    /// **静默跳过**，导致客户端快照不被应用、多客户端互相不可见（2026-09 事故）。
    /// </para>
    /// <para>
    /// 无法构造的系统**抛异常而非返回 null**：带 <see cref="ArchSystemAttribute"/> 的系统没有
    /// 可用构造是编程错误，静默跳过只会让"少注册一个系统"难以察觉。
    /// </para>
    /// </summary>
    private static IArchSystem? DefaultFactory(Type type)
    {
        var parameterless = type.GetConstructor(Type.EmptyTypes);
        if (parameterless != null)
        {
            return (IArchSystem?)parameterless.Invoke(null);
        }

        // 回退：全部参数带默认值的构造（最少参数者优先），以 Type.Missing 绑定默认值。
        ConstructorInfo? optionalCtor = null;
        foreach (var ctor in type.GetConstructors())
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length == 0) continue;
            var allOptional = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (!parameters[i].IsOptional) { allOptional = false; break; }
            }
            if (!allOptional) continue;
            if (optionalCtor == null || parameters.Length < optionalCtor.GetParameters().Length)
            {
                optionalCtor = ctor;
            }
        }

        if (optionalCtor != null)
        {
            var args = new object?[optionalCtor.GetParameters().Length];
            for (var i = 0; i < args.Length; i++) args[i] = Type.Missing;
            return (IArchSystem?)optionalCtor.Invoke(args);
        }

        throw new InvalidOperationException(
            $"Arch 系统 '{type.FullName}' 没有可用的构造函数：需提供无参构造，或全部参数带默认值的构造。" +
            "反射注册器不允许静默跳过系统。");
    }
}
