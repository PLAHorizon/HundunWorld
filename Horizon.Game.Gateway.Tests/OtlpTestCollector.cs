using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 6.3 P2⑩ — mock OTLP/HTTP collector：TcpListener 收 POST /v1/metrics，记录请求体，回 200。
/// 网关（<c>OtlpExporterTests</c>）与 Silo（<c>SiloOtlpExporterTests</c>）两侧 exporter 测试共用；
/// protobuf 体中指标名为明文 ASCII，可用 <see cref="AnyBodyContains"/> 断言。
/// </summary>
public sealed class OtlpTestCollector : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    public readonly List<string> RequestBodies = new();

    public int Port { get; }

    public OtlpTestCollector()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                await using var stream = client.GetStream();
                var body = await ReadHttpRequestAsync(stream);
                if (body is not null)
                {
                    lock (RequestBodies) RequestBodies.Add(body);
                }
                var response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                var bytes = Encoding.ASCII.GetBytes(response);
                try
                {
                    await stream.WriteAsync(bytes, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // 测试断开竞态：忽略
        }
        finally
        {
            _listener.Stop();
        }
    }

    /// <summary>读取 HTTP 请求：解析 header（Content-Length）后读 body；返回原始 body 文本。</summary>
    private static async Task<string?> ReadHttpRequestAsync(NetworkStream stream)
    {
        var headerBuffer = new List<byte>();
        var buf = new byte[1];
        int contentLength = -1;
        var headerDone = false;

        // 读 header 直到 \r\n\r\n
        while (!headerDone && headerBuffer.Count < 65536)
        {
            if (await stream.ReadAsync(buf) != 1) return null;
            headerBuffer.Add(buf[0]);
            if (headerBuffer.Count >= 4
                && headerBuffer[^4] == (byte)'\r'
                && headerBuffer[^3] == (byte)'\n'
                && headerBuffer[^2] == (byte)'\r'
                && headerBuffer[^1] == (byte)'\n')
            {
                headerDone = true;
            }
        }
        if (!headerDone) return null;

        var header = Encoding.ASCII.GetString(headerBuffer.ToArray());
        foreach (var line in header.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);
            }
        }
        if (contentLength <= 0) return null;

        var body = new byte[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, contentLength - read));
            if (n == 0) break;
            read += n;
        }
        // protobuf 二进制体：直接按 Latin-1 读出以便 Contains 断言指标名（ASCII 明文）。
        return Encoding.Latin1.GetString(body, 0, read);
    }

    public int RequestCount { get { lock (RequestBodies) return RequestBodies.Count; } }

    public bool AnyBodyContains(string needle)
    {
        lock (RequestBodies)
        {
            foreach (var b in RequestBodies)
            {
                if (b.Contains(needle, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}
