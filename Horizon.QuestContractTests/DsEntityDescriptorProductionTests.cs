using System;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Quests;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// DS 侧实体身份描述的**执行级**证据（此前只有编译级）。
/// <para>
/// 这条链断在任何一环，现场表现都是"服务端认不出玩家面前的对象"，而不会有任何报错：
/// 所以这里直接开一个真的 <see cref="DsWorldSession"/>，刷出怪、杀掉，
/// 看 tick 结果里到底有没有把身份带出来、带对了没有、撤销了没有。
/// </para>
/// </summary>
public class DsEntityDescriptorProductionTests
{
    private static DsWorldSession NewWorld() => new(new DsWorldSessionOptions
    {
        ShardId = 3,
        TicksPerSecond = 60,
        SnapshotIntervalTicks = 3,
    });

    private static SpawnPointConfig Point(int id, int maxAlive, float x)
    {
        var cfg = SpawnPointConfig.Default;
        cfg.SpawnPointId = id;
        cfg.ShardId = 3;
        cfg.X = x;
        cfg.Y = 0f;
        cfg.MaxAlive = maxAlive;
        cfg.MonsterTemplateId = 100 + id;
        cfg.RespawnDelaySeconds = 5f;
        cfg.AggroRadius = 0f;   // 不打架，只看身份上报，免得 AI 把位置搅动
        cfg.LeashRadius = 0f;
        cfg.WanderRadius = 0f;
        cfg.MoveSpeed = 0f;
        cfg.SlotSpacing = 6f;
        return cfg;
    }

    [Fact]
    public void 刷出怪的那个tick必须带出身份描述符()
    {
        var world = NewWorld();
        world.ConfigureSpawnPoints(new[] { Point(1, 2, 0f) });

        var result = world.Tick();

        Assert.NotEmpty(result.SpawnedMonsters);
        var descriptors = result.EntityDescriptors;
        Assert.Equal(result.SpawnedMonsters.Count, descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            Assert.False(descriptor.Removed);
            Assert.Equal(101, descriptor.TemplateId);      // 刷怪点 1 → 模板 101
            Assert.Equal(1, descriptor.SpawnPointId);
            Assert.Equal(0, descriptor.ScriptedKind);      // 静态点 = 非脚本生成
            Assert.True(descriptor.EntityId != 0UL);
        }

        // 身份里的坐标必须真的是生成坐标：邻近判定全靠它，写成 0 就等于所有对象都"站在原点"。
        foreach (var descriptor in descriptors)
        {
            Assert.True(world.TryGetMonster(descriptor.EntityId, out var monster));
            Assert.Equal(monster.Ai.X, descriptor.X);
            Assert.Equal(monster.Ai.Y, descriptor.Y);
            Assert.Equal(monster.Ai.Z, descriptor.Z);
        }
    }

    [Fact]
    public void 没有变更的tick不该重复播报身份()
    {
        var world = NewWorld();
        world.ConfigureSpawnPoints(new[] { Point(1, 1, 0f) });

        Assert.NotEmpty(world.Tick().EntityDescriptors);   // 第一个 tick：生成 → 有一条
        Assert.Empty(world.Tick().EntityDescriptors);      // 第二个 tick：什么都没变 → 必须为空
    }

    [Fact]
    public void 实体离开世界必须撤销它的身份()
    {
        var world = NewWorld();
        world.ConfigureSpawnPoints(new[] { Point(1, 1, 0f) });

        var spawned = world.Tick();
        var entityId = spawned.SpawnedMonsters[0];
        Assert.Contains(spawned.EntityDescriptors, d => d.EntityId == entityId && !d.Removed);

        Assert.True(world.MarkMonsterDead(entityId, world.ServerTick));

        // 死亡即离开世界：注册表必须收到撤销，否则一个死透的 NPC 会永远留在"可交互对象"名单里。
        var afterDeath = world.Tick();
        var removal = Assert.Single(afterDeath.EntityDescriptors, d => d.EntityId == entityId);
        Assert.True(removal.Removed);

        Assert.DoesNotContain(world.CollectAllEntityDescriptors(), d => d.EntityId == entityId);
    }

    [Fact]
    public void 全量补发与逐条变更算出的是同一个世界()
    {
        var world = NewWorld();
        world.ConfigureSpawnPoints(new[] { Point(1, 2, 0f), Point(2, 1, 40f) });

        world.Tick();

        var all = world.CollectAllEntityDescriptors();
        Assert.Equal(world.MonsterCount, all.Count);
        // 顺序固定：补发列表进协议/UI 时必须可复现
        for (var i = 1; i < all.Count; i++)
            Assert.True(all[i - 1].EntityId < all[i].EntityId);

        Assert.Contains(all, d => d.TemplateId == 101);
        Assert.Contains(all, d => d.TemplateId == 102);
    }

    [Fact]
    public void DS产出的描述符喂进注册表后就该能解出权威身份()
    {
        // 这一条把 §11 的两端第一次接起来跑：DS 生成 → 转线上契约 → 网关注册表 → 服务端裁决。
        var world = NewWorld();
        world.ConfigureSpawnPoints(new[] { Point(1, 1, 12f) });

        var descriptor = Assert.Single(world.Tick().EntityDescriptors);
        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = world.ShardId,
            Descriptors = new[] { DsEntityDescriptorDto.FromCore(in descriptor) },
        });

        Assert.True(registry.TryResolve(world.ShardId, descriptor.EntityId, out var entry));
        Assert.Equal(101, entry.TemplateId);
        Assert.Equal(QuestEntityKind.Monster, entry.QuestKind);

        // 静态刷怪点是"怪"，不是对话对象：裁决必须拒绝，而不是因为认得出 id 就放行。
        var verdict = QuestInteractionAuthority.Evaluate(registry, world.ShardId, descriptor.EntityId, (0f, 0f, 0f));
        Assert.False(verdict.Ok);
        Assert.Equal("not_interactable", verdict.Reason);

        // 换个类别再判一次：证明"能交互"是身份决定的，不是"认识这个 id"决定的。
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = world.ShardId,
            Descriptors = new[]
            {
                new DsEntityDescriptorDto
                {
                    EntityId = descriptor.EntityId, ScriptedKind = 2, TemplateId = 101,
                    X = descriptor.X, Y = descriptor.Y, Z = descriptor.Z,
                },
            },
        });

        var npcVerdict = QuestInteractionAuthority.Evaluate(
            registry, world.ShardId, descriptor.EntityId, (descriptor.X, descriptor.Y, descriptor.Z));
        Assert.True(npcVerdict.Ok);
        Assert.Equal(101, npcVerdict.TemplateId);
    }

    [Fact]
    public void 没有玩家时也照样产出身份()
    {
        // 注册表服务的是"业务问题"（这个对象存在吗），不是"画面问题"：
        // 一个玩家都没有的时候世界仍然是权威的，网关不该等到有人进场才知道世界上有什么。
        var world = NewWorld();
        world.ConfigureSpawnPoints(new[] { Point(1, 2, 0f) });

        Assert.Equal(0, world.PlayerCount);
        Assert.Equal(2, world.Tick().EntityDescriptors.Count);
    }
}
