using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using Serilog;
using UE5ToFlaxConverter.Core.Models;
using UE5ToFlaxConverter.Core.Pipeline;
using UE5ToFlaxConverter.Core.Readers;
using UE5ToFlaxConverter.Core.Mappers;

namespace UE5ToFlaxConverter.UI.ViewModels;

public sealed class MainViewModel : ReactiveObject
{
    private string _ue5ContentPath = string.Empty;
    private string _outputPath = "./_conversion_preview";
    private string _profile = "preview";
    private string _filter = string.Empty;
    private bool _isScanning;
    private bool _isConverting;
    private double _progress;
    private string _statusText = "就绪";
    private string _logText = string.Empty;

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _convertCts;

    public MainViewModel()
    {
        // 使用 RxSuspendableCommand 可以更优雅地处理取消，但 ReactiveUI 11+ API 已简化。
        // 这里使用 CanExecute 与显式 CancellationTokenSource 组合实现可取消的命令。
        ScanCommand = ReactiveCommand.CreateFromTask(ScanAsync);
        ConvertCommand = ReactiveCommand.CreateFromTask(ConvertAsync);
        CancelConvertCommand = ReactiveCommand.Create(CancelConvert);
        BrowseUe5Command = ReactiveCommand.CreateFromTask(BrowseUe5);
        BrowseOutputCommand = ReactiveCommand.CreateFromTask(BrowseOutput);
    }

