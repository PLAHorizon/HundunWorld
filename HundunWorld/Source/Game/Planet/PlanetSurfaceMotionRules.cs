using System;
using Horizon.Planet;

namespace HundunWorld.Game.Planet
{
    /// <summary>
    /// 球面步行的<em>决策层</em>：把"这一帧想怎么走"算成"下一个全局点 + 一套朝向 + 落地状态"。
    /// 纯 C#，不碰引擎——判据在 <c>Horizon.Game.Gateway.Tests</c> 里把这份源码直接编译进去真跑。
    /// <para><b>为什么单独一层</b>：这是 P3 里唯一能被离线钉住的那一半（§13.51 已经把几何验完了）。
    /// 控制器本体（读输入、写 Actor 变换、动画）必须碰引擎，而它的正确性几乎全部落在三个数上：
    /// 走一步半径不变、跳起来的高度与滞空时间对得上 1g、落地不穿地。这三个数在这里。</para>
    /// <para><b>不重复造数</b>：重力只从 <see cref="PlanetGravity.GameCmPerS2"/> 取（§13.55 的产品决定），
    /// 贴地容差只从 <see cref="PlanetGround.BelowToleranceCm"/> 取（与 §6.1 的服务端拒收同一个数），
    /// 走一步只走 <see cref="PlanetSphericalMotion.StepAlongGeodesic"/>（它保半径）。
    /// 这一层自己<em>不写</em>任何 g、任何容差、任何"前进 = 点 + 前 × 距离"的近似——
    /// 每抄一份就是一条将来会与另一端分家的真相。</para>
    /// <para><b>它不知道轴序</b>：输入输出全是星球全局坐标（ECEF cm）与切向单位向量，
    /// 所以角色位姿那一侧的口径（Flax 轴序 vs ECS 轴序，见 <see cref="PlanetActorPoseRules"/>）
    /// 与这一层无关；把两者对接的适配器要等口径<em>量出来</em>之后再写。</para>
    /// </summary>
    public static class PlanetSurfaceMotionRules
    {
        /// <summary>水平走速（厘米/秒）。与客户端 <c>MoveSpeed=500cm/s</c> 同口径。</summary>
        public const double WalkSpeedCmPerS = 500.0;

        /// <summary>跳跃高度目标（厘米）。手感锚：1.5 m ⇒ 初速由 <see cref="PlanetGravity.TakeoffSpeedForApex"/> 反解。</summary>
        public const double JumpApexCm = 150.0;

        /// <summary>
        /// 单步时长上限（秒）。超过它的 dt 是"卡了一帧"，不是"走得很快"：
        /// 按大 dt 积分会让重力一次跨过落地、把角色按进地面，所以直接拒绝并要求调用方先钳。
        /// </summary>
        public const double MaxStepSeconds = 0.1;

        /// <summary>
        /// 下台阶的高度上限（厘米）：贴着地面走时，地面比脚下低不超过这个数就"踩下去"继续贴地，
        /// 超过它就算走出了边沿、转成下落。10 cm 是路缘/台阶的常见量级；把它调大到米级会让角色
        /// 从悬崖上滑下去而不是掉下去，调成 0 则连 1 cm 的接缝都会触发起跳式的失重。
        /// <para>注意它<b>不是</b> §6.1 的穿模容差：那条管的是"服务端拒不拒收"，管的是状态合法性；
        /// 落地本身必须落在<em>地面</em>上，不能落在地面之上一米的空气里。</para>
        /// </summary>
        public const double StepDownCm = 10.0;

        public enum StepOutcome
        {
            /// <summary>在地面上按输入走了一步（半径跟着地面走）。</summary>
            Walked,
            /// <summary>在地面上起跳：这一帧位置不变，垂直速度取起跳初速。</summary>
            Jumped,
            /// <summary>空中：位置按抛物线与测地线各走一步。</summary>
            Airborne,
            /// <summary>这一帧落地：垂直速度清零，半径贴到地面。</summary>
            Landed,
            /// <summary>没有输入且在地面上：原地不动（朝向仍可转）。</summary>
            Stood,
        }

