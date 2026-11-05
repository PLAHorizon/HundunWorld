using System;
using Horizon.Planet;

namespace HundunWorld.Game.Planet
{
    /// <summary>
    /// 角色位姿 → 星球局部坐标的<em>口径判定</em>层。纯 C#，不含引擎类型——
    /// 判据在 <c>Horizon.Game.Gateway.Tests</c> 里把这份源码直接编译进去真跑，
    /// 而不是复制一份（同一件事写两遍就会有两套口径，而这套东西错的形状是"人站在地底下"）。
    /// <para><b>为什么要单独一层</b>：本仓库里同一件事（换 Y/Z 轴、不缩放）目前有六处各写一遍
    /// （<c>LocalPlayerActorSyncSystem.cs:124</c>、<c>HundunWorldGame.cs:1565</c> 的注释与实现、
    /// <c>GaspTraversalProbe.cs:502</c>、<c>DsWorldSession.cs:4677</c>…），
    /// 而星球这套局部帧用的是第三种顺序：<see cref="RegionFrame"/> 的局部轴是
    /// <c>X=东、Y=上、Z=南</c>。把角色位置直接塞进 <c>PlanetVector3</c> 而不做任何置换，
    /// 等于假定"Flax 的轴序恰好就是星球帧的轴序"——这个假定今天没被任何一处证据支持过。</para>
    /// <para><b>这一层不猜。</b>它把两种候选口径各自的<em>离地高度</em>算出来，
    /// 落在站立带里的那一个才是证据支持的口径；两个都落进去（角色离帧原点太近，
    /// 深度与高度的差别还没拉开）如实报 Ambiguous，两个都不落进去（场景根本没贴在这块近景地面上）
    /// 报 Neither。<em>静默挑一个</em>是本仓库反复踩过的那类错：换区上行会带着一个"看起来合法"的
    /// 错误归属出门，而屏幕上什么都没有。</para>
    /// </summary>
    public static class PlanetActorPoseRules
    {
        /// <summary>候选的轴序口径。<b>只这两档</b>：仓里实测存在的就是这两种写法。</summary>
        public enum AxisConvention
        {
            /// <summary>Flax Actor 自己的顺序：X=左右(东)、Y=高度(上)、Z=前后(南)。
            /// 星球局部帧恰好也是 X/Y/Z = 东/上/南，所以这一档是<em>逐位不变</em>。</summary>
            FlaxYUp,
            /// <summary>ECS/网络那一侧的顺序：X=左右、Y=前后(深度)、Z=上下(高度)
            /// （<c>HundunWorldGame.cs:1565</c> 明写 "Flax.Y → ECS.Z、Flax.Z → ECS.Y"）。
            /// 换到星球局部帧要<em>换 Y/Z</em>。</summary>
            EcsZUp,
        }

        /// <summary>
        /// 站立带（±5 m，相对该点基场地面的离地高度）。<b>为什么是这个数</b>：
        /// 角色锚点到脚底的合理差在 0~200 cm，蹲/跳/坡再加几米；而<em>错口径</em>的差
        /// 是"把深度当高度"，量级是区域半边长（这里 5 km 级），差出三到四个数量级。
        /// 带子取得宽，是为了让判据只在"真的分得开"时下结论。
        /// </summary>
        public const double StandingBandCm = 500.0;

        /// <summary>判定结论。</summary>
        public enum PoseVerdict
        {
            /// <summary>Flax 那一档的离地高度在带内而 ECS 那一档不在：证据支持 <see cref="AxisConvention.FlaxYUp"/>。</summary>
            FlaxYUpSupported,
            /// <summary>反过来。</summary>
            EcsZUpSupported,
            /// <summary>两档都在带内（角色离帧原点太近，两档还没拉开）——不许猜。</summary>
            Ambiguous,
            /// <summary>两档都不在带内：这块场景压根不是贴在该近景地面上跑的（今天的实况就是这一档）。</summary>
            Neither,
            /// <summary>输入不可用（非有限值），连"算一遍"都做不到。</summary>
            UnusableInput,
        }

        /// <summary>判定的全部读数，回执里就念这一行。</summary>
        public readonly struct PoseProbe
        {
            public PoseProbe(PoseVerdict verdict, double altitudeFlaxCm, double altitudeEcsCm,
                             long regionFlaxId, long regionEcsId, string why)
            {
                Verdict = verdict;
                AltitudeFlaxCm = altitudeFlaxCm;
                AltitudeEcsCm = altitudeEcsCm;
                RegionFlaxId = regionFlaxId;
                RegionEcsId = regionEcsId;
                Why = why;
            }

            public PoseVerdict Verdict { get; }
            public double AltitudeFlaxCm { get; }
            public double AltitudeEcsCm { get; }

            /// <summary>两种口径各自反查出来的区域。<b>两个都报</b>：即便离地高度分不开，
            /// 区域号也可能分开，而换区上行要的就是这个数。</summary>
            public long RegionFlaxId { get; }
            public long RegionEcsId { get; }
            public string Why { get; }

            public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "verdict={0} alt_flax={1:0.###} alt_ecs={2:0.###} region_flax={3} region_ecs={4} why={5}",
                Verdict, AltitudeFlaxCm, AltitudeEcsCm, RegionFlaxId, RegionEcsId, Why);
        }

