using System;
using System.Collections.Generic;
using FlaxEngine;
using MetaHuman;
using MetaHuman.Core;

namespace MetaHuman.Runtime
{
    /// <summary>
    /// 表情动画器：把脸部控制（SKM_Face.dna 抽出的 174 个 GUI 控制）的数值流
    /// 落到真实 blendshape 与面部骨骼上，支持多段叠加、镜像联动与外部驱动（追踪/音频）。
    ///
    /// 它是「骨骼动画自动绑定」在脸部的落地端：绑骨工具给出控制→落点的对照表，
    /// 这里按表把动画曲线的值流变成引擎能执行的写入，并统一交给写回端与捏脸形状相加，
    /// 避免表情把玩家自定义的形状覆盖掉。
    /// </summary>
    public class MetaHumanFaceAnimator : Script
    {
        [Tooltip("要驱动的宿主角色组件；留空则在父级链上自动找一个 MetaHumanCharacter")]
        [Serialize]
        public MetaHumanCharacter Owner;

        [Tooltip("镜像联动：写一侧控制时同对岸一起写（对应控制表里的 MirrorOf）")]
        [Serialize]
        public bool LinkMirrors = true;

        [Tooltip("走骨骼落点的控制每单位数值产生的位移（厘米）")]
        [Serialize]
        public float JointAmplitude = 0.05f;

        [Tooltip("自动播放一段内置眨眼/微笑，用于在没有动画资产的工程里目视验证通道是否接通")]
        [Serialize]
        public bool AutoPlayDemo;

        [Tooltip("自然眨眼：没有外部驱动时每隔几秒眨一次。UE 的 MetaHuman 常驻待机也会眨眼，"
            + "这里关掉就等于静态人偶。")]
        [Serialize]
        public bool AutoBlink = true;

        [Tooltip("两次眨眼之间的间隔范围（秒），每次在区间内随机取")]
        [Serialize]
        public float BlinkIntervalMin = 2.5f;

        [Serialize]
        public float BlinkIntervalMax = 6.0f;

        /// <summary>眨眼一次的开合时长（秒）：闭眼比睁眼快，这里用一个正弦包络近似。</summary>
        [Serialize]
        public float BlinkDuration = 0.22f;

        /// <summary>
        /// 调试用：周期性张嘴，验证张口平移通道是否真的落到脸上。
        /// 正式角色应关掉——真人不会每几秒把嘴张到 1cm。
        /// </summary>
        [Serialize]
        public bool AutoJawDemo;

        [Serialize]
        public float JawDemoInterval = 3.5f;

        [Serialize]
        public float JawDemoDuration = 1.2f;

        [Tooltip("调试：>=0 时强制眨眼值（0..1），用于像素级验证 morph 通路；<0 关闭。")]
        public float DebugBlink { get; set; } = -1f;

        [Tooltip("调试：>=0 时强制张口值（0..1）；<0 关闭。")]
        public float DebugJaw { get; set; } = -1f;

        [Tooltip("调试：morph 权重增益（1=原样）。用于确认'看不见'是幅度不足还是通路没生效。")]
        public float DebugMorphGain { get; set; } = 1f;

        [Tooltip("调试：每秒打一行 morph/写回端诊断。")]
        public bool DebugLog { get; set; }

        private float _blinkTime;
        private float _blinkStartAt = -999f;
        private float _nextBlinkAt = 3f;
        private readonly System.Random _blinkRandom = new System.Random();
        private float _autoBlinkValue;
        private string _blinkLeftId;
        private string _blinkRightId;
        private float _jawTime;
        private float _debugLogTimer;
        private float _autoJawValue;
        private string _jawId;

        private class Keyframe
        {
            public float Time;
            public float Value;
        }

        private class Clip
        {
            public string Name;
            public float Duration;
            public float Weight = 1f;
            public bool Looping = true;
            public float Playhead;
            public bool Playing;
            public readonly Dictionary<string, List<Keyframe>> Tracks = new Dictionary<string, List<Keyframe>>();
        }

        private readonly Dictionary<string, float> _manual = new Dictionary<string, float>();
        private readonly Dictionary<string, Clip> _clips = new Dictionary<string, Clip>();
        private readonly Dictionary<string, float> _layered = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _morphs = new Dictionary<string, float>();
        private readonly Dictionary<string, MetaHumanVec3> _offsets = new Dictionary<string, MetaHumanVec3>();
        private readonly Dictionary<string, MetaHumanBoneRotation> _rotations = new Dictionary<string, MetaHumanBoneRotation>();

