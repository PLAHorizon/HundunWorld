// ============================================================
// UE5 → Flax 资源自动化导入脚本
// 在 Flax Editor 中通过 Tools > Scripts 执行此脚本
// ============================================================

using System;
using System.IO;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.Content.Import;

public class UE5AssetImporter
{
    /// <summary>主入口：用户在 Editor Scripts 窗口调用此方法。</summary>
    public static void ImportAll()
    {
        var projectContentFolder = Editor.ContentProjectFolder;
        var projectRoot = projectContentFolder.FullPath;
        var importRoot = Path.Combine(projectRoot, "Imported");
        Editor.Log("[UE5Importer] 开始导入 UE5 资源到 " + importRoot);

        if (!Directory.Exists(importRoot))
            Directory.CreateDirectory(importRoot);

        int imported = 0, failed = 0;

        Editor.Log($"[UE5Importer] 完成：成功 {imported} 个，失败 {failed} 个");
    }

    /// <summary>创建 Material 资源并设置 PBR 参数。</summary>
    public static Material CreateMaterial(string assetPath, string albedoColor, double roughness, double metallic)
    {
        var surface = Material.NewSurface();
        surface.Info.Domain = MaterialDomain.Surface;
        surface.Info.ShadingModel = MaterialShadingModel.Lit;

        // TODO: 通过 surface.Nodes API 添加 Color/Scalar/Normal 节点并连接到 Master 节点
        // 这部分需要根据 UE5 材质表达式映射到 Flax 材质节点图，比较复杂

        surface.Save(assetPath);
        Editor.Log($"已创建材质: {assetPath}");
        return FlaxEngine.Content.Load<Material>(assetPath);
    }

    /// <summary>创建 ParticleEmitter 资源（Niagara/Cascade → Flax）。</summary>
    public static ParticleEmitter CreateParticleEmitter(string assetPath, Dictionary<string, object> modules)
    {
        // Flax ParticleEmitter 通过 Editor API 创建
        var emitter = ParticleEmitter.CreateDefault();
        // TODO: 根据 modules 字典添加 Spark/Burst/Velocity/Lifetime 等模块
        emitter.Save(assetPath);
        Editor.Log($"已创建粒子发射器: {assetPath}");
        return emitter;
    }
}

