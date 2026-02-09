using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Performance
{
    /// <summary>
    /// 渲染优化器
    /// 管理LOD切换、材质合批、遮挡剔除和粒子系统预算
    /// </summary>
    public class RenderingOptimizer : Script
    {
        #region LOD配置

        [Header("LOD设置")]
        [Tooltip("LOD等级数量")]
        public int LODLevelCount = 4;

        [Tooltip("LOD切换距离")]
        public float[] LODDistances = { 30f, 80f, 150f, 300f };

        [Tooltip("当前强制LOD等级（-1表示自动）")]
        public int ForcedLODLevel = -1;

        #endregion

        #region 遮挡剔除配置

        [Header("遮挡剔除")]
        [Tooltip("是否启用遮挡剔除")]
        public bool EnableOcclusionCulling = true;

        [Tooltip("最大可见距离")]
        public float MaxViewDistance = 500f;

        [Tooltip("遮挡剔除检测间隔（秒）")]
        public float CullingCheckInterval = 0.1f;

        #endregion

        #region 材质合批配置

        [Header("材质合批")]
        [Tooltip("是否启用材质合批")]
        public bool EnableMaterialBatching = true;

        [Tooltip("合批最大顶点数")]
        public int MaxBatchVertices = 65535;

        #endregion

        #region 粒子预算配置

        [Header("粒子预算")]
        [Tooltip("最大同时粒子数")]
        public int MaxParticleCount = 10000;

        [Tooltip("最大粒子发射器数")]
        public int MaxEmitterCount = 50;

        [Tooltip("粒子质量等级（0-3）")]
        public int ParticleQualityLevel = 2;

        [Tooltip("是否启用GPU粒子")]
        public bool EnableGPUParticles = true;

        [Tooltip("粒子可见距离")]
        public float ParticleViewDistance = 200f;

        #endregion

        #region 调试

        [Header("调试")]
        [Tooltip("显示调试信息")]
        public bool ShowDebug = false;

        #endregion

        // 统计数据
        private int _currentActiveEmitters;
        private int _culledObjectCount;
        private float _lastCullingCheckTime;

        // 被管理的粒子发射器
        private readonly List<ParticleEmitterEntry> _managedEmitters = new List<ParticleEmitterEntry>();

        /// <summary>
        /// 粒子发射器条目
        /// </summary>
        private class ParticleEmitterEntry
        {
            public ParticleEffect Effect;
            public float SpawnDistance;
            public bool IsActive;
            public int Priority;
        }

        /// <summary>
        /// 渲染统计信息
        /// </summary>
        public struct RenderingStats
        {
            public int ActiveEmitters;
            public int CulledObjects;
            public int CurrentLODLevel;
        }

        public override void OnStart()
        {
            Debug.Log("渲染优化器已初始化");
        }

        public override void OnUpdate()
        {
            // 定期进行遮挡剔除检查
            if (EnableOcclusionCulling && Time.GameTime - _lastCullingCheckTime >= CullingCheckInterval)
            {
                PerformOcclusionCulling();
                _lastCullingCheckTime = Time.GameTime;
            }

            // 更新粒子预算
            UpdateParticleBudget();

            if (ShowDebug)
            {
                DrawDebugInfo();
            }
        }

        #region LOD管理

        /// <summary>
        /// 根据距离计算LOD等级
        /// </summary>
        /// <param name="distance">到相机的距离</param>
        /// <returns>LOD等级</returns>
        public int CalculateLODLevel(float distance)
        {
            if (ForcedLODLevel >= 0)
                return Mathf.Clamp(ForcedLODLevel, 0, LODLevelCount - 1);

            for (int i = 0; i < LODDistances.Length; i++)
            {
                if (distance <= LODDistances[i])
                    return i;
            }

            return LODLevelCount - 1;
        }

        /// <summary>
        /// 更新LOD距离配置
        /// </summary>
        public void UpdateLODDistances(float[] distances)
        {
            if (distances != null && distances.Length > 0)
            {
                LODDistances = distances;
                LODLevelCount = distances.Length;
            }
        }

        /// <summary>
        /// 设置强制LOD等级
        /// </summary>
        public void SetForcedLOD(int level)
        {
            ForcedLODLevel = level;
        }

        /// <summary>
        /// 清除强制LOD
        /// </summary>
        public void ClearForcedLOD()
        {
            ForcedLODLevel = -1;
        }

        #endregion

        #region 遮挡剔除

        /// <summary>
        /// 执行遮挡剔除
        /// </summary>
        private void PerformOcclusionCulling()
        {
            if (!EnableOcclusionCulling)
                return;

            var camera = Camera.MainCamera;
            if (camera == null)
                return;

            _culledObjectCount = 0;

            // 遮挡剔除通过Flax Engine的内置系统处理
            // 这里主要管理超距离的粒子和特效的显隐
            foreach (var entry in _managedEmitters)
            {
                if (entry.Effect == null)
                    continue;

                float distance = Vector3.Distance(camera.Position, entry.Effect.Position);
                bool shouldBeActive = distance <= ParticleViewDistance;

                if (entry.IsActive != shouldBeActive)
                {
                    entry.IsActive = shouldBeActive;
                    entry.Effect.IsActive = shouldBeActive;

                    if (!shouldBeActive)
                        _culledObjectCount++;
                }
            }
        }

        /// <summary>
        /// 检查对象是否在可见距离内
        /// </summary>
        public bool IsWithinViewDistance(Vector3 position)
        {
            var camera = Camera.MainCamera;
            if (camera == null)
                return true;

            float distanceSq = Vector3.DistanceSquared(camera.Position, position);
            return distanceSq <= MaxViewDistance * MaxViewDistance;
        }

        #endregion

        #region 粒子预算管理

        /// <summary>
        /// 注册粒子发射器
        /// </summary>
        public void RegisterParticleEmitter(ParticleEffect effect, int priority = 0)
        {
            if (effect == null)
                return;

            _managedEmitters.Add(new ParticleEmitterEntry
            {
                Effect = effect,
                IsActive = true,
                Priority = priority
            });
        }

        /// <summary>
        /// 注销粒子发射器
        /// </summary>
        public void UnregisterParticleEmitter(ParticleEffect effect)
        {
            _managedEmitters.RemoveAll(e => e.Effect == effect);
        }

        /// <summary>
        /// 更新粒子预算
        /// </summary>
        private void UpdateParticleBudget()
        {
            _currentActiveEmitters = 0;

            // 清理无效条目
            _managedEmitters.RemoveAll(e => e.Effect == null);

            // 计算当前活跃发射器数
            foreach (var entry in _managedEmitters)
            {
                if (entry.IsActive)
                    _currentActiveEmitters++;
            }

            // 如果超过预算，按优先级禁用低优先级发射器
            if (_currentActiveEmitters > MaxEmitterCount)
            {
                EnforceEmitterBudget();
            }
        }

        /// <summary>
        /// 强制执行发射器预算
        /// </summary>
        private void EnforceEmitterBudget()
        {
            // 按优先级排序（低优先级在前）
            _managedEmitters.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            int activeCount = 0;
            foreach (var entry in _managedEmitters)
            {
                if (activeCount >= MaxEmitterCount)
                {
                    if (entry.IsActive)
                    {
                        entry.IsActive = false;
                        if (entry.Effect != null)
                            entry.Effect.IsActive = false;
                    }
                }
                else if (entry.IsActive)
                {
                    activeCount++;
                }
            }
        }

        /// <summary>
        /// 设置粒子质量等级
        /// </summary>
        public void SetParticleQuality(int level)
        {
            ParticleQualityLevel = Mathf.Clamp(level, 0, 3);

            // 根据质量等级调整参数
            switch (ParticleQualityLevel)
            {
                case 0: // 最低
                    MaxParticleCount = 2000;
                    MaxEmitterCount = 10;
                    ParticleViewDistance = 50f;
                    break;
                case 1: // 低
                    MaxParticleCount = 5000;
                    MaxEmitterCount = 25;
                    ParticleViewDistance = 100f;
                    break;
                case 2: // 中
                    MaxParticleCount = 10000;
                    MaxEmitterCount = 50;
                    ParticleViewDistance = 200f;
                    break;
                case 3: // 高
                    MaxParticleCount = 20000;
                    MaxEmitterCount = 100;
                    ParticleViewDistance = 400f;
                    break;
            }
        }

        #endregion

        #region 统计和调试

        /// <summary>
        /// 获取渲染统计信息
        /// </summary>
        public RenderingStats GetStats()
        {
            return new RenderingStats
            {
                ActiveEmitters = _currentActiveEmitters,
                CulledObjects = _culledObjectCount,
                CurrentLODLevel = ForcedLODLevel >= 0 ? ForcedLODLevel : -1
            };
        }

        /// <summary>
        /// 绘制调试信息
        /// </summary>
        private void DrawDebugInfo()
        {
            var stats = GetStats();
            DebugDraw.DrawText(
                $"[Rendering] Emitters: {stats.ActiveEmitters}/{MaxEmitterCount}, " +
                $"Culled: {stats.CulledObjects}, LOD: {(ForcedLODLevel >= 0 ? ForcedLODLevel.ToString() : "Auto")}, " +
                $"Particle Quality: {ParticleQualityLevel}",
                new Vector3(100, 210, 0), Color.Green);
        }

        #endregion

        #region 公共配置API

        /// <summary>
        /// 应用LOD配置消息
        /// </summary>
        public void ApplyLODConfig(int lodLevelCount, List<float> lodDistances, bool occlusionCulling, float maxViewDist, bool materialBatching)
        {
            LODLevelCount = lodLevelCount;
            if (lodDistances != null && lodDistances.Count > 0)
            {
                LODDistances = lodDistances.ToArray();
            }
            EnableOcclusionCulling = occlusionCulling;
            MaxViewDistance = maxViewDist;
            EnableMaterialBatching = materialBatching;
        }

        /// <summary>
        /// 应用粒子预算配置
        /// </summary>
        public void ApplyParticleBudget(int maxParticles, int maxEmitters, int quality, bool gpuParticles, float viewDistance)
        {
            MaxParticleCount = maxParticles;
            MaxEmitterCount = maxEmitters;
            ParticleQualityLevel = quality;
            EnableGPUParticles = gpuParticles;
            ParticleViewDistance = viewDistance;
        }

        #endregion
    }
}