        /// <summary>表情骨骼表（控制 → 眼睑骨旋转）。装载失败时为 null，表情只剩 morph 这一层。</summary>
        private MetaHumanExpressionRig _rig;

        private MetaHumanFaceControlTable _table;
        private int _unresolved;
        private int _rawTargetsWithoutMorph;
        private int _rotationBones;
        private int _translationBones;

        /// <summary>
        /// morph→GUI 表在本次 Rebuild 里取到的那份（取不到就是 null，不做每帧重试：
        /// 表缺失/校验不过会在装载点报错，这里只负责"这一帧用不用得上"）。
        /// </summary>
        private MetaHumanMorphGuiMap _morphGui;

        /// <summary>第二张表的左岸是 UE 网格族名；查表一律走 MetaHumanMorphBridge 同时问两张。</summary>
        private MetaHumanMorphGuiMap _ueMorphGui;

        private int _rawTargetsResolvedByMorphTable;

        /// <summary>
        /// 本轮交给写回端按资产名录定夺（直查或翻译）的形状落点数。
        /// 动画器刻意不自己判"这个形状资产里有没有"：名录只有写回端一份，两处各问一遍必然分裂。
        /// 计数与 <see cref="RawTargetsWithoutMorph"/> 分开——前者是"名字已送出、等定夺"，
        /// 后者是"连名字都没有"。
        /// </summary>
        private int _morphsDeferredToApplier;

        /// <summary>靠 morph→GUI 表补上的落点数（>0 就说明这张表在真头上确实在承重）。</summary>
        public int RawTargetsResolvedByMorphTable
        {
            get { return _rawTargetsResolvedByMorphTable; }
        }

        /// <summary>本轮送出的、需要写回端翻译或判缺失的形状落点数。</summary>
        public int MorphsDeferredToApplier
        {
            get { return _morphsDeferredToApplier; }
        }
        private bool _ownerResolved;

        public int UnresolvedCount { get { return _unresolved; } }

        /// <summary>换算后落到真实资产里不存在的 blendshape 上的 raw 个数（导入完整 MetaHuman 头才会归零）。</summary>
        public int RawTargetsWithoutMorph { get { return _rawTargetsWithoutMorph; } }

        public int ControlCount { get { return _table == null ? 0 : _table.Count; } }

        public override void OnEnable()
        {
            _table = MetaHumanTemplateStore.FaceControls;
            if (_table == null)
            {
                Debug.LogError("MetaHuman 表情动画器：脸部控制表装载失败，表情通道不会生效");
                return;
            }

            MetaHumanFaceControl blinkLeft = FindControl("CTRL_L_eye_blink.ty");
            MetaHumanFaceControl blinkRight = FindControl("CTRL_R_eye_blink.ty");
            _blinkLeftId = blinkLeft != null ? blinkLeft.Id : null;
            _blinkRightId = blinkRight != null ? blinkRight.Id : null;
            MetaHumanFaceControl jaw = FindControl("CTRL_C_jaw.ty");
            _jawId = jaw != null ? jaw.Id : null;

            _rig = MetaHumanTemplateStore.ExpressionRig;
            if (_rig == null)
            {
                // 不是致命错误：morph 那一层还能动，但实测幅度只有 0.1mm，所以必须让读者知道"看不见是为什么"。
                Debug.LogWarning("MetaHuman 表情动画器：表情骨骼表缺失，眨眼/眼睑只会走 morph（幅度约 0.1mm）");
            }

            ResolveOwner();

            if (AutoPlayDemo)
            {
                BuildDemoClips();
            }
        }

