using System;
using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// NPC 施法（<c>Casts</c> + 技能挂载）：代码全通、由数据开关。
/// <para>
/// 挂载来自 NPC 内容（024 表；未执行时为空 ⇒ 行为逐字不变）。运行期复用**同一个**施法内核
/// <see cref="Horizon.Game.Sim.Monsters.MonsterCastingCore"/> 与同一条伤害结算口 <c>ResolveMonsterCast</c>。
/// </para>
/// <para>
/// 两次独立运行做正反对照：同一个挂载、同一个 10 米起手距离——
/// 勾了 <c>Casts</c> 的在**近战距离之外**就伤人；没勾的必须走到近战范围才掉血。
/// 放在同一场里会让"谁打的"分不清。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcCastingTests
{
    private readonly ITestOutputHelper _log;

    public NpcCastingTests(ITestOutputHelper log) => _log = log;

    private const int CasterNpcId = 500010;
    private const int MeleeOnlyNpcId = 500011;
    private const int CastSkillId = 1001;

    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 10,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 100,
    };

    private static NpcContentSnapshot Snapshot(
        int npcId, bool casts, bool withMounts = true, int chanceBps = 10000,
        int castMs = 1000, int schoolMask = 0, float spawnX = 10f, int damageScaleBps = 10000,
        byte targetSelect = 0, int targetCount = 1, float aoeRadius = 0f,
        float minRange = 0f, float maxRange = 20f, byte slotType = 0, int emergencyHpPercent = 30,
        byte interruptible = 1, byte allowMovement = 0)
    {
        var snapshot = new NpcContentSnapshot { ContentRevision = 1 };
        snapshot.Templates.Add(new NpcTemplateSnapshot
        {
            NpcId = npcId, Name = casts ? "术士" : "刀客", Level = 1,
            Reaction = (byte)NpcStance.Hostile, AiTier = (byte)NpcAiTier.Elite,
            AiBitsForceOn = casts ? (uint)NpcAiBits.Casts : 0u,
            AiBitsForceOff = casts ? 0u : (uint)NpcAiBits.Casts,
        });
        snapshot.SpawnPoints.Add(new NpcSpawnSnapshotDto
        {
            SpawnPointId = 1, NpcId = npcId, X = spawnX, Y = 0f, Z = 0f, MaxAlive = 1,
        });

        if (withMounts)
        {
            snapshot.SkillMounts.Add(new NpcSkillMountDto
            {
                NpcId = npcId, SkillId = CastSkillId, SlotSeq = 0, SlotType = slotType,
                ChanceBps = chanceBps, CastMs = castMs, CooldownMs = 5000,
                MinRange = minRange, MaxRange = maxRange, TargetSelect = targetSelect, TargetCount = targetCount,
                AoeRadius = aoeRadius, EmergencyHpPercent = emergencyHpPercent,
                SchoolMask = schoolMask, Interruptible = interruptible, AllowMovement = allowMovement, DamageScaleBps = damageScaleBps,
            });
        }

        return snapshot;
    }

    /// <summary>跑一场单怪对局：返回（是否出现过吟唱、第一次掉血时的距离、第一次掉血掉了多少）。</summary>
    private (bool SawCasting, float FirstHitDistance, int FirstHitDamage) Run(
        int npcId, bool casts, int chanceBps = 10000, int damageScaleBps = 10000, float minRange = 0f,
        byte slotType = 0)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(
            Snapshot(npcId, casts, chanceBps: chanceBps, damageScaleBps: damageScaleBps,
                minRange: minRange, slotType: slotType));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        var world = new DsWorldSession(Options());
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
        world.ApplyNpcContentSpawns();

        ulong entityId = 0UL;
        for (var tick = 1; tick <= 10 && entityId == 0UL; tick++)
        {
            var result = world.Tick();
            foreach (var delta in result.WorldSnapshot?.Deltas ?? Array.Empty<Horizon.Game.Message.Sync.EntityDelta>())
            {
                if (delta.Kind != Horizon.Game.Message.Sync.EntityDeltaKind.Spawn || delta.Identity is not { } identity) continue;
                if (identity.TemplateId == npcId) entityId = delta.EntityId;
            }
        }

        Assert.True(entityId != 0UL, "敌对 NPC 没有生成");

        var sawCasting = false;
        var firstHitDistance = -1f;
        var firstHitDamage = 0;
        for (var tick = 0; tick < 120 && firstHitDistance < 0f; tick++)
        {
            var before = world.TryGetPlayer(1001, out var p0) ? p0.Hp : 0;
            world.Tick();

            if (world.TryGetMonster(entityId, out var monster) && monster.Runtime != null
                && monster.Runtime.Casting.ActiveMountIndex >= 0)
                sawCasting = true;

            var hp = world.TryGetPlayer(1001, out var p1) ? p1.Hp : 0;
            if (hp >= before) continue;

            firstHitDamage = before - hp;
            if (world.TryGetMonster(entityId, out var hitter))
                firstHitDistance = MathF.Sqrt(hitter.Ai.X * hitter.Ai.X + hitter.Ai.Y * hitter.Ai.Y);
        }

        return (sawCasting, firstHitDistance, firstHitDamage);
    }

    [Fact]
    public void 装配把挂载读进内容包_非法概率报出来()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(CasterNpcId, casts: true));
        Assert.Equal(0, receipt.ProblemCount);
        Assert.Equal(1, receipt.SkillMounts);
        Assert.Equal(1, bundle.SkillMounts[CasterNpcId].Length);
        Assert.Equal(CastSkillId, bundle.SkillMounts[CasterNpcId][0].SkillId);
        Assert.Contains("skill_mounts=1", receipt.Describe());

        var (_, bad) = NpcContentLoader.Compile(Snapshot(CasterNpcId, casts: true, chanceBps: 20000));
        Assert.Contains($"skill_mount_bad_chance:{CasterNpcId}:1001:20000", bad.Problems);
    }

    [Fact]
    public void 没有挂载时整段不执行_内容回执为0()
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(CasterNpcId, casts: true, withMounts: false));
        Assert.Equal(0, receipt.ProblemCount);
        Assert.Equal(0, receipt.SkillMounts);
        Assert.Empty(bundle.SkillMounts);
        Assert.Contains("skill_mounts=0", receipt.Describe());
    }

    [Fact]
    public void 勾了Casts的NPC在近战距离之外伤人()
    {
        NpcContent.ResetToHardcoded();
        try
        {
            var (sawCasting, firstHit, _) = Run(CasterNpcId, casts: true);
            _log.WriteLine($"caster: casting={sawCasting} firstHit@{firstHit:F2}m");
            Assert.True(sawCasting, "勾了 Casts 且配了挂载的 NPC 从未进入吟唱");
            Assert.True(firstHit > 0f, "施法 NPC 在 120 拍内没有造成任何伤害");
            Assert.True(firstHit > 3f, $"第一次伤人发生在近战距离内（{firstHit:F2}m）——这不像是法术");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 没勾Casts的同挂载NPC必须走到近战才伤人()
    {
        NpcContent.ResetToHardcoded();
        try
        {
            var (sawCasting, firstHit, _) = Run(MeleeOnlyNpcId, casts: false);
            _log.WriteLine($"melee: casting={sawCasting} firstHit@{firstHit:F2}m");
            Assert.False(sawCasting, "没勾 Casts 的 NPC 居然进入了吟唱");
            Assert.True(firstHit > 0f, "近战 NPC 在 120 拍内没有造成任何伤害");
            Assert.True(firstHit < 3f, $"没勾 Casts 的那只在近战距离外就伤了人（{firstHit:F2}m）");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 晚进场的人能看到NPC正在念的技能身份()
    {
        NpcContent.ResetToHardcoded();
        try
        {
            // 反向对照先跑：同一份内容、同一个挂载，但 NPC 在 9000 米外全程不施法。
            // 这条夹具证明"技能身份恒 0"不是断言写死出来的（补发通路本身能走到，只是没有起手）。
            var (idleBundle, idleReceipt) = NpcContentLoader.Compile(
                Snapshot(CasterNpcId, casts: true, castMs: 2000, schoolMask: 2, spawnX: 9000f));
            Assert.Equal(0, idleReceipt.ProblemCount);
            NpcContent.Install(idleBundle);

            var idleWorld = new DsWorldSession(Options());
            Assert.True(idleWorld.AddPlayer(1001, 1001, 0f, 0f, 0f, out var idleReason), idleReason);
            idleWorld.ApplyNpcContentSpawns();
            var idleIdentity = WaitMonsterIdentity(idleWorld, 60);
            Assert.True(idleIdentity.CastTotalSeconds <= 0f,
                $"NPC 没在读条，补发 Spawn 却带了吟唱时长 {idleIdentity.CastTotalSeconds}——客户端会画一根不存在的条");
            Assert.Equal(0, idleIdentity.CastSkillId);

            // 正例：起手之后新人进场，补发必须带**技能身份**（id/系别/可打断），不只是时长。
            var (fightBundle, fightReceipt) = NpcContentLoader.Compile(
                Snapshot(CasterNpcId, casts: true, castMs: 2000, schoolMask: 2));
            Assert.Equal(0, fightReceipt.ProblemCount);
            NpcContent.Install(fightBundle);

            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
            world.ApplyNpcContentSpawns();

            ulong entityId = 0UL;
            for (var tick = 1; tick <= 10 && entityId == 0UL; tick++)
            {
                var r = world.Tick();
                foreach (var delta in r.WorldSnapshot?.Deltas ?? Array.Empty<Horizon.Game.Message.Sync.EntityDelta>())
                {
                    if (delta.Kind != Horizon.Game.Message.Sync.EntityDeltaKind.Spawn || delta.Identity is not { } identity) continue;
                    if (identity.TemplateId == CasterNpcId) entityId = delta.EntityId;
                }
            }

            Assert.True(entityId != 0UL, "敌对 NPC 没有生成");

            var casting = false;
            for (var tick = 0; tick < 120 && !casting; tick++)
            {
                world.Tick();
                if (world.TryGetMonster(entityId, out var monster) && monster.Runtime != null
                    && monster.Runtime.Casting.IsCasting)
                    casting = true;
            }

            Assert.True(casting, "NPC 在 120 拍内没有起手，这条用例测不到补发通路");

            Assert.True(world.AddPlayer(2002, 2002, 0f, 0f, 0f, out var lateReason), lateReason);
            var late = WaitMonsterIdentity(world, 5);

            _log.WriteLine($"late-join: total={late.CastTotalSeconds:F2} elapsed={late.CastElapsedSeconds:F2} " +
                           $"skill={late.CastSkillId} school={late.CastSchoolMask} interruptible={late.CastInterruptible}");
            Assert.Equal(2f, late.CastTotalSeconds, 2);
            Assert.True(late.CastElapsedSeconds > 0f,
                $"补发说\"在读条\"却零进度：{late.CastElapsedSeconds}——客户端会把条子从头重放");
            Assert.True(late.CastElapsedSeconds < late.CastTotalSeconds, "进度越过了总时长");
            Assert.Equal(CastSkillId, late.CastSkillId);
            Assert.Equal(2, late.CastSchoolMask);
            Assert.Equal(1, late.CastInterruptible);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 一场"两人站在 AOE 里"的对局：返回（主目标是否掉血、旁观者是否掉血、各自掉了多少）。
    /// 旁观者不攻击、不挡路，唯一可能打到它的就是这次 AOE。
    /// </summary>
    private (bool PrimaryHit, bool BystanderHit, int PrimaryDamage, int BystanderDamage) RunAoe(
        int targetCount, float aoeRadius)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(
            CasterNpcId, casts: true, targetSelect: 4, targetCount: targetCount, aoeRadius: aoeRadius));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        var world = new DsWorldSession(Options());
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var primaryReason), primaryReason);
        Assert.True(world.AddPlayer(1002, 1002, 0f, 4f, 0f, out var bystanderReason), bystanderReason);
        world.ApplyNpcContentSpawns();

        var primaryHp = world.TryGetPlayer(1001, out var p) ? p.Hp : 0;
        var bystanderHp = world.TryGetPlayer(1002, out var b) ? b.Hp : 0;
        var primaryDamage = 0;
        var bystanderDamage = 0;

        for (var tick = 0; tick < 200; tick++)
        {
            world.Tick();

            if (world.TryGetPlayer(1001, out var p1) && p1.Hp < primaryHp)
            {
                primaryDamage += primaryHp - p1.Hp;
                primaryHp = p1.Hp;
            }

            if (world.TryGetPlayer(1002, out var b1) && b1.Hp < bystanderHp)
            {
                bystanderDamage += bystanderHp - b1.Hp;
                bystanderHp = b1.Hp;
            }

            // 主目标一死，NPC 会转火旁观者（近战），那之后的掉血与"这一发 AOE 打几个"无关。
            if (!world.TryGetPlayer(1001, out var alive) || alive.Hp <= 0) break;
        }

        return (primaryDamage > 0, bystanderDamage > 0, primaryDamage, bystanderDamage);
    }

    [Fact]
    public void AOE挂载按目标数打到第二个玩家()
    {
        // 目标数/半径是编辑器里"这一发打几个、多大圈"的旋钮；此前没有一条断言证明它们进了运行期。
        NpcContent.ResetToHardcoded();
        try
        {
            // 正对照：同半径、目标数 1 ⇒ 旁观者一滴血都不该掉（证明"2"不是被忽略后无脑打全体）。
            var (onePrimary, oneBystander, onePrimaryDmg, oneBystanderDmg) = RunAoe(targetCount: 1, aoeRadius: 8f);
            _log.WriteLine($"aoe x1: primary={onePrimaryDmg} bystander={oneBystanderDmg}");
            Assert.True(onePrimary, "目标数 1 时连主目标都没掉血，这场对局不成立");
            Assert.False(oneBystander, $"目标数 1 却打到了旁观者（{oneBystanderDmg}）——目标数没生效");

            var (twoPrimary, twoBystander, twoPrimaryDmg, twoBystanderDmg) = RunAoe(targetCount: 2, aoeRadius: 8f);
            _log.WriteLine($"aoe x2: primary={twoPrimaryDmg} bystander={twoBystanderDmg}");
            Assert.True(twoPrimary, "目标数 2 时主目标没掉血");
            Assert.True(twoBystander,
                $"目标数 2、半径 8 米、两人相距 4 米，旁观者却一滴没掉——TargetCount/AoeRadius 没进结算");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 最小射程内的目标不放技能()
    {
        // MinRange 是"远程怪别贴脸放"的判据（内核：dist < MinRange ⇒ 不cast）。
        // 同一夹具两跑：不限射程会起手，MinRange=25 时即使贴到近战距离也**永不起手**。
        NpcContent.ResetToHardcoded();
        try
        {
            var (freeCast, _, _) = Run(CasterNpcId, casts: true, minRange: 0f);
            Assert.True(freeCast, "正对照：不限射程时应该起手，否则这场对局不成立");

            var (blockedCast, _, blockedDamage) = Run(CasterNpcId, casts: true, minRange: 25f);
            _log.WriteLine($"minRange=25: casting={blockedCast} meleeDamage={blockedDamage}");
            Assert.False(blockedCast, "MinRange=25 而全程都在 25 米内，却仍然起手了——最小射程没生效");
            Assert.True(blockedDamage > 0, "被射程挡住的 NPC 应该照样会走到近战——它连近战都没打，夹具不成立");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 非战斗位的挂载在接敌后不放()
    {
        // 内核：NonCombat ⇒ inCombat 时直接排除。敌对 NPC 一生成就会接敌，所以这条挂载全程不该起手；
        // 正对照用战斗位（SlotType=0）证明同一对局本身会施法。
        NpcContent.ResetToHardcoded();
        try
        {
            var (combatSlot, _, _) = Run(CasterNpcId, casts: true, slotType: 0);
            Assert.True(combatSlot, "正对照：战斗位应该起手，否则这场对局不成立");

            var (nonCombatSlot, _, nonCombatDamage) = Run(CasterNpcId, casts: true, slotType: 1);
            _log.WriteLine($"nonCombat: casting={nonCombatSlot} meleeDamage={nonCombatDamage}");
            Assert.False(nonCombatSlot, "接敌之后非战斗位仍然起手了——时机过滤没生效");
            Assert.True(nonCombatDamage > 0, "非战斗位被挡住后连近战都没打，夹具不成立");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 受控位当前等价于战斗位()
    {
        // 现状（刻意钉住）：内核的 OnControlled 只判"在战斗中"，全仓没有"是否受控"的输入源，
        // 所以受控位 = 战斗位。将来接上 CC 判据时，这条断言会红——那正是要人回来改它的提醒。
        NpcContent.ResetToHardcoded();
        try
        {
            var (controlled, _, _) = Run(CasterNpcId, casts: true, slotType: 3);
            _log.WriteLine($"controlled: casting={controlled}");
            Assert.True(controlled, "受控位在战斗中没起手——要么 CC 判据接上了（请更新本用例），要么时机过滤坏了");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    private static CombatActionPacket Attack(ulong attackerId, ulong targetId) => new()
    {
        AttackerId = attackerId,
        TargetId = targetId,
        SkillId = 0,
        ClientTick = 1,
        AttackerYaw = 0f,
        ActionKind = CombatActionKind.NormalAttack,
    };

    [Fact]
    public void 残血位要等血量掉到阈值以下才放()
    {
        // 内核：EmergencyLowHp ⇒ 必须 inCombat 且 hpPercent <= EmergencyHpPercent。
        // 阈值填 99：满血时不许放；被玩家打一下（哪怕只掉几点血）就该放。
        NpcContent.ResetToHardcoded();
        try
        {
            var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(
                CasterNpcId, casts: true, slotType: 2, emergencyHpPercent: 99));
            Assert.Equal(0, receipt.ProblemCount);
            NpcContent.Install(bundle);

            var world = new DsWorldSession(Options());
            Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
            world.ApplyNpcContentSpawns();

            ulong entityId = 0UL;
            for (var tick = 0; tick < 10 && entityId == 0UL; tick++)
            {
                var r = world.Tick();
                foreach (var delta in r.WorldSnapshot?.Deltas ?? Array.Empty<Horizon.Game.Message.Sync.EntityDelta>())
                {
                    if (delta.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Spawn
                        && delta.Identity is { } id && id.TemplateId == CasterNpcId)
                        entityId = delta.EntityId;
                }
            }

            Assert.True(entityId != 0UL, "敌对 NPC 没有生成");
            Assert.True(world.TryGetPlayer(1001, out var player) && player != null);
            Assert.True(world.TryGetMonster(entityId, out var before) && before != null);
            var fullHp = before.Ai.Hp;

            // 第一阶段：满血、接敌（它会走过来打人），但**残血位不许放**。
            var earlyCast = false;
            for (var tick = 0; tick < 40; tick++)
            {
                world.Tick();
                if (world.TryGetMonster(entityId, out var m) && m.Runtime != null && m.Runtime.Casting.IsCasting)
                    earlyCast = true;
            }

            Assert.False(earlyCast, "满血（阈值 99）时残血位就起手了——血量门槛没生效");

            // 第二阶段：打它一下，掉血后再给 60 拍。
            var damaged = false;
            var lateCast = false;
            for (var tick = 0; tick < 60 && !lateCast; tick++)
            {
                if (!damaged)
                {
                    world.SubmitCombatAction(1001, Attack(player.EntityId, entityId));
                    world.Tick();
                    if (world.TryGetMonster(entityId, out var now) && now != null && now.Ai.Hp < fullHp) damaged = true;
                }
                else
                {
                    world.Tick();
                }

                if (world.TryGetMonster(entityId, out var m) && m.Runtime != null && m.Runtime.Casting.IsCasting)
                    lateCast = true;
            }

            _log.WriteLine($"emergency: damaged={damaged} lateCast={lateCast}");
            Assert.True(damaged, "玩家一下都没打中，第二阶段无从谈起");
            Assert.True(lateCast, "血量已经低于残血阈值，残血位却始终没放");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 打断探针：起手后对宿主公开的 <c>InterruptRequested</c> 发一次请求，看这根挂载是否被中断。
    /// 返回（是否起手、请求后是否仍在吟唱、是否在近战距离外结算了伤害）。
    /// </summary>
    private (bool CastStarted, bool StillCastingAfterRequest, bool DamageAtRange) RunInterruptProbe(byte interruptible)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(CasterNpcId, casts: true, interruptible: interruptible));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        var world = new DsWorldSession(Options());
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
        world.ApplyNpcContentSpawns();

        ulong entityId = 0UL;
        for (var tick = 0; tick < 10 && entityId == 0UL; tick++)
        {
            var r = world.Tick();
            foreach (var delta in r.WorldSnapshot?.Deltas ?? Array.Empty<Horizon.Game.Message.Sync.EntityDelta>())
            {
                if (delta.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Spawn
                    && delta.Identity is { } id && id.TemplateId == CasterNpcId)
                    entityId = delta.EntityId;
            }
        }

        Assert.True(entityId != 0UL, "敌对 NPC 没有生成");

        var castStarted = false;
        for (var tick = 0; tick < 120 && !castStarted; tick++)
        {
            world.Tick();
            if (world.TryGetMonster(entityId, out var m) && m.Runtime != null && m.Runtime.Casting.IsCasting)
                castStarted = true;
        }

        Assert.True(castStarted, "120 拍内没起手，打断探针无从谈起");

        Assert.True(world.TryGetMonster(entityId, out var casting));
        casting.InterruptRequested = true;
        world.Tick();
        world.Tick();
        var stillCasting = world.TryGetMonster(entityId, out var after) && after.Runtime != null
                           && after.Runtime.Casting.IsCasting;

        // 让它把这一发走完（若被中断则不会结算），看近战距离外有没有掉血。
        var damageAtRange = false;
        var seenRange = 0f;
        for (var tick = 0; tick < 40 && !damageAtRange; tick++)
        {
            var before = world.TryGetPlayer(1001, out var p0) ? p0.Hp : 0;
            world.Tick();
            var hp = world.TryGetPlayer(1001, out var p1) ? p1.Hp : 0;
            if (hp >= before) continue;
            if (!world.TryGetMonster(entityId, out var hitter)) continue;
            seenRange = MathF.Sqrt(hitter.Ai.X * hitter.Ai.X + hitter.Ai.Y * hitter.Ai.Y);
            damageAtRange = seenRange > 3f;
        }

        _log.WriteLine($"interruptible={interruptible}: started={castStarted} stillCasting={stillCasting} " +
                       $"damageAtRange={damageAtRange}@{seenRange:F1}m");
        return (castStarted, stillCasting, damageAtRange);
    }

    [Fact]
    public void 可打断的挂载被打断后不再结算()
    {
        // Interruptible 是编辑器里"这一发能不能被打断"的开关；宿主把打断请求写进 InterruptRequested，
        // 内核按它决定是取消起手还是照放。判据只看**请求后的两拍**：
        // 内核在打断后会按 1.5 秒退避**重试**（打断≠永久封禁），所以"之后还会掉血"是正确表现，不能拿它当判据。
        NpcContent.ResetToHardcoded();
        try
        {
            var (_, interruptibleStillCasting, _) = RunInterruptProbe(interruptible: 1);
            Assert.False(interruptibleStillCasting, "可打断的挂载收到打断请求后仍在吟唱");

            var (_, hardStillCasting, _) = RunInterruptProbe(interruptible: 0);
            Assert.True(hardStillCasting, "不可打断的挂载收到打断请求就停了——这一位没被读");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 吟唱窗口内的位移探针：从**起手那一刻**记 X，到这次吟唱结束（或 12 拍）为止累计走了多远。
    /// 宿主在 <c>ShouldHalt</c> 时把本拍位移清零，所以"不许移动"应当表现为位移≈0。
    /// </summary>
    private float RunMovementProbe(byte allowMovement)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(CasterNpcId, casts: true, allowMovement: allowMovement));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        var world = new DsWorldSession(Options());
        Assert.True(world.AddPlayer(1001, 1001, 0f, 0f, 0f, out var reason), reason);
        world.ApplyNpcContentSpawns();

        ulong entityId = 0UL;
        for (var tick = 0; tick < 10 && entityId == 0UL; tick++)
        {
            var r = world.Tick();
            foreach (var delta in r.WorldSnapshot?.Deltas ?? Array.Empty<Horizon.Game.Message.Sync.EntityDelta>())
            {
                if (delta.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Spawn
                    && delta.Identity is { } id && id.TemplateId == CasterNpcId)
                    entityId = delta.EntityId;
            }
        }

        Assert.True(entityId != 0UL, "敌对 NPC 没有生成");

        var casting = false;
        for (var tick = 0; tick < 120 && !casting; tick++)
        {
            world.Tick();
            casting = world.TryGetMonster(entityId, out var m) && m.Runtime != null && m.Runtime.Casting.IsCasting;
        }

        Assert.True(casting, "120 拍内没起手，位移探针无从谈起");

        Assert.True(world.TryGetMonster(entityId, out var atStart));
        var startX = atStart.Ai.X;
        var maxDrift = 0f;
        for (var tick = 0; tick < 12; tick++)
        {
            world.Tick();
            if (!world.TryGetMonster(entityId, out var now)) break;
            var drift = MathF.Abs(now.Ai.X - startX);
            if (drift > maxDrift) maxDrift = drift;
            if (now.Runtime == null || !now.Runtime.Casting.IsCasting) break;
        }

        _log.WriteLine($"allowMovement={allowMovement}: drift={maxDrift:F2}m");
        return maxDrift;
    }

    [Fact]
    public void 吟唱中不许移动时位移为零()
    {
        // AllowMovement 是"站桩读条还是边跑边读"的开关；两个方向各跑一次，比较同一吟唱窗口内的位移。
        NpcContent.ResetToHardcoded();
        try
        {
            var blocked = RunMovementProbe(allowMovement: 0);
            var free = RunMovementProbe(allowMovement: 1);

            Assert.True(free > blocked,
                $"允许移动的位移（{free:F2}m）不比站桩（{blocked:F2}m）大——AllowMovement 没进宿主");
            Assert.True(blocked < 0.2f, $"不许移动时仍然走了 {blocked:F2} 米——本拍位移没被清零");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 概率为零的挂载永不起手()
    {
        // 内核判据：ChanceBasisPoints == 0 ⇒ 这根挂载不是"必放"，而是"永不参与"。
        // 编辑器允许填 0（等于关掉这一条），所以必须有断言证明 0 真的不是 100%。
        NpcContent.ResetToHardcoded();
        try
        {
            var (sawCasting, _, _) = Run(CasterNpcId, casts: true, chanceBps: 0);
            Assert.False(sawCasting, "概率填 0 的挂载仍然起手了——0 被当成 10000 用了");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 伤害倍率真的进到伤害里()
    {
        // DamageScaleBps 是编辑器里唯一能调"这一发打多疼"的旋钮；没有这条断言时，
        // 即使装配把 20000 丢成默认 10000，所有用例依旧全绿。
        NpcContent.ResetToHardcoded();
        try
        {
            var (_, _, baseDmg) = Run(CasterNpcId, casts: true, damageScaleBps: 10000);
            var (_, _, doubleDmg) = Run(CasterNpcId, casts: true, damageScaleBps: 20000);
            _log.WriteLine($"damage: base={baseDmg} x2={doubleDmg}");

            Assert.True(baseDmg > 0, "基准局没造成伤害，比较无从谈起");
            // 实测 15 → 30：这条夹具里伤害是确定值，所以直接钉精确倍数（"更高"会把 1.1 倍也放过）。
            Assert.Equal(baseDmg * 2, doubleDmg);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 取最近一份带怪物 Identity 的 delta（NPC 敌对走怪物管线，EntityType 同为 Monster）。
    /// <para>晚进场者的补发可能走**专属**快照（<c>InitialSpawnSnapshots</c>）而不是共享分组，两条都要扫。</para>
    /// </summary>
    private static Horizon.Game.Message.Sync.Components.NetworkIdentityAuthComponent WaitMonsterIdentity(
        DsWorldSession world, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var r = world.Tick();
            var found = Scan(r.WorldSnapshot);
            if (found.HasValue) return found.Value;

            foreach (var group in r.SnapshotGroups ?? Array.Empty<Horizon.Game.Core.Sim.Server.DsSnapshotGroup>())
            {
                found = Scan(group.Snapshot);
                if (found.HasValue) return found.Value;
            }

            foreach (var initial in r.InitialSpawnSnapshots ?? Array.Empty<Horizon.Game.Core.Sim.Server.DsInitialSpawnSnapshot>())
            {
                found = Scan(initial.Snapshot);
                if (found.HasValue) return found.Value;
            }
        }

        throw new Xunit.Sdk.XunitException("跑了 " + maxTicks + " tick 都没拿到怪物的 Spawn Identity（补发通路根本没走通）");
    }

    private static Horizon.Game.Message.Sync.Components.NetworkIdentityAuthComponent? Scan(
        Horizon.Game.Message.Sync.SnapshotPacket? snapshot)
    {
        if (snapshot == null) return null;
        foreach (var d in snapshot.Deltas)
        {
            if (d.Identity.HasValue && d.Identity.Value.EntityType == DsWorldSession.EntityTypeMonster)
                return d.Identity.Value;
        }

        return null;
    }
}
