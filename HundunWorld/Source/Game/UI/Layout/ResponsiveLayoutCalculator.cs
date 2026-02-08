using FlaxEngine;
using System;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Layout
{
    /// <summary>
    /// 响应式布局计算器
    /// 实现动态居中计算和多分辨率适配
    /// </summary>
    public static class ResponsiveLayoutCalculator
    {
        /// <summary>
        /// 黄金比例常量
        /// </summary>
        public const float GoldenRatio = 1.618f;
        
        /// <summary>
        /// 最小安全边距
        /// </summary>
        public const float MinSafeMargin = 20f;
        
        /// <summary>
        /// 计算屏幕居中位置
        /// </summary>
        /// <param name="panelSize">面板尺寸</param>
        /// <returns>居中位置</returns>
        public static Float2 CalculateCenterPosition(Float2 panelSize)
        {
            var screenSize = FlaxEngine.Screen.Size;
            return new Float2(
                (screenSize.X - panelSize.X) / 2f,
                (screenSize.Y - panelSize.Y) / 2f
            );
        }
        
        /// <summary>
        /// 计算相对于父容器的居中位置
        /// </summary>
        /// <param name="panelSize">面板尺寸</param>
        /// <param name="parentSize">父容器尺寸</param>
        /// <returns>相对居中位置</returns>
        public static Float2 CalculateRelativeCenterPosition(Float2 panelSize, Float2 parentSize)
        {
            return new Float2(
                (parentSize.X - panelSize.X) / 2f,
                (parentSize.Y - panelSize.Y) / 2f
            );
        }
        
        /// <summary>
        /// 根据黄金比例计算推荐尺寸
        /// </summary>
        /// <param name="baseWidth">基础宽度</param>
        /// <param name="useGoldenRatio">是否使用黄金比例</param>
        /// <returns>推荐尺寸</returns>
        public static Float2 CalculateOptimalSize(float baseWidth, bool useGoldenRatio = true)
        {
            if (useGoldenRatio)
            {
                return new Float2(baseWidth, baseWidth / GoldenRatio);
            }
            return new Float2(baseWidth, baseWidth * 0.75f); // 4:3 比例
        }
        
        /// <summary>
        /// 验证并调整尺寸以确保在安全区域内
        /// </summary>
        /// <param name="size">原始尺寸</param>
        /// <param name="maxSize">最大可用尺寸</param>
        /// <returns>调整后的安全尺寸</returns>
        public static Float2 EnsureSafeSize(Float2 size, Float2? maxSize = null)
        {
            var screenSize = maxSize ?? FlaxEngine.Screen.Size;
            var safeSize = new Float2(
                screenSize.X - MinSafeMargin * 2,
                screenSize.Y - MinSafeMargin * 2
            );
            
            return new Float2(
                Math.Min(size.X, safeSize.X),
                Math.Min(size.Y, safeSize.Y)
            );
        }
        
        /// <summary>
        /// 计算缩放因子以适应不同分辨率
        /// </summary>
        /// <param name="targetSize">目标尺寸</param>
        /// <param name="referenceResolution">参考分辨率</param>
        /// <returns>缩放因子</returns>
        public static float CalculateScaleFactor(Float2? referenceResolution = null)
        {
            var reference = referenceResolution ?? new Float2(1920, 1080);
            var current = FlaxEngine.Screen.Size;
            
            // 使用最小缩放因子保持宽高比
            var scaleX = current.X / reference.X;
            var scaleY = current.Y / reference.Y;
            
            return Math.Min(scaleX, scaleY);
        }
        
        /// <summary>
        /// 根据屏幕分辨率类型返回适配策略
        /// </summary>
        /// <returns>分辨率类型</returns>
        public static ResolutionType GetResolutionType()
        {
            var screenSize = FlaxEngine.Screen.Size;
            
            if (screenSize.X <= 1024 || screenSize.Y <= 768)
                return ResolutionType.Low;
            else if (screenSize.X <= 1920 && screenSize.Y <= 1080)
                return ResolutionType.Standard;
            else if (screenSize.Y <= 1440)
                return ResolutionType.High;
            else
                return ResolutionType.UltraHigh;
        }
        
        /// <summary>
        /// 根据分辨率类型获取UI缩放因子
        /// </summary>
        /// <param name="resolutionType">分辨率类型</param>
        /// <returns>UI缩放因子</returns>
        public static float GetUIScaleForResolution(ResolutionType resolutionType)
        {
            return resolutionType switch
            {
                ResolutionType.Low => 0.8f,
                ResolutionType.Standard => 1.0f,
                ResolutionType.High => 1.2f,
                ResolutionType.UltraHigh => 1.4f,
                _ => 1.0f
            };
        }
        
        /// <summary>
        /// 检查是否为超宽屏
        /// </summary>
        /// <returns>是否为超宽屏</returns>
        public static bool IsUltraWideScreen()
        {
            var screenSize = FlaxEngine.Screen.Size;
            var aspectRatio = screenSize.X / screenSize.Y;
            return aspectRatio >= 2.1f; // 21:9 或更宽
        }
        
        /// <summary>
        /// 为超宽屏计算居中位置（保持16:9显示区域）
        /// </summary>
        /// <param name="panelSize">面板尺寸</param>
        /// <returns>超宽屏适配的居中位置</returns>
        public static Float2 CalculateUltraWideCenterPosition(Float2 panelSize)
        {
            var screenSize = FlaxEngine.Screen.Size;
            
            if (IsUltraWideScreen())
            {
                // 在屏幕中央创建一个16:9的显示区域
                var targetWidth = screenSize.Y * (16f / 9f);
                var sideMargin = (screenSize.X - targetWidth) / 2f;
                
                return new Float2(
                    sideMargin + (targetWidth - panelSize.X) / 2f,
                    (screenSize.Y - panelSize.Y) / 2f
                );
            }
            
            return CalculateCenterPosition(panelSize);
        }
    }
}
