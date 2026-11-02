using System;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;
using ScriptedKind = Horizon.Game.Message.Network.ScriptedKind;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 敌对型 NPC 走怪物管线时，"名字/等级"必须退到 NPC 目录。
/// <para>
/// 不测这条的话，症状是玩家看到 "Monster_500001 / Lv.1" 的敌人——它能打、给经验，
/// 但名字和等级全是目录兜底值，且没有任何异常能提示你配错了。
/// 两个解析函数是内部静态（仅对测试程序集开放），这里直接断言回退分支。
/// </para>
/// </summary>
/// <remarks>
/// 本类读**进程级**的怪物代码目录、并且会改写 NPC 内容注册表，所以必须与怪物内容测试串行
/// （`MonsterEditorContractTests` 的扫描闸门就是为此设的）。
/// </remarks>
[Collection("monster-content")]
public class HostileNpcIdentityTests
{
    private readonly ITestOutputHelper _log;

    public HostileNpcIdentityTests(ITestOutputHelper log) => _log = log;

    private static void InstallHostileNpc()
    {
        var bundle = new NpcContentBundle { ContentRevision = 1 };
        bundle.ByNpcId[500001] = new NpcRuntimeConfig
        {
            NpcId = 500001,
            Name = "山贼头目",
            Title = "山贼",
            ModelKey = "npc_bandit_chief",
            LevelBase = 32,
            LevelVariance = 0,
            Stance = NpcStance.Hostile,
            Rank = 1,
            WuxingDomain = 2,
            AttackableOverride = -1,
        };

        NpcContent.Install(bundle);
    }

    [Fact]
    public void HostileNpc_UsesNpcNameAndLevelWhenMonsterCatalogHasNoSuchEntry()
    {
        InstallHostileNpc();
        try
        {
            // 前提：怪物目录里确实没有 500001（否则这条测的是别的东西）。
            Assert.False(Horizon.Game.Sim.MonsterTemplateCatalog.TryGetRegistered(500001, out _));

            var monster = new DsMonster { TemplateId = 500001, ScriptedKind = (int)ScriptedKind.Monster };
            var template = DsWorldSession.ResolveMonsterTemplate(monster);

            Assert.Equal("山贼头目", template.Name);
            Assert.Equal(32, template.Level);
            Assert.Equal("npc_bandit_chief", template.ModelKey);
            Assert.Equal(32, DsWorldSession.ResolveTemplateLevel(500001));

            _log.WriteLine($"name={template.Name} level={template.Level} model={template.ModelKey}");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void RegularMonster_StillResolvesFromMonsterCatalog()
    {
        InstallHostileNpc();
        try
        {
            // 对照：普通怪（1 号）必须仍走怪物目录，不能被 NPC 回退抢走。
            Assert.True(Horizon.Game.Sim.MonsterTemplateCatalog.TryGetRegistered(1, out var expected));
            Assert.Equal(expected.Level, DsWorldSession.ResolveTemplateLevel(1));

            var monster = new DsMonster { TemplateId = 1, ScriptedKind = (int)ScriptedKind.Monster };
            Assert.Equal(expected.Name, DsWorldSession.ResolveMonsterTemplate(monster).Name);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void UnknownId_FallsBackToCatalogPlaceholder()
    {
        NpcContent.ResetToHardcoded();
        var monster = new DsMonster { TemplateId = 987654, ScriptedKind = (int)ScriptedKind.Monster };
        var template = DsWorldSession.ResolveMonsterTemplate(monster);

        // 两边目录都没有 → 走怪物目录的确定性兜底名，而不是抛异常或显示空名。
        Assert.False(string.IsNullOrWhiteSpace(template.Name));
        Assert.True(DsWorldSession.ResolveTemplateLevel(987654) >= 1);
    }
}
