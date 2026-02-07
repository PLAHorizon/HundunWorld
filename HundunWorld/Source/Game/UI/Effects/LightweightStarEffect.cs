using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Effects
{
    /// <summary>
    /// 轻量级星空粒子效果
    /// 针对性能较低的设备进行优化的简化版本
    /// </summary>
    public class LightweightStarEffect : Script
    {
        [Header("基础配置")]
        [Tooltip("粒子数量（建议10-30）")]
        public int ParticleCount = 20;
        
        [Tooltip("效果区域大小")]
        public Float2 EffectArea = new Float2(600, 400);
        
        [Tooltip("粒子大小")]
        public float ParticleSize = 2.0f;
        
        [Tooltip("闪烁强度（0-1）")]
        public float TwinkleIntensity = 0.5f;
        
        [Header("颜色配置")]
        [Tooltip("星星颜色")]
        public Color StarColor = Color.White;
        
        [Tooltip("最小亮度")]
        public float MinBrightness = 0.3f;
        
        [Tooltip("最大亮度")]
        public float MaxBrightness = 0.8f;
        
        // 简化的粒子数据
        private struct LightStar
        {
            public Float2 Position;
            public float Phase;
            public float Frequency;
            public float Brightness;
        }
        
        private LightStar[] _stars;
        private bool _initialized = false;
        private Random _random;
        private float _time = 0f;
        
        /// <summary>
        /// 初始化轻量级星空效果
        /// </summary>
        public override void OnStart()
        {
            _random = new Random();
            InitializeStars();
            _initialized = true;
            
            FlaxEngine.Debug.Log($"轻量级星空效果初始化完成，粒子数量: {ParticleCount}");
        }
        
        /// <summary>
        /// 初始化星星数据
        /// </summary>
        private void InitializeStars()
        {
            _stars = new LightStar[ParticleCount];
            
            for (int i = 0; i < ParticleCount; i++)
            {
                _stars[i] = new LightStar
                {
                    Position = new Float2(
                        (_random.NextSingle() - 0.5f) * EffectArea.X,
                        (_random.NextSingle() - 0.5f) * EffectArea.Y
                    ),
                    Phase = _random.NextSingle() * Mathf.TwoPi,
                    Frequency = 0.5f + _random.NextSingle() * 1.0f,
                    Brightness = Mathf.Lerp(MinBrightness, MaxBrightness, _random.NextSingle())
                };
            }
        }
        
        /// <summary>
        /// 更新星空效果
        /// </summary>
        public override void OnUpdate()
        {
            if (!_initialized) return;
            
            _time += Time.DeltaTime;
            
            // 简单的闪烁更新
            for (int i = 0; i < _stars.Length; i++)
            {
                float twinkle = Mathf.Sin(_time * _stars[i].Frequency + _stars[i].Phase);
                _stars[i].Brightness = MinBrightness + (MaxBrightness - MinBrightness) * 
                                     (0.5f + 0.5f * twinkle * TwinkleIntensity);
            }
        }
        
        /// <summary>
        /// 绘制星空效果（使用简单的调试绘制）
        /// </summary>
        public override void OnDebugDraw()
        {
            if (!_initialized) return;
            
            var basePosition = Transform.Translation;
            
            for (int i = 0; i < _stars.Length; i++)
            {
                var worldPos = basePosition + new Float3(_stars[i].Position.X, _stars[i].Position.Y, 0);
                var color = new Color(StarColor.R, StarColor.G, StarColor.B, _stars[i].Brightness);
                
                // 使用简单的点绘制
                DebugDraw.DrawSphere(new BoundingSphere(worldPos, ParticleSize * 0.5f), color);
            }
        }
        
        /// <summary>
        /// 设置效果区域
        /// </summary>
        public void SetEffectArea(Float2 area)
        {
            EffectArea = area;
            if (_initialized)
            {
                InitializeStars();
            }
        }
        
        /// <summary>
        /// 设置星星颜色
        /// </summary>
        public void SetStarColor(Color color)
        {
            StarColor = color;
        }
        
        /// <summary>
        /// 设置闪烁强度
        /// </summary>
        public void SetTwinkleIntensity(float intensity)
        {
            TwinkleIntensity = Mathf.Clamp(intensity,0,1);
        }
        
        /// <summary>
        /// 重启效果
        /// </summary>
        public void RestartEffect()
        {
            if (_initialized)
            {
                InitializeStars();
                _time = 0f;
            }
        }
    }
    
    /// <summary>
    /// UI中的2D星空效果（完全基于GUI渲染）
    /// 当无法使用3D粒子系统时的备选方案
    /// </summary>
    public class GUI2DStarEffect
    {
        private struct GUI2DStar
        {
            public Float2 Position;
            public float Size;
            public Color BaseColor;
            public float TwinklePhase;
            public float TwinkleSpeed;
            public Panel StarPanel;
        }
        
        private GUI2DStar[] _guiStars;
        private Panel _containerPanel;
        private bool _isActive = false;
        private Random _random;
        private float _time = 0f;
        
        public int StarCount { get; private set; } = 15;
        public Float2 EffectSize { get; private set; } = new Float2(400, 300);
        public Color PrimaryColor { get; set; } = Color.White;
        public Color SecondaryColor { get; set; } = new Color(0.8f, 0.9f, 1.0f);
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public GUI2DStarEffect(Panel container, int starCount = 15)
        {
            _containerPanel = container;
            StarCount = Math.Max(5, Math.Min(30, starCount)); // 限制星星数量
            _random = new Random();
            
            InitializeGUIStars();
        }
        
        /// <summary>
        /// 初始化GUI星星
        /// </summary>
        private void InitializeGUIStars()
        {
            if (_containerPanel == null) return;
            
            _guiStars = new GUI2DStar[StarCount];
            EffectSize = new Float2(_containerPanel.Width, _containerPanel.Height);
            
            for (int i = 0; i < StarCount; i++)
            {
                var star = new GUI2DStar();
                
                // 随机位置
                star.Position = new Float2(
                    _random.NextSingle() * EffectSize.X,
                    _random.NextSingle() * EffectSize.Y
                );
                
                // 随机大小
                star.Size = 1.0f + _random.NextSingle() * 2.0f;
                
                // 随机颜色
                star.BaseColor = Color.Lerp(PrimaryColor, SecondaryColor, _random.NextSingle());
                
                // 闪烁参数
                star.TwinklePhase = _random.NextSingle() * Mathf.TwoPi;
                star.TwinkleSpeed = 0.5f + _random.NextSingle() * 1.5f;
                
                // 创建GUI面板
                star.StarPanel = new Panel
                {
                    Size = new Float2(star.Size, star.Size),
                    Location = star.Position,
                    BackgroundColor = star.BaseColor,
                    Visible = false // 初始隐藏
                };
                
                _containerPanel.AddChild(star.StarPanel);
                _guiStars[i] = star;
            }
            
            FlaxEngine.Debug.Log($"GUI2D星空效果初始化完成，星星数量: {StarCount}");
        }
        
        /// <summary>
        /// 启动效果
        /// </summary>
        public void Start()
        {
            _isActive = true;
            _time = 0f;
            
            // 显示所有星星
            for (int i = 0; i < _guiStars.Length; i++)
            {
                if (_guiStars[i].StarPanel != null)
                {
                    _guiStars[i].StarPanel.Visible = true;
                }
            }
        }
        
        /// <summary>
        /// 停止效果
        /// </summary>
        public void Stop()
        {
            _isActive = false;
            
            // 隐藏所有星星
            for (int i = 0; i < _guiStars.Length; i++)
            {
                if (_guiStars[i].StarPanel != null)
                {
                    _guiStars[i].StarPanel.Visible = false;
                }
            }
        }
        
        /// <summary>
        /// 更新效果（需要在父组件的Update中调用）
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!_isActive) return;
            
            _time += deltaTime;
            
            for (int i = 0; i < _guiStars.Length; i++)
            {
                if (_guiStars[i].StarPanel == null) continue;
                
                // 计算闪烁效果
                float twinkleValue = Mathf.Sin(_time * _guiStars[i].TwinkleSpeed + _guiStars[i].TwinklePhase);
                float alpha = 0.3f + 0.7f * (0.5f + 0.5f * twinkleValue);
                
                // 更新颜色
                var currentColor = new Color(
                    _guiStars[i].BaseColor.R,
                    _guiStars[i].BaseColor.G,
                    _guiStars[i].BaseColor.B,
                    alpha
                );
                
                _guiStars[i].StarPanel.BackgroundColor = currentColor;
            }
        }
        
        /// <summary>
        /// 设置效果颜色
        /// </summary>
        public void SetColors(Color primary, Color secondary)
        {
            PrimaryColor = primary;
            SecondaryColor = secondary;
            
            // 更新现有星星的基础颜色
            for (int i = 0; i < _guiStars.Length; i++)
            {
                float colorLerp = _random.NextSingle();
                _guiStars[i].BaseColor = Color.Lerp(primary, secondary, colorLerp);
            }
        }
        
        /// <summary>
        /// 调整星星位置以适应新的容器尺寸
        /// </summary>
        public void ResizeToContainer()
        {
            if (_containerPanel == null) return;
            
            var newSize = new Float2(_containerPanel.Width, _containerPanel.Height);
            if (newSize == EffectSize) return;
            
            EffectSize = newSize;
            
            // 重新分布星星位置
            for (int i = 0; i < _guiStars.Length; i++)
            {
                if (_guiStars[i].StarPanel == null) continue;
                
                var newPos = new Float2(
                    _random.NextSingle() * EffectSize.X,
                    _random.NextSingle() * EffectSize.Y
                );
                
                _guiStars[i].Position = newPos;
                _guiStars[i].StarPanel.Location = newPos;
            }
        }
        
        /// <summary>
        /// 销毁效果
        /// </summary>
        public void Destroy()
        {
            Stop();
            
            if (_guiStars != null)
            {
                for (int i = 0; i < _guiStars.Length; i++)
                {
                    if (_guiStars[i].StarPanel != null)
                    {
                        _containerPanel?.RemoveChild(_guiStars[i].StarPanel);
                        _guiStars[i].StarPanel.Dispose();
                    }
                }
            }
            
            _guiStars = null;
            _containerPanel = null;
        }
    }
}