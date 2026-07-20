using System.Diagnostics;
using UE5ToFlaxConverter.Core.Mappers;
using UE5ToFlaxConverter.Core.Models;
using UE5ToFlaxConverter.Core.Models.FlaxAssets;
using UE5ToFlaxConverter.Core.Readers;
using UE5ToFlaxConverter.Core.Writers;

namespace UE5ToFlaxConverter.Core.Pipeline;

/// <summary>
/// 转换上下文。封装一次批量转换所需的所有依赖和配置。
/// </summary>
public sealed class ConversionContext
{
    public required string UE5ContentPath { get; init; }
    public required string OutputRootPath { get; init; }
    public required MappingRules Rules { get; init; }
    public string ProfileName { get; init; } = "preview";
    public bool GenerateReport { get; init; } = true;
    public bool BackupExisting { get; init; } = false;
    public byte[]? AesKey { get; init; }
    public GameplayTagMapper TagMapper { get; init; } = new();
    public IProgress<ConversionProgress>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// 创建一个默认 ConversionContext，使用加载默认映射规则的 Rules。
    /// </summary>
    public static ConversionContext CreateDefault(string ue5ContentPath, string outputRootPath) =>
        new()
        {
            UE5ContentPath = ue5ContentPath,
            OutputRootPath = outputRootPath,
            Rules = MappingRules.Load()
        };
}

public sealed record ConversionProgress(
    int Current,
    int Total,
    string CurrentAsset,
    ConversionStatus Status,
    string? Message = null);

/// <summary>
/// 转换流水线。协调 Readers / Mappers / Writers 完成批量转换。
/// </summary>
public sealed class ConversionPipeline
{
    private readonly Func<ConversionContext, UassetProvider> _providerFactory;

    public ConversionPipeline(Func<ConversionContext, UassetProvider>? providerFactory = null)
    {
        _providerFactory = providerFactory ?? (ctx => new UassetProvider());
    }

    /// <summary>
    /// 执行批量转换。
    /// </summary>
    public async Task<BatchConversionResult> ExecuteAsync(
        IReadOnlyList<AssetScanResult> assets,
        ConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Rules is null)
            throw new ArgumentException("ConversionContext.Rules 不能为 null", nameof(context));

        var overallSw = Stopwatch.StartNew();
        var reports = new List<ConversionReport>();
        var outputs = new List<WriterOutput>();
        var registry = new FlaxAssetRegistry();
        var fbxImports = new List<FbxImportManifest>();

        // 初始化 Provider
        var provider = _providerFactory(context);
        try
        {
            provider.Initialize(context.UE5ContentPath, context.AesKey);
        }
        catch (Exception ex)
        {
            return new BatchConversionResult(
                reports,
                outputs,
                TimeSpan.FromTicks(overallSw.ElapsedTicks),
                false,
                $"Provider 初始化失败: {ex.Message}");
        }

        // 创建各 Reader/Writer（共享 registry，便于跨资源 GUID 引用解析）
        var meshReader = new MeshReader(provider);
        var animReader = new AnimationReader(provider);
        var particleReader = new ParticleReader(provider);
        var gasReader = new GasReader(provider, context.TagMapper);

        var modelWriter = new ModelWriter(context.OutputRootPath, registry);
        var animWriter = new AnimationWriter(context.OutputRootPath, registry);
        var particleWriter = new ParticleWriter(context.OutputRootPath, registry);
        var gasWriter = new GasWriter(context.OutputRootPath);

