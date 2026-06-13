using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Horizon.Game.GengDi.Installer.Pages
{
    public partial class CompletePage : UserControl
    {
        private readonly MainWindow _host;
        private readonly string     _installPath;

        public CompletePage(MainWindow host, string installPath)
        {
            _host        = host;
            _installPath = installPath;
            InitializeComponent();

            TxtInstallInfo.Text =
                $"耕地 已成功安装到：\n{installPath}";
        }

        // ── 立即启动 ──────────────────────────────────────────

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            string exe = Path.Combine(_installPath, "GengDi.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute  = true,
                    WorkingDirectory = _installPath
                });
            }
            Application.Current.Shutdown();
        }

        // ── 关闭安装程序 ──────────────────────────────────────

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
