using System;
using Arch.Core;
using Horizon.Game.ECS.Core;

namespace Horizon.Game.ECS.Arch.Compatibility;

/// <summary>
/// 把旧版 <see cref="IEcsSystem"/> 适配为 Arch <see cref="Horizon.Game.ECS.Arch.Core.IArchSystem"/>。
/// 老系统继续在它自己的轻量 <see cref="EcsWorld"/> 上运行（由调用方持有），
/// 适配器只是把 Tick 时机挂到 Arch 调度上，方便渐进迁移：每个系统逐个改写时不破坏整体执行。
/// </summary>
public sealed class LegacyEcsSystemAdapter : Horizon.Game.ECS.Arch.Core.IArchSystem
{
    private readonly IEcsSystem _legacy;
    private readonly EcsWorld _legacyWorld;

    public LegacyEcsSystemAdapter(
        IEcsSystem legacySystem,
        EcsWorld legacyWorld,
        Horizon.Game.ECS.Arch.Core.SystemGroup group = Horizon.Game.ECS.Arch.Core.SystemGroup.Update,
        int order = 0,
        bool mainThreadOnly = true)
    {
        _legacy = legacySystem ?? throw new ArgumentNullException(nameof(legacySystem));
        _legacyWorld = legacyWorld ?? throw new ArgumentNullException(nameof(legacyWorld));
        Group = group;
        Order = order;
        MainThreadOnly = mainThreadOnly;
    }

    public string Name => $"Legacy::{_legacy.Name}";

    public Horizon.Game.ECS.Arch.Core.SystemGroup Group { get; }

    public int Order { get; }

    public bool MainThreadOnly { get; }

    public void Update(World world, TimeSpan deltaTime)
    {
        _legacy.Execute(_legacyWorld, deltaTime);
    }
}
