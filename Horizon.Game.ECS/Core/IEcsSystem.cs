using System;

namespace Horizon.Game.ECS.Core;

/// <summary>
/// ECS 系统执行接口。
/// </summary>
public interface IEcsSystem
{
    string Name { get; }

    void Execute(EcsWorld world, TimeSpan deltaTime);
}
