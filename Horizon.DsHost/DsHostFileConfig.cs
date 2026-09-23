using System;
using System.IO;
using System.Text.Json;

namespace Horizon.DsHost;

/// <summary>
/// DsHost 配置文件（<c>appsettings.json</c>）读取。
/// <para>
/// <b>为什么用 <see cref="System.Text.Json"/> 手读而不是 <c>Microsoft.Extensions.Configuration</c></b>：
/// 本宿主刻意保持极小的依赖面（它是 DS 进程，不属于 Orleans 集群），
/// 而配置结构就是一小组标量，手读比引入配置栈 + 中央包管理登记更简单、更可审计。
/// </para>
/// <para>
/// <b>优先级</b>（高 → 低）：命令行参数 &gt; 环境变量 &gt; <c>appsettings.json</c> &gt; 内置默认。
/// 这样"配置固化在文件里、启动时不带参数"是常态，而临时压测/多实例部署仍可用参数覆盖。
/// </para>
/// <para>
/// 文件缺失或字段缺失都**不是错误**——缺什么就用下一层默认值，保证"没有配置文件也能起"。
/// </para>
/// </summary>
internal sealed class DsHostFileConfig
{
    internal sealed class OrleansSection
    {
        public string? ConnectionString { get; set; }
        public string? ClusterId { get; set; }
        public string? ServiceId { get; set; }
    }

    public string? DsId { get; set; }
    public long[]? ShardIds { get; set; }
    public int? ListenPort { get; set; }
    public string? ListenAddress { get; set; }
    public string? AdvertiseHost { get; set; }
    public string? Region { get; set; }
    public string? GatewayHost { get; set; }
    public int? GatewayPort { get; set; }
    public int? TicksPerSecond { get; set; }
    public int? HeartbeatSeconds { get; set; }
    public int? SpawnPointCount { get; set; }
    public OrleansSection? Orleans { get; set; }

    /// <summary>配置文件路径（诊断用）。</summary>
    public string? LoadedFrom { get; private set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 从宿主目录加载 <c>appsettings.json</c>；不存在则返回空配置（而非抛异常）。
    /// </summary>
    public static DsHostFileConfig Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return new DsHostFileConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            // 允许文件只写 "DsHost": { ... } 一层；也兼容直接平铺。
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            JsonElement section = doc.RootElement;
            if (doc.RootElement.TryGetProperty("DsHost", out var dsHost))
            {
                section = dsHost;
            }

            var cfg = section.Deserialize<DsHostFileConfig>(Options) ?? new DsHostFileConfig();
            cfg.LoadedFrom = path;
            return cfg;
        }
        catch (Exception)
        {
            // 配置文件坏了不应该让 DS 起不来——退回默认值，由日志暴露问题。
            return new DsHostFileConfig { LoadedFrom = path + "（解析失败，已忽略）" };
        }
    }
}
