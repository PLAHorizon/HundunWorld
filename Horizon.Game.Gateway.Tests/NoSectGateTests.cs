using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// "取消学习技能的门派限制"这条需求的**护栏**：需求写的是取消，而库里那两列还在
/// （<c>Game_HunduShijie_SkillTemplate.profession_limit</c> 与物品表同名列，种子里一律写空串）。
/// 列留着没关系，**关系是没有任何代码再拿它当闸门**——一旦有人"顺手"加回一道门派判定，
/// §14 那套纯动态门槛（好感/材料/属性/技能树完整性）就退回到"看出身能不能学"，
/// 而这套动态条件的全部意义就在于它不看出身。
/// </summary>
public class NoSectGateTests
{
    private static readonly string[] GateLayers =
    {
        "Horizon.Orleans.Grains",          // 习得判定（SkillGrain / WuxingLearnGates）
        "Horizon.Game.Core",               // 消息处理与玩法编排
        "Horizon.Game.Message",            // 契约：不该有"门派"字段在门槛语义里流转
    };

    /// <summary>
    /// 允许出现的仅限"数据形状"本身：实体列定义与 EF 迁移快照。
    /// 判定逻辑一律不许出现这两个标识符。
    /// </summary>
    private static readonly string[] AllowedFiles =
    {
        "Horizon.Model" , "Horizon.Entities",
    };

    [Fact]
    public void NoCodeReadsProfessionLimitAsAGate()
    {
        var root = FindRepoRoot();

        // 1) 判定/编排/契约三层里彻底没有这个标识符
        foreach (var layer in GateLayers)
        {
            var dir = Path.Combine(root, layer);
            if (!Directory.Exists(dir)) continue;

            var hits = new List<string>();
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsGenerated(file)) continue;
                var text = File.ReadAllText(file);
                if (text.Contains("ProfessionLimit", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("profession_limit", StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(Path.GetRelativePath(root, file));
                }
            }

            Assert.True(hits.Count == 0,
                $"{layer} 里出现了门派限制标识符（{string.Join(", ", hits)}）：习得条件必须是纯动态的，" +
                "加回门派判定会让 §14 整套门槛形同虚设");
        }

        // 2) 反向确认"列还在、只是没人读"：实体定义里它仍存在（否则这条护栏就成了空转）
        var modelDir = Path.Combine(root, AllowedFiles[0]);
        Assert.True(Directory.Exists(modelDir), "找不到 Horizon.Model，本测试的对照前提失效");
        var stillDefined = Directory
            .EnumerateFiles(modelDir, "*.cs", SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f).Contains("ProfessionLimit", StringComparison.OrdinalIgnoreCase));
        Assert.True(stillDefined, "门派列若已被删除，这条护栏应改成断'列不存在'，而不是留着空转");
    }

    private static bool IsGenerated(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Contains("/obj/") || normalized.Contains("/bin/")
            || normalized.Contains("/Migrations/");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        return dir!.FullName;
    }
}
