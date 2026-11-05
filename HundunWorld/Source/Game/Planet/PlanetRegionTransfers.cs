using System;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Network;
using Horizon.Planet;
using Horizon.Planet.Wire;

namespace HundunWorld.Game.Planet
{
    /// <summary>
    /// 客户端换区上行的<em>唯一入口</em>：把"该不该发"交给 <see cref="PlanetRegionTransferRules"/>，
    /// 把"在飞几条"交给 <see cref="PlanetRegionTransferTracker"/>，这里只负责接上引擎时钟与网络会话。
    /// <para>为什么单独一层：决策规则要能离线跑（判据在 <c>Horizon.Game.Gateway.Tests</c> 里直接编译那份纯 C#），
    /// 而这一层必然碰 <see cref="FlaxEngine"/>（帧号、日志、会话对象），碰了就测不到。
    /// 把两条路切开，规则那半边才有真判据，而不是只能在编辑器里"看着对"。</para>
    /// <para><b>时钟单位用毫秒</b>：Flax 1.13 的 <c>FlaxEngine.Environment</c> 没有帧计数（编译器直接报了
    /// "does not contain a definition for 'FrameCount'"），而客户端各处的惯例是自己累加计数器，
    /// 这一层拿不到那个数。改用 <see cref="System.Environment.TickCount64"/>（Windows 上是自开机起的毫秒，
    /// 不受系统时间调整影响），失联预算也就是"2 秒还没回来"。服务端那边用的是 tick，两者不必混。</para>
    /// </summary>
    public static class PlanetRegionTransfers
    {
        /// <summary>在飞预算：2 秒还没回执就按"没回执"清账。</summary>
        public const long TimeoutMs = 2000;

        public static readonly PlanetRegionTransferTracker Tracker = new(TimeoutMs);

        /// <summary>这一层自己的单调时钟。写成方法是为了让"用哪个钟"这件事只有一处可以说错。
        /// <para>公开的 <see cref="NowMs"/> 是给同一层其它链（快照订阅）用的——两条链各写一个钟，
        /// 节流与超时就会按两种时间走。</para></summary>
        private static long Now() => System.Environment.TickCount64;

        /// <summary>同一个钟的对外名字。</summary>
        public static long NowMs() => Now();

        /// <summary>
        /// 把口径编成 §6.2 那 35 字节。<b>迁移上行与快照请求共用这一份</b>：
        /// 两处各写一遍 TryWrite，早晚会有一处忘了检查 written 或者少带一项。
        /// </summary>
        public static bool TryBuildCaliberBytes(out byte[] bytes, out string why)
        {
            bytes = new byte[PlanetHandshake.Size];
            if (!PlanetHandshake.TryWrite(bytes, Caliber, out int written) || written != PlanetHandshake.Size)
            {
                why = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "口径编不出来（written={0}/{1}）", written, PlanetHandshake.Size);
                bytes = Array.Empty<byte>();
                return false;
            }
            why = "";
            return true;
        }

        /// <summary>取当前会话。返回 null = 这条链还没接上网络，<see cref="TrySendAsync"/> 会如实报"发不出去"。
        /// 用委托而不是直接持有实例：会话会被重连换掉，持有旧实例会往一条死连接上发。</summary>
        public static Func<Network.NetworkManager> SessionProvider;

        /// <summary>
        /// 取当前角色号。<b>用委托而不是一个字段</b>：号是登录之后才有的，而重连会换号——
        /// 存下来那一刻的值，之后每条上行都带着那个旧号，服务端看到的正是 §13.53 取样顺序那一类病：
        /// 判定照跑、屏幕上什么都没有。0（还没进世界）由规则层如实拦下不发。
        /// </summary>
        public static Func<ulong> CharacterIdProvider = () => CharacterId;

        /// <summary>显式给定的角色号；<see cref="CharacterIdProvider"/> 没换过来之前用的就是它。</summary>
        public static ulong CharacterId;

        /// <summary>
        /// 客户端这一侧的星球口径，随每条迁移请求上行（§6.2）。
        /// <para>默认值是 <c>default(PlanetHandshake)</c>，它的 wireProtocol=0 ——
        /// <see cref="PlanetRegionTransferRules.TryBuild"/> 会因此<em>拒发</em>。这是刻意的：
        /// 基场没加载成功时发出去的请求带着"没有口径"，服务端只能报 wire 0 != 1，
        /// 而那条日志会把排查引向"服务端换了协议"。宁可在客户端先闭嘴并说明原因。</para>
        /// <para>谁来填：加载基场的那一方（<c>PlanetNearFieldDriver</c>）在 <see cref="PlanetHeightField"/>
        /// 建起来之后调一次 <see cref="SetCaliber"/>，把那份高度场自带的口径递过来。
        /// 这里<em>不再自己读 manifest</em>：客户端读一次、服务端读一次已经够多了，
        /// 同一进程内读两遍意味着"地形是 A 套、握手是 B 套"是可能发生的，而服务端只看得见 B。</para>
        /// </summary>
        public static PlanetHandshake Caliber;

