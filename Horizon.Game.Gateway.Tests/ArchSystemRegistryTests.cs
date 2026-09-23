using System;
using System.Linq;
using System.Reflection;
using Arch.Core;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Systems;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// <see cref="SystemRegistry"/> 反射注册回归（2026-09 多客户端互相不可见事故）。
/// <para>
/// 事故根因：<see cref="SystemRegistry.DefaultFactory"/> 曾只识别真·无参构造，
/// <c>SnapshotApplySystem(SnapshotReceiveBuffer? receiveBuffer = null)</c> 这种
/// "全可选参数构造"被<b>静默跳过</b>——客户端快照不被应用、远端实体不创建、玩家间互相不可见。
/// </para>
/// <para>
/// 本套件锁定两条不变量：
/// 1) ECS.Arch 程序集中全部 <see cref="ArchSystemAttribute"/> 类型必须全部注册成功；
/// 2) 无法构造的系统必须抛异常，而非静默跳过。
/// </para>
/// </summary>
public class ArchSystemRegistryTests
{
    [Fact]
    public void RegisterFromAssembly_RegistersAllArchSystems_IncludingOptionalCtorSystems()
    {
        using var host = new ArchWorldHost(World.Create());
        var assembly = typeof(SnapshotApplySystem).Assembly;

        var expectedCount = assembly.GetTypes().Count(IsArchSystemType);
        var systems = SystemRegistry.RegisterFromAssembly(host, assembly);

        Assert.Equal(expectedCount, systems.Count);
        // 回归：可选参数构造曾被静默跳过——必须注册到位。
        Assert.Contains(systems, s => s is SnapshotApplySystem);
    }

    [Fact]
    public void RegisterFromAssembly_UnconstructibleSystem_ThrowsInsteadOfSilentSkip()
    {
        using var host = new ArchWorldHost(World.Create());
        Assert.Throws<InvalidOperationException>(
            () => SystemRegistry.RegisterFromAssembly(host, typeof(UnconstructibleSystem).Assembly));
    }

    private static bool IsArchSystemType(Type type) =>
        !type.IsAbstract && !type.IsInterface
        && typeof(IArchSystem).IsAssignableFrom(type)
        && type.GetCustomAttribute<ArchSystemAttribute>() is not null;

    /// <summary>测试用：带 [ArchSystem] 但不具备可用构造的系统（必须触发告警而非静默跳过）。</summary>
    [ArchSystem(SystemGroup.Update, order: 999)]
    private sealed class UnconstructibleSystem : ArchSystemBase
    {
        public UnconstructibleSystem(int required) => _ = required;

        public override void Update(World world, TimeSpan deltaTime) { }
    }
}
