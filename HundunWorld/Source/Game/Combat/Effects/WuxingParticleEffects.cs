using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.Utilities;
using Game.Character.Attributes;

namespace Game.Combat.Effects
{
    /// <summary>
    /// 五行粒子特效系统 - 管理所有五行相关的视觉特效
    /// </summary>
    public class WuxingParticleEffects : Script
    {
        // 五行粒子系统预制体
        [Header("五行粒子特效预制体")]
        [Tooltip("金系粒子特效预制体")]
        public Prefab MetalParticlePrefab;
        
        [Tooltip("木系粒子特效预制体")]
        public Prefab WoodParticlePrefab;
        
        [Tooltip("水系粒子特效预制体")]
        public Prefab WaterParticlePrefab;
        
        [Tooltip("火系粒子特效预制体")]
        public Prefab FireParticlePrefab;
        
        [Tooltip("土系粒子特效预制体")]
        public Prefab EarthParticlePrefab;

        // 粒子系统缓存池
        private Dictionary<string, List<Actor>> particlePool = new Dictionary<string, List<Actor>>();
        private const int POOL_SIZE = 10; // 每种特效的缓存数量

        public override void OnStart()
        {
            InitializeParticlePool();
        }

        /// <summary>
        /// 初始化粒子特效缓存池
        /// </summary>
        private void InitializeParticlePool()
        {
            CreateParticlePool("Metal", MetalParticlePrefab);
            CreateParticlePool("Wood", WoodParticlePrefab);
            CreateParticlePool("Water", WaterParticlePrefab);
            CreateParticlePool("Fire", FireParticlePrefab);
            CreateParticlePool("Earth", EarthParticlePrefab);
        }

        /// <summary>
        /// 创建特定类型的粒子缓存池
        /// </summary>
        private void CreateParticlePool(string elementType, Prefab prefab)
        {
            if (prefab == null) return;

            var pool = new List<Actor>();
            for (int i = 0; i < POOL_SIZE; i++)
            {
                var particleInstance = PrefabManager.SpawnPrefab(prefab);
                particleInstance.IsActive = false;
                AttachToScene(particleInstance);
                pool.Add(particleInstance);
            }
            particlePool[elementType] = pool;
        }

        /// <summary>
        /// 播放五行粒子特效
        /// </summary>
        /// <param name="elementType">五行元素类型</param>
        /// <param name="position">特效位置</param>
        /// <param name="duration">持续时间</param>
        /// <param name="scale">缩放比例</param>
        public void PlayWuxingParticle(WuxingElement elementType, Vector3 position, float duration = 2.0f, float scale = 1.0f)
        {
            string elementTypeStr = elementType.ToString();
            Actor particleActor = GetAvailableParticle(elementTypeStr);
            
            if (particleActor != null)
            {
                // 设置位置和缩放
                particleActor.Position = position;
                particleActor.Scale = new Vector3(scale, scale, scale);
                
                // 激活粒子系统
                // 注意：在FlaxEngine中，ParticleSystem通常作为Actor的子组件自动播放
                particleActor.IsActive = true;
                
                // 设置自动回收
                ScheduleParticleRecycle(particleActor, duration);
            }
        }

        /// <summary>
        /// 获取可用的粒子系统实例
        /// </summary>
        private Actor GetAvailableParticle(string elementType)
        {
            if (!particlePool.ContainsKey(elementType)) return null;

            var pool = particlePool[elementType];
            foreach (var particle in pool)
            {
                if (!particle.IsActiveInHierarchy)
                {
                    return particle;
                }
            }

            // 如果缓存池满了，创建新的实例
            if (elementType == "Metal" && MetalParticlePrefab != null)
            {
                Actor newInstance = PrefabManager.SpawnPrefab(MetalParticlePrefab);
                AttachToScene(newInstance);
                pool.Add(newInstance);
                return newInstance;
            }
            else if (elementType == "Wood" && WoodParticlePrefab != null)
            {
                Actor newInstance = PrefabManager.SpawnPrefab(WoodParticlePrefab);
                AttachToScene(newInstance);
                pool.Add(newInstance);
                return newInstance;
            }
            else if (elementType == "Water" && WaterParticlePrefab != null)
            {
                Actor newInstance = PrefabManager.SpawnPrefab(WaterParticlePrefab);
                AttachToScene(newInstance);
                pool.Add(newInstance);
                return newInstance;
            }
            else if (elementType == "Fire" && FireParticlePrefab != null)
            {
                Actor newInstance = PrefabManager.SpawnPrefab(FireParticlePrefab);
                AttachToScene(newInstance);
                pool.Add(newInstance);
                return newInstance;
            }
            else if (elementType == "Earth" && EarthParticlePrefab != null)
            {
                Actor newInstance = PrefabManager.SpawnPrefab(EarthParticlePrefab);
                AttachToScene(newInstance);
                pool.Add(newInstance);
                return newInstance;
            }

            return null;
        }