        int total = assets.Count;
        try
        {
            for (int i = 0; i < total; i++)
            {
                var asset = assets[i];
                if (context.CancellationToken.IsCancellationRequested) break;
                if (!asset.IsSelected) continue;

                context.Progress?.Report(new ConversionProgress(i + 1, total, asset.AssetName, ConversionStatus.Running));
                var report = new ConversionReport { SourcePath = asset.SourcePath };
                var sw = Stopwatch.StartNew();

                try
                {
                    WriterOutput? output = asset.Type switch
                    {
                        AssetType.StaticMesh => await WriteMeshAsync(modelWriter, meshReader, asset, report, context.CancellationToken, fbxImports),
                        AssetType.SkeletalMesh => await WriteSkinnedMeshAsync(modelWriter, meshReader, asset, report, context.CancellationToken, fbxImports),
                        AssetType.AnimationSequence or AssetType.AnimationMontage or AssetType.BlendSpace =>
                            await WriteAnimationAsync(animWriter, animReader, asset, report, context.CancellationToken, fbxImports),
                        AssetType.NiagaraSystem or AssetType.CascadeParticleSystem =>
                            await WriteParticleAsync(particleWriter, particleReader, asset, report, context.CancellationToken),
                        AssetType.GameplayAbility or AssetType.GameplayEffect or AssetType.AttributeSet =>
                            await WriteGasAsync(gasWriter, gasReader, asset, report, context.CancellationToken),
                        _ => await SkipUnknown(asset, report)
                    };

                    if (output != null)
                    {
                        outputs.Add(output);
                        report.TargetPath = output.TargetDirectory;
                        if (output.PendingManualSteps.Count > 0)
                        {
                            report.Status = ConversionStatus.PartialSuccess;
                            foreach (var step in output.PendingManualSteps)
                                report.Warn($"待手动完成: {step}");
                        }
                        else
                        {
                            report.Status = ConversionStatus.Success;
                        }
                    }
                    else
                    {
                        report.Status = ConversionStatus.Skipped;
                    }
                }
                catch (OperationCanceledException)
                {
                    report.Status = ConversionStatus.Skipped;
                    report.Warn("转换被取消");
                    throw;
                }
                catch (Exception ex)
                {
                    report.Status = ConversionStatus.Failed;
                    report.Error($"转换失败: {ex.Message}", ex);
                }

                sw.Stop();
                report.Elapsed = sw.Elapsed;
                reports.Add(report);

                context.Progress?.Report(new ConversionProgress(
                    i + 1, total, asset.AssetName, report.Status,
                    string.Join("; ", report.Messages.Take(2).Select(m => m.Text))));
            }
        }
        finally
        {
            provider.Dispose();
        }

        overallSw.Stop();

        // 生成 UE5AssetImporter.cs 主脚本（汇总所有 FBX 导入清单）
        try
        {
            var scriptWriter = new EditorScriptWriter(context.OutputRootPath, registry);
            var scriptOutput = await scriptWriter.WriteImporterScriptAsync(fbxImports, context.CancellationToken);
            outputs.Add(scriptOutput);

            var registryOutput = await scriptWriter.WriteRegistryAsync(context.CancellationToken);
            outputs.Add(registryOutput);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[警告] Editor 脚本生成失败: {ex.Message}");
        }

        // 异常容忍策略：仅当所有报告都未失败时视为成功
        var success = reports.All(r => r.Status != ConversionStatus.Failed);
        var message = success
            ? $"转换完成: {reports.Count(r => r.Status == ConversionStatus.Success)} 成功, {reports.Count(r => r.Status == ConversionStatus.PartialSuccess)} 部分成功, {reports.Count(r => r.Status == ConversionStatus.Failed)} 失败"
            : $"转换存在失败项: {reports.Count(r => r.Status == ConversionStatus.Failed)} 个";

        // 生成批处理报告
        if (context.GenerateReport)
        {
            try
            {
                await WriteBatchReportAsync(context.OutputRootPath, reports, outputs, overallSw.Elapsed);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[警告] 批处理报告生成失败: {ex.Message}");
            }
        }

