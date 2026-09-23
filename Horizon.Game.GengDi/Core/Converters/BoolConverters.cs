using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Horizon.Game.GengDi.Core.Converters
{
    public class BoolToMaxHeightConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? double.PositiveInfinity : 200.0;
            return 200.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "收起 ▲" : "展开 ▼";
            return "展开 ▼";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>bool → Opacity：true=1.0（当月），false=0.35（非当月日期置灰）。</summary>
    public class BoolToOpacityConverter : IValueConverter
    {
        public static BoolToOpacityConverter Instance { get; } = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool b && b ? 1.0 : 0.35;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 宽度下限钳制：返回 max(value, parameter)。
    /// 用于农事日历网格 —— 视口够宽时撑满自适应，不够宽时保住 7×168 最小宽度并出横向滚动。
    /// </summary>
    public class ClampToMinConverter : IValueConverter
    {
        public static ClampToMinConverter Instance { get; } = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double width && parameter != null
                && double.TryParse(parameter.ToString(), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var min))
            {
                return Math.Max(min, width);
            }
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int count)
                return count > 0;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EqualToZeroConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue == 0;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EqualToOneConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue == 1;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EqualToTwoConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue == 2;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EqualToThreeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue == 3;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToIntEqualOneConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue == 1;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool b && b ? 1 : 0;
        }
    }

    public class BoolToIntEqualZeroConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue == 0;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool b && b ? 0 : 1;
        }
    }

    public class MapLayerToForegroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string selected && parameter is string layer)
                return string.Equals(selected, layer, StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.Parse("#7AA9FF"))
                    : new SolidColorBrush(Color.Parse("#FFFFFF88"));
            return new SolidColorBrush(Color.Parse("#FFFFFF88"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HexToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
            {
                try
                {
                    var color = Color.Parse(hex);
                    return new SolidColorBrush(color);
                }
                catch
                {
                    return new SolidColorBrush(Colors.White);
                }
            }
            return new SolidColorBrush(Colors.White);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
