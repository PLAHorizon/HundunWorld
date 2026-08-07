using System.Threading;

using Horizon.Game.GengDi.Core.Services.Call;

using Xunit.Abstractions;

namespace Horizon.Game.GengDi.Tests.Social;

/// <summary>
/// 摄像头采集管线诊断测试：逐环节输出 设备枚举 → 图构建/运行 → 帧回调 的实际结果，
/// 用于定位"视频通话无画面"的断点。本测试始终通过，仅输出诊断信息。
/// </summary>
public class CameraCaptureDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public CameraCaptureDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 测试图卡兑底源不依赖硬件，必须在短时间内稳定产出尺寸正确的帧（视频通话链路可用性的最后保障）。
    /// </summary>
    [Fact]
    public void TestPatternFallback_ProducesValidFrames()
    {
        using var pattern = new TestPatternVideoCapture();
        var count = 0;
        var validSize = false;
        pattern.FrameCaptured += (data, width, height, stride, topDown) =>
        {
            Interlocked.Increment(ref count);
            validSize = width == 640 && height == 480 && stride == 640 * 3 && data.Length == stride * height;
        };

        Assert.True(pattern.Start());
        Thread.Sleep(700);
        pattern.Stop();

        Assert.True(count > 0, "测试图卡应在 700ms 内产出帧");
        Assert.True(validSize, "测试图卡帧尺寸/stride 应正确");
    }

    [Fact]
    public void DiagnoseCameraPipeline()
    {
        // ========== 方案一：Media Foundation ==========
        _output.WriteLine($"[诊断-MF] mfplat/mfreadwrite 可用：{MediaFoundationVideoCapture.IsAvailable()}");

        var mfNames = MediaFoundationVideoCapture.EnumerateDeviceNames();
        _output.WriteLine($"[诊断-MF] 枚举到 {mfNames.Count} 个设备：{string.Join(" | ", mfNames)}");
        _output.WriteLine($"[诊断-MF] 枚举诊断：{(string.IsNullOrWhiteSpace(MediaFoundationVideoCapture.LastEnumerateDiagnostics) ? "无异常" : MediaFoundationVideoCapture.LastEnumerateDiagnostics)}");

        if (mfNames.Count > 0)
        {
            using var mfCapture = new MediaFoundationVideoCapture();
            var mfFrameCount = 0;
            var mfLastInfo = string.Empty;
            mfCapture.FrameCaptured += (data, width, height, stride, topDown) =>
            {
                Interlocked.Increment(ref mfFrameCount);
                mfLastInfo = $"width={width}, height={height}, stride={stride}, topDown={topDown}, bytes={data?.Length ?? 0}";
            };

            var mfStarted = mfCapture.Start();
            _output.WriteLine($"[诊断-MF] Start() = {mfStarted}, IsRunning = {mfCapture.IsRunning}, {mfCapture.FrameWidth}x{mfCapture.FrameHeight}");
            _output.WriteLine($"[诊断-MF] 启动诊断：{(string.IsNullOrWhiteSpace(MediaFoundationVideoCapture.LastStartDiagnostics) ? "无异常" : MediaFoundationVideoCapture.LastStartDiagnostics)}");

            if (mfStarted)
            {
                Thread.Sleep(3000);
                _output.WriteLine($"[诊断-MF] 3秒内收到帧数 = {mfFrameCount}");
                if (mfFrameCount > 0)
                {
                    _output.WriteLine($"[诊断-MF] 最近一帧：{mfLastInfo}");
                    _output.WriteLine("[诊断-MF] 结论：MF 采集管线正常出帧");
                }
                else
                {
                    _output.WriteLine("[诊断-MF] 结论：MF 图已运行但无帧（ReadSample 未返回样本）");
                }

                mfCapture.Stop();
                return;
            }
        }

        // ========== 方案二：DirectShow ==========
        var names = DirectShowVideoCapture.EnumerateDeviceNames();
        _output.WriteLine($"[诊断-DS] 枚举到 {names.Count} 个设备：{string.Join(" | ", names)}");
        _output.WriteLine($"[诊断-DS] 枚举诊断：{(string.IsNullOrWhiteSpace(DirectShowVideoCapture.LastEnumerateDiagnostics) ? "无异常" : DirectShowVideoCapture.LastEnumerateDiagnostics)}");

        if (names.Count == 0)
        {
            _output.WriteLine("[诊断] 结论：两种采集方案均枚举不到摄像头（隐私设置/设备禁用/无设备）");
            return;
        }

        using var capture = new DirectShowVideoCapture();
        var frameCount = 0;
        var lastFrameInfo = string.Empty;
        capture.FrameCaptured += (data, width, height, stride) =>
        {
            Interlocked.Increment(ref frameCount);
            lastFrameInfo = $"width={width}, height={height}, stride={stride}, bytes={data?.Length ?? 0}";
        };

        var started = capture.Start();
        _output.WriteLine($"[诊断-DS] capture.Start() = {started}");
        _output.WriteLine($"[诊断-DS] IsRunning = {capture.IsRunning}, FrameWidth = {capture.FrameWidth}, FrameHeight = {capture.FrameHeight}");
        _output.WriteLine($"[诊断-DS] 启动诊断：{(string.IsNullOrWhiteSpace(DirectShowVideoCapture.LastStartDiagnostics) ? "无异常" : DirectShowVideoCapture.LastStartDiagnostics)}");

        if (!started)
        {
            _output.WriteLine("[诊断] 结论：采集图构建或运行失败（设备被占用/驱动异常/COM 互操作失败）");
            return;
        }

        Thread.Sleep(3000);
        _output.WriteLine($"[诊断-DS] 3秒内收到帧数 = {frameCount}");
        _output.WriteLine($"[诊断-DS] 连接格式：{(string.IsNullOrWhiteSpace(DirectShowVideoCapture.LastConnectedFormat) ? "未知" : DirectShowVideoCapture.LastConnectedFormat)}");
        if (frameCount > 0)
        {
            _output.WriteLine($"[诊断-DS] 最近一帧参数：{lastFrameInfo}");
            _output.WriteLine("[诊断] 结论：采集管线正常出帧，问题在编码/发送/UI 层");
        }
        else
        {
            _output.WriteLine("[诊断-DS] RGB24 模式无帧，尝试原生格式采集…");
        }

        capture.Stop();

        if (frameCount == 0)
        {
            // 原生格式实验：不强制 RGB24，直接采集摄像头原生格式，
            // 用于区分"驱动完全无法流送"与"RGB 转换链损坏"
            using var nativeCapture = new DirectShowVideoCapture();
            var nativeFrameCount = 0;
            var nativeLastLen = 0;
            nativeCapture.FrameCaptured += (data, width, height, stride) =>
            {
                Interlocked.Increment(ref nativeFrameCount);
                nativeLastLen = data?.Length ?? 0;
            };

            var nativeStarted = nativeCapture.Start(0, forceRgb24: false);
            _output.WriteLine($"[诊断-原生] Start() = {nativeStarted}");
            _output.WriteLine($"[诊断-原生] 启动诊断：{(string.IsNullOrWhiteSpace(DirectShowVideoCapture.LastStartDiagnostics) ? "无异常" : DirectShowVideoCapture.LastStartDiagnostics)}");
            _output.WriteLine($"[诊断-原生] 连接格式：{(string.IsNullOrWhiteSpace(DirectShowVideoCapture.LastConnectedFormat) ? "未知" : DirectShowVideoCapture.LastConnectedFormat)}");

            if (nativeStarted)
            {
                Thread.Sleep(3000);
                _output.WriteLine($"[诊断-原生] 3秒内收到帧数 = {nativeFrameCount}（最近一帧 {nativeLastLen} bytes）");
                if (nativeFrameCount > 0)
                {
                    _output.WriteLine("[诊断] 结论：摄像头能出帧，问题在 RGB 格式转换链，可改用原生格式采集修复");
                }
                else
                {
                    _output.WriteLine("[诊断] 结论：原生格式也无帧，驱动层无法流送（需重装驱动/外置摄像头）");
                }

                nativeCapture.Stop();
            }
        }
    }
}
