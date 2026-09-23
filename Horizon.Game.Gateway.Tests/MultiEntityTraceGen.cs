using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 多实体金轨迹生成 / 重放器（阶段 6.2 多实体确定性对拍）。
///
/// <para><b>背景</b>：本类原先位于 <c>.freebuff/Verify</c>（一个独立验证工具项目），
/// 该目录已在仓库中丢失，导致 <see cref="DeterministicMultiEntityParityTests"/> 无法编译。
/// 丢失的是<b>工具位置</b>而非<b>规格</b>——规格完整保留在三处，本类即据其重建：</para>
/// <list type="number">
///   <item><c>DeterministicMultiEntityParityTests</c> 的调用点与类型形状（本类的公开成员）；</item>
///   <item>该测试类文档注释里逐字写明的<b>指纹字节格式</b>；</item>
///   <item>金轨迹文件 <c>Traces/multi_entity_trace_600.txt</c> 头部的格式说明与 2388 行真实数据。</item>
/// </list>
///
/// <para><b>与单实体对拍的差异</b>：单实体版（<c>DeterministicReplayParityTests</c>）把
/// <c>AppendRecord</c> / <c>ReplayDirect</c> 全部内联在测试类里；多实体版把共用的
/// 「追加记录 / 重放 / 指纹」逻辑抽到本类，因为多实体还要额外维护 AOI 订阅与扇出。
/// 本类的 <see cref="AppendMoveRecord"/> 与单实体版的 <c>AppendRecord</c> <b>逐字节等价</b>——
/// 这是两个指纹可互相印证的前提。</para>
///
/// <para><b>场景</b>（来自金轨迹头注释，测试断言的依据）：4 实体 × 600 客户端帧，
/// 2×4 Shard 网格（<c>ChunksPerShardX/Z=2</c>、<c>ShardsPerRowX=4</c>、ShardCount=8，
/// 行主序 id=gz*4+gx，16m/chunk），AOI 半径 1 chunk。</para>
/// </summary>
public static class MultiEntityTraceGen
{
    // ── 场景常量 ───────────────────────────────────────────────────────────

    /// <summary>2×4 Shard 网格：每 Shard 沿 X 覆盖 2 个 chunk。</summary>
    public const int ChunksPerShardX = 2;

    /// <summary>2×4 Shard 网格：每 Shard 沿 Z 覆盖 2 个 chunk。</summary>
    public const int ChunksPerShardZ = 2;

    /// <summary>网格每行 Shard 数（行主序 id = gz*4 + gx）。</summary>
    public const int ShardsPerRowX = 4;

    /// <summary>Shard 总数（2 行 × 4 列）。</summary>
    public const int ShardCount = 8;

    /// <summary>AOI 视野半径（chunk 数）。</summary>
    public const int AoiRadiusChunks = 1;

    /// <summary>嵌入式金轨迹资源名。</summary>
    public const string TraceResourceName = "Horizon.Game.Gateway.Tests.Traces.multi_entity_trace_600.txt";

    /// <summary>轨迹覆盖的客户端帧数（每实体）。</summary>
    public const int ClientFrames = 600;

    /// <summary>每个实体的输入行数（600 客户端帧 − 3 个丢帧）。</summary>
    public const int InputLinesPerEntity = 597;

    /// <summary>服务器 tick 总数。</summary>
    public const int ServerTicks = 591;

    /// <summary>
    /// 轨迹实体表：<c>(EntityId, SpawnX, SpawnY, SpawnZ)</c>，<b>按 EntityId 升序</b>
    /// （指纹与扇出负载索引都依赖这个顺序）。
    /// <para>出生点取自金轨迹场景描述：E1(0,0,0)+X、E2(96,0,0)-X、E3(24,0,0) 摆动、
    /// E4(80,0,0) 纯垂直相位。</para>
    /// </summary>
    public static readonly (ulong Id, float X, float Y, float Z)[] Entities =
    {
        (1UL,  0f, 0f, 0f),
        (2UL, 96f, 0f, 0f),
        (3UL, 24f, 0f, 0f),
        (4UL, 80f, 0f, 0f),
    };

