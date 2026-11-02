using System;
using System.Collections.Generic;
using System.IO;
using Horizon.Game.Sim.Npcs;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 制作/教学的**执行面**必须与清单面同数。
/// <para>
/// 症状（两条都已实证）：清单只列"这位 NPC 教的/绑定到这位设施的"配方，而动作只查"有没有训练师位/设施位"——
/// 于是站到任意训练师面前就能学会**全服任意**配方、站在任意设施前能做不属于它的东西；
/// 并且学费只写在教学行上、动作从不收——面板标价 800 金，实际免费。
/// </para>
/// <para>
/// 判据住在 <see cref="NpcCraftCore"/>（清单与动作共用一份），收费/退回的接线用源码守卫钉住。
/// </para>
/// </summary>
public class NpcCraftAuthorityTests
{
    private static List<NpcTrainerRow> Rows() => new()
    {
        new NpcTrainerRow(1001, NpcTrainerService.Skill, 200, 0, 0, 0),
        new NpcTrainerRow(900001, NpcTrainerService.Recipe, 800, 700011, 2, 1),
    };

    [Fact]
    public void FindTaughtRecipeRow_OnlyMatchesRecipeRowsOfThisTrainer()
    {
        var rows = Rows();

        var hit = NpcCraftCore.FindTaughtRecipeRow(rows, 900001);
        Assert.True(hit.HasValue);
        Assert.Equal(800, hit!.Value.CostGold);        // 收费也从这一行读
        Assert.Equal(700011, hit.Value.CostItemId);
        Assert.Equal(2, hit.Value.CostItemCount);

        Assert.Null(NpcCraftCore.FindTaughtRecipeRow(rows, 900002));   // 别人教的配方
        Assert.Null(NpcCraftCore.FindTaughtRecipeRow(rows, 1001));     // 技能行不算配方
        Assert.Null(NpcCraftCore.FindTaughtRecipeRow(rows, 0));
        Assert.Null(NpcCraftCore.FindTaughtRecipeRow(null, 900001));
        Assert.Null(NpcCraftCore.FindTaughtRecipeRow(Array.Empty<NpcTrainerRow>(), 900001));
    }

    [Fact]
    public void TaughtByTrainer_AgreesWithRowLookup()
    {
        var rows = Rows();
        foreach (var id in new[] { 900001, 900002, 1001, 0, -5 })
        {
            Assert.Equal(NpcCraftCore.FindTaughtRecipeRow(rows, id).HasValue, NpcCraftCore.TaughtByTrainer(rows, id));
        }
    }

    [Fact]
    public void StationIdentity_MustMatch_AndUnboundRecipesAreCraftableAnywhere()
    {
        // 未绑设施（≤0）：随处可做，是设计不是漏洞。
        Assert.True(NpcCraftCore.StationIdentityOk(0, 0));
        Assert.True(NpcCraftCore.StationIdentityOk(-1, 400001));
        // 绑了设施：交互对象必须就是那一位。
        Assert.True(NpcCraftCore.StationIdentityOk(1, 1));
        Assert.False(NpcCraftCore.StationIdentityOk(1, 2));
        Assert.False(NpcCraftCore.StationIdentityOk(400001, 1));
    }

    /// <summary>动作侧接线：执行面必须调同一份判据、必须收学费、学不成必须退回。</summary>
    [Fact]
    public void CraftHandler_WiresTheSameAuthorityAndChargesTheFee()
    {
        var handler = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Game.Core", "Handlers", "CraftHandler.cs"));

        Assert.Contains("NpcCraftCore.FindTaughtRecipeRow(NpcContent.TrainerRows(resolve.NpcId)", handler, StringComparison.Ordinal);
        Assert.Contains("NpcCraftCore.StationIdentityOk(recipe.StationNpcId, resolve.NpcId)", handler, StringComparison.Ordinal);
        Assert.Contains("这位前辈不教这张配方", handler, StringComparison.Ordinal);
        Assert.Contains("这里不是做这件东西的设施", handler, StringComparison.Ordinal);

        // 学费与材料：先扣、失败原路退回（与技能分支同一条纪律）。
        Assert.Contains("SpendAsync(WalletCurrency.Gold, row.CostGold", handler, StringComparison.Ordinal);
        Assert.Contains("ConsumeByTemplateAsync(request.CharacterId, row.CostItemId, row.CostItemCount)", handler, StringComparison.Ordinal);
        Assert.Contains("RefundRecipeLearnAsync(", handler, StringComparison.Ordinal);
        Assert.Contains("await wallet.AddAsync(WalletCurrency.Gold, gold, label)", handler, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到仓库根（Horizon.sln）");
    }
}