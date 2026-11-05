using System;
using Horizon.Planet;
using Horizon.Planet.Wire;

namespace HundunWorld.Game.Planet
{
    /// <summary>
    /// 客户端换区上行的决策规则。<b>纯 C#，不含任何引擎类型</b>——判据要能离线真跑，
    /// 而不是只在编辑器里"看着对"（源码在 <c>Horizon.Game.Gateway.Tests</c> 里被直接编译进去测）。
    /// <para>这里刻意只做"该不该发 / 收到之后怎么办"这两件事。<b>坐标系换基本身不在这里</b>：
    /// 换基由 <c>PlanetNearFieldDriver</c> 无条件跟着全局点走（§13.15 的教训：客户端把服务端的
    /// 共边不变量误用成了自己的规则，沿局部东向走会合法地对角跨格）。服务端那一趟是另一回事——
    /// 它决定的是"权威侧认不认这次换区、什么时候交接"，不是"我站在哪一块切平面上"。</para>
    /// </summary>
    public static class PlanetRegionTransferRules
    {
        /// <summary>一条上行请求的全部字段（线上是 <c>PlanetRegionTransferRequest</c>，这里只给值不给类型，
        /// 免得决策规则绑上网络程序集）。</summary>
        public readonly struct Request
        {
            public Request(ulong characterId, long fromRegionId, long toRegionId,
                           long xCm, long yCm, long zCm, long tick, in PlanetHandshake caliber)
            {
                CharacterId = characterId;
                FromRegionId = fromRegionId;
                ToRegionId = toRegionId;
                Xcm = xCm;
                Ycm = yCm;
                Zcm = zCm;
                Tick = tick;
                Caliber = caliber;
            }

            public ulong CharacterId { get; }
            public long FromRegionId { get; }

            /// <summary>由全局点反查出来的区域，<b>不接受调用方直接给</b>（见 <see cref="TryBuild"/>）。</summary>
            public long ToRegionId { get; }
            public long Xcm { get; }
            public long Ycm { get; }
            public long Zcm { get; }
            public long Tick { get; }

            /// <summary>客户端的星球口径，随每条迁移请求上行（§6.2 的握手字段）。
            /// 写线上时由 <c>PlanetHandshake.TryWrite</c> 编码，服务端 <c>TryRead</c> 解码——
            /// 这条路上没有第二份字段搬运，也就没有"两端各错一项"的机会。</summary>
            public PlanetHandshake Caliber { get; }
        }

        /// <summary>收到回执之后该做什么。</summary>
        public enum ReceiptAction
        {
            /// <summary>没有回执（超时/丢包）。<b>绝不等于受理</b>：客户端不许据此换权威交接 tick。</summary>
            Ignore,
            /// <summary>服务端裁定的区域和我们的当前块不同：按服务端那份换掉本地信念（含被拒时回带的真实归属）。</summary>
            AdoptServerRegion,
            /// <summary>服务端确认我们就在这儿（同区/空操作）：什么都不用改，把这次当成一次对账成功。</summary>
            KeepAndConfirm,
            /// <summary>tick 倒退被拒：等本地时钟追上回执里的 <c>ServerTick</c> 再发，别改归属。</summary>
            WaitClock,
            /// <summary>服务端没有我们的归属（或请求压根没进判定）：重新落位，而不是照回执里的区域号切区。</summary>
            Rebind,
            /// <summary>
            /// 服务端说"你和我不在同一颗星球上"（§6.2 的口径不符）。
            /// <para><b>这一条绝不能落到"重新落位"或"重试"</b>：重新落位带的还是同一份口径，
            /// 于是每次落位都被拒——客户端在自己的错误里无限打转，而屏幕上什么都没有。
            /// 该做的是停下发迁移、把理由报出去，等基场数据同步完（或换一个 build）。</para>
            /// </summary>
            RefuseWorld,
        }

