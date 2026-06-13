using Avalonia.Data.Converters;
using System;

namespace Horizon.Game.GengDi.Core.Converters
{
    public class BooleanToClassConverter : IValueConverter
    {
        public static readonly BooleanToClassConverter Instance = new BooleanToClassConverter();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool booleanValue && booleanValue && parameter is string className)
            {
                return className;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}