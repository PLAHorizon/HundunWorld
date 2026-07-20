using System.CommandLine;
using Serilog;
using UE5ToFlaxConverter.Core.Mappers;
using UE5ToFlaxConverter.Core.Pipeline;
using UE5ToFlaxConverter.Core.Readers;

namespace UE5ToFlaxConverter.Cli;

/// <summary>
/// CLI 入口。命令示例：
///   ue52flax scan --ue5 "D:/UE5Project/Content" -o scan.json
///   ue52flax convert --ue5 "D:/UE5Project/Content" --output "./_preview" --profile preview
///   ue52flax convert --ue5 "D:/UE5Project/Content" --filter "GA_*.uasset" --output "C:/.../Content" --profile apply
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code)
            .CreateLogger();

        var ue5Option = new Option<string>("--ue5")
        {
            Description = "UE5 Content 或 Paks 目录路径",
            Required = true
        };
        var outputOption = new Option<string>("--output")
        {
            Description = "输出根目录路径",
            Required = true
        };
        var profileOption = new Option<string>("--profile")
        {
            Description = "输出 Profile: preview | apply",
            DefaultValueFactory = _ => "preview"
        };
        var filterOption = new Option<string?>("--filter")
        {
            Description = "资源过滤 glob 模式（如 GA_*.uasset）"
        };
        var typeFilterOption = new Option<string?>("--types")
        {
            Description = "资源类型过滤（逗号分隔: StaticMesh,AnimationSequence,...）"
        };
        var aesKeyOption = new Option<string?>("--aes")
        {
            Description = "AES 密钥（hex，可选；当前版本仅记录警告）"
        };
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "详细日志",
            DefaultValueFactory = _ => false
        };

        var scanCommand = new Command("scan", "扫描 UE5 目录下所有资源")
        {
            ue5Option, outputOption, filterOption, typeFilterOption, aesKeyOption, verboseOption
        };
        scanCommand.SetAction(async (parseResult, ct) =>
        {
            ApplyVerbose(parseResult.GetValue(verboseOption));
            return await RunScanAsync(
                parseResult.GetValue(ue5Option)!,
                parseResult.GetValue(outputOption)!,
                parseResult.GetValue(filterOption),
                parseResult.GetValue(typeFilterOption),
                ParseAes(parseResult.GetValue(aesKeyOption)),
                ct);
        });

        var convertCommand = new Command("convert", "执行批量转换")
        {
            ue5Option, outputOption, profileOption, filterOption, typeFilterOption, aesKeyOption, verboseOption
        };
        convertCommand.SetAction(async (parseResult, ct) =>
        {
            ApplyVerbose(parseResult.GetValue(verboseOption));
            return await RunConvertAsync(
                parseResult.GetValue(ue5Option)!,
                parseResult.GetValue(outputOption)!,
                parseResult.GetValue(profileOption)!,
                parseResult.GetValue(filterOption),
                parseResult.GetValue(typeFilterOption),
                ParseAes(parseResult.GetValue(aesKeyOption)),
                ct);
        });

        var root = new RootCommand("UE5 → Flax Engine 资源转换工具")
        {
            scanCommand,
            convertCommand
        };

        var config = new CommandLineConfiguration(root);
        return await config.InvokeAsync(args);
    }

    private static void ApplyVerbose(bool verbose)
    {
        if (verbose)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code)
                .CreateLogger();
        }
    }

    private static async Task<int> RunScanAsync(string ue5, string output, string? filter, string? types, byte[]? parseAes, CancellationToken ct)
    {
        var provider = new UassetProvider();
        try
        {
            provider.Initialize(ue5, parseAes);
            var assets = provider.ScanAssets();
            if (!string.IsNullOrEmpty(filter))
            {
                assets = assets.Where(a => MatchesGlob(a.SourcePath, filter)).ToList();
            }
            if (!string.IsNullOrEmpty(types))
            {
                var typeSet = types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => Enum.Parse<UE5ToFlaxConverter.Core.Models.AssetType>(t, true))
                    .ToHashSet();
                assets = assets.Where(a => typeSet.Contains(a.Type)).ToList();
            }

            Log.Information("扫描完成，共 {Count} 个资源", assets.Count);
            foreach (var grp in assets.GroupBy(a => a.Type).OrderByDescending(g => g.Count()))
                Log.Information("  {Type}: {Count}", grp.Key, grp.Count());

            var outputDir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(outputDir)) Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(assets,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct);
            Log.Information("扫描结果已保存到 {Path}", output);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "扫描失败");
            return 1;
        }
        finally
        {
            provider.Dispose();
        }
    }

    private static async Task<int> RunConvertAsync(string ue5, string output, string profile, string? filter, string? types, byte[]? aes, CancellationToken ct)
    {
        try
        {
            var rules = MappingRules.Load();
            var profileConfig = rules.GetProfile(profile);
            var actualOutput = string.IsNullOrEmpty(output) ? profileConfig.RootPath : output;
            if (string.IsNullOrEmpty(actualOutput))
            {
                Log.Error("输出路径为空，且 Profile 未配置 RootPath");
                return 1;
            }

            // 扫描（使用 using 确保释放）
            List<UE5ToFlaxConverter.Core.Models.AssetScanResult> assets;
            using (var provider = new UassetProvider())
            {
                provider.Initialize(ue5, aes);
                assets = provider.ScanAssets().ToList();
            }

            if (!string.IsNullOrEmpty(filter))
                assets = assets.Where(a => MatchesGlob(a.SourcePath, filter)).ToList();
            if (!string.IsNullOrEmpty(types))
            {
                var typeSet = types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => Enum.Parse<UE5ToFlaxConverter.Core.Models.AssetType>(t, true))
                    .ToHashSet();
                assets = assets.Where(a => typeSet.Contains(a.Type)).ToList();
            }

            if (assets.Count == 0)
            {
                Log.Warning("未找到匹配的资源");
                return 1;
            }

            Log.Information("准备转换 {Count} 个资源到 {Output}（Profile={Profile}）", assets.Count, actualOutput, profile);

            var context = new ConversionContext
            {
                UE5ContentPath = ue5,
                OutputRootPath = actualOutput,
                ProfileName = profile,
                GenerateReport = profileConfig.GenerateReport,
                BackupExisting = profileConfig.BackupExisting,
                AesKey = aes,
                Rules = rules,
                TagMapper = new GameplayTagMapper(),
                Progress = new Progress<ConversionProgress>(p =>
                {
                    Log.Information("[{Current}/{Total}] {Name} -> {Status} {Message}",
                        p.Current, p.Total, p.CurrentAsset, p.Status, p.Message);
                }),
                CancellationToken = ct
            };

            var pipeline = new ConversionPipeline();
            var result = await pipeline.ExecuteAsync(assets, context);

            Log.Information(result.Message);
            Log.Information("总耗时: {Elapsed:F2}s", result.Elapsed.TotalSeconds);
            Log.Information("总输出文件数: {Count}", result.Outputs.Sum(o => o.Files.Count));
            return result.Success ? 0 : 2;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("转换被取消");
            return 3;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "转换失败");
            return 1;
        }
    }

    private static byte[]? ParseAes(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        if (hex.Length % 2 != 0)
        {
            Log.Warning("AES 密钥长度不是偶数，已忽略");
            return null;
        }
        try
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
        catch (FormatException ex)
        {
            Log.Warning(ex, "AES 密钥包含非 hex 字符，已忽略");
            return null;
        }
    }

    private static bool MatchesGlob(string path, string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(path, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
