using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Horizon.Game.GengDi.Core.Services;
using Xunit;

namespace Horizon.Game.GengDi.Tests.Services;

/// <summary>
/// SslConfiguration 自签证书锁定（pinning）测试：
/// 嵌入证书可加载、指纹比对正确、真实自签服务器可完成 TLS 握手。
/// </summary>
public sealed class SslPinnedCertificateTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("未找到仓库根目录");
    }

    private static X509Certificate2 LoadServerCertFromRepo()
    {
        var pemPath = Path.Combine(FindRepoRoot(), "Horizon.Game.GengDi", "Core", "Security", "server_cert.pem");
        return new X509Certificate2(File.ReadAllBytes(pemPath));
    }

    [Fact]
    public void EmbeddedPinnedCert_LoadsAndMatchesRepoPem()
    {
        // 嵌入资源可加载（非空 64 位十六进制 SHA-256 指纹）
        var thumbprint = PinnedServerCertificate.Thumbprint;
        Assert.Matches("^[0-9A-F]{64}$", thumbprint);

        // 与仓库 PEM 的指纹一致
        using var expected = LoadServerCertFromRepo();
        using var sha256 = SHA256.Create();
        var expectedHex = Convert.ToHexString(sha256.ComputeHash(expected.GetRawCertData()));
        Assert.Equal(expectedHex, thumbprint);

        // 同证书放行，其他证书不放行（临时生成的无关自签证书）
        Assert.True(PinnedServerCertificate.IsPinned(expected));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=not-our-server", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var other = request.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        Assert.False(PinnedServerCertificate.IsPinned(other));
    }

    [Fact]
    public async System.Threading.Tasks.Task TestHandler_CompletesTlsHandshake_WithSelfSignedServer()
    {
        // 服务器未启动时跳过（保持 CI 可移植）
        using (var tcpProbe = new System.Net.Sockets.TcpClient())
        {
            try
            {
                var connectTask = tcpProbe.ConnectAsync("192.168.1.78", 5101);
                var finished = await System.Threading.Tasks.Task.WhenAny(
                    connectTask, System.Threading.Tasks.Task.Delay(3000));
                if (finished != connectTask || !tcpProbe.Connected)
                {
                    return; // 开发服务器不在线，跳过活体验证
                }
            }
            catch
            {
                return;
            }
        }

        using var handler = SslConfiguration.CreateTestEnvironmentHandler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        // 只要拿到任意 HTTP 状态码即证明自签 TLS 握手成功（400/404/307 均可）
        using var response = await client.GetAsync("https://192.168.1.78:5101/FlowerSpecies/list");
        Assert.True((int)response.StatusCode > 0);
    }
}