        /// <summary>
        /// 把一份<em>原始三元数</em>按指定口径换成星球局部坐标（厘米）。
        /// <para>这一处是全客户端唯一允许写"换不换 Y/Z"的地方：别处再写一遍，
        /// 就有两套真相，而它们的差别是"角色站在地面上"与"角色站在两公里下面"。</para>
        /// </summary>
        public static PlanetVector3 ToLocalCm(double axis0, double axis1, double axis2, AxisConvention convention)
        {
            if (!double.IsFinite(axis0) || !double.IsFinite(axis1) || !double.IsFinite(axis2))
                throw new ArgumentException($"位姿三元数里有非有限值：({axis0}, {axis1}, {axis2})");
            return convention == AxisConvention.FlaxYUp
                ? new PlanetVector3(axis0, axis1, axis2)
                : new PlanetVector3(axis0, axis2, axis1);
        }

        /// <summary>
        /// 离地高度（厘米，相对<em>该点</em>的基场地面）。
        /// <para>半径取 <paramref name="frame"/>.RadiusCm 而不是 <c>PlanetConstants.PlanetRadiusCm</c>：
        /// 帧是照着哪个半径建的，局部点就属于哪颗星球。混用两个半径的错不会报错，
        /// 只会让离地高度整体偏掉一个常数——正是 §6.2 那条口径判定要防的东西。</para>
        /// </summary>
        public static double AltitudeAboveGroundCm(in PlanetVector3 localCm, RegionFrame frame, double groundElevationCm)
        {
            PlanetVector3 global = frame.GlobalFromLocalCm(localCm);
            return (global.Length - frame.RadiusCm) - groundElevationCm;
        }

        /// <summary>
        /// 拿角色位置自己算的那份"离地高度"当证据，问哪种口径站得住。
        /// <paramref name="groundElevationCm"/> 是该点基场给的地面海拔（厘米，相对海平面）。
        /// </summary>
        public static PoseProbe Decide(double axis0, double axis1, double axis2,
                                       RegionFrame frame, double groundElevationCm)
        {
            if (!double.IsFinite(axis0) || !double.IsFinite(axis1) || !double.IsFinite(axis2)
                || !double.IsFinite(groundElevationCm))
                return new PoseProbe(PoseVerdict.UnusableInput, double.NaN, double.NaN, 0, 0,
                    "输入不可用（非有限值）：连算一遍都做不到");

            var localFlax = ToLocalCm(axis0, axis1, axis2, AxisConvention.FlaxYUp);
            var localEcs = ToLocalCm(axis0, axis1, axis2, AxisConvention.EcsZUp);
            double altFlax = AltitudeAboveGroundCm(localFlax, frame, groundElevationCm);
            double altEcs = AltitudeAboveGroundCm(localEcs, frame, groundElevationCm);

            bool inFlax = Math.Abs(altFlax) <= StandingBandCm;
            bool inEcs = Math.Abs(altEcs) <= StandingBandCm;

            PoseVerdict verdict;
            string why;
            if (inFlax && inEcs)
            {
                verdict = PoseVerdict.Ambiguous;
                why = "两档都落在站立带里：深度与高度还分不开（角色离帧原点太近），不许猜";
            }
            else if (inFlax)
            {
                verdict = PoseVerdict.FlaxYUpSupported;
                why = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Flax 档 {0:0.###} cm 在带内，ECS 档 {1:0.###} cm 不在", altFlax, altEcs);
            }
            else if (inEcs)
            {
                verdict = PoseVerdict.EcsZUpSupported;
                why = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "ECS 档 {0:0.###} cm 在带内，Flax 档 {1:0.###} cm 不在", altEcs, altFlax);
            }
            else
            {
                verdict = PoseVerdict.Neither;
                why = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "两档都不在带内（{0:0.###} / {1:0.###} cm）：这个角色不是贴在这块近景地面上跑的",
                    altFlax, altEcs);
            }
            return new PoseProbe(verdict, altFlax, altEcs,
                                 RegionOf(localFlax, frame), RegionOf(localEcs, frame), why);
        }

        /// <summary>该局部点反查回来的区域号。<b>由全局点算，不接受调用方直接给区域号</b>
        /// （§13.15 那个缺陷的形状就是把区域号抄进本该由点算出来的位置）。</summary>
        public static long RegionOf(in PlanetVector3 localCm, RegionFrame frame)
        {
            PlanetVector3 global = frame.GlobalFromLocalCm(localCm);
            double length = global.Length;
            if (!double.IsFinite(length) || length <= 1e-300) return 0;
            return RegionKey.FromDirection(global / length, frame.Key.Level).ToId();
        }
    }
}