        /// <summary>
        /// 该不该发这次换区请求。<b>失败原因逐条给出来</b>，因为"没发"在实机上和"发了被拒"一样难查。
        /// <para><paramref name="toRegionId"/> 这种参数压根不存在——目标区域只能由全局点反查。
        /// 这不是偷懒：把 from 抄进 to 是 §13.15 那个真实缺陷的形状，接口上没有那个入口就抄不了。</para>
        /// <para><paramref name="caliber"/> 是<em>必填</em>的：构造一条不声明口径的迁移请求在接口上做不到。
        /// 留空就会让"没握手也能换区"成为默认路径，而 §6.2 要防的恰恰是这件事。</para>
        /// </summary>
        public static bool TryBuild(ulong characterId, RegionKey current, PlanetVector3 globalCm,
                                    long tick, in PlanetHandshake caliber, out Request request, out string why)
        {
            request = default;
            why = "";

            // 口径没填就在构造阶段拦下，而不是发出去等服务端拒：默认值 PlanetHandshake{} 的
            // wireProtocol 是 0，服务端会报"wire 0 != 1"——那条理由在客户端日志里读起来像
            // "服务端换了协议版本"，而真实原因是我们这边压根没算口径（DEM 没加载成功是常见触发）。
            if (caliber.WireProtocol != PlanetHandshake.ProtocolVersion)
            {
                why = string.Format(Inv, "客户端口径没填（wireProtocol={0}，应为 {1}）：基场还没加载出来就不该发迁移",
                                    caliber.WireProtocol, PlanetHandshake.ProtocolVersion);
                return false;
            }
            if (caliber.PlanetRadiusCm != PlanetConstants.PlanetRadiusCm)
            {
                why = string.Format(Inv, "口径里的半径 {0} 与全局口径 {1} 不一致：客户端选了另一颗星球",
                                    caliber.PlanetRadiusCm, PlanetConstants.PlanetRadiusCm);
                return false;
            }
            if (caliber.RegionLevel != current.Level)
            {
                // 区域号是按 current.Level 打包的，口径里写着别的层级，服务端就会把同一个点算成另一块区域
                why = string.Format(Inv, "口径里的区域层级 {0} 与要换的这块区域的层级 {1} 不一致",
                                    caliber.RegionLevel, current.Level);
                return false;
            }

            if (characterId == 0UL)
            {
                why = "角色号为 0（还没进世界）";
                return false;
            }
            if (characterId > long.MaxValue)
            {
                // 服务端权威表与位打包区域号都是有符号 long：unchecked 收窄会翻成负数，
                // 那是一条"看起来合法"的假归属，比不发危险得多。
                why = string.Format(Inv, "角色号 {0} 超出 int64，不该发（发出去服务端只会按 MalformedMessage 拒）", characterId);
                return false;
            }

            double length = globalCm.Length;
            if (!double.IsFinite(length) || length <= 1e-300)
            {
                why = "全局点退化（零向量或非有限），反查不出区域";
                return false;
            }
            if (Math.Abs(length - PlanetConstants.PlanetRadiusCm) > PlanetRegions.MaxTransferAltitudeCm)
            {
                why = string.Format(Inv, "离海面 {0:0} km，超出迁移口径，服务端会按 TooFarFromSurface 拒",
                                    (length - PlanetConstants.PlanetRadiusCm) / 100000.0);
                return false;
            }

            var next = RegionKey.FromDirection(globalCm / length, current.Level);
            if (next == current)
            {
                why = "还没真的走出当前区域";
                return false;
            }
            if (tick <= 0)
            {
                why = "tick 必须为正";
                return false;
            }

            var ecef = EcefCentimeters.FromVector(globalCm);
            request = new Request(characterId, current.ToId(), next.ToId(), ecef.X, ecef.Y, ecef.Z, tick, in caliber);
            return true;
        }

