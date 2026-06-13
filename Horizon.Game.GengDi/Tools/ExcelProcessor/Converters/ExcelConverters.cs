using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Horizon.Game.GengDi.Tools.ExcelProcessor.Converters
{
    public class ProcessingButtonTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? "⏳ 处理中..." : "▶ 开始合并";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LogLevelColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Horizon.Game.GengDi.Tools.ExcelProcessor.Models.LogLevel level)
            {
                return level switch
                {
                    Horizon.Game.GengDi.Tools.ExcelProcessor.Models.LogLevel.Info => new SolidColorBrush(Color.Parse("#4A90D9")),
                    Horizon.Game.GengDi.Tools.ExcelProcessor.Models.LogLevel.Success => new SolidColorBrush(Color.Parse("#34A853")),
                    Horizon.Game.GengDi.Tools.ExcelProcessor.Models.LogLevel.Warning => new SolidColorBrush(Color.Parse("#FBBC04")),
                    Horizon.Game.GengDi.Tools.ExcelProcessor.Models.LogLevel.Error => new SolidColorBrush(Color.Parse("#EA4335")),
                    _ => new SolidColorBrush(Color.Parse("#666666"))
                };
            }
            return new SolidColorBrush(Color.Parse("#666666"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
