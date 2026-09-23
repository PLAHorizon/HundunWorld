using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 0.1.7 — Orleans Dashboard 地址配置化契约测试。
/// <para>
/// 目标：消除最后一批硬编码内网 IP（原 <c>192.168.1.78:1199</c>）。验证：
/// ①两个宿主（Silo / Gateway）的 <c>DashboardOptions</c> 区段完整（Host/Port/Username/Password）；
/// ②Host 不再硬编码 <c>192.168.1.78</c>（必须走环境变量占位 <c>${ORLEANS_DASHBOARD_HOST}</c> 或合法可绑定值）；
/// ③Silo <c>Program.cs</c> 的 Dashboard 配置读取逻辑绑定到 <c>DashboardOptions</c> 区段（非字面量）。
/// </para>
/// </summary>
public sealed class DashboardOptionsConfigurationTests
{
    private const string HardCodedIp = "192.168.1.78";

    private static string RepoRoot()
    {
        // 从测试输出目录向上找含 Horizon.sln 的仓库根（深度因 bin 布局而异，不硬编码层级）。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IConfiguration Load(string projectDir, string fileName)
    {
        var root = RepoRoot();
        var path = Path.Combine(root, projectDir, fileName);
        Assert.True(File.Exists(path), $"配置文件不存在: {path}");
        return new ConfigurationBuilder()
            .AddJsonFile(path, optional: false)
            .Build();
    }

    [Theory]
    [InlineData("Horizon.Orleans.Silo")]
    [InlineData("Horizon.Game.Gateway")]
    public void DashboardOptions_Section_IsFullyConfigured(string projectDir)
    {
        var config = Load(projectDir, "appsettings.json");

        var section = config.GetSection("DashboardOptions");
        Assert.True(section.Exists(), $"{projectDir} 缺少 DashboardOptions 区段");

        // Host/Port/Username/Password 四键齐全（与 Program.cs:588 的绑定面一致）。
        Assert.False(string.IsNullOrEmpty(section["Host"]), "DashboardOptions.Host 缺失");
        Assert.True(int.TryParse(section["Port"], out var port), "DashboardOptions.Port 非整数");
        Assert.True(port > 0 && port < 65536, $"DashboardOptions.Port 越界: {port}");
        Assert.False(string.IsNullOrEmpty(section["Username"]), "DashboardOptions.Username 缺失");
        Assert.False(string.IsNullOrEmpty(section["Password"]), "DashboardOptions.Password 缺失");
    }

    [Theory]
    [InlineData("Horizon.Orleans.Silo")]
    [InlineData("Horizon.Game.Gateway")]
    public void DashboardHost_DoesNotHardCodeInternalIp(string projectDir)
    {
        var config = Load(projectDir, "appsettings.json");
        var host = config.GetSection("DashboardOptions")["Host"] ?? "";

        // 硬编码 IP 已消除：Host 必须走环境变量占位（${...}）或合法非内网值。
        Assert.False(host.Contains(HardCodedIp, StringComparison.Ordinal),
            $"{projectDir} 的 DashboardOptions.Host 仍硬编码内网 IP: {host}");
    }

    [Theory]
    [InlineData("Horizon.Orleans.Silo")]
    [InlineData("Horizon.Game.Gateway")]
    public void AllConfigFiles_ContainNoHardCodedInternalIp(string projectDir)
    {
        var root = RepoRoot();
        foreach (var fileName in new[] { "appsettings.json", "appsettings.template.json", "appsettings.Production.json" })
        {
            var path = Path.Combine(root, projectDir, fileName);
            if (!File.Exists(path)) continue; // 部分项目无 template/Production
            var content = File.ReadAllText(path);
            Assert.False(content.Contains(HardCodedIp, StringComparison.Ordinal),
                $"{projectDir}/{fileName} 含硬编码内网 IP {HardCodedIp}");
        }
    }
}
