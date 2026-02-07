using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Effects
{
    /// <summary>
    /// 星空粒子效果系统
    /// 为UI对话框提供专业的星空背景粒子效果
    /// </summary>
    public class StarParticleSystem : Script
    {
        [Header("粒子配置")]
        [Tooltip("粒子数量")]
        public int ParticleCount = 50;
        
        [Tooltip("粒子生成区域大小")]
        public Float2 EmissionArea = new Float2(800, 600);
        
        [Tooltip("粒子最小大小")]
        public float MinSize = 1.0f;
        
        [Tooltip("粒子最大大小")]
        public float MaxSize = 3.0f;
        
        [Tooltip("闪烁速度")]
        public float TwinkleSpeed = 2.0f;
        
        [Header("颜色配置")]
        [Tooltip("主要颜色")]
        public Color PrimaryColor = Color.White;
        
        [Tooltip("次要颜色")]
        public Color SecondaryColor = new Color(0.8f, 0.9f, 1.0f);
        
        [Tooltip("最小透明度")]
        public float MinAlpha = 0.3f;
        
        [Tooltip("最大透明度")]
        public float MaxAlpha = 1.0f;
        
        // 内部数据结构
        private struct StarParticle
        {
            public Float3 Position;
            public float Size;
            public Color BaseColor;
            public float TwinklePhase;
            public float TwinkleFrequency;
            public float Brightness;
        }
        
        private StarParticle[] _particles;
        private bool _isInitialized = false;
        private Random _random;
        
        /// <summary>
        /// 初始化事件
        /// </summary>
        public override void OnStart()
        {
            _random = new Random();
            InitializeParticleSystem();
        }
        
        /// <summary>
        /// 初始化粒子系统
        /// </summary>
        private void InitializeParticleSystem()
        {
            try
            {
                InitializeParticles();
                _isInitialized = true;
                
                FlaxEngine.Debug.Log($"星空粒子系统初始化完成，粒子数量: {ParticleCount}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"星空粒子系统初始化失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 创建星星材质
        /// </summary>
        private void CreateStarMaterial()
        {
            // 对于简化的粒子系统，我们使用简单的调试绘制
            // 不需要创建复杂的材质系统
            FlaxEngine.Debug.Log("星星材质系统初始化（使用简化渲染）");
        }
        
        /// <summary>
        /// 创建星星网格
        /// </summary>
        private void CreateStarMesh()
        {
            // 对于简化的粒子系统，我们使用简单的几何形状渲染
            // 不需要创建复杂的网格数据
            FlaxEngine.Debug.Log("星星网格系统初始化（使用简化渲染）");
        }
        
        /// <summary>
        /// 初始化粒子数据
        /// </summary>
        private void InitializeParticles()
        {
            _particles = new StarParticle[ParticleCount];
            
            for (int i = 0; i < ParticleCount; i++)
            {
                _particles[i] = CreateRandomParticle();
            }
        }
        
        /// <summary>
        /// 创建随机粒子
        /// </summary>
        private StarParticle CreateRandomParticle()
        {
            var particle = new StarParticle();
            
            // 随机位置
            particle.Position = new Float3(
                (_random.NextSingle() - 0.5f) * EmissionArea.X,
                (_random.NextSingle() - 0.5f) * EmissionArea.Y,
                0
            );
            
            // 随机大小
            particle.Size = Mathf.Lerp(MinSize, MaxSize, _random.NextSingle());
            
            // 随机颜色（在主要和次要颜色之间插值）
            float colorLerp = _random.NextSingle();
            particle.BaseColor = Color.Lerp(PrimaryColor, SecondaryColor, colorLerp);
            
            // 随机闪烁参数
            particle.TwinklePhase = _random.NextSingle() * Mathf.TwoPi;
            particle.TwinkleFrequency = 0.5f + _random.NextSingle() * 1.5f; // 0.5-2.0的频率范围
            particle.Brightness = Mathf.Lerp(MinAlpha, MaxAlpha, _random.NextSingle());
            
            return particle;
        }
        
        /// <summary>
        /// 更新粒子系统
        /// </summary>
        public override void OnUpdate()
        {
            if (!_isInitialized) return;
            
            UpdateParticles();
        }
        
        /// <summary>
        /// 更新粒子状态
        /// </summary>
        private void UpdateParticles()
        {
            float deltaTime = Time.DeltaTime;
            
            for (int i = 0; i < _particles.Length; i++)
            {
                // 更新闪烁效果
                _particles[i].TwinklePhase += deltaTime * TwinkleSpeed * _particles[i].TwinkleFrequency;
                
                // 计算当前亮度（基于正弦波的闪烁）
                float twinkleFactor = (Mathf.Sin(_particles[i].TwinklePhase) + 1.0f) * 0.5f;
                _particles[i].Brightness = Mathf.Lerp(MinAlpha, MaxAlpha, twinkleFactor);
            }
        }
        
        /// <summary>
        /// 渲染粒子系统（使用简单的调试绘制）
        /// </summary>
        public override void OnDebugDraw()
        {
            if (!_isInitialized) return;
            
            // 使用简单的调试绘制来渲染星星
            for (int i = 0; i < _particles.Length; i++)
            {
                var particle = _particles[i];
                
                // 计算当前颜色（包含亮度）
                var currentColor = new Color(
                    particle.BaseColor.R,
                    particle.BaseColor.G,
                    particle.BaseColor.B,
                    particle.Brightness
                );
                
                // 计算世界位置
                var worldPosition = particle.Position + Transform.Translation;
                
                // 使用球体绘制来模拟星星
                var sphere = new BoundingSphere(worldPosition, particle.Size * 0.5f);
                DebugDraw.DrawSphere(sphere, currentColor);
            }
        }
        
        /// <summary>
        /// 设置发射区域大小
        /// </summary>
        public void SetEmissionArea(Float2 area)
        {
            EmissionArea = area;
            if (_isInitialized)
            {
                // 重新初始化粒子位置
                InitializeParticles();
            }
        }
        
        /// <summary>
        /// 设置粒子颜色
        /// </summary>
        public void SetColors(Color primary, Color secondary)
        {
            PrimaryColor = primary;
            SecondaryColor = secondary;
            
            if (_isInitialized)
            {
                // 更新现有粒子的颜色
                for (int i = 0; i < _particles.Length; i++)
                {
                    float colorLerp = _random.NextSingle();
                    _particles[i].BaseColor = Color.Lerp(PrimaryColor, SecondaryColor, colorLerp);
                }
            }
        }
        
        /// <summary>
        /// 销毁粒子系统
        /// </summary>
        public override void OnDestroy()
        {
            _isInitialized = false;
            _particles = null;
            base.OnDestroy();
        }
        
        /// <summary>
        /// 重新启动粒子系统
        /// </summary>
        public void Restart()
        {
            if (_isInitialized)
            {
                InitializeParticles();
                FlaxEngine.Debug.Log("星空粒子系统已重启");
            }
        }
        
        /// <summary>
        /// 设置粒子系统激活状态
        /// </summary>
        public void SetActive(bool active)
        {
            Enabled = active;
        }
    }
}