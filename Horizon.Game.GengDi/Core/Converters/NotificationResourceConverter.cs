using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Horizon.Game.GengDi.Core.Converters
{
    /// <summary>
    /// 将资源 key 字符串解析为应用程序级动态资源对象（画刷或 StreamGeometry）。
    /// 用于在 XAML 中通过 {Binding IconGeometryKey, Converter={...}} 绑定通知项的图标和颜色。
    /// </summary>
    public sealed class NotificationResourceConverter : IValueConverter
    {
        public static readonly NotificationResourceConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string key || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (Avalonia.Application.Current?.TryGetResource(key, null, out var resource) == true)
            {
                return resource;
            }

            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
