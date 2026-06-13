using System.Threading.Tasks;
using FluentAvalonia.UI.Controls;

namespace Horizon.Game.GengDi.Core.Controls
{
    /// <summary>
    /// 轻量级确认对话框，复用 <see cref="ContentDialog"/>（FluentAvalonia 已有组件），统一"取消下载 / 取消安装 / 卸载"等危险操作的 UI 确认流程。
    /// </summary>
    public static class ConfirmDialog
    {
        /// <summary>
        /// 弹出确认对话框，返回用户选择：true = 确认（Primary），false = 取消（Close）。
        /// </summary>
        public static async Task<bool> ShowAsync(string title, string message, string primaryText = "确定", string cancelText = "取消")
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = primaryText,
                CloseButtonText = cancelText,
                DefaultButton = ContentDialogButton.Close
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}
