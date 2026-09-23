using System;

namespace Horizon.Game.Message.Configuration;

/// <summary>
/// 连接字符串配置必需性校验（阶段 0.1 — 12-factor 集中配置）。
/// <para>
/// 消除"生产环境静默回退到本地默认端点"（<c>127.0.0.1:9379</c> / <c>Data Source=.;</c>）：
/// 配置缺失时在启动期抛出明确错误（含配置键、用途、环境变量名提示），
/// 而不是带着错误端点静默启动后运行时才暴露。开发环境若确需本地默认值，
/// 应在配置文件（appsettings.Development.json）显式声明，而非依赖代码内回退。
/// </para>
/// </summary>
public static class ConnectionStrings
{
    /// <summary>
    /// 校验连接字符串：缺失/空白时抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    /// <param name="rawValue">从配置读到的原始值（<c>IConfiguration[key]</c> 的结果，可为 null）。</param>
    /// <param name="configKey">配置键路径（如 <c>Redis:ConnectionString</c>），用于错误信息。</param>
    /// <param name="purpose">用途描述（如"Orleans 集群存储"），用于错误信息。</param>
    /// <param name="envVarHint">对应的环境变量名（如 <c>REDIS_CONNECTION_STRING</c>），缺失时提示。</param>
    /// <returns>非空白连接字符串。</returns>
    public static string Require(string? rawValue, string configKey, string purpose, string? envVarHint = null)
    {
        if (!string.IsNullOrWhiteSpace(rawValue))
            return rawValue!;

        var envHint = string.IsNullOrWhiteSpace(envVarHint) ? string.Empty : $"（或设置环境变量 {envVarHint}）";
        throw new InvalidOperationException(
            $"缺少必需配置 {configKey}（{purpose}）。" +
            $"请在 appsettings.json / appsettings.{{Environment}}.json 中配置该键{envHint}，" +
            "禁止使用代码内本地默认连接串（12-factor）。");
    }
}
