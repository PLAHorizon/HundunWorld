using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Horizon.Game.GengDi.Core.Services.Call
{
    /// <summary>
    /// 基于 DirectShow 的摄像头采集器（手写 COM 互操作，零第三方依赖）。
    /// 采集图：摄像头源滤镜 → SampleGrabber(RGB24) → NullRenderer。
    /// 帧数据通过 <see cref="FrameCaptured"/> 在 DirectShow 工作线程回调（RGB24，自底向上 DIB）。
    /// 无摄像头/驱动异常时 <see cref="Start"/> 返回 false，由上层提示"画面异常/设备不可用"。
    /// </summary>
    public sealed class DirectShowVideoCapture : IDisposable
    {
        // ===== DirectShow CLSID =====
        private static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        private static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
        private static readonly Guid CLSID_FilterGraph = new("e436ebb3-524f-11ce-9f53-0020af0ba770");
        private static readonly Guid CLSID_CaptureGraphBuilder2 = new("BF87B6E1-8C27-11d0-B3F0-00AA003761C5");
        private static readonly Guid CLSID_SampleGrabber = new("C1F400A0-3F08-11d3-9F0B-006008039E37");
        private static readonly Guid CLSID_NullRenderer = new("C1F400A4-3F08-11d3-9F0B-006008039E37");

        // ===== DirectShow 类别/媒体类型 GUID =====
        private static readonly Guid PinCategoryCapture = new("fb6c4281-0353-11d1-905f-0000c0cc16ba");
        private static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
        private static readonly Guid MediaSubTypeRGB24 = new("e436eb7d-524f-11ce-9f53-0020af0ba770");
        private static readonly Guid FormatTypeVideoInfo = new("05589f80-c356-11ce-bf01-00aa0055595a");
        private static readonly Guid IID_IPropertyBag = new("55272A00-42CB-11CE-8135-00AA004BB851");
        private static readonly Guid IID_IBaseFilter = new("56a86895-0ad4-11ce-b03a-0020af0ba770");

        // KS 采集类别：部分摄像头（虚拟摄像头/新型 UVC 设备）仅在此类别下可见，作为传统类别的兑底
        private static readonly Guid AMKSCategoryCapture = new("65E8773D-8F56-11D0-A3B9-00A0C9223196");

        private readonly object _graphSync = new();

        private IFilterGraph _graph;
        private ICaptureGraphBuilder2 _captureBuilder;
        private IBaseFilter _sourceFilter;
        private IBaseFilter _grabberFilter;
        private IBaseFilter _nullRenderer;
        private IMediaControl _mediaControl;
        private IMoniker _deviceMoniker;
        private ISampleGrabber _grabberRef;
        private Thread _pollThread;
        private CancellationTokenSource _pollCts;

        private bool _disposed;

        /// <summary>采集到一帧 RGB24 数据（自底向上 DIB 布局）。</summary>
        public event Action<byte[], int, int, int> FrameCaptured;

        public int FrameWidth { get; private set; }

        public int FrameHeight { get; private set; }

        public bool IsRunning { get; private set; }

        /// <summary>最近一次设备枚举的诊断信息（HRESULT/异常），无异常时为空。</summary>
        public static string LastEnumerateDiagnostics { get; private set; } = string.Empty;

        /// <summary>最近一次启动采集的诊断信息（各环节 HRESULT），成功时为空。</summary>
        public static string LastStartDiagnostics { get; private set; } = string.Empty;

        /// <summary>最近一次连接成功的媒体格式描述（子类型 GUID + 分辨率），用于诊断。</summary>
        public static string LastConnectedFormat { get; private set; } = string.Empty;

        /// <summary>枚举系统中可用的视频采集设备名称（无设备时返回空列表，不抛异常）。</summary>
        public static IReadOnlyList<string> EnumerateDeviceNames()
        {
            LastEnumerateDiagnostics = string.Empty;
            var names = new List<string>();

            foreach (var moniker in EnumerateDeviceMonikers())
            {
                try
                {
                    names.Add(ReadFriendlyName(moniker) ?? $"摄像头 {names.Count + 1}");
                }
                catch
                {
                    names.Add($"摄像头 {names.Count + 1}");
                }
                finally
                {
                    Marshal.ReleaseComObject(moniker);
                }
            }

            return names;
        }

        /// <summary>
        /// 依次在传统 DirectShow 视频采集类别与 KS 采集类别下枚举设备 Moniker，
        /// 任一类别命中即返回，最大化兼容不同类型的摄像头。调用方负责释放返回的 Moniker。
        /// </summary>
        private static List<IMoniker> EnumerateDeviceMonikers()
        {
            var result = new List<IMoniker>();

            foreach (var category in new[] { CLSID_VideoInputDeviceCategory, AMKSCategoryCapture })
            {
                CollectMonikers(category, result);
                if (result.Count > 0)
                {
                    break;
                }
            }

            return result;
        }

        private static void CollectMonikers(Guid category, List<IMoniker> sink)
        {
            ICreateDevEnum deviceEnum = null;
            IEnumMoniker enumMoniker = null;
            try
            {
                deviceEnum = (ICreateDevEnum)new CreateClassEnumeratorHost();
                var cat = category;
                var hr = deviceEnum.CreateClassEnumerator(ref cat, out enumMoniker, 0);
                if (hr != 0)
                {
                    // 记录 HRESULT 供上层提示（如 E_ACCESSDENIED 通常指向摄像头隐私权限拦截）
                    LastEnumerateDiagnostics = $"CreateClassEnumerator 返回 0x{hr:X8}";
                    System.Diagnostics.Debug.WriteLine($"[DirectShowVideoCapture] 设备枚举失败：category={cat}, hr=0x{hr:X8}");
                    return;
                }

                if (enumMoniker == null)
                {
                    // S_FALSE：该类别下确实没有设备
                    return;
                }

                while (true)
                {
                    var nextHr = enumMoniker.Next(1, out var moniker, out var fetched);
                    if (nextHr != 0 || fetched == 0 || moniker == null)
                    {
                        break;
                    }

                    sink.Add(moniker);
                }
            }
            catch (Exception ex)
            {
                LastEnumerateDiagnostics = $"枚举异常：{ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[DirectShowVideoCapture] 设备枚举异常：category={category}, {ex.Message}");
            }
            finally
            {
                if (enumMoniker != null) Marshal.ReleaseComObject(enumMoniker);
                if (deviceEnum != null) Marshal.ReleaseComObject(deviceEnum);
            }
        }

        /// <summary>
        /// 启动第 deviceIndex 个摄像头。失败返回 false（无设备/驱动异常/图构建失败）。
        /// forceRgb24=false 时不强制 RGB24 输出，直接采集摄像头原生格式（用于诊断格式转换链问题）。
        /// </summary>
        public bool Start(int deviceIndex = 0, bool forceRgb24 = true)
        {
            if (_disposed || IsRunning)
            {
                return IsRunning;
            }

            LastStartDiagnostics = string.Empty;
            LastConnectedFormat = string.Empty;

            try
            {
                _deviceMoniker = GetDeviceMoniker(deviceIndex);
                if (_deviceMoniker == null)
                {
                    LastStartDiagnostics = "未找到设备 Moniker";
                    return false;
                }

                _graph = (IFilterGraph)new FilterGraphHost();
                _captureBuilder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2Host();
                _captureBuilder.SetFiltergraph(_graph);

                var baseFilterGuid = IID_IBaseFilter;
                var bindHr = _deviceMoniker.BindToStorage(null, null, ref baseFilterGuid, out var sourceObj);
                if (bindHr != 0 || sourceObj == null)
                {
                    LastStartDiagnostics = $"绑定设备源滤镜失败 0x{bindHr:X8}（设备可能被拔除或禁用）";
                    Stop();
                    return false;
                }

                _sourceFilter = (IBaseFilter)sourceObj;
                _graph.AddFilter(_sourceFilter, "VideoCaptureSource");

                _grabberFilter = (IBaseFilter)new SampleGrabberHost();
                var grabber = (ISampleGrabber)_grabberFilter;
                if (forceRgb24)
                {
                    var mediaType = new AMMediaType
                    {
                        MajorType = MediaTypeVideo,
                        SubType = MediaSubTypeRGB24
                    };
                    var setMediaTypeHr = grabber.SetMediaType(ref mediaType);
                    if (setMediaTypeHr != 0)
                    {
                        LastStartDiagnostics = $"SampleGrabber SetMediaType 失败 0x{setMediaTypeHr:X8}";
                        Stop();
                        return false;
                    }
                }

                grabber.SetBufferSamples(true);
                grabber.SetOneShot(false);
                // 不使用 COM 回调（CCW 封送在部分运行时/设备组合下不可靠），改为后台轮询 GetCurrentBuffer 取帧
                _grabberRef = grabber;
                _graph.AddFilter(_grabberFilter, "SampleGrabber");

                _nullRenderer = (IBaseFilter)new NullRendererHost();
                _graph.AddFilter(_nullRenderer, "NullRenderer");

                var category = PinCategoryCapture;
                var mediaTypeVideo = MediaTypeVideo;
                var renderHr = _captureBuilder.RenderStream(
                    ref category, ref mediaTypeVideo, _sourceFilter, _grabberFilter, _nullRenderer);
                if (renderHr != 0)
                {
                    LastStartDiagnostics = $"RenderStream 失败 0x{renderHr:X8}（设备可能被占用或驱动不支持 RGB24）";
                    Stop();
                    return false;
                }

                var connectedType = new AMMediaType();
                if (grabber.GetConnectedMediaType(ref connectedType) == 0)
                {
                    LastConnectedFormat = $"子类型={connectedType.SubType}, 格式块={connectedType.FormatType}";

                    if (connectedType.FormatType == FormatTypeVideoInfo
                        && connectedType.FormatPtr != IntPtr.Zero)
                    {
                        // VIDEOINFOHEADER 布局：rcSource(16) + rcTarget(16) + dwBitRate(4) + dwBitErrorRate(4)
                        // + AvgTimePerFrame(8) = 48 字节，其后为 BITMAPINFOHEADER（biSize@48、biWidth@52、biHeight@56）
                        FrameWidth = Marshal.ReadInt32(connectedType.FormatPtr, 52);
                        FrameHeight = Marshal.ReadInt32(connectedType.FormatPtr, 56);
                    }

                    if (connectedType.FormatPtr != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(connectedType.FormatPtr);
                    }
                }

                // 防御性校验：分辨率读入异常（历史数据/非常规格式）时回退到安全默认值，
                // 避免后续按错误尺寸构造 Bitmap 导致所有帧编码失败、画面全部丢失
                if (FrameWidth < 16 || FrameWidth > 4096 || FrameHeight < 16 || FrameHeight > 4096)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[DirectShowVideoCapture] 分辨率读取异常（{FrameWidth}x{FrameHeight}），回退到 640x480");
                    FrameWidth = 640;
                    FrameHeight = 480;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[DirectShowVideoCapture] 摄像头采集已连接，分辨率 {FrameWidth}x{FrameHeight}");

                _mediaControl = (IMediaControl)_graph;
                // Run 可能返回 S_FALSE（0x1）表示图尚未完全进入运行态（异步源/驱动慢启动），
                // 短暂重试后仍为 S_FALSE 时容忍，由无帧看门狗兼底判断实际是否出帧
                var runHr = -1;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    runHr = _mediaControl.Run();
                    if (runHr == 0)
                    {
                        break;
                    }

                    System.Threading.Thread.Sleep(200);
                }

                if (runHr != 0 && runHr != 1)
                {
                    LastStartDiagnostics = $"采集图 Run 失败 0x{runHr:X8}";
                    Stop();
                    return false;
                }

                _pollCts = new CancellationTokenSource();
                StartPollThread(_pollCts.Token);

                IsRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                LastStartDiagnostics = $"启动异常：{ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[DirectShowVideoCapture] 启动摄像头失败：{ex.Message}");
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            lock (_graphSync)
            {
                if (_disposed)
                {
                    return;
                }

                IsRunning = false;
                StopPollThread();
                _grabberRef = null;

                try
                {
                    _mediaControl?.Stop();
                }
                catch
                {
                }

                ReleaseCom(ref _mediaControl);
                ReleaseCom(ref _nullRenderer);
                ReleaseCom(ref _grabberFilter);
                ReleaseCom(ref _sourceFilter);
                ReleaseCom(ref _captureBuilder);
                ReleaseCom(ref _graph);

                if (_deviceMoniker != null)
                {
                    Marshal.ReleaseComObject(_deviceMoniker);
                    _deviceMoniker = null;
                }
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

        private static string ReadFriendlyName(IMoniker moniker)
        {
            if (moniker == null)
            {
                return null;
            }

            object propObj = null;
            try
            {
                var propertyBagGuid = IID_IPropertyBag;
                // BindToStorage 失败时 propObj 为 null，必须检查返回值，避免后续强转/释放抛空引用
                var hr = moniker.BindToStorage(null, null, ref propertyBagGuid, out propObj);
                if (hr != 0 || propObj == null)
                {
                    return null;
                }

                var propertyBag = propObj as IPropertyBag;
                if (propertyBag == null)
                {
                    return null;
                }

                if (propertyBag.Read("FriendlyName", out var nameObj, IntPtr.Zero) != 0)
                {
                    return null;
                }

                return nameObj?.ToString();
            }
            catch
            {
                return null;
            }
            finally
            {
                if (propObj != null)
                {
                    Marshal.ReleaseComObject(propObj);
                }
            }
        }

        private static IMoniker GetDeviceMoniker(int deviceIndex)
        {
            var targetIndex = Math.Max(0, deviceIndex);
            var monikers = EnumerateDeviceMonikers();
            try
            {
                if (targetIndex < monikers.Count)
                {
                    var selected = monikers[targetIndex];
                    monikers[targetIndex] = null; // 移交所有权给调用方
                    return selected;
                }

                return null;
            }
            finally
            {
                foreach (var moniker in monikers)
                {
                    if (moniker != null)
                    {
                        Marshal.ReleaseComObject(moniker);
                    }
                }
            }
        }

        private void StartPollThread(CancellationToken token)
        {
            _pollThread = new Thread(() => PollLoop(token)) { IsBackground = true, Name = "DirectShowFramePoll" };
            _pollThread.Start();
        }

        /// <summary>
        /// 后台轮询 SampleGrabber.GetCurrentBuffer 取帧（约 25Hz，上层引擎再节流到 5fps），
        /// 相比 COM 回调方式不依赖托管对象封送（CCW），兼容性更好。
        /// </summary>
        private void PollLoop(CancellationToken token)
        {
            var buffer = Array.Empty<byte>();

            while (!token.IsCancellationRequested && IsRunning)
            {
                try
                {
                    var grabber = _grabberRef;
                    if (grabber == null)
                    {
                        break;
                    }

                    var size = 0;
                    var hr = grabber.GetCurrentBuffer(ref size, IntPtr.Zero);
                    if (hr == 0 && size > 0)
                    {
                        // 缓冲尺寸与当前分辨率不匹配（格式块读取失败等场景）时，按缓冲尺寸推断分辨率
                        var expected = ((FrameWidth * 3 + 3) & ~3) * FrameHeight;
                        if (size != expected)
                        {
                            TryInferDimensions(size);
                        }

                        if (buffer.Length != size)
                        {
                            buffer = new byte[size];
                        }

                        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                        try
                        {
                            var fillHr = grabber.GetCurrentBuffer(ref size, handle.AddrOfPinnedObject());
                            if (fillHr == 0 && size > 0)
                            {
                                // 拷贝后交付，避免复用缓冲与异步编码竞争
                                var copy = new byte[size];
                                Array.Copy(buffer, copy, size);
                                FrameCaptured?.Invoke(copy, FrameWidth, FrameHeight, (FrameWidth * 3 + 3) & ~3);
                            }
                        }
                        finally
                        {
                            handle.Free();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DirectShowVideoCapture] 帧轮询异常：{ex.Message}");
                }

                Thread.Sleep(40);
            }
        }

        /// <summary>按 RGB24 常见分辨率表，从缓冲字节数反推宽高（兼容格式块读取失败的非常规设备）。</summary>
        private void TryInferDimensions(int bufferSize)
        {
            foreach (var width in new[] { 1920, 1600, 1440, 1366, 1280, 1080, 1024, 960, 848, 800, 720, 640, 480, 424, 400, 352, 320, 240, 176, 160 })
            {
                var stride = (width * 3 + 3) & ~3;
                if (bufferSize % stride != 0)
                {
                    continue;
                }

                var height = bufferSize / stride;
                if (height >= 16 && height <= 4096)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[DirectShowVideoCapture] 按缓冲尺寸推断分辨率：{width}x{height}（{bufferSize} bytes）");
                    FrameWidth = width;
                    FrameHeight = height;
                    return;
                }
            }
        }

        private void StopPollThread()
        {
            try
            {
                _pollCts?.Cancel();
            }
            catch
            {
            }

            var thread = _pollThread;
            _pollThread = null;
            if (thread != null && thread.IsAlive)
            {
                try
                {
                    thread.Join(TimeSpan.FromMilliseconds(1500));
                }
                catch
                {
                }
            }

            _pollCts?.Dispose();
            _pollCts = null;
        }

        private static void ReleaseCom<T>(ref T reference) where T : class
        {
            if (reference != null)
            {
                try
                {
                    Marshal.ReleaseComObject(reference);
                }
                catch
                {
                }

                reference = null;
            }
        }

        // ==================== COM 互操作声明 ====================

        [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum
        {
            [PreserveSig]
            int CreateClassEnumerator(ref Guid deviceClass, out IEnumMoniker enumMoniker, int flags);
        }

        // 注意：此处的 Guid 必须是 CLSID_SystemDeviceEnum（62BE5D10-...），而非 ICreateDevEnum 的 IID
        [ComImport, Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")]
        private class CreateClassEnumeratorHost
        {
        }

        [ComImport, Guid("00000102-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumMoniker
        {
            [PreserveSig]
            int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IMoniker rgelt, out uint fetched);
        }

        [ComImport, Guid("0000000f-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMoniker
        {
            // IPersist
            [PreserveSig]
            int GetClassID(out Guid classId);

            // IPersistStream（占位，保持 vtable 对齐）
            [PreserveSig]
            int IsDirty();

            [PreserveSig]
            int Load(IntPtr stream);

            [PreserveSig]
            int Save(IntPtr stream, [MarshalAs(UnmanagedType.Bool)] bool clearDirty);

            [PreserveSig]
            int GetSizeMax(out long size);

            // IMoniker
            [PreserveSig]
            int BindToStorage(
                [MarshalAs(UnmanagedType.Interface)] object bindCtx,
                [MarshalAs(UnmanagedType.Interface)] object mkBindCtx,
                ref Guid riid,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppvObj);
        }

        [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig]
            int Read(
                [In, MarshalAs(UnmanagedType.LPWStr)] string propertyName,
                [Out, MarshalAs(UnmanagedType.Struct)] out object value,
                IntPtr errorLog);

            [PreserveSig]
            int Write([In, MarshalAs(UnmanagedType.LPWStr)] string propertyName, ref object value);
        }

        [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IBaseFilter
        {
            // 仅作为滤镜句柄使用，不调用其方法；vtable 占位从略（Marshal 按引用传递）
        }

        [ComImport, Guid("56a8689f-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFilterGraph
        {
            [PreserveSig]
            int AddFilter(IBaseFilter filter, [MarshalAs(UnmanagedType.LPWStr)] string name);
        }

        // 注意：此处的 Guid 必须是 CLSID_FilterGraph（e436ebb3-...），而非 IFilterGraph 的 IID
        [ComImport, Guid("e436ebb3-524f-11ce-9f53-0020af0ba770")]
        private class FilterGraphHost
        {
        }

        [ComImport, Guid("93E5A4E0-2D50-11d2-ABFA-00A0C9C6E38D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICaptureGraphBuilder2
        {
            [PreserveSig]
            int SetFiltergraph(IFilterGraph graph);

            [PreserveSig]
            int GetFiltergraph(out IFilterGraph graph);

            [PreserveSig]
            int SetOutputFileName(ref Guid type, [MarshalAs(UnmanagedType.LPWStr)] string fileName, out IBaseFilter filter, out IntPtr sink);

            [PreserveSig]
            int FindInterface(ref Guid category, ref Guid type, IBaseFilter filter, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

            [PreserveSig]
            int RenderStream(ref Guid category, ref Guid type, IBaseFilter source, IBaseFilter via, IBaseFilter sink);
        }

        [ComImport, Guid("BF87B6E1-8C27-11d0-B3F0-00AA003761C5")]
        private class CaptureGraphBuilder2Host
        {
        }

        [ComImport, Guid("6B652FFF-11FE-4fce-92AD-0266B5D7C78F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISampleGrabber
        {
            [PreserveSig]
            int SetOneShot([MarshalAs(UnmanagedType.Bool)] bool oneShot);

            [PreserveSig]
            int SetMediaType(ref AMMediaType mediaType);

            [PreserveSig]
            int GetConnectedMediaType(ref AMMediaType mediaType);

            [PreserveSig]
            int SetBufferSamples([MarshalAs(UnmanagedType.Bool)] bool bufferThem);

            [PreserveSig]
            int GetCurrentBuffer(ref int bufferSize, IntPtr buffer);

            [PreserveSig]
            int GetCurrentSample(out IntPtr sample);

            [PreserveSig]
            int SetCallback(ISampleGrabberCB callback, int whichMethodToCallback);
        }

        [ComImport, Guid("C1F400A0-3F08-11d3-9F0B-006008039E37")]
        private class SampleGrabberHost
        {
        }

        [ComImport, Guid("0579154A-2B53-4994-B0D0-E773148EFF85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISampleGrabberCB
        {
            [PreserveSig]
            int SampleCB(double sampleTime, IntPtr sample);

            [PreserveSig]
            int BufferCB(double sampleTime, IntPtr buffer, int bufferLen);
        }

        [ComImport, Guid("C1F400A4-3F08-11d3-9F0B-006008039E37")]
        private class NullRendererHost
        {
        }

        [ComImport, Guid("56a868b1-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMediaControl
        {
            // IDispatch vtable 占位（IMediaControl 继承自 IDispatch，本实现不调用这些方法）
            [PreserveSig]
            int GetTypeInfoCount(out int typeInfoCount);

            [PreserveSig]
            int GetTypeInfo(uint typeInfo, uint lcid, out IntPtr info);

            [PreserveSig]
            int GetIDsOfNames(ref Guid riid, IntPtr names, uint nameCount, uint lcid, IntPtr dispIds);

            [PreserveSig]
            int Invoke(int dispIdMember, ref Guid riid, uint lcid, ushort flags, IntPtr dispParams, IntPtr result, IntPtr excepInfo, IntPtr argErr);

            [PreserveSig]
            int Run();

            [PreserveSig]
            int Pause();

            [PreserveSig]
            int Stop();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AMMediaType
        {
            public Guid MajorType;
            public Guid SubType;
            [MarshalAs(UnmanagedType.Bool)]
            public bool FixedSizeSamples;
            [MarshalAs(UnmanagedType.Bool)]
            public bool TemporalCompression;
            public int SampleSize;
            public Guid FormatType;
            public IntPtr UnknownPtr;
            public int FormatSize;
            public IntPtr FormatPtr;
        }
    }
}