        /// <summary>一步之后的全部状态。值语义：不原地改，返回新的。</summary>
        public readonly struct State
        {
            public State(PlanetVector3 positionCm, PlanetVector3 forwardTangent, double verticalSpeedCmPerS,
                         bool grounded)
            {
                PositionCm = positionCm;
                ForwardTangent = forwardTangent;
                VerticalSpeedCmPerS = verticalSpeedCmPerS;
                Grounded = grounded;
            }

            /// <summary>角色锚点的全局位置（ECEF 厘米）。</summary>
            public PlanetVector3 PositionCm { get; }
            /// <summary>切平面里的"朝前"单位向量（与径向垂直）。</summary>
            public PlanetVector3 ForwardTangent { get; }
            /// <summary>垂直速度（厘米/秒，向外为正）。</summary>
            public double VerticalSpeedCmPerS { get; }
            public bool Grounded { get; }

            /// <summary>离海平面高度（厘米）。</summary>
            public double AltitudeCm => PositionCm.Length - PlanetConstants.PlanetRadiusCm;

            public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "pos=({0:0.#},{1:0.#},{2:0.#}) alt={3:0.###} up={4} v={5:0.###} grounded={6}",
                PositionCm.X, PositionCm.Y, PositionCm.Z, AltitudeCm, Grounded ? 1 : 0,
                VerticalSpeedCmPerS, Grounded ? 1 : 0);
        }

        /// <summary>取一个站在地面上的初始状态（半径 = 地面海拔；朝向由调用方给）。</summary>
        public static State StandingAt(PlanetVector3 surfaceDirection, double groundElevationCm,
                                       PlanetVector3 desiredForward)
        {
            if (!PlanetSphericalMotion.TryUp(surfaceDirection, out var up))
                throw new ArgumentException($"落点退化：{surfaceDirection}", nameof(surfaceDirection));
            var position = up * (PlanetConstants.PlanetRadiusCm + groundElevationCm);
            if (!PlanetSphericalMotion.TryCharacterBasis(up, desiredForward, out var forward, out _, out _))
                throw new ArgumentException($"朝向退化：{desiredForward}", nameof(desiredForward));
            return new State(position, forward, 0.0, grounded: true);
        }

        /// <summary>
        /// 走一步。<paramref name="wishForward"/>／<paramref name="wishLeft"/> 是输入在
        /// "当前朝向"坐标系里的分量（各自 ∈ [-1,1]，合成长度超过 1 会被归一化）；
        /// <paramref name="groundElevationCm"/> 由调用方接基场探针，取不出时回 null。
        /// <para>返回 false 的每一种理由都分开给：<em>状态不可用</em>、<em>dt 不合法</em>、
        /// <em>地面取不出来还站在地上</em>——实机上它们对应完全不同的处置（钳输入 / 修控制器 / 补数据）。</para>
        /// </summary>
        public static bool TryStep(in State current, double dtSeconds,
                                   double wishForward, double wishLeft, bool jumpPressed,
                                   Func<PlanetVector3, double?> groundElevationCm,
                                   out State next, out StepOutcome outcome, out string why)
        {
            next = current;
            outcome = StepOutcome.Stood;
            why = "";

            if (!PlanetSphericalMotion.TryUp(current.PositionCm, out var up))
            {
                why = "状态不可用：位置退化成零向量或非有限值";
                return false;
            }
            if (!(dtSeconds > 0) || dtSeconds > MaxStepSeconds || !double.IsFinite(dtSeconds))
            {
                why = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "dt={0} 不在 (0, {1}] 秒内：卡帧按大 dt 积分会把角色按进地面，调用方要先钳", dtSeconds, MaxStepSeconds);
                return false;
            }
            if (!double.IsFinite(wishForward) || !double.IsFinite(wishLeft))
            {
                why = "输入分量非有限值";
                return false;
            }

            double? ground = groundElevationCm?.Invoke(current.PositionCm);
            if (current.Grounded && ground is null)
            {
                // 站在地上却不知道地面在哪：既不抬也不落，如实拒绝而不是把 0 当海平面用
                why = "站在地上但地面取不出来（基场没接上）：不知道该贴着哪一层";
                return false;
            }

