using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace Horizon.Game.GengDi.Core.Services.Call
{
    /// <summary>
    /// 视频通话引擎：摄像头采集 → 缩放/JPEG 编码 → UDP 分片发送；远端 JPEG 帧 → 显示事件。
    /// 采集失败、帧解码失败均通过事件上报，不中断通话（语音仍可继续）。
    /// </summary>
    public sealed class VideoCallEngine : IDisposable
    {
        private const int FrameIntervalMs = 200; // 约 5fps，兼顾画面流畅度与带宽占用
        private const int TargetWidth = 320;
        private const int TargetHeight = 240;
        private const long JpegQuality = 45L;

        private static readonly ImageCodecInfo JpegCodec = ResolveJpegCodec();

        private DirectShowVideoCapture _dsCapture;
        private MediaFoundationVideoCapture _mfCapture;
        private TestPatternVideoCapture _patternCapture;
        private readonly object _encodeSync = new();
        private CallMediaTransport _transport;
        private long _lastSendTick;
        private int _encoding;
        private int _encodedFrameCount;
        private int _droppedFrameNoticeShown;
        private volatile bool _cameraOff;
        private bool _disposed;

        /// <summary>本地预览帧（JPEG）。关闭摄像头或采集失败时不再产生。</summary>
        public event Action<byte[]> LocalPreviewFrame;

        /// <summary>远端画面帧（JPEG）。</summary>
        public event Action<byte[]> RemoteVideoFrame;

        /// <summary>视频设备/画面异常提示。</summary>
        public event Action<string> DeviceError;

        /// <summary>摄像头是否已成功启动（含测试图卡兑底）。</summary>
        public bool IsCameraRunning => _mfCapture?.IsRunning == true || _dsCapture?.IsRunning == true || _patternCapture?.IsRunning == true;

        /// <summary>当前是否正在使用测试图卡（真实摄像头不可用时的兑底画面）。</summary>
        public bool IsUsingTestPattern => _patternCapture?.IsRunning == true;

        /// <summary>摄像头是否被本端关闭（MediaState 信令会同步给对端）。</summary>
        public bool IsCameraOff => _cameraOff;

        /// <summary>关联媒体传输层（订阅远端视频帧）。</summary>
        public void Attach(CallMediaTransport transport)
        {
            _transport = transport;
            if (transport != null)
            {
                transport.VideoFrameReceived += OnRemoteFrameReceived;
            }
        }

        /// <summary>启动摄像头采集。优先 Media Foundation（不依赖 DirectShow 组件实例化），失败时回退 DirectShow。</summary>
        public bool StartCamera()
        {
            if (_disposed)
            {
                return false;
            }

            // 方案一：Media Foundation（部分系统 DirectShow 组件无法实例化，MF 更可靠）
            if (MediaFoundationVideoCapture.IsAvailable()
                && MediaFoundationVideoCapture.EnumerateDeviceNames().Count > 0)
            {
                var mfCapture = new MediaFoundationVideoCapture();
                mfCapture.FrameCaptured += OnMfFrameCaptured;

                if (mfCapture.Start())
                {
                    _mfCapture = mfCapture;
                    _cameraOff = false;
                    _encodedFrameCount = 0;
                    ScheduleNoFrameWatchdog();
                    return true;
                }

                mfCapture.FrameCaptured -= OnMfFrameCaptured;
                mfCapture.Dispose();
                System.Diagnostics.Debug.WriteLine("[VideoCallEngine] Media Foundation 采集启动失败，回退 DirectShow。");
            }

            // 方案二：DirectShow 兑底
            if (DirectShowVideoCapture.EnumerateDeviceNames().Count == 0)
            {
                var diagnostics = string.IsNullOrWhiteSpace(MediaFoundationVideoCapture.LastEnumerateDiagnostics)
                    ? string.Empty
                    : $"（{MediaFoundationVideoCapture.LastEnumerateDiagnostics}）";
                RaiseDeviceError(
                    "未检测到摄像头设备，视频画面不可用（语音仍可正常通话）。"
                    + $"{diagnostics} 请检查 Windows 设置 → 隐私和安全性 → 摄像头，开启\"允许桌面应用访问你的摄像头\"，并确认设备管理器中摄像头未被禁用。");
                return false;
            }

            var dsCapture = new DirectShowVideoCapture();
            dsCapture.FrameCaptured += OnDsFrameCaptured;

            if (!dsCapture.Start())
            {
                var dsDiagnostics = string.IsNullOrWhiteSpace(DirectShowVideoCapture.LastStartDiagnostics)
                    ? string.Empty
                    : $"（DirectShow：{DirectShowVideoCapture.LastStartDiagnostics}）";
                var mfDiagnostics = string.IsNullOrWhiteSpace(MediaFoundationVideoCapture.LastStartDiagnostics)
                    ? string.Empty
                    : $"（MediaFoundation：{MediaFoundationVideoCapture.LastStartDiagnostics}）";
                dsCapture.FrameCaptured -= OnDsFrameCaptured;
                dsCapture.Dispose();

                // 方案三：测试图卡兑底（真实摄像头不可用时保证视频通话链路可用，并明确告知用户）
                var pattern = new TestPatternVideoCapture();
                pattern.FrameCaptured += OnTestPatternFrameCaptured;
                if (pattern.Start())
                {
                    _patternCapture = pattern;
                    _cameraOff = false;
                    _encodedFrameCount = 0;
                    RaiseDeviceError(
                        "摄像头不可用，已切换为测试画面（视频通话仍可正常进行）。"
                        + "请检查：1) Windows 设置 → 隐私和安全性 → 摄像头，开启\"允许桌面应用访问你的摄像头\"；"
                        + "2) 摄像头驱动（Boot Camp 设备可能需要重装驱动）或改接外置 USB 摄像头。"
                        + dsDiagnostics + mfDiagnostics);
                    ScheduleNoFrameWatchdog();
                    return true;
                }

                pattern.FrameCaptured -= OnTestPatternFrameCaptured;
                pattern.Dispose();
                RaiseDeviceError(
                    "摄像头启动失败，视频画面不可用（语音仍可正常通话）。"
                    + dsDiagnostics + mfDiagnostics);
                return false;
            }

            _dsCapture = dsCapture;
            _cameraOff = false;
            _encodedFrameCount = 0;
            ScheduleNoFrameWatchdog();
            return true;
        }

        /// <summary>
        /// 画面异常检测：摄像头已启动但 5 秒内未产生任何有效帧时提示用户（设备被遮挡/驱动异常/分辨率不匹配等），
        /// 避免用户只看到黑屏而无任何反馈。
        /// </summary>
        private void ScheduleNoFrameWatchdog()
        {
            _ = System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
            {
                if (_disposed || !IsCameraRunning || _cameraOff)
                {
                    return;
                }

                if (_encodedFrameCount == 0)
                {
                    RaiseDeviceError("摄像头已启动但未产生有效画面，请检查摄像头是否被遮挡、被其他应用占用或驱动异常。");
                }
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        /// <summary>开启/关闭摄像头（关闭后停止发送与预览，但不释放设备，可快速恢复）。</summary>
        public void SetCameraEnabled(bool enabled)
        {
            _cameraOff = !enabled;
        }

        public void StopCamera()
        {
            try
            {
                var patternCapture = _patternCapture;
                _patternCapture = null;
                if (patternCapture != null)
                {
                    patternCapture.FrameCaptured -= OnTestPatternFrameCaptured;
                    patternCapture.Dispose();
                }

                var mfCapture = _mfCapture;
                _mfCapture = null;
                if (mfCapture != null)
                {
                    mfCapture.FrameCaptured -= OnMfFrameCaptured;
                    mfCapture.Dispose();
                }

                var dsCapture = _dsCapture;
                _dsCapture = null;
                if (dsCapture != null)
                {
                    dsCapture.FrameCaptured -= OnDsFrameCaptured;
                    dsCapture.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VideoCallEngine] 停止摄像头异常：{ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            var transport = _transport;
            if (transport != null)
            {
                transport.VideoFrameReceived -= OnRemoteFrameReceived;
                _transport = null;
            }

            StopCamera();
        }

        private void OnMfFrameCaptured(byte[] bgra, int width, int height, int stride, bool topDown)
        {
            HandleCapturedFrame(bgra, width, height, stride, bitsPerPixel: 32, topDown);
        }

        private void OnDsFrameCaptured(byte[] rgb24, int width, int height, int stride)
        {
            HandleCapturedFrame(rgb24, width, height, stride, bitsPerPixel: 24, topDown: false);
        }

        private void OnTestPatternFrameCaptured(byte[] rgb24, int width, int height, int stride, bool topDown)
        {
            HandleCapturedFrame(rgb24, width, height, stride, bitsPerPixel: 24, topDown);
        }

        private void HandleCapturedFrame(byte[] pixels, int width, int height, int stride, int bitsPerPixel, bool topDown)
        {
            if (_disposed || _cameraOff)
            {
                return;
            }

            var now = Environment.TickCount64;
            if (now - _lastSendTick < FrameIntervalMs)
            {
                return;
            }

            // 编码较慢时丢弃新帧，避免阻塞采集线程
            if (Interlocked.CompareExchange(ref _encoding, 1, 0) != 0)
            {
                return;
            }

            _lastSendTick = now;

            var data = pixels;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var jpeg = EncodeFrame(data, width, height, stride, bitsPerPixel, topDown);
                    if (jpeg == null)
                    {
                        if (Interlocked.Exchange(ref _droppedFrameNoticeShown, 1) == 0)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[VideoCallEngine] 帧数据无效被丢弃：{width}x{height} stride={stride} bpp={bitsPerPixel} length={data?.Length ?? 0}");
                        }
                        return;
                    }

                    Interlocked.Increment(ref _encodedFrameCount);
                    LocalPreviewFrame?.Invoke(jpeg);

                    var transport = _transport;
                    if (transport != null && !transport.SendVideoFrame(jpeg))
                    {
                        // 远端端点尚未就绪（Connecting 阶段），属正常时序，不提示错误
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VideoCallEngine] 帧编码失败：{ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _encoding, 0);
                }
            });
        }

        private static byte[] EncodeFrame(byte[] pixels, int width, int height, int stride, int bitsPerPixel, bool topDown)
        {
            if (JpegCodec == null || width <= 0 || height <= 0 || pixels == null
                || (bitsPerPixel != 24 && bitsPerPixel != 32)
                || pixels.Length < stride * height)
            {
                return null;
            }

            var pixelFormat = bitsPerPixel == 32 ? PixelFormat.Format32bppRgb : PixelFormat.Format24bppRgb;
            using var source = new Bitmap(width, height, pixelFormat);
            var rect = new Rectangle(0, 0, width, height);
            var bits = source.LockBits(rect, ImageLockMode.WriteOnly, pixelFormat);
            try
            {
                // DirectShow RGB24 为自底向上 DIB，需逐行倒序拷贝扶正；MF RGB32 为自顶向下，直接逐行拷贝
                var destStride = Math.Abs(bits.Stride);
                var rowBytes = Math.Min(stride, destStride);
                var tempRow = new byte[rowBytes];
                for (var y = 0; y < height; y++)
                {
                    var sourceRow = topDown ? y : (height - 1 - y);
                    Array.Copy(pixels, sourceRow * stride, tempRow, 0, rowBytes);
                    System.Runtime.InteropServices.Marshal.Copy(
                        tempRow, 0, bits.Scan0 + y * bits.Stride, rowBytes);
                }
            }
            finally
            {
                source.UnlockBits(bits);
            }

            using var scaled = new Bitmap(TargetWidth, TargetHeight, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.DrawImage(source, 0, 0, TargetWidth, TargetHeight);
            }

            using var stream = new MemoryStream();
            using (var encoderParams = new EncoderParameters(1))
            {
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                scaled.Save(stream, JpegCodec, encoderParams);
            }

            return stream.ToArray();
        }

        private void OnRemoteFrameReceived(byte[] jpeg)
        {
            if (_disposed || jpeg == null || jpeg.Length < 2)
            {
                return;
            }

            // 简单校验 JPEG 魔数，丢弃损坏帧
            if (jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            {
                return;
            }

            RemoteVideoFrame?.Invoke(jpeg);
        }

        private void RaiseDeviceError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoCallEngine] 设备异常：{message}");
            DeviceError?.Invoke(message);
        }

        private static ImageCodecInfo ResolveJpegCodec()
        {
            try
            {
                foreach (var codec in ImageCodecInfo.GetImageEncoders())
                {
                    if (string.Equals(codec.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        return codec;
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