        /// <summary>
        /// 回执处置。<paramref name="hasReceipt"/> 为 false 表示超时/丢包——那条路一律 <see cref="ReceiptAction.Ignore"/>，
        /// 因为"没收到"和"被拒"在客户端必须走不同的路：前者不能改本地已经生效的换基，后者要按码处理。
        /// </summary>
        public static ReceiptAction Decide(bool hasReceipt, bool accepted, bool applied,
                                          int code, long newRegionId, RegionKey current)
        {
            if (!hasReceipt) return ReceiptAction.Ignore;

            if (accepted && applied)
            {
                // 0 是"服务端没有归属可报"的约定值。声称"改了归属"却不给归属是不可信的回执：
                // 切到 0 号区域等于切到一个不存在的切平面上，只能重新落位。
                if (newRegionId == 0) return ReceiptAction.Rebind;
                return newRegionId == current.ToId() ? ReceiptAction.KeepAndConfirm : ReceiptAction.AdoptServerRegion;
            }
            if (accepted)
                return ReceiptAction.KeepAndConfirm;          // 合法但没改归属（同区重复请求）：一次对账成功

            switch ((Horizon.Game.Message.Network.PlanetRegionRejectCode)code)
            {
                case Horizon.Game.Message.Network.PlanetRegionRejectCode.SameRegion:
                    return ReceiptAction.KeepAndConfirm;
                case Horizon.Game.Message.Network.PlanetRegionRejectCode.TickRegression:
                    return ReceiptAction.WaitClock;
                case Horizon.Game.Message.Network.PlanetRegionRejectCode.UnknownEntity:
                case Horizon.Game.Message.Network.PlanetRegionRejectCode.MalformedMessage:
                case Horizon.Game.Message.Network.PlanetRegionRejectCode.SessionMismatch:
                    return ReceiptAction.Rebind;
                case Horizon.Game.Message.Network.PlanetRegionRejectCode.CaliberMismatch:
                    // 必须排在 default 前面：走 default 会得到 Rebind，而重新落位带的还是同一份口径，
                    // 于是"落位→被拒→落位"循环到永远。口径不符不是归属问题，是"不在同一个世界"。
                    return ReceiptAction.RefuseWorld;
                case Horizon.Game.Message.Network.PlanetRegionRejectCode.Underground:
                    // 服务端说"你报的那个点在地下"。这与口径不符<em>不</em>同类：那条本地没法修，
                    // 这条本地能修——发送侧在 TryBuild 之前就会把点抬到地面（LiftAboveGround），
                    // 所以"重新落位"带的是抬过的点，一次就收敛，不会循环。
                    // 走 default 会得到 AdoptServerRegion：那是把一个地下位置的区域信念换成服务端的区域，
                    // 位置本身还在地里——屏幕上的表现是"换了基但还是穿模"。
                    return ReceiptAction.Rebind;
                default:
                    // 几何类拒绝（不共边/声明与反查不符/层级不符/坏包）：服务端已经把它认定的归属回带过来。
                    // 有值就照它换（我们本地的信念才是错的那一方），没值（0）就只能重新落位。
                    return newRegionId != 0 ? ReceiptAction.AdoptServerRegion : ReceiptAction.Rebind;
            }
        }

        /// <summary>
        /// 把埋在地面下的点抬到地面上：<b>只改半径，方向不动</b>，因此反查出来的区域与埋着时是同一块，
        /// 换区语义不受影响。取不出海拔（probe 为 null 或回 null）、点退化、
        /// 或埋深没超过共用的容差时原样返回。
        /// <para>为什么放在规则层而不是发送层：抬位的门槛与服务端拒收的门槛必须是同一个数
        /// （都在 <see cref="PlanetGround"/>），而只有放在这里才能被离线判据钉住——发送层碰着
        /// FlaxEngine 的会话，判据跑不到。</para>
        /// <para>不抬位的后果不是"包被拒"这么轻：服务端拒了，客户端按重新落位再发一次，
        /// 带的还是同一个地下点，于是每次落位都被拒。这条分支在实机上的症状是"站在山脚下永远进不去下一区"。</para>
        /// </summary>
        public static PlanetVector3 LiftAboveGround(in PlanetVector3 globalCm,
                                                    Func<PlanetVector3, double?> groundElevationCm)
        {
            if (groundElevationCm is null) return globalCm;
            double ground = groundElevationCm(globalCm) ?? double.NaN;
            double length = globalCm.Length;
            if (!double.IsFinite(ground) || !double.IsFinite(length) || length <= 1e-300) return globalCm;
            double pointElevation = length - PlanetConstants.PlanetRadiusCm;
            if (PlanetGround.BelowGroundCm(ground, pointElevation) <= PlanetGround.BelowToleranceCm) return globalCm;
            return globalCm * ((PlanetConstants.PlanetRadiusCm + ground) / length);
        }