            // ---- 水平：只走测地线，半径由后面垂直那一段单独决定 ----
            var forward = PlanetSphericalMotion.ProjectToTangent(current.ForwardTangent, up);
            if (forward.LengthSquared <= 1e-24) forward = current.ForwardTangent;   // 退化时保持原样，下面会照常拒绝
            var left = PlanetVector3.Cross(up, forward);
            var wish = forward * wishForward + left * wishLeft;
            double wishLength = wish.Length;
            // 朝向跟着走：本层不做"保持朝向的横移"（那需要一个额外的面朝参数，等真有需求再加）。
            // 走斜向时角色面朝合速度方向，与第三人称项目的惯例一致。
            var facing = forward;
            if (wishLength > 0)
            {
                var turned = PlanetSphericalMotion.ProjectToTangent(wish, up);
                if (turned.LengthSquared > 1e-24) facing = turned;
            }
            PlanetVector3 newPosition = current.PositionCm;
            if (wishLength > 0)
            {
                var direction = wish / wishLength;
                double distance = WalkSpeedCmPerS * Math.Min(1.0, wishLength) * dtSeconds;
                newPosition = PlanetSphericalMotion.StepAlongGeodesic(current.PositionCm, direction, distance);
            }

            // ---- 垂直：起跳 / 空中积分 / 落地 ----
            double verticalSpeed = current.VerticalSpeedCmPerS;
            bool grounded = current.Grounded;
            if (grounded)
            {
                if (jumpPressed)
                {
                    // 起跳这一帧位置不动：位移从下一帧开始算，滞空时间才对得上 FreeFall 的解析解
                    double takeoff = PlanetGravity.TakeoffSpeedForApex(JumpApexCm);
                    next = new State(current.PositionCm, facing, takeoff, grounded: false);
                    outcome = StepOutcome.Jumped;
                    return true;
                }

                // 走出边沿：地面比脚下低超过一个台阶，就不是"贴地"而是"开始掉"
                double altitude = current.PositionCm.Length - PlanetConstants.PlanetRadiusCm;
                if (altitude - ground!.Value > StepDownCm)
                {
                    next = new State(newPosition, facing, 0.0, grounded: false);
                    outcome = StepOutcome.Airborne;
                    return true;
                }

                // 贴地：半径跟地面走（上坡抬、下台阶踩），离地高度归零
                newPosition = ScaleToAltitude(newPosition, ground!.Value);
                bool moved = wishLength > 0;
                next = new State(newPosition, facing, 0.0, grounded: true);
                outcome = moved ? StepOutcome.Walked : StepOutcome.Stood;
                return true;
            }

            // 空中：常加速度的精确解（v·dt − ½g·dt²）。不用半隐式 Euler——它的跳跃顶点会低 v₀·dt/2：
            // 2 ms 时 0.54 cm 看不出来，60 fps 的 16.7 ms 就是 4.5 cm（3%），跳跃高度会随手感调参抖。
            double g = PlanetGravity.GameCmPerS2;
            double dropThisStep = verticalSpeed * dtSeconds - 0.5 * g * dtSeconds * dtSeconds;
            double nextVerticalSpeed = verticalSpeed - g * dtSeconds;
            double currentAltitude = newPosition.Length - PlanetConstants.PlanetRadiusCm;
            double targetAltitude = currentAltitude + dropThisStep;
            if (ground is { } gAt && targetAltitude <= gAt && nextVerticalSpeed <= 0)
            {
                newPosition = ScaleToAltitude(newPosition, gAt);
                next = new State(newPosition, facing, 0.0, grounded: true);
                outcome = StepOutcome.Landed;
                return true;
            }
            newPosition = ScaleToAltitude(newPosition, targetAltitude);
            next = new State(newPosition, facing, nextVerticalSpeed, grounded: false);
            outcome = StepOutcome.Airborne;
            return true;
        }

        /// <summary>沿径向把点放到指定海拔（不动方向，只改半径）。</summary>
        public static PlanetVector3 ScaleToAltitude(PlanetVector3 positionCm, double altitudeCm)
        {
            double length = positionCm.Length;
            if (!double.IsFinite(length) || length <= 1e-300)
                throw new ArgumentException($"位置退化：{positionCm}", nameof(positionCm));
            if (!double.IsFinite(altitudeCm))
                throw new ArgumentOutOfRangeException(nameof(altitudeCm), altitudeCm, "海拔必须有限");
            return positionCm * ((PlanetConstants.PlanetRadiusCm + altitudeCm) / length);
        }
    }
}
