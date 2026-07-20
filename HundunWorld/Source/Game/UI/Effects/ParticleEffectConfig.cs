using System;
using FlaxEngine;

namespace HundunWorld.Game.UI.Effects
{
    /// <summary>
    /// 粒子效果配置类
    /// 定义不同场景下的粒子效果参数
    /// </summary>
    [Serializable]
    public struct ParticleEffectConfig
    {
        [Header("基础参数")]
        public int ParticleCount;
        public Float2 EmissionArea;
        public float MinSize;
        public float MaxSize;
        public float TwinkleSpeed;
        
        [Header("颜色配置")]
        public Color PrimaryColor;
        public Color SecondaryColor;
        public float MinAlpha;
        public float MaxAlpha;
        
        [Header("性能配置")]
        public bool EnableAdvancedEffects;
        public bool UseGPUParticles;
        public int MaxRenderDistance;
        
        /// <summary>
        /// 创建对话框默认配置
        /// </summary>
        public static ParticleEffectConfig CreateDialogDefault()
        {
            return new ParticleEffectConfig
            {
                ParticleCount = 30,
                EmissionArea = new Float2(600, 400),
                MinSize = 1.0f,
                MaxSize = 2.5f,
                TwinkleSpeed = 1.5f,
                PrimaryColor = UIStyleTokens.Gold(0.8f), // 金粉微粒（--ink-gold-primary）
                SecondaryColor = UIStyleTokens.Jade(0.6f), // 青玉萤光（--ink-jade-primary）
                MinAlpha = 0.2f,
                MaxAlpha = 0.9f,
                EnableAdvancedEffects = true,
                UseGPUParticles = false,
                MaxRenderDistance = 1000
            };
        }
        
        /// <summary>
        /// 创建简化配置（低性能设备）
        /// </summary>
        public static ParticleEffectConfig CreateSimplified()
        {
            return new ParticleEffectConfig
            {
                ParticleCount = 15,
                EmissionArea = new Float2(400, 300),
                MinSize = 1.0f,
                MaxSize = 2.0f,
                TwinkleSpeed = 1.0f,
                PrimaryColor = UIStyleTokens.Gold(0.6f),
                SecondaryColor = UIStyleTokens.WithAlpha(UIStyleTokens.TextPrimary, 0.4f),
                MinAlpha = 0.3f,
                MaxAlpha = 0.7f,
                EnableAdvancedEffects = false,
                UseGPUParticles = false,
                MaxRenderDistance = 500
            };
        }
        
        /// <summary>
        /// 创建登录界面配置
        /// </summary>
        public static ParticleEffectConfig CreateLoginBackground()
        {
            return new ParticleEffectConfig
            {
                ParticleCount = 50,
                EmissionArea = new Float2(1200, 800),
                MinSize = 0.5f,
                MaxSize = 2.0f,
                TwinkleSpeed = 0.8f,
                PrimaryColor = UIStyleTokens.Jade(0.3f), // 登录页 青玉环境微粒
                SecondaryColor = UIStyleTokens.Gold(0.2f),
                MinAlpha = 0.1f,
                MaxAlpha = 0.6f,
                EnableAdvancedEffects = true,
                UseGPUParticles = true,
                MaxRenderDistance = 1500
            };
        }
        
        /// <summary>
        /// 创建游戏主界面配置
        /// </summary>
        public static ParticleEffectConfig CreateGameUI()
        {
            return new ParticleEffectConfig
            {
                ParticleCount = 25,
                EmissionArea = new Float2(800, 600),
                MinSize = 0.8f,
                MaxSize = 1.8f,
                TwinkleSpeed = 1.2f,
                PrimaryColor = UIStyleTokens.Gold(0.5f),
                SecondaryColor = UIStyleTokens.Jade(0.4f),
                MinAlpha = 0.2f,
                MaxAlpha = 0.8f,
                EnableAdvancedEffects = true,
                UseGPUParticles = false,
                MaxRenderDistance = 800
            };
        }
    }
    
    /// <summary>
    /// 粒子效果质量等级
    /// </summary>
    public enum ParticleQuality
    {
        Low,        // 低质量：最少粒子，基础效果
        Medium,     // 中等质量：平衡性能和效果
        High,       // 高质量：较多粒子，丰富效果
        Ultra       // 超高质量：最多粒子，所有效果
    }
    
    /// <summary>
    /// 粒子效果类型
    /// </summary>
    public enum ParticleEffectType
    {
        StarField,      // 星空效果
        FloatingDots,   // 漂浮点效果
        Sparkles,       // 闪光效果
        Dust,           // 尘埃效果
        Energy,         // 能量效果
        Magic           // 魔法效果
    }
    
