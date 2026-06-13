using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Horizon.Game.GengDi.Installer.Services;
using Microsoft.Win32;

namespace Horizon.Game.GengDi.Installer.Pages
{
    public partial class UninstallPage : UserControl
    {
        private readonly MainWindow _host;
        private string _installPath;

        public UninstallPage(MainWindow host)
        {
            _host = host;
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // 从注册表读取安装路径
            _installPath = ReadInstallLocationFromRegistry()
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GengDi");

            TxtInstallPath.Text = $"将从以下路径卸载：\n{_installPath}";
        }

        private static string? ReadInstallLocationFromRegistry()
        {
            const string keyPath =
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\GengDi";
            try
            {
                using var key =
                    Registry.LocalMachine.OpenSubKey(keyPath)
                    ?? Registry.CurrentUser.OpenSubKey(keyPath);
                return key?.GetValue("InstallLocation") as string;
            }
            catch { return null; }
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            BtnUninstall.IsEnabled = false;
            PanelButtons.Visibility = Visibility.Collapsed;
            ProgressBar.Visibility = Visibility.Visible;
            TxtStatus.Visibility   = Visibility.Visible;

            var progress = new Progress<(string Message, double Percent)>(report =>
            {
                TxtStatus.Text    = report.Message;
                ProgressBar.Value = report.Percent * 100.0;
            });

            try
            {
                await InstallationService.UninstallAsync(_installPath, progress);

                MessageBox.Show(
                    "耕地游戏中心已成功卸载。",
                    "卸载完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"卸载过程中出现错误：\n{ex.Message}",
                    "卸载失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // 恢复按钮
                BtnUninstall.IsEnabled  = true;
                PanelButtons.Visibility = Visibility.Visible;
                ProgressBar.Visibility  = Visibility.Collapsed;
                TxtStatus.Visibility    = Visibility.Collapsed;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
