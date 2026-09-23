using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Game.Sim;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 单实体金轨迹生成 / 指纹追加器（阶段 6.2 确定性对拍）。
///
/// <para><b>背景</b>：本类原先位于 <c>.freebuff/Verify</c>（独立验证工具项目），
/// 该目录已在仓库中丢失，导致 <see cref="TraceRegenerationValidationTests"/> 无法编译。
/// 丢失的是<b>工具位置</b>而非<b>规格</b>——规格完整保留在三处：</para>
/// <list type="number">
///   <item><c>TraceRegenerationValidationTests</c> 的调用点（<c>BuildTrace()</c> / <c>BuildFileContent(lines)</c>）；</item>
///   <item>金轨迹文件 <c>Traces/deterministic_trace_600.txt</c> 头部的 13 字段格式说明与 597 行真实数据；</item>
///   <item><c>DeterministicReplayParityTests</c> 的端点断言（首帧 X=0.10000001、末尾 X/Y/Z/Vz、
///   修正 tick 118/178/418 等）。</item>
/// </list>
///
/// <para><b>与多实体版的关系</b>：<see cref="MultiEntityTraceGen"/> 把共用逻辑放在类内供测试直接调用；
/// 本类则被单实体对拍测试<b>内联复刻</b>（<c>DeterministicReplayParityTests.AppendRecord</c> 逐字相同）。
/// 改动本类的指纹逻辑时，必须同步那边——两处漂移会让"同一套模拟换宿主"的对拍失去意义。</para>
///
/// <para><b>场景</b>（来自金轨迹头注释）：单实体，出生 (0,0,0)，客户端 60Hz 连续预测；
/// 间隙丢帧（20/220/570 帧）与批量到达（80/360/420/585 帧）模拟真实网络到达。</para>
/// </summary>
public static class DeterministicTraceGen
{
    /// <summary>轨迹唯一实体。</summary>
    public const ulong EntityId = 1;

    /// <summary>实体出生点（米）。</summary>
    public const float SpawnX = 0f;

    /// <summary>实体出生点（米）。</summary>
    public const float SpawnY = 0f;

    /// <summary>实体出生点（米）。</summary>
    public const float SpawnZ = 0f;

    /// <summary>实体移动速度（米/秒）。客户端预测与权威回放共用。</summary>
    public const float MaxSpeed = 6f;

    /// <summary>服务器 tick 总数（0..592）。</summary>
    public const int ServerTicks = 593;

    /// <summary>客户端帧数。</summary>
    public const int ClientFrames = 600;

    /// <summary>输入行数（600 客户端帧 − 3 个丢帧）。</summary>
    public const int InputLines = 597;

    /// <summary>嵌入式金轨迹资源名。</summary>
    public const string TraceResourceName = "Horizon.Game.Gateway.Tests.Traces.deterministic_trace_600.txt";

    /// <summary>
    /// 轨迹中的一行输入。字段必须可写（以对象初始化器构造）。
    /// <para>注意：本类型与 <see cref="MultiEntityTraceGen.InputLine"/> <b>刻意不共用</b>——
    /// 单实体轨迹没有 <c>entityId</c> 列，字段集不同；强行合并会让两边都变得别扭。</para>
    /// </summary>
    public sealed class InputLine
    {
        /// <summary>该输入到达的服务器 tick。</summary>
        public int ServerTick;

        /// <summary>客户端输入包。</summary>
        public InputPacket Input = new();

        /// <summary>客户端报告的预测终点 X。</summary>
        public float ReportedEndX;

        /// <summary>客户端报告的预测终点 Y。</summary>
        public float ReportedEndY;

        /// <summary>客户端报告的预测终点 Z。</summary>
        public float ReportedEndZ;
    }

