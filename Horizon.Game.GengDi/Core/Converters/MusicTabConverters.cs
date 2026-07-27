using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Horizon.Game.GengDi.Core.Converters
{
    /// <summary>
    /// 当枚举值（如 SelectedRankingType）与 ConverterParameter 匹配时返回品牌主色背景，否则透明。
    /// 用于音乐排行榜 tab 的 active 态背景（设计稿 gd-tab-btn.is-active）。
    /// </summary>
    public class EnumMatchToBackgroundConverter : IValueConverter
    {
        public static readonly EnumMatchToBackgroundConverter Instance = new();
        private static readonly SolidColorBrush Active = new(Color.Parse("#FF2962FF"));
        private static readonly SolidColorBrush Inactive = new(Colors.Transparent);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && parameter != null && value.ToString() == parameter.ToString())
                return Active;
            return Inactive;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 当枚举值与 ConverterParameter 匹配时返回白色前景，否则返回 muted-foreground。
    /// 用于音乐排行榜 tab 的 active 态文字颜色。
    /// </summary>
    public class EnumMatchToForegroundConverter : IValueConverter
    {
        public static readonly EnumMatchToForegroundConverter Instance = new();
        private static readonly SolidColorBrush Active = new(Colors.White);
        private static readonly SolidColorBrush Inactive = new(Color.Parse("#FF787B86"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && parameter != null && value.ToString() == parameter.ToString())
                return Active;
            return Inactive;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
