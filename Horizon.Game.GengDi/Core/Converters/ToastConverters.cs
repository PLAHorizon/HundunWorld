using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Horizon.Game.GengDi.Core.Controls;

namespace Horizon.Game.GengDi.Core.Converters
{
    /// <summary>
    /// Toast 背景色 Converter，使用 gd token 对应的 Dark 主题颜色值。
    /// Success/Warning/Error/Info 分别对应 gd 状态色 surface（12% 不透明度）。
    /// </summary>
    public class ToastBgConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ToastType type ? type switch
            {
                // GdSuccessSurface = #1A26A69A
                ToastType.Success => new SolidColorBrush(Color.FromArgb(0x1A, 0x26, 0xA6, 0x9A)),
                // GdWarningSurface = #1AFFA726
                ToastType.Warning => new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xA7, 0x26)),
                // GdErrorSurface = #1AEF5350
                ToastType.Error => new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x53, 0x50)),
                // GdInfoSurface = #1A2962FF
                _ => new SolidColorBrush(Color.FromArgb(0x1A, 0x29, 0x62, 0xFF))
            } : Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// Toast 边框色 Converter，使用 gd token 对应的状态色。
    /// </summary>
    public class ToastBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ToastType type ? type switch
            {
                // GdSuccess = #FF26A69A
                ToastType.Success => new SolidColorBrush(Color.FromRgb(0x26, 0xA6, 0x9A)),
                // GdWarning = #FFFFA726
                ToastType.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0xA7, 0x26)),
                // GdError = #FFEF5350
                ToastType.Error => new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                // GdInfo = #FF2962FF
                _ => new SolidColorBrush(Color.FromRgb(0x29, 0x62, 0xFF))
            } : Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// Toast 前景色 Converter，使用 GdForeground (#FFE0E6ED)。
    /// </summary>
    public class ToastFgConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // GdForeground = #FFE0E6ED
            return new SolidColorBrush(Color.FromRgb(0xE0, 0xE6, 0xED));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class ToastIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ToastType type ? type switch
            {
                ToastType.Success => "✓",
                ToastType.Warning => "⚠",
                ToastType.Error => "✕",
                _ => "ℹ"
            } : "ℹ";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