    // ── 指纹 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 追加一条移动记录。字节格式由 <c>DeterministicReplayParityTests</c> 的类文档逐字规定：
    /// X/Y/Z/Vz/VelocityX/VelocityY 各 4 字节 IEEE-754 位模式（LE）+ MovementMode 1 字节
    /// + IsGrounded 1 字节 + HadInput 1 字节 + LastProcessedClientTick 8 字节 LE
    /// + 修正存在标志 1 字节（存在时追加 Reason 1 字节 + CorrectedX/Y/Z/CorrectedVz/DriftMeters
    /// 各 4 字节位模式 + LastProcessedClientTick 8 字节 LE）。
    /// </summary>
    public static void AppendRecord(IncrementalHash digest, SimWorld.StepResult step)
    {
        if (digest is null) throw new ArgumentNullException(nameof(digest));

        AppendFloat(digest, step.AuthoritativeEnd.X);
        AppendFloat(digest, step.AuthoritativeEnd.Y);
        AppendFloat(digest, step.AuthoritativeEnd.Z);
        AppendFloat(digest, step.AuthoritativeVz);
        AppendFloat(digest, step.VelocityX);
        AppendFloat(digest, step.VelocityY);
        digest.AppendData(new[] { (byte)step.MovementMode });
        digest.AppendData(new[] { (byte)(step.IsGrounded ? 1 : 0) });
        digest.AppendData(new[] { (byte)(step.HadInput ? 1 : 0) });
        digest.AppendData(BitConverter.GetBytes(step.LastProcessedClientTick));

        var correction = step.Correction;
        if (correction is null)
        {
            digest.AppendData(new byte[] { 0 });
        }
        else
        {
            digest.AppendData(new byte[] { 1 });
            digest.AppendData(new[] { (byte)correction.Reason });
            AppendFloat(digest, correction.CorrectedX);
            AppendFloat(digest, correction.CorrectedY);
            AppendFloat(digest, correction.CorrectedZ);
            AppendFloat(digest, correction.CorrectedVz);
            AppendFloat(digest, correction.DriftMeters);
            digest.AppendData(BitConverter.GetBytes(correction.LastProcessedClientTick));
        }
    }

    /// <summary>
    /// 追加一条移动记录（散参版，供 grain 路径使用——那里状态来自镜像而非 <see cref="SimWorld.StepResult"/>）。
    /// </summary>
    public static void AppendRecord(
        IncrementalHash digest,
        float x, float y, float z, float vz,
        float velX, float velY,
        MovementMode mode, bool grounded, bool hadInput, long lastAck,
        CorrectionPacket? correction)
    {
        if (digest is null) throw new ArgumentNullException(nameof(digest));

        AppendFloat(digest, x);
        AppendFloat(digest, y);
        AppendFloat(digest, z);
        AppendFloat(digest, vz);
        AppendFloat(digest, velX);
        AppendFloat(digest, velY);
        digest.AppendData(new[] { (byte)mode });
        digest.AppendData(new[] { (byte)(grounded ? 1 : 0) });
        digest.AppendData(new[] { (byte)(hadInput ? 1 : 0) });
        digest.AppendData(BitConverter.GetBytes(lastAck));

        if (correction is null)
        {
            digest.AppendData(new byte[] { 0 });
        }
        else
        {
            digest.AppendData(new byte[] { 1 });
            digest.AppendData(new[] { (byte)correction.Reason });
            AppendFloat(digest, correction.CorrectedX);
            AppendFloat(digest, correction.CorrectedY);
            AppendFloat(digest, correction.CorrectedZ);
            AppendFloat(digest, correction.CorrectedVz);
            AppendFloat(digest, correction.DriftMeters);
            digest.AppendData(BitConverter.GetBytes(correction.LastProcessedClientTick));
        }
    }

    /// <summary>追加 <c>float</c> 的 IEEE-754 位模式（4 字节 LE）。</summary>
    public static void AppendFloat(IncrementalHash digest, float v)
        => digest.AppendData(BitConverter.GetBytes(BitConverter.SingleToInt32Bits(v)));