        public override void OnUpdate()
        {
            if (_table == null)
            {
                return;
            }

            if (!_ownerResolved)
            {
                ResolveOwner();
            }

            if (Owner == null)
            {
                return;
            }

            float deltaSeconds = Time.DeltaTime;
            bool dirty = false;
            foreach (Clip clip in _clips.Values)
            {
                if (!clip.Playing)
                {
                    continue;
                }

                clip.Playhead += deltaSeconds;
                if (clip.Playhead > clip.Duration)
                {
                    if (clip.Looping)
                    {
                        clip.Playhead -= clip.Duration;
                    }
                    else
                    {
                        clip.Playhead = clip.Duration;
                        clip.Playing = false;
                    }
                }

                dirty = true;
            }

            if (UpdateAutoBlink(deltaSeconds))
            {
                dirty = true;
            }

            if (UpdateAutoJaw(deltaSeconds))
            {
                dirty = true;
            }

            // 调试强制：绕开 AutoBlink 的"_rig==null 就不跑"，直接喂 morph 层做像素验证。
            if (DebugBlink >= 0f)
            {
                _autoBlinkValue = Mathf.Saturate(DebugBlink);
                dirty = true;
            }
            if (DebugJaw >= 0f)
            {
                _autoJawValue = Mathf.Saturate(DebugJaw);
                dirty = true;
            }

            if (dirty || _manual.Count > 0)
            {
                Rebuild();
            }
        }

        /// <summary>
        /// 外部驱动（面部追踪、音频 viseme、脚本表演）。找不到控制名/Id 时返回 false。
        /// 钳位用控制自己的量程：174 个里有 70 个是 [-1,1] 的双极控制
        /// （如 CTRL_L_eye_blink.ty 的负半轴是"睁大"），一律按 [0,1] 钳就抹掉了半张脸的动作。
        /// </summary>
        public bool SetControl(string controlNameOrId, float value)
        {
            MetaHumanFaceControl control = FindControl(controlNameOrId);
            if (control == null)
            {
                return false;
            }

            float low = control.RangeLow;
            float high = control.RangeHigh;
            if (value < low)
            {
                value = low;
            }
            else if (value > high)
            {
                value = high;
            }

            _manual[control.Id] = value;

            if (LinkMirrors && !string.IsNullOrEmpty(control.MirrorOf))
            {
                _manual[control.MirrorOf] = value;
            }

            Rebuild();
            return true;
        }

        /// <summary>按整张 GUI 控制表驱动（LiveLink 一类的外部输入），表里没给的控制取缺省值。</summary>
        public int SetGuiControls(Dictionary<string, float> guiControls)
        {
            if (guiControls == null || guiControls.Count == 0)
            {
                return 0;
            }

            int applied = 0;
            foreach (KeyValuePair<string, float> kv in guiControls)
            {
                if (SetControl(kv.Key, kv.Value))
                {
                    applied++;
                }
            }

            return applied;
        }

        /// <summary>
        /// 用一批 ARKit 通道值驱动（iPhone 原深、LiveLinkFace、各类 viseme 输出）。
        /// 值先按表反解成 GUI 控制值，再走与手动驱动完全相同的那条换算通路，
        /// 所以 blink/widen 这类「同一根控制的正负半轴」能被如实分开。
        /// 返回接上的条目数；没接上的通道数从 out 参数给出，调用方能看见而不是静默丢。
        /// </summary>
        public int SetArkitChannels(Dictionary<string, float> arkitValues, out int unmatchedChannels)
        {
            unmatchedChannels = 0;
            MetaHumanArkitMap map = MetaHuman.Core.MetaHumanTemplateStore.ArkitMap;
            if (map == null)
            {
                unmatchedChannels = arkitValues == null ? 0 : arkitValues.Count;
                return 0;
            }

            int skippedDeclined;
            int appliedEntries;
            Dictionary<string, float> guiValues = map.ToGuiControls(arkitValues, out unmatchedChannels, out skippedDeclined, out appliedEntries);
            foreach (KeyValuePair<string, float> kv in guiValues)
            {
                SetControl(kv.Key, kv.Value);
            }

            return appliedEntries;
        }

        public bool TryGetControl(string controlNameOrId, out float value)
        {
            MetaHumanFaceControl control = FindControl(controlNameOrId);
            if (control == null)
            {
                value = 0f;
                return false;
            }

            return _layered.TryGetValue(control.Id, out value) || _manual.TryGetValue(control.Id, out value);
        }

