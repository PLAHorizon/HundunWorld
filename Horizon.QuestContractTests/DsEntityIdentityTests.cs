using System;
using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Quests;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// 世界实体身份通路（设计文档 §11 方案 1）的契约测试。
/// <para>
/// 这条通路存在的唯一理由：<b>客户端不能声明"我和谁说过话"</b>。它只能报实体 id，
/// 身份由 DS 上行的描述符在网关侧解出。于是"这个 id 是否存在、是什么类别、离玩家多远"
/// 三件事全部变成服务端可判定的事实——本文件盯的就是这三件事在各环节口径一致。
/// </para>
/// </summary>
public class DsEntityIdentityTests
{
    private static DsEntityDescriptorDto Npc(ulong entityId, int templateId, float x, float y, float z) => new()
    {
        EntityId = entityId,
        ScriptedKind = 2, // ScriptedKind.Npc
        TemplateId = templateId,
        SpawnPointId = -1,
        X = x,
        Y = y,
        Z = z,
    };

    // ── 线上契约 ──────────────────────────────────────────────

    [Fact]
    public void 描述符编解码后身份与坐标一位不差()
    {
        var batch = new DsEntityDescriptorBatch
        {
            ShardId = 7,
            Descriptors = new[]
            {
                Npc(4295032833UL, 3, 100.5f, -12.25f, 300f),
                new DsEntityDescriptorDto { EntityId = 8589934593UL, ScriptedKind = 0, TemplateId = 11, SpawnPointId = 2, X = 1f, Y = 2f, Z = 3f, Removed = true },
            },
        };

        var payload = DsUplinkCodec.EncodePayload(DsUplinkMessageKind.EntityDescriptor, batch);
        Assert.True(DsUplinkCodec.TryDecode<DsEntityDescriptorBatch>(payload, out var kind, out var decoded));
        Assert.Equal(DsUplinkMessageKind.EntityDescriptor, kind);
        Assert.Equal(7L, decoded!.ShardId);
        Assert.Equal(2, decoded.Descriptors.Length);

        var npc = decoded.Descriptors[0];
        Assert.Equal(4295032833UL, npc.EntityId);
        Assert.Equal(2, npc.ScriptedKind);
        Assert.Equal(3, npc.TemplateId);
        Assert.Equal(-1, npc.SpawnPointId);
        Assert.Equal(100.5f, npc.X);
        Assert.Equal(-12.25f, npc.Y);
        Assert.Equal(300f, npc.Z);
        Assert.False(npc.Removed);

        Assert.True(decoded.Descriptors[1].Removed);
    }

    [Fact]
    public void 内核结构与线上契约互逆()
    {
        var core = new DsEntityDescriptor
        {
            EntityId = 99UL, ScriptedKind = 2, TemplateId = 5, SpawnPointId = -4,
            X = 1.5f, Y = 2.5f, Z = 3.5f, Removed = true,
        };

        var dto = DsEntityDescriptorDto.FromCore(in core);
        var back = dto.ToCore();

        Assert.Equal(core.EntityId, back.EntityId);
        Assert.Equal(core.ScriptedKind, back.ScriptedKind);
        Assert.Equal(core.TemplateId, back.TemplateId);
        Assert.Equal(core.SpawnPointId, back.SpawnPointId);
        Assert.Equal(core.X, back.X);
        Assert.Equal(core.Y, back.Y);
        Assert.Equal(core.Z, back.Z);
        Assert.Equal(core.Removed, back.Removed);
    }

    [Theory]
    [InlineData(0, QuestEntityKind.Monster)] // 静态刷怪点：不是脚本生成物，语义就是怪
    [InlineData(1, QuestEntityKind.Monster)] // ScriptedKind.Monster
    [InlineData(2, QuestEntityKind.Npc)]
    [InlineData(3, QuestEntityKind.Prop)]   // ScriptedKind.Event：世界里的一个物件
    [InlineData(4, QuestEntityKind.Zone)]   // ScriptedKind.TriggerZone
    public void 生成类别按任务语义分类(byte scriptedKind, QuestEntityKind expected)
        => Assert.Equal(expected, DsEntityRegistry.ToQuestKind(scriptedKind));

    // ── 注册表 ────────────────────────────────────────────────

