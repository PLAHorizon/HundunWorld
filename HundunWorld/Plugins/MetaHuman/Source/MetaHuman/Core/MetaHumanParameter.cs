using System;
using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>
    /// 一条参数效果：值 v 落在某个目标上产生的贡献。
    /// Morph: weight = Offset + Gain * v；BoneScale: scale = One + v * Gain(逐轴)；
    /// BoneTranslate: translate = v * Gain(逐轴)；Material*: 值 = Offset + Gain * v。
    /// 用扁平结构而非继承，是为了 System.Text.Json 无需类型判别器即可往返。
    /// </summary>
    [Serializable]
    public class MetaHumanEffect
    {
        public MetaHumanEffectKind Kind { get; set; }

        /// <summary>blendshape 名 / 骨骼名 / 材质参数名。</summary>
        public string Target { get; set; }

        /// <summary>线性增益。</summary>
        public float Gain { get; set; }

        /// <summary>基线偏移（v=0 时的固定贡献）。</summary>
        public float Offset { get; set; }

        /// <summary>骨骼效果用的逐轴掩码分量；非骨骼效果忽略。</summary>
        public MetaHumanVec3 Axis { get; set; }

        /// <summary>颜色效果的基色（v=0）。</summary>
        public MetaHumanColor BaseColor { get; set; }

        /// <summary>颜色效果的染色（v=1）。</summary>
        public MetaHumanColor TargetColor { get; set; }

        /// <summary>贴图通道：值低于阈值时用这张（资产路径）。</summary>
        public string TextureOff { get; set; }

        /// <summary>贴图通道：值不低于阈值时用这张（资产路径）。</summary>
        public string TextureOn { get; set; }

        /// <summary>贴图通道的切换阈值（参数值口径，默认 0.5）。</summary>
        public float TextureThreshold { get; set; }

        public MetaHumanEffect()
        {
            Gain = 1f;
            Axis = MetaHumanVec3.One;
        }

        public MetaHumanEffect(MetaHumanEffectKind kind, string target, float gain, float offset = 0f)
        {
            Kind = kind;
            Target = target;
            Gain = gain;
            Offset = offset;
            Axis = MetaHumanVec3.One;
        }

        public static MetaHumanEffect Morph(string blendShapeName, float gain, float offset = 0f)
        {
            return new MetaHumanEffect(MetaHumanEffectKind.Morph, blendShapeName, gain, offset);
        }

        public static MetaHumanEffect BoneScale(string boneName, MetaHumanVec3 axisGain, float scale = 1f)
        {
            return new MetaHumanEffect(MetaHumanEffectKind.BoneScale, boneName, scale) { Axis = axisGain };
        }

        public static MetaHumanEffect BoneTranslate(string boneName, MetaHumanVec3 axisGain, float scale = 1f)
        {
            return new MetaHumanEffect(MetaHumanEffectKind.BoneTranslate, boneName, scale) { Axis = axisGain };
        }

        public static MetaHumanEffect Material(string parameterName, float gain, float offset = 0f)
        {
            return new MetaHumanEffect(MetaHumanEffectKind.MaterialFloat, parameterName, gain, offset);
        }

        public static MetaHumanEffect MaterialColor(string parameterName, MetaHumanColor baseColor, MetaHumanColor targetColor)
        {
            return new MetaHumanEffect(MetaHumanEffectKind.MaterialColor, parameterName, 1f)
            {
                BaseColor = baseColor,
                TargetColor = targetColor,
            };
        }
    }

    /// <summary>
    /// 一个玩家可调的数值（GUI 控制）。Id 是身份键，DisplayName 只是标签，绝不参与持久化。
    /// </summary>
    [Serializable]
    public class MetaHumanParameter
    {
        /// <summary>稳定键，模板内唯一；改它等于换掉一个参数的历史数据。</summary>
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public MetaHumanRegion Region { get; set; }

        public MetaHumanParamKind Kind { get; set; }

        public MetaHumanSide Side { get; set; }

        /// <summary>镜像对岸参数的 Id；无对岸时为空。</summary>
        public string PairId { get; set; }

        public float Min { get; set; }

        public float Max { get; set; }

        public float Default { get; set; }

        /// <summary>UI 步进；0 表示连续。</summary>
        public float Step { get; set; }

        /// <summary>
        /// 参数层级："semantic" 是按五官聚合出来的、给玩家看的粗调；
        /// "advanced" 是逐关节逐轴的原始表。默认只把 semantic 摆上界面，
        /// 因为 441 根原始滑杆没人能用完，而「数字太多」会被误当成「功能没做完」。
        /// </summary>
        public string Group { get; set; }

        /// <summary>该参数在此模板的有序表中的位置，导出为紧凑快照的下标。由模板装配时回填。</summary>
        public int Index { get; set; }

        public List<MetaHumanEffect> Effects { get; set; }

        public MetaHumanParameter()
        {
            Effects = new List<MetaHumanEffect>();
            Group = "semantic";
            Side = MetaHumanSide.None;
            Index = -1;
            Max = 1f;
        }

        public float Clamp(float value)
        {
            if (float.IsNaN(value))
            {
                return Default;
            }

            if (value < Min)
            {
                return Min;
            }

            if (value > Max)
            {
                return Max;
            }

            return value;
        }

        /// <summary>是否偏离默认值（快照只存偏离项）。</summary>
        public bool IsDefault(float value)
        {
            return Math.Abs(value - Default) < Epsilon;
        }

        public const float Epsilon = 1e-5f;

        public IEnumerable<string> MorphTargets()
        {
            foreach (MetaHumanEffect effect in Effects)
            {
                if (effect.Kind == MetaHumanEffectKind.Morph && !string.IsNullOrEmpty(effect.Target))
                {
                    yield return effect.Target;
                }
            }
        }
    }

    /// <summary>约束：一组参数偏离默认值之和的上下限。</summary>
    [Serializable]
    public class MetaHumanConstraint
    {
        public MetaHumanConstraintKind Kind { get; set; }

        public List<string> ParamIds { get; set; }

        public float Min { get; set; }

        public float Max { get; set; }

        public MetaHumanConstraint()
        {
            Kind = MetaHumanConstraintKind.Sum;
            ParamIds = new List<string>();
        }
    }

    /// <summary>预设：一组参数值的命名集合，可整体应用，也可两者线性混合。</summary>
    [Serializable]
    public class MetaHumanPreset
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public MetaHumanRegion Region { get; set; }

        /// <summary>
        /// true = 这个预设的语义是"回默认"（对应 UE 的 Default 预设）。
        /// 不写这个标记而 Values 又为空，就是一份死预设：玩家点了以后什么都没发生，
        /// 既不报错也不留痕，只有目视能发现。
        /// </summary>
        public bool ResetToDefault { get; set; }

        /// <summary>参数 Id → 值。允许只覆盖部分参数。</summary>
        public Dictionary<string, float> Values { get; set; }

        public MetaHumanPreset()
        {
            Values = new Dictionary<string, float>();
        }
    }
}
