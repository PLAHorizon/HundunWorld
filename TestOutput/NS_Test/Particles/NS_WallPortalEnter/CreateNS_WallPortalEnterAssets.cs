// ============================================================
// NS_WallPortalEnter 粒子资源创建脚本
// 在 Flax Editor 中通过 Tools > Scripts 执行 CreateAll()
// ============================================================

using System;
using FlaxEngine;
using FlaxEditor;

public class CreateNS_WallPortalEnterAssets
{
    /// <summary>主入口：创建 ParticleEmitter + ParticleSystem + 更新 Prefab 引用。</summary>
    public static void CreateAll()
    {
        var emitterPath = @"Particles/NS_WallPortalEnter/NS_WallPortalEnter_Emitter.flax";
        var systemPath = @"Particles/NS_WallPortalEnter/NS_WallPortalEnter_System.flax";
        var projectRoot = Editor.ContentProjectFolder.FullPath;

        // === 1. 创建 ParticleEmitter ===
        var emitter = ParticleEmitter.CreateDefault();
        // 添加模块
        emitter.AddModule<ParticleEmitter.SpawnModule>();
        emitter.AddModule<ParticleEmitter.LifeModule>();
        emitter.AddModule<ParticleEmitter.PositionModule>();
        emitter.AddModule<ParticleEmitter.VelocityModule>();
        emitter.AddModule<ParticleEmitter.SizeModule>();
        emitter.AddModule<ParticleEmitter.ColorModule>();

        // 保存 Emitter
        var emitterFullPath = System.IO.Path.Combine(projectRoot, emitterPath);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(emitterFullPath));
        emitter.Save(emitterFullPath);
        Editor.Log($"已创建 ParticleEmitter: {emitterFullPath}");

        // === 2. 创建 ParticleSystem ===
        var system = new ParticleSystem();
        system.Emitters = new[]
        {
            new ParticleSystem.Emitter
            {
                Emitter = FlaxEngine.Content.Load<ParticleEmitter>(emitterPath),
                Duration = 5.0f,
                StartTime = 0.0f,
                SpawnMode = ParticleSystemSpawnMode.Loop,
            }
        };

        var systemFullPath = System.IO.Path.Combine(projectRoot, systemPath);
        system.Save(systemFullPath);
        Editor.Log($"已创建 ParticleSystem: {systemFullPath}");

        Editor.Log("资源创建完成。Prefab 中的 ParticleEffect.Actor 引用需手动指定或重新加载。");
    }
}

