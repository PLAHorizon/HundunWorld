using FlaxEngine;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// AOE技能范围指示器系统
    /// 显示圆形/扇形/矩形范围预览
    /// </summary>
    public class AOEIndicatorSystem : Script
    {
        public enum IndicatorShape
        {
            Circle,     // 圆形（火球术爆炸）
            Sector,     // 扇形（火焰风暴）
            Rectangle,  // 矩形（剑气斩）
            Line        // 直线（冲锋路径）
        }

        private static AOEIndicatorSystem _instance;
        public static AOEIndicatorSystem Instance => _instance;

        [Header("指示器设置")]
        [Tooltip("指示器颜色（有效范围）")]
        public Color ValidColor = new Color(0.0f, 1.0f, 0.0f, 0.4f);

        [Tooltip("指示器颜色（超出范围）")]
        public Color InvalidColor = new Color(1.0f, 0.0f, 0.0f, 0.4f);

        [Tooltip("指示器高度偏移")]
        public float IndicatorHeightOffset = 0.1f;

        [Tooltip("指示器分段数（圆形精度）")]
        public int CircleSegments = 32;

        [Tooltip("是否启用调试日志")]
        public bool EnableDebugLog = false;

        // 当前指示器配置
        private IndicatorConfig _currentConfig;
        private bool _isShowing = false;
        private Vector3 _indicatorPosition;
        private bool _isInRange = true;

        // 最大技能射程（用于范围检查）
        private float _maxRange = 25f;

        public override void OnAwake()
        {
            base.OnAwake();
            _instance = this;

            if (EnableDebugLog)
                Debug.Log("[AOEIndicatorSystem] 初始化完成");
        }

        public override void OnUpdate()
        {
            if (!_isShowing || _currentConfig == null)
                return;

            // 更新指示器位置（跟随鼠标）
            UpdateIndicatorPosition();

            // 绘制指示器
            DrawIndicator();
        }

        /// <summary>
        /// 显示AOE指示器
        /// </summary>
        /// <param name="shape">指示器形状</param>
        /// <param name="radius">半径（圆形/扇形）或宽度（矩形）</param>
        /// <param name="angle">角度（扇形，单位：度）</param>
        /// <param name="length">长度（矩形/直线）</param>
        /// <param name="maxRange">最大技能射程</param>
        public void ShowIndicator(IndicatorShape shape, float radius, float angle = 90f, float length = 0f, float maxRange = 25f)
        {
            _currentConfig = new IndicatorConfig
            {
                Shape = shape,
                Radius = radius,
                Angle = angle,
                Length = length
            };

            _maxRange = maxRange;
            _isShowing = true;

            if (EnableDebugLog)
                Debug.Log($"[AOEIndicatorSystem] 显示指示器: {shape}, 半径={radius}, 角度={angle}, 长度={length}");
        }

        /// <summary>
        /// 隐藏AOE指示器
        /// </summary>
        public void HideIndicator()
        {
            _isShowing = false;
            _currentConfig = null;

            if (EnableDebugLog)
                Debug.Log("[AOEIndicatorSystem] 隐藏指示器");
        }

        /// <summary>
        /// 更新指示器位置（跟随鼠标）
        /// </summary>
        private void UpdateIndicatorPosition()
        {
            var camera = Camera.MainCamera;
            if (camera == null) return;

            // 从鼠标位置发射射线到地面
            var mousePos = Input.MousePosition;
            var ray = camera.ConvertMouseToRay(mousePos);
            
            if (Physics.RayCast(ray.Position, ray.Direction, out RayCastHit hit, 1000f))
            {
                // 放置在地面上
                _indicatorPosition = hit.Point + new Vector3(0, IndicatorHeightOffset, 0);

                // 检查是否在技能范围内
                _isInRange = CheckInRange(hit.Point);
            }
        }

        /// <summary>
        /// 绘制指示器
        /// </summary>
        private void DrawIndicator()
        {
            if (_currentConfig == null)
                return;

            Color currentColor = _isInRange ? ValidColor : InvalidColor;

            switch (_currentConfig.Shape)
            {
                case IndicatorShape.Circle:
                    DrawCircleIndicator(_indicatorPosition, _currentConfig.Radius, currentColor);
                    break;

                case IndicatorShape.Sector:
                    DrawSectorIndicator(_indicatorPosition, _currentConfig.Radius, _currentConfig.Angle, currentColor);
                    break;

                case IndicatorShape.Rectangle:
                    DrawRectangleIndicator(_indicatorPosition, _currentConfig.Radius, _currentConfig.Length, currentColor);
                    break;

                case IndicatorShape.Line:
                    DrawLineIndicator(_indicatorPosition, _currentConfig.Length, currentColor);
                    break;
            }
        }

        /// <summary>
        /// 绘制圆形指示器
        /// </summary>
        private void DrawCircleIndicator(Vector3 center, float radius, Color color)
        {
            // 绘制主圆圈
            DebugDraw.DrawCircle(center, Vector3.Up, radius, color, 0.0f);

            // 绘制十字线（帮助瞄准）
            Vector3 left = center + new Vector3(-radius, 0, 0);
            Vector3 right = center + new Vector3(radius, 0, 0);
            Vector3 forward = center + new Vector3(0, 0, radius);
            Vector3 back = center + new Vector3(0, 0, -radius);

            DebugDraw.DrawLine(left, right, color, 0.0f);
            DebugDraw.DrawLine(forward, back, color, 0.0f);

            // 绘制额外的圆圈（显示范围边缘）
            DebugDraw.DrawCircle(center, Vector3.Up, radius * 0.5f, 
                new Color(color.R, color.G, color.B, color.A * 0.5f), 0.0f);
        }

        /// <summary>
        /// 绘制扇形指示器
        /// </summary>
        private void DrawSectorIndicator(Vector3 center, float radius, float angle, Color color)
        {
            // 获取玩家朝向
            var playerForward = Actor.Direction;
            
            // 计算扇形的起始和结束角度
            float halfAngle = angle * 0.5f * Mathf.DegreesToRadians;
            
            // 绘制扇形边缘
            int segments = (int)(CircleSegments * (angle / 360f));
            Vector3 prevPoint = center;
            
            for (int i = 0; i <= segments; i++)
            {
                float currentAngle = -halfAngle + (2 * halfAngle * i / segments);
                
                // 使用玩家朝向旋转
                Quaternion rotation = Quaternion.Euler(0, currentAngle * Mathf.RadiansToDegrees, 0);
                Vector3 direction = Vector3.Transform(playerForward, rotation);
                
                Vector3 point = center + direction * radius;
                
                if (i > 0)
                {
                    DebugDraw.DrawLine(prevPoint, point, color, 0.0f);
                }
                else
                {
                    // 第一个点，绘制从中心到边缘的线
                    DebugDraw.DrawLine(center, point, color, 0.0f);
                }
                
                prevPoint = point;
            }
            
            // 绘制最后一条线回到中心
            DebugDraw.DrawLine(prevPoint, center, color, 0.0f);

            // 绘制中心十字
            Vector3 forwardLine = center + playerForward * radius * 0.3f;
            DebugDraw.DrawLine(center, forwardLine, color, 0.0f);
        }

        /// <summary>
        /// 绘制矩形指示器
        /// </summary>
        private void DrawRectangleIndicator(Vector3 center, float width, float length, Color color)
        {
            var playerForward = Actor.Direction;
            var right = Vector3.Cross(Vector3.Up, playerForward).Normalized;

            // 计算矩形四个角
            Vector3 frontLeft = center + playerForward * length * 0.5f - right * width * 0.5f;
            Vector3 frontRight = center + playerForward * length * 0.5f + right * width * 0.5f;
            Vector3 backLeft = center - playerForward * length * 0.5f - right * width * 0.5f;
            Vector3 backRight = center - playerForward * length * 0.5f + right * width * 0.5f;

            // 绘制矩形边框
            DebugDraw.DrawLine(frontLeft, frontRight, color, 0.0f);
            DebugDraw.DrawLine(frontRight, backRight, color, 0.0f);
            DebugDraw.DrawLine(backRight, backLeft, color, 0.0f);
            DebugDraw.DrawLine(backLeft, frontLeft, color, 0.0f);

            // 绘制对角线
            DebugDraw.DrawLine(frontLeft, backRight, 
                new Color(color.R, color.G, color.B, color.A * 0.3f), 0.0f);
            DebugDraw.DrawLine(frontRight, backLeft, 
                new Color(color.R, color.G, color.B, color.A * 0.3f), 0.0f);
        }

        /// <summary>
        /// 绘制直线指示器
        /// </summary>
        private void DrawLineIndicator(Vector3 start, float length, Color color)
        {
            var playerForward = Actor.Direction;
            Vector3 end = start + playerForward * length;

            // 绘制主线
            DebugDraw.DrawLine(start, end, color, 0.0f);

            // 绘制箭头
            Vector3 right = Vector3.Cross(Vector3.Up, playerForward).Normalized;
            Vector3 arrowLeft = end - playerForward * 0.5f - right * 0.3f;
            Vector3 arrowRight = end - playerForward * 0.5f + right * 0.3f;
            
            DebugDraw.DrawLine(end, arrowLeft, color, 0.0f);
            DebugDraw.DrawLine(end, arrowRight, color, 0.0f);
        }

        /// <summary>
        /// 检查是否在技能范围内
        /// </summary>
        private bool CheckInRange(Vector3 targetPos)
        {
            float distance = Vector3.Distance(Actor.Position, targetPos);
            return distance <= _maxRange;
        }

        /// <summary>
        /// 获取当前指示器位置
        /// </summary>
        public Vector3 GetIndicatorPosition()
        {
            return _indicatorPosition;
        }

        /// <summary>
        /// 是否正在显示指示器
        /// </summary>
        public bool IsShowing => _isShowing;

        /// <summary>
        /// 当前位置是否在有效范围内
        /// </summary>
        public bool IsInRange => _isInRange;

        public override void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            
            base.OnDestroy();
        }

        /// <summary>
        /// 指示器配置
        /// </summary>
        private class IndicatorConfig
        {
            public IndicatorShape Shape { get; set; }
            public float Radius { get; set; }
            public float Angle { get; set; }
            public float Length { get; set; }
        }
    }
}
