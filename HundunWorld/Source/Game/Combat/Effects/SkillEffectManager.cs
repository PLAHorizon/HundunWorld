using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game.Combat.Effects
{
    /// <summary>
    /// 技能特效管理器
    /// 管理技能施放、命中、持续等各阶段的视觉特效
    /// 包括粒子系统、光效、拖尾等
    /// </summary>
    public class SkillEffectManager : Script
    {
        // 单例实例
        private static SkillEffectManager _instance;
        public static SkillEffectManager Instance => _instance;
        /// <summary>
        /// 特效类型
        /// </summary>
        public enum EffectType
        {
            CastStart,          // 施法开始（蓄力特效）
            CastLoop,           // 施法循环（持续施法）
            CastRelease,        // 施法释放（瞬间爆发）
            Projectile,         // 弹道特效
            Hit,                // 命中特效
            AreaOfEffect,       // 范围特效（地面标记）
            Buff,               // 增益特效（光环）
            Debuff,             // 减益特效
            Trail,              // 拖尾特效
            Explosion           // 爆炸特效
        }

        /// <summary>
        /// 特效数据
        /// </summary>
        [Serializable]
        public class EffectData
        {
            [Tooltip("特效名称")]
            public string EffectName;

            [Tooltip("特效类型")]
            public EffectType Type;

            [Tooltip("特效预制体")]
            public Prefab EffectPrefab;

            [Tooltip("持续时间（秒，0表示手动销毁）")]
            public float Duration = 2.0f;

            [Tooltip("特效缩放")]
            public float Scale = 1.0f;

            [Tooltip("是否跟随目标")]
            public bool FollowTarget = false;

            [Tooltip("偏移位置")]
            public Vector3 PositionOffset = Vector3.Zero;

            [Tooltip("是否自动播放")]
            public bool AutoPlay = true;
        }

        /// <summary>
        /// 活跃的特效实例
        /// </summary>
        private class ActiveEffect
        {
            public Actor EffectActor;
            public EffectData Data;
            public float SpawnTime;
            public Actor TargetActor; // 跟随的目标
        }

        [Header("特效预制体配置")]
        [Tooltip("技能特效数据列表")]
        public List<EffectData> EffectDatabase = new List<EffectData>();

        [Header("性能设置")]
        [Tooltip("最大同时活跃特效数量")]
        public int MaxActiveEffects = 100;

        [Tooltip("是否启用特效池（复用特效对象）")]
        public bool UseEffectPooling = true;

        [Tooltip("特效池大小")]
        public int EffectPoolSize = 50;

        [Header("调试")]
        [Tooltip("显示调试信息")]
        public bool ShowDebug = false;

        // 活跃特效列表
        private List<ActiveEffect> activeEffects = new List<ActiveEffect>();

        // 特效对象池
        private Dictionary<string, Queue<Actor>> effectPool = new Dictionary<string, Queue<Actor>>();

        // 特效数据快速查找
        private Dictionary<string, EffectData> effectDataMap = new Dictionary<string, EffectData>();

        /// <summary>
        /// 初始化
        /// </summary>
        public override void OnEnable()
        {
            // 设置单例实例
            _instance = this;
            
            // 构建特效数据映射表
            effectDataMap.Clear();
            foreach (var effectData in EffectDatabase)
            {
                if (!effectDataMap.ContainsKey(effectData.EffectName))
                {
                    effectDataMap.Add(effectData.EffectName, effectData);
                }
            }

            // 初始化特效池
            if (UseEffectPooling)
            {
                InitializeEffectPool();
            }

            if (ShowDebug)
            {
                Debug.Log($"SkillEffectManager initialized with {EffectDatabase.Count} effect types");
            }
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public override void OnUpdate()
        {
            UpdateActiveEffects();

            if (ShowDebug)
            {
                DebugDraw.DrawText($"Active Effects: {activeEffects.Count}/{MaxActiveEffects}", 
                    new Vector3(100, 200, 0), Color.Cyan);
            }
        }

        /// <summary>
        /// 初始化特效池
        /// </summary>
        private void InitializeEffectPool()
        {
            effectPool.Clear();

            foreach (var effectData in EffectDatabase)
            {
                if (effectData.EffectPrefab == null)
                    continue;

                Queue<Actor> pool = new Queue<Actor>();
                
                // 预创建一些特效对象（按需调整数量）
                int preloadCount = Mathf.Min(5, EffectPoolSize);
                for (int i = 0; i < preloadCount; i++)
                {
                    Actor effectActor = CreateEffectActor(effectData);
                    if (effectActor != null)
                    {
                        effectActor.IsActive = false; // 初始隐藏
                        pool.Enqueue(effectActor);
                    }
                }

                effectPool[effectData.EffectName] = pool;
            }

            if (ShowDebug)
            {
                Debug.Log($"Effect pool initialized with {effectPool.Count} effect types");
            }
        }

        /// <summary>
        /// 播放技能特效
        /// </summary>
        /// <param name="effectName">特效名称</param>
        /// <param name="position">播放位置</param>
        /// <param name="rotation">旋转角度</param>
        /// <param name="targetActor">目标Actor（用于跟随特效）</param>
        /// <returns>特效Actor实例</returns>
        public Actor PlayEffect(string effectName, Vector3 position, Quaternion rotation = default, Actor targetActor = null)
        {
            if (!effectDataMap.ContainsKey(effectName))
            {
                if (ShowDebug)
                    Debug.LogWarning($"Effect not found: {effectName}");
                return null;
            }

            EffectData effectData = effectDataMap[effectName];

            // 检查活跃特效数量限制
            if (activeEffects.Count >= MaxActiveEffects)
            {
                // 移除最旧的特效
                RemoveOldestEffect();
            }

            // 从对象池获取或创建新特效
            Actor effectActor = GetEffectFromPool(effectData);

            if (effectActor == null)
            {
                if (ShowDebug)
                    Debug.LogWarning($"Failed to create effect: {effectName}");
                return null;
            }

            // 设置位置和旋转
            effectActor.Position = position + effectData.PositionOffset;
            effectActor.Orientation = rotation == default ? Quaternion.Identity : rotation;
            effectActor.Scale = new Vector3(effectData.Scale, effectData.Scale, effectData.Scale);
            effectActor.IsActive = true;

            // 如果有目标且需要跟随
            if (effectData.FollowTarget && targetActor != null)
            {
                effectActor.Parent = targetActor;
            }

            // 播放特效
            if (effectData.AutoPlay)
            {
                PlayEffectActor(effectActor);
            }

            // 添加到活跃列表
            ActiveEffect activeEffect = new ActiveEffect
            {
                EffectActor = effectActor,
                Data = effectData,
                SpawnTime = Time.GameTime,
                TargetActor = targetActor
            };
            activeEffects.Add(activeEffect);

            if (ShowDebug)
            {
                Debug.Log($"Effect played: {effectName} at {position}");
            }

            return effectActor;
        }

        /// <summary>
        /// 播放施法特效
        /// </summary>
        public Actor PlayCastEffect(string effectName, Actor caster)
        {
            return PlayEffect(effectName, caster.Position, caster.Orientation, caster);
        }

        /// <summary>
        /// 播放命中特效
        /// </summary>
        public Actor PlayHitEffect(string effectName, Vector3 hitPosition, Quaternion hitRotation = default)
        {
            return PlayEffect(effectName, hitPosition, hitRotation);
        }

        /// <summary>
        /// 播放弹道特效
        /// </summary>
        public Actor PlayProjectileEffect(string effectName, Vector3 startPosition, Vector3 direction)
        {
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.Up);
            return PlayEffect(effectName, startPosition, rotation);
        }

        /// <summary>
        /// 播放范围特效
        /// </summary>
        public Actor PlayAreaEffect(string effectName, Vector3 centerPosition, float radius = 5.0f)
        {
            Actor effectActor = PlayEffect(effectName, centerPosition);
            if (effectActor != null)
            {
                effectActor.Scale = new Vector3(radius, radius, radius);
            }
            return effectActor;
        }

        /// <summary>
        /// 停止特效
        /// </summary>
        public void StopEffect(Actor effectActor)
        {
            if (effectActor == null)
                return;

            var activeEffect = activeEffects.Find(e => e.EffectActor == effectActor);
            if (activeEffect != null)
            {
                ReturnEffectToPool(activeEffect);
                activeEffects.Remove(activeEffect);
            }
        }

        /// <summary>
        /// 停止指定名称的所有特效
        /// </summary>
        public void StopAllEffectsByName(string effectName)
        {
            var effectsToRemove = activeEffects.FindAll(e => e.Data.EffectName == effectName);
            foreach (var effect in effectsToRemove)
            {
                ReturnEffectToPool(effect);
                activeEffects.Remove(effect);
            }
        }

        /// <summary>
        /// 播放技能发射特效
        /// </summary>
        public void PlaySkillLaunchEffect(Vector3 startPosition, int skillId)
        {
            string effectName = $"Skill_Launch_{skillId}";
            PlayEffect(effectName, startPosition);
            
            if (ShowDebug)
            {
                Debug.Log($"Playing skill launch effect: {effectName} at {startPosition}");
            }
        }

        /// <summary>
        /// 播放范围技能特效
        /// </summary>
        public void PlayAreaSkillEffect(Vector3 targetPosition, int skillId, float range)
        {
            string effectName = $"Skill_Area_{skillId}";
            Actor effectActor = PlayEffect(effectName, targetPosition);
            if (effectActor != null)
            {
                effectActor.Scale = new Vector3(range, range, range);
            }
            
            if (ShowDebug)
            {
                Debug.Log($"Playing area skill effect: {effectName} at {targetPosition} with range {range}");
            }
        }

        /// <summary>
        /// 播放攻击特效
        /// </summary>
        public void PlayAttackEffect(Vector3 startPosition, Vector3 impactPosition, int skillId)
        {
            string effectName = $"Attack_{skillId}";
            PlayEffect(effectName, startPosition);
            
            if (ShowDebug)
            {
                Debug.Log($"Playing attack effect: {effectName} from {startPosition} to {impactPosition}");
            }
        }

        /// <summary>
        /// 播放受击特效
        /// </summary>
        public void PlayHitEffect(Vector3 impactPosition, int elementType)
        {
            string effectName = $"Hit_Element_{elementType}";
            PlayEffect(effectName, impactPosition);
            
            if (ShowDebug)
            {
                Debug.Log($"Playing hit effect: {effectName} at {impactPosition}");
            }
        }

        /// <summary>
        /// 播放死亡特效
        /// </summary>
        public void PlayDeathEffect(Vector3 position)
        {
            string effectName = "Death_Effect";
            PlayEffect(effectName, position);
            
            if (ShowDebug)
            {
                Debug.Log($"Playing death effect at {position}");
            }
        }

        /// <summary>
        /// 播放复活特效
        /// </summary>
        public void PlayResurrectEffect(Vector3 position)
        {
            string effectName = "Resurrect_Effect";
            PlayEffect(effectName, position);
            
            if (ShowDebug)
            {
                Debug.Log($"Playing resurrect effect at {position}");
            }
        }

        /// <summary>
        /// 播放效果视觉特效
        /// </summary>
        public void PlayEffectVisual(Vector3 position, int effectId, EffectType effectType)
        {
            string effectName = $"Effect_{effectId}_{effectType}";
            PlayEffect(effectName, position);
            
            if (ShowDebug)
            {
                Debug.Log($"Playing visual effect: {effectName} at {position}");
            }
        }

        /// <summary>
        /// 停止所有特效
        /// </summary>
        public void StopAllEffects()
        {
            foreach (var effect in activeEffects)
            {
                ReturnEffectToPool(effect);
            }
            activeEffects.Clear();
        }

        /// <summary>
        /// 更新活跃特效
        /// </summary>
        private void UpdateActiveEffects()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                ActiveEffect effect = activeEffects[i];

                // 检查特效是否需要销毁
                if (effect.Data.Duration > 0)
                {
                    float elapsed = Time.GameTime - effect.SpawnTime;
                    if (elapsed >= effect.Data.Duration)
                    {
                        ReturnEffectToPool(effect);
                        activeEffects.RemoveAt(i);
                    }
                }

                // 检查跟随目标是否有效
                if (effect.Data.FollowTarget && effect.TargetActor != null)
                {
                    if (effect.TargetActor == null)
                    {
                        ReturnEffectToPool(effect);
                        activeEffects.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// 从对象池获取特效
        /// </summary>
        private Actor GetEffectFromPool(EffectData effectData)
        {
            if (UseEffectPooling && effectPool.ContainsKey(effectData.EffectName))
            {
                Queue<Actor> pool = effectPool[effectData.EffectName];
                
                // 尝试从池中获取
                while (pool.Count > 0)
                {
                    Actor pooledActor = pool.Dequeue();
                    if (pooledActor != null && pooledActor.IsActiveInHierarchy)
                    {
                        return pooledActor;
                    }
                }
            }

            // 池中没有可用对象，创建新对象
            return CreateEffectActor(effectData);
        }

        /// <summary>
        /// 创建特效Actor
        /// </summary>
        private Actor CreateEffectActor(EffectData effectData)
        {
            if (effectData.EffectPrefab == null)
                return null;

            // 使用Flax Engine的预制体实例化
            Actor effectActor = PrefabManager.SpawnPrefab(effectData.EffectPrefab, null);
            
            if (effectActor != null)
            {
                effectActor.Name = $"Effect_{effectData.EffectName}";
                effectActor.StaticFlags = StaticFlags.None; // 确保是动态对象
                return effectActor;
            }
            
            // 如果预制体加载失败，创建空占位符
            effectActor = new EmptyActor();
            effectActor.Name = $"Effect_{effectData.EffectName}";
            Level.SpawnActor(effectActor);

            return effectActor;
        }

        /// <summary>
        /// 播放特效Actor
        /// </summary>
        private void PlayEffectActor(Actor effectActor)
        {
            // 触发粒子系统播放
            // 查找所有ParticleEffect组件并播放
            var particleSystems = effectActor.GetScripts<ParticleEffect>();
            foreach (var ps in particleSystems)
            {
                if (ps != null)
                {
                    ps.Play();
                    Debug.Log($"Playing particle system on {effectActor.Name}");
                }
            }
            
            // 也可以查找子对象中的粒子系统
            for (int i = 0; i < effectActor.ChildrenCount; i++)
            {
                var child = effectActor.GetChild(i);
                if (child is ParticleEffect particleEffect)
                {
                    particleEffect.Play();
                    Debug.Log($"Playing particle effect: {particleEffect.Name}");
                }
            }
        }

        /// <summary>
        /// 停止特效Actor
        /// </summary>
        private void StopEffectActor(Actor effectActor)
        {
            // 停止粒子系统播放
            var particleSystems = effectActor.GetScripts<ParticleEffect>();
            foreach (var ps in particleSystems)
            {
                if (ps != null)
                {
                    ps.Stop();
                    Debug.Log($"Stopping particle system on {effectActor.Name}");
                }
            }
            
            // 停止子对象中的粒子系统
            for (int i = 0; i < effectActor.ChildrenCount; i++)
            {
                var child = effectActor.GetChild(i);
                if (child is ParticleEffect particleEffect)
                {
                    particleEffect.Stop();
                    Debug.Log($"Stopped particle effect: {particleEffect.Name}");
                }
            }
        }

        /// <summary>
        /// 将特效归还到对象池
        /// </summary>
        private void ReturnEffectToPool(ActiveEffect effect)
        {
            if (effect.EffectActor == null || !effect.EffectActor.IsActiveInHierarchy)
                return;

            StopEffectActor(effect.EffectActor);

            if (UseEffectPooling)
            {
                effect.EffectActor.IsActive = false;
                effect.EffectActor.Parent = null; // 解除父子关系

                if (effectPool.ContainsKey(effect.Data.EffectName))
                {
                    Queue<Actor> pool = effectPool[effect.Data.EffectName];
                    if (pool.Count < EffectPoolSize)
                    {
                        pool.Enqueue(effect.EffectActor);
                    }
                    else
                    {
                        // 池已满，销毁对象
                        Destroy(effect.EffectActor);
                    }
                }
            }
            else
            {
                // 不使用对象池，直接销毁
                Destroy(effect.EffectActor);
            }
        }

        /// <summary>
        /// 移除最旧的特效
        /// </summary>
        private void RemoveOldestEffect()
        {
            if (activeEffects.Count == 0)
                return;

            ActiveEffect oldest = activeEffects[0];
            ReturnEffectToPool(oldest);
            activeEffects.RemoveAt(0);
        }

        /// <summary>
        /// 清理对象池
        /// </summary>
        public override void OnDisable()
        {
            // 清除单例实例
            if (_instance == this)
            {
                _instance = null;
            }
            
            // 停止所有活跃特效
            StopAllEffects();

            // 销毁对象池中的所有对象
            foreach (var pool in effectPool.Values)
            {
                while (pool.Count > 0)
                {
                    Actor actor = pool.Dequeue();
                    if (actor != null && actor.IsActiveInHierarchy)
                    {
                        Destroy(actor);
                    }
                }
            }
            effectPool.Clear();
        }
    }
}
