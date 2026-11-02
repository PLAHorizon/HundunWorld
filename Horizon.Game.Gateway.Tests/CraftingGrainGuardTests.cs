using System;
using System.IO;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 钉住 <c>CraftingGrain</c> 的两个假实现不再回来。
/// <para>
/// 这两处缺陷（现场捏配方 / 不看背包返回 true）是"没有测试就看不出"的典型：
/// 它们不抛异常、不写日志、也不影响编译，只会让玩家凭空造物。行为级断言需要真集群，
/// 所以这里用**源码针**兜底——只要那三段特征串重新出现就红。
/// </para>
/// <para>源码针必须同时钉"旧串消失"与"新串存在"，只钉一半时把实现整个删掉也会绿。</para>
/// </summary>
public class CraftingGrainGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void CraftingGrain_NoLongerFabricatesRecipesMaterialsOrItemIds()
    {
        var path = Path.Combine(RepoRoot(), "Horizon.Orleans.Grains", "CraftingGrain.cs");
        Assert.True(File.Exists(path), "找不到 CraftingGrain.cs：" + path);
        var source = File.ReadAllText(path);

        // ① 现场捏配方（任意 id 都能学会，且成功率是编的）
        Assert.DoesNotContain("SuccessRate = 1.0f,", source, StringComparison.Ordinal);

        // ② 恒真的材料检查
        Assert.DoesNotContain("此处返回true表示配方存在", source, StringComparison.Ordinal);

        // ③ 伪造实例 id
        Assert.DoesNotContain("recipeId * 1000L + state.CraftingHistory.Count", source, StringComparison.Ordinal);

        // 反向：判据必须真的接进来
        Assert.Contains("NpcCraftCore.CheckMaterials", source, StringComparison.Ordinal);
        Assert.Contains("LearnRecipeAsync(CraftingRecipe definition)", source, StringComparison.Ordinal);
        Assert.Contains("CraftItemAsync(int recipeId, Dictionary<long, int> held)", source, StringComparison.Ordinal);
    }
}
