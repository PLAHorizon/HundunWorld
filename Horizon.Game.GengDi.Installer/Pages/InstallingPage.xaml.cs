using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Horizon.Game.GengDi.Installer.Services;

namespace Horizon.Game.GengDi.Installer.Pages
{
    public partial class InstallingPage : UserControl
    {
        private readonly MainWindow _host;
        private readonly string     _installPath;

        // 安装过程中轮播展示的特性文案
        private static readonly (string Title, string Desc)[] Features =
        {
            ("一键发现好游戏",
             "海量游戏库精心整理，\n按类型、评分快速找到心仪作品。"),
            ("智能下载管理",
             "多线程加速下载，断点续传，\n随时掌握每款游戏的安装进度。"),
            ("云端存档同步",
             "换机不丢进度，云端自动备份，\n随时随地继续你的冒险。"),
        };

        private int _featureIndex;

        public InstallingPage(MainWindow host, string installPath)
        {
            _host        = host;
            _installPath = installPath;
            InitializeComponent();
        }

        // ── 页面加载后启动安装流程 ──────────────────────────

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ShowFeature(0);
            await RunInstallationAsync();
        }

        // ── 安装主流程 ────────────────────────────────────────

        private async Task RunInstallationAsync()
        {
            var progress = new Progress<(string Message, double Percent)>(report =>
            {
                TxtStatus.Text  = report.Message;
                TxtPercent.Text = $"{(int)(report.Percent * 100)}%";
                ProgressBar.Value = report.Percent * 100.0;

                // 每完成约 1/3 切换特性文案
                int newIndex = (int)(report.Percent * Features.Length);
                if (newIndex != _featureIndex && newIndex < Features.Length)
                {
                    _featureIndex = newIndex;
                    ShowFeature(_featureIndex);
                }
            });

            try
            {
                // 步骤 1：检测并按需安装 .NET 10
                if (!InstallationService.IsDotNet10Installed())
                {
                    await InstallationService.InstallDotNet10Async(
                        new Progress<(string, double)>(r =>
                        {
                            // .NET 安装占总进度 0-40%
                            ((IProgress<(string, double)>)progress)
                                .Report((r.Item1, r.Item2 * 0.40));
                        }));
                }
                else
                {
                    ((IProgress<(string, double)>)progress)
                        .Report((".NET 运行环境已就绪", 0.40));
                    await Task.Delay(400);
                }

                // 步骤 2：复制应用文件（在后台线程执行，避免 UI 卡顿）
                await InstallationService.InstallApplicationAsync(
                    _installPath,
                    new Progress<(string, double)>(r =>
                    {
                        // 文件复制占总进度 40-85%
                        ((IProgress<(string, double)>)progress)
                            .Report((r.Item1, 0.40 + r.Item2 * 0.45));
                    }));

                // 步骤 3：创建快捷方式
                ((IProgress<(string, double)>)progress)
                    .Report(("正在创建快捷方式...", 0.88));
                await Task.Run(() =>
                    InstallationService.CreateShortcuts(_installPath));

                // 步骤 4：注册卸载项（读取实际 Assembly 版本号）
                ((IProgress<(string, double)>)progress)
                    .Report(("正在注册应用信息...", 0.95));
                string appVersion =
                    Assembly.GetExecutingAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion
                    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                    ?? "1.0.0";
                // 截断可能附带的 git hash（e.g. "1.0.0+abc1234" → "1.0.0"）
                int plusIdx = appVersion.IndexOf('+');
                if (plusIdx > 0)
                    appVersion = appVersion.Substring(0, plusIdx);
                await Task.Run(() =>
                    InstallationService.RegisterUninstallEntry(_installPath, appVersion));

                // 完成
                ((IProgress<(string, double)>)progress)
                    .Report(("安装完成！", 1.0));
                await Task.Delay(600);

                _host.ShowCompletePage(_installPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"安装过程中出现错误：\n{ex.Message}\n\n" +
                    "请检查磁盘空间或网络连接后重试。",
                    "安装失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                _host.ShowWelcomePage();
            }
        }

        // ── 切换营销文案 ────────────────────────────────────

        private void ShowFeature(int index)
        {
            if (index < 0 || index >= Features.Length) return;
            TxtFeatureTitle.Text = Features[index].Title;
            TxtFeatureDesc.Text  = Features[index].Desc;
        }
    }
}
