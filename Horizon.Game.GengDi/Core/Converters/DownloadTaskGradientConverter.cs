using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Horizon.Game.GengDi.Core.Converters
{
    /// <summary>
    /// 将下载任务 GradientIndex（0-3）转换为对应的 LinearGradientBrush 资源。
    /// 用于在 XAML 中通过 {Binding GradientIndex, Converter={...}} 绑定图标容器背景。
    /// </summary>
    public class DownloadTaskGradientConverter : IValueConverter
    {
        public static readonly DownloadTaskGradientConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                var resourceKey = $"GdDlGradient{index}";
                if (Avalonia.Application.Current?.TryGetResource(resourceKey, null, out var resource) == true
                    && resource is IBrush brush)
                {
                    return brush;
                }
            }

            // 回退到第一个渐变
            if (Avalonia.Application.Current?.TryGetResource("GdDlGradient0", null, out var fallback) == true
                && fallback is IBrush fallbackBrush)
            {
                return fallbackBrush;
            }

            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
