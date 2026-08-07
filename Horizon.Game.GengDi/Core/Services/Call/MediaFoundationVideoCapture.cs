using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Horizon.Game.GengDi.Core.Services.Call
{
    /// <summary>
    /// 基于 Media Foundation（IMFSourceReader）的摄像头采集器（手写 COM 互操作，零第三方依赖）。
    /// 相比 DirectShow 方案：不依赖托管回调封送（CCW），由后台线程主动轮询 ReadSample 取帧，
    /// 且不要求 DirectShow 组件可实例化（部分系统下 CoCreateInstance 会返回 REGDB_E_CLASSNOTREG）。
    /// 输出格式统一请求为 RGB32（BGRA，自顶向下）。
    /// 所有 IID/GUID 均取自 Windows SDK（mfobjects.h / mfidl.h / mfreadwrite.h / mfapi.h）。
    /// </summary>
    public sealed class MediaFoundationVideoCapture : IDisposable
    {
        // ===== Media Foundation 属性与格式 GUID =====
        private static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
        private static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
        private static readonly Guid MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
        private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
        private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
        private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");

        private const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
        private const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;
        private const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x00040000;
        private const uint MF_SOURCE_READERF_ERROR = 0x00080000;
        private const uint MF_SOURCE_READERF_STREAMTICK = 0x00100000;

        private const ushort VT_UI8 = 21;

        private readonly object _startStopSync = new();
        private Thread _readThread;
        private CancellationTokenSource _readCts;
        private IMFMediaSource _mediaSource;
        private IMFSourceReader _reader;

        private volatile bool _isRunning;
        private bool _disposed;

        /// <summary>采集到一帧 RGB32 数据（BGRA，自顶向下）。</summary>
        public event Action<byte[], int, int, int, bool> FrameCaptured;

        public int FrameWidth { get; private set; }

        public int FrameHeight { get; private set; }

        public bool IsRunning => _isRunning;

        /// <summary>最近一次设备枚举的诊断信息（HRESULT/异常），无异常时为空。</summary>
        public static string LastEnumerateDiagnostics { get; private set; } = string.Empty;

        /// <summary>最近一次启动采集的诊断信息（各环节 HRESULT），成功时为空。</summary>
        public static string LastStartDiagnostics { get; private set; } = string.Empty;

        /// <summary>当前环境的 Media Foundation 采集组件是否可用（校验 DLL 与导出函数入口点）。</summary>
        public static bool IsAvailable()
        {
            try
            {
                var mfplat = NativeMethods.LoadLibrary("mfplat.dll");
                var mf = NativeMethods.LoadLibrary("mf.dll");
                var mfreadwrite = NativeMethods.LoadLibrary("mfreadwrite.dll");
                if (mfplat == IntPtr.Zero || mf == IntPtr.Zero || mfreadwrite == IntPtr.Zero)
                {
                    return false;
                }

                return NativeMethods.GetProcAddress(mf, "MFEnumDeviceSources") != IntPtr.Zero
                    && NativeMethods.GetProcAddress(mfreadwrite, "MFCreateSourceReaderFromMediaSource") != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>枚举视频采集设备名称（无设备时返回空列表，不抛异常）。</summary>
        public static IReadOnlyList<string> EnumerateDeviceNames()
        {
            LastEnumerateDiagnostics = string.Empty;
            var names = new List<string>();

            if (!TryMfStartup(out var startupError))
            {
                LastEnumerateDiagnostics = startupError;
                return names;
            }

            var attributes = CreateVidCapAttributes();
            if (attributes == null)
            {
                LastEnumerateDiagnostics = "MFCreateAttributes/SetGUID 失败";
                return names;
            }

            try
            {
                var hr = NativeMethods.MFEnumDeviceSources(attributes, out var devices, out var count);
                if (hr != 0)
                {
                    LastEnumerateDiagnostics = $"MFEnumDeviceSources 返回 0x{hr:X8}";
                    return names;
                }

                for (var i = 0; i < count; i++)
                {
                    var devicePtr = Marshal.ReadIntPtr(devices, i * IntPtr.Size);
                    var device = Marshal.GetObjectForIUnknown(devicePtr) as IMFActivate;
                    try
                    {
                        names.Add(ReadFriendlyName(device) ?? $"摄像头 {names.Count + 1}");
                    }
                    catch
                    {
                        names.Add($"摄像头 {names.Count + 1}");
                    }
                    finally
                    {
                        if (device != null) Marshal.ReleaseComObject(device);
                        Marshal.Release(devicePtr);
                    }
                }

                Marshal.FreeCoTaskMem(devices);
            }
            catch (Exception ex)
            {
                LastEnumerateDiagnostics = $"枚举异常：{ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] 设备枚举异常：{ex.Message}");
            }
            finally
            {
                Marshal.ReleaseComObject(attributes);
            }

            return names;
        }

        /// <summary>启动第 deviceIndex 个摄像头。失败返回 false。</summary>
        public bool Start(int deviceIndex = 0)
        {
            if (_disposed || _isRunning)
            {
                return _isRunning;
            }

            lock (_startStopSync)
            {
                LastStartDiagnostics = string.Empty;

                try
                {
                    if (!TryMfStartup(out var startupError))
                    {
                        LastStartDiagnostics = $"MFStartup 失败：{startupError}";
                        System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] {LastStartDiagnostics}");
                        return false;
                    }

                    var activate = GetDeviceActivate(deviceIndex);
                    if (activate == null)
                    {
                        LastStartDiagnostics = "未找到摄像头设备（MFEnumDeviceSources 返回空）";
                        System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] {LastStartDiagnostics}");
                        return false;
                    }

                    try
                    {
                        var sourceGuid = typeof(IMFMediaSource).GUID;
                        var hr = activate.ActivateObject(ref sourceGuid, out var sourceObj);
                        if (hr != 0 || sourceObj == IntPtr.Zero)
                        {
                            LastStartDiagnostics = $"激活摄像头失败 0x{hr:X8}（可能被隐私设置拦截或被占用）";
                            System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] {LastStartDiagnostics}");
                            return false;
                        }

                        _mediaSource = (IMFMediaSource)Marshal.GetTypedObjectForIUnknown(sourceObj, typeof(IMFMediaSource));
                        Marshal.Release(sourceObj);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(activate);
                    }

                    var hr2 = NativeMethods.MFCreateSourceReaderFromMediaSource(_mediaSource, IntPtr.Zero, out _reader);
                    if (hr2 != 0 || _reader == null)
                    {
                        LastStartDiagnostics = $"创建 SourceReader 失败 0x{hr2:X8}";
                        System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] {LastStartDiagnostics}");
                        StopCore();
                        return false;
                    }

                    // 只启用第一个视频流
                    _reader.SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, false);
                    _reader.SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM, true);

                    // 请求输出格式：视频 RGB32
                    var partialType = CreatePartialVideoType();
                    if (partialType == null)
                    {
                        LastStartDiagnostics = "创建 RGB32 媒体类型失败";
                        StopCore();
                        return false;
                    }

                    try
                    {
                        var setHr = _reader.SetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, partialType);
                        if (setHr != 0)
                        {
                            LastStartDiagnostics = $"设置 RGB32 输出格式失败 0x{setHr:X8}";
                            System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] {LastStartDiagnostics}");
                            StopCore();
                            return false;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(partialType);
                    }

                    // 读取实际协商的分辨率（MF_MT_FRAME_SIZE 打包为 (width<<32)|height）
                    FrameWidth = 640;
                    FrameHeight = 480;
                    var connectedPtr = IntPtr.Zero;
                    var getHr = _reader.GetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, out connectedPtr);
                    if (getHr == 0 && connectedPtr != IntPtr.Zero)
                    {
                        var connected = (IMFMediaType)Marshal.GetTypedObjectForIUnknown(connectedPtr, typeof(IMFMediaType));
                        try
                        {
                            var frameSizeGuid = MF_MT_FRAME_SIZE;
                            if (connected.GetItem(ref frameSizeGuid, out var sizeValue) == 0 && sizeValue.vt == VT_UI8)
                            {
                                var packed = sizeValue.Value64;
                                var width = (int)((ulong)packed >> 32);
                                var height = (int)(packed & 0xFFFFFFFF);
                                if (width >= 16 && width <= 4096 && height >= 16 && height <= 4096)
                                {
                                    FrameWidth = width;
                                    FrameHeight = height;
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(connected);
                            Marshal.Release(connectedPtr);
                        }
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[MFVideoCapture] 摄像头已连接，分辨率 {FrameWidth}x{FrameHeight}");

                    _readCts = new CancellationTokenSource();
                    _isRunning = true;
                    _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "MFVideoCaptureRead" };
                    _readThread.Start(_readCts.Token);
                    return true;
                }
                catch (Exception ex)
                {
                    LastStartDiagnostics = $"启动异常：{ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] 启动摄像头失败：{ex.Message}");
                    StopCore();
                    return false;
                }
            }
        }

        public void Stop()
        {
            lock (_startStopSync)
            {
                StopCore();
            }
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

        private void StopCore()
        {
            _isRunning = false;

            try
            {
                _readCts?.Cancel();
            }
            catch
            {
            }

            var thread = _readThread;
            _readThread = null;
            if (thread != null && thread.IsAlive)
            {
                thread.Join(TimeSpan.FromSeconds(2));
            }

            _readCts?.Dispose();
            _readCts = null;

            if (_reader != null)
            {
                try
                {
                    Marshal.ReleaseComObject(_reader);
                }
                catch
                {
                }

                _reader = null;
            }

            if (_mediaSource != null)
            {
                try
                {
                    Marshal.ReleaseComObject(_mediaSource);
                }
                catch
                {
                }

                _mediaSource = null;
            }
        }

        private void ReadLoop(object state)
        {
            var token = (CancellationToken)state;

            while (!token.IsCancellationRequested && _isRunning)
            {
                var reader = _reader;
                if (reader == null)
                {
                    break;
                }

                try
                {
                    var hr = reader.ReadSample(
                        MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                        0,
                        out _,
                        out var streamFlags,
                        out _,
                        out var samplePtr);

                    if (hr != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] ReadSample 失败 0x{hr:X8}，停止采集");
                        break;
                    }

                    if ((streamFlags & (MF_SOURCE_READERF_ENDOFSTREAM | MF_SOURCE_READERF_ERROR)) != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] 采集流结束/出错（flags=0x{streamFlags:X8}）");
                        break;
                    }

                    if ((streamFlags & MF_SOURCE_READERF_STREAMTICK) != 0 || samplePtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    var mediaSample = (IMFSample)Marshal.GetTypedObjectForIUnknown(samplePtr, typeof(IMFSample));
                    Marshal.Release(samplePtr);

                    try
                    {
                        EmitFrame(mediaSample);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(mediaSample);
                    }
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MFVideoCapture] 读帧循环异常：{ex.Message}");
                    }
                    break;
                }
            }

            _isRunning = false;
        }

        private void EmitFrame(IMFSample sample)
        {
            if (sample.ConvertToContiguousBuffer(out var bufferPtr) != 0 || bufferPtr == IntPtr.Zero)
            {
                return;
            }

            var mediaBuffer = (IMFMediaBuffer)Marshal.GetTypedObjectForIUnknown(bufferPtr, typeof(IMFMediaBuffer));
            Marshal.Release(bufferPtr);

            try
            {
                if (mediaBuffer.Lock(out var dataPtr, out _, out var currentLength) != 0 || dataPtr == IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    var stride = FrameWidth * 4;
                    var expected = stride * FrameHeight;
                    if (currentLength < expected)
                    {
                        return;
                    }

                    var frame = new byte[expected];
                    Marshal.Copy(dataPtr, frame, 0, expected);
                    FrameCaptured?.Invoke(frame, FrameWidth, FrameHeight, stride, true);
                }
                finally
                {
                    mediaBuffer.Unlock();
                }
            }
            finally
            {
                Marshal.ReleaseComObject(mediaBuffer);
            }
        }

        private static bool TryMfStartup(out string error)
        {
            error = string.Empty;
            try
            {
                var hr = NativeMethods.MFStartup(NativeMethods.MF_VERSION, 0);
                if (hr != 0)
                {
                    error = $"MFStartup 返回 0x{hr:X8}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"MFStartup 异常：{ex.Message}";
                return false;
            }
        }

        private static IMFAttributes CreateVidCapAttributes()
        {
            try
            {
                var hr = NativeMethods.MFCreateAttributes(out var attributes, 2);
                if (hr != 0 || attributes == null)
                {
                    return null;
                }

                var typeGuid = MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
                var vidcapGuid = MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
                if (attributes.SetGUID(ref typeGuid, ref vidcapGuid) != 0)
                {
                    Marshal.ReleaseComObject(attributes);
                    return null;
                }

                return attributes;
            }
            catch
            {
                return null;
            }
        }

        private static IMFMediaType CreatePartialVideoType()
        {
            try
            {
                var hr = NativeMethods.MFCreateMediaType(out var mediaType);
                if (hr != 0 || mediaType == null)
                {
                    return null;
                }

                var majorGuid = MF_MT_MAJOR_TYPE;
                var videoGuid = MFMediaType_Video;
                if (mediaType.SetGUID(ref majorGuid, ref videoGuid) != 0)
                {
                    Marshal.ReleaseComObject(mediaType);
                    return null;
                }

                var subGuid = MF_MT_SUBTYPE;
                var rgb32Guid = MFVideoFormat_RGB32;
                if (mediaType.SetGUID(ref subGuid, ref rgb32Guid) != 0)
                {
                    Marshal.ReleaseComObject(mediaType);
                    return null;
                }

                return mediaType;
            }
            catch
            {
                return null;
            }
        }

        private static IMFActivate GetDeviceActivate(int deviceIndex)
        {
            var attributes = CreateVidCapAttributes();
            if (attributes == null)
            {
                return null;
            }

            try
            {
                if (NativeMethods.MFEnumDeviceSources(attributes, out var devices, out var count) != 0 || count == 0)
                {
                    return null;
                }

                var targetIndex = Math.Max(0, deviceIndex);
                IMFActivate selected = null;

                for (var i = 0; i < count; i++)
                {
                    var devicePtr = Marshal.ReadIntPtr(devices, i * IntPtr.Size);
                    if (i == targetIndex && selected == null)
                    {
                        selected = Marshal.GetObjectForIUnknown(devicePtr) as IMFActivate;
                        Marshal.Release(devicePtr);
                    }
                    else
                    {
                        Marshal.Release(devicePtr);
                    }
                }

                Marshal.FreeCoTaskMem(devices);
                return selected;
            }
            catch
            {
                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(attributes);
            }
        }

        private static string ReadFriendlyName(IMFActivate activate)
        {
            if (activate == null)
            {
                return null;
            }

            var nameGuid = MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME;
            if (activate.GetAllocatedString(ref nameGuid, out var name, out _) != 0 || name == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }

        // ==================== COM 互操作声明（IID/顺序均与 Windows SDK 头文件一致） ====================

        private static class NativeMethods
        {
            public const uint MF_VERSION = 0x20070;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr LoadLibrary(string lpFileName);

            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

            [DllImport("mfplat.dll")]
            public static extern int MFStartup(uint version, int dwFlags);

            [DllImport("mfplat.dll")]
            public static extern int MFCreateAttributes([MarshalAs(UnmanagedType.Interface)] out IMFAttributes attributes, uint initialSize);

            // 注意：MFEnumDeviceSources 导出自 mf.dll（非 mfplat.dll），已在本机导出表验证
            [DllImport("mf.dll")]
            public static extern int MFEnumDeviceSources(
                [MarshalAs(UnmanagedType.Interface)] IMFAttributes attributes,
                out IntPtr devices,
                out uint count);

            [DllImport("mfplat.dll")]
            public static extern int MFCreateMediaType([MarshalAs(UnmanagedType.Interface)] out IMFMediaType mediaType);

            [DllImport("mfreadwrite.dll")]
            public static extern int MFCreateSourceReaderFromMediaSource(
                [MarshalAs(UnmanagedType.Interface)] IMFMediaSource mediaSource,
                IntPtr attributes,
                [MarshalAs(UnmanagedType.Interface)] out IMFSourceReader reader);
        }

        /// <summary>
        /// PROPVARIANT 简化布局：vt(2) + 保留(6) + 联合体起点（偏移8）。
        /// VT_UI8 的 8 字节值与 VT_BOOL 的低 2 字节均位于偏移 8。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public long Value64;
        }

        [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFAttributes
        {
            [PreserveSig]
            int GetItem(ref Guid guidKey, out PROPVARIANT value);

            [PreserveSig]
            int GetItemType(ref Guid guidKey, out int type);

            [PreserveSig]
            int CompareItem(ref Guid guidKey, ref PROPVARIANT value, [MarshalAs(UnmanagedType.Bool)] out bool result);

            [PreserveSig]
            int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes other, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);

            [PreserveSig]
            int GetUINT32(ref Guid guidKey, out uint value);

            [PreserveSig]
            int GetUINT64(ref Guid guidKey, out ulong value);

            [PreserveSig]
            int GetDouble(ref Guid guidKey, out double value);

            [PreserveSig]
            int GetGUID(ref Guid guidKey, out Guid guidValue);

            [PreserveSig]
            int GetStringLength(ref Guid guidKey, out uint length);

            [PreserveSig]
            int GetString(ref Guid guidKey, IntPtr value, uint size, out uint length);

            [PreserveSig]
            int GetAllocatedString(ref Guid guidKey, out IntPtr value, out uint length);

            [PreserveSig]
            int GetBlobSize(ref Guid guidKey, out uint size);

            [PreserveSig]
            int GetBlob(ref Guid guidKey, IntPtr buf, uint size, out uint length);

            [PreserveSig]
            int GetAllocatedBlob(ref Guid guidKey, out IntPtr buf, out uint size);

            [PreserveSig]
            int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);

            [PreserveSig]
            int SetItem(ref Guid guidKey, ref PROPVARIANT value);

            [PreserveSig]
            int DeleteItem(ref Guid guidKey);

            [PreserveSig]
            int DeleteAllItems();

            [PreserveSig]
            int SetUINT32(ref Guid guidKey, uint value);

            [PreserveSig]
            int SetUINT64(ref Guid guidKey, ulong value);

            [PreserveSig]
            int SetDouble(ref Guid guidKey, double value);

            [PreserveSig]
            int SetGUID(ref Guid guidKey, ref Guid guidValue);

            [PreserveSig]
            int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string value);

            [PreserveSig]
            int SetBlob(ref Guid guidKey, IntPtr buf, uint size);

            [PreserveSig]
            int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object unknown);

            [PreserveSig]
            int LockStore();

            [PreserveSig]
            int UnlockStore();

            [PreserveSig]
            int GetCount(out uint count);

            [PreserveSig]
            int GetItemByIndex(uint index, out Guid guidKey, out PROPVARIANT value);

            [PreserveSig]
            int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes dest);
        }

        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType : IMFAttributes
        {
            [PreserveSig]
            int GetMajorType(out Guid type);

            [PreserveSig]
            int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool compressed);

            [PreserveSig]
            int IsEqual([MarshalAs(UnmanagedType.Interface)] IMFMediaType mediaType, out uint flags);

            [PreserveSig]
            int GetRepresentation(ref Guid formatType, out IntPtr representation);

            [PreserveSig]
            int FreeRepresentation(ref Guid formatType, IntPtr representation);
        }

        [ComImport, Guid("7FEE9E9A-4A89-47a6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFActivate : IMFAttributes
        {
            [PreserveSig]
            int ActivateObject(ref Guid riid, out IntPtr ppv);

            [PreserveSig]
            int ShutdownObject();

            [PreserveSig]
            int DetachObject();
        }

        // 仅用于 Marshal.GetTypedObjectForIUnknown 的 QI 目标，不调用其方法
        [ComImport, Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaSource
        {
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample : IMFAttributes
        {
            [PreserveSig]
            int GetSampleFlags(out uint flags);

            [PreserveSig]
            int SetSampleFlags(uint flags);

            [PreserveSig]
            int GetSampleTime(out long sampleTime);

            [PreserveSig]
            int SetSampleTime(long sampleTime);

            [PreserveSig]
            int GetSampleDuration(out long duration);

            [PreserveSig]
            int SetSampleDuration(long duration);

            [PreserveSig]
            int GetBufferCount(out uint count);

            [PreserveSig]
            int GetBufferByIndex(uint index, out IntPtr buffer);

            [PreserveSig]
            int ConvertToContiguousBuffer(out IntPtr buffer);
        }

        [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            [PreserveSig]
            int Lock(out IntPtr buffer, out int maxLength, out int currentLength);

            [PreserveSig]
            int Unlock();

            [PreserveSig]
            int GetCurrentLength(out int length);

            [PreserveSig]
            int SetCurrentLength(int length);

            [PreserveSig]
            int GetMaxLength(out int length);
        }

        // vtable 顺序严格对应 mfreadwrite.h：
        // GetStreamSelection(0) SetStreamSelection(1) GetNativeMediaType(2) GetCurrentMediaType(3)
        // SetCurrentMediaType(4) SetCurrentPosition(5) ReadSample(6) Flush(7)
        [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSourceReader
        {
            [PreserveSig]
            int GetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] out bool selected);

            [PreserveSig]
            int SetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);

            [PreserveSig]
            int GetNativeMediaType(uint streamIndex, uint mediaTypeIndex, out IntPtr mediaType);

            [PreserveSig]
            int GetCurrentMediaType(uint streamIndex, out IntPtr mediaType);

            [PreserveSig]
            int SetCurrentMediaType(uint streamIndex, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] IMFMediaType mediaType);

            [PreserveSig]
            int SetCurrentPosition(uint streamIndex, ref PROPVARIANT position);

            [PreserveSig]
            int ReadSample(
                uint streamIndex,
                uint controlFlags,
                out uint actualStreamIndex,
                out uint streamFlags,
                out long timestamp,
                out IntPtr sample);

            [PreserveSig]
            int Flush(uint streamIndex);
        }
    }
}