        private static readonly System.IFormatProvider Inv = System.Globalization.CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// 在飞的区域迁移请求的账。<b>刻意做成实例而不是静态</b>：本仓库已经吃过共享静态串扰的亏
    /// （`BaselineResyncE2ETests` 那句"注入本测试独立 buffer，消除共享静态串扰"就是那次教训），
    /// 判据要能一条一条独立跑。
    /// <para>为什么需要这一层：站在区域边界上抖几厘米就会每帧发一次迁移请求，而服务端每次都要落盘。
    /// 单条在飞 + 超时重发这两条规矩把这件事压住，同时"超时"必须明确按<em>没回执</em>处理——
    /// 把超时当成被拒会让我们错误地放弃一次其实已经生效的交接。</para>
    /// </summary>
    public sealed class PlanetRegionTransferTracker
    {
        /// <summary>在飞请求的失联预算（单位由调用方给：帧号或 tick 号都行，只要单调）。</summary>
        public long TimeoutUnits { get; }

        public PlanetRegionTransferTracker(long timeoutUnits = 120)
        {
            if (timeoutUnits <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutUnits), timeoutUnits, "超时预算必须为正");
            TimeoutUnits = timeoutUnits;
        }

        public bool HasPending { get; private set; }
        public long PendingSince { get; private set; }
        public long LastSentTick { get; private set; }

        /// <summary>这条在飞请求当时报的 from。<b>回执回来时用它当"当前归属"</b>——
        /// 处理器那一侧拿不到玩家的区域，而"服务端确认的就是我这一块"这个判断必须对着发出去的那一块算。</summary>
        public long PendingFromRegionId { get; private set; }

        /// <summary>被 tick 倒退拒过之后，本地时钟要越过这个服务端值才许再发（0 = 没在等）。</summary>
        public long WaitUntilServerTick { get; private set; }

        /// <summary>服务端说"我们没有你的归属"，需要重新落位。读一次即消费。</summary>
        public bool NeedsRebind { get; private set; }

        /// <summary>最近一次服务端裁定的归属（0 = 还没收到过任何带归属的回执）。</summary>
        public long ConfirmedRegionId { get; private set; }

        public long Sent { get; private set; }
        public long RefusedInFlight { get; private set; }
        public long RefusedWaitingClock { get; private set; }
        public long Expired { get; private set; }
        public long Receipts { get; private set; }

        /// <summary>
        /// 回到刚构造时的状态。<b>换一次会话必须先调它</b>：引擎里这些账挂在静态上，
        /// 上一次 PIE 留下的"在飞没回来"会让新会话第一次换区被自己挡下，
        /// 而上一次留下的 <see cref="ConfirmedRegionId"/> 会让新会话把一条其实属于别人的裁定当成"已确认"。
        /// 计数器也一起清零——回执与判据里的账要对得上<em>这一次</em>会话，不然"发送 0 次"这种读数
        /// 分不清是本轮没发还是上一轮没清。
        /// <para><see cref="TimeoutUnits"/> 是构造时给定的配置，不是状态，所以它<em>不</em>跟着清。</para>
        /// </summary>
        public void Reset()
        {
            HasPending = false;
            PendingSince = 0;
            LastSentTick = 0;
            PendingFromRegionId = 0;
            WaitUntilServerTick = 0;
            NeedsRebind = false;
            ConfirmedRegionId = 0;
            Sent = 0;
            RefusedInFlight = 0;
            RefusedWaitingClock = 0;
            Expired = 0;
            Receipts = 0;
        }

