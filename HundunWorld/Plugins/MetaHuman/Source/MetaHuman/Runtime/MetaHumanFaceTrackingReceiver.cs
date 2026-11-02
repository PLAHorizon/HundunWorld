using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using FlaxEngine;
using Newtonsoft.Json.Linq;

namespace MetaHuman.Runtime
{
    /// <summary>
    /// 外部面部追踪输入源（对标 UE 的 Live Link / Live Link Face）。
    ///
    /// ARKit 词汇表（MetaHumanArkitMap）与写入端（MetaHumanFaceAnimator.SetArkitChannels）早已存在，
    /// 但游戏里一直没有"谁来喂数据"：本组件就是那个入口。它在一个后台线程收 UDP 上的 JSON 帧，
    /// 主线程每帧把最新一帧交给表情动画器，不阻塞渲染/逻辑线程。
    ///
    /// 支持的报文（三种都收，够接常见的追踪程序）：
    ///   {"BlendShapes":{"eyeBlinkLeft":0.4,"jawOpen":0.1}}
    ///   {"BlendShapes":[{"eyeBlinkLeft":0.4},{"jawOpen":0.1}]}
    ///   {"eyeBlinkLeft":0.4,"jawOpen":0.1}
    /// 值超出 [0,1] 或非有限数会被丢弃（不把坏数据灌进骨骼）。
    ///
    /// 断流保护：超过 ReleaseAfterSeconds 没有新帧，就把上一批通道清零一次，
    /// 否则追踪程序退出后人脸会永远停在最后一个表情上。
    /// </summary>
    public class MetaHumanFaceTrackingReceiver : Script
    {
        [Tooltip("打开后监听 UDP。默认关闭：这是开发/演出用的输入，正式客户端不该默认占端口。")]
        [Serialize]
        public bool Receiving { get; set; }

        [Tooltip("监听端口。Live Link Face 常用 11111，这里同名默认。")]
        [Serialize]
        public int Port { get; set; } = 11111;

        [Tooltip("绑定地址。只收本机数据用 127.0.0.1；要让同网段设备发过来用 0.0.0.0。")]
        [Serialize]
        public string BindAddress { get; set; } = "127.0.0.1";

        [Tooltip("多久没新帧就认为追踪断流（秒），断流后把上一批通道清零一次。")]
        [Serialize]
        public float ReleaseAfterSeconds { get; set; } = 0.75f;

        [Tooltip("要驱动的表情动画器；留空则在自身与父级链上自动找一个 MetaHumanFaceAnimator。")]
        [Serialize]
        public MetaHumanFaceAnimator Animator { get; set; }

        [Tooltip("每收到一帧打一行日志（排查追踪程序有没有真的发出来）。")]
        [Serialize]
        public bool DebugLog { get; set; }

        /// <summary>累计收到的合法帧数。</summary>
        public long FramesReceived { get; private set; }

        /// <summary>最近一帧里的通道数。</summary>
        public int LastChannelCount { get; private set; }

        /// <summary>最近一帧真正接上的对照条目数。</summary>
        public int LastAppliedEntries { get; private set; }

        /// <summary>最近一帧没接上的通道数（词汇表没有/超出段区间）。</summary>
        public int LastUnmatchedChannels { get; private set; }

        /// <summary>最近一次出错信息（空表示没出错）。</summary>
        public string LastError { get; private set; } = string.Empty;

        private UdpClient _socket;
        private Thread _thread;
        private volatile bool _running;
        private readonly object _lock = new object();
        private Dictionary<string, float> _pending;
        private float _pendingAt = -1f;
        private Dictionary<string, float> _lastApplied;
        private bool _released = true;

        public override void OnStart()
        {
            if (Receiving)
            {
                StartListening();
            }
        }

        public override void OnUpdate()
        {
            // 允许运行时开关（编辑器属性面板 / actor_set_property 都能改）。
            if (Receiving && _socket == null)
            {
                StartListening();
            }
            else if (!Receiving && _socket != null)
            {
                StopListening();
            }

            Dictionary<string, float> frame = null;
            float frameAt = 0f;
            lock (_lock)
            {
                if (_pending != null)
                {
                    frame = _pending;
                    frameAt = _pendingAt;
                    _pending = null;
                }
            }

            if (frame != null)
            {
                Apply(frame);
                _released = false;
                return;
            }

            // 断流：把上一批通道清零一次，避免人脸停在最后表情上。
            if (!_released && _lastApplied != null && NowSeconds() - _pendingAt > ReleaseAfterSeconds)
            {
                var zeros = new Dictionary<string, float>(_lastApplied.Count);
                foreach (string key in _lastApplied.Keys)
                {
                    zeros[key] = 0f;
                }

                Apply(zeros);
                _released = true;
            }
        }

