using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace UE5ToFlaxConverter.Core.Models.FlaxAssets;

/// <summary>
/// Flax 风格 GUID 生成器：生成 32 位十六进制字符串（无连字符，小写）。
/// 资源 GUID 必须全局唯一。
/// </summary>
public static class FlaxGuid
{
    /// <summary>
    /// 生成新的 Flax 风格 GUID 字符串（32 位十六进制小写）。
    /// </summary>
    public static string NewGuid()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        // 设置 RFC 4122 版本位（版本 4）
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>空 GUID（Flax 空引用）。</summary>
    public static string Empty => FlaxAssetFile.NullGuid;

    /// <summary>
    /// 基于 UE5 资源路径生成确定性 GUID（同一资源多次转换结果一致，便于增量更新）。
    /// </summary>
    public static string FromPath(string ue5AssetPath)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ue5AssetPath.ToLowerInvariant()));
        var bytes = new byte[16];
        Array.Copy(hash, 0, bytes, 0, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>
/// 坐标系映射：UE5 → Flax。
/// UE5 Z(上下) → Flax Y(上下)；UE5 Y(前后) → Flax Z(前后)；UE5 X(右) → Flax X(右)。
/// </summary>
public static class CoordinateMapper
{
    /// <summary>UE5 位置 → Flax 位置。</summary>
    public static FlaxVector3 ToFlaxPosition(double ue5X, double ue5Y, double ue5Z)
        => new(ue5X, ue5Z, ue5Y);

    /// <summary>UE5 四元数 → Flax 四元数（交换 Y 和 Z 分量，X 和 W 不变）。</summary>
    public static FlaxQuaternion ToFlaxRotation(double ue5Qx, double ue5Qy, double ue5Qz, double ue5Qw)
        => new(ue5Qx, ue5Qz, ue5Qy, ue5Qw);

    /// <summary>UE5 缩放 → Flax 缩放（交换 Y 和 Z）。</summary>
    public static FlaxVector3 ToFlaxScale(double ue5X, double ue5Y, double ue5Z)
        => new(ue5X, ue5Z, ue5Y);
}

/// <summary>
/// 资源 GUID 注册表：记录转换过程中生成的所有资源 GUID，
/// 用于 prefab/scene 中的引用解析（如 SkinnedModel 引用、Material 引用等）。
/// </summary>
public sealed class FlaxAssetRegistry
{
    private readonly Dictionary<string, FlaxAssetEntry> _bySourcePath = new();
    private readonly Dictionary<string, FlaxAssetEntry> _byGuid = new();

    public void Register(FlaxAssetEntry entry)
    {
        _bySourcePath[entry.SourcePath] = entry;
        _byGuid[entry.Guid] = entry;
    }

    public string ResolveGuid(string sourcePath)
        => _bySourcePath.TryGetValue(sourcePath, out var e) ? e.Guid : FlaxAssetFile.NullGuid;

    public FlaxAssetEntry? FindByGuid(string guid)
        => _byGuid.TryGetValue(guid, out var e) ? e : null;

    public IReadOnlyCollection<FlaxAssetEntry> All => _byGuid.Values;
}

/// <summary>转换生成的 Flax 资源条目。</summary>
public sealed class FlaxAssetEntry
{
    public string Guid { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string FlaxAssetPath { get; set; } = string.Empty; // 相对 Content 的路径（含扩展名）
    public string TypeName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // Prefab/Material/ParticleSystem/Animation/...
}
