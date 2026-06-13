using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Horizon.Game.GengDi.Core.Controls;

namespace Horizon.Game.GengDi.Core.Converters
{
    public class ToastBgConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ToastType type ? type switch
            {
                ToastType.Success => new SolidColorBrush(Color.FromRgb(0x2D, 0x50, 0x3E)),
                ToastType.Warning => new SolidColorBrush(Color.FromRgb(0x8B, 0x69, 0x14)),
                ToastType.Error => new SolidColorBrush(Color.FromRgb(0x6B, 0x2D, 0x2D)),
                _ => new SolidColorBrush(Color.FromRgb(0x3A, 0x50, 0x6B))
            } : Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class ToastBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ToastType type ? type switch
            {
                ToastType.Success => new SolidColorBrush(Color.FromRgb(0x3A, 0x6B, 0x4D)),
                ToastType.Warning => new SolidColorBrush(Color.FromRgb(0xA6, 0x7D, 0x1C)),
                ToastType.Error => new SolidColorBrush(Color.FromRgb(0x8B, 0x3A, 0x3A)),
                _ => new SolidColorBrush(Color.FromRgb(0x4A, 0x6A, 0x8B))
            } : Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class ToastFgConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
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
