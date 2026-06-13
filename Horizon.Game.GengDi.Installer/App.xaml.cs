using System.Linq;
using System.Windows;

namespace Horizon.Game.GengDi.Installer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var win = new MainWindow();

            // 检测命令行参数：以 /uninstall 启动时显示卸载界面
            if (e.Args.Any(a =>
                a.Equals("/uninstall", System.StringComparison.OrdinalIgnoreCase)))
            {
                win.ShowUninstallPage();
            }

            win.Show();
        }
    }
}
