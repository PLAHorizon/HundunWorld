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
/// 施放入口的"死亡判定"必须区分**真死**与**服务端还没算过属性**。
/// <para>
/// 原判定是 <c>casterInfo.CurrentHealth &lt;= 0</c>。服务端角色状态里 <c>MaxHealth</c> 没被聚合过
/// （新角色、未同步存档、或属性权威尚未落到服务端那份）时血就是 0，于是玩家看到的是
/// "一进游戏所有技能都回施法者已死亡"，而客户端角色活得好好的（血条是本地聚合的）。
/// 真死（有上限而血为 0）仍然必须被挡住 —— 两条都要有断言，只断一条会把这个分支写歪而无人知晓。
/// </para>
/// <para>
/// 死亡判定读的是**另一个 grain**（CharacterGrain）。这类跨 grain 调用若直接写 <c>GrainFactory</c>，
/// 在脚手架里会被外层 <c>catch</c> 静默吞掉，分支等于从没被执行过；所以 SkillGrain 留了
/// <c>_characterGrainProvider</c> 缝隙，本套件就是钉它。
/// </para>
/// </summary>
public class SkillCastDeathCheckTests
{
    private const int SkillId = 10101;
    private const ulong CasterId = 2001UL;

    private static SkillGrain CreateGrain(float maxHealth, float currentHealth, bool isAlive)
    {
        var grain = (SkillGrain)RuntimeHelpers.GetUninitializedObject(typeof(SkillGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<SkillGrain>.Instance);

        var state = new SkillState
        {
            LearnedSkills = new Dictionary<int, SkillInfo>
            {
                [SkillId] = new SkillInfo { SkillId = SkillId, Level = 1, Cooldown = 0, Range = 0f },
            },
            SkillCooldowns = new Dictionary<int, DateTime>(),
            SkillDependencies = new Dictionary<int, List<int>>(),
        };
        var persistent = new Mock<IPersistentState<SkillState>>();
        persistent.SetupGet(s => s.State).Returns(state);
        persistent.Setup(s => s.WriteStateAsync()).Returns(Task.CompletedTask);
        persistent.Setup(s => s.ClearStateAsync()).Returns(Task.CompletedTask);
        GrainTestHarness.InjectField(grain, "_skillState", persistent.Object);
        GrainTestHarness.InjectField(grain, "_skillTemplateContext",
            (IDataContext<GameEntityContext, SkillTemplateEntity, int>?)null);

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetCharacterInfoAsync(It.IsAny<GameQueryDto>()))
            .ReturnsAsync(new CharacterInfo
            {
                CharacterId = (long)CasterId,
                MaxHealth = maxHealth,
                CurrentHealth = currentHealth,
                IsAlive = isAlive,
            });
        GrainTestHarness.InjectField(grain, "_characterGrainProvider",
            new Func<ulong, ICharacterGrain>(_ => character.Object));

        return grain;
    }

    private static SkillCastMessage Request() => new()
    {
        CasterId = CasterId,
        SkillId = SkillId,
        TargetEntityId = 0,          // 不打靶：只验施放方自己的前置判定
    };

    [Fact]
    public async Task TrueDeath_StillBlocksTheCast()
    {
        var grain = CreateGrain(maxHealth: 1000f, currentHealth: 0f, isAlive: false);

        var result = await grain.CastSkillAsync(Request());

        Assert.False(result.Success);
        Assert.Contains("已死亡", result.Message);
    }

    [Fact]
    public async Task UninitialisedHealthPool_IsNotTreatedAsDeath()
    {
        // 上限为 0 = 服务端这份属性还没算过（不是死了）。挡下施放会把整条五行玩法锁死在登录界面上。
        var grain = CreateGrain(maxHealth: 0f, currentHealth: 0f, isAlive: true);

        var result = await grain.CastSkillAsync(Request());

        Assert.True(!result.Message.Contains("已死亡"),
            $"属性未初始化被误判成死亡：{result.Message}");
    }

    [Fact]
    public async Task MissingCharacterState_DoesNotBlockTheCast()
    {
        // 角色 grain 回 null（无存档）时，死亡判定不该自己再抛出去——它拿不到事实就不该断言"死了"。
        var grain = CreateGrain(maxHealth: 1000f, currentHealth: 0f, isAlive: false);
        var empty = new Mock<ICharacterGrain>();
        empty.Setup(c => c.GetCharacterInfoAsync(It.IsAny<GameQueryDto>()))
            .ReturnsAsync((CharacterInfo?)null);
        GrainTestHarness.InjectField(grain, "_characterGrainProvider",
            new Func<ulong, ICharacterGrain>(_ => empty.Object));

        var result = await grain.CastSkillAsync(Request());

        Assert.True(!result.Message.Contains("已死亡"),
            $"拿不到角色信息却判定死亡：{result.Message}");
    }
}
