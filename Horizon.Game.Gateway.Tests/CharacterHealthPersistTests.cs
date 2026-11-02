using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 服务端算出来的血池必须真的写回角色表（§14.7 G15 的另一半 / 任务 #29）。
/// <para>
/// 症状形状：角色表的 <c>max_health</c> 列长期<b>只有读方没有写方</b>——
/// 同一个 Grain 里 <c>LoadCharacterRowForProfileAsync</c> 那条路径在读这一列，
/// 而写它的人不存在。于是上限只活在 Redis 热缓存里，清缓存或换环境后行值是 0，
/// 死亡判定就把活人当死人（客户端血条却是满的，因为它自己按基值聚合）。
/// </para>
/// <para>
/// 这里钉的是"落库的那一行确实带上了权威数值"，以及一条更严的负向：
/// 上限还没建立时<b>不许</b>写——写 0 等于把别的系统算出来的数抹掉。
/// </para>
/// </summary>
public class CharacterHealthPersistTests
{
    private const ulong CharacterId = 770001UL;

    private static (CharacterGrain grain, Mock<IDataContext<GameEntityContext, CharacterEntity, long>> db,
                    CharacterEntity row) CreateGrain(float maxHealth, float currentHealth)
    {
        var grain = (CharacterGrain)RuntimeHelpers.GetUninitializedObject(typeof(CharacterGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CharacterGrain>.Instance);

        // CharacterId 是自动属性而不是字段，InjectField 找不到字段会直接断言失败；
        // 这里显式走属性 setter，并且断言属性还在——将来改名要炸在测试里，而不是静默写成 0 号角色。
        var characterIdProperty = typeof(CharacterGrain)
            .GetProperty("CharacterId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(characterIdProperty);
        characterIdProperty!.SetValue(grain, CharacterId);

        var info = new CharacterInfo
        {
            CharacterId = (long)CharacterId,
            CharacterName = "落库测试",
            MaxHealth = maxHealth,
            CurrentHealth = currentHealth,
            IsAlive = true,
        };
        var state = new CharacterState { CharacterInfo = info };
        var persistent = new Mock<IPersistentState<CharacterState>>();
        persistent.SetupGet(s => s.State).Returns(state);
        persistent.Setup(s => s.WriteStateAsync()).Returns(Task.CompletedTask);
        GrainTestHarness.InjectField(grain, "_characterState", persistent.Object);

        // 表里已有的行：血两列故意留 0，正是线上真实形状（只有别人写过的等级/攻防列）
        var row = new CharacterEntity { Id = (long)CharacterId, Level = 7, MaxHealth = 0f, Health = 0f };
        var db = new Mock<IDataContext<GameEntityContext, CharacterEntity, long>>();
        db.Setup(d => d.QueryFirstOrDefaultAsync(It.IsAny<Expression<Func<CharacterEntity, bool>>>(), It.IsAny<bool>()))
          .ReturnsAsync(row);
        db.Setup(d => d.UpdateAsync(It.IsAny<CharacterEntity>(), It.IsAny<long>()))
          .ReturnsAsync(true);
        GrainTestHarness.InjectField(grain, "_gameCharacterContext", db.Object);

        return (grain, db, row);
    }

    private static ResurrectMessage FullRestore() => new()
    {
        ResurrectedId = CharacterId,
        ResurrectType = 1,          // 1 = 完全复活
    };

    [Fact]
    public async Task ResurrectWritesTheAuthoritativePoolIntoTheCharacterRow()
    {
        var (grain, db, row) = CreateGrain(maxHealth: 1234f, currentHealth: 0f);

        var response = await grain.ResurrectAsync(FullRestore());

        Assert.Equal(1234f, response.MaxHealth);
        db.Verify(d => d.UpdateAsync(It.Is<CharacterEntity>(r =>
            r.MaxHealth == 1234f && r.Health == 1234f), It.Is<long>(id => id == (long)CharacterId)), Times.Once);
        // 只碰血两列：等级这种别人维护的列不许被顺手改写
        Assert.Equal(7, row.Level);
    }

    [Fact]
    public async Task HalfRestorePersistsHalfHealth()
    {
        var (grain, db, _) = CreateGrain(maxHealth: 1000f, currentHealth: 0f);

        await grain.ResurrectAsync(new ResurrectMessage { ResurrectedId = CharacterId, ResurrectType = 0 });

        db.Verify(d => d.UpdateAsync(It.Is<CharacterEntity>(r =>
            r.MaxHealth == 1000f && r.Health == 500f), It.IsAny<long>()), Times.Once);
    }

    /// <summary>
    /// 上限为 0（还没被算过）时必须<b>不写</b>：写进去就是把 0 盖到这一列上，
    /// 而 0 正是我们要消灭的那个值。这条断言的意义在于防住"顺手加个兜底写入"的改法。
    /// </summary>
    [Fact]
    public async Task UncomputedPoolIsNeverWrittenToTheRow()
    {
        var (grain, db, row) = CreateGrain(maxHealth: 0f, currentHealth: 0f);

        await grain.ResurrectAsync(FullRestore());

        db.Verify(d => d.UpdateAsync(It.IsAny<CharacterEntity>(), It.IsAny<long>()), Times.Never);
        Assert.Equal(0f, row.MaxHealth);
    }

    /// <summary>角色表没有这一行时只记日志，不新建行：建行是创建角色流程的职责。</summary>
    [Fact]
    public async Task MissingCharacterRowDoesNotCreateOne()
    {
        var (grain, db, _) = CreateGrain(maxHealth: 900f, currentHealth: 0f);
        db.Setup(d => d.QueryFirstOrDefaultAsync(It.IsAny<Expression<Func<CharacterEntity, bool>>>(), It.IsAny<bool>()))
          .ReturnsAsync((CharacterEntity?)null);

        await grain.ResurrectAsync(FullRestore());

        db.Verify(d => d.AddAsync(It.IsAny<CharacterEntity>()), Times.Never);
        db.Verify(d => d.UpdateAsync(It.IsAny<CharacterEntity>(), It.IsAny<long>()), Times.Never);
    }
}