        public override void OnDestroy()
        {
            StopListening();
        }

        private void StartListening()
        {
            if (_socket != null)
            {
                return;
            }

            try
            {
                IPAddress address = string.IsNullOrEmpty(BindAddress)
                    ? IPAddress.Loopback
                    : IPAddress.Parse(BindAddress);
                _socket = new UdpClient(new IPEndPoint(address, Port));
                _running = true;
                _thread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "MetaHumanFaceTracking",
                };
                _thread.Start();
                Debug.Log("[FaceTracking] 监听 udp://" + BindAddress + ":" + Port);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.LogWarning("[FaceTracking] 打开端口失败: " + ex.Message);
                StopListening();
                Receiving = false;
            }
        }

        private void StopListening()
        {
            _running = false;
            try
            {
                _socket?.Close();
            }
            catch (Exception)
            {
                // 关闭失败没有补救动作：线程是后台线程，端口会随进程释放。
            }

            _socket = null;
            _thread = null;
        }

        private void ReceiveLoop()
        {
            UdpClient socket = _socket;
            if (socket == null)
            {
                return;
            }

            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running && socket == _socket)
            {
                try
                {
                    byte[] payload = socket.Receive(ref remote);
                    if (payload == null || payload.Length == 0)
                    {
                        continue;
                    }

                    Dictionary<string, float> values = ParseFrame(Encoding.UTF8.GetString(payload));
                    if (values == null || values.Count == 0)
                    {
                        continue;
                    }

                    lock (_lock)
                    {
                        _pending = values;
                        _pendingAt = NowSeconds();
                    }
                }
                catch (Exception)
                {
                    // 关闭 socket 会打断 Receive；除此之外的异常退避一下，别空转烧 CPU。
                    if (!_running || socket != _socket)
                    {
                        return;
                    }

                    Thread.Sleep(50);
                }
            }
        }

        /// <summary>收发两端（后台线程/主线程）共用的单调秒表，不依赖引擎时钟。</summary>
        private static float NowSeconds()
        {
            return Environment.TickCount64 / 1000f;
        }

        private static Dictionary<string, float> ParseFrame(string text)
        {
            JToken root;
            try
            {
                root = JToken.Parse(text);
            }
            catch (Exception)
            {
                return null;
            }

            JToken shapes = root is JObject obj
                ? obj["BlendShapes"] ?? obj["blendShapes"] ?? obj["blend_shapes"] ?? root
                : root;

            var values = new Dictionary<string, float>(64);
            if (shapes is JArray array)
            {
                foreach (JToken item in array)
                {
                    Collect(item as JObject, values);
                }
            }
            else
            {
                Collect(shapes as JObject, values);
            }

            return values;
        }

        private static void Collect(JObject obj, Dictionary<string, float> values)
        {
            if (obj == null)
            {
                return;
            }

            foreach (JProperty property in obj.Properties())
            {
                if (property.Value.Type != JTokenType.Float && property.Value.Type != JTokenType.Integer)
                {
                    continue;
                }

                float value = (float)property.Value;
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
                {
                    continue;
                }

                values[property.Name] = value;
            }
        }

        private void Apply(Dictionary<string, float> values)
        {
            MetaHumanFaceAnimator animator = ResolveAnimator();
            if (animator == null)
            {
                return;
            }

            int unmatched;
            int applied = animator.SetArkitChannels(values, out unmatched);
            LastChannelCount = values.Count;
            LastAppliedEntries = applied;
            LastUnmatchedChannels = unmatched;
            _lastApplied = values;
            FramesReceived++;

            if (DebugLog && (FramesReceived <= 3 || FramesReceived % 120 == 0))
            {
                Debug.Log("[FaceTracking] frame=" + FramesReceived + " channels=" + values.Count
                          + " applied=" + applied + " unmatched=" + unmatched);
            }
        }

        private MetaHumanFaceAnimator ResolveAnimator()
        {
            if (Animator != null)
            {
                return Animator;
            }

            Actor actor = Actor;
            while (actor != null)
            {
                MetaHumanFaceAnimator found = actor.GetScript<MetaHumanFaceAnimator>();
                if (found != null)
                {
                    Animator = found;
                    return found;
                }

                actor = actor.Parent;
            }

            return null;
        }
    }
}