    [Fact]
    public void 注册表按描述符增删并在移除后查不到()
    {
        var registry = new DsEntityRegistry();

        registry.Apply(new DsEntityDescriptorBatch { ShardId = 1, Descriptors = new[] { Npc(11UL, 3, 0f, 0f, 0f) } });
        Assert.Equal(1, registry.Count(1));
        Assert.True(registry.TryResolve(1, 11UL, out var entry));
        Assert.Equal(3, entry.TemplateId);

        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 1,
            Descriptors = new[] { new DsEntityDescriptorDto { EntityId = 11UL, Removed = true } },
        });
        Assert.Equal(0, registry.Count(1));
        Assert.False(registry.TryResolve(1, 11UL, out _));
    }

    [Fact]
    public void 同一片shard的重复登记以最后一次为准且不会翻倍()
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch { ShardId = 1, Descriptors = new[] { Npc(11UL, 3, 1f, 1f, 1f) } });
        registry.Apply(new DsEntityDescriptorBatch { ShardId = 1, Descriptors = new[] { Npc(11UL, 3, 9f, 9f, 9f) } });

        Assert.Equal(1, registry.Count(1));
        Assert.True(registry.TryResolve(1, 11UL, out var entry));
        Assert.Equal(9f, entry.X);
    }

    [Fact]
    public void 注册表按shard隔离且整片丢弃只影响那一片()
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch { ShardId = 1, Descriptors = new[] { Npc(11UL, 3, 0f, 0f, 0f) } });
        registry.Apply(new DsEntityDescriptorBatch { ShardId = 2, Descriptors = new[] { Npc(11UL, 4, 0f, 0f, 0f) } });

        // 实体 id 由 (spawnPointId<<32)|slot 生成，不同 shard 用同一份刷怪表时会撞号——
        // 所以跨 shard 绝不能互查，否则一个 shard 的假 id 就能在另一个 shard 蒙中目标。
        Assert.Equal(3, registry.TryResolve(1, 11UL, out var a) ? a.TemplateId : -1);
        Assert.Equal(4, registry.TryResolve(2, 11UL, out var b) ? b.TemplateId : -1);

        registry.ClearShard(1);
        Assert.Equal(0, registry.Count(1));
        Assert.Equal(1, registry.Count(2));
    }

    [Fact]
    public void 邻近查询只收半径内且类别成立的实体并按实体id定序()
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 1,
            Descriptors = new[]
            {
                Npc(30UL, 3, 10f, 0f, 0f),                       // 近、NPC
                Npc(20UL, 2, 10f, 0f, 0f),                       // 近、NPC（id 更小，应排在前面）
                new DsEntityDescriptorDto { EntityId = 40UL, ScriptedKind = 1, TemplateId = 9, X = 5f, Y = 0f, Z = 0f }, // 近、怪
                Npc(50UL, 5, 500f, 0f, 0f),                      // 远、NPC
            },
        });

        var givers = registry.FindNear(1, 0f, 0f, 0f, 100f, e => e.QuestKind == QuestEntityKind.Npc);

        Assert.Equal(2, givers.Count);
        Assert.Equal(20UL, givers[0].EntityId); // 同一世界状态必须产出同一份列表
        Assert.Equal(30UL, givers[1].EntityId);
        Assert.All(givers, e => Assert.Equal(QuestEntityKind.Npc, e.QuestKind));
    }

    [Fact]
    public void 半径外的实体一律不算邻近即使类别成立()
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch { ShardId = 1, Descriptors = new[] { Npc(20UL, 3, 100f, 0f, 0f) } });

        Assert.Empty(registry.FindNear(1, 0f, 0f, 0f, 50f));
        Assert.Single(registry.FindNear(1, 0f, 0f, 0f, 101f));
        // 负半径是调用方的错误，不解释为"全域"
        Assert.Empty(registry.FindNear(1, 0f, 0f, 0f, -1f));
    }

    [Fact]
    public void 空批次与未知shard都不会把注册表搞坏()
    {
        var registry = new DsEntityRegistry();

        registry.Apply(null);
        registry.Apply(new DsEntityDescriptorBatch { ShardId = 1, Descriptors = Array.Empty<DsEntityDescriptorDto>() });
        Assert.Equal(0, registry.Count(1));

        // 未登记的 shard：解析失败、邻近查询为空（读取侧一律"未知即不成立"）
        Assert.False(registry.TryResolve(9, 1UL, out _));
        Assert.Empty(registry.FindNear(9, 0f, 0f, 0f, 1000f));
    }

    [Fact]
    public void 角色位置缓存是ECS_Z_up而注册表是Flax_Y_up换轴只在一处做()
    {
        // 写回侧口径（DsPositionWritebackService）：ecs(x,y,z) = flax(x, z, y)。
        // 反向比较时必须用同一个换轴，否则"水平距离"会变成"高度差"，邻近判定全体失效。
        var registry = new DsEntityRegistry();
        const float flaxX = 100f, flaxY = 5f, flaxZ = 300f;
        registry.Apply(new DsEntityDescriptorBatch { ShardId = 1, Descriptors = new[] { Npc(20UL, 3, flaxX, flaxY, flaxZ) } });

        // 角色缓存里的 ECS 坐标
        float ecsX = flaxX, ecsY = flaxZ, ecsZ = flaxY;
        // 换回 Flax 空间再比
        var hits = registry.FindNear(1, ecsX, ecsZ, ecsY, 10f);
        Assert.Single(hits);

        // 不换轴的直接比较：Y/Z 差 295，落在 10 半径之外——正是"忘了换轴"的现场表现
        Assert.Empty(registry.FindNear(1, ecsX, ecsY, ecsZ, 10f));
    }

    // ── 交互裁决（§11b：客户端只报实体 id，身份/距离由服务端定）──

    private static DsEntityRegistry RegistryWithNpcNearOrigin(long shardId = 1, float x = 10f, float y = 0f, float z = 0f)
    {
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = shardId,
            Descriptors = new[]
            {
                Npc(20UL, 3, x, y, z),                                                                 // NPC 模板 3
                new DsEntityDescriptorDto { EntityId = 21UL, ScriptedKind = 1, TemplateId = 9, X = x, Y = y, Z = z }, // 怪
            },
        });
        return registry;
    }

    [Fact]
    public void 裁决成立时用的是服务端解出的模板id而不是客户端报的()
    {
        var verdict = QuestInteractionAuthority.Evaluate(
            RegistryWithNpcNearOrigin(), 1, 20UL, (0f, 0f, 0f));

        Assert.True(verdict.Ok);
        Assert.Equal(string.Empty, verdict.Reason);
        Assert.Equal(3, verdict.TemplateId);
        Assert.Equal(QuestEntityKind.Npc, verdict.Kind);
    }

    [Fact]
    public void 通路未接线时一律不采信绝不退回相信客户端()
        => Assert.Equal("no_registry", QuestInteractionAuthority.Evaluate(null, 1, 20UL, (0f, 0f, 0f)).Reason);

    [Theory]
    [InlineData(0UL, "unknown_entity")]      // 0 号是"无实体"哨兵
    [InlineData(99UL, "unknown_entity")]     // 这个 shard 的世界里没有它
    [InlineData(21UL, "not_interactable")]   // 是怪，不是对话对象
    public void 身份不成立的三种拒绝各有各的短码(ulong entityId, string expectedReason)
    {
        var verdict = QuestInteractionAuthority.Evaluate(RegistryWithNpcNearOrigin(), 1, entityId, (0f, 0f, 0f));
        Assert.False(verdict.Ok);
        Assert.Equal(expectedReason, verdict.Reason);
    }

    [Fact]
    public void 实体只在自己那片shard可解跨片id不能蒙()
    {
        // 两个 shard 用同一份刷怪表 → 同一个实体 id 在两边都存在，但模板不同。
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch { ShardId = 1, Descriptors = new[] { Npc(20UL, 3, 0f, 0f, 0f) } });
        registry.Apply(new DsEntityDescriptorBatch { ShardId = 2, Descriptors = new[] { Npc(20UL, 7, 0f, 0f, 0f) } });

        Assert.Equal(3, QuestInteractionAuthority.Evaluate(registry, 1, 20UL, (0f, 0f, 0f)).TemplateId);
        Assert.Equal(7, QuestInteractionAuthority.Evaluate(registry, 2, 20UL, (0f, 0f, 0f)).TemplateId);
        // 角色在 shard 3（那片根本没注册）：查不到，不采信
        Assert.Equal("unknown_entity", QuestInteractionAuthority.Evaluate(registry, 3, 20UL, (0f, 0f, 0f)).Reason);
    }

    [Fact]
    public void 读不到角色位置时不采信而不是免检通过()
    {
        var verdict = QuestInteractionAuthority.Evaluate(RegistryWithNpcNearOrigin(), 1, 20UL, null);
        Assert.False(verdict.Ok);
        Assert.Equal("no_position", verdict.Reason);
    }

    [Fact]
    public void 隔着半个地图不算交互()
    {
        // NPC 站在 flax(100000, 0, 0)，玩家在原点：无论怎么换算轴都不该重合。
        var registry = RegistryWithNpcNearOrigin(x: 100_000f);
        Assert.Equal("too_far", QuestInteractionAuthority.Evaluate(registry, 1, 20UL, (0f, 0f, 0f)).Reason);
        Assert.Equal("too_far", QuestInteractionAuthority.Evaluate(
            registry, 1, 20UL, QuestInteractionAuthority.ToFlax(0f, 0f, 100_000f)).Reason);

        // 玩家确实站在 NPC 旁边（ECS 记作 x=100000，换轴回 flax 仍是 x=100000）：成立。
        Assert.True(QuestInteractionAuthority.Evaluate(
            registry, 1, 20UL, QuestInteractionAuthority.ToFlax(100_000f, 0f, 0f)).Ok);
    }

    [Fact]
    public void ECS到Flax的换轴必须是x不变y取z()
    {
        var flax = QuestInteractionAuthority.ToFlax(ecsX: 11f, ecsY: 22f, ecsZ: 33f);
        Assert.Equal(11f, flax.X);
        Assert.Equal(33f, flax.Y); // flax 上下 = ECS 前后
        Assert.Equal(22f, flax.Z); // flax 前后 = ECS 上下
    }

    [Fact]
    public void 发布者收窄用的是服务端解出的身份而不是客户端填的数()
    {
        var registry = RegistryWithNpcNearOrigin();   // NPC 模板 3 在 (10,0,0)；怪在 (10,0,0)

        // 站得够近 + 是 NPC：解出 (类别=Npc, 模板=3)
        Assert.True(QuestInteractionAuthority.TryResolveGiver(
            registry, 1, 20UL, (10f, 0f, 0f), out var kind, out var templateId, out var reason));
        Assert.Equal((int)QuestEntityKind.Npc, kind);
        Assert.Equal(3, templateId);
        Assert.Equal(string.Empty, reason);

        // 同一片世界里的怪不能当发布者（它不会被"点开"）
        Assert.False(QuestInteractionAuthority.TryResolveGiver(
            registry, 1, 21UL, (10f, 0f, 0f), out _, out _, out var monsterReason));
        Assert.Equal("not_interactable", monsterReason);

        // 走远了就不算"正对着它"（默认半径 800 世界单位）：宁可给空可接，也不能退化成总表
        Assert.False(QuestInteractionAuthority.TryResolveGiver(
            registry, 1, 20UL, (10f + QuestInteractionAuthority.DefaultRadius + 50f, 0f, 0f),
            out _, out _, out var farReason));
        Assert.Equal("too_far", farReason);

        // 不存在的实体 id / 未接线的注册表，各有各的短码（都判不成立）
        Assert.False(QuestInteractionAuthority.TryResolveGiver(
            registry, 1, 777UL, (10f, 0f, 0f), out _, out _, out var unknownReason));
        Assert.Equal("unknown_entity", unknownReason);
        Assert.False(QuestInteractionAuthority.TryResolveGiver(
            null, 1, 20UL, (10f, 0f, 0f), out _, out _, out var noRegistryReason));
        Assert.Equal("no_registry", noRegistryReason);
    }

    [Fact]
    public void 一次交互只推进自己那类目标且模板id必须相等()
    {
        // 服务端认定：NPC 模板 3
        Assert.True(QuestInteractionAuthority.MatchesObjective("talk_npc:3", QuestEntityKind.Npc, 3));
        Assert.False(QuestInteractionAuthority.MatchesObjective("talk_npc:4", QuestEntityKind.Npc, 3));
        // 类别必须对得上：拿怪的身份推不动对话目标
        Assert.False(QuestInteractionAuthority.MatchesObjective("talk_npc:3", QuestEntityKind.Monster, 3));
        // 物件目标认物件（脚本 Event 归 Prop），不认 NPC
        Assert.True(QuestInteractionAuthority.MatchesObjective("interact_prop:8", QuestEntityKind.Prop, 8));
        Assert.False(QuestInteractionAuthority.MatchesObjective("interact_prop:8", QuestEntityKind.Npc, 8));
        // 击杀/采集类目标绝不能被"对话"越权推进
        Assert.False(QuestInteractionAuthority.MatchesObjective("kill_monster:3", QuestEntityKind.Npc, 3));
        Assert.False(QuestInteractionAuthority.MatchesObjective("collect_item:3", QuestEntityKind.Npc, 3));
        // 裸关键词（历史数据里的"与NPC对话"）不匹配：一次交互不许把任意对话目标推满
        Assert.False(QuestInteractionAuthority.MatchesObjective("talk", QuestEntityKind.Npc, 3));
        Assert.False(QuestInteractionAuthority.MatchesObjective(null, QuestEntityKind.Npc, 3));
    }
}