        /// <summary>登记一段关键帧动画：times/values 必须等长。</summary>
        public bool AddClip(string clipName, string controlNameOrId, float[] times, float[] values, bool looping = true)
        {
            if (string.IsNullOrEmpty(clipName) || times == null || values == null || times.Length != values.Length || times.Length < 2)
            {
                return false;
            }

            MetaHumanFaceControl control = FindControl(controlNameOrId);
            if (control == null)
            {
                return false;
            }

            Clip clip;
            if (!_clips.TryGetValue(clipName, out clip))
            {
                clip = new Clip { Name = clipName };
                _clips[clipName] = clip;
            }

            List<Keyframe> track = new List<Keyframe>();
            for (int i = 0; i < times.Length; i++)
            {
                track.Add(new Keyframe { Time = times[i], Value = values[i] });
                if (times[i] > clip.Duration)
                {
                    clip.Duration = times[i];
                }
            }

            clip.Tracks[control.Id] = track;
            return true;
        }

        public bool PlayClip(string clipName)
        {
            Clip clip;
            if (!clipNameToClip(clipName, out clip))
            {
                return false;
            }

            clip.Playhead = 0f;
            clip.Playing = true;
            Rebuild();
            return true;
        }

        public void StopClip(string clipName)
        {
            Clip clip;
            if (clipNameToClip(clipName, out clip))
            {
                clip.Playing = false;
                clip.Playhead = 0f;
                Rebuild();
            }
        }

        public bool SetClipWeight(string clipName, float weight)
        {
            Clip clip;
            if (!clipNameToClip(clipName, out clip))
            {
                return false;
            }

            if (weight < 0f)
            {
                weight = 0f;
            }
            else if (weight > 1f)
            {
                weight = 1f;
            }

            clip.Weight = weight;
            Rebuild();
            return true;
        }

