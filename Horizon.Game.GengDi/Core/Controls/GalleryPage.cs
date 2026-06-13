using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace Horizon.Game.GengDi.Core.Controls;

public class GalleryPage : ContentControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<GalleryPage, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<GalleryPage, string>(nameof(Subtitle), string.Empty);

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<GalleryPage, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<IconSource> IconSourceProperty =
        AvaloniaProperty.Register<GalleryPage, IconSource>(nameof(IconSource));

    public static readonly StyledProperty<object> HeaderActionContentProperty =
        AvaloniaProperty.Register<GalleryPage, object>(nameof(HeaderActionContent));

    public static readonly StyledProperty<bool> IsPageScrollingEnabledProperty =
        AvaloniaProperty.Register<GalleryPage, bool>(nameof(IsPageScrollingEnabled), true);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public IconSource IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public object HeaderActionContent
    {
        get => GetValue(HeaderActionContentProperty);
        set => SetValue(HeaderActionContentProperty, value);
    }

    public bool IsPageScrollingEnabled
    {
        get => GetValue(IsPageScrollingEnabledProperty);
        set => SetValue(IsPageScrollingEnabledProperty, value);
    }
}