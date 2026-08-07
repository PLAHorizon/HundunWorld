using System;
using System.Collections.Generic;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Horizon.Game.GengDi.Core.Services.Call
{
    /// <summary>
    /// 通话音频引擎：麦克风采集（WASAPI）→ 统一为 16kHz 单声道 16bit PCM → 发送；
    /// 远端 PCM → 抖动缓冲 → 扬声器播放。
    /// 设备缺失/初始化失败通过 <see cref="DeviceError"/> 事件上报，不抛出到 UI 线程。
    /// </summary>
    public sealed class AudioCallEngine : IDisposable
    {
        private const int TargetSampleRate = 16000;
        private const int TargetBytesPerSample = 2;
        private const int EmitChunkSamples = 320; // 20ms @ 16kHz
        private const int PlaybackBufferSeconds = 2;
        private const int MaxBufferedBytesBeforeTrim = TargetSampleRate * TargetBytesPerSample / 2; // 500ms

        private readonly object _captureSync = new();
        private readonly object _playbackSync = new();

        private WasapiCapture _capture;
        private LinearResampler _resampler;
        private readonly List<byte> _emitBuffer = new(EmitChunkSamples * TargetBytesPerSample);

        private WaveOutEvent _playbackDevice;
        private BufferedWaveProvider _playbackProvider;

        private volatile bool _muted;
        private bool _disposed;

        /// <summary>采集到一段可发送的 PCM 数据（16kHz 单声道 16bit，约20ms/段）。</summary>
        public event Action<byte[]> CapturedAudio;

        /// <summary>音频设备异常提示（无麦克风、无声卡、设备初始化失败等）。</summary>
        public event Action<string> DeviceError;

        /// <summary>是否处于静音状态（静音时不产生采集输出）。</summary>
        public bool IsMuted => _muted;

        /// <summary>麦克风是否已成功启动。</summary>
        public bool IsCaptureRunning { get; private set; }

        /// <summary>扬声器播放是否已成功启动。</summary>
        public bool IsPlaybackRunning { get; private set; }

        /// <summary>设置静音/取消静音。</summary>
        public void SetMuted(bool muted)
        {
            _muted = muted;
        }

        /// <summary>
        /// 启动麦克风采集。设备缺失或初始化失败时通过 <see cref="DeviceError"/> 上报并返回 false。
        /// </summary>
        public bool StartCapture()
        {
            if (_disposed || IsCaptureRunning)
            {
                return IsCaptureRunning;
            }

            // 预检查：先区分"无设备"与"有设备但初始化失败（如权限拒绝）"，避免误导性提示
            if (!TryResolveDefaultCaptureDevice(out var deviceCheckError))
            {
                RaiseDeviceError(deviceCheckError);
                return false;
            }

            try
            {
                _capture = new WasapiCapture();
                var format = _capture.WaveFormat;

                if (!IsSupportedCaptureFormat(format))
                {
                    SafeDisposeCapture();
                    RaiseDeviceError($"麦克风格式不受支持（{format.Encoding} {format.SampleRate}Hz），请检查音频设备设置。");
                    return false;
                }

                _resampler = new LinearResampler(
                    format.SampleRate,
                    TargetSampleRate,
                    format.Channels,
                    format.Encoding == WaveFormatEncoding.IeeeFloat);

                _capture.DataAvailable += OnCaptureDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                IsCaptureRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                SafeDisposeCapture();
                RaiseDeviceError(ClassifyCaptureError(ex));
                return false;
            }
        }

        /// <summary>检查系统是否存在已启用的默认采集设备（区分无设备与权限问题）。</summary>
        private static bool TryResolveDefaultCaptureDevice(out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                return device != null;
            }
            catch (Exception ex)
            {
                if (IsAccessDenied(ex))
                {
                    errorMessage = BuildAccessDeniedMessage(ex);
                }
                else
                {
                    errorMessage = "未检测到可用的麦克风设备。请确认麦克风已连接并在系统声音设置中已启用，然后重试。";
                }

                return false;
            }
        }

        /// <summary>
        /// 按异常类型分类麦克风初始化失败原因，给出可操作的修复指引：
        /// E_ACCESSDENIED（0x80070005）通常是 Windows 麦克风隐私设置拦截了桌面应用访问。
        /// </summary>
        private static string ClassifyCaptureError(Exception ex)
        {
            if (IsAccessDenied(ex))
            {
                return BuildAccessDeniedMessage(ex);
            }

            // AUDCLNT_E_DEVICE_INVALIDATED：设备被拔出/禁用/失效
            if (ex.HResult == unchecked((int)0x88890004))
            {
                return "麦克风设备无效或已断开。请确认麦克风已连接并在系统声音设置中已启用，然后重试。";
            }

            return $"麦克风初始化失败：{ex.Message}";
        }

        private static bool IsAccessDenied(Exception ex)
        {
            var message = ex.Message ?? string.Empty;
            return ex.HResult == unchecked((int)0x80070005)
                || ex is UnauthorizedAccessException
                || message.Contains("0x80070005", StringComparison.OrdinalIgnoreCase)
                || message.Contains("E_ACCESSDENIED", StringComparison.OrdinalIgnoreCase)
                || message.Contains("拒绝访问", StringComparison.Ordinal)
                || message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildAccessDeniedMessage(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCallEngine] 麦克风访问被拒绝：{ex.Message}");
            return "麦克风访问被系统拒绝（E_ACCESSDENIED）。请打开 Windows 设置 → 隐私和安全性 → 麦克风，"
                + "开启\"麦克风访问\"，并确认\"允许桌面应用访问麦克风\"已开启；"
                + "同时检查麦克风未被其他应用独占，设置完成后重试。";
        }

        /// <summary>启动扬声器播放。无声卡或初始化失败时通过 <see cref="DeviceError"/> 上报并返回 false。</summary>
        public bool StartPlayback()
        {
            if (_disposed || IsPlaybackRunning)
            {
                return IsPlaybackRunning;
            }

            lock (_playbackSync)
            {
                try
                {
                    _playbackProvider = new BufferedWaveProvider(
                        new WaveFormat(TargetSampleRate, TargetBytesPerSample * 8, 1))
                    {
                        BufferDuration = TimeSpan.FromSeconds(PlaybackBufferSeconds),
                        DiscardOnBufferOverflow = true
                    };

                    _playbackDevice = new WaveOutEvent { DesiredLatency = 80 };
                    _playbackDevice.Init(_playbackProvider);
                    _playbackDevice.Play();
                    IsPlaybackRunning = true;
                    return true;
                }
                catch (Exception ex)
                {
                    SafeDisposePlayback();
                    RaiseDeviceError($"未检测到可用的音频播放设备：{ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>写入远端音频数据（16kHz 单声道 16bit）。缓冲过多时裁剪以控制延迟。</summary>
        public void PushRemoteAudio(byte[] pcmData)
        {
            if (_disposed || pcmData == null || pcmData.Length == 0 || !IsPlaybackRunning)
            {
                return;
            }

            lock (_playbackSync)
            {
                var provider = _playbackProvider;
                if (provider == null)
                {
                    return;
                }

                try
                {
                    // 抖动缓冲积压超过 500ms 时清空重建，避免通话延迟持续累积
                    if (provider.BufferedBytes > MaxBufferedBytesBeforeTrim)
                    {
                        provider.ClearBuffer();
                    }

                    provider.AddSamples(pcmData, 0, pcmData.Length);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioCallEngine] 播放远端音频失败：{ex.Message}");
                }
            }
        }

        public void StopCapture()
        {
            lock (_captureSync)
            {
                SafeDisposeCapture();
                IsCaptureRunning = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (_captureSync)
            {
                SafeDisposeCapture();
                IsCaptureRunning = false;
            }

            lock (_playbackSync)
            {
                SafeDisposePlayback();
                IsPlaybackRunning = false;
            }
        }

        private static bool IsSupportedCaptureFormat(WaveFormat format)
        {
            if (format == null || format.Channels <= 0 || format.SampleRate <= 0)
            {
                return false;
            }

            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                return true;
            }

            return format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16;
        }

        private void OnCaptureDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_disposed || e.Buffer == null || e.BytesRecorded <= 0)
            {
                return;
            }

            // 静音：丢弃采集数据（保持采集管线运行，取消静音后可立即恢复）
            if (_muted)
            {
                return;
            }

            lock (_captureSync)
            {
                if (_resampler == null)
                {
                    return;
                }

                try
                {
                    var outputSamples = _resampler.Process(e.Buffer, e.BytesRecorded);
                    if (outputSamples == null || outputSamples.Count == 0)
                    {
                        return;
                    }

                    foreach (var sample in outputSamples)
                    {
                        _emitBuffer.Add((byte)(sample & 0xFF));
                        _emitBuffer.Add((byte)((sample >> 8) & 0xFF));

                        if (_emitBuffer.Count >= EmitChunkSamples * TargetBytesPerSample)
                        {
                            CapturedAudio?.Invoke(_emitBuffer.ToArray());
                            _emitBuffer.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioCallEngine] 处理采集数据异常：{ex.Message}");
                }
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            IsCaptureRunning = false;
            if (e.Exception != null && !_disposed)
            {
                RaiseDeviceError($"麦克风采集中断：{e.Exception.Message}");
            }
        }

        private void RaiseDeviceError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCallEngine] 设备异常：{message}");
            DeviceError?.Invoke(message);
        }

        private void SafeDisposeCapture()
        {
            var capture = _capture;
            _capture = null;
            _resampler = null;
            _emitBuffer.Clear();

            if (capture != null)
            {
                try
                {
                    capture.DataAvailable -= OnCaptureDataAvailable;
                    capture.RecordingStopped -= OnRecordingStopped;
                    capture.StopRecording();
                }
                catch
                {
                }

                try
                {
                    capture.Dispose();
                }
                catch
                {
                }
            }
        }

        private void SafeDisposePlayback()
        {
            var device = _playbackDevice;
            _playbackDevice = null;
            _playbackProvider = null;

            if (device != null)
            {
                try
                {
                    device.Stop();
                }
                catch
                {
                }

                try
                {
                    device.Dispose();
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// 线性插值重采样器：任意采样率/任意声道（float32 或 PCM16）→ 16kHz 单声道 PCM16。
        /// 无外部依赖，保证在没有 MediaFoundation 组件的机器上也能工作。
        /// </summary>
        private sealed class LinearResampler
        {
            private readonly int _inputRate;
            private readonly int _outputRate;
            private readonly int _channels;
            private readonly bool _isFloat;
            private readonly List<short> _pendingInput = new();
            private double _fractionalIndex;

            public LinearResampler(int inputRate, int outputRate, int channels, bool isFloat)
            {
                _inputRate = inputRate;
                _outputRate = outputRate;
                _channels = Math.Max(1, channels);
                _isFloat = isFloat;
            }

            public List<short> Process(byte[] buffer, int bytesRecorded)
            {
                // 1. 解码为单声道 short 样本（float32 → 缩放；多声道 → 取平均）
                var sampleBytes = _isFloat ? 4 : 2;
                var frameSize = sampleBytes * _channels;
                var frameCount = bytesRecorded / frameSize;
                if (frameCount <= 0)
                {
                    return null;
                }

                for (var frame = 0; frame < frameCount; frame++)
                {
                    var frameOffset = frame * frameSize;
                    double sum = 0;

                    for (var channel = 0; channel < _channels; channel++)
                    {
                        var offset = frameOffset + channel * sampleBytes;
                        if (_isFloat)
                        {
                            var value = BitConverter.ToSingle(buffer, offset);
                            if (value > 1f) value = 1f;
                            else if (value < -1f) value = -1f;
                            sum += value * short.MaxValue;
                        }
                        else
                        {
                            sum += BitConverter.ToInt16(buffer, offset);
                        }
                    }

                    var mono = (int)(sum / _channels);
                    _pendingInput.Add((short)Math.Clamp(mono, short.MinValue, short.MaxValue));
                }

                // 2. 线性插值重采样；保留最后一个样本作为下一段的左边界
                var output = new List<short>();
                var ratio = (double)_inputRate / _outputRate;
                var available = _pendingInput.Count;

                while (_fractionalIndex + 1 < available)
                {
                    var index = (int)_fractionalIndex;
                    var fraction = _fractionalIndex - index;
                    var left = _pendingInput[index];
                    var right = _pendingInput[index + 1];
                    output.Add((short)(left + (right - left) * fraction));
                    _fractionalIndex += ratio;
                }

                var consumed = (int)Math.Floor(_fractionalIndex);
                if (consumed > 0 && consumed <= _pendingInput.Count)
                {
                    _pendingInput.RemoveRange(0, consumed);
                    _fractionalIndex -= consumed;
                }

                if (_pendingInput.Count > _inputRate * 2)
                {
                    // 防御性清理：积压超过 2 秒时丢弃旧数据，避免内存增长
                    _pendingInput.RemoveRange(0, _pendingInput.Count - _inputRate);
                    _fractionalIndex = 0;
                }

                return output;
            }
        }
    }
}
