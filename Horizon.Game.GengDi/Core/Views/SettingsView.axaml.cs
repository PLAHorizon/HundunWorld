using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Horizon.Game.GengDi.Core.Animations;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += SettingsView_Loaded;
        }

        private async void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SettingsView_Loaded;
            ImplicitContentAnimationHelper.AttachSlideAndScale(this.FindControl<TransitioningContentControl>("InstallPathPreviewHost"));
            ImplicitContentAnimationHelper.AttachSlideAndScale(this.FindControl<TransitioningContentControl>("ThemePreviewHost"));

            if (DataContext is SettingsViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }

        private async void BrowseInstallPathButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel viewModel)
            {
                return;
            }

            if (TopLevel.GetTopLevel(this) is not Window window)
            {
                return;
            }

            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择自定义游戏安装目录",
                AllowMultiple = false
            });

            var selectedPath = folders.Count > 0 ? folders[0].Path.LocalPath : null;
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                viewModel.UseCustomInstallPathCommand.Execute(null);
                viewModel.SetCustomInstallPath(selectedPath);
                await viewModel.SaveSettingsAsync();
            }
        }
    }
}