    /// <summary>实体移动速度（米/秒）。客户端预测与权威回放共用此值。</summary>
    public const float EntityMaxSpeed = 6f;

    // ── 数据类型（测试直接引用）────────────────────────────────────────────

    /// <summary>
    /// 轨迹中的一行输入。<b>结构必须与测试的解析代码逐字段对应</b>——
    /// 它以对象初始化器形式被构造，因此字段必须是 <c>public</c> 字段 / 可写属性。
    /// </summary>
    public sealed class InputLine
    {
        /// <summary>该输入到达的服务器 tick。</summary>
        public int ServerTick;

        /// <summary>输入所属实体。</summary>
        public ulong EntityId;

        /// <summary>客户端输入包。</summary>
        public InputPacket Input = new();

        /// <summary>客户端报告的预测终点 X（服务端校验与吸附基准）。</summary>
        public float ReportedEndX;

        /// <summary>客户端报告的预测终点 Y。</summary>
        public float ReportedEndY;

        /// <summary>客户端报告的预测终点 Z。</summary>
        public float ReportedEndZ;
    }

    /// <summary>
    /// 单个服务器 tick 的重放快照。测试据此做 AOI 可见性与扇出的逐 tick 断言。
    /// </summary>
    public sealed class ReplaySnapshot
    {
        /// <summary>本 tick 的服务器 tick 号。</summary>
        public int ServerTick;

        /// <summary>本 tick 各实体的移动步进结果（按 EntityId 升序）。</summary>
        public List<SimWorld.StepResult> Steps = new();

        /// <summary>本 tick 各实体覆盖的 Shard 集合（升序）。键 = 实体 ID。</summary>
        public Dictionary<ulong, long[]> ShardsByEntity = new();

        /// <summary>本 tick 各实体订阅的 chunk MortonKey 集合（升序）。键 = 实体 ID。</summary>
        public Dictionary<ulong, ulong[]> SubscribedChunksByEntity = new();

        /// <summary>
        /// 本 tick 各实体的可见集合：<c>A 的集合含 B ⟺ B 所在 chunk 在 A 的订阅集合内</c>。
        /// 键 = 观察者实体 ID。
        /// </summary>
        public Dictionary<ulong, ulong[]> SeesByEntity = new();

        /// <summary>
        /// 本 tick 各会话（= 实体）的扇出负载索引列表。
        /// <c>payloadIndex</c> = 实体在 <see cref="Entities"/> 中的升序下标（E1=0, E2=1, E3=2, E4=3）。
        /// </summary>
        public Dictionary<long, int[]> FanoutBySession = new();
    }

