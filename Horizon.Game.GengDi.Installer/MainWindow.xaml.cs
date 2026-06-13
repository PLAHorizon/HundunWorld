using System.Windows;
using System.Windows.Input;
using Horizon.Game.GengDi.Installer.Pages;

namespace Horizon.Game.GengDi.Installer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ShowWelcomePage();
        }

        // ── 页面导航 ────────────────────────────────────────

        public void ShowWelcomePage()
        {
            PageHost.Content = new WelcomePage(this);
        }

        public void ShowInstallingPage(string installPath)
        {
            PageHost.Content = new InstallingPage(this, installPath);
        }

        public void ShowUninstallPage()
        {
            PageHost.Content = new UninstallPage(this);
        }

        public void ShowCompletePage(string installPath)
        {
            PageHost.Content = new CompletePage(this, installPath);
        }

        // ── 窗口控制 ────────────────────────────────────────

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
