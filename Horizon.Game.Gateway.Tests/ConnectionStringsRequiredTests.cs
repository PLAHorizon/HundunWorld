using System;
using Horizon.Game.Message.Configuration;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 0.1：连接字符串配置必需性校验（12-factor 集中配置）。
/// 缺失/空白必须抛清晰错误（含配置键、用途、环境变量提示），禁止静默回退本地默认端点。
/// </summary>
public sealed class ConnectionStringsRequiredTests
{
    [Fact]
    public void Require_NullValue_ThrowsWithKeyAndPurposeAndEnvHint()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConnectionStrings.Require(null, "Redis:ConnectionString", "Orleans 客户端 Redis 集群", "GATEWAY_REDIS_CONNECTION_STRING"));

        Assert.Contains("Redis:ConnectionString", ex.Message);
        Assert.Contains("Orleans 客户端 Redis 集群", ex.Message);
        Assert.Contains("GATEWAY_REDIS_CONNECTION_STRING", ex.Message);
    }

    [Fact]
    public void Require_WhitespaceValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ConnectionStrings.Require("   ", "DatabaseOptions:Game:ConnectionString", "游戏数据库"));
    }

    [Fact]
    public void Require_NonEmptyValue_ReturnsAsIs()
    {
        var result = ConnectionStrings.Require(
            "Server=prod-db;Database=Game;User Id=svc;Password=****",
            "DatabaseOptions:Game:ConnectionString", "游戏数据库");

        Assert.Equal("Server=prod-db;Database=Game;User Id=svc;Password=****", result);
    }

    [Fact]
    public void Require_MissingEnvHint_OmittedFromMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConnectionStrings.Require(null, "Redis:ConnectionString", "缓存"));

        Assert.DoesNotContain("环境变量", ex.Message);
    }
}
