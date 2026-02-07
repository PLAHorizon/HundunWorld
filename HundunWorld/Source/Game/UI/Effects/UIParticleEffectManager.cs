using FlaxEngine;
using HundunWorld.Game.UI.Effects;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace Game.UI.Effects
{
    /// <summary>
    /// UI粒子效果管理器
    /// 负责在UI界面中集成和管理各种粒子效果
    /// </summary>
    public static class UIParticleEffectManager
    {
        private static Dictionary<string, Actor> _activeEffects = new Dictionary<string, Actor>();
        private static Scene _currentScene;  // 修复: 这里应该是FlaxEngine.Scene类型
        
        /// <summary>
        /// 初始化粒子效果管理器
        /// </summary>
        public static void Initialize()
        {
            _currentScene = Level.GetScene(0);
            FlaxEngine.Debug.Log("UI粒子效果管理器已初始化");
        }
        
        /// <summary>
        /// 为对话框创建星空粒子效果
        /// </summary>
        /// <param name="dialogId">对话框唯一标识</param>
        /// <param name="dialogSize">对话框尺寸</param>
        /// <param name="dialogPosition">对话框世界坐标位置</param>
        /// <returns>粒子效果Actor</returns>
        public static Actor CreateDialogStarEffect(string dialogId, Float2 dialogSize, Float3 dialogPosition)
        {
            try
            {
                // 清理已存在的效果
                DestroyEffect(dialogId);
                
                // 创建粒子效果Actor
                var effectActor = new EmptyActor();
                effectActor.Name = $"StarEffect_{dialogId}";
                effectActor.Transform =new Transform(new Vector3(dialogPosition.X, dialogPosition.Y, dialogPosition.Z)) ;
                
                // 添加星空粒子系统脚本
                var starSystem = effectActor.AddScript<StarParticleSystem>();
                
                // 配置粒子系统参数
                ConfigureDialogStarSystem(starSystem, dialogSize);

                // 将Actor添加到场景
                Level.SpawnActor(effectActor,_currentScene);

                // 记录活跃效果
                _activeEffects[dialogId] = effectActor;
                
                FlaxEngine.Debug.Log($"为对话框 {dialogId} 创建星空粒子效果成功");
                return effectActor;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"创建对话框星空效果失败: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 配置对话框星空粒子系统
        /// </summary>
        private static void ConfigureDialogStarSystem(StarParticleSystem starSystem, Float2 dialogSize)
        {
            if (starSystem == null) return;
            
            // 设置粒子数量（基于对话框大小）
            float area = dialogSize.X * dialogSize.Y;
            starSystem.ParticleCount = Math.Max(20, Math.Min(80, (int)(area / 10000f * 50)));
            
            // 设置发射区域（略大于对话框）
            starSystem.EmissionArea = new Float2(dialogSize.X * 1.1f, dialogSize.Y * 1.1f);
            
            // 设置粒子大小
            starSystem.MinSize = 1.0f;
            starSystem.MaxSize = 2.5f;
            
            // 设置闪烁速度
            starSystem.TwinkleSpeed = 1.5f;
            
            // 设置中国古典主题颜色
            starSystem.PrimaryColor = new Color(
                ChineseClassicalTheme.SecondaryColor.R,
                ChineseClassicalTheme.SecondaryColor.G,
                ChineseClassicalTheme.SecondaryColor.B,
                0.8f
            );
            
            starSystem.SecondaryColor = new Color(
                ChineseClassicalTheme.TextColor.R,
                ChineseClassicalTheme.TextColor.G,
                ChineseClassicalTheme.TextColor.B,
                0.6f
            );
            
            // 设置透明度范围
            starSystem.MinAlpha = 0.2f;
            starSystem.MaxAlpha = 0.9f;
            
            FlaxEngine.Debug.Log($"星空粒子系统配置完成，粒子数量: {starSystem.ParticleCount}");
        }
        
        /// <summary>
        /// 创建简化的UI星空效果（用于性能较低的设备）
        /// </summary>
        public static Actor CreateSimpleStarEffect(string effectId, Float2 area, Float3 position)
        {
            try
            {
                DestroyEffect(effectId);
                
                var effectActor = new EmptyActor();
                effectActor.Name = $"SimpleStarEffect_{effectId}";
                effectActor.Transform = new Transform(new Vector3(position.X, position.Y, position.Z));

                var starSystem = effectActor.AddScript<StarParticleSystem>();
                
                // 简化配置
                starSystem.ParticleCount = 15;
                starSystem.EmissionArea = area;
                starSystem.MinSize = 1.0f;
                starSystem.MaxSize = 2.0f;
                starSystem.TwinkleSpeed = 1.0f;
                
                // 使用更简单的颜色
                starSystem.PrimaryColor = Color.White * 0.7f;
                starSystem.SecondaryColor = new Color(0.8f, 0.9f, 1.0f, 0.5f);
                starSystem.MinAlpha = 0.3f;
                starSystem.MaxAlpha = 0.7f;

                // 将Actor添加到场景
                Level.SpawnActor(effectActor, _currentScene);

                _activeEffects[effectId] = effectActor;
                
                FlaxEngine.Debug.Log($"简化星空效果 {effectId} 创建成功");
                return effectActor;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"创建简化星空效果失败: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 创建漂浮粒子效果（用于登录界面等）
        /// </summary>
        public static Actor CreateFloatingParticleEffect(string effectId, Float2 area, Float3 position, Color primaryColor)
        {
            try
            {
                DestroyEffect(effectId);
                
                var effectActor = new EmptyActor();
                effectActor.Name = $"FloatingEffect_{effectId}";
                effectActor.Transform = new Transform(new Vector3(position.X, position.Y, position.Z));

                var starSystem = effectActor.AddScript<StarParticleSystem>();
                
                // 配置漂浮效果
                starSystem.ParticleCount = 30;
                starSystem.EmissionArea = area;
                starSystem.MinSize = 0.5f;
                starSystem.MaxSize = 1.5f;
                starSystem.TwinkleSpeed = 0.8f;
                
                starSystem.PrimaryColor = primaryColor;
                starSystem.SecondaryColor = Color.Lerp(primaryColor, Color.White, 0.3f);
                starSystem.MinAlpha = 0.1f;
                starSystem.MaxAlpha = 0.6f;

                // 将Actor添加到场景
                Level.SpawnActor(effectActor, _currentScene);

                _activeEffects[effectId] = effectActor;
                
                FlaxEngine.Debug.Log($"漂浮粒子效果 {effectId} 创建成功");
                return effectActor;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"创建漂浮粒子效果失败: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 更新粒子效果位置
        /// </summary>
        public static void UpdateEffectPosition(string effectId, Float3 newPosition)
        {
            if (_activeEffects.TryGetValue(effectId, out var effectActor) && effectActor != null)
            {
                effectActor.Transform = new Transform(new Vector3(newPosition.X, newPosition.Y, newPosition.Z));
            }
        }
        
        /// <summary>
        /// 更新粒子效果区域大小
        /// </summary>
        public static void UpdateEffectArea(string effectId, Float2 newArea)
        {
            if (_activeEffects.TryGetValue(effectId, out var effectActor) && effectActor != null)
            {
                var starSystem = effectActor.GetScript<StarParticleSystem>();
                if (starSystem != null)
                {
                    starSystem.SetEmissionArea(newArea);
                }
            }
        }
        
        /// <summary>
        /// 设置粒子效果激活状态
        /// </summary>
        public static void SetEffectActive(string effectId, bool active)
        {
            if (_activeEffects.TryGetValue(effectId, out var effectActor) && effectActor != null)
            {
                var starSystem = effectActor.GetScript<StarParticleSystem>();
                if (starSystem != null)
                {
                    starSystem.SetActive(active);
                }
            }
        }
        
        /// <summary>
        /// 销毁指定的粒子效果
        /// </summary>
        public static void DestroyEffect(string effectId)
        {
            if (_activeEffects.TryGetValue(effectId, out var effectActor) && effectActor != null)
            {
                Actor.Destroy(effectActor);
                _activeEffects.Remove(effectId);
                FlaxEngine.Debug.Log($"粒子效果 {effectId} 已销毁");
            }
        }
        
        /// <summary>
        /// 销毁所有活跃的粒子效果
        /// </summary>
        public static void DestroyAllEffects()
        {
            foreach (var kvp in _activeEffects)
            {
                if (kvp.Value != null)
                {
                    Actor.Destroy(kvp.Value);
                }
            }
            
            _activeEffects.Clear();
            FlaxEngine.Debug.Log($"所有粒子效果已清理，共清理 {_activeEffects.Count} 个效果");
        }
        
        /// <summary>
        /// 获取活跃效果数量
        /// </summary>
        public static int GetActiveEffectCount()
        {
            // 清理已销毁的引用
            var toRemove = new List<string>();
            foreach (var kvp in _activeEffects)
            {
                if (kvp.Value == null)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in toRemove)
            {
                _activeEffects.Remove(key);
            }
            
            return _activeEffects.Count;
        }
        
        /// <summary>
        /// 检查指定效果是否存在
        /// </summary>
        public static bool HasEffect(string effectId)
        {
            return _activeEffects.ContainsKey(effectId) && _activeEffects[effectId] != null;
        }
        
        /// <summary>
        /// 获取粒子效果Actor
        /// </summary>
        public static Actor GetEffect(string effectId)
        {
            _activeEffects.TryGetValue(effectId, out var effectActor);
            return effectActor;
        }
        
        /// <summary>
        /// 场景切换时的清理工作
        /// </summary>
        public static void OnSceneChanged(Scene newScene)
        {
            DestroyAllEffects();
            _currentScene = newScene;
            FlaxEngine.Debug.Log("粒子效果管理器已适配新场景");
        }
    }
}
