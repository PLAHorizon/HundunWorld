using System;
using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>一根骨骼的累积形变算子：最终缩放 = 1 + ScaleDelta，最终平移 = Translate。</summary>
    public class MetaHumanBoneOp
    {
        public MetaHumanVec3 ScaleDelta;
        public MetaHumanVec3 Translate;

        /// <summary>
        /// 模型空间附加旋转（表情通道用）。绕 <see cref="PivotBone"/> 的当前位置转，
        /// 而不是绕本骨自己的头——眼睑是绕眼球中心转的，绕骨自己转几乎不产生位移。
        /// </summary>
        public MetaHumanQuat Rotation = MetaHumanQuat.Identity;

        public string PivotBone;

        public MetaHumanVec3 FinalScale
        {
            get
            {
                return new MetaHumanVec3(1f + ScaleDelta.X, 1f + ScaleDelta.Y, 1f + ScaleDelta.Z);
            }
        }

        public bool IsIdentity
        {
            get
            {
                return ScaleDelta.X == 0f && ScaleDelta.Y == 0f && ScaleDelta.Z == 0f
                    && Translate.X == 0f && Translate.Y == 0f && Translate.Z == 0f
                    && Rotation.IsIdentity;
            }
        }

        /// <summary>
        /// 复制一份。表情通道要把量叠到同一根骨上时必须先克隆：
        /// composed.Bones 里可能是解算器**持有并复用**的对象，就地 Add 会让表达式每帧累加
        /// （2026-10-23 实测：张口 0.5s 内从 1.5cm 涨到 6.5cm，再继续涨到几米）。
        /// </summary>
        public MetaHumanBoneOp Clone()
        {
            MetaHumanBoneOp copy = new MetaHumanBoneOp();
            copy.ScaleDelta = ScaleDelta;
            copy.Translate = Translate;
            copy.Rotation = Rotation;
            copy.PivotBone = PivotBone;
            return copy;
        }
    }

    /// <summary>表情通道的一根骨旋转：绕 <see cref="PivotBone"/> 的当前位置转 <see cref="Rotation"/>。</summary>
    public struct MetaHumanBoneRotation
    {
        public MetaHumanQuat Rotation;
        public string PivotBone;
    }

    /// <summary>参数值向量解出来的落地效果集合，写回端只认这个结构。</summary>
    public class MetaHumanSolution
    {
        public readonly Dictionary<string, float> MorphWeights = new Dictionary<string, float>();
        public readonly Dictionary<string, MetaHumanBoneOp> Bones = new Dictionary<string, MetaHumanBoneOp>();
        public readonly Dictionary<string, float> MaterialFloats = new Dictionary<string, float>();
        public readonly Dictionary<string, MetaHumanColor> MaterialColors = new Dictionary<string, MetaHumanColor>();

        /// <summary>
        /// 贴图通道：材质参数名 → 该用的贴图资产路径。
        /// 与浮点/颜色不同，这是**离散赋值**（UE 的 MetaHuman 肤色/痣/皱纹/妆就是换贴图），
        /// 所以它不参与 ±增量累计：谁被写进去，就是"当前值过阈后该用的那一张"。
        /// </summary>
        public readonly Dictionary<string, string> MaterialTextures = new Dictionary<string, string>();

        /// <summary>发型选择（离散下标，键为效果 Target，如 "hair"）。</summary>
        public readonly Dictionary<string, int> HairStyles = new Dictionary<string, int>();

        /// <summary>多个参数写同一材质目标——模板装配冲突，必须被看见而不是静默相加。</summary>
        public readonly List<string> Conflicts = new List<string>();

        /// <summary>被钳到 ±1 的 blendshape 个数与名字（名字只留前 16 个，避免诊断本身撑爆内存）。</summary>
        public int ClampedMorphCount;
        public readonly List<string> ClampedMorphNames = new List<string>();

        public const int ClampedNameBudget = 16;

        public bool IsEmpty
        {
            get
            {
                return MorphWeights.Count == 0 && Bones.Count == 0 && MaterialFloats.Count == 0
                    && MaterialColors.Count == 0 && MaterialTextures.Count == 0
                    && HairStyles.Count == 0;
            }
        }

        public void Reset()
        {
            MorphWeights.Clear();
            Bones.Clear();
            MaterialFloats.Clear();
            MaterialColors.Clear();
            MaterialTextures.Clear();
            HairStyles.Clear();
            Conflicts.Clear();
            ClampedMorphCount = 0;
            ClampedMorphNames.Clear();
        }

        public MetaHumanBoneOp GetOrCreateBone(string name)
        {
            MetaHumanBoneOp op;
            if (!Bones.TryGetValue(name, out op))
            {
                op = new MetaHumanBoneOp();
                Bones[name] = op;
            }

            return op;
        }

        internal void NoteClamped(string morphName)
        {
            ClampedMorphCount++;
            if (ClampedMorphNames.Count < ClampedNameBudget)
            {
                ClampedMorphNames.Add(morphName);
            }
        }
    }

    /// <summary>
    /// 参数值 → 效果的线性求解器。
    /// 语义：效果是「绝对参数值」的函数（与 UE 的参数向量一致），因此全量求解遍历所有参数，
    /// 增量求解需要先把旧值那份贡献原样减掉。
    /// </summary>
    public static class MetaHumanSolver
    {
        public static MetaHumanSolution Solve(MetaHumanState state)
        {
            MetaHumanSolution solution = new MetaHumanSolution();
            Dictionary<string, string> owners = new Dictionary<string, string>();

            foreach (MetaHumanParameter p in state.Template.All())
            {
                Accumulate(p, state.Get(p.Id), 1f, solution, owners);
            }

            ClampMorphs(solution);
            return solution;
        }

        /// <summary>
        /// 就地增量刷新：用 state.Dirty 里记录的旧值减去贡献、加上新值贡献，
        /// 结果必须与全量 Solve 逐字段相等（离线断言里配独立暴力参照核对该性质）。
        /// </summary>
        public static void UpdateIncremental(MetaHumanState state, MetaHumanSolution solution)
        {
            Dictionary<string, string> owners = new Dictionary<string, string>();

            foreach (KeyValuePair<string, float> kv in state.Dirty)
            {
                MetaHumanParameter p = state.Template.Find(kv.Key);
                if (p == null)
                {
                    continue;
                }

                // 无条件先减再加：效果里可能带 Offset，旧值为 0 时的贡献并不为 0，
                // 省这次减法会让增量结果偏离全量结果。
                Accumulate(p, kv.Value, -1f, solution, owners);
                Accumulate(p, state.Get(p.Id), 1f, solution, owners);
            }

            state.ClearDirty();
            ClampMorphs(solution);
        }

        private static void Accumulate(MetaHumanParameter p, float value, float sign, MetaHumanSolution solution, Dictionary<string, string> owners)
        {
            if (p.Effects == null)
            {
                return;
            }

            for (int i = 0; i < p.Effects.Count; i++)
            {
                MetaHumanEffect effect = p.Effects[i];
                string target = effect.Target;
                if (string.IsNullOrEmpty(target))
                {
                    continue;
                }

                switch (effect.Kind)
                {
                    case MetaHumanEffectKind.Morph:
                    {
                        float existing;
                        solution.MorphWeights.TryGetValue(target, out existing);
                        solution.MorphWeights[target] = existing + sign * (effect.Offset + effect.Gain * value);
                        break;
                    }
                    case MetaHumanEffectKind.BoneScale:
                    {
                        MetaHumanBoneOp op = solution.GetOrCreateBone(target);
                        float amount = sign * (effect.Offset + effect.Gain * value);
                        op.ScaleDelta.X += effect.Axis.X * amount;
                        op.ScaleDelta.Y += effect.Axis.Y * amount;
                        op.ScaleDelta.Z += effect.Axis.Z * amount;
                        break;
                    }
                    case MetaHumanEffectKind.BoneTranslate:
                    {
                        MetaHumanBoneOp op = solution.GetOrCreateBone(target);
                        // 与 morph 同一套语义：Offset + Gain*值。
                        // 模板靠 Offset = -默认值*Gain 把「默认姿态零贡献」编码进数据里，
                        // 这里少算 Offset 就会让每个角色一进场就自带形变。
                        float amount = sign * (effect.Offset + effect.Gain * value);
                        op.Translate.X += effect.Axis.X * amount;
                        op.Translate.Y += effect.Axis.Y * amount;
                        op.Translate.Z += effect.Axis.Z * amount;
                        break;
                    }
                    case MetaHumanEffectKind.MaterialFloat:
                    {
                        TrackOwner(owners, solution, p.Id, target);
                        float existing;
                        solution.MaterialFloats.TryGetValue(target, out existing);
                        solution.MaterialFloats[target] = existing + sign * (effect.Offset + effect.Gain * value);
                        break;
                    }
                    case MetaHumanEffectKind.MaterialColor:
                    {
                        TrackOwner(owners, solution, p.Id, target);
                        float t = value;
                        if (t < 0f)
                        {
                            t = 0f;
                        }
                        else if (t > 1f)
                        {
                            t = 1f;
                        }

                        MetaHumanColor color = new MetaHumanColor(
                            effect.BaseColor.R + (effect.TargetColor.R - effect.BaseColor.R) * t,
                            effect.BaseColor.G + (effect.TargetColor.G - effect.BaseColor.G) * t,
                            effect.BaseColor.B + (effect.TargetColor.B - effect.BaseColor.B) * t);

                        MetaHumanColor existing;
                        if (solution.MaterialColors.TryGetValue(target, out existing))
                        {
                            color = new MetaHumanColor(existing.R + sign * color.R, existing.G + sign * color.G, existing.B + sign * color.B);
                        }

                        solution.MaterialColors[target] = color;
                        break;
                    }
                    case MetaHumanEffectKind.HairStyle:
                    {
                        // 离散下标：只认"加"的那一次；取整并夹到参数量程内。
                        if (sign < 0f)
                        {
                            break;
                        }

                        float rounded = (float)Math.Round(value);
                        if (rounded < p.Min)
                        {
                            rounded = p.Min;
                        }
                        else if (rounded > p.Max)
                        {
                            rounded = p.Max;
                        }

                        solution.HairStyles[target] = (int)rounded;
                        break;
                    }
                    case MetaHumanEffectKind.MaterialTexture:
                    {
                        // 离散赋值：只在"加"的那一次按当前值定夺该用哪张，不做加减累计。
                        // 增量通路先减后加，减的那次留在这里不管——加的那次一定覆盖成最新值对应的贴图。
                        if (sign < 0f)
                        {
                            break;
                        }

                        TrackOwner(owners, solution, p.Id, target);
                        float threshold = effect.TextureThreshold > 0f ? effect.TextureThreshold : 0.5f;
                        string chosen = value >= threshold ? effect.TextureOn : effect.TextureOff;
                        if (!string.IsNullOrEmpty(chosen))
                        {
                            solution.MaterialTextures[target] = chosen;
                        }
                        else
                        {
                            solution.MaterialTextures.Remove(target);
                        }

                        break;
                    }
                }
            }
        }

        private static void TrackOwner(Dictionary<string, string> owners, MetaHumanSolution solution, string paramId, string target)
        {
            string owner;
            if (owners.TryGetValue(target, out owner))
            {
                if (owner != paramId)
                {
                    solution.Conflicts.Add(target + " 同时被 " + owner + " 与 " + paramId + " 写入");
                    owners[target] = paramId;
                }
            }
            else
            {
                owners[target] = paramId;
            }
        }

        /// <summary>把 blendshape 权重钳到引擎的合法区间（AnimatedModel 内部同样钳 ±1）。</summary>
        public static void ClampMorphs(MetaHumanSolution solution)
        {
            solution.ClampedMorphCount = 0;
            solution.ClampedMorphNames.Clear();

            List<string> keys = new List<string>(solution.MorphWeights.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                float w = solution.MorphWeights[keys[i]];
                if (w > 1f)
                {
                    solution.MorphWeights[keys[i]] = 1f;
                    solution.NoteClamped(keys[i]);
                }
                else if (w < -1f)
                {
                    solution.MorphWeights[keys[i]] = -1f;
                    solution.NoteClamped(keys[i]);
                }
            }
        }

        /// <summary>
        /// 把解里已经消失的效果写回中性值：写回端维护上一帧的键集，
        /// 只对差集清零，避免每次改动都重刷全部 blendshape。
        /// </summary>
        public static void DiffInto(MetaHumanSolution previous, MetaHumanSolution current, HashSet<string> previousApplied)
        {
            foreach (string name in previousApplied)
            {
                if (!current.MorphWeights.ContainsKey(name))
                {
                    current.MorphWeights[name] = 0f;
                }
            }
        }
    }
}
