using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game.Combat.Effects
{
    /// <summary>
    /// 伤害数字显示系统
    /// 负责在屏幕上显示战斗中的伤害数值、治疗数值等
    /// </summary>
    public class DamageNumberSystem : Script
    {
        // 单例实例
        private static DamageNumberSystem _instance;
        public static DamageNumberSystem Instance => _instance;

        // 字体资源
        private FontAsset _font;
        
        // Canvas用于显示UI元素
        private UICanvas _canvas;
        
        // 活跃的伤害数字列表
        private List<DamageNumber> _activeNumbers = new List<DamageNumber>();

        /// <summary>
        /// 初始化系统
        /// </summary>
        public override void OnStart()
        {
            _instance = this;
            Initialize();
        }

        /// <summary>
        /// 系统初始化
        /// </summary>
        private void Initialize()
        {
            // 加载字体
            _font = Content.Load<FontAsset>("/Game/Fonts/DefaultFont");
            
            // 获取或创建Canvas用于显示伤害数字
            _canvas = Actor.Parent?.GetScript<UICanvas>();
            if (_canvas == null)
            {
                // 简化的Canvas创建方式
                _canvas = new UICanvas();
                _canvas.Name = "DamageNumberCanvas";
                var canvasActor = new EmptyActor { Name = "DamageNumberCanvasActor" };
                canvasActor.SetParent(Actor.Parent, false);
                // 创建Canvas Actor并添加UICanvas组件
                _canvas = canvasActor.AddChild<UICanvas>();
                Level.SpawnActor(canvasActor);
            }
        }

        /// <summary>
        /// 显示伤害数字
        /// </summary>
        public void ShowDamageNumber(float damage, Vector3 worldPosition, bool isCritical = false, bool isDodged = false, bool isBlocked = false)
        {
            try
            {
                // 如果是闪避或格挡，显示相应文字
                string damageText;
                Color damageColor;
                
                if (isDodged)
                {
                    damageText = "闪避";
                    damageColor = Color.Yellow;
                }
                else if (isBlocked)
                {
                    damageText = "格挡";
                    damageColor = Color.Gray;
                }
                else if (isCritical)
                {
                    damageText = ((int)damage).ToString();
                    damageColor = Color.Red; // 暴击用红色
                }
                else
                {
                    damageText = ((int)damage).ToString();
                    damageColor = Color.White; // 普通伤害用白色
                }

                // 创建伤害数字对象
                var damageNumber = new DamageNumber
                {
                    Text = damageText,
                    Color = damageColor,
                    Position = worldPosition,
                    StartTime = Time.TimeSinceStartup,
                    Duration = 1.5f, // 显示1.5秒
                    IsCritical = isCritical,
                    Scale = isCritical ? 1.5f : 1.0f // 暴击数字更大
                };

                _activeNumbers.Add(damageNumber);
            }
            catch (Exception ex)
            {
                Debug.LogError($"显示伤害数字时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示文本
        /// </summary>
        public void ShowText(string text, Vector3 worldPosition, Color color, float duration = 1.5f)
        {
            try
            {
                var damageNumber = new DamageNumber
                {
                    Text = text,
                    Color = color,
                    Position = worldPosition,
                    StartTime = Time.TimeSinceStartup,
                    Duration = duration,
                    IsCritical = false,
                    Scale = 1.0f
                };

                _activeNumbers.Add(damageNumber);
            }
            catch (Exception ex)
            {
                Debug.LogError($"显示文本时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新系统（每帧调用）
        /// </summary>
        public override void OnUpdate()
        {
            Update();
        }

        /// <summary>
        /// 更新系统（每帧调用）
        /// </summary>
        public void Update()
        {
            // 更新所有活跃的伤害数字
            for (int i = _activeNumbers.Count - 1; i >= 0; i--)
            {
                var damageNumber = _activeNumbers[i];
                
                // 检查是否过期
                if (Time.TimeSinceStartup - damageNumber.StartTime > damageNumber.Duration)
                {
                    _activeNumbers.RemoveAt(i);
                    continue;
                }

                // 更新位置（向上飘动）
                float elapsed = Time.TimeSinceStartup - damageNumber.StartTime;
                float newY = damageNumber.Position.Y + (elapsed * 2.0f); // 每秒上升2米
                
                // 更新透明度（逐渐消失）
                float alpha = 1.0f - (elapsed / damageNumber.Duration);
                
                // 绘制伤害数字
                DrawDamageNumber(damageNumber, new Vector3(damageNumber.Position.X, newY, damageNumber.Position.Z), alpha);
            }
        }

        /// <summary>
        /// 绘制伤害数字
        /// </summary>
        private void DrawDamageNumber(DamageNumber damageNumber, Vector3 position, float alpha)
        {
            try
            {
                // 将3D位置转换为屏幕坐标
                var camera = Camera.MainCamera;
                if (camera == null) return;

                // 使用摄像机视口将世界坐标投影到屏幕坐标
                camera.ProjectPoint(position, out var screenPos);

                // 在摄像机后面的不显示
                if (screenPos.Z <= 0)
                    return;

                // 设置绘制颜色（包含透明度）
                var drawColor = damageNumber.Color;
                drawColor.A *= alpha;

                // 使用DebugDraw在3D空间中绘制文字（始终面向摄像机）
                var fontSize = damageNumber.IsCritical ? 16 : 12;
                DebugDraw.DrawText(damageNumber.Text, position, drawColor, fontSize, 0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"绘制伤害数字时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 伤害数字数据结构
        /// </summary>
        private class DamageNumber
        {
            public string Text { get; set; }
            public Color Color { get; set; }
            public Vector3 Position { get; set; }
            public float StartTime { get; set; }
            public float Duration { get; set; }
            public bool IsCritical { get; set; }
            public float Scale { get; set; }
        }

        /// <summary>
        /// 伤害类型枚举
        /// </summary>
        public enum DamageType
        {
            Physical,    // 物理伤害
            Magical,     // 法术伤害
            True,        // 真实伤害
            Healing      // 治疗
        }
    }
}