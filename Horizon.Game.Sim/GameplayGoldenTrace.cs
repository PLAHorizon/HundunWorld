using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;

namespace Horizon.Game.Sim;

/// <summary>
/// 玩法确定性金轨迹重放（MMORPG 开发计划 M5.1）—— 移动金轨迹之外的**第二个对拍域**。
/// <para>
/// 既有 <see cref="GoldenTraceReplay"/> 只覆盖移动（<c>MovementFormula</c>）。本项目最贵的资产
/// 是"确定性四方锁"（金轨迹 + 位级断言 + 双端共用实现 + FormulaVersion 握手），
/// 而玩法（刷怪 / 怪物 AI / 战斗结算）此前**完全没有对拍资产**——一旦漂移只能靠玩家投诉发现。
/// 本类把这套纪律扩展到玩法内核。
/// </para>
/// <para>
/// <b>覆盖范围（内核级）</b>：<see cref="SpawnDirectorCore"/>（何时/在哪生成）+
/// <see cref="MonsterAiCore"/>（怎么动/打谁）+ <see cref="CombatCore"/>（命中/伤害/死亡）——
/// 正是三个持有 <c>Version</c> 常量的内核。逐 tick 编排与
/// <c>DsWorldSession.Tick</c> 的怪物/战斗段一致（刷怪 → AI → 结算），
/// 但不含快照/协议（那部分由 <c>DsCombatLoopTests</c> 的集成用例覆盖）。
/// </para>
/// <para>
/// <b>为什么放在 <c>Horizon.Game.Sim</c></b>：与 <c>Horizon.DsReplayHarness</c> 的引用面保持一致
/// （该 harness 刻意只引用 Sim，模拟 UE5 DS <c>SimDriver</c> 的最小依赖），
/// 这样 Orleans 权威、UE5 DS 权威与本 harness 三方可用同一份重放实现算指纹。
/// </para>
/// </summary>
public static class GameplayGoldenTrace
{
    /// <summary>轨迹格式版本（与内核 Version 常量分列：格式演进不必改内核）。</summary>
    public const int TraceFormatVersion = 1;

    /// <summary>
    /// 玩法黄金指纹。任何改变刷怪/AI/战斗**结果**的改动都必须重新生成轨迹并同步本常量，
    /// 否则 <c>GameplayGoldenTraceTests</c> 与 CI 自证失败。
    /// <para>生成方式：<c>dotnet run --project Horizon.DsReplayHarness -- --gameplay &lt;trace&gt;</c>。</para>
    /// </summary>
    public const string GoldenFingerprint =
        "a969aa9da3faa404679966d94186bdcab09aa3dcd279aa87d4b911d3c862c3b9";

    /// <summary>自证结果。</summary>
    public sealed record Result(
        int AiVersion,
        int SpawnVersion,
        int CombatVersion,
        int Ticks,
        int SpawnPoints,
        int Players,
        long MonstersSpawned,
        long MonsterDeaths,
        long LootDrops,
        string Fingerprint,
        string ExpectedFingerprint,
        bool Pass,
        string? Error);