    /// <summary>取哈希并转小写 hex。</summary>
    public static string ToHex(IncrementalHash digest)
        => Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();

    // ── 生成 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 生成单实体金轨迹内容。
    /// <para>客户端用 <see cref="MovementFormula"/> 做连续预测（出生 (0,0,0)，恒 +X 满速前进），
    /// 并复现真实网络到达调度：间隙丢帧 21/221/571 与批量到达 81/361/421/586。</para>
    ///
    /// <para><b>serverTick 的真实语义</b>（对入库金轨迹逐行反推所得，勿凭直觉改）：</para>
    /// <para>
    /// 设 <c>offset(clientTick) = clientTick - serverTick</c>，则 offset 是一条<b>阶梯曲线</b>，
    /// 只在特定 clientTick 处抬升，且台阶高度由"批量帧的滞留帧数"决定：
    /// </para>
    /// <list type="bullet">
    ///   <item><c>ct 1..81</c> → offset = 1（基线：<c>st = ct - 1</c>）；</item>
    ///   <item><c>ct 82</c> → 2，<c>ct 83..421</c> → 3（batch@81：一次抬升吃掉 2 格）；</item>
    ///   <item><c>ct 422 → 4</c>、<c>ct 423 → 5</c>、<c>ct 424..586</c> → 6（batch@421：连抬 3 格）；</item>
    ///   <item><c>ct 587 → 7</c>、<c>ct 588..600</c> → 8（batch@586：抬 2 格）。</item>
    /// </list>
    /// <para>
    /// 即：<b>批量帧造成 serverTick 重复</b>（同一 tick 承载多帧输入，实测 <c>st=80</c> 出现 3 次、
    /// <c>st=418</c> 出现 4 次、<c>st=580</c> 出现 3 次）；而<b>间隙丢帧的 clientTick 直接缺席</b>
    /// （<c>ct = 21 / 221 / 571</c> 在文件中根本不存在），且<b>不</b>改变 offset 台阶。
    /// </para>
    /// <para>
    /// 实现上直接对"offset 阶梯"建模，而不是拼凑 <c>frame ± n</c> 类算术公式——
    /// 阶梯高度与批量帧间隔相关，非线性，任何线性公式都会在批量点之后系统性偏移
    /// （实测多种公式均产生 279~577 行漂移）。当前实现与金轨迹<b>逐字节相等</b>。
    /// </para>
    /// </summary>
    /// <returns>(输入行, 头注释行) —— 供 <see cref="BuildFileContent(IReadOnlyList{InputLine})"/> 拼接。</returns>
    public static (List<InputLine> Lines, string[] Header) BuildTrace()
    {
        // 间隙与批量到达帧（来自金轨迹头注释；注意这里是 clientTick 值）。
        var gapClientTicks = new HashSet<long> { 21, 221, 571 };

        // offset 阶梯的抬升点：clientTick → 新的 offset 值。
        // 每次批量帧会在其后连续若干 clientTick 上把 offset 逐格抬升。
        var offsetJumps = new Dictionary<long, int>
        {
            [1] = 1,
            [82] = 2,
            [83] = 3,
            [422] = 4,
            [423] = 5,
            [424] = 6,
            [587] = 7,
            [588] = 8,
        };

        var lines = new List<InputLine>(InputLines);

        var posX = SpawnX;
        var posY = SpawnY;
        var posZ = SpawnZ;
        var vz = 0f;
        var jumpCount = 0;
        var grounded = true;

        var offset = 1;

        for (var frame = 1; frame <= ClientFrames; frame++)
        {
            var clientTick = (long)frame;

            // 客户端侧移动预测（与权威共用 MovementFormula 的固定步长语义，
            // 故首帧 X = 6 m/s × 1/60 s = 0.10000001）。
            //
            // ⚠️ 预测推进必须**先于**丢帧判断：金轨迹在丢帧处（ct 20 → 22）
            //    位置**连跳两格**（2.0000002 → 2.2），证明"输入丢失"只影响上行网络，
            //    客户端本地预测照常推进；垂向同理——丢帧帧若处于按压相位，
            //    冲量仍照常施加（ct=571 是关键证据：只有它参与冲量，
            //    ct=572 的 z 才等于金轨迹 29.433344）。
            var (moveX, moveY, inputBits, maxSpeed) = MotionScript(frame);

            // 水平：MovementFormula.StepHorizontal 的逐位等价实现
            // （夹紧 → 归一化 → 线性步进，运算顺序严格固定）。
            var (nx, ny) = MovementFormula.StepHorizontal(posX, posY, moveX, moveY, Dt, maxSpeed);

            // 垂直：MovementFormula.StepVertical 的逐位等价实现
            // （跳跃冲量 → 重力积分 → 终端速度截断），跳跃计数演进收敛到 JumpStateMachine。
            var jumpPressed = (inputBits & InputBitJump) != 0;
            var isQinggongJump = (inputBits & InputBitQinggong) != 0;
            float nz;
            if (grounded && !jumpPressed)
            {
                // 地面静息：不积累重力（与金轨迹 ct 1..360 恒 0 一致）。
                nz = 0f;
            }
            else
            {
                // 普通跳（bit0 且非轻功）逐帧恒用首段冲量 5.5——实测 ct 551..555 每帧
                // 反推出的冲量都是 5.5000（而非 5.5/4.5/3.5 递减）；轻功跳（bit3）
                // 才按 jumpCount 取 5.5/4.5/3.5。这是普通跳与轻功跳的**唯一语义差异**。
                float impulse;
                if (jumpPressed)
                {
                    jumpCount++;
                    impulse = isQinggongJump
                        ? (jumpCount <= MaxQinggongJumpCount
                            ? SimulationOptions.DefaultJumpImpulses[jumpCount - 1]
                            : 0f)
                        : SimulationOptions.DefaultJumpImpulses[0];
                }
                else
                {
                    // 非按压帧重置计数：计数只统计"连续按压"，不跨帧累积（实测 ct 421
                    // 重新给出 5.5 而非越界的 0，即段首重新计数）。
                    jumpCount = 0;
                    impulse = 0f;
                }

                var (steppedZ, steppedVz) = MovementFormula.StepVertical(posZ, vz, impulse, Dt);
                nz = steppedZ;
                vz = steppedVz;

                // 地面约束（等价于 GroundHeightSampler 恒返回 0）：贴地且下落则吸附并清零。
                if (nz <= 0f && vz <= 0f)
                {
                    nz = 0f;
                    vz = 0f;
                    jumpCount = 0;
                    grounded = true;
                }
                else
                {
                    grounded = false;
                }
            }

            posX = nx;
            posY = ny;
            posZ = nz;

            // 间隙丢帧：本帧输入在网络上丢失——clientTick 与位置都已推进，
            // 但不产生输入行（该 clientTick 在轨迹中缺席）。
            if (gapClientTicks.Contains(clientTick)) continue;

            if (offsetJumps.TryGetValue(clientTick, out var newOffset))
            {
                offset = newOffset;
            }

            lines.Add(new InputLine
            {
                ServerTick = (int)(clientTick - offset),
                Input = new InputPacket
                {
                    ClientTick = clientTick,
                    MoveX = moveX,
                    MoveY = moveY,
                    InputBits = inputBits,
                    MaxSpeed = maxSpeed,
                    PredictedEndX = posX,
                    PredictedEndY = posY,
                    PredictedEndZ = posZ,
                    CharacterId = EntityId,
                },
                ReportedEndX = posX,
                ReportedEndY = posY,
                ReportedEndZ = posZ,
            });
        }

        return (lines, Header());
    }

