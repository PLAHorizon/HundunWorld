using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Versions;
using Microsoft.Extensions.Logging;
using UE5ToFlaxConverter.Core.Models;

namespace UE5ToFlaxConverter.Core.Readers;

/// <summary>
/// UE5 资源读取器入口。基于 CUE4Parse 的 DefaultFileProvider。
/// 支持 .pak/.ucas/.utoc/.uasset/.uexp 的混合加载。
/// </summary>
public sealed class UassetProvider : IDisposable
{
    private readonly ILogger<UassetProvider>? _logger;
    private DefaultFileProvider? _provider;
    private bool _initialized;
    private bool _disposed;
    private string _rootPath = string.Empty;

    public UassetProvider(ILogger<UassetProvider>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 初始化 UE5 内容目录。可传入 Content 根目录或 Paks 目录。
    /// </summary>
    /// <param name="ueContentOrPaksPath">UE5 Content 或 Paks 目录绝对路径。</param>
    /// <param name="aesKey">可选 AES 密钥，目前未实现加密 pak 解密；传入非空值会记录警告。</param>
    /// <param name="caseInsensitive">文件路径是否大小写不敏感。</param>
    /// <exception cref="ArgumentException">路径为空或不存在。</exception>
    public void Initialize(string ueContentOrPaksPath, byte[]? aesKey = null, bool caseInsensitive = true)
    {
        if (string.IsNullOrWhiteSpace(ueContentOrPaksPath))
            throw new ArgumentException("UE5 资源路径不能为空", nameof(ueContentOrPaksPath));
        if (!Directory.Exists(ueContentOrPaksPath))
            throw new DirectoryNotFoundException($"UE5 资源目录不存在: {ueContentOrPaksPath}");

        _rootPath = ueContentOrPaksPath;
        var versions = new VersionContainer(EGame.GAME_UE5_LATEST);
        _provider = new DefaultFileProvider(
            directory: ueContentOrPaksPath,
            searchOption: SearchOption.AllDirectories,
            isCaseInsensitive: caseInsensitive,
            versions: versions);
        _provider.Initialize();

        // AES 密钥：CUE4Parse 的密钥提交通过 SubmitKeys API 完成。
        // 本工具暂未实现 pak 解密流程；明确警告而非静默忽略，避免用户误以为已生效。
        if (aesKey is { Length: > 0 })
        {
            _logger?.LogWarning("已收到 AES 密钥（{Length} 字节），但当前版本未实现加密 pak 解密支持。", aesKey.Length);
        }

        _initialized = true;
        _logger?.LogInformation("UE5 资源提供器已初始化: {Path}", ueContentOrPaksPath);
    }

    /// <summary>
    /// 列出目录下所有 .uasset/.umap 资源（自动识别类型）。
    /// 类型识别策略：先按文件名启发式 GuessAssetType，若结果为 Unknown 则打开文件读 ExportType 精确识别。
    /// </summary>
    public IReadOnlyList<AssetScanResult> ScanAssets(string? subPath = null, Func<AssetType, bool>? typeFilter = null)
    {
        EnsureInitialized();
        var results = new List<AssetScanResult>();
        if (_provider == null) return results;

        // 规范化子路径：替换分隔符，移除前导斜杠
        var normalizedSubPath = string.IsNullOrEmpty(subPath)
            ? null
            : subPath.Replace("\\", "/").TrimStart('/');

        foreach (var kv in _provider.Files)
        {
            var path = kv.Key;
            if (!path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                continue;

            if (normalizedSubPath != null &&
                !path.Replace("\\", "/").Contains(normalizedSubPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var assetName = Path.GetFileNameWithoutExtension(path);
            var type = GuessAssetType(path);

            // 启发式未识别时，打开资源读取 ExportType 精确识别
            var ueClass = type.ToString();
            if (type == AssetType.Unknown)
            {
                try
                {
                    var inferred = InferTypeFromExport(path);
                    if (inferred.HasValue)
                    {
                        type = inferred.Value.type;
                        ueClass = inferred.Value.exportType;
                    }
                }
                catch
                {
                    // 资源读取失败时保持 Unknown
                }
            }

            if (typeFilter != null && !typeFilter(type)) continue;

            results.Add(new AssetScanResult
            {
                SourcePath = path,
                AssetName = assetName,
                Type = type,
                UEClass = ueClass,
                FileSizeBytes = TryGetFileLength(kv.Value),
                LastModified = DateTime.UtcNow
            });
        }

        return results;
    }

    /// <summary>
    /// 打开 .uasset 资源读取 ExportType，精确识别资源类型。
    /// 用于 GuessAssetType 启发式失败时的回退路径。
    /// </summary>
    private (AssetType type, string exportType)? InferTypeFromExport(string assetPath)
    {
        try
        {
            var objs = LoadAllObjects(assetPath);
            // 找第一个非辅助类型的 Export
            var auxiliary = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "BodySetup", "NavCollision", "MetaData", "PackageMetaData",
                "FbxStaticMeshImportData", "FbxSkeletalMeshImportData",
                "StaticMeshDescriptionBulkData", "HiResMeshDescription",
                "AssetUserData", "ThumbnailInfo", "Package"
            };
            foreach (var obj in objs)
            {
                if (string.IsNullOrEmpty(obj.ExportType)) continue;
                if (auxiliary.Contains(obj.ExportType)) continue;
                return MapExportTypeToAssetType(obj.ExportType);
            }
        }
        catch { }
        return null;
    }

    private static (AssetType type, string exportType) MapExportTypeToAssetType(string exportType)
    {
        // 严格按 ExportType 字符串映射到 AssetType 枚举
        if (string.Equals(exportType, "StaticMesh", StringComparison.OrdinalIgnoreCase))
            return (AssetType.StaticMesh, exportType);
        if (string.Equals(exportType, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
            return (AssetType.SkeletalMesh, exportType);
        if (string.Equals(exportType, "AnimSequence", StringComparison.OrdinalIgnoreCase))
            return (AssetType.AnimationSequence, exportType);
        if (string.Equals(exportType, "AnimMontage", StringComparison.OrdinalIgnoreCase))
            return (AssetType.AnimationMontage, exportType);
        if (string.Equals(exportType, "BlendSpace", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exportType, "BlendSpace1D", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exportType, "BlendSpaceBy", StringComparison.OrdinalIgnoreCase))
            return (AssetType.BlendSpace, exportType);
        if (string.Equals(exportType, "NiagaraSystem", StringComparison.OrdinalIgnoreCase))
            return (AssetType.NiagaraSystem, exportType);
        if (string.Equals(exportType, "NiagaraEmitter", StringComparison.OrdinalIgnoreCase))
            return (AssetType.NiagaraEmitter, exportType);
        if (string.Equals(exportType, "ParticleSystem", StringComparison.OrdinalIgnoreCase))
            return (AssetType.CascadeParticleSystem, exportType);
        if (string.Equals(exportType, "GameplayAbility", StringComparison.OrdinalIgnoreCase))
            return (AssetType.GameplayAbility, exportType);
        if (string.Equals(exportType, "GameplayEffect", StringComparison.OrdinalIgnoreCase))
            return (AssetType.GameplayEffect, exportType);
        if (string.Equals(exportType, "AttributeSet", StringComparison.OrdinalIgnoreCase))
            return (AssetType.AttributeSet, exportType);
        if (string.Equals(exportType, "Material", StringComparison.OrdinalIgnoreCase))
            return (AssetType.Material, exportType);
        if (string.Equals(exportType, "MaterialInstanceConstant", StringComparison.OrdinalIgnoreCase))
            return (AssetType.MaterialInstance, exportType);
        if (string.Equals(exportType, "Texture2D", StringComparison.OrdinalIgnoreCase))
            return (AssetType.Texture2D, exportType);
        if (string.Equals(exportType, "CurveTable", StringComparison.OrdinalIgnoreCase))
            return (AssetType.CurveTable, exportType);
        if (string.Equals(exportType, "DataTable", StringComparison.OrdinalIgnoreCase))
            return (AssetType.DataTable, exportType);
        if (string.Equals(exportType, "BlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase))
            return (AssetType.Blueprint, exportType);
        return (AssetType.Other, exportType);
    }

    /// <summary>
    /// 加载指定路径的所有 UObject（返回 .uasset 内的所有 Export 对象）。
    /// </summary>
    public IReadOnlyList<UObject> LoadAllObjects(string assetPath)
    {
        EnsureInitialized();
        if (_provider == null)
            throw new InvalidOperationException("Provider 未初始化");

        if (string.IsNullOrWhiteSpace(assetPath))
            throw new ArgumentException("资源路径不能为空", nameof(assetPath));

        var normalizedPath = assetPath.Replace("\\", "/");
        if (normalizedPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalizedPath = normalizedPath[..^7];
        else if (normalizedPath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            normalizedPath = normalizedPath[..^5];

        return _provider.LoadPackageObjects(normalizedPath).ToArray();
    }

    /// <summary>
    /// 加载主对象（资源本身的 Export，跳过辅助对象如 BodySetup/NavCollision/MetaData/BulkData）。
    ///
    /// 策略：
    /// 1. 按资源类型期望的 ExportType 名称精确匹配（如 StaticMesh → "StaticMesh"）
    /// 2. 若无匹配，退化到"第一个非 Package 对象"（旧行为）
    /// 3. 若都失败，返回第一个对象
    /// </summary>
    public UObject LoadObject(string assetPath, string? expectedExportType = null)
    {
        var objs = LoadAllObjects(assetPath);
        if (objs.Count == 0)
            throw new FileNotFoundException($"资源未找到或无法解析: {assetPath}");

        // 优先按期望的 ExportType 精确匹配
        if (!string.IsNullOrEmpty(expectedExportType))
        {
            foreach (var obj in objs)
            {
                if (string.Equals(obj.ExportType, expectedExportType, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }
        }

        // 自动推断：跳过辅助对象类型，找资源本身
        var auxiliaryTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BodySetup", "NavCollision", "MetaData", "PackageMetaData",
            "FbxStaticMeshImportData", "FbxSkeletalMeshImportData",
            "StaticMeshDescriptionBulkData", "HiResMeshDescription",
            "AssetUserData", "ThumbnailInfo"
        };
        foreach (var obj in objs)
        {
            if (string.IsNullOrEmpty(obj.ExportType)) continue;
            if (auxiliaryTypes.Contains(obj.ExportType)) continue;
            // 资源本身的 ExportType 通常与文件名前缀匹配（如 SM_xxx → StaticMesh）
            return obj;
        }

        // 退化：所有对象都是辅助类型时返回第一个
        return objs[0];
    }

    public DefaultFileProvider Provider
    {
        get
        {
            EnsureInitialized();
            return _provider!;
        }
    }

    public string RootPath => _rootPath;

    /// <summary>
    /// 根据文件路径启发式猜测资源类型。
    /// 使用 /Boundary/ 边界匹配避免短前缀（如 GA_）误匹配。
    /// </summary>
    public static AssetType GuessAssetType(string path)
    {
        if (string.IsNullOrEmpty(path)) return AssetType.Unknown;
        var p = path.Replace("\\", "/").ToUpperInvariant();

        // 使用路径开头 / 或 _ 作为前缀边界，避免 "Glass_" 等误匹配 "AS_"
        bool HasBoundary(string token)
            => p.StartsWith(token, StringComparison.Ordinal) ||
               p.Contains("/" + token, StringComparison.Ordinal) ||
               p.Contains("_" + token, StringComparison.Ordinal);

        // 长前缀优先（避免 GA_ 被 G 覆盖）。
        // Niagara/Cascade 必须在 GAS Effect 之前检查，避免 "/Effects/" 等路径误判。
        if (HasBoundary("SK_") || p.Contains("/SKM/", StringComparison.Ordinal) || p.Contains("SKELETAL", StringComparison.Ordinal))
            return AssetType.SkeletalMesh;
        if (HasBoundary("SM_") || p.Contains("/SM/", StringComparison.Ordinal) || p.Contains("STATIC", StringComparison.Ordinal))
            return AssetType.StaticMesh;
        if (HasBoundary("AM_") || p.Contains("MONTAGE", StringComparison.Ordinal))
            return AssetType.AnimationMontage;
        if (HasBoundary("BS_") || p.Contains("BLENDSPACE", StringComparison.Ordinal))
            return AssetType.BlendSpace;
        if (HasBoundary("ABP_") || p.Contains("ANIMBP", StringComparison.Ordinal))
            return AssetType.AnimationBlueprint;
        if (HasBoundary("NS_") || p.Contains("NIAGARA", StringComparison.Ordinal))
            return AssetType.NiagaraSystem;
        if (HasBoundary("PS_") || p.Contains("PARTICLE", StringComparison.Ordinal))
            return AssetType.CascadeParticleSystem;
        if (HasBoundary("GA_") || p.Contains("/ABILITY", StringComparison.Ordinal) || p.Contains("ABILITY", StringComparison.Ordinal))
            return AssetType.GameplayAbility;
        if (HasBoundary("GE_") || p.Contains("GAMEPLAYEFFECT", StringComparison.Ordinal))
            return AssetType.GameplayEffect;
        if (HasBoundary("AS_") || p.Contains("ATTRIBUTESET", StringComparison.Ordinal))
            return AssetType.AttributeSet;
        if (HasBoundary("M_") || p.Contains("MATERIAL", StringComparison.Ordinal))
            return AssetType.Material;
        if (HasBoundary("MI_") || p.Contains("MATERIALINSTANCE", StringComparison.Ordinal))
            return AssetType.MaterialInstance;
        if (HasBoundary("T_") || p.Contains("TEXTURE", StringComparison.Ordinal))
            return AssetType.Texture2D;
        if (p.EndsWith(".UMAP", StringComparison.Ordinal))
            return AssetType.Other;
        return AssetType.Unknown;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _provider?.Dispose();
        _initialized = false;
        _disposed = true;
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _provider == null)
            throw new InvalidOperationException("UassetProvider 尚未初始化，请先调用 Initialize");
        if (_disposed)
            throw new ObjectDisposedException(nameof(UassetProvider));
    }

    private static long TryGetFileLength(object gameFile)
    {
        try
        {
            // 优先使用强类型接口（如 IGameFile.HasUasset / Size），失败时退化到反射。
            var prop = gameFile.GetType().GetProperty("Size") ?? gameFile.GetType().GetProperty("Length");
            return prop?.GetValue(gameFile) is long size ? size : 0;
        }
        catch
        {
            return 0;
        }
    }
}
