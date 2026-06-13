namespace Horizon.Game.ECS.Core;

/// <summary>
/// ECS 实体标识。
/// </summary>
public readonly record struct EcsEntity(int Id)
{
    public bool IsValid => Id > 0;

    public override string ToString()
    {
        return IsValid ? $"Entity#{Id}" : "Entity#Invalid";
    }
}