        /// <summary>口径是怎么来的（成功时的目录 / 失败原因），给 HUD 与回执文件用。</summary>
        public static string CaliberStatus = "not loaded";

        /// <summary>
        /// 装上由高度场算出的口径。<b>只赋值、不推导</b>：推导在 <see cref="PlanetHeightField"/>，
        /// 那里也是服务端 <c>DemPlanetGroundSource</c> 用的同一处装配。
        /// </summary>
        public static void SetCaliber(in PlanetHandshake caliber, string demRoot)
        {
            Caliber = caliber;
            CaliberStatus = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "root={0} terrainVersion={1:x8} seed={2:x} basefield={3}",
                demRoot, caliber.TerrainVersion, caliber.TerrainSeed, caliber.BasefieldVersion);
        }

        /// <summary>
        /// 记一次"没装上"。<b>保持 <see cref="Caliber"/> 原值不动</b>并写清原因：
        /// 半途失败却把一个默认口径留在原地，等于让之后每条请求都带着 wire 0 出门。
        /// </summary>
        public static void MarkCaliberMissing(string why) =>
            CaliberStatus = "load failed: " + why;

        /// <summary>
        /// 本地这一份基场给的"脚下地面海拔"（方向 → 厘米，相对海平面），由 <c>PlanetNearFieldDriver</c>
        /// 在高度场建起来之后接上。<b>默认 null = 没有高度场，于是不抬位</b>。
        /// <para>为什么客户端要自己抬：换区请求带的是角色控制器给出的全局点，而控制器不读基场。
        /// 不抬位的后果不是"包被拒"这么轻——服务端拒了，客户端按 <c>Rebind</c> 重新落位，
        /// 带的还是同一个地下点，于是每次落位都被拒。这条判据在两端用的是同一个容差
        /// （<see cref="PlanetGround.BelowToleranceCm"/>），就是为了不出现"这边不抬、那边拒"的缝。</para>
        /// </summary>
        public static Func<PlanetVector3, double?> GroundElevationCm;

        /// <summary>抬位次数与最后一次的埋深（厘米），进回执行：悄悄改过的坐标比不改更难查。</summary>
        public static int GroundLifts;
        public static double LastGroundLiftCm;

        /// <summary>
        /// 发送前的一次修正。<b>算法在规则层</b>（<see cref="PlanetRegionTransferRules.LiftAboveGround"/>），
        /// 这里只负责取探针、记账、把改过坐标这件事写进日志。
        /// </summary>
        private static PlanetVector3 LiftBeforeSend(in PlanetVector3 globalCm)
        {
            var probe = GroundElevationCm;
            var lifted = PlanetRegionTransferRules.LiftAboveGround(globalCm, probe);
            if (lifted.X == globalCm.X && lifted.Y == globalCm.Y && lifted.Z == globalCm.Z) return lifted;
            double ground = probe(globalCm) ?? double.NaN;
            double pointElevation = globalCm.Length - PlanetConstants.PlanetRadiusCm;
            GroundLifts++;
            LastGroundLiftCm = PlanetGround.BelowGroundCm(ground, pointElevation);
            Debug.Log($"[planet] 换区点在地面以下，已抬到地面海拔：{PlanetGround.Describe(ground, pointElevation)}");
            return lifted;
        }

        /// <summary>最近一次处置结果，给 HUD/回执文件用。</summary>
        public static PlanetRegionTransferRules.ReceiptAction LastAction = PlanetRegionTransferRules.ReceiptAction.Ignore;
        public static long LastReceiptAtMs = -1;

        /// <summary>
        /// 最近一次<em>被受理</em>的回执原件（含 §6.2 那条 ack：新区局部厘米 + 原点 + 姿态）。
        /// <para>换基那一侧要用的就是这份原件，而不是自己再反查一遍：服务端算出来的帧与客户端自己猜的
        /// 帧对不上时，差的是一整个星球的朝向，而它不会报错。被拒时这里为 null——
        /// 半套 ack 比没有 ack 更危险，所以只在 Applied 上赋值。</para>
        /// </summary>
        public static PlanetRegionTransferResult LastAppliedAck;

        /// <summary>
        /// 换区时调这一次。返回 false 的三种原因分开记日志：<em>规则不让发</em>、<em>在飞没回来</em>、
        /// <em>发不出去</em>——它们在实机上的处置完全不同，混成一条"发送失败"就只能靠猜。
        /// </summary>
        public static async Task<bool> TrySendAsync(RegionKey current, PlanetVector3 globalCm, long tick)
        {
            long now = Now();

            // 地面修正排在 TryBuild 之前：埋在地下的点反查出来的区域是"地表下面那一格"，
            // 让它带着原坐标出门，服务端要么拒（然后客户端按重新落位再发一次同一个点），要么收下这个没有意义的归属。
            var point = LiftBeforeSend(globalCm);

            if (!PlanetRegionTransferRules.TryBuild(CharacterIdProvider(), current, point, tick, in Caliber,
                                                   out var request, out string buildWhy))
            {
                Debug.Log($"[planet] 换区上行跳过：{buildWhy} from={current}");
                return false;
            }
            // 口径按 §6.2 那 35 字节编码上行。编不出来就不发，而不是发一条"没带口径"的包：
            // 空数组在服务端解成 Truncated，回执读起来像"包坏了"，而真实原因是客户端这边没算出口径。
            // 这一步排在 Tracker.TryBegin 之前——发不出去的包不该占住在飞名额。
            if (!TryBuildCaliberBytes(out var caliberBytes, out string caliberWhy))
            {
                Debug.LogWarning($"[planet] 换区上行不发：{caliberWhy} from={current}");
                return false;
            }

            if (!Tracker.TryBegin(tick, now, current, out string why))
            {
                Debug.Log($"[planet] 换区上行被自己挡下：{why} from={current}");
                return false;
            }

            var session = SessionProvider?.Invoke();
            if (session == null || !session.CanSendMessage())
            {
                // 发不出去必须按"没回执"清账：留在飞标记里会把之后每一次换区都挡死
                Tracker.OnReceipt(false, false, false, 0, 0, now);
                Debug.LogWarning($"[planet] 换区上行发不出去（会话不可用），已按超时清账：{current} → {RegionKey.FromId(request.ToRegionId)}");
                return false;
            }

            var message = new PlanetRegionTransferRequest
            {
                CharacterId = request.CharacterId,
                FromRegionId = request.FromRegionId,
                ToRegionId = request.ToRegionId,
                GlobalXcm = request.Xcm,
                GlobalYcm = request.Ycm,
                GlobalZcm = request.Zcm,
                Tick = request.Tick,
                PlanetCaliber = caliberBytes,
            };

            bool sent;
            try
            {
                sent = await session.SendAsync(message);
            }
            catch (Exception e)
            {
                sent = false;
                Debug.LogWarning($"[planet] 换区上行抛异常（按超时清账）：{e.GetType().Name}");
            }
            if (!sent)
                Tracker.OnReceipt(false, false, false, 0, 0, now);
            else
                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "planet_transfer_ms={0} from={1} to={2} tick={3}", now, current,
                    RegionKey.FromId(request.ToRegionId), tick));
            return sent;
        }

        /// <summary>
        /// 最近一次 ack 的坐标系交叉检查结果。
        /// <para>服务端发完姿态就结束了，客户端解不回去的话"这一半协议错不错"永远没人知道——
        /// 这两个数就是把那条路接上的方式。实测贴地残差 ~3.5e-5 rad（≈0.002°，判据
        /// <c>ack换算回全局的误差随局部距离放大</c>量的），所以 0.01° 这个界已经比量化余量宽了 5 倍：
        /// 越过它不是"精度不够"，而是两端对同一块地面的切平面有两种说法。</para>
        /// </summary>
        public static string LastAckCheck = "not run";
        public static double LastAckAxisDeg = -1;

        /// <summary>超过它就告警而不是静默记录：这条检查的意义正在于"平时不发生"。</summary>
        public const double AckAxisWarnDeg = 0.01;

        private static void CheckAckFrame(PlanetRegionTransferResult result)
        {
            if (!result.Applied) return;                     // 被拒时字段全 0，解它只会制造假报警
            if (!PlanetRegionAckFrame.TryReconstruct(result, in Caliber, out var frame, out string why))
            {
                LastAckCheck = "rejected: " + why;
                LastAckAxisDeg = -1;
                Debug.LogWarning($"[planet] ack 坐标系解不开：{why}");
                return;
            }
            var clientFrame = RegionFrame.Build(RegionKey.FromId(result.NewRegionId), PlanetConstants.PlanetRadiusCm);
            double deg = PlanetRegionAckFrame.SignedAxisDeviationDeg(frame, clientFrame);
            LastAckAxisDeg = deg;
            LastAckCheck = deg <= AckAxisWarnDeg ? "consistent" : $"frame disagreement {deg:0.####}°";
            if (deg > AckAxisWarnDeg)
                Debug.LogWarning($"[planet] 服务端与客户端的切平面差 {deg:0.####}°，区域={result.NewRegionId}");
        }

        /// <summary>回执入口（网络处理器调这里）。</summary>
        public static void OnReceipt(PlanetRegionTransferResult result)
        {
            if (result is null)
            {
                Debug.LogWarning("[planet] 收到空的迁移回执");
                return;
            }
            long now = Now();
            LastAction = Tracker.OnReceipt(true, result.Accepted, result.Applied,
                                          (int)result.Code, result.NewRegionId, now);
            LastReceiptAtMs = now;
            // 只认 Applied 那一条：被拒时 ack 字段全 0，存下来就等于把"半套坐标系"留给换基那一侧
            LastAppliedAck = result.Applied ? result : null;
            if (result.Applied) CheckAckFrame(result);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "planet_transfer_receipt ms={0} accepted={1} applied={2} code={3} new_region={4} action={5} ack_check={6} axis_deg={7:0.#####} | {8}",
                now, result.Accepted, result.Applied, result.Code, result.NewRegionId, LastAction,
                LastAckCheck, LastAckAxisDeg, Tracker.Describe()));
        }


        /// <summary>对账用的一行，进驱动的回执文件。</summary>
        public static string Describe() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} last_action={1} last_receipt_ms={2} confirmed={3} needs_rebind={4} ack={5} "
                + "character={6} session={7} caliber_status={8} ack_check={9} ack_axis_deg={10:0.#####} "
                + "ground_probe={11} ground_lifts={12} ground_lift_cm={13:0.###}",
                Tracker.Describe(), LastAction, LastReceiptAtMs, Tracker.ConfirmedRegionId, Tracker.NeedsRebind ? 1 : 0,
                LastAppliedAck is null ? "none" : $"{LastAppliedAck.NewRegionId}@{LastAppliedAck.LocalXCm},{LastAppliedAck.LocalYCm},{LastAppliedAck.LocalZCm}",
                CharacterIdProvider(), SessionProvider?.Invoke() is null ? "none" : "live", CaliberStatus,
                LastAckCheck, LastAckAxisDeg,
                GroundElevationCm is null ? "off" : "on", GroundLifts, LastGroundLiftCm);

        /// <summary>
        /// 一次会话开始时把账倒回干净状态。<b>装配那一侧（<c>PlanetNearFieldDriver.Start</c>）第一件事就是它</b>：
        /// 这一层的状态全挂在静态上，而 Flax 的脚本装配跨 PIE 会话存活，于是上一轮留下的账会直接变成
        /// 新会话的症状——<c>TryBegin</c> 里那句"上一条还在飞"会挡掉新会话的第一次换区，
        /// 上一轮的 <see cref="LastAppliedAck"/> 会让换基那一侧拿着一个旧会话的坐标系用，
        /// 而 <see cref="GroundElevationCm"/> 指着<em>上一轮那个已经 Dispose 掉的</em> <c>PlanetHeightField</c>
        /// （闭包捕获的是驱动实例的字段，驱动换了新的，旧的连同它的文件句柄一起成了孤儿）。
        /// <para><b>只清账，不动接线</b>：<c>SessionProvider</c> / <c>CharacterIdProvider</c> 由装配每轮重新赋值，
        /// 这里清成 null 反而会把"这层压根没接会话"和"会话刚换掉"压成同一种症状。</para>
        /// <para>判据在 <c>PlanetRegionSessionResetGateTests</c>：它扫这份声明清单，要求每个 public static
        /// 状态<em>要么在这里被清、要么在排除名单里写明为什么不归这里管</em>。日后加一个静态而没想清楚这件事时，
        /// 那条会红——漏清一个状态位的症状是"下一轮换区莫名其妙地不发"，靠读日志基本查不到。</para>
        /// </summary>
        public static void BeginSession()
        {
            Tracker.Reset();
            CharacterId = 0UL;
            Caliber = default;
            CaliberStatus = "not loaded";
            GroundElevationCm = null;
            GroundLifts = 0;
            LastGroundLiftCm = 0.0;
            LastAction = PlanetRegionTransferRules.ReceiptAction.Ignore;
            LastReceiptAtMs = -1;
            LastAppliedAck = null;
            LastAckCheck = "not run";
            LastAckAxisDeg = -1;
            PlanetRegionWatches.Reset();
        }
    }
}