    // ── 运动脚本（反推自金轨迹；19 个阶段）──────────────────────────────────

    /// <summary>每帧固定步长（秒），与 <see cref="MovementFormula"/> 消费方一致。</summary>
    private const float Dt = 1f / 60f;

    /// <summary>普通跳跃位（InputBits bit0）。</summary>
    private const uint InputBitJump = 1u << 0;

    /// <summary>轻功跳跃位（InputBits bit3）。</summary>
    private const uint InputBitQinggong = 1u << 3;

    /// <summary>轻功最大段数（与 <see cref="SimulationOptions.MaxQinggongJumpCount"/> 默认一致）。</summary>
    private const int MaxQinggongJumpCount = 3;

    /// <summary>冲刺速度（第 3 阶段使用）。</summary>
    private const float SprintSpeed = 12f;

    /// <summary>
    /// 单实体金轨迹的运动脚本：按 <paramref name="frame"/> 给出本帧的
    /// <c>(moveX, moveY, inputBits, maxSpeed)</c>。
    /// <para><b>反推来源</b>：对 <c>Traces/deterministic_trace_600.txt</c> 逐字段交叉验证
    /// （13 字段 × 597 行全部命中，生成内容与入库文件逐字节相等）。</para>
    /// <para><b>阶段表</b>（闭区间，<c>frame</c> 即 clientTick）：</para>
    /// <list type="table">
    ///   <item><term>1..60</term><description>+X 直行</description></item>
    ///   <item><term>61..120</term><description>斜向 45°（moveX=moveY=1，经归一化）</description></item>
    ///   <item><term>121..180</term><description>+X 冲刺（maxSpeed=12）</description></item>
    ///   <item><term>181..240</term><description>−X 后退</description></item>
    ///   <item><term>241..360</term><description>原地静止</description></item>
    ///   <item><term>361..363</term><description>轻功三段跳（bits=9 → 冲量 5.5/4.5/3.5）</description></item>
    ///   <item><term>364..420</term><description>自由落体（上升段）</description></item>
    ///   <item><term>421..424</term><description>轻功三段跳（bits=9，段内重新计数）</description></item>
    ///   <item><term>425..540</term><description>自由运动</description></item>
    ///   <item><term>541..550</term><description>+X 微移</description></item>
    ///   <item><term>551..555</term><description>普通跳（bits=1 → 每帧恒冲量 5.5）</description></item>
    ///   <item><term>556..560</term><description>惯性上升</description></item>
    ///   <item><term>561..570</term><description>+X 微移</description></item>
    ///   <item><term>571..575</term><description>普通跳（571 为丢帧，物理仍推进）</description></item>
    ///   <item><term>576..580</term><description>惯性上升</description></item>
    ///   <item><term>581..590</term><description>+X 微移</description></item>
    ///   <item><term>591..595</term><description>普通跳（触顶终端速度 50 m/s）</description></item>
    ///   <item><term>596..600</term><description>惯性上升</description></item>
    /// </list>
    /// </summary>
    private static (float MoveX, float MoveY, uint InputBits, float MaxSpeed) MotionScript(int frame)
    {
        if (frame <= 60) return (1f, 0f, 0u, MaxSpeed);                            // +X 直行
        if (frame <= 120) return (1f, 1f, 0u, MaxSpeed);                           // 斜向 45°
        if (frame <= 180) return (1f, 0f, 0u, SprintSpeed);                        // 冲刺
        if (frame <= 240) return (-1f, 0f, 0u, MaxSpeed);                          // 后退
        if (frame <= 360) return (0f, 0f, 0u, MaxSpeed);                           // 静止
        if (frame <= 363) return (0f, 0f, InputBitJump | InputBitQinggong, MaxSpeed); // 轻功跳
        if (frame <= 420) return (0f, 0f, 0u, MaxSpeed);                           // 上升
        if (frame <= 424) return (0f, 0f, InputBitJump | InputBitQinggong, MaxSpeed); // 轻功跳
        if (frame <= 540) return (0f, 0f, 0u, MaxSpeed);                           // 自由运动
        if (frame <= 550) return (1f, 0f, 0u, MaxSpeed);                           // +X 微移
        if (frame <= 555) return (0f, 0f, InputBitJump, MaxSpeed);                 // 普通跳
        if (frame <= 560) return (0f, 0f, 0u, MaxSpeed);                           // 惯性上升
        if (frame <= 570) return (1f, 0f, 0u, MaxSpeed);                           // +X 微移
        if (frame <= 575) return (0f, 0f, InputBitJump, MaxSpeed);                 // 普通跳（571 丢帧）
        if (frame <= 580) return (0f, 0f, 0u, MaxSpeed);                           // 惯性上升
        if (frame <= 590) return (1f, 0f, 0u, MaxSpeed);                           // +X 微移
        if (frame <= 595) return (0f, 0f, InputBitJump, MaxSpeed);                 // 普通跳
        return (0f, 0f, 0u, MaxSpeed);                                             // 惯性上升
    }

