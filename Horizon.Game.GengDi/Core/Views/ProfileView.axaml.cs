using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class ProfileView : UserControl
    {
        private Image _avatarPreviewImage;
        private ProfileViewModel _viewModel;
        // 当前预览的 Bitmap，用于在更新时释放旧资源
        private Bitmap _currentAvatarBitmap;

        public ProfileView()
        {
            InitializeComponent();
            Loaded += ProfileView_Loaded;
            Unloaded += ProfileView_Unloaded;
        }

        private async void ProfileView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= ProfileView_Loaded;

            _avatarPreviewImage = this.FindControl<Image>("AvatarPreviewImage");

            if (DataContext is ProfileViewModel viewModel)
            {
                _viewModel = viewModel;

                // 将文件选择命令注入 ViewModel，保持 ViewModel 的平台无关性
                viewModel.PickAvatarCommand = new RelayCommand(async () => await PickAvatarFileAsync());

                // 监听 Avatar 属性变化，实时刷新预览图
                viewModel.PropertyChanged += ViewModel_PropertyChanged;

                await viewModel.InitializeAsync();
                _ = RefreshAvatarPreviewAsync(viewModel.Avatar);
            }
        }

        private void ProfileView_Unloaded(object sender, RoutedEventArgs e)
        {
            // 取消事件订阅，防止内存泄漏
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProfileViewModel.Avatar))
            {
                _ = RefreshAvatarPreviewAsync((_viewModel)?.Avatar);
            }
        }

        private async Task PickAvatarFileAsync()
        {
            if (_viewModel == null)
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择头像图片",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" },
                        MimeTypes = new[] { "image/png", "image/jpeg", "image/bmp", "image/gif", "image/webp" }
                    }
                }
            });

            if (files == null || files.Count == 0)
            {
                return;
            }

            var sourceFile = files[0];
            var localPath = await CopyAvatarToAppDataAsync(sourceFile);
            if (string.IsNullOrEmpty(localPath))
            {
                return;
            }

            // 打开裁剪对话框（需要宿主窗口才能以模态方式弹出）
            if (topLevel is Window ownerWindow)
            {
                var cropWindow = new AvatarCropWindow(localPath);
                await cropWindow.ShowDialog(ownerWindow);

                // 如果用户确认了裁剪，使用裁剪后的图片并删除中间文件
                if (!string.IsNullOrEmpty(cropWindow.ResultPath))
                {
                    _viewModel.Avatar = cropWindow.ResultPath;
                    // 删除裁剪前的临时副本，避免孤立文件堆积
                    TryDeleteFile(localPath);
                }
                else
                {
                    _viewModel.Avatar = localPath;
                    if (!string.IsNullOrEmpty(cropWindow.ErrorMessage))
                    {
                        _viewModel.BasicInfoMessage = cropWindow.ErrorMessage;
                    }
                }
            }
            else
            {
                // 无法获取宿主窗口时直接使用原始图片
                _viewModel.Avatar = localPath;
            }
        }

        /// <summary>
        /// 将用户选择的图片复制到应用数据目录下的 avatars 子目录，
        /// 并返回目标文件的本地绝对路径。
        /// </summary>
        private static async Task<string> CopyAvatarToAppDataAsync(IStorageFile sourceFile)
        {
            try
            {
                var avatarDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HundunWorld",
                    "avatars");
                Directory.CreateDirectory(avatarDir);

                var ext = Path.GetExtension(sourceFile.Name).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext))
                    ext = ".png";

                var destFileName = $"{Guid.NewGuid():N}{ext}";
                var destPath = Path.Combine(avatarDir, destFileName);

                await using var sourceStream = await sourceFile.OpenReadAsync();
                await using var destStream = File.Create(destPath);
                await sourceStream.CopyToAsync(destStream);

                return destPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfileView] 复制头像文件失败: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 静默删除文件，忽略失败（文件可能已被移动或占用）。
        /// </summary>
        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfileView] 删除临时头像文件失败: {ex}");
            }
        }

        /// <summary>
        /// 异步加载头像预览图并更新 UI。返回 Task 以便调用方追踪异常。
        /// </summary>
        private async Task RefreshAvatarPreviewAsync(string avatarPath)
        {
            if (_avatarPreviewImage == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(avatarPath))
            {
                // 清除当前绑定。不要主动 Dispose：该 Bitmap 可能来自共享缓存并被其他控件复用。
                _currentAvatarBitmap = null;
                _avatarPreviewImage.Source = null;
                if (_viewModel != null)
                    _viewModel.AvatarIsLoaded = false;
                return;
            }

            // 在后台线程加载图片，加载完成后回到 UI 线程更新控件
            var bitmap = await PreviewImageService.Instance.LoadAsync(avatarPath);
            // 防止慢速加载覆盖更新后的头像
            if (!string.Equals((_viewModel)?.Avatar, avatarPath, StringComparison.Ordinal))
                return;

            // 回到 UI 线程更新控件（确保线程安全）
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 不主动释放旧 Bitmap，避免误释放共享缓存对象导致渲染线程访问已释放实例。
                _currentAvatarBitmap = bitmap;
                _avatarPreviewImage.Source = bitmap;
                if (_viewModel != null)
                    _viewModel.AvatarIsLoaded = bitmap != null;
            });
        }
    }
}