        /// <summary>立刻把所有落点写回引擎。宿主角色的形状变了之后也要调它，否则表情停在旧值。</summary>
        public void Rebuild()
        {
            _layered.Clear();

            foreach (Clip clip in _clips.Values)
            {
                if (!clip.Playing)
                {
                    continue;
                }

                foreach (KeyValuePair<string, List<Keyframe>> track in clip.Tracks)
                {
                    float value = Sample(track.Value, clip.Playhead) * clip.Weight;
                    float existing;
                    _layered.TryGetValue(track.Key, out existing);
                    // 同一条控制被多段动画写到时取最大值：这是表情层叠加的通行做法，
                    // 相加会让半开的眼睛被两段动画叠成闭眼。
                    if (value > existing)
                    {
                        _layered[track.Key] = value;
                    }
                }
            }

            foreach (KeyValuePair<string, float> kv in _manual)
            {
                _layered[kv.Key] = kv.Value;
            }

            // 自动眨眼与外部驱动同层：有人显式写了更大的值就听那个，不覆盖。
            if (_autoBlinkValue > 0f)
            {
                if (_blinkLeftId != null)
                {
                    float existing;
                    _layered.TryGetValue(_blinkLeftId, out existing);
                    _layered[_blinkLeftId] = System.Math.Max(existing, _autoBlinkValue);
                }

                if (_blinkRightId != null)
                {
                    float existing;
                    _layered.TryGetValue(_blinkRightId, out existing);
                    _layered[_blinkRightId] = System.Math.Max(existing, _autoBlinkValue);
                }
            }

            // 口型演示也必须在这一层合并：放到循环后面写只会改数字，不会落到骨骼（10-23 踩过）。
            if (_autoJawValue > 0f && _jawId != null)
            {
                float existing;
                _layered.TryGetValue(_jawId, out existing);
                _layered[_jawId] = System.Math.Max(existing, _autoJawValue);
            }

            _morphs.Clear();
            _offsets.Clear();
            _rotations.Clear();
            _rotationBones = 0;
            _translationBones = 0;
            _unresolved = 0;
            _rawTargetsWithoutMorph = 0;
            _rawTargetsResolvedByMorphTable = 0;
            _morphsDeferredToApplier = 0;
            _morphGui = MetaHumanTemplateStore.MorphGui;
            _ueMorphGui = MetaHumanTemplateStore.UeMorphGui;

            foreach (KeyValuePair<string, float> kv in _layered)
            {
                MetaHumanFaceControl control = _table.Find(kv.Key);
                if (control == null)
                {
                    _unresolved++;
                    continue;
                }

                if (System.Math.Abs(kv.Value) < 1e-6f)
                {
                    continue;
                }

                // 眼睑/眼球这类控制同时有 morph 与骨骼两条通路：骨骼这一层是主运动，
                // 有骨骼表时就不再叠控制表里那条"把眼球整体平移"的旧落点（它会把眼球拖走）。
                MetaHumanExpressionChannel rigChannel = _rig != null ? _rig.Find(control.ControlName) : null;
                if (rigChannel != null)
                {
                    ApplyRigChannel(rigChannel, kv.Value);
                }

                if (control.RawTargets != null && control.RawTargets.Count > 0)
                {
                    // 有权威换算段就走 GUI→raw 这条路：双极控制的正负半轴会落到不同的 raw，
                    // 折线段还能表达阶段间的交叉淡入淡出。
                    WriteConverted(control, kv.Value);
                }
                else
                {
                    // 控制表直接给了形状名（真头上多为 .dna 族 head_wmN_*）。这里既不查资产名录也不翻译：
                    // 名录只有写回端一份（MetaHumanAppearanceApplier._availableMorphs），它在落地前会按
                    // MetaHumanMorphBridge 翻译一次，翻不出来才记 SkippedMorphCount 并给「morph 缺失」告警。
                    // 动画器再问一遍就是第二个口径，最难查的分裂正是"一处支持双岸、另一处只支持单岸"。
                    // 只把数量记下来：这一支过去在报告里被当成"已解析落点"，读者据此以为资产里真有。
                    for (int i = 0; i < control.MorphTargets.Count; i++)
                    {
                        float existing;
                        _morphs.TryGetValue(control.MorphTargets[i], out existing);
                        _morphs[control.MorphTargets[i]] = existing + kv.Value;
                        _morphsDeferredToApplier++;
                    }
                }

                if (control.Joints.Count > 0 && rigChannel == null)
                {
                    MetaHumanVec3 axis = AxisFor(control.Axis);
                    MetaHumanVec3 offset = new MetaHumanVec3(
                        axis.X * JointAmplitude * kv.Value,
                        axis.Y * JointAmplitude * kv.Value,
                        axis.Z * JointAmplitude * kv.Value);

                    for (int i = 0; i < control.Joints.Count; i++)
                    {
                        _offsets[control.Joints[i]] = offset;
                    }
                }
            }

            if (System.Math.Abs(DebugMorphGain - 1f) > 1e-6f && _morphs.Count > 0)
            {
                var keys = new System.Collections.Generic.List<string>(_morphs.Keys);
                for (int i = 0; i < keys.Count; i++)
                    _morphs[keys[i]] = _morphs[keys[i]] * DebugMorphGain;
            }

            if (Owner != null)
            {
                Owner.PushExpression(_morphs, _offsets, _rotations);
            }

            if (DebugLog)
            {
                _debugLogTimer += Time.DeltaTime;
                if (_debugLogTimer >= 1f)
                {
                    _debugLogTimer = 0f;
                    string first = "none";
                    foreach (var kv in _morphs)
                    {
                        first = kv.Key + "=" + kv.Value.ToString("F3");
                        break;
                    }
                    Debug.Log("[FaceAnimator] morphs=" + _morphs.Count + " deferred=" + _morphsDeferredToApplier
                              + " owner=" + (Owner != null) + " appliers=" + (Owner != null ? Owner.Appliers.Count : 0)
                              + " blink=" + _autoBlinkValue.ToString("F2") + " jaw=" + _autoJawValue.ToString("F2")
                              + " gain=" + DebugMorphGain.ToString("F2") + " first=" + first);
                }
            }
        }

        /// <summary>调试用张口演示：张一下、停一下。正式角色关掉（普通人不该每几秒把嘴张到 1cm）。</summary>
        private bool UpdateAutoJaw(float deltaSeconds)
        {
            if (!AutoJawDemo || _rig == null || _jawId == null)
            {
                return false;
            }

            _jawTime += deltaSeconds;
            float cycle = Mathf.Max(0.5f, JawDemoInterval) + Mathf.Max(0.2f, JawDemoDuration);
            float phase = _jawTime % cycle;
            float value = 0f;
            if (phase <= JawDemoDuration && JawDemoDuration > 0.01f)
            {
                value = (float)System.Math.Sin(System.Math.PI * phase / JawDemoDuration);
            }

            if (System.Math.Abs(value - _autoJawValue) < 1e-4f)
            {
                return false;
            }

            _autoJawValue = value;
            return true;
        }