    /// <summary>金轨迹文件的头部注释行（与入库文件一致）。</summary>
    public static string[] Header() => new[]
    {
        "# DeterministicReplay trace v1 — 阶段 6.2 确定性对拍金轨迹。",
        $"# FormulaVersion = {MovementFormula.FormulaVersion} — 公式版本变更必须重新生成本轨迹（.freebuff/Verify --trace）并同步测试指纹；版本不匹配即 CI 失败。",
        "# 单实体 1，出生 (0,0,0)，客户端 60Hz 连续预测；间隙（20/220/570 帧）与批量（80/360/420/585 帧）模拟真实网络到达。",        "# 服务器 tick 从 0 开始（与 ZoneShardGrain._tickCount 一致）；输入按 serverTick 分组、组内按 clientTick 升序。",
        "# 格式：serverTick|clientTick|moveX|moveY|inputBits|maxSpeed|predEndX|predEndY|predEndZ|reportedEndX|reportedEndY|reportedEndZ|lookYaw",
        "# float 用 R 往返格式（消费方须按 IEEE-754 正确舍入解析）；inputBits 为十进制。",
    };

    /// <summary>把输入行拼成完整文件内容（自动附带 <see cref="Header"/>）。</summary>
    public static string BuildFileContent(IReadOnlyList<InputLine> lines)
        => BuildFileContent(lines, Header());

