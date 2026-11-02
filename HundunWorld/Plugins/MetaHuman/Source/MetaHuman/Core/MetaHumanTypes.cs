using System;

namespace MetaHuman
{
    /// <summary>参数所属的身体/头部区域，对齐 UE5.8 MetaHumanCharacter 编辑器的区域划分。</summary>
    public enum MetaHumanRegion
    {
        None = 0,
        Head,
        Forehead,
        Brow,
        Eye,
        Nose,
        Lip,
        Chin,
        Cheek,
        Ear,
        Jaw,
        Scalp,
        Neck,
        Teeth,
        Tongue,
        EyeShell,
        Skin,
        Hair,
        Body,
    }

    /// <summary>参数类别，决定它在玩家界面里归到哪个页签、以及是否属于「形状」类。</summary>
    public enum MetaHumanParamKind
    {
        HeadShape = 0,
        BodyShape,
        Proportion,
        SkinTone,
        SkinDetail,
        Hair,
        Eye,
        Makeup,
    }

    /// <summary>参数的侧向归属；两侧成对参数用 PairId 互指，联动时一次写入两值。</summary>
    public enum MetaHumanSide
    {
        None = 0,
        Left,
        Right,
    }

    /// <summary>单个效果的落地方式。</summary>
    public enum MetaHumanEffectKind
    {
        /// <summary>写 AnimatedModel 的 blendshape 权重。</summary>
        Morph = 0,
        /// <summary>累积到骨骼缩放（体型比例的骨架通道）。</summary>
        BoneScale,
        /// <summary>累积到骨骼平移。</summary>
        BoneTranslate,
        /// <summary>材质实例浮点参数。</summary>
        MaterialFloat,
        /// <summary>材质实例颜色参数。</summary>
        MaterialColor,
        /// <summary>
        /// 材质实例的贴图槽位（UE 的 MetaHuman 肤色/痣/皱纹/妆是换贴图，不是改颜色）。
        /// 这是**离散通道**：值过阈就整张贴换掉，不能像浮点那样加减累计。
        /// </summary>
        MaterialTexture,
        /// <summary>
        /// 发型选择（离散下标）。与贴图通道同理只认"加"的那一次，不做加减累计；
        /// 落地是换 StaticModel 的 Model，不是改材质。
        /// </summary>
        HairStyle,
    }

    /// <summary>约束类型。UE 侧由 EvaluateConstraintRange 提供，这里移植可离线校验的等价子集。</summary>
    public enum MetaHumanConstraintKind
    {
        /// <summary>一组参数的「值 − 默认值」之和落在 [Min, Max]，超出按到限的距离等比回退。</summary>
        Sum = 0,
    }

    /// <summary>纯数据结构里的三维向量：核不依赖 Flax 类型，才能离线断言。</summary>
    public struct MetaHumanVec3
    {
        public float X;
        public float Y;
        public float Z;

        public MetaHumanVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static MetaHumanVec3 One { get { return new MetaHumanVec3(1f, 1f, 1f); } }

        public static MetaHumanVec3 Zero { get { return new MetaHumanVec3(0f, 0f, 0f); } }

        public void ScaleBy(MetaHumanVec3 other)
        {
            X *= other.X;
            Y *= other.Y;
            Z *= other.Z;
        }

        public void Add(MetaHumanVec3 other)
        {
            X += other.X;
            Y += other.Y;
            Z += other.Z;
        }
    }

    /// <summary>
    /// 纯数据核里的四元数（x,y,z,w）。只在核里做加法合成，
    /// 落到引擎再换 Flax 的 Quaternion —— 这样表情通道的合成也能离线断言。
    /// </summary>
    public struct MetaHumanQuat
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public MetaHumanQuat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static MetaHumanQuat Identity { get { return new MetaHumanQuat(0f, 0f, 0f, 1f); } }

        public bool IsIdentity
        {
            get { return X == 0f && Y == 0f && Z == 0f && (W == 1f || W == -1f); }
        }

        /// <summary>绕单位轴 axis 转 radians 弧度（右手系，与 glTF/离线测量一致）。</summary>
        public static MetaHumanQuat FromAxisAngle(MetaHumanVec3 axis, float radians)
        {
            float len = (float)Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z);
            if (len < 1e-8f || Math.Abs(radians) < 1e-9f)
            {
                return Identity;
            }

            float half = radians * 0.5f;
            float s = (float)Math.Sin(half) / len;
            return new MetaHumanQuat(axis.X * s, axis.Y * s, axis.Z * s, (float)Math.Cos(half));
        }

        /// <summary>this * other（先转 other 再转 this）。</summary>
        public static MetaHumanQuat Multiply(MetaHumanQuat a, MetaHumanQuat b)
        {
            return new MetaHumanQuat(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }
    }

    /// <summary>纯数据结构里的颜色，分量归一到 0..1。</summary>
    public struct MetaHumanColor
    {
        public float R;
        public float G;
        public float B;

        public MetaHumanColor(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    /// <summary>模板/外观的版本，对应 UE 的 FMetaHumanVersion{Major,Minor,Revision}。</summary>
    [Serializable]
    public class MetaHumanVersion
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Revision { get; set; }

        public MetaHumanVersion()
        {
        }

        public MetaHumanVersion(int major, int minor, int revision)
        {
            Major = major;
            Minor = minor;
            Revision = revision;
        }

        public override string ToString()
        {
            return Major + "." + Minor + "." + Revision;
        }

        /// <summary>只保证「不破坏已有外观数据」的方向：主版本相同即认得旧快照。</summary>
        public bool CompatibleWith(MetaHumanVersion other)
        {
            return other != null && other.Major == Major;
        }
    }
}
