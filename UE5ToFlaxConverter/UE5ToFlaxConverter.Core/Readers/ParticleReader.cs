using System.IO;
using Microsoft.Extensions.Logging;
using UE5ToFlaxConverter.Core.Models;
using UObject = CUE4Parse.UE4.Assets.Exports.UObject;

namespace UE5ToFlaxConverter.Core.Readers;

/// <summary>
/// UE5 粒子系统读取器。支持 Niagara (UNiagaraSystem) 和 Cascade (UParticleSystem)。
/// </summary>
public sealed class ParticleReader
{
    private readonly UassetProvider _provider;
    private readonly ILogger<ParticleReader>? _logger;

    public ParticleReader(UassetProvider provider, ILogger<ParticleReader>? logger = null)
    {
        _provider = provider;
        _logger = logger;
    }

    public IntermediateParticleSystem Read(string assetPath)
    {
        // 资源名优先从路径提取（避免 obj.Name 误用辅助对象名如 AssetImportData）
        var assetName = Path.GetFileNameWithoutExtension(assetPath);

        // 遍历所有 Export，找真正的 NiagaraSystem / UParticleSystem 主对象
        UObject? mainObj = null;
        string className = string.Empty;
        try
        {
            var allExports = _provider.LoadAllObjects(assetPath);
            // 优先按 ExportType 精确匹配
            foreach (var obj in allExports)
            {
                var exportType = obj.ExportType ?? string.Empty;
                if (exportType == "NiagaraSystem" || exportType == "ParticleSystem"
                    || exportType == "NiagaraEmitter" || exportType == "NiagaraScript")
                {
                    mainObj = obj;
                    className = exportType;
                    break;
                }
            }
            // 退化1：按反射类名查找
            if (mainObj == null)
            {
                foreach (var obj in allExports)
                {
                    var cls = ReflectionHelper.GetClassName(obj);
                    if (cls == "NiagaraSystem" || cls == "ParticleSystem"
                        || cls == "NiagaraEmitter" || cls == "NiagaraScript")
                    {
                        mainObj = obj;
                        className = cls;
                        break;
                    }
                }
            }
            // 退化2：自动推断（跳过辅助类型）
            if (mainObj == null)
            {
                mainObj = _provider.LoadObject(assetPath);
                className = mainObj.ExportType ?? ReflectionHelper.GetClassName(mainObj);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("加载 ParticleSystem 失败: {Path} -> {Msg}", assetPath, ex.Message);
            return new IntermediateParticleSystem
            {
                SourcePath = assetPath,
                AssetName = assetName,
                Kind = ParticleSystemKind.Niagara
            };
        }

        _logger?.LogInformation("读取 ParticleSystem: {Name} ({Class})", assetName, className);

        return className switch
        {
            "NiagaraSystem" => ReadNiagara(mainObj, assetPath, assetName),
            "ParticleSystem" => ReadCascade(mainObj, assetPath, assetName),
            _ => new IntermediateParticleSystem
            {
                SourcePath = assetPath,
                AssetName = assetName,
                Kind = ParticleSystemKind.Niagara
            }
        };
    }

    private IntermediateParticleSystem ReadNiagara(UObject obj, string path, string assetName)
    {
        var system = new IntermediateParticleSystem
        {
            SourcePath = path,
            AssetName = assetName,
            Kind = ParticleSystemKind.Niagara
        };

        var emittersProp = obj.GetOrDefault<object[]>("NiagaraEmitters");
        if (emittersProp == null) return system;

        foreach (var emitterRef in emittersProp)
        {
            var emitterObj = ReflectionHelper.GetMember(emitterRef, "Emitter");
            if (emitterObj == null) continue;

            var emitter = new ParticleEmitter
            {
                Name = ReflectionHelper.GetMember(emitterObj, "Name")?.ToString() ?? "Emitter"
            };

            var usageProps = ReflectionHelper.GetMember(emitterObj, "UsageProps");
            if (usageProps != null)
            {
                emitter.Capacity = ReflectionHelper.GetInt32(usageProps, "Capacity", 1000);
            }

            var scripts = ReflectionHelper.GetEnumerableMember(emitterObj, "NiagaraScripts");
            if (scripts != null)
            {
                foreach (var script in scripts)
                {
                    var usage = ReflectionHelper.GetMember(script, "Usage");
                    var usageEnumValue = usage != null ? ReflectionHelper.GetMember(usage, "Value") as byte? : null;
                    var moduleName = ReflectionHelper.GetMember(script, "Name")?.ToString() ?? "UnknownModule";
                    var modulePath = ReflectionHelper.GetMember(script, "FilePath")?.ToString() ?? string.Empty;
                    var flaxModuleName = ParticleModuleCatalog.NiagaraToFlax.TryGetValue(modulePath, out var mapped)
                        ? mapped
                        : moduleName;

                    var module = new ParticleModule
                    {
                        ModuleType = flaxModuleName,
                        SourceClassName = modulePath
                    };

                    ExtractScriptParameters(script, module);

                    switch (usageEnumValue)
                    {
                        case 0: emitter.SpawnModules.Add(module); break;
                        case 1: emitter.InitializeModules.Add(module); break;
                        case 2: emitter.UpdateModules.Add(module); break;
                        case 3: emitter.RenderModules.Add(module); break;
                        default: emitter.UpdateModules.Add(module); break;
                    }
                }
            }

            system.Emitters.Add(emitter);
        }

        return system;
    }

    private IntermediateParticleSystem ReadCascade(UObject obj, string path, string assetName)
    {
        var system = new IntermediateParticleSystem
        {
            SourcePath = path,
            AssetName = assetName,
            Kind = ParticleSystemKind.Cascade
        };

        var emitters = obj.GetOrDefault<object[]>("Emitters");
        if (emitters == null) return system;

        foreach (var emitterObj in emitters)
        {
            var emitter = new ParticleEmitter
            {
                Name = ReflectionHelper.GetMember(emitterObj, "EmitterName")?.ToString() ?? "CascadeEmitter"
            };

            var required = ReflectionHelper.GetMember(emitterObj, "RequiredModule");
            if (required != null)
            {
                emitter.Capacity = ReflectionHelper.GetInt32(required, "PeakActiveParticles", 1000);
            }

            var lods = ReflectionHelper.GetEnumerableMember(emitterObj, "LODLevels");
            if (lods != null)
            {
                foreach (var lod in lods)
                {
                    var modules = ReflectionHelper.GetEnumerableMember(lod, "Modules");
                    if (modules == null) continue;
                    foreach (var module in modules)
                    {
                        var moduleClass = module.GetType().Name;
                        var flaxType = ParticleModuleCatalog.CascadeToFlax.TryGetValue(moduleClass, out var m)
                            ? m
                            : moduleClass;

                        var pm = new ParticleModule
                        {
                            ModuleType = flaxType,
                            SourceClassName = moduleClass
                        };

                        if (flaxType == "SpawnRate") emitter.SpawnModules.Add(pm);
                        else if (flaxType == "SpriteRenderer" || flaxType == "MeshRenderer" || flaxType == "LightRenderer")
                            emitter.RenderModules.Add(pm);
                        else if (flaxType is "Lifetime" or "Position" or "Velocity" or "Size" or "Color")
                            emitter.InitializeModules.Add(pm);
                        else
                            emitter.UpdateModules.Add(pm);
                    }
                }
            }

            system.Emitters.Add(emitter);
        }

        return system;
    }

    private static void ExtractScriptParameters(object script, ParticleModule module)
    {
        var parameters = ReflectionHelper.GetEnumerableMember(script, "Parameters");
        if (parameters == null) return;
        foreach (var param in parameters)
        {
            var name = ReflectionHelper.GetMember(param, "Name")?.ToString() ?? "Param";
            var defaultValue = ReflectionHelper.GetMember(param, "DefaultValue");
            module.Properties[name] = defaultValue;
        }
    }
}