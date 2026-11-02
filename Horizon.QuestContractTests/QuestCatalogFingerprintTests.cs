using System;
using System.Linq;
using Horizon.Orleans.Interface.World;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// 任务目录版本号（TECH_DEBT T10）：客户端的任务日志/追踪栏靠这个整数做脏标记重画，
/// 所以"库里换了内容而版本号不动"就等于**运营改了玩法、玩家看不到**——
/// 不停服实时更新这条承诺失效时不会有任何报错，这是它必须单独钉住的原因。
/// <para>
/// 旧实现是 <c>Σ(ContentVersion + PublishState) + 行数</c>。下面第 3、4 条是**专门照旧算法写不通过的样子**
/// 设计的（互补变化、不 bump 内容版本的手改库），把它们连同第 1 条一起看，才构成"改对了"的证据；
/// 反过来，把 <see cref="QuestCatalogFingerprint.Compute"/> 换回那句求和，第 3、4 条会立刻判红。
/// </para>
/// </summary>
public class QuestCatalogFingerprintTests
{
    private static QuestCatalogEntry Entry(int questId,
                                          int contentVersion = 1,
                                          int publishState = 1,
                                          string objectives = "kill_monster:4#2",
                                          string rewards = "exp#100",
                                          int[]? prerequisites = null,
                                          string giverName = "铸剑师")
        => new QuestCatalogEntry
        {
            QuestId = questId,
            QuestName = "任务" + questId,
            QuestType = 1,
            LevelRequire = 1,
            PublishState = publishState,
            GiverKind = 1,
            GiverTemplateId = 3,
            ContentVersion = contentVersion,
            PrerequisiteQuestIds = prerequisites ?? Array.Empty<int>(),
            Description = "描述",
            RepeatLimit = 0,
            ObjectivesJson = objectives,
            RewardsJson = rewards,
            GiverName = giverName,
        };

    [Fact]
    public void 同内容同顺序必须得到同一个版本号()
    {
        var left = new[] { Entry(90001), Entry(90002), Entry(90003) };
        var right = new[] { Entry(90001), Entry(90002), Entry(90003) };
        Assert.Equal(QuestCatalogFingerprint.Compute(left), QuestCatalogFingerprint.Compute(right));
    }

    [Fact]
    public void 入参顺序不得影响版本号()
    {
        var a = new[] { Entry(90001), Entry(90002), Entry(90003) };
        var b = new[] { Entry(90003), Entry(90001), Entry(90002) };
        Assert.Equal(QuestCatalogFingerprint.Compute(a), QuestCatalogFingerprint.Compute(b));
    }

    /// <summary>旧算法在这一条上是错的：90001 下线（−1）被 90002 内容版本 +1 抵掉，和不变。</summary>
    [Fact]
    public void 两条任务的互补变化不得互相抵消()
    {
        var before = new[] { Entry(90001, publishState: 1, contentVersion: 1), Entry(90002, publishState: 1, contentVersion: 2) };
        var after = new[] { Entry(90001, publishState: 0, contentVersion: 1), Entry(90002, publishState: 1, contentVersion: 3) };

        var sumBefore = before.Sum(e => e.ContentVersion + e.PublishState) + before.Length;
        var sumAfter = after.Sum(e => e.ContentVersion + e.PublishState) + after.Length;
        Assert.Equal(sumBefore, sumAfter); // 先证明"求和"这一路确实看不见这次改动（不是我在挑一个假例子）

        Assert.NotEqual(QuestCatalogFingerprint.Compute(before), QuestCatalogFingerprint.Compute(after));
    }

    /// <summary>直接改库（运营手改目标串、没动 content_version）必须让版本号前进。</summary>
    [Fact]
    public void 内容换了而没有bump版本时也必须前进()
    {
        var before = new[] { Entry(90001, contentVersion: 1, objectives: "kill_monster:4#2") };
        var after = new[] { Entry(90001, contentVersion: 1, objectives: "kill_monster:4#3") };

        Assert.Equal(before.Sum(e => e.ContentVersion + e.PublishState) + before.Length,
                     after.Sum(e => e.ContentVersion + e.PublishState) + after.Length);

        Assert.NotEqual(QuestCatalogFingerprint.Compute(before), QuestCatalogFingerprint.Compute(after));
    }

    [Theory]
    [InlineData("kill_monster:4#2", "talk_npc:3")]
    [InlineData("exp#100", "exp#101")]
    [InlineData("铸剑师", "游方商队")]
    public void 玩家看得见的每一处都得留下痕迹(string field, string changed)
    {
        var before = new[] { Entry(90001, objectives: field, rewards: "exp#100", giverName: "铸剑师") };
        var after = new[]
        {
            Entry(90001,
                  objectives: field == "kill_monster:4#2" ? changed : field,
                  rewards: field == "exp#100" ? changed : "exp#100",
                  giverName: field == "铸剑师" ? changed : "铸剑师"),
        };

        Assert.NotEqual(QuestCatalogFingerprint.Compute(before), QuestCatalogFingerprint.Compute(after));
    }

    [Fact]
    public void 前置任务的集合变化必须可见而不能只算和()
    {
        var left = new[] { Entry(90001, prerequisites: new[] { 1, 3 }) };
        var right = new[] { Entry(90001, prerequisites: new[] { 2, 2 }) };
        var reordered = new[] { Entry(90001, prerequisites: new[] { 3, 1 }) };

        Assert.NotEqual(QuestCatalogFingerprint.Compute(left), QuestCatalogFingerprint.Compute(right));
        // 同一组前置换个写法不该被当成"改了玩法"
        Assert.Equal(QuestCatalogFingerprint.Compute(left), QuestCatalogFingerprint.Compute(reordered));
    }

    [Fact]
    public void 空目录与单条任务不得撞成同一个版本号()
    {
        Assert.Equal(QuestCatalogFingerprint.EmptyCatalog, QuestCatalogFingerprint.Compute(Array.Empty<QuestCatalogEntry>()));
        Assert.NotEqual(QuestCatalogFingerprint.Compute(Array.Empty<QuestCatalogEntry>()),
                        QuestCatalogFingerprint.Compute(new[] { Entry(90001) }));
    }
}
