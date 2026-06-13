using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Horizon.Game.GengDi.Installer.Pages
{
    public partial class WelcomePage : UserControl
    {
        private readonly MainWindow _host;

        // EULA 文本（可替换为读取嵌入资源文件）
        private const string EulaText =
            "耕地游戏中心 用户许可协议\n\n" +
            "请在使用本软件前仔细阅读以下条款。安装即表示您同意受本协议约束。\n\n" +
            "1. 授权范围\n   本软件仅供个人非商业用途使用。\n\n" +
            "2. 限制\n   您不得反编译、逆向工程或以任何方式修改本软件。\n\n" +
            "3. 免责声明\n   本软件按\"现状\"提供，不提供任何明示或暗示的担保。\n\n" +
            "4. 终止\n   违反本协议任何条款将自动终止您的授权。\n\n" +
            "© HundunWorld  保留所有权利。";

        public WelcomePage(MainWindow host)
        {
            _host = host;
            InitializeComponent();
            TxtInstallPath.Text = DefaultInstallPath();
        }

        // ── 默认安装路径（无需管理员权限）────────────────────

        private static string DefaultInstallPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GengDi");
        }

        // ── 勾选/取消协议时切换按钮状态 ──────────────────────

        private void ChkEula_Changed(object sender, RoutedEventArgs e)
        {
            BtnInstall.IsEnabled = ChkEula.IsChecked == true;
        }

        // ── 浏览文件夹 ────────────────────────────────────────

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description       = "选择耕地的安装目录",
                SelectedPath      = TxtInstallPath.Text,
                ShowNewFolderButton = true
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtInstallPath.Text = dlg.SelectedPath;
        }

        // ── 查看协议 ──────────────────────────────────────────

        private void HypEula_Click(object sender,
            System.Windows.RoutedEventArgs e)
        {
            MessageBox.Show(EulaText, "用户许可协议",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ── 开始安装 ──────────────────────────────────────────

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            string installPath = TxtInstallPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(installPath))
            {
                MessageBox.Show("请选择安装目录。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _host.ShowInstallingPage(installPath);
        }
    }
}