    /// <summary>
    /// 粒子效果设置管理器
    /// </summary>
    public static class ParticleEffectSettings
    {
        private static ParticleQuality _currentQuality = ParticleQuality.Medium;
        private static bool _particleEffectsEnabled = true;
        
        /// <summary>
        /// 当前粒子质量等级
        /// </summary>
        public static ParticleQuality CurrentQuality
        {
            get => _currentQuality;
            set => _currentQuality = value;
        }
        
        /// <summary>
        /// 粒子效果是否启用
        /// </summary>
        public static bool ParticleEffectsEnabled
        {
            get => _particleEffectsEnabled;
            set => _particleEffectsEnabled = value;
        }
        
        /// <summary>
        /// 根据质量等级调整配置
        /// </summary>
        public static ParticleEffectConfig AdjustConfigByQuality(ParticleEffectConfig baseConfig)
        {
            if (!_particleEffectsEnabled)
            {
                return CreateDisabledConfig();
            }
            
            var adjustedConfig = baseConfig;
            
            switch (_currentQuality)
            {
                case ParticleQuality.Low:
                    adjustedConfig.ParticleCount = Math.Max(5, baseConfig.ParticleCount / 3);
                    adjustedConfig.TwinkleSpeed *= 0.5f;
                    adjustedConfig.EnableAdvancedEffects = false;
                    adjustedConfig.UseGPUParticles = false;
                    break;
                    
                case ParticleQuality.Medium:
                    adjustedConfig.ParticleCount = Math.Max(10, baseConfig.ParticleCount / 2);
                    adjustedConfig.TwinkleSpeed *= 0.8f;
                    adjustedConfig.EnableAdvancedEffects = false;
                    break;
                    
                case ParticleQuality.High:
                    // 使用基础配置
                    break;
                    
                case ParticleQuality.Ultra:
                    adjustedConfig.ParticleCount = (int)(baseConfig.ParticleCount * 1.5f);
                    adjustedConfig.TwinkleSpeed *= 1.2f;
                    adjustedConfig.EnableAdvancedEffects = true;
                    adjustedConfig.UseGPUParticles = true;
                    break;
            }
            
            return adjustedConfig;
        }
        
        /// <summary>
        /// 创建禁用状态的配置
        /// </summary>
        private static ParticleEffectConfig CreateDisabledConfig()
        {
            return new ParticleEffectConfig
            {
                ParticleCount = 0,
                EmissionArea = Float2.Zero,
                MinSize = 0,
                MaxSize = 0,
                TwinkleSpeed = 0,
                PrimaryColor = Color.Transparent,
                SecondaryColor = Color.Transparent,
                MinAlpha = 0,
                MaxAlpha = 0,
                EnableAdvancedEffects = false,
                UseGPUParticles = false,
                MaxRenderDistance = 0
            };
        }
        
        /// <summary>
        /// 自动检测并设置质量等级（基于性能）
        /// </summary>
        public static void AutoDetectQuality()
        {
            try
            {
                // 这里可以基于设备性能进行自动检测
                // 简化实现：基于屏幕分辨率进行粗略判断
                var screenSize = Screen.Size;
                var totalPixels = screenSize.X * screenSize.Y;
                
                if (totalPixels > 3840 * 2160) // 4K及以上
                {
                    _currentQuality = ParticleQuality.Ultra;
                }
                else if (totalPixels > 1920 * 1080) // 2K
                {
                    _currentQuality = ParticleQuality.High;
                }
                else if (totalPixels > 1366 * 768) // 1080p
                {
                    _currentQuality = ParticleQuality.Medium;
                }
                else // 低分辨率
                {
                    _currentQuality = ParticleQuality.Low;
                }
                
                FlaxEngine.Debug.Log($"自动检测粒子质量等级: {_currentQuality} (屏幕分辨率: {screenSize})");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"自动检测粒子质量失败，使用默认中等质量: {ex.Message}");
                _currentQuality = ParticleQuality.Medium;
            }
        }
        
        /// <summary>
        /// 获取推荐的配置
        /// </summary>
        public static ParticleEffectConfig GetRecommendedConfig(ParticleEffectType effectType)
        {
            ParticleEffectConfig baseConfig = effectType switch
            {
                ParticleEffectType.StarField => ParticleEffectConfig.CreateDialogDefault(),
                ParticleEffectType.FloatingDots => ParticleEffectConfig.CreateGameUI(),
                ParticleEffectType.Sparkles => ParticleEffectConfig.CreateLoginBackground(),
                _ => ParticleEffectConfig.CreateSimplified()
            };
            
            return AdjustConfigByQuality(baseConfig);
        }
    }
}