using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Converters
{
    public class WeatherIconConverter : IValueConverter
    {
        public static readonly WeatherIconConverter Instance = new WeatherIconConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string conditionCode)
            {
                return WeatherConditionIcons.GetIcon(conditionCode);
            }

            return WeatherConditionIcons.GetIcon("999");
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
