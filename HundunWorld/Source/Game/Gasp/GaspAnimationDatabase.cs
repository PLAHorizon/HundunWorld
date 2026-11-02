using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    public enum GaspClipTag : byte
    {
        Ground = 0,
        Air = 1,
        Traversal = 2,
    }

    /// <summary>
    /// 一个剪辑里的单条骨骼通道。NodeIndex 由 <see cref="GaspSkeleton"/> 在加载后按名解析，
    /// -1 表示这具骨架没有对应节点（例如 ALS 的 ik_hand_r）。
    /// </summary>
    public sealed class GaspChannel
    {
        public string Name;
        public int Flags;
        public int NodeIndex = -1;
        public int Offset;
    }

    /// <summary>
    /// 离线烘焙的动画剪辑：30Hz 采样的局部 TRS 曲线。
    /// 数据由 Tools/GaspBakeAnimations.py 生成，格式见 docs/GASP_ANIMATION_SYSTEM_DESIGN.md。
    /// </summary>
    public sealed class GaspClip
    {
        public const int FlagPosition = 1;
        public const int FlagRotation = 2;
        public const int FlagScale = 4;

        public string Name;
        public string Path;
        public byte[] Guid;
        public float Length;
        public float Fps;
        public bool Loop;
        public GaspClipTag Tag;
        /// <summary>步态族：0=非步态，1=走，2=跑，3=冲刺。</summary>
        public byte Gait;
        public GaspChannel[] Channels;
        public int FrameCount;
        public int Stride;
        public float[] Data;
        public int SampleRate;

        public float TimePerFrame => 1f / SampleRate;
        public float LoopLength => Length;

        public int FrameAt(float time)
        {
            if (FrameCount <= 1)
                return 0;
            if (Loop)
            {
                time %= Length;
                if (time < 0f)
                    time += Length;
                int f = (int)(time * SampleRate + 0.5f);
                return f % FrameCount;
            }
            int frame = (int)(time * SampleRate + 0.5f);
            return Mathf.Clamp(frame, 0, FrameCount - 1);
        }

        /// <summary>按时间插值采样到 dst（长度 = Stride，通道顺序与 Channels 一致）。</summary>
        public void Sample(float time, float[] dst)
        {
            if (FrameCount <= 0)
                return;
            float wrapped = time;
            if (Loop)
            {
                wrapped %= Length;
                if (wrapped < 0f)
                    wrapped += Length;
            }
            else if (wrapped < 0f)
            {
                wrapped = 0f;
            }
            else if (wrapped > Length)
            {
                wrapped = Length;
            }

            float framePos = wrapped * SampleRate;
            int i0 = (int)Math.Floor(framePos);
            float alpha = framePos - i0;
            if (Loop)
            {
                i0 %= FrameCount;
                if (i0 < 0)
                    i0 += FrameCount;
            }
            else
            {
                i0 = Mathf.Clamp(i0, 0, FrameCount - 1);
            }
            int i1 = i0 + 1;
            if (i1 >= FrameCount)
            {
                if (Loop)
                    i1 = 0;
                else
                {
                    i1 = FrameCount - 1;
                    alpha = 0f;
                }
            }

            int a = i0 * Stride;
            int b = i1 * Stride;
            if (alpha <= 0.0001f || a == b)
            {
                Array.Copy(Data, a, dst, 0, Stride);
                return;
            }

            for (int c = 0; c < Channels.Length; c++)
            {
                var ch = Channels[c];
                int o = ch.Offset;
                if ((ch.Flags & FlagPosition) != 0)
                {
                    dst[o] = Mathf.Lerp(Data[a + o], Data[b + o], alpha);
                    dst[o + 1] = Mathf.Lerp(Data[a + o + 1], Data[b + o + 1], alpha);
                    dst[o + 2] = Mathf.Lerp(Data[a + o + 2], Data[b + o + 2], alpha);
                }
                int r = o + (((ch.Flags & FlagPosition) != 0) ? 3 : 0);
                if ((ch.Flags & FlagRotation) != 0)
                {
                    var qa = new Quaternion(Data[a + r], Data[a + r + 1], Data[a + r + 2], Data[a + r + 3]);
                    var qb = new Quaternion(Data[b + r], Data[b + r + 1], Data[b + r + 2], Data[b + r + 3]);
                    var q = Quaternion.Slerp(qa, qb, alpha);
                    dst[r] = q.X;
                    dst[r + 1] = q.Y;
                    dst[r + 2] = q.Z;
                    dst[r + 3] = q.W;
                }
                int s = r + (((ch.Flags & FlagRotation) != 0) ? 4 : 0);
                if ((ch.Flags & FlagScale) != 0)
                {
                    dst[s] = Mathf.Lerp(Data[a + s], Data[b + s], alpha);
                    dst[s + 1] = Mathf.Lerp(Data[a + s + 1], Data[b + s + 1], alpha);
                    dst[s + 2] = Mathf.Lerp(Data[a + s + 2], Data[b + s + 2], alpha);
                }
            }
        }
    }

    /// <summary>
    /// 运行时动画库。加载失败时 <see cref="IsLoaded"/> 为 false，调用方应整条 GASP 链路停用。
    /// </summary>
    public sealed class GaspAnimationDatabase
    {
        public const uint Version = 1;
        public static readonly byte[] Magic = { (byte)'G', (byte)'A', (byte)'S', (byte)'P', (byte)'D', (byte)'B', (byte)'1', 0 };

        public readonly List<GaspClip> Clips = new List<GaspClip>();
        public float SampleRate = 30f;
        public int MaxStride;
        public string SourcePath;
        public bool IsLoaded { get; private set; }
        public string Error { get; private set; }

        public static GaspAnimationDatabase Load(string path)
        {
            var db = new GaspAnimationDatabase { SourcePath = path };
            try
            {
                db.Parse(File.ReadAllBytes(path));
                db.IsLoaded = true;
            }
            catch (Exception ex)
            {
                db.Error = ex.GetType().Name + ": " + ex.Message;
                db.IsLoaded = false;
            }
            return db;
        }

        /// <summary>从 Content 的 RawDataAsset 加载（打包版首选路径）。</summary>
        public static GaspAnimationDatabase LoadFromContent(string assetPath = DefaultContentAssetPath)
        {
            var db = new GaspAnimationDatabase { SourcePath = "asset:" + assetPath };
            try
            {
                var asset = Content.Load<RawDataAsset>(assetPath, 15000.0);
                if (asset == null || !asset.IsLoaded || asset.Data == null || asset.Data.Length == 0)
                    throw new InvalidDataException("RawDataAsset 未加载或为空");
                db.Parse(asset.Data);
                db.IsLoaded = true;
            }
            catch (Exception ex)
            {
                db.Error = ex.GetType().Name + ": " + ex.Message;
                db.IsLoaded = false;
            }
            return db;
        }

        public const string DefaultContentAssetPath = "Content/Gasp/gasp_anims.flax";

        public static string ResolveDefaultPath()
        {
            string project = Globals.ProjectFolder;
            string p = Path.Combine(project, "Content", "Gasp", "gasp_anims.bin");
            if (File.Exists(p))
                return p;
            string p2 = Path.Combine(project, "..", "Content", "Gasp", "gasp_anims.bin");
            return File.Exists(p2) ? p2 : p;
        }

        private void Parse(byte[] data)
        {
            if (data.Length < 20)
                throw new InvalidDataException("文件过短");
            for (int i = 0; i < Magic.Length; i++)
            {
                if (data[i] != Magic[i])
                    throw new InvalidDataException("magic 不匹配");
            }
            int off = 8;
            uint version = ReadU32(data, ref off);
            if (version != Version)
                throw new InvalidDataException("不支持的版本 " + version);
            SampleRate = ReadF32(data, ref off);
            int clipCount = (int)ReadU32(data, ref off);
            if (clipCount <= 0 || clipCount > 512)
                throw new InvalidDataException("剪辑数量非法 " + clipCount);

            for (int ci = 0; ci < clipCount; ci++)
            {
                var clip = new GaspClip
                {
                    Name = ReadString(data, ref off),
                    Path = ReadString(data, ref off),
                    Guid = ReadBytes(data, ref off, 16),
                    Length = ReadF32(data, ref off),
                    Fps = ReadF32(data, ref off),
                    SampleRate = (int)Mathf.Round(SampleRate),
                };
                byte flags = data[off++];
                clip.Loop = (flags & 1) != 0;
                clip.Tag = (GaspClipTag)((flags >> 1) & 7);
                if (clip.Name.IndexOf("Sprint", StringComparison.OrdinalIgnoreCase) >= 0)
                    clip.Gait = 3;
                else if (clip.Name.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0)
                    clip.Gait = 2;
                else if (clip.Name.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0)
                    clip.Gait = 1;
                int channelCount = (int)ReadU32(data, ref off);
                if (channelCount <= 0 || channelCount > 4096)
                    throw new InvalidDataException(clip.Name + " 通道数非法");
                clip.Channels = new GaspChannel[channelCount];
                int stride = 0;
                for (int i = 0; i < channelCount; i++)
                {
                    var ch = new GaspChannel
                    {
                        Name = ReadString(data, ref off),
                        Flags = data[off++],
                    };
                    ch.Offset = stride;
                    stride += ((ch.Flags & GaspClip.FlagPosition) != 0 ? 3 : 0)
                              + ((ch.Flags & GaspClip.FlagRotation) != 0 ? 4 : 0)
                              + ((ch.Flags & GaspClip.FlagScale) != 0 ? 3 : 0);
                    clip.Channels[i] = ch;
                }
                clip.Stride = stride;
                int frameCount = (int)ReadU32(data, ref off);
                if (frameCount <= 0 || frameCount > 100000)
                    throw new InvalidDataException(clip.Name + " 帧数非法");
                clip.FrameCount = frameCount;
                int floatCount = frameCount * stride;
                if (off + floatCount * 4 > data.Length)
                    throw new InvalidDataException(clip.Name + " 数据越界");
                clip.Data = new float[floatCount];
                Buffer.BlockCopy(data, off, clip.Data, 0, floatCount * 4);
                off += floatCount * 4;
                Clips.Add(clip);
                if (stride > MaxStride)
                    MaxStride = stride;
            }
        }

        private static int ReadI32(byte[] d, ref int o) { int v = BitConverter.ToInt32(d, o); o += 4; return v; }
        private static uint ReadU32(byte[] d, ref int o) { uint v = BitConverter.ToUInt32(d, o); o += 4; return v; }
        private static float ReadF32(byte[] d, ref int o) { float v = BitConverter.ToSingle(d, o); o += 4; return v; }
        private static byte[] ReadBytes(byte[] d, ref int o, int n)
        {
            var r = new byte[n];
            Buffer.BlockCopy(d, o, r, 0, n);
            o += n;
            return r;
        }
        private static string ReadString(byte[] d, ref int o)
        {
            int len = BitConverter.ToUInt16(d, o);
            o += 2;
            string s = System.Text.Encoding.UTF8.GetString(d, o, len);
            o += len;
            return s;
        }
    }
}
