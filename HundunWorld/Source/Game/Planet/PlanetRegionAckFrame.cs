using System;
using System.Globalization;
using Horizon.Game.Message.Network;
using Horizon.Planet;
using Horizon.Planet.Wire;

namespace HundunWorld.Game.Planet
{
    /// <summary>
    /// 把服务端 ack 里那套新坐标系<em>解回</em>一个可用的帧，并拿它和客户端自己算的帧对账。
    /// <para>§6.3 第 3 步写的是"客户端切换 RegionId/WorldOrigin/RegionFrame，把旧局部坐标转新局部坐标"。
    /// 服务端在回执里给了原点（i64 厘米）与姿态（Q15 四元数，列 j 是局部第 j 轴在全局里的指向），
    /// 但此前<em>没有任何一侧把这两个数解回去用过</em>：驱动换基是靠全局点自己反查的（那是 §13.15 的结论，
    /// 没错），于是 ack 里的帧一直是"发出去了但没人读"的那一份。这个类把它变成一个交叉检查：</para>
    /// <para>两侧各自独立算出同一块地面的切平面，如果对不上，差的是整颗星球的朝向而不是几毫米——
    /// 而那正是 §13.15 那次事故的形状。交叉检查是唯一能在此之前把它看见的方式。</para>
    /// <para><b>纯 C#，不含引擎类型</b>：源码被 <c>Horizon.Game.Gateway.Tests</c> 直接链进去编译，
    /// 判据真跑而不是"编辑器里看着对"。</para>
    /// </summary>
    public static class PlanetRegionAckFrame
    {
        /// <summary>解出来的帧。局部三轴按 RegionFrame 的约定：X=东、Y=上、Z=南。</summary>
        public readonly struct Reconstructed
        {
            public Reconstructed(PlanetVector3 east, PlanetVector3 up, PlanetVector3 south, PlanetVector3 originCm)
            {
                East = east; Up = up; South = south; OriginCm = originCm;
            }

            public PlanetVector3 East { get; }
            public PlanetVector3 Up { get; }
            public PlanetVector3 South { get; }

            /// <summary>新区原点（ECEF 整数厘米）。</summary>
            public PlanetVector3 OriginCm { get; }

            /// <summary>局部 → 全局：原点 + 姿态旋转。这就是 §6.3 第 3 步要的换算。</summary>
            public PlanetVector3 GlobalFromLocalCm(in PlanetVector3 localCm)
            {
                PlanetVector3 dir = East * localCm.X + Up * localCm.Y + South * localCm.Z;
                return OriginCm + dir;
            }

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "ack frame origin={0},{1},{2}", OriginCm.X, OriginCm.Y, OriginCm.Z);
        }

        /// <summary>
        /// 解 ack 的坐标系。<b>先验再用</b>：未生效/带拒绝码/归属为 0、全 0 四元数、
        /// 模长明显不是 1、原点退化或不在海面上——每一条都回 false 并说清是哪一条。
        /// （三轴正交性<em>不</em>在这里查：解算先把四元数归一了，那两项检查够不着，见下面注释。）
        /// 每一条都回 false 并说清是哪一条——解出来的帧如果本身是坏的，拿它换算出来的位置不会报错，
        /// 只会让人站在歪掉的地面上。
        /// </summary>
        public static bool TryReconstruct(PlanetRegionTransferResult ack, in PlanetHandshake expectedCaliber,
                                          out Reconstructed frame, out string why)
        {
            frame = default;
            why = "";

            if (ack is null) { why = "ack 为空"; return false; }
            if (!ack.Applied)
            {
                // 被拒时这些字段全是 0。把"半套坐标系"解出来用，比没有坐标系更危险：
                // 原点 (0,0,0) 是地心，姿态是恒等——它会让玩家相对地心站直，而不是相对脚下的地面。
                why = "ack 未生效（Applied=false），坐标系字段不可用";
                return false;
            }
            if (ack.Code != PlanetRegionRejectCode.None)
            {
                why = string.Format(CultureInfo.InvariantCulture, "ack 带着拒绝码 {0}，不该按坐标系读", ack.Code);
                return false;
            }
            if (ack.NewRegionId == 0)
            {
                why = "ack 回的归属是 0（服务端没有归属可报），坐标系没有对应的区域";
                return false;
            }

            var q = new QuaternionQuantized { X = ack.FrameX, Y = ack.FrameY, Z = ack.FrameZ, W = ack.FrameW };
            double nx = q.DecodeX, ny = q.DecodeY, nz = q.DecodeZ, nw = q.DecodeW;
            double norm = Math.Sqrt(nx * nx + ny * ny + nz * nz + nw * nw);
            if (!double.IsFinite(norm) || norm < 1e-6)
            {
                why = string.Format(CultureInfo.InvariantCulture, "四元数退化（|q|={0:E3}）：全 0 不是合法姿态", norm);
                return false;
            }
            // Q15 每个分量最大误差 0.5/32767 ≈ 1.5e-5，四个分量合起来 |q| 的偏差在 3e-5 量级；
            // 阈值放到 1e-4 是"容忍量化、不容忍概念错"：真的写错（少一次归一、符号翻转）会差得远不止这些。
            if (Math.Abs(norm - 1.0) > 1e-4)
            {
                why = string.Format(CultureInfo.InvariantCulture, "四元数没归一：|q|-1 = {0:E3}", norm - 1.0);
                return false;
            }

            PlanetVector3 east = q.Rotate(PlanetVector3.AxisX);
            PlanetVector3 up = q.Rotate(PlanetVector3.AxisY);
            PlanetVector3 south = q.Rotate(PlanetVector3.AxisZ);
            // 这里不再检查"三轴正交/单位"：Rotate 先把四元数归一了，任何一组 short 解出来的
            // 三轴都必然正交单位（差到 double 舍入量级），那两个分支写在这儿是够不着的死路。
            // 真正能坏的是别的：没归一的模长（上面那条 |q|-1）、全 0、以及原点本身。

            var origin = new PlanetVector3(ack.OriginXcm, ack.OriginYcm, ack.OriginZcm);
            double originLength = origin.Length;
            if (!double.IsFinite(originLength) || originLength <= 1e-300)
            {
                why = "区域原点退化（零向量或非法值）";
                return false;
            }
            // 原点必须在海面上：它应当是"区域中心方向 × 半径"。这里用握手里的半径而不是常量——
            // 客户端选的星球与服务端的星球如果不同，这条正好先撞上（口径闸门的下游复述）。
            double radiusDeviation = Math.Abs(originLength - expectedCaliber.PlanetRadiusCm);
            if (radiusDeviation > 1.0)
            {
                why = string.Format(CultureInfo.InvariantCulture,
                    "区域原点离海面 {0:0.###} cm（半径口径 {1}）：原点不是按这颗星球算的",
                    radiusDeviation, expectedCaliber.PlanetRadiusCm);
                return false;
            }

            frame = new Reconstructed(east, up, south, origin);
            return true;
        }

