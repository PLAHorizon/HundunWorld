using System;
using System.Diagnostics;
using System.Threading;

namespace Horizon.Game.GengDi.Core.Services.Call
{
    /// <summary>
    /// 测试图卡虚拟视频源：当真实摄像头不可用（驱动异常/权限拦截/无设备）时作为兜底，
    /// 生成带动态元素的彩条测试画面，保证视频通话整条链路（编码/传输/显示）可用可验证。
    /// 由 VideoCallEngine 在真实摄像头启动失败后自动启用，并向用户明确提示。
    /// </summary>
    public sealed class TestPatternVideoCapture : IDisposable
    {
        private const int Width = 640;
        private const int Height = 480;
        private const int Stride = Width * 3;
        private const int FrameIntervalMs = 125; // 约 8fps，上层引擎再节流到 5fps

        private Thread _frameThread;
        private CancellationTokenSource _cts;
        private long _frameIndex;
        private bool _disposed;

        /// <summary>产生一帧 RGB24 数据（自顶向下）。</summary>
        public event Action<byte[], int, int, int, bool> FrameCaptured;

        public int FrameWidth => Width;

        public int FrameHeight => Height;

        public bool IsRunning { get; private set; }

        public bool Start(int deviceIndex = 0)
        {
            if (_disposed || IsRunning)
            {
                return IsRunning;
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;
            _frameThread = new Thread(() => FrameLoop(_cts.Token))
            {
                IsBackground = true,
                Name = "TestPatternVideoSource"
            };
            _frameThread.Start();
            return true;
        }

        public void Stop()
        {
            IsRunning = false;

            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            var thread = _frameThread;
            _frameThread = null;
            if (thread != null && thread.IsAlive)
            {
                try
                {
                    thread.Join(TimeSpan.FromMilliseconds(1000));
                }
                catch
                {
                }
            }

            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }

        private void FrameLoop(CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();

            while (!token.IsCancellationRequested && IsRunning)
            {
                try
                {
                    // 每帧交付独立副本，避免与异步编码线程竞争同一缓冲
                    var frame = new byte[Stride * Height];
                    RenderFrame(frame, _frameIndex++, stopwatch.ElapsedMilliseconds);
                    FrameCaptured?.Invoke(frame, Width, Height, Stride, true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TestPatternVideoCapture] 帧生成异常：{ex.Message}");
                }

                Thread.Sleep(FrameIntervalMs);
            }

            IsRunning = false;
        }

        /// <summary>
        /// 渲染彩条测试图：上部七色彩条 + 中部灰阶渐变 + 移动白色方块与边框闪烁，
        /// 便于肉眼确认画面在实时刷新、方向正确、链路正常。
        /// </summary>
        private static void RenderFrame(byte[] frame, long frameIndex, long elapsedMs)
        {
            // SMPTE 风格七色彩条（BGR 顺序）
            var bars = new byte[][]
            {
                new byte[] { 255, 255, 255 }, // 白
                new byte[] { 0, 255, 255 },   // 黄
                new byte[] { 255, 255, 0 },   // 青
                new byte[] { 0, 255, 0 },     // 绿
                new byte[] { 255, 0, 255 },   // 品红
                new byte[] { 0, 0, 255 },     // 红
                new byte[] { 255, 0, 0 }      // 蓝
            };

            var barBottom = Height * 2 / 3;
            var barWidth = Width / bars.Length;

            for (var y = 0; y < barBottom; y++)
            {
                var rowOffset = y * Stride;
                for (var x = 0; x < Width; x++)
                {
                    var bar = Math.Min(x / barWidth, bars.Length - 1);
                    var color = bars[bar];
                    var offset = rowOffset + x * 3;
                    frame[offset] = color[0];
                    frame[offset + 1] = color[1];
                    frame[offset + 2] = color[2];
                }
            }

            // 下部灰阶渐变条
            for (var y = barBottom; y < Height; y++)
            {
                var rowOffset = y * Stride;
                for (var x = 0; x < Width; x++)
                {
                    var level = (byte)(x * 255 / Width);
                    var offset = rowOffset + x * 3;
                    frame[offset] = level;
                    frame[offset + 1] = level;
                    frame[offset + 2] = level;
                }
            }

            // 移动白色方块：沿水平方向往复，证明画面实时刷新
            var boxSize = 80;
            var travel = Width - boxSize;
            var phase = (elapsedMs / 20) % (travel * 2);
            var boxX = (int)(phase < travel ? phase : travel * 2 - phase);
            var boxY = Height / 2 - boxSize / 2;

            for (var y = boxY; y < boxY + boxSize && y < Height; y++)
            {
                var rowOffset = y * Stride;
                for (var x = boxX; x < boxX + boxSize && x < Width; x++)
                {
                    var offset = rowOffset + x * 3;
                    frame[offset] = 255;
                    frame[offset + 1] = 255;
                    frame[offset + 2] = 255;
                }
            }

            // 边框每 0.5 秒闪烁一次，辅助确认时间轴推进
            var borderOn = (frameIndex / 4) % 2 == 0;
            if (borderOn)
            {
                for (var x = 0; x < Width; x++)
                {
                    SetWhite(frame, 0, x);
                    SetWhite(frame, Height - 1, x);
                }

                for (var y = 0; y < Height; y++)
                {
                    SetWhite(frame, y, 0);
                    SetWhite(frame, y, Width - 1);
                }
            }
        }

        private static void SetWhite(byte[] frame, int y, int x)
        {
            var offset = y * Stride + x * 3;
            frame[offset] = 255;
            frame[offset + 1] = 255;
            frame[offset + 2] = 255;
        }
    }
}