        /// <summary>
        /// 自然眨眼。返回 true 表示这一帧的值变了、需要重写一次表情。
        /// 没有骨骼表时直接不跑：只走 morph 的眨眼实测 0.1mm，动了也看不见，不如别白算。
        /// </summary>
        private bool UpdateAutoBlink(float deltaSeconds)
        {
            if (!AutoBlink || _rig == null || _blinkLeftId == null)
            {
                return false;
            }

            _blinkTime += deltaSeconds;
            if (_blinkTime >= _nextBlinkAt)
            {
                _blinkStartAt = _blinkTime;
                float min = Mathf.Max(0.2f, BlinkIntervalMin);
                float max = Mathf.Max(min, BlinkIntervalMax);
                _nextBlinkAt = _blinkTime + min + (float)_blinkRandom.NextDouble() * (max - min) + BlinkDuration;
            }

            float elapsed = _blinkTime - _blinkStartAt;
            float value = 0f;
            if (elapsed >= 0f && elapsed <= BlinkDuration && BlinkDuration > 0.01f)
            {
                // 正弦包络：0→1→0，比线性更像真实眨眼（闭合快、张开略慢由后半段承担）。
                value = (float)System.Math.Sin(System.Math.PI * elapsed / BlinkDuration);
            }

            if (System.Math.Abs(value - _autoBlinkValue) < 1e-4f)
            {
                return false;
            }

            _autoBlinkValue = value;
            return true;
        }

        /// <summary>
        /// 把一条控制值摊到表情骨骼表里的骨上。每条骨记的是"绕支点转多少"，
        /// 支点骨由表给出（眼睑用眼球骨）——绕骨自己的头转几乎不产生睑缘位移（实测 60° 只有 0.25mm）。
        /// </summary>
        private void ApplyRigChannel(MetaHumanExpressionChannel channel, float value)
        {
            if (System.Math.Abs(value) < 1e-6f)
            {
                return;
            }

            // 平移通道（张口）：表里写的是物理厘米，而写回端对 FACIAL_ 骨会乘 FaceBoneGain，
            // 所以这里先除掉增益；这样改增益时表里的厘米数仍然是对的。
            // 一组骨可以有多段（下唇 3cm、下齿 1cm 各自走），每段独立乘控制值。
            if (channel.Translates != null)
            {
                float gain = MetaHumanAppearanceApplier.FaceBoneGain;
                for (int t = 0; t < channel.Translates.Count; t++)
                {
                    MetaHumanExpressionTranslate translate = channel.Translates[t];
                    if (translate == null || translate.Bones == null || translate.Bones.Count == 0
                        || translate.DeltaAtOneCm == null || translate.DeltaAtOneCm.Count < 3)
                    {
                        continue;
                    }

                    MetaHumanVec3 delta = new MetaHumanVec3(
                        translate.DeltaAtOneCm[0] * value / gain,
                        translate.DeltaAtOneCm[1] * value / gain,
                        translate.DeltaAtOneCm[2] * value / gain);
                    for (int b = 0; b < translate.Bones.Count; b++)
                    {
                        string bone = translate.Bones[b];
                        if (string.IsNullOrEmpty(bone))
                        {
                            continue;
                        }

                        MetaHumanVec3 existing;
                        if (_offsets.TryGetValue(bone, out existing))
                        {
                            existing.Add(delta);
                            _offsets[bone] = existing;
                        }
                        else
                        {
                            _offsets[bone] = delta;
                        }

                        _translationBones++;
                    }
                }
            }

            if (channel.Axis == null || channel.Axis.Count < 3 || channel.Groups == null
                || string.IsNullOrEmpty(channel.PivotBone))
            {
                return;
            }

            MetaHumanVec3 axis = new MetaHumanVec3(channel.Axis[0], channel.Axis[1], channel.Axis[2]);
            for (int g = 0; g < channel.Groups.Count; g++)
            {
                MetaHumanExpressionGroup group = channel.Groups[g];
                if (group == null || group.Bones == null || group.Bones.Count == 0
                    || System.Math.Abs(group.AngleAtOne) < 1e-6f)
                {
                    continue;
                }

                MetaHumanQuat rotation = MetaHumanQuat.FromAxisAngle(
                    axis, group.AngleAtOne * value * (float)(System.Math.PI / 180.0));
                if (rotation.IsIdentity)
                {
                    continue;
                }

                for (int b = 0; b < group.Bones.Count; b++)
                {
                    string bone = group.Bones[b];
                    if (string.IsNullOrEmpty(bone))
                    {
                        continue;
                    }

                    MetaHumanBoneRotation existing;
                    if (_rotations.TryGetValue(bone, out existing))
                    {
                        existing.Rotation = MetaHumanQuat.Multiply(existing.Rotation, rotation);
                        _rotations[bone] = existing;
                    }
                    else
                    {
                        _rotations[bone] = new MetaHumanBoneRotation
                        {
                            Rotation = rotation,
                            PivotBone = channel.PivotBone,
                        };
                    }

                    _rotationBones++;
                }
            }
        }