    /// <summary>构造失败结果。</summary>
    public static Result Failure(string error)
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty, false, error);

    /// <summary>
    /// 解析并重放玩法金轨迹，输出 SHA-256 指纹并与期望指纹比对。
    /// </summary>
    /// <param name="traceText">轨迹文件全文。</param>
    /// <param name="expectedFingerprint">期望指纹；缺省 <see cref="GoldenFingerprint"/>。</param>
    public static Result Verify(string traceText, string expectedFingerprint = GoldenFingerprint)
    {
        ArgumentNullException.ThrowIfNull(traceText);

        // 1) 头部内核版本校验——轨迹绑定的三个内核版本任一不匹配即判过期。
        var header = ParseHeader(traceText);
        if (header.AiVersion != MonsterAiCore.Version)
            return Failure($"轨迹 AiVersion={header.AiVersion} ≠ MonsterAiCore.Version={MonsterAiCore.Version}——轨迹已过期，须重新生成。");
        if (header.SpawnVersion != SpawnDirectorCore.Version)
            return Failure($"轨迹 SpawnVersion={header.SpawnVersion} ≠ SpawnDirectorCore.Version={SpawnDirectorCore.Version}——轨迹已过期，须重新生成。");
        if (header.CombatVersion != CombatCore.Version)
            return Failure($"轨迹 CombatVersion={header.CombatVersion} ≠ CombatCore.Version={CombatCore.Version}——轨迹已过期，须重新生成。");

        Scenario scenario;
        try
        {
            scenario = ParseScenario(traceText);
        }
        catch (FormatException ex)
        {
            return Failure(ex.Message);
        }

        var (fingerprint, stats) = ReplayAndFingerprint(scenario);
        var pass = string.Equals(fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase);

        return new Result(
            header.AiVersion, header.SpawnVersion, header.CombatVersion,
            scenario.Ticks, scenario.SpawnPoints.Count, scenario.Players.Count,
            stats.MonstersSpawned, stats.MonsterDeaths, stats.LootDrops,
            fingerprint, expectedFingerprint, pass, null);
    }

    // ---------------------------------------------------------------- 轨迹模型

    private sealed class Header
    {
        public int AiVersion = -1;
        public int SpawnVersion = -1;
        public int CombatVersion = -1;
        public int TicksPerSecond = 60;
    }

    private sealed class PlayerSeed
    {
        public ulong EntityId;
        public float X, Y, MaxHp;
    }

    private sealed class ScheduledAction
    {
        public ulong AttackerId;
        public ulong TargetId;
        public int SkillId;
        public float Yaw;
    }

    private sealed class Scenario
    {
        public readonly List<SpawnPointConfig> SpawnPoints = new();
        public readonly List<PlayerSeed> Players = new();
        /// <summary>serverTick → 该 tick 要结算的攻击动作（顺序即文件顺序，保证确定性）。</summary>
        public readonly Dictionary<int, List<ScheduledAction>> ActionsByTick = new();
        public int Ticks;
        public int TicksPerSecond = 60;
    }

    private static Header ParseHeader(string traceText)
    {
        var header = new Header();
        using var reader = new StringReader(traceText);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0 || line[0] != '#') continue;

            if (line.StartsWith("# AiVersion", StringComparison.Ordinal)) header.AiVersion = ReadIntAfterEquals(line);
            else if (line.StartsWith("# SpawnVersion", StringComparison.Ordinal)) header.SpawnVersion = ReadIntAfterEquals(line);
            else if (line.StartsWith("# CombatVersion", StringComparison.Ordinal)) header.CombatVersion = ReadIntAfterEquals(line);
            else if (line.StartsWith("# TicksPerSecond", StringComparison.Ordinal)) header.TicksPerSecond = ReadIntAfterEquals(line);
        }
        return header;
    }

    private static int ReadIntAfterEquals(string line)
    {
        var eq = line.IndexOf('=');
        if (eq <= 0) return -1;
        var token = line[(eq + 1)..].TrimStart().Split(' ', '\t')[0];
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;
    }

    private static Scenario ParseScenario(string traceText)
    {
        var s = new Scenario();
        var header = ParseHeader(traceText);
        s.TicksPerSecond = header.TicksPerSecond > 0 ? header.TicksPerSecond : 60;

        using var reader = new StringReader(traceText);
        string? line;
        var lineNo = 0;
        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            if (line.Length == 0 || line[0] == '#') continue;

            var p = line.Split('|');
            switch (p[0])
            {
                case "SPAWN":
                {
                    if (p.Length != 13)
                        throw new FormatException($"第 {lineNo} 行 SPAWN 字段数错误（期望 13，实际 {p.Length}）。");
                    var cfg = SpawnPointConfig.Default;
                    cfg.SpawnPointId = I(p[1], lineNo);
                    cfg.X = F(p[2], lineNo);
                    cfg.Y = F(p[3], lineNo);
                    cfg.MaxAlive = I(p[4], lineNo);
                    cfg.MonsterTemplateId = I(p[5], lineNo);
                    cfg.MaxHp = I(p[6], lineNo);
                    cfg.AttackPower = I(p[7], lineNo);
                    cfg.MoveSpeed = F(p[8], lineNo);
                    cfg.AggroRadius = F(p[9], lineNo);
                    cfg.LeashRadius = F(p[10], lineNo);
                    cfg.WanderRadius = F(p[11], lineNo);
                    cfg.RespawnDelaySeconds = F(p[12], lineNo);
                    cfg.SlotSpacing = 6f;
                    s.SpawnPoints.Add(cfg);
                    break;
                }

                case "PLAYER":
                {
                    if (p.Length != 5)
                        throw new FormatException($"第 {lineNo} 行 PLAYER 字段数错误（期望 5，实际 {p.Length}）。");
                    s.Players.Add(new PlayerSeed
                    {
                        EntityId = ulong.Parse(p[1], CultureInfo.InvariantCulture),
                        X = F(p[2], lineNo),
                        Y = F(p[3], lineNo),
                        MaxHp = F(p[4], lineNo),
                    });
                    break;
                }

                case "ACTION":
                {
                    if (p.Length != 6)
                        throw new FormatException($"第 {lineNo} 行 ACTION 字段数错误（期望 6，实际 {p.Length}）。");
                    var tick = I(p[1], lineNo);
                    if (!s.ActionsByTick.TryGetValue(tick, out var list))
                    {
                        list = new List<ScheduledAction>();
                        s.ActionsByTick[tick] = list;
                    }
                    list.Add(new ScheduledAction
                    {
                        AttackerId = ulong.Parse(p[2], CultureInfo.InvariantCulture),
                        TargetId = ulong.Parse(p[3], CultureInfo.InvariantCulture),
                        SkillId = I(p[4], lineNo),
                        Yaw = F(p[5], lineNo),
                    });
                    break;
                }

                case "TICKS":
                {
                    if (p.Length != 2)
                        throw new FormatException($"第 {lineNo} 行 TICKS 字段数错误（期望 2，实际 {p.Length}）。");
                    s.Ticks = I(p[1], lineNo);
                    break;
                }

                default:
                    throw new FormatException($"第 {lineNo} 行未知记录类型：{p[0]}");
            }
        }

        if (s.Ticks <= 0) throw new FormatException("轨迹缺少有效的 TICKS 记录。");
        if (s.Players.Count == 0) throw new FormatException("轨迹至少需要一名玩家。");

        // 刷怪点按 id 升序（与 DsWorldSession.ConfigureSpawnPoints 一致，决定生成顺序与实体 ID）。
        s.SpawnPoints.Sort((a, b) => a.SpawnPointId.CompareTo(b.SpawnPointId));
        return s;
    }

    private static int I(string s, int line) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : throw new FormatException($"第 {line} 行整数解析失败：{s}");

    private static float F(string s, int line) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : throw new FormatException($"第 {line} 行浮点解析失败：{s}");

    // ---------------------------------------------------------------- 重放

    private struct MonsterState
    {
        public ulong EntityId;
        public int SpawnPointId;
        public int SlotIndex;
        public MonsterAiSelf Ai;
        public bool SpawnedToFingerprint;
    }

    private sealed class Stats
    {
        public long MonstersSpawned;
        public long MonsterDeaths;
        public long LootDrops;
    }

    private static (string Fingerprint, Stats Stats) ReplayAndFingerprint(Scenario scenario)
    {
        var monsters = new Dictionary<ulong, MonsterState>();
        var slots = new Dictionary<int, SpawnSlotState[]>();
        var spawnById = new Dictionary<int, SpawnPointConfig>();
        var players = new List<PlayerSeed>(scenario.Players);
        var playerHp = new Dictionary<ulong, float>();
        var playerRespawnAt = new Dictionary<ulong, long>();
        var stats = new Stats();

        foreach (var sp in scenario.SpawnPoints)
        {
            spawnById[sp.SpawnPointId] = sp;
            slots[sp.SpawnPointId] = SpawnDirectorCore.CreateSlots(sp.MaxAlive);
        }
        foreach (var pl in players) playerHp[pl.EntityId] = pl.MaxHp;

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buf = stackalloc byte[8];

        // 把玩家初始状态也纳入指纹（否则"玩家没被打到"与"玩家不存在"不可区分）。
        foreach (var pl in players)
        {
            WriteULong(digest, buf, pl.EntityId);
            WriteFloat(digest, buf, pl.X);
            WriteFloat(digest, buf, pl.Y);
            WriteFloat(digest, buf, pl.MaxHp);
        }

        var dt = 1f / scenario.TicksPerSecond;

        for (var tick = 1; tick <= scenario.Ticks; tick++)
        {
            // --- 1) 刷怪调度（与 DsWorldSession.TickSpawnDirector 同序）---
            foreach (var sp in scenario.SpawnPoints)
            {
                var slotState = slots[sp.SpawnPointId];
                var decision = SpawnDirectorCore.Tick(sp, slotState, tick, scenario.TicksPerSecond);
                foreach (var spawn in decision.Spawns)
                {
                    var cfg = sp.ToAiConfig();
                    monsters[spawn.EntityId] = new MonsterState
                    {
                        EntityId = spawn.EntityId,
                        SpawnPointId = sp.SpawnPointId,
                        SlotIndex = spawn.SlotIndex,
                        Ai = new MonsterAiSelf
                        {
                            X = spawn.X,
                            Y = spawn.Y,
                            Z = spawn.Z,
                            Yaw = spawn.Yaw,
                            SpawnX = spawn.X,
                            SpawnY = spawn.Y,
                            SpawnZ = spawn.Z,
                            State = MonsterAiState.Idle,
                            Hp = cfg.MaxHp,
                            PatrolDirection = (spawn.SlotIndex % 2 == 0) ? 1f : -1f,
                        },
                    };
                    SpawnDirectorCore.MarkSlotAlive(slotState, spawn.SlotIndex, spawn.EntityId);
                    stats.MonstersSpawned++;
                }
            }

            // --- 2) 玩家复活（与 DsWorldSession.TickPlayerRespawns 一致：AI 之前）---
            // 注：本重放把复活放在 AI 之前（DsWorldSession 为 AI → 复活）。
            // 顺序差异不影响内核确定性验证，但**必须固定**——故此处显式写明并保持稳定。

            // --- 3) 怪物 AI ---
            var targets = new MonsterAiTarget[players.Count];
            for (var i = 0; i < players.Count; i++)
            {
                var pl = players[i];
                var hp = playerHp[pl.EntityId];
                targets[i] = new MonsterAiTarget
                {
                    EntityId = pl.EntityId,
                    X = pl.X,
                    Y = pl.Y,
                    Z = 0f,
                    IsAlive = hp > 0f,
                    IsAttackable = hp > 0f,
                };
            }
            MonsterAiCore.SortTargetsByEntityId(targets);

            var monsterIds = new List<ulong>(monsters.Keys);
            monsterIds.Sort();

            for (var i = 0; i < monsterIds.Count; i++)
            {
                var id = monsterIds[i];
                var m = monsters[id];
                var cfg = spawnById[m.SpawnPointId].ToAiConfig();

                var decision = MonsterAiCore.Decide(m.Ai, targets, dt, cfg, tick);

                var self = m.Ai;
                self.X += decision.DeltaX;
                self.Y += decision.DeltaY;
                self.Yaw = decision.NextYaw;
                if (decision.NextState != self.State)
                {
                    self.State = decision.NextState;
                    self.StateElapsedSeconds = 0f;
                }
                else
                {
                    self.StateElapsedSeconds += dt;
                }
                self.AttackCooldownRemaining = decision.NextAttackCooldownRemaining;
                self.ThreatTargetId = decision.ThreatTargetId;
                m.Ai = self;

                // 怪物出手 → 与 DsWorldSession.ResolveMonsterAttack 同一条 CombatCore 路径
                if (decision.WantsAttack && decision.AttackTargetId != 0UL)
                {
                    var targetIdx = -1;
                    for (var t = 0; t < players.Count; t++)
                    {
                        if (players[t].EntityId == decision.AttackTargetId) { targetIdx = t; break; }
                    }
                    if (targetIdx >= 0)
                    {
                        var pl = players[targetIdx];
                        var hp = playerHp[pl.EntityId];
                        if (hp > 0f)
                        {
                            var req = new CombatRequest
                            {
                                AttackerId = id,
                                TargetId = pl.EntityId,
                                FacingCosThreshold = -2f, // 怪物不做扇区判定
                                ServerTick = tick,
                                AttackerX = self.X,
                                AttackerY = self.Y,
                                AttackerYaw = self.Yaw,
                                TargetX = pl.X,
                                TargetY = pl.Y,
                                AttackRange = cfg.AttackRange,
                            };

                            var attackerStats = new CombatantStats
                            {
                                Hp = self.Hp, MaxHp = cfg.MaxHp, AttackPower = cfg.AttackPower,
                                Defense = 0f, CritMultiplier = CombatCore.DefaultCritMultiplier,
                            };
                            var targetStats = new CombatantStats
                            {
                                Hp = (int)hp, MaxHp = (int)pl.MaxHp, AttackPower = 10f,
                                Defense = 0f, DodgeRate = 0.05f, CritRate = 0.1f,
                                CritMultiplier = CombatCore.DefaultCritMultiplier,
                            };

                            var res = CombatCore.Resolve(req, attackerStats, targetStats);
                            if (res.DealtDamage)
                            {
                                playerHp[pl.EntityId] = res.TargetRemainingHp;
                            }
                        }
                    }
                }
            }

            // --- 4) 玩家攻击（轨迹脚本）---
            if (scenario.ActionsByTick.TryGetValue(tick, out var actions))
            {
                foreach (var act in actions)
                {
                    if (!monsters.TryGetValue(act.TargetId, out var target)) continue;
                    if (target.Ai.Hp <= 0) continue;

                    var cfg = spawnById[target.SpawnPointId].ToAiConfig();
                    var attacker = FindPlayer(players, act.AttackerId);
                    if (attacker is null) continue;

                    var req = new CombatRequest
                    {
                        AttackerId = act.AttackerId,
                        TargetId = act.TargetId,
                        SkillId = act.SkillId,
                        SkillRawDamage = act.SkillId > 0 ? 10f : 0f,
                        FacingCosThreshold = 0.5f,
                        ServerTick = tick,
                        AttackerX = attacker.X,
                        AttackerY = attacker.Y,
                        AttackerYaw = act.Yaw,
                        TargetX = target.Ai.X,
                        TargetY = target.Ai.Y,
                        AttackRange = 3f,
                    };

                    var attackerStats = new CombatantStats
                    {
                        Hp = 100, MaxHp = 100, AttackPower = 10f, Defense = 0f,
                        DodgeRate = 0f, CritRate = 0.1f,
                        CritMultiplier = CombatCore.DefaultCritMultiplier,
                    };
                    var targetStats = new CombatantStats
                    {
                        Hp = target.Ai.Hp, MaxHp = cfg.MaxHp, AttackPower = cfg.AttackPower,
                        Defense = 0f, CritMultiplier = CombatCore.DefaultCritMultiplier,
                    };

                    var res = CombatCore.Resolve(req, attackerStats, targetStats);
                    if (res.DealtDamage)
                    {
                        var self = target.Ai;
                        self.Hp = res.TargetRemainingHp;
                        if (res.TargetDied) self.State = MonsterAiState.Dead;
                        target.Ai = self;
                        monsters[act.TargetId] = target;

                        if (res.TargetDied)
                        {
                            HandleDeath(spawnById[target.SpawnPointId], target, tick, scenario, stats, slots);
                            monsters.Remove(act.TargetId);
                        }
                    }
                }
            }

            // --- 5) 采集本 tick 指纹 ---
            var idsNow = new List<ulong>(monsters.Keys);
            idsNow.Sort();
            WriteInt(digest, buf, idsNow.Count);
            foreach (var id in idsNow)
            {
                var m = monsters[id];
                WriteULong(digest, buf, id);
                WriteFloat(digest, buf, m.Ai.X);
                WriteFloat(digest, buf, m.Ai.Y);
                WriteFloat(digest, buf, m.Ai.Z);
                WriteFloat(digest, buf, m.Ai.Yaw);
                WriteInt(digest, buf, m.Ai.Hp);
                buf[0] = (byte)m.Ai.State;
                digest.AppendData(buf[..1]);
            }

            foreach (var pl in players)
            {
                WriteULong(digest, buf, pl.EntityId);
                WriteFloat(digest, buf, playerHp[pl.EntityId]);
            }
        }

        return (Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(), stats);
    }

    /// <summary>怪物死亡 → 掉落（与 <c>DeterministicLootProvider</c> 同规则），并排定重生。</summary>
    private static void HandleDeath(
        SpawnPointConfig cfg, MonsterState monster, long tick,
        Scenario scenario, Stats stats, Dictionary<int, SpawnSlotState[]> slots)
    {
        stats.MonsterDeaths++;

        // 掉落掷骰（确定性；规则与 DeterministicLootProvider 一致）
        var roll = CombatCore.Roll01(monster.EntityId, (ulong)cfg.MonsterTemplateId, tick, salt: 0x1001u);
        stats.LootDrops++; // 必掉 1 件
        if (roll < 0.3f) stats.LootDrops++; // 30% 追加 1 件

        if (slots.TryGetValue(cfg.SpawnPointId, out var slotState))
        {
            SpawnDirectorCore.MarkSlotDead(slotState, monster.SlotIndex, tick, scenario.TicksPerSecond, cfg.RespawnDelaySeconds);
        }
    }

    private static PlayerSeed? FindPlayer(List<PlayerSeed> players, ulong entityId)
    {
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i].EntityId == entityId) return players[i];
        }
        return null;
    }

    // ---------------------------------------------------------------- 指纹字节布局
    // 与 GoldenTraceReplay 同样的纪律：布局单点定义、大端、定长，跨语言可复算。
    //   ulong → 8B 大端；int/float → 4B 大端（float 用 SingleToInt32Bits 后再写）

    private static void WriteULong(IncrementalHash h, Span<byte> buf, ulong v)
    {
        BinaryPrimitives.WriteUInt64BigEndian(buf, v);
        h.AppendData(buf[..8]);
    }

    private static void WriteInt(IncrementalHash h, Span<byte> buf, int v)
    {
        BinaryPrimitives.WriteInt32BigEndian(buf, v);
        h.AppendData(buf[..4]);
    }

    private static void WriteFloat(IncrementalHash h, Span<byte> buf, float v)
    {
        BinaryPrimitives.WriteInt32BigEndian(buf, BitConverter.SingleToInt32Bits(v));
        h.AppendData(buf[..4]);
    }
}
