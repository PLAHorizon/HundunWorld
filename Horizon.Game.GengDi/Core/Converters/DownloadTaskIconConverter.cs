using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Horizon.Game.GengDi.Core.Converters
{
    /// <summary>
    /// 将下载任务 IconKey 字符串转换为对应的 Lucide StreamGeometry 资源。
    /// 用于在 XAML 中通过 {Binding IconKey, Converter={...}} 绑定 Path.Data。
    /// </summary>
    public class DownloadTaskIconConverter : IValueConverter
    {
        public static readonly DownloadTaskIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string key && !string.IsNullOrEmpty(key))
            {
                var resourceKey = $"Lucide{char.ToUpperInvariant(key[0])}{key.Substring(1)}Geometry";
                if (Avalonia.Application.Current?.TryGetResource(resourceKey, null, out var resource) == true
                    && resource is StreamGeometry geometry)
                {
                    return geometry;
                }
            }

            // 回退到 download 图标
            if (Avalonia.Application.Current?.TryGetResource("LucideDownloadGeometry", null, out var fallback) == true
                && fallback is StreamGeometry fallbackGeometry)
            {
                return fallbackGeometry;
            }

            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