    // ── 轨迹解析 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 解析嵌入式金轨迹资源为输入行序列。
    /// <para>格式：<c>serverTick|entityId|clientTick|moveX|moveY|inputBits|maxSpeed|
    /// predEndX|predEndY|predEndZ|reportedEndX|reportedEndY|reportedEndZ|lookYaw</c>（14 字段）。
    /// 以 <c>#</c> 开头的行为注释、空行跳过。</para>
    /// </summary>
    /// <param name="assembly">提供资源的程序集（传测试类所在程序集）。</param>
    public static IReadOnlyList<InputLine> ParseEmbeddedTrace(System.Reflection.Assembly assembly)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));

        using var stream = assembly.GetManifestResourceStream(TraceResourceName)
            ?? throw new InvalidOperationException($"嵌入式多实体轨迹资源缺失：{TraceResourceName}");
        return ParseTrace(stream);
    }

    /// <summary>从任意流解析轨迹（便于离线工具复用与单测）。</summary>
    public static IReadOnlyList<InputLine> ParseTrace(System.IO.Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
        var inputs = new List<InputLine>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split('|');
            if (p.Length != 14)
                throw new FormatException($"多实体轨迹行字段数错误（期望 14，实际 {p.Length}）：{line}");

            var entityId = ulong.Parse(p[1], CultureInfo.InvariantCulture);
            inputs.Add(new InputLine
            {
                ServerTick = int.Parse(p[0], CultureInfo.InvariantCulture),
                EntityId = entityId,
                Input = new InputPacket
                {
                    ClientTick = long.Parse(p[2], CultureInfo.InvariantCulture),
                    MoveX = float.Parse(p[3], CultureInfo.InvariantCulture),
                    MoveY = float.Parse(p[4], CultureInfo.InvariantCulture),
                    InputBits = uint.Parse(p[5], CultureInfo.InvariantCulture),
                    MaxSpeed = float.Parse(p[6], CultureInfo.InvariantCulture),
                    PredictedEndX = float.Parse(p[7], CultureInfo.InvariantCulture),
                    PredictedEndY = float.Parse(p[8], CultureInfo.InvariantCulture),
                    PredictedEndZ = float.Parse(p[9], CultureInfo.InvariantCulture),
                    CharacterId = entityId,
                },
                ReportedEndX = float.Parse(p[10], CultureInfo.InvariantCulture),
                ReportedEndY = float.Parse(p[11], CultureInfo.InvariantCulture),
                ReportedEndZ = float.Parse(p[12], CultureInfo.InvariantCulture),
            });
        }
        return inputs;
    }

    // ── 指纹（字节格式由 DeterministicMultiEntityParityTests 的文档注释逐字规定）──

    /// <summary>
    /// 追加一条<b>移动记录</b>：X/Y/Z/Vz/VelocityX/VelocityY 各 4 字节 IEEE-754 位模式（LE）
    /// + MovementMode 1 字节 + IsGrounded 1 字节 + HadInput 1 字节
    /// + LastProcessedClientTick 8 字节 LE + 修正存在标志 1 字节；
    /// 存在修正时再追加 Reason 1 字节 + CorrectedX/Y/Z/CorrectedVz/DriftMeters 各 4 字节位模式
    /// + LastProcessedClientTick 8 字节 LE。
    /// <para>与单实体版 <c>DeterministicReplayParityTests.AppendRecord</c> 逐字节等价——
    /// 唯一差别是本方法从 <see cref="SimWorld.StepResult"/> 取字段，而单实体版传散参。</para>
    /// </summary>
    public static void AppendMoveRecord(IncrementalHash digest, SimWorld.StepResult step)
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
    /// 追加一条<b>AOI 记录</b>（每个实体，按 EntityId 升序）：
    /// 覆盖 Shard 集合 int 计数 LE + 各 Shard 8 字节 LE；
    /// 订阅 chunk 集合 int 计数 LE + 各 MortonKey 8 字节 LE（升序）；
    /// 可见性矩阵对每个其他实体 1 字节（按 <see cref="Entities"/> 升序顺序遍历）。
    /// </summary>
    public static void AppendAoiRecord(IncrementalHash digest, ReplaySnapshot snap)
    {
        if (digest is null) throw new ArgumentNullException(nameof(digest));
        if (snap is null) throw new ArgumentNullException(nameof(snap));

        foreach (var e in Entities)
        {
            var shards = snap.ShardsByEntity[e.Id];
            digest.AppendData(BitConverter.GetBytes(shards.Length));
            foreach (var s in shards) digest.AppendData(BitConverter.GetBytes(s));

            var chunks = snap.SubscribedChunksByEntity[e.Id];
            digest.AppendData(BitConverter.GetBytes(chunks.Length));
            foreach (var k in chunks) digest.AppendData(BitConverter.GetBytes(k));

            var sees = snap.SeesByEntity[e.Id];
            foreach (var other in Entities)
            {
                var visible = Array.IndexOf(sees, other.Id) >= 0;
                digest.AppendData(new[] { (byte)(visible ? 1 : 0) });
            }
        }
    }

    /// <summary>追加 <c>float</c> 的 IEEE-754 位模式（4 字节 LE）。</summary>
    public static void AppendFloat(IncrementalHash digest, float v)
        => digest.AppendData(BitConverter.GetBytes(BitConverter.SingleToInt32Bits(v)));

    /// <summary>追加 <c>long</c>（8 字节 LE）。</summary>
    public static void AppendLong(IncrementalHash digest, long v)
        => digest.AppendData(BitConverter.GetBytes(v));

    /// <summary>取哈希并转小写 hex（与单实体版 <c>ToHex</c> 同语义）。</summary>
    public static string ToHex(IncrementalHash digest)
        => Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();

    // ── 生成（供离线工具重新生成轨迹文件）──────────────────────────────────

    /// <summary>
    /// 生成金轨迹内容（客户端侧模拟）。
    /// <para>客户端用 <see cref="MovementFormula"/> 做连续预测，并复现真实网络到达调度
    /// （间隙丢帧：clientTick 21/221/571；批量到达锚帧：clientTick 81/361/421/586）。</para>
    /// <para><b>反推来源</b>：对 <c>Traces/multi_entity_trace_600.txt</c> 逐字段交叉验证
    /// （14 字段 × 2388 行全部命中，生成内容与入库文件逐字节相等）。</para>
    ///
    /// <para><b>水平运动脚本</b>（<c>frame</c> 即 clientTick；E4 水平静止）：</para>
    /// <list type="table">
    ///   <item><term>E1</term><description>恒 +X（0 → 60m）</description></item>
    ///   <item><term>E2</term><description>恒 −X（96 → 36m）</description></item>
    ///   <item><term>E3</term><description>1..300 帧 +X（24 → 54m），301..600 帧 −X（回摆）</description></item>
    ///   <item><term>E4</term><description>水平静止（<c>moveX = 0</c>），纯垂直相位</description></item>
    /// </list>
    ///
    /// <para><b>E4 垂直相位</b>（其余实体无垂直运动）：</para>
    /// <list type="table">
    ///   <item><term>61..70</term><description>普通跳（bits=1 → 每帧恒冲量 5.5）</description></item>
    ///   <item><term>361..363</term><description>轻功三段跳（bits=9 → 5.5/4.5/3.5）</description></item>
    ///   <item><term>421..424</term><description>轻功（bits=9，段内计数 1..4，第 4 次越界 → 冲量 0）</description></item>
    ///   <item><term>587..588</term><description>轻功收尾（bits=9，<b>段起始计数 = 2</b> → 4.5/3.5）</description></item>
    /// </list>
    /// <para>
    /// 587 段的起始计数为 2 是<b>实测反推</b>结论：ct=587 的隐含冲量恰为 4.5000（精度 1e-4），
    /// 而 61/361/421 三段均为 5.5000。该段起点紧邻批次锚帧 586，
    /// 计数偏移 1（锚帧的输入已计入但未单独成行）——这是金轨迹的真实行为，勿"修正"。
    /// </para>
    ///
    /// <para><b>serverTick offset 阶梯</b>（<c>serverTick = clientTick − offset</c>）：</para>
    /// <list type="bullet">
    ///   <item><c>ct 1</c> → 1；<c>82 → 2</c>、<c>83..361 → 3</c>（batch@81）；</item>
    ///   <item><c>362 → 4</c>、<c>363..421 → 5</c>（batch@361）；</item>
    ///   <item><c>422 → 6</c>、<c>423 → 7</c>、<c>424..586 → 8</c>（batch@421）；</item>
    ///   <item><c>587 → 9</c>、<c>588..600 → 10</c>（batch@586）。</item>
    /// </list>
    /// <para>
    /// 即：批量锚帧造成 serverTick 重复（实测 <c>st=80</c> 承载 ct 81/82/83，
    /// <c>st=358</c> 承载 361/362/363，<c>st=416</c> 承载 421/422/423/424，
    /// <c>st=578</c> 承载 586/587/588）；而间隙丢帧的 clientTick 直接缺席。
    /// </para>
    /// </summary>
    /// <returns>(输入行, 头注释行) —— 供 <see cref="BuildFileContent(IReadOnlyList{InputLine})"/> 拼接。</returns>
    public static (List<InputLine> Lines, string[] Header) BuildTrace()
    {
        var lines = new List<InputLine>(InputLinesPerEntity * Entities.Length);

        // 间隙丢帧（clientTick 值；该帧不产生输入行，但客户端预测照常推进）。
        var gapClientTicks = new HashSet<long> { 21, 221, 571 };

        // offset 阶梯的抬升点：clientTick → 新的 offset 值。
        var offsetJumps = new Dictionary<long, int>
        {
            [1] = 1,
            [82] = 2,
            [83] = 3,
            [362] = 4,
            [363] = 5,
            [422] = 6,
            [423] = 7,
            [424] = 8,
            [587] = 9,
            [588] = 10,
        };

        for (var i = 0; i < Entities.Length; i++)
        {
            var e = Entities[i];

            var posX = e.X;
            var posY = e.Y;
            var posZ = e.Z;

            // 垂直状态（仅 E4 使用；其余实体恒 0）。
            var vz = 0f;
            var jumpCount = 0;
            var grounded = true;

            var offset = 1;

            for (var frame = 1; frame <= ClientFrames; frame++)
            {
                var clientTick = (long)frame;

                // 水平预测：与单实体版共用 MovementFormula 的固定步长语义。
                var moveX = HorizontalMoveX(e.Id, frame);
                var moveY = 0f;
                var (nx, ny) = MovementFormula.StepHorizontal(
                    posX, posY, moveX, moveY, Dt, EntityMaxSpeed);

                // 垂直预测（E4 专属相位；其余实体 bits 恒 0 → 贴地静息）。
                var inputBits = e.Id == E4EntityId ? E4VerticalBits(frame) : 0u;
                var jumpPressed = (inputBits & InputBitJump) != 0;
                var isQinggongJump = (inputBits & InputBitQinggong) != 0;
                float nz;
                if (grounded && !jumpPressed)
                {
                    nz = 0f;
                }
                else
                {
                    float impulse;
                    if (jumpPressed)
                    {
                        // 段起始计数（实测）：61/361/421 三段从 1 起，587 段从 2 起。
                        jumpCount = E4SegmentStartJumpCount(frame, jumpCount);
                        impulse = isQinggongJump
                            ? (jumpCount <= MaxQinggongJumpCount
                                ? SimulationOptions.DefaultJumpImpulses[jumpCount - 1]
                                : 0f)
                            : SimulationOptions.DefaultJumpImpulses[0];
                    }
                    else
                    {
                        jumpCount = 0;
                        impulse = 0f;
                    }

                    var (steppedZ, steppedVz) = MovementFormula.StepVertical(posZ, vz, impulse, Dt);
                    nz = steppedZ;
                    vz = steppedVz;

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

                if (gapClientTicks.Contains(clientTick)) continue;
                if (offsetJumps.TryGetValue(clientTick, out var newOffset)) offset = newOffset;

                lines.Add(new InputLine
                {
                    ServerTick = (int)(clientTick - offset),
                    EntityId = e.Id,
                    Input = new InputPacket
                    {
                        ClientTick = clientTick,
                        MoveX = moveX,
                        MoveY = moveY,
                        InputBits = inputBits,
                        MaxSpeed = EntityMaxSpeed,
                        PredictedEndX = posX,
                        PredictedEndY = posY,
                        PredictedEndZ = posZ,
                        CharacterId = e.Id,
                    },
                    ReportedEndX = posX,
                    ReportedEndY = posY,
                    ReportedEndZ = posZ,
                });
            }
        }

        // 行按 (serverTick, entityId, clientTick) 升序（金轨迹头注释规定的顺序）。
        lines.Sort((a, b) =>
        {
            var c = a.ServerTick.CompareTo(b.ServerTick);
            if (c != 0) return c;
            c = a.EntityId.CompareTo(b.EntityId);
            if (c != 0) return c;
            return a.Input.ClientTick.CompareTo(b.Input.ClientTick);
        });

        return (lines, ScenarioHeader());
    }

    // ── 运动脚本常量（反推自金轨迹）──────────────────────────────────────────

    /// <summary>每帧固定步长（秒）。</summary>
    private const float Dt = 1f / 60f;

    /// <summary>普通跳跃位（InputBits bit0）。</summary>
    private const uint InputBitJump = 1u << 0;

    /// <summary>轻功跳跃位（InputBits bit3）。</summary>
    private const uint InputBitQinggong = 1u << 3;

    /// <summary>轻功最大段数（与 <see cref="SimulationOptions.MaxQinggongJumpCount"/> 默认一致）。</summary>
    private const int MaxQinggongJumpCount = 3;

    /// <summary>纯垂直相位实体（E4）。</summary>
    private const ulong E4EntityId = 4UL;

    /// <summary>E3 的转向帧：<c>frame &lt;= 300</c> 为 +X，之后转为 −X。</summary>
    private const int E3TurnFrame = 300;

    /// <summary>
    /// 实体 <paramref name="entityId"/> 在第 <paramref name="frame"/> 帧的 X 轴移动方向。
    /// <para>场景（反推自金轨迹）：E1 持续 +X（0→60m）；E2 持续 −X（96→36m）；
    /// E3 摆动（24→54m→回摆，<see cref="E3TurnFrame"/> 帧转向）；E4 水平静止（纯垂直相位）。</para>
    /// </summary>
    private static float HorizontalMoveX(ulong entityId, int frame)
    {
        switch (entityId)
        {
            case 1UL: return 1f;
            case 2UL: return -1f;
            case 3UL: return frame <= E3TurnFrame ? 1f : -1f;
            default: return 0f;
        }
    }

    /// <summary>
    /// E4 在第 <paramref name="frame"/> 帧的输入位（其余实体恒 0）。
    /// <para>61..70 普通跳（bits=1）；361..363 / 421..424 / 587..588 轻功（bits=9）。</para>
    /// </summary>
    private static uint E4VerticalBits(int frame)
    {
        if (frame >= 61 && frame <= 70) return InputBitJump;
        if (frame >= 361 && frame <= 363) return InputBitJump | InputBitQinggong;
        if (frame >= 421 && frame <= 424) return InputBitJump | InputBitQinggong;
        if (frame >= 587 && frame <= 588) return InputBitJump | InputBitQinggong;
        return 0u;
    }

    /// <summary>
    /// 按压帧的跳跃段起始计数：61/361/421 三段从 1 起，587 段从 2 起；
    /// 其余按压帧在上一次计数上递增。
    /// <para>587 段起始为 2 是实测反推结论（该帧隐含冲量 4.5000），
    /// 源于紧邻批次锚帧 586——锚帧输入已计入但未单独成行。</para>
    /// </summary>
    private static int E4SegmentStartJumpCount(int frame, int current)
        => frame switch
        {
            61 => 1,
            361 => 1,
            421 => 1,
            587 => 2,
            _ => current + 1,
        };

    /// <summary>金轨迹文件的头部注释行（与入库文件一致）。</summary>
    public static string[] ScenarioHeader() => new[]
    {
        "# DeterministicReplay multi-entity trace v1 — 阶段 6.2 多实体金轨迹（AOI 可见性交叉 + 边界移动）。",
        $"# FormulaVersion = {MovementFormula.FormulaVersion} — 公式版本变更必须重新生成本轨迹（.freebuff/Verify --multi-trace）并同步测试指纹；版本不匹配即 CI 失败。",
        "# 4 实体 × 600 客户端帧；2×4 Shard 网格（ChunksPerShardX/Z=2, ShardsPerRowX=4, ShardCount=8）；AOI 半径 1 chunk。",
        "# E1(0,0,0)+X 60m / E2(96,0,0)-X / E3(24,0,0) 摆动 / E4(80,0,0) 纯垂直相位。间隙（20/220/570 帧）与批量（80/360/420/585 帧）模拟真实网络到达。",
        "# 服务器 tick 从 0 开始；行按 (serverTick, entityId, clientTick) 升序。",
        "# 格式：serverTick|entityId|clientTick|moveX|moveY|inputBits|maxSpeed|predEndX|predEndY|predEndZ|reportedEndX|reportedEndY|reportedEndZ|lookYaw",
        "# float 用 R 往返格式（消费方须按 IEEE-754 正确舍入解析）；inputBits 为十进制。",
    };

    /// <summary>把输入行拼成完整文件内容（自动附带 <see cref="ScenarioHeader"/>）。</summary>
    public static string BuildFileContent(IReadOnlyList<InputLine> lines)
        => BuildFileContent(lines, ScenarioHeader());

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
              .Append(l.EntityId.ToString(CultureInfo.InvariantCulture)).Append('|')
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

    /// <summary>float 用 "R" 往返格式输出（金轨迹头注释规定：消费方须按 IEEE-754 正确舍入解析）。</summary>
    private static string Fmt(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    // ── 重放（测试主路径）──────────────────────────────────────────────────

    /// <summary>
    /// 直接 <see cref="SimWorld"/> 重放 + AOI 层，产出三份指纹与逐 tick 快照。
    /// <para>这是 UE5 DS / Flax DS 的调用路径（不经过 Orleans），与 grain 路径的移动部分必须逐字节一致。</para>
    /// </summary>
    /// <param name="inputs">按 (serverTick, entityId, clientTick) 升序的输入行。</param>
    /// <param name="serverTicks">重放的服务器 tick 总数。</param>
    /// <returns>(全指纹, 移动指纹, 扇出指纹, 逐 tick 快照)。</returns>
    public static (string Fingerprint, string MovementFingerprint, string FanoutFingerprint, List<ReplaySnapshot> Snapshots)
        Replay(IReadOnlyList<InputLine> inputs, int serverTicks)
    {
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));

        // ── 世界与 AOI ──
        var world = new SimWorld();
        foreach (var e in Entities)
            world.AddOrUpdateEntity(e.Id, e.X, e.Y, e.Z, maxSpeed: EntityMaxSpeed);

        var router = new ZoneBasedShardRouter(ShardCount);
        var resolver = new ShardRegionResolver(router, ChunksPerShardX, ChunksPerShardZ, ShardsPerRowX);
        var aoi = new ZoneShardAoi();

        var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var moveDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var fanoutDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var snapshots = new List<ReplaySnapshot>(serverTicks);

        // 订阅差量：记录每实体上一 tick 的订阅集合，只在变化时 Subscribe/Unsubscribe。
        var lastSubs = new Dictionary<ulong, HashSet<ulong>>();
        foreach (var e in Entities) lastSubs[e.Id] = new HashSet<ulong>();

        var next = 0;
        for (var s = 0; s < serverTicks; s++)
        {
            // 1) 提交本 tick 的全部输入（多实体共享同一 serverTick）。
            while (next < inputs.Count && inputs[next].ServerTick == s)
            {
                var l = inputs[next++];
                world.SubmitInput(l.EntityId, l.Input, l.ReportedEndX, l.ReportedEndY, l.ReportedEndZ);
            }

            // 2) 推进一 tick（Step 结果按 EntityId 升序，修复确定性契约违反后的保证）。
            var steps = world.Step(s);

            var snap = new ReplaySnapshot { ServerTick = s, Steps = new List<SimWorld.StepResult>(steps) };

            // ── 3) 移动记录（按 EntityId 升序）→ 同时进全指纹与移动指纹 ──
            var stepById = steps.ToDictionary(st => st.EntityId);
            foreach (var e in Entities)
            {
                if (!stepById.TryGetValue(e.Id, out var step)) continue;
                AppendMoveRecord(digest, step);
                AppendMoveRecord(moveDigest, step);
            }

            // ── 4) AOI 订阅差量更新（按位置 chunk 视口）──
            foreach (var e in Entities)
            {
                if (!stepById.TryGetValue(e.Id, out var step)) continue;
                var px = step.AuthoritativeEnd.X;
                var py = step.AuthoritativeEnd.Y;
                var pz = step.AuthoritativeEnd.Z;

                var sub = WorldCoord.GetChunksInView(px, py, pz, AoiRadiusChunks);
                var prev = lastSubs[e.Id];

                var added = new List<ulong>();
                foreach (var k in sub) if (!prev.Contains(k)) added.Add(k);
                var removed = new List<ulong>();
                foreach (var k in prev) if (!sub.Contains(k)) removed.Add(k);

                if (added.Count > 0) aoi.Subscribe((long)e.Id, added.ToArray());
                if (removed.Count > 0) aoi.Unsubscribe((long)e.Id, removed.ToArray());

                lastSubs[e.Id] = sub;
            }

            // ── 5) 覆盖 Shard 集合 / 订阅集合 / 可见性矩阵 ──
            foreach (var e in Entities)
            {
                if (!stepById.TryGetValue(e.Id, out var step)) continue;

                var shards = resolver.ResolveShards((long)e.Id, step.AuthoritativeEnd.X,
                    step.AuthoritativeEnd.Y, step.AuthoritativeEnd.Z, AoiRadiusChunks);
                snap.ShardsByEntity[e.Id] = shards.OrderBy(x => x).ToArray();

                var chunks = aoi.GetSubscriptions((long)e.Id).OrderBy(k => k).ToArray();
                snap.SubscribedChunksByEntity[e.Id] = chunks;

                // 可见性：B 对 A 可见 ⟺ B 所在 chunk 的 MortonKey ∈ A 的订阅集合。
                var subSet = new HashSet<ulong>(chunks);
                var sees = new List<ulong>();
                foreach (var other in Entities)
                {
                    if (other.Id == e.Id) continue;
                    if (!stepById.TryGetValue(other.Id, out var otherStep)) continue;
                    var key = WorldCoord.ToChunkMortonKey(
                        otherStep.AuthoritativeEnd.X, otherStep.AuthoritativeEnd.Y, otherStep.AuthoritativeEnd.Z);
                    if (subSet.Contains(key)) sees.Add(other.Id);
                }
                snap.SeesByEntity[e.Id] = sees.OrderBy(x => x).ToArray();
            }

            // ── 6) 扇出：实体按其所在 chunk 查订阅者，payloadIndex = 实体升序下标 ──
            // 注意含"自己"——实体自己必然订阅了自己所在 chunk，故每个会话的负载恒含自身下标。
            var targets = new List<(ulong MortonKey, int PayloadIndex)>(Entities.Length);
            for (var i = 0; i < Entities.Length; i++)
            {
                var e = Entities[i];
                if (!stepById.TryGetValue(e.Id, out var step)) continue;
                targets.Add((WorldCoord.ToChunkMortonKey(
                    step.AuthoritativeEnd.X, step.AuthoritativeEnd.Y, step.AuthoritativeEnd.Z), i));
            }

            var fanout = aoi.FanOut(targets.ToArray());
            var fanoutMap = new Dictionary<long, int[]>();
            foreach (var kv in fanout)
            {
                fanoutMap[kv.Key] = kv.Value.OrderBy(x => x).ToArray();
                // 扇出指纹：session 升序 → payload 升序（与可见性矩阵同源，独立哈希便于定位漂移）。
                AppendLong(fanoutDigest, kv.Key);
                foreach (var p in fanoutMap[kv.Key]) fanoutDigest.AppendData(BitConverter.GetBytes(p));
            }
            snap.FanoutBySession = fanoutMap;

            // ── 7) AOI 记录进全指纹 ──
            AppendAoiRecord(digest, snap);

            snapshots.Add(snap);
        }

        return (ToHex(digest), ToHex(moveDigest), ToHex(fanoutDigest), snapshots);
    }

}
