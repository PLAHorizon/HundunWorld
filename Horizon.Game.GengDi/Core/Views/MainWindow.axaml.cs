using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Windowing;

namespace Horizon.Game.GengDi.Core.Views;

public partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        if (TitleBar != null)
        {
            TitleBar.ExtendsContentIntoTitleBar = true;
            TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;
        }
    }
}