using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace UE5ToFlaxConverter.Core.Writers;

/// <summary>
/// JSON 序列化与 Flax Content 路径规范化辅助类。
/// 统一所有 Writer 的 JSON 输出格式（缩进、UTF-8、不输出 null 字段）。
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// 全局共享的 JSON 序列化选项（线程安全）。
    /// - 驼峰命名（与 Flax Engine 的 JsonAsset 默认风格一致）
    /// - 忽略 null 字段
    /// - 缩进 2 空格
    /// </summary>
    public static readonly JsonSerializerOptions Cached = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 将对象序列化为 JSON 并写入文件（UTF-8 无 BOM）。
    /// 自动创建父目录。
    /// </summary>
    public static async Task SerializeToFileAsync(object value, string path, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, value, Cached, ct);
    }

    /// <summary>
    /// 将任意路径规范化为 Flax Content 路径。
    /// 规则：
    /// - 替换反斜杠为正斜杠
    /// - 去掉前导斜杠
    /// - 若未以 "Content/" 开头，则补 "Content/" 前缀
    /// </summary>
    public static string ToFlaxContentPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "Content/";
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("Content/", System.StringComparison.OrdinalIgnoreCase))
            return normalized;
        return "Content/" + normalized;
    }
}
