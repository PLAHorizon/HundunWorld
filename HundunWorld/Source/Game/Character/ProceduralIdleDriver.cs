using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 世界角色的程序化待机（呼吸/轻微摆动）。
    /// <para>
    /// 为什么需要：工程的 `Animation Graph` 是空图（2174 B、deps=0），ALS 剪辑又与 DEF-* 骨架没有映射，
    /// 实测世界角色 1.2 s 内零骨骼位移（审计 6a2v）。这里在**骨架本身**上叠加极小幅度旋转，
    /// 不依赖动画图资产；一旦检测到真实动画在驱动骨架就自动让位（双向自检），以后接上真图不会打架。
    /// </para>
    /// </summary>
    public class ProceduralIdleDriver : Script
    {
        /// <summary>摆幅倍率（0 关闭）。</summary>
        public float AmplitudeScale = 1f;

        private AnimatedModel _model;
        private readonly Dictionary<string, Matrix> _basePose = new Dictionary<string, Matrix>();
        private readonly string[] _resolved = new string[IdleBoneSlots.Length];
        private bool _captured;
        private bool _active;
        private bool _loggedActive;
        private int _frames;
        private Matrix _gateMatrix;
        private int _gateTicks;

        private static readonly string[][] IdleBoneSlots =
        {
            new[] { "DEF-spine", "hips", "spine_01", "pelvis", "bip001 spine", "mixamorig:spine" },
            new[] { "DEF-spine.002", "spine1", "spine_02", "bip001 spine1", "mixamorig:spine1" },
            new[] { "DEF-spine.003", "spine2", "spine_03", "bip001 spine2", "mixamorig:spine2" },
            new[] { "DEF-spine.005", "neck", "neck_01", "bip001 neck", "mixamorig:neck" },
            new[] { "DEF-spine.006", "head", "bip001 head", "mixamorig:head" },
            new[] { "DEF-upper_arm.L", "leftarm", "upperarm_l", "left_upperarm", "bip001 l upperarm", "mixamorig:leftupperarm" },
            new[] { "DEF-upper_arm.R", "rightarm", "upperarm_r", "right_upperarm", "bip001 r upperarm", "mixamorig:rightupperarm" },
        };

        private static readonly float[] SlotFrequency = { 0.35f, 0.35f, 0.35f, 0.22f, 0.22f, 0.30f, 0.30f };
        private static readonly float[] SlotAmplitude = { 0.60f, 1.10f, 0.70f, 0.90f, 1.40f, 2.20f, 2.20f };
        private static readonly float[] SlotPhase = { 0f, 0f, 0.942f, 1.571f, 5.341f, 0f, 3.142f };

        public override void OnStart()
        {
            _model = Actor as AnimatedModel;
            if (_model == null)
            {
                Debug.LogWarning("[ProceduralIdle] 宿主不是 AnimatedModel，跳过");
                return;
            }

            _model.AnimationUpdated += OnAnimationUpdated;
        }

        public override void OnDisable()
        {
            if (_model != null)
            {
                _model.AnimationUpdated -= OnAnimationUpdated;
            }
            _captured = false;
            _active = false;
            _basePose.Clear();
        }

        public override void OnUpdate()
        {
            // 兜底：AnimationUpdated 不响时也保证每帧有一次机会（Tick 基于基准姿态，重复调用幂等）。
            Tick();
        }

        private void OnAnimationUpdated()
        {
            Tick();
        }

        private void Tick()
        {
            if (_model == null || !_model)
            {
                return;
            }

            // 有可用动画图时就优先用真动画：程序化待机只做"没有图"的兜底。
            // 否则它每帧覆盖 spine/手臂，会掩盖剪辑、并把自己的"真实动画检测"永远卡在无动画分支。
            if (_model.AnimationGraph != null && _model.AnimationGraph.IsLoaded)
            {
                return;
            }

            if (!_captured)
            {
                if (++_frames < 20)
                {
                    return;
                }
                CaptureBasePose();
                return;
            }

            float scale = Mathf.Max(0f, AmplitudeScale);
            if (scale <= 0f)
            {
                return;
            }

            if (!CheckRealAnimation())
            {
                return;
            }

            float t = Time.GameTime;
            for (int slot = 0; slot < _resolved.Length; slot++)
            {
                string bone = _resolved[slot];
                if (bone == null || !_basePose.TryGetValue(bone, out Matrix baseM))
                {
                    continue;
                }

                float angle = Mathf.Sin(t * SlotFrequency[slot] * Mathf.TwoPi + SlotPhase[slot])
                              * SlotAmplitude[slot] * scale;
                var delta = Quaternion.Euler(angle * 0.5f, angle, angle * 0.35f);
                _model.SetNodeTransformation(bone, baseM * Matrix.RotationQuaternion(delta), false);
            }
        }

        /// <summary>
        /// 双向自检：骨架被别人动过（真实动画/写回）就让位；连续一段时间没动才启用程序化待机。
        /// </summary>
        /// <returns>true 表示本帧应由程序化待机驱动。</returns>
        private bool CheckRealAnimation()
        {
            string probe = _resolved[1] ?? _resolved[0];
            if (probe == null || !_basePose.TryGetValue(probe, out Matrix baseM))
            {
                return false;
            }

            _model.GetNodeTransformation(probe, out Matrix now, false);
            if (_gateTicks == 0)
            {
                _gateMatrix = now;
            }

            if (++_gateTicks < 30)
            {
                return _active;
            }

            float diff = MaxAbsDiff(now, _gateMatrix);
            _gateTicks = 0;
            _gateMatrix = now;

            // 未激活：只要窗口内骨骼在动，就说明有别人在驱动（真实动画/写回），继续让位。
            // 已激活：我们的摆动幅度只有 ~0.05 矩阵量级，只有被推到远离 basePose 才判"真实动画接管"。
            // （写回端在装载时的一次性偏移会让 fromBase 一开始就非零，不能拿来当"正在动画"的判据。）
            float fromBase = MaxAbsDiff(now, baseM);
            bool realAnimation = _active ? fromBase > 0.3f : diff > 0.002f;

            if (realAnimation)
            {
                if (_active)
                {
                    _active = false;
                    Debug.Log("[ProceduralIdle] 检测到真实动画接管骨架，程序化待机让位");
                }
                return false;
            }

            if (!_active)
            {
                _active = true;
                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Debug.Log($"[ProceduralIdle] 骨架无动画驱动，启用程序化待机（骨骼 {_basePose.Count} 根）");
                }
            }
            return true;
        }

        private static float MaxAbsDiff(in Matrix a, in Matrix b)
        {
            return Math.Max(
                Math.Max(Math.Max(Math.Abs(a.M11 - b.M11), Math.Abs(a.M12 - b.M12)), Math.Abs(a.M13 - b.M13)),
                Math.Max(Math.Max(Math.Abs(a.M21 - b.M21), Math.Abs(a.M22 - b.M22)), Math.Abs(a.M23 - b.M23)));
        }

        private void CaptureBasePose()
        {
            _basePose.Clear();
            for (int i = 0; i < _resolved.Length; i++)
            {
                _resolved[i] = null;
            }

            SkinnedModel skin = _model.SkinnedModel;
            if (skin == null || !skin.IsLoaded)
            {
                _frames = 0;
                return;
            }

            for (int slot = 0; slot < IdleBoneSlots.Length; slot++)
            {
                string bone = FindBoneName(skin, IdleBoneSlots[slot]);
                if (bone == null)
                {
                    continue;
                }

                _model.GetNodeTransformation(bone, out Matrix m, false);
                if (m.M11 == 0f && m.M44 == 0f)
                {
                    continue;
                }

                _resolved[slot] = bone;
                _basePose[bone] = m;
            }

            _captured = _basePose.Count > 0;
            if (!_captured)
            {
                _frames = 0;
                Debug.LogWarning("[ProceduralIdle] 一根候选骨骼都没命中 → 角色会保持静态姿态");
                return;
            }

            Debug.Log($"[ProceduralIdle] 基准姿态捕获：{_basePose.Count}/{IdleBoneSlots.Length} 根（{_resolved[1] ?? _resolved[0]}）");
        }

        private static string FindBoneName(SkinnedModel model, string[] candidates)
        {
            if (model == null || model.Nodes == null)
            {
                return null;
            }

            for (int c = 0; c < candidates.Length; c++)
            {
                for (int i = 0; i < model.Nodes.Length; i++)
                {
                    string name = model.Nodes[i].Name;
                    if (!string.IsNullOrEmpty(name) &&
                        string.Equals(name, candidates[c], StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }
            return null;
        }
    }
}