        /// <summary>本轮落到骨上的旋转条数（同一根骨被两条控制写到会算两次，用来判断表在不在承重）。</summary>
        public int RotationBonesLastPass { get { return _rotationBones; } }

        /// <summary>
        /// 按 UE 的换算语义把一个 GUI 控制值摊到它各自的 raw 落点上。
        /// 与 MetaHumanGuiToRawMap.ConvertInto 是同一套规则，差别只在这里的值来自控制表自带的段，
        /// 不必在运行期加载全量 251 控制表（cooked 包里少一份散文件）。
        /// </summary>
        private void WriteConverted(MetaHumanFaceControl control, float guiValue)
        {
            float low = control.RangeLow;
            float high = control.RangeHigh;

            for (int t = 0; t < control.RawTargets.Count; t++)
            {
                MetaHumanFaceRawTarget target = control.RawTargets[t];
                if (target == null || target.Segments == null)
                {
                    continue;
                }

                float contribution = 0f;
                for (int s = 0; s < target.Segments.Count; s++)
                {
                    List<float> segment = target.Segments[s];
                    if (segment == null || segment.Count < 4)
                    {
                        continue;
                    }

                    float from = segment[0];
                    float to = segment[1];
                    float slope = segment[2];
                    float cut = segment[3];

                    if (from <= guiValue && guiValue < to)
                    {
                        contribution += slope * guiValue + cut;
                    }
                    else if (from == low && guiValue < from)
                    {
                        contribution += slope * from + cut;
                    }
                    else if (to == high && guiValue >= to)
                    {
                        contribution += slope * to + cut;
                    }
                }

                if (System.Math.Abs(contribution) < 1e-6f)
                {
                    continue;
                }

                if (target.MorphTargets == null || target.MorphTargets.Count == 0)
                {
                    // 控制表没给这个 raw 落点配 blendshape 时，查两张 morph→GUI 表：
                    // 真头的 morph 名与 raw 控制名不同族（.dna 族与 UE 网格族同名命中 0/37），
                    // 而"哪张表先查、怎么并起来去重"只在 MetaHumanMorphBridge 一处，免得与写回端各走各的。
                    List<string> bridged = MetaHumanMorphBridge.MorphsForGui(
                        control.ControlName, _morphGui, _ueMorphGui);
                    if (bridged.Count > 0)
                    {
                        for (int i = 0; i < bridged.Count; i++)
                        {
                            float bridgedExisting;
                            _morphs.TryGetValue(bridged[i], out bridgedExisting);
                            _morphs[bridged[i]] = bridgedExisting + contribution;
                        }

                        _rawTargetsResolvedByMorphTable++;
                        continue;
                    }

                    // 桥没给名字时过去直接丢弃这段换算结果：换算是对的，只是这张表在当前资产里对不上，
                    // 而"对不上"和"没人管"在报告里同形。raw 名（brow_down_L 这一族）本身就是 morph→GUI
                    // 表的左岸键，写回端拿着它还能再翻译一次，所以改成送出去定夺：
                    // 翻译成功就在写回端记 bridged，失败就变成可见的 skipped + 告警，不再在这里静默归零。
                    if (!string.IsNullOrEmpty(target.Raw))
                    {
                        float rawExisting;
                        _morphs.TryGetValue(target.Raw, out rawExisting);
                        _morphs[target.Raw] = rawExisting + contribution;
                        _morphsDeferredToApplier++;
                    }

                    _rawTargetsWithoutMorph++;
                    continue;
                }

                for (int i = 0; i < target.MorphTargets.Count; i++)
                {
                    float existing;
                    _morphs.TryGetValue(target.MorphTargets[i], out existing);
                    _morphs[target.MorphTargets[i]] = existing + contribution;
                }
            }
        }