        /// <summary>该不该发这一条。失败时给出原因，实机排查全靠这行字。</summary>
        public bool TryBegin(long requestTick, long now, RegionKey from, out string why)
        {
            why = "";
            if (HasPending && now - PendingSince <= TimeoutUnits)
            {
                RefusedInFlight++;
                why = string.Format(Inv, "上一条还在飞（{0} 单位前发的，失联预算 {1}）",
                                    now - PendingSince, TimeoutUnits);
                return false;
            }
            if (HasPending)
            {
                Expired++;
                HasPending = false;               // 超时：按"没回执"清账，不改任何归属
            }
            if (requestTick <= WaitUntilServerTick)
            {
                RefusedWaitingClock++;
                why = string.Format(Inv, "tick {0} 还没越过服务端时钟 {1}（上次被按 tick 倒退拒过）",
                                    requestTick, WaitUntilServerTick);
                return false;
            }

            HasPending = true;
            PendingSince = now;
            LastSentTick = requestTick;
            PendingFromRegionId = from.ToId();
            Sent++;
            return true;
        }

        /// <summary>
        /// 回执进账。<paramref name="hasReceipt"/>=false 就是超时/丢包那条路：
        /// 只清在飞标记，<b>不动归属</b>，也不清 <see cref="WaitUntilServerTick"/>。
        /// </summary>
        public PlanetRegionTransferRules.ReceiptAction OnReceipt(bool hasReceipt, bool accepted, bool applied,
                                                                 int code, long newRegionId, long now)
        {
            if (!HasPending)
                return PlanetRegionTransferRules.ReceiptAction.Ignore;      // 没人等这条回执：迟到的旧回执

            var current = RegionKey.FromId(PendingFromRegionId);
            HasPending = false;
            var action = PlanetRegionTransferRules.Decide(hasReceipt, accepted, applied, code, newRegionId, current);
            if (!hasReceipt)
            {
                Expired++;
                return action;
            }

            Receipts++;
            switch (action)
            {
                case PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion:
                    ConfirmedRegionId = newRegionId;
                    WaitUntilServerTick = 0;
                    break;
                case PlanetRegionTransferRules.ReceiptAction.KeepAndConfirm:
                    ConfirmedRegionId = current.ToId();
                    WaitUntilServerTick = 0;
                    break;
                case PlanetRegionTransferRules.ReceiptAction.WaitClock:
                    // 不等过服务端时钟就再发，只会被同一条 TickRegression 一路拒下去
                    WaitUntilServerTick = now;
                    break;
                case PlanetRegionTransferRules.ReceiptAction.Rebind:
                    NeedsRebind = true;
                    ConfirmedRegionId = 0;
                    WaitUntilServerTick = 0;
                    break;
            }
            return action;
        }

        /// <summary>取走"需要重新落位"这个请求（消费一次即清，避免每次换区都重发落位）。</summary>
        public bool ConsumeRebind()
        {
            if (!NeedsRebind) return false;
            NeedsRebind = false;
            return true;
        }

        /// <summary>对账用的一行。</summary>
        public string Describe() => string.Format(Inv,
            "transfers sent={0} inflight_refused={1} clock_refused={2} expired={3} receipts={4} pending={5}",
            Sent, RefusedInFlight, RefusedWaitingClock, Expired, Receipts, HasPending ? 1 : 0);

        // 与规则类同一份不变文化：账要跨端逐字对得上，不能跟着区域设置走
        private static readonly System.IFormatProvider Inv = System.Globalization.CultureInfo.InvariantCulture;
    }
}
