using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.Games;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 演化封存（D2）在<b>施放入口</b>真的挡得住——并且是常驻断言，不是只在真集群里跑的那几条。
/// <para>
/// 封存在本仓是<b>派生态</b>：不写"已封存"标记，而是每次现算"已学演化式的 consume 并集"。
/// 这个选择很好（散功即自动解封、不会出现僵尸灰格），但它把风险挪到了判定处：
/// 只要施放路径少问一次 <c>ComputeRetired()</c>，被融合掉的母技就<b>照样能放</b>——
/// 玩家侧表现为"技能树里画了斜杠、技能栏清空，却还在偷偷生效"，
/// 而"换代即让位"这条演化设计的地基就没了。
/// </para>
/// <para>
/// 这条规则此前只有 <c>SkillEvolutionLiveTests</c>（环境门控，缺 Redis 直接 return）覆盖，
/// 等于平时并不存在。这里用与 <see cref="SkillCastDeathCheckTests"/> 同一套脚手架做常驻断言。
/// </para>
/// </summary>
public class SkillCastRetirementTests
{
    private const int SourceSkillId = 10301;      // 金·天阶·极（会被融合掉的母技）
    private const int DaoSkillId = 70103;         // 道阶演化式，consume = [母技]
    private const int SiblingSkillId = 20301;     // 与演化无关的一式，用来验"不该挡的别挡"
    private const ulong CasterId = 3001UL;

    private static SkillGrain CreateGrain(params int[] learnedIds)
    {
        var grain = (SkillGrain)RuntimeHelpers.GetUninitializedObject(typeof(SkillGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<SkillGrain>.Instance);

        var learned = new Dictionary<int, SkillInfo>();
        foreach (var id in learnedIds)
        {
            learned[id] = new SkillInfo
            {
                SkillId = id,
                Level = 1,
                Cooldown = 0,
                Range = 0f,
            };
        }

        var state = new SkillState
        {
            LearnedSkills = learned,
            SkillCooldowns = new Dictionary<int, DateTime>(),
            SkillDependencies = new Dictionary<int, List<int>>(),
        };
        var persistent = new Mock<IPersistentState<SkillState>>();
        persistent.SetupGet(s => s.State).Returns(state);
        persistent.Setup(s => s.WriteStateAsync()).Returns(Task.CompletedTask);
        persistent.Setup(s => s.ClearStateAsync()).Returns(Task.CompletedTask);
        GrainTestHarness.InjectField(grain, "_skillState", persistent.Object);

        // 没有 DB 上下文时 EnsureReferenceDataAsync 不会覆盖已注入的模板表，所以这里能直接喂两份行。
        GrainTestHarness.InjectField(grain, "_skillTemplateContext",
            (IDataContext<GameEntityContext, SkillTemplateEntity, int>?)null);
        GrainTestHarness.InjectField(grain, "_templates", new Dictionary<int, SkillTemplateEntity>
        {
            [SourceSkillId] = new SkillTemplateEntity { Id = SourceSkillId, SkillName = "金锋蔽日", MaxLevel = 3 },
            [SiblingSkillId] = new SkillTemplateEntity { Id = SiblingSkillId, SkillName = "青藤缠岳", MaxLevel = 3 },
            [DaoSkillId] = new SkillTemplateEntity
            {
                Id = DaoSkillId,
                SkillName = "金木·成道",
                MaxLevel = 3,
                // 演化式回指被融合的两式；封存集合就是"已学演化式的这个数组"并集
                LearnCost = "{\"consume\":[" + SourceSkillId + ",90303]}",
            },
        });

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetCharacterInfoAsync(It.IsAny<GameQueryDto>()))
            .ReturnsAsync(new CharacterInfo
            {
                CharacterId = (long)CasterId,
                MaxHealth = 1000f,
                CurrentHealth = 1000f,
                IsAlive = true,
            });
        GrainTestHarness.InjectField(grain, "_characterGrainProvider",
            new Func<ulong, ICharacterGrain>(_ => character.Object));

        return grain;
    }

    private static SkillCastMessage Request(int skillId) => new()
    {
        CasterId = CasterId,
        SkillId = skillId,
        TargetEntityId = 0,
    };

    [Fact]
    public async Task SealedSourceSkill_IsRefusedAtTheCastGate()
    {
        var grain = CreateGrain(SourceSkillId, DaoSkillId);

        var result = await grain.CastSkillAsync(Request(SourceSkillId));

        Assert.False(result.Success);
        Assert.Contains("已融于演化", result.Message);
    }

    [Fact]
    public async Task TheEvolvedSkill_ItselfIsNotSealed()
    {
        // 反向也要钉：封存集合若被算成"演化式自己"或"整行全封"，玩家换代后反而一个都放不出来。
        // 口径只问封存与否——冷却/目标等原因被拒都算通过（越界断言会把这条写成假红）。
        var grain = CreateGrain(SourceSkillId, DaoSkillId);

        var result = await grain.CastSkillAsync(Request(DaoSkillId));

        Assert.True(!result.Message.Contains("已融于演化"),
            $"演化式自己被封存了（集合算错）：{result.Message}");
    }

    [Fact]
    public async Task AnUnrelatedSkill_IsNotCollateralDamage()
    {
        // 派生集合按 consume 精确匹配；把整阶或整行一起封掉是这类"派生态算太宽"的常见错法。
        var grain = CreateGrain(SourceSkillId, SiblingSkillId, DaoSkillId);

        var result = await grain.CastSkillAsync(Request(SiblingSkillId));

        Assert.True(!result.Message.Contains("已融于演化"),
            $"与演化无关的式被封存牵连：{result.Message}");
    }
}
