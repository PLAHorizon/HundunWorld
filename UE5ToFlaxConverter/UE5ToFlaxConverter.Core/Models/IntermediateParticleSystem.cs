using System.Numerics;

namespace UE5ToFlaxConverter.Core.Models;

/// <summary>
/// UE5 粒子系统（Niagara / Cascade）的中间表示。
/// 对应 Flax 的 ParticleEmitter + ParticleSystem 二进制资产。
/// </summary>
public sealed class IntermediateParticleSystem
{
    public string SourcePath { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public ParticleSystemKind Kind { get; set; } = ParticleSystemKind.Niagara;

    public List<ParticleEmitter> Emitters { get; set; } = new();
    public Vector3 SystemBounds { get; set; }
    public float FixedDeltaTime { get; set; } = 0.01667f;
    public bool UseAutoBounds { get; set; } = true;
}

public enum ParticleSystemKind { Niagara, Cascade }

public sealed class ParticleEmitter
{
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; } = 1000;
    public SimulationMode SimulationMode { get; set; } = SimulationMode.CPU;
    public SimulationSpace SimulationSpace { get; set; } = SimulationSpace.Local;
    public bool EnablePooling { get; set; } = true;
    public bool UseAutoBounds { get; set; } = true;

    public List<ParticleModule> SpawnModules { get; set; } = new();
    public List<ParticleModule> InitializeModules { get; set; } = new();
    public List<ParticleModule> UpdateModules { get; set; } = new();
    public List<ParticleModule> RenderModules { get; set; } = new();

    public List<EmitterParameter> Parameters { get; set; } = new();
}

public enum SimulationMode { CPU, GPU, Auto }
public enum SimulationSpace { Local, World }

public sealed class ParticleModule
{
    public string ModuleType { get; set; } = string.Empty; // 如 "SpawnRate", "Lifetime", "Gravity", "Color", "SpriteRenderer"
    public string SourceClassName { get; set; } = string.Empty; // UE5 原始模块类全名（UNiagaraModule / UParticleModuleXxx）
    public Dictionary<string, object?> Properties { get; set; } = new();
}

public sealed class EmitterParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // float, Vector3, Color
    public object? DefaultValue { get; set; }
}

/// <summary>
/// Niagara/Cascade 常见模块到 Flax 模块的映射辅助。
/// </summary>
public static class ParticleModuleCatalog
{
    public static readonly Dictionary<string, string> NiagaraToFlax = new()
    {
        ["Engine.SpawnRate"] = "SpawnRate",
        ["Engine.Lifetime"] = "Lifetime",
        ["Engine.InitialColor"] = "Color",
        ["Engine.InitialPosition"] = "Position",
        ["Engine.InitialVelocity"] = "Velocity",
        ["Engine.InitialSize"] = "Size",
        ["Engine.Mass"] = "Mass",
        ["Engine.GravityForce"] = "Gravity",
        ["Engine.Velocity"] = "UpdateVelocity",
        ["Engine.Color"] = "UpdateColor",
        ["Engine.ScaleSize"] = "UpdateSize",
        ["Engine.Collision"] = "Collision",
        ["Engine.RibbonRenderer"] = "RibbonRenderer",
        ["Engine.SpriteRenderer"] = "SpriteRenderer",
        ["Engine.MeshRenderer"] = "MeshRenderer",
        ["Engine.LightRenderer"] = "LightRenderer",
        ["Engine.SubUV"] = "SubUV",
        ["Engine.SubUVAge"] = "UpdateSubUV"
    };

    public static readonly Dictionary<string, string> CascadeToFlax = new()
    {
        ["UParticleModuleSpawn"] = "SpawnRate",
        ["UParticleModuleLifetime"] = "Lifetime",
        ["UParticleModuleInitialColor"] = "Color",
        ["UParticleModuleLocation"] = "Position",
        ["UParticleModuleVelocity"] = "Velocity",
        ["UParticleModuleSize"] = "Size",
        ["UParticleModuleGravity"] = "Gravity",
        ["UParticleModuleColor"] = "UpdateColor",
        ["UParticleModuleSizeScale"] = "UpdateSize",
        ["UParticleModuleCollision"] = "Collision",
        ["UParticleModuleRequired"] = "Required",
        ["UParticleModuleSpriteRenderer"] = "SpriteRenderer",
        ["UParticleModuleMeshRenderer"] = "MeshRenderer",
        ["UParticleModuleLight"] = "LightRenderer",
        ["UParticleModuleSubUV"] = "SubUV"
    };
}
