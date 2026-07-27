using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Horizon.Game.GengDi.Core.Converters
{
    /// <summary>
    /// 将预警级别（danger/warning/info）映射为设计稿 alert-icon-* 的表面色背景。
    /// danger → error-surface，warning → warning-surface，其他 → info-surface。
    /// </summary>
    public class AlertLevelToSurfaceConverter : IValueConverter
    {
        public static readonly AlertLevelToSurfaceConverter Instance = new();
        private static readonly SolidColorBrush ErrorSurface = new(Color.Parse("#1FEF5350"));
        private static readonly SolidColorBrush WarningSurface = new(Color.Parse("#1FFF9800"));
        private static readonly SolidColorBrush InfoSurface = new(Color.Parse("#1F2962FF"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value as string switch
            {
                "danger" => ErrorSurface,
                "warning" => WarningSurface,
                _ => InfoSurface
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