        /// <summary>
        /// 安排粒子系统回收
        /// </summary>
        private void ScheduleParticleRecycle(Actor particleActor, float delay)
        {
            // 使用协程或定时器来回收粒子系统
            // 这里简化处理，实际应该使用更精确的定时机制
            // 在FlaxEngine中，我们暂时不实现延迟回收，直接激活粒子系统
            particleActor.IsActive = true;
            // 注意：实际项目中可能需要实现更精确的定时机制来回收粒子系统
        }

        /// <summary>
        /// 将粒子系统附加到场景中
        /// </summary>
        private void AttachToScene(Actor particleActor)
        {
            // 将粒子系统附加到场景根节点或特效管理器节点
            if (Level.Scenes.Length > 0)
            {
                particleActor.SetParent(Level.Scenes[0], false);
            }
        }

        /// <summary>
        /// 播放特定技能的粒子特效组合
        /// </summary>
        public void PlaySkillEffect(WuxingElement elementType, string effectType, Vector3 position)
        {
            switch (effectType)
            {
                case "Cast":
                    PlayCastEffect(elementType, position);
                    break;
                case "Impact":
                    PlayImpactEffect(elementType, position);
                    break;
                case "Trail":
                    PlayTrailEffect(elementType, position);
                    break;
                case "Aura":
                    PlayAuraEffect(elementType, position);
                    break;
            }
        }

        /// <summary>
        /// 播放施法特效
        /// </summary>
        private void PlayCastEffect(WuxingElement elementType, Vector3 position)
        {
            float scale = 1.5f;
            float duration = 1.0f;
            
            switch (elementType)
            {
                case WuxingElement.Metal:
                    // 金系施法特效：金属光泽汇聚
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Wood:
                    // 木系施法特效：绿色能量汇聚
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Water:
                    // 水系施法特效：蓝色水珠汇聚
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Fire:
                    // 火系施法特效：红色火焰汇聚
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Earth:
                    // 土系施法特效：棕色土石汇聚
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
            }
        }

        /// <summary>
        /// 播放命中特效
        /// </summary>
        private void PlayImpactEffect(WuxingElement elementType, Vector3 position)
        {
            float scale = 2.0f;
            float duration = 1.5f;
            
            switch (elementType)
            {
                case WuxingElement.Metal:
                    // 金系命中特效：金属碎片飞溅
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Wood:
                    // 木系命中特效：叶片飞散
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Water:
                    // 水系命中特效：水花四溅
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Fire:
                    // 火系命中特效：火焰爆炸
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Earth:
                    // 土系命中特效：岩石碎屑飞散
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
            }
        }

        /// <summary>
        /// 播放轨迹特效
        /// </summary>
        private void PlayTrailEffect(WuxingElement elementType, Vector3 position)
        {
            float scale = 1.0f;
            float duration = 0.5f;
            
            switch (elementType)
            {
                case WuxingElement.Metal:
                    // 金系轨迹特效：金属光泽拖尾
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Wood:
                    // 木系轨迹特效：绿色能量拖尾
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Water:
                    // 水系轨迹特效：蓝色水珠拖尾
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Fire:
                    // 火系轨迹特效：红色火焰拖尾
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Earth:
                    // 土系轨迹特效：棕色尘土拖尾
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
            }
        }

        /// <summary>
        /// 播放光环特效
        /// </summary>
        private void PlayAuraEffect(WuxingElement elementType, Vector3 position)
        {
            float scale = 3.0f;
            float duration = 3.0f;
            
            switch (elementType)
            {
                case WuxingElement.Metal:
                    // 金系光环特效：金色能量场
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Wood:
                    // 木系光环特效：绿色生命场
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Water:
                    // 水系光环特效：蓝色水流场
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Fire:
                    // 火系光环特效：红色火焰场
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
                case WuxingElement.Earth:
                    // 土系光环特效：棕色大地场
                    PlayWuxingParticle(elementType, position, duration, scale);
                    break;
            }
        }

        /// <summary>
        /// 清理所有缓存的粒子系统
        /// </summary>
        public void Cleanup()
        {
            foreach (var pool in particlePool.Values)
            {
                foreach (var particle in pool)
                {
                    if (particle != null)
                    {
                        FlaxEngine.Object.Destroy(particle);
                    }
                }
                pool.Clear();
            }
            particlePool.Clear();
        }

        public override void OnDisable()
        {
            Cleanup();
        }
    }
}
