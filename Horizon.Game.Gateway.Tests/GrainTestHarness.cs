using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Grain 裸激活共享测试基建。
/// <para>
/// Orleans 10 的 <see cref="Grain"/> 依赖 GrainContext/Runtime 才能提供
/// <c>GetPrimaryKeyLong()</c>/<c>GrainId</c>/<c>RegisterTimer</c>（委托给
/// <c>Runtime.TimerRegistry</c>）等能力；直接构造（不经 TestCluster）的测试需要
/// 注入 mock <see cref="IGrainContext"/> 与 mock <see cref="IGrainRuntime"/> +
/// <see cref="global::Orleans.Timers.ITimerRegistry"/>，否则裸激活会在定时器注册处抛
/// <c>EnsureRuntime</c> 异常。本助手统一封装该基建，供所有直接构造 grain 的测试复用。
/// </para>
/// </summary>
public static class GrainTestHarness
{
    /// <summary>创建 grain 并注入 mock IGrainContext（GrainId 指向指定类型/key）。</summary>
    public static T CreateGrain<T>(string grainType = "ZoneShard", string grainKey = "1") where T : Grain, new()
    {
        var grain = new T();
        AttachGrainContext(grain, grainType, grainKey);
        return grain;
    }

    /// <summary>
    /// 为已有 grain 实例注入 mock <see cref="IGrainContext"/>（GrainId）。
    /// 用于无法用 <c>new T()</c> 构造的 grain（如需要非默认构造参数的 ZoneShardGrain）。
    /// </summary>
    public static T AttachGrainContext<T>(T grain, string grainType = "ZoneShard", string grainKey = "1") where T : Grain
    {
        var grainId = GrainId.Create(GrainType.Create(grainType), grainKey);
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);
        InjectBackingField(grain, "<GrainContext>k__BackingField", mockContext.Object);
        return grain;
    }

    /// <summary>
    /// 注入 mock <see cref="IGrainRuntime"/> + mock <see cref="global::Orleans.Timers.ITimerRegistry"/>，
    /// 使 <see cref="Grain.RegisterTimer"/>（委托给 Runtime.TimerRegistry）可用——裸激活的
    /// persist/orphan/sceneObject 定时器注册不再抛 EnsureRuntime 异常。
    /// </summary>
    public static (Mock<IGrainRuntime> Runtime, Mock<global::Orleans.Timers.ITimerRegistry> Timers)
        AttachMockRuntime<T>(T grain) where T : Grain
    {
        var mockRuntime = new Mock<IGrainRuntime>();
        var timerRegistry = new Mock<global::Orleans.Timers.ITimerRegistry>();
        timerRegistry
            .Setup(r => r.RegisterTimer(It.IsAny<IGrainContext>(), It.IsAny<Func<object?, Task>>(),
                It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());
        mockRuntime.SetupGet(r => r.TimerRegistry).Returns(timerRegistry.Object);
        InjectBackingField(grain, "<Runtime>k__BackingField", mockRuntime.Object);
        return (mockRuntime, timerRegistry);
    }

    /// <summary>反射注入 backing 字段（Grain 的 GrainContext/Runtime 都是 auto-property backing field）。</summary>
    public static void InjectBackingField<T>(T grain, string fieldName, object value) where T : Grain
    {
        var field = typeof(Grain).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(grain, value);
    }

    /// <summary>反射注入任意实例字段（含 private，业务字段如 _logger/_characterState）。</summary>
    public static void InjectField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    /// <summary>反射调用 OnActivateAsync（Grain 无公开入口，仅 override 点）。</summary>
    public static async Task ActivateAsync<T>(T grain) where T : Grain
    {
        var method = typeof(T).GetMethod("OnActivateAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(grain, new object[] { CancellationToken.None })!;
    }

    /// <summary>反射调用 OnDeactivateAsync。</summary>
    public static async Task DeactivateAsync<T>(T grain, DeactivationReason reason = default) where T : Grain
    {
        var method = typeof(T).GetMethod("OnDeactivateAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(grain, new object[] { reason, CancellationToken.None })!;
    }

    /// <summary>无副作用 IDisposable，供 timer registry mock 返回。</summary>
    public sealed class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