    public string Ue5ContentPath
    {
        get => _ue5ContentPath;
        set => this.RaiseAndSetIfChanged(ref _ue5ContentPath, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => this.RaiseAndSetIfChanged(ref _outputPath, value);
    }

    public string Profile
    {
        get => _profile;
        set => this.RaiseAndSetIfChanged(ref _profile, value);
    }

    public string Filter
    {
        get => _filter;
        set => this.RaiseAndSetIfChanged(ref _filter, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set => this.RaiseAndSetIfChanged(ref _isScanning, value);
    }

    public bool IsConverting
    {
        get => _isConverting;
        set => this.RaiseAndSetIfChanged(ref _isConverting, value);
    }

    public double Progress
    {
        get => _progress;
        set
        {
            var old = _progress;
            this.RaiseAndSetIfChanged(ref _progress, value);
            if (!old.Equals(value))
            {
                this.RaisePropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string LogText
    {
        get => _logText;
        set => this.RaiseAndSetIfChanged(ref _logText, value);
    }

    /// <summary>资源数量显示文本（如 "共 795 个资源"）。</summary>
    public string AssetsCountText => Assets.Count > 0 ? $"共 {Assets.Count} 个资源" : "暂无资源";

    /// <summary>进度百分比文本（如 "75.0%"）。</summary>
    public string ProgressText => $"{Progress:0.#}%";

    /// <summary>日志条数显示文本（如 "128 行"）。</summary>
    public string LogCountText => Logs.Count > 0 ? $"{Logs.Count} 行" : string.Empty;

    public ObservableCollection<AssetScanResult> Assets { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<string> Profiles { get; } = new() { "preview", "apply" };

    public ReactiveCommand<Unit, Unit> ScanCommand { get; }
    public ReactiveCommand<Unit, Unit> ConvertCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelConvertCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseUe5Command { get; }
    public ReactiveCommand<Unit, Unit> BrowseOutputCommand { get; }

    public Func<string, Task<string?>>? ShowOpenFolderDialog { get; set; }

    private async Task BrowseUe5()
    {
        if (ShowOpenFolderDialog == null) return;
        var path = await ShowOpenFolderDialog("选择 UE5 Content 目录");
        if (!string.IsNullOrEmpty(path)) Ue5ContentPath = path;
    }

    private async Task BrowseOutput()
    {
        if (ShowOpenFolderDialog == null) return;
        var path = await ShowOpenFolderDialog("选择输出目录");
        if (!string.IsNullOrEmpty(path)) OutputPath = path;
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(Ue5ContentPath) || !Directory.Exists(Ue5ContentPath))
        {
            AppendLog("[错误] UE5 Content 路径无效");
            return;
        }

        IsScanning = true;
        StatusText = "扫描中...";
        Assets.Clear();
        this.RaisePropertyChanged(nameof(AssetsCountText));
        AppendLog($"开始扫描: {Ue5ContentPath}");

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        try
        {
            // 在后台线程执行 IO，避免阻塞 UI。
            // ObservableCollection 必须在 UI 线程修改，所以批量返回结果后在 UI 线程添加。
            var filter = Filter?.Trim();
            var scanned = await Task.Run(() =>
            {
                using var provider = new UassetProvider();
                provider.Initialize(Ue5ContentPath);
                var assets = provider.ScanAssets().ToList();
                if (!string.IsNullOrEmpty(filter))
                {
                    assets = assets.Where(a => MatchesGlob(a.SourcePath, filter)).ToList();
                }
                return assets;
            }, _scanCts.Token);

            foreach (var a in scanned) Assets.Add(a);
            this.RaisePropertyChanged(nameof(AssetsCountText));

            StatusText = $"扫描完成: {Assets.Count} 个资源";
            AppendLog($"扫描完成: {Assets.Count} 个资源");
            foreach (var grp in Assets.GroupBy(a => a.Type).OrderByDescending(g => g.Count()))
                AppendLog($"  {grp.Key}: {grp.Count()}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消";
            AppendLog("[信息] 扫描已取消");
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 扫描失败: {ex.Message}");
            AppendLog($"[堆栈] {ex.StackTrace}");
            StatusText = "扫描失败";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void CancelConvert()
    {
        _convertCts?.Cancel();
    }

    private async Task ConvertAsync()
    {
        if (Assets.Count == 0)
        {
            AppendLog("[警告] 请先扫描资源");
            return;
        }

        var selected = Assets.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AppendLog("[警告] 未选中任何资源");
            return;
        }

        IsConverting = true;
        Progress = 0;
        AppendLog($"开始转换 {selected.Count} 个资源...");

        _convertCts?.Cancel();
        _convertCts = new CancellationTokenSource();

        try
        {
            var rules = MappingRules.Load();
            var profileConfig = rules.GetProfile(Profile);
            var context = new ConversionContext
            {
                UE5ContentPath = Ue5ContentPath,
                OutputRootPath = OutputPath,
                ProfileName = Profile,
                GenerateReport = profileConfig.GenerateReport,
                BackupExisting = profileConfig.BackupExisting,
                Rules = rules,
                TagMapper = new GameplayTagMapper(),
                Progress = new Progress<ConversionProgress>(OnProgress),
                CancellationToken = _convertCts.Token
            };

            var pipeline = new ConversionPipeline();
            var result = await pipeline.ExecuteAsync(selected, context);

            Progress = 100;
            StatusText = result.Success ? "转换完成" : "转换完成（含失败）";
            AppendLog(result.Message);
            AppendLog($"总耗时: {result.Elapsed.TotalSeconds:F2}s, 输出文件数: {result.Outputs.Sum(o => o.Files.Count)}");
            AppendLog($"报告: {Path.Combine(OutputPath, "conversion-report.json")}");
            if (rules.GetProfile(Profile).GenerateImportScript)
                AppendLog($"导入脚本: {Path.Combine(OutputPath, "import-to-flax.bat")}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "转换已取消";
            AppendLog("[信息] 转换已取消");
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 转换失败: {ex.Message}");
            AppendLog($"[堆栈] {ex.StackTrace}");
            if (ex.InnerException != null)
                AppendLog($"[内部异常] {ex.InnerException.Message}");
            StatusText = "转换失败";
        }
        finally
        {
            IsConverting = false;
        }
    }

    /// <summary>
    /// 进度回调。IProgress&lt;T&gt;.Capture 默认会捕获 SynchronizationContext，
    /// 但在 ReactiveUI 命令内部，回调可能仍由后台线程触发，因此显式使用 Dispatcher。
    /// </summary>
    private void OnProgress(ConversionProgress p)
    {
        // 使用 Dispatcher.Post 保证 UI 线程更新；如果已经在 UI 线程则直接同步调用。
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateProgressUI(p);
        }
        else
        {
            Dispatcher.UIThread.Post(() => UpdateProgressUI(p));
        }
    }

    private void UpdateProgressUI(ConversionProgress p)
    {
        Progress = (double)p.Current / Math.Max(1, p.Total) * 100;
        StatusText = $"[{p.Current}/{p.Total}] {p.CurrentAsset} - {p.Status}";
        AppendLog($"[{p.Current}/{p.Total}] {p.CurrentAsset}: {p.Status} {p.Message}");
    }

    private void AppendLog(string line)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {line}";
        // Logs 是 ObservableCollection，必须 UI 线程更新
        if (Dispatcher.UIThread.CheckAccess())
        {
            Logs.Add(entry);
            LogText += entry + Environment.NewLine;
            this.RaisePropertyChanged(nameof(LogCountText));
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                Logs.Add(entry);
                LogText += entry + Environment.NewLine;
                this.RaisePropertyChanged(nameof(LogCountText));
            });
        }
        Log.Information(line);
    }

    private static bool MatchesGlob(string path, string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(path, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