        /// <summary>ack 里那三个局部厘米（§6.2 的 i32 厘米）。</summary>
        public static PlanetVector3 LocalFromAck(PlanetRegionTransferResult ack) =>
            new(ack.LocalXCm, ack.LocalYCm, ack.LocalZCm);

        /// <summary>
        /// ack 自洽性：把 ack 自己的 (原点, 姿态, 局部) 换算成全局，与<em>客户端发出去的那个全局点</em>比。
        /// <para>差的应该是纯量化误差（姿态 Q15 + 局部取整），量级由判据实测给出；
        /// 一旦到了米级，说明某一侧的约定不对（行列转置、q 与 q⁻¹、轴顺序），
        /// 而那不会是异常，只会是"人站在歪掉的地面上"。</para>
        /// </summary>
        public static double DeviationCm(in Reconstructed frame, in PlanetVector3 ackLocalCm,
                                        in PlanetVector3 expectedGlobalCm)
        {
            PlanetVector3 back = frame.GlobalFromLocalCm(ackLocalCm);
            double dx = back.X - expectedGlobalCm.X;
            double dy = back.Y - expectedGlobalCm.Y;
            double dz = back.Z - expectedGlobalCm.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>与客户端自己反查出来的帧比：两轴夹角（度）。同一个区域却差得多，就是两套真相。</summary>
        public static double AxisDeviationDeg(in Reconstructed ackFrame, in RegionFrame clientFrame)
        {
            double Worst(PlanetVector3 a, PlanetVector3 b)
            {
                double dot = Math.Clamp(PlanetVector3.Dot(a, b), -1.0, 1.0);
                return Math.Acos(Math.Abs(dot)) * 180.0 / Math.PI;   // 取绝对值：轴反号同样要报，但那是另一条判据
            }
            double dev = Worst(ackFrame.East, clientFrame.East);
            dev = Math.Max(dev, Worst(ackFrame.Up, clientFrame.Up));
            dev = Math.Max(dev, Worst(ackFrame.South, clientFrame.South));
            return dev;
        }

        /// <summary>
        /// 轴<em>反号</em>检查。<see cref="AxisDeviationDeg"/> 为了把"轴搞混"与"轴反号"分开计，
        /// 特意取了绝对值——那条判据抓不到反向；而反向恰好是左手系/共轭四元数那一类错的形状，
        /// 所以单独一条：点积必须为正，且接近 1。
        /// </summary>
        public static double SignedAxisDeviationDeg(in Reconstructed ackFrame, in RegionFrame clientFrame)
        {
            double Worst(PlanetVector3 a, PlanetVector3 b)
            {
                double dot = Math.Clamp(PlanetVector3.Dot(a, b), -1.0, 1.0);
                return Math.Acos(dot) * 180.0 / Math.PI;
            }
            double dev = Worst(ackFrame.East, clientFrame.East);
            dev = Math.Max(dev, Worst(ackFrame.Up, clientFrame.Up));
            return Math.Max(dev, Worst(ackFrame.South, clientFrame.South));
        }
    }
}