        private static MetaHumanVec3 AxisFor(string axis)
        {
            if (axis == "tx")
            {
                return new MetaHumanVec3(1f, 0f, 0f);
            }

            if (axis == "tz")
            {
                return new MetaHumanVec3(0f, 0f, 1f);
            }

            return new MetaHumanVec3(0f, 1f, 0f);
        }

        private static float Sample(List<Keyframe> track, float time)
        {
            if (track.Count == 0)
            {
                return 0f;
            }

            if (time <= track[0].Time)
            {
                return track[0].Value;
            }

            for (int i = 1; i < track.Count; i++)
            {
                if (time <= track[i].Time)
                {
                    Keyframe a = track[i - 1];
                    Keyframe b = track[i];
                    float span = b.Time - a.Time;
                    if (span <= 0f)
                    {
                        return b.Value;
                    }

                    float t = (time - a.Time) / span;
                    return a.Value + (b.Value - a.Value) * t;
                }
            }

            return track[track.Count - 1].Value;
        }

        private bool clipNameToClip(string clipName, out Clip clip)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                clip = null;
                return false;
            }

            return _clips.TryGetValue(clipName, out clip);
        }

        private MetaHumanFaceControl FindControl(string controlNameOrId)
        {
            if (_table == null || string.IsNullOrEmpty(controlNameOrId))
            {
                return null;
            }

            return _table.Find(controlNameOrId);
        }

        private void ResolveOwner()
        {
            if (Owner != null)
            {
                _ownerResolved = true;
                return;
            }

            Actor current = Actor != null ? Actor.Parent : null;
            while (current != null)
            {
                MetaHumanCharacter[] characters = current.GetScripts<MetaHumanCharacter>();
                if (characters.Length > 0)
                {
                    Owner = characters[0];
                    _ownerResolved = true;
                    return;
                }

                current = current.Parent;
            }

            _ownerResolved = true;
        }

        private void BuildDemoClips()
        {
            // 不硬写控制名：控制表里的名字来自 SKM_Face.dna，直接按关键子串挑真实存在的条目，
            // 挑不到就如实说明，而不是钉一个猜出来的 Id 让这段变成假演示。
            string blink = FirstControlContaining("blink");
            string smile = FirstControlContaining("smile");

            if (blink != null)
            {
                AddClip("demo_blink", blink, new float[] { 0f, 0.12f, 0.24f }, new float[] { 0f, 1f, 0f });
                PlayClip("demo_blink");
            }

            if (smile != null)
            {
                AddClip("demo_smile", smile, new float[] { 0f, 0.4f, 1.2f, 1.6f }, new float[] { 0f, 0.6f, 0.6f, 0f });
                PlayClip("demo_smile");
            }

            Debug.Log("[MetaHuman] 演示动画：眨眼=" + (blink ?? "未找到对应控制") + " 微笑=" + (smile ?? "未找到对应控制"));
        }

        private string FirstControlContaining(string token)
        {
            if (_table == null)
            {
                return null;
            }

            foreach (MetaHumanFaceControl control in _table.Controls)
            {
                if (control.Resolution == "unresolved")
                {
                    continue;
                }

                if (control.ControlName != null && control.ControlName.ToLowerInvariant().Contains(token))
                {
                    return control.Id;
                }
            }

            return null;
        }

        public string BuildDiagnosticReport()
        {
            return "MetaHumanFaceAnimator[" + (Actor != null ? Actor.Name : "<无>") + "] 控制表="
                + ControlCount + " 送出落点=" + (_morphs.Count + _offsets.Count)
                + "（其中待写回端定名/翻译=" + _morphsDeferredToApplier + "）"
                + " 未解析控制=" + _unresolved
                + " 换算后无同名形状=" + _rawTargetsWithoutMorph
                + " 由 morph→GUI 表补上=" + _rawTargetsResolvedByMorphTable
                + " 骨骼旋转落点=" + _rotationBones
                + " 骨骼平移落点=" + _translationBones
                + " 表情骨骼表=" + (_rig == null ? "缺失" : _rig.ChannelCount.ToString())
                + " 片段=" + _clips.Count
                + " 宿主=" + (Owner != null ? "已绑定" : "未找到 MetaHumanCharacter")
                + "；写回端回执：" + (Owner == null ? "无宿主" : Owner.MorphReceiptText());
        }
    }
}
