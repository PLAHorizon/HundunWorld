using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public class StorySectionTypeConverter : IValueConverter
    {
        public static StorySectionTypeConverter Instance { get; } = new StorySectionTypeConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string type && parameter is string expectedType)
            {
                return type == expectedType;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringNotEmptyConverter : IValueConverter
    {
        public static StringNotEmptyConverter Instance { get; } = new StringNotEmptyConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool result = value is string str && !string.IsNullOrWhiteSpace(str);
            if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
            {
                result = !result;
            }
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class MusicStoryDrawerView : UserControl
    {
        public MusicStoryDrawerView()
        {
            InitializeComponent();
            DataContext = MusicStoryViewModel.Instance;
        }
    }
}
