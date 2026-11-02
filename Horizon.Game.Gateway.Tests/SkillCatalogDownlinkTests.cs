using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
/// 技能模板目录下行的契约测试。
/// <para>
/// 钉三件事：目录按 id 升序整份下来、公式串折成数值、以及"读不到"必须回 <c>Success=false</c>
/// 且不带半份数据——客户端据此保留本地兜底表，把失败当成"服务端确认没有技能"会清空技能栏。
/// </para>
/// </summary>
public class SkillCatalogDownlinkTests
{
    private static SkillGrain CreateGrain(
        IDataContext<GameEntityContext, SkillTemplateEntity, int>? context,
        Dictionary<int, SkillInfo>? learned = null)
    {
        var grain = (SkillGrain)RuntimeHelpers.GetUninitializedObject(typeof(SkillGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<SkillGrain>.Instance);

        var state = new SkillState { LearnedSkills = learned ?? new Dictionary<int, SkillInfo>() };
        var persistent = new Mock<IPersistentState<SkillState>>();
        persistent.SetupGet(s => s.State).Returns(state);
        GrainTestHarness.InjectField(grain, "_skillState", persistent.Object);

        GrainTestHarness.InjectField(grain, "_skillTemplateContext", context);
        return grain;
    }

    private static SkillTemplateEntity Template(int id, string name, int element,
        string energy, string cooldown, string castTime) => new SkillTemplateEntity
    {
        Id = id,
        SkillName = name,
        Description = "desc-" + id,
        WuxingElement = element,
        EnergyCostFormula = energy,
        CooldownFormula = cooldown,
        CastTimeFormula = castTime,
        AttackRange = 15f,
        MaxLevel = 10,
        LearnLevelRequire = 5,
    };

    private static Mock<IDataContext<GameEntityContext, SkillTemplateEntity, int>> ContextReturning(
        params SkillTemplateEntity[] rows)
    {
        var context = new Mock<IDataContext<GameEntityContext, SkillTemplateEntity, int>>();
        context.Setup(c => c.QueryAsync(It.IsAny<Expression<Func<SkillTemplateEntity, bool>>>(), It.IsAny<bool>()))
            .ReturnsAsync((Expression<Func<SkillTemplateEntity, bool>> predicate, bool _) =>
                rows.AsQueryable().Where(predicate).AsQueryable());
        return context;
    }

    [Fact]
    public async Task Catalog_comes_back_ordered_with_formulas_resolved()
    {
        // 故意乱序给进去：目录必须按 id 升序，客户端才不会出现段内错位。
        var grain = CreateGrain(ContextReturning(
            Template(3001, "寒冰掌", 3, "60", "6", "0.3"),
            Template(1001, "裂金斩", 1, "30", "3", "0.4"),
            Template(2001, "青木藤缠", 2, "70", "10", "0.3")).Object);

        var response = await grain.GetSkillCatalogAsync();

        Assert.True(response.Success);
        Assert.Equal(new[] { 1001, 2001, 3001 }, response.Templates.Select(t => t.SkillId).ToArray());
        Assert.Equal("裂金斩", response.Templates[0].SkillName);
        Assert.Equal(1, response.Templates[0].Element);
        Assert.Equal(30f, response.Templates[0].EnergyCost);
        Assert.Equal(3f, response.Templates[0].Cooldown);
        Assert.Equal(0.4f, response.Templates[0].CastTime);
    }

    [Fact]
    public async Task Dirty_formula_falls_back_to_zero_instead_of_failing_the_whole_catalog()
    {
        var grain = CreateGrain(ContextReturning(
            Template(1001, "裂金斩", 1, "30 + 2*level", "3", "0.4")).Object);

        var response = await grain.GetSkillCatalogAsync();

        // 现有内容一律是常数串；真出现公式串时按 0 下行并保留这一条，而不是整份失败。
        Assert.True(response.Success);
        Assert.Single(response.Templates);
        Assert.Equal(0f, response.Templates[0].EnergyCost);
        Assert.Equal(3f, response.Templates[0].Cooldown);
    }

    [Fact]
    public async Task Missing_context_reports_failure_without_a_half_populated_catalog()
    {
        var response = await CreateGrain(null).GetSkillCatalogAsync();

        Assert.False(response.Success);
        Assert.Empty(response.Templates);
        Assert.False(string.IsNullOrEmpty(response.Message));
    }

    [Fact]
    public async Task Read_failure_clears_templates_and_learned_and_reports_failure()
    {
        var context = new Mock<IDataContext<GameEntityContext, SkillTemplateEntity, int>>();
        context.Setup(c => c.QueryAsync(It.IsAny<Expression<Func<SkillTemplateEntity, bool>>>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("表未迁移"));

        var response = await CreateGrain(context.Object).GetSkillCatalogAsync();

        Assert.False(response.Success);
        Assert.Empty(response.Templates);
        Assert.Empty(response.Learned);
    }

    [Fact]
    public async Task Learned_skills_ride_along_with_the_catalog()
    {
        var grain = CreateGrain(ContextReturning(Template(1001, "裂金斩", 1, "30", "3", "0.4")).Object,
            new Dictionary<int, SkillInfo>
            {
                [1001] = new SkillInfo { SkillId = 1001, SkillName = "裂金斩", Level = 3 },
            });

        var response = await grain.GetSkillCatalogAsync();

        Assert.True(response.Success);
        Assert.Single(response.Learned);
        Assert.Equal(3, response.Learned[0].Level);
    }
}