        return new BatchConversionResult(reports, outputs, overallSw.Elapsed, success, message);
    }

    private static async Task<WriterOutput?> WriteMeshAsync(
        ModelWriter writer, MeshReader reader, AssetScanResult asset, ConversionReport report,
        CancellationToken ct, List<FbxImportManifest> fbxImports)
    {
        report.Info($"读取 StaticMesh: {asset.AssetName}");
        var mesh = reader.ReadStaticMesh(asset.SourcePath);
        report.Info($"LOD 数: {mesh.LODs.Count}, 材质数: {mesh.Materials.Count}");
        var output = await writer.WriteStaticMeshAsync(mesh, ct);
        CollectFbxImport(output, fbxImports);
        return output;
    }

    private static async Task<WriterOutput?> WriteSkinnedMeshAsync(
        ModelWriter writer, MeshReader reader, AssetScanResult asset, ConversionReport report,
        CancellationToken ct, List<FbxImportManifest> fbxImports)
    {
        report.Info($"读取 SkeletalMesh: {asset.AssetName}");
        var mesh = reader.ReadSkeletalMesh(asset.SourcePath);
        report.Info($"LOD 数: {mesh.LODs.Count}, 骨骼数: {mesh.Bones.Count}, Morph 数: {mesh.MorphTargets.Count}");
        var output = await writer.WriteSkeletalMeshAsync(mesh, ct);
        CollectFbxImport(output, fbxImports);
        return output;
    }

    private static async Task<WriterOutput?> WriteAnimationAsync(
        AnimationWriter writer, AnimationReader reader, AssetScanResult asset, ConversionReport report,
        CancellationToken ct, List<FbxImportManifest> fbxImports)
    {
        report.Info($"读取 Animation: {asset.AssetName}");
        var anim = reader.Read(asset.SourcePath);
        report.Info($"类型: {anim.Kind}, 时长: {anim.DurationSeconds:F2}s, 轨道: {anim.TrackBoneNames.Count}");
        var output = await writer.WriteAsync(anim, ct);
        CollectFbxImport(output, fbxImports);
        return output;
    }

    private static async Task<WriterOutput?> WriteParticleAsync(
        ParticleWriter writer, ParticleReader reader, AssetScanResult asset, ConversionReport report,
        CancellationToken ct)
    {
        report.Info($"读取 ParticleSystem: {asset.AssetName}");
        var ps = reader.Read(asset.SourcePath);
        report.Info($"发射器数: {ps.Emitters.Count}, 类型: {ps.Kind}");
        return await writer.WriteAsync(ps, ct);
    }

    private static async Task<WriterOutput?> WriteGasAsync(
        GasWriter writer, GasReader reader, AssetScanResult asset, ConversionReport report,
        CancellationToken ct)
    {
        report.Info($"读取 GAS: {asset.AssetName}");
        var gas = reader.Read(asset.SourcePath);
        switch (gas.Kind)
        {
            case GASKind.Ability when gas.Ability != null:
                report.Info($"Ability: tags={gas.Ability.AbilityTags.Count}, triggers={gas.Ability.Triggers.Count}");
                return await writer.WriteAbilityAsync(gas.Ability, ct);
            case GASKind.Effect when gas.Effect != null:
                report.Info($"Effect: modifiers={gas.Effect.Modifiers.Count}, duration={gas.Effect.DurationPolicy}");
                return await writer.WriteEffectAsync(gas.Effect, ct);
            case GASKind.AttributeSet when gas.AttributeSet != null:
                report.Info($"AttributeSet: attributes={gas.AttributeSet.Attributes.Count}");
                return await writer.WriteAttributeSetAsync(gas.AttributeSet, ct);
            default:
                report.Warn($"未识别的 GAS 子类型: {gas.Kind}");
                return null;
        }
    }

    /// <summary>
    /// 从 WriterOutput 中提取 FbxImportManifest（如果有）并加入汇总列表。
    /// </summary>
    private static void CollectFbxImport(WriterOutput? output, List<FbxImportManifest> fbxImports)
    {
        if (output == null) return;
        // 从输出目录中查找 import-manifest.json 文件
        try
        {
            if (!Directory.Exists(output.TargetDirectory)) return;
            var manifestPath = Path.Combine(output.TargetDirectory, "import-manifest.json");
            if (!File.Exists(manifestPath)) return;
            var json = File.ReadAllText(manifestPath);
            // JsonHelper.Cached 使用 camelCase 命名策略，反序列化也必须使用相同选项以匹配
            var manifest = System.Text.Json.JsonSerializer.Deserialize<FbxImportManifest>(json, JsonHelper.Cached);
            if (manifest != null && !string.IsNullOrEmpty(manifest.SourceFbxPath))
                fbxImports.Add(manifest);
        }
        catch { /* 忽略提取失败 */ }
    }

    private static Task<WriterOutput?> SkipUnknown(AssetScanResult asset, ConversionReport report)
    {
        report.Status = ConversionStatus.Skipped;
        report.Warn($"未支持的资源类型: {asset.Type}（{asset.AssetName}）");
        return Task.FromResult<WriterOutput?>(null);
    }

    private static async Task WriteBatchReportAsync(string outputRoot, List<ConversionReport> reports, List<WriterOutput> outputs, TimeSpan elapsed)
    {
        var report = new
        {
            generatedAt = DateTime.UtcNow,
            totalElapsed = elapsed.TotalSeconds,
            totalAssets = reports.Count,
            success = reports.Count(r => r.Status == ConversionStatus.Success),
            partialSuccess = reports.Count(r => r.Status == ConversionStatus.PartialSuccess),
            failed = reports.Count(r => r.Status == ConversionStatus.Failed),
            skipped = reports.Count(r => r.Status == ConversionStatus.Skipped),
            totalFiles = outputs.Sum(o => o.Files.Count),
            pendingManualSteps = outputs.SelectMany(o => o.PendingManualSteps).ToList(),
            assets = reports.Select(r => new
            {
                source = r.SourcePath,
                target = r.TargetPath,
                status = r.Status.ToString(),
                elapsedMs = r.Elapsed.TotalMilliseconds,
                messages = r.Messages
            })
        };
        var path = Path.Combine(outputRoot, "conversion-report.json");
        await JsonHelper.SerializeToFileAsync(report, path);
    }
}

/// <summary>
/// 批量转换结果。
/// </summary>
public sealed record BatchConversionResult(
    IReadOnlyList<ConversionReport> Reports,
    IReadOnlyList<WriterOutput> Outputs,
    TimeSpan Elapsed,
    bool Success,
    string Message);
