using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace UE5ToFlaxConverter.UI;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Views.MainWindow
            {
                Width = 1200,
                Height = 800,
                MinWidth = 960,
                MinHeight = 640
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
