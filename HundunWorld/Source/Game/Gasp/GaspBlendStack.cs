using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 轻量 BlendStack：最多 4 个播放器，交叉淡入淡出，逐骨混合。
    /// 与图姿态的混合在 Evaluate 里完成：玩家总权重 &lt; 1 时按剩余权重回到当前图姿态。
    /// </summary>
    public sealed class GaspBlendStack
    {
        private sealed class Player
        {
            public int Clip;
            public float Time;
            public float Weight;
            public float Target;
            public float FadeIn;
            public float FadeOut;
            public bool Loop;
            public int Slot;
            public float PlayRate = 1f;

            /// <summary>骨骼掩码（null = 全骨骼）；用于上半身/下半身分层混合。</summary>
            public bool[] Mask;
            /// <summary>剖面名（事件回调用；空串表示匿名层）。</summary>
            public string Profile = "";
            public bool ReportedCompleted;
        }

        private const int MaxPlayers = 4;
        private readonly List<Player> _players = new List<Player>(MaxPlayers);
        private readonly GaspPoseBuffer[] _poses = new GaspPoseBuffer[MaxPlayers];
        private readonly bool[] _slotUsed = new bool[MaxPlayers];
        private float[] _scratch;
        private GaspSkeleton _skel;

        /// <summary>新层开始混入（参数为剖面名）。</summary>
        public event Action<string> BlendStarted;
        /// <summary>某层淡入到位（权重达到 1）。</summary>
        public event Action<string> BlendCompleted;
        /// <summary>某层淡出完毕并被移除。</summary>
        public event Action<string> BlendFinished;

        public int MainClip => _players.Count > 0 ? _players[_players.Count - 1].Clip : -1;
        public float MainTime => _players.Count > 0 ? _players[_players.Count - 1].Time : 0f;
        public int PlayerCount => _players.Count;

        /// <summary>设置主播放器播放速率（用于按实际移动速度抵消滑步）。</summary>
        public void SetMainPlayRate(float rate)
        {
            if (_players.Count > 0)
                _players[_players.Count - 1].PlayRate = Mathf.Clamp(rate, 0.2f, 2.5f);
        }

        public void Init(GaspSkeleton skeleton, int stride)
        {
            _skel = skeleton;
            _scratch = new float[stride];
            for (int i = 0; i < MaxPlayers; i++)
                _poses[i] = new GaspPoseBuffer(skeleton.Count);
        }

        public void Play(int clip, float time, bool loop, float fadeIn, float fadeOut)
            => PlayMasked(clip, time, loop, fadeIn, fadeOut, null, "", fadeExisting: true);

        /// <summary>
        /// 叠加层：新增带掩码的层但**不**淡出已有层（上层动作在基础运动之上混合）。
        /// 与掩码配合时，未被掩码覆盖的骨骼仍完全由基础层决定。
        /// </summary>
        public void PlayOverlay(int clip, float time, bool loop, float fadeIn, float fadeOut, bool[] mask, string profile)
            => PlayMasked(clip, time, loop, fadeIn, fadeOut, mask, profile, fadeExisting: false);

        /// <summary>
        /// 带骨骼掩码/剖面名的播放：掩码只让该层作用于指定骨骼子树（如只混上半身）。
        /// 掩码为 null 时等价于 <see cref="Play(int,float,bool,float,float)"/>。
        /// </summary>
        public void PlayMasked(int clip, float time, bool loop, float fadeIn, float fadeOut, bool[] mask, string profile, bool fadeExisting = true)
        {
            if (_players.Count > 0)
            {
                var main = _players[_players.Count - 1];
                if (main.Clip == clip)
                {
                    float delta = Mathf.Abs(main.Time - time);
                    if (delta < 0.12f)
                        return;
                }
            }

            if (fadeExisting)
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    _players[i].Target = 0f;
                    _players[i].FadeOut = Mathf.Max(0.05f, fadeOut);
                }
            }

            var player = new Player
            {
                Clip = clip,
                Time = time,
                Weight = 0f,
                Target = 1f,
                FadeIn = Mathf.Max(0.02f, fadeIn),
                FadeOut = Mathf.Max(0.05f, fadeOut),
                Loop = loop,
                Slot = AcquireSlot(),
                Mask = mask,
                Profile = profile ?? "",
            };
            _players.Add(player);
            TrimToMax();
            if (!string.IsNullOrEmpty(player.Profile))
                BlendStarted?.Invoke(player.Profile);
        }

        /// <summary>只淡出指定剖面名的层（叠加层移除，不影响基础层）。</summary>
        public void StopProfile(string profile, float fadeOut = 0.1f)
        {
            if (string.IsNullOrEmpty(profile))
                return;
            for (int i = 0; i < _players.Count; i++)
            {
                if (string.Equals(_players[i].Profile, profile, StringComparison.Ordinal))
                {
                    _players[i].Target = 0f;
                    _players[i].FadeOut = Mathf.Max(0.05f, fadeOut);
                }
            }
        }

        public void StopAll(float fadeOut)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                _players[i].Target = 0f;
                _players[i].FadeOut = Mathf.Max(0.05f, fadeOut);
            }
        }

        public void Update(float dt, GaspAnimationDatabase db)
        {
            for (int i = _players.Count - 1; i >= 0; i--)
            {
                var p = _players[i];
                p.Time += dt * Mathf.Max(0.05f, p.PlayRate);
                var clip = db.Clips[p.Clip];
                if (clip.Loop)
                {
                    if (p.Time > clip.Length)
                        p.Time %= clip.Length;
                }
                else if (p.Time > clip.Length)
                {
                    p.Time = clip.Length;
                }

                if (p.Target > p.Weight)
                {
                    p.Weight = Mathf.Min(1f, p.Weight + dt / p.FadeIn);
                }
                else if (p.Target < p.Weight)
                {
                    p.Weight = Mathf.Max(0f, p.Weight - dt / p.FadeOut);
                }

                if (!p.ReportedCompleted && p.Target >= 1f && p.Weight >= 0.999f)
                {
                    p.ReportedCompleted = true;
                    if (!string.IsNullOrEmpty(p.Profile))
                        BlendCompleted?.Invoke(p.Profile);
                }

                if (p.Weight <= 0.0005f && p.Target <= 0f)
                {
                    ReleaseSlot(p.Slot);
                    _players.RemoveAt(i);
                    if (!string.IsNullOrEmpty(p.Profile))
                        BlendFinished?.Invoke(p.Profile);
                }
            }
        }

        /// <summary>
        /// 输出混合后的**未转正**局部姿态。covered 节点由 GASP 接管；
        /// baseLocal 为当前动画图的原始局部矩阵（未做 Z-up 修正），用于淡入淡出衔接。
        /// </summary>
        public void Evaluate(GaspAnimationDatabase db, Matrix[] baseLocal, GaspPoseBuffer outPose)
        {
            outPose.Resize(_skel.Count);
            Array.Copy(_skel.Bind, outPose.Local, _skel.Count);
            Array.Clear(outPose.Covered, 0, _skel.Count);
            if (_players.Count == 0)
                return;

            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                var clip = db.Clips[p.Clip];
                GaspPoseUtil.SampleClip(clip, p.Time, _skel, _poses[p.Slot], _scratch);
            }

            for (int node = 0; node < _skel.Count; node++)
            {
                float total = 0f;
                Quaternion rot = Quaternion.Identity;
                Vector3 trans = Vector3.Zero;
                Vector3 scale = Vector3.Zero;
                bool hasRot = false;
                for (int i = 0; i < _players.Count; i++)
                {
                    var p = _players[i];
                    if (p.Weight <= 0f)
                        continue;
                    var pose = _poses[p.Slot];
                    if (p.Mask != null && !p.Mask[node])
                        continue;
                    if (!pose.Covered[node])
                        continue;
                    var t = pose.Local[node];
                    trans += t.Translation * p.Weight;
                    scale += new Vector3(t.Scale.X, t.Scale.Y, t.Scale.Z) * p.Weight;
                    if (!hasRot)
                    {
                        rot = t.Orientation;
                        hasRot = true;
                        total = p.Weight;
                    }
                    else
                    {
                        total += p.Weight;
                        rot = Quaternion.Slerp(rot, t.Orientation, p.Weight / Mathf.Max(total, 1e-5f));
                    }
                }
                if (!hasRot || total <= 0f)
                    continue;

                var blended = new Transform
                {
                    Translation = trans / total,
                    Orientation = Quaternion.Normalize(rot),
                };
                var s = scale / total;
                blended.Scale = new Float3(s.X, s.Y, s.Z);

                if (total < 0.999f && baseLocal != null)
                {
                    baseLocal[node].Decompose(out Transform baseT);
                    float w = Mathf.Saturate(total);
                    blended.Translation = Vector3.Lerp(baseT.Translation, blended.Translation, w);
                    blended.Orientation = Quaternion.Normalize(Quaternion.Slerp(baseT.Orientation, blended.Orientation, w));
                    blended.Scale = Float3.Lerp(baseT.Scale, blended.Scale, w);
                }

                outPose.Local[node] = blended;
                outPose.Covered[node] = true;
            }
        }

        private int AcquireSlot()
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (!_slotUsed[i])
                {
                    _slotUsed[i] = true;
                    return i;
                }
            }
            return 0;
        }

        private void ReleaseSlot(int slot)
        {
            if (slot >= 0 && slot < MaxPlayers)
                _slotUsed[slot] = false;
        }

        private void TrimToMax()
        {
            while (_players.Count > MaxPlayers)
            {
                var p = _players[0];
                ReleaseSlot(p.Slot);
                _players.RemoveAt(0);
            }
        }
    }
}