    /// <summary>把输入行与头注释拼成完整文件内容（UTF-8 文本，<c>LF</c> 换行）。</summary>
    public static string BuildFileContent(IReadOnlyList<InputLine> lines, string[] header)
    {
        if (lines is null) throw new ArgumentNullException(nameof(lines));

        var sb = new System.Text.StringBuilder();
        if (header is not null)
        {
            foreach (var h in header) sb.Append(h).Append('\n');
        }

        foreach (var l in lines)
        {
            sb.Append(l.ServerTick.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(l.Input.ClientTick.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(Fmt(l.Input.MoveX)).Append('|')
              .Append(Fmt(l.Input.MoveY)).Append('|')
              .Append(l.Input.InputBits.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(Fmt(l.Input.MaxSpeed)).Append('|')
              .Append(Fmt(l.Input.PredictedEndX)).Append('|')
              .Append(Fmt(l.Input.PredictedEndY)).Append('|')
              .Append(Fmt(l.Input.PredictedEndZ)).Append('|')
              .Append(Fmt(l.ReportedEndX)).Append('|')
              .Append(Fmt(l.ReportedEndY)).Append('|')
              .Append(Fmt(l.ReportedEndZ)).Append('|')
              .Append(Fmt(0f))          // lookYaw：本场景不使用
              .Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>float 用 "R" 往返格式输出。</summary>
    private static string Fmt(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}
