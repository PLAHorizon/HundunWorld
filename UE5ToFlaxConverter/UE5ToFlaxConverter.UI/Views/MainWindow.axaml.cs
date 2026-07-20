using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using UE5ToFlaxConverter.UI.ViewModels;

namespace UE5ToFlaxConverter.UI.Views;

public sealed partial class MainWindow : ReactiveWindow<MainViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        // 异步显示文件夹选择对话框，避免 .GetAwaiter().GetResult() 同步阻塞 UI 线程导致死锁。
        ViewModel.ShowOpenFolderDialog = async prompt =>
        {
            var dialog = new OpenFolderDialog { Title = prompt };
            return await dialog.ShowAsync(this);
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}