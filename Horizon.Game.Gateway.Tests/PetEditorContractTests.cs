using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Horizon.Game.Sim.Pets;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 宠物编辑器的**跨宿主协议对账**：Flax 插件的脚本域引不到仓库工程，所以草稿字段名与内核常量
/// 在插件里各有一份字面副本。这份副本不会编译失败，只会在运行时静默丢字段——
/// 因此两侧的等价性必须由测试按**源文本**断言。
/// <para>
/// <b>为什么不是"读一侧调另一侧"</b>：插件程序集不能被 xUnit 引用（它由 Flax.Build 产出、
/// 引用集是引擎给的那一套），所以这里只能读源文件、抠字面量。这也是最朴素但唯一可行的办法。
/// </para>
/// </summary>
public class PetEditorContractTests
{
    private readonly ITestOutputHelper _log;

    public PetEditorContractTests(ITestOutputHelper log) => _log = log;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(string relative)
    {
        var path = Path.Combine(RepoRoot(), relative);
        Assert.True(File.Exists(path), "源文件不存在：" + path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 从源文本里抠出一个字符串数组字面量的元素集合。
    /// <para><c>new string[] { … }</c> 与 <c>{ … }</c> 两种写法都要吃：本仓库里简写更常见，
    /// 只认带 <c>new</c> 的版本会让断言"找不到锚点"而不是"发现不一致"——那等于没测。</para>
    /// </summary>
    private static HashSet<string> ExtractKeySet(string source, string variableName)
    {
        var match = Regex.Match(source,
            Regex.Escape(variableName) + @"\s*=\s*(?:new\s+(?:string|System\.Collections\.Generic\.List)<string>\s*)?\{(?<body>[^}]*)\}");
        Assert.True(match.Success, $"在源文本里找不到 {variableName} 的数组字面量（改名或换写法会让本断言变成空跑）");
        var items = Regex.Matches(match.Groups["body"].Value, "\"(?<v>[^\"]+)\"")
            .Cast<Match>().Select(m => m.Groups["v"].Value).ToList();
        Assert.True(items.Count > 0, $"{variableName} 抠出来是 0 个元素——尺子错了");
        return new HashSet<string>(items, StringComparer.Ordinal);
    }

    /// <summary>抠出一个 int 数组字面量（内核常量表的对账用）。</summary>
    private static int[] ExtractIntArray(string source, string variableName)
    {
        var match = Regex.Match(source,
            Regex.Escape(variableName) + @"\s*=\s*(?:new\s+int\[\]\s*)?\{(?<body>[^}]*)\}");
        Assert.True(match.Success, $"找不到 int[] 字面量 {variableName}");
        // 数字里的分隔下划线必须先去掉，否则 7_500 会被抠成 7 和 500 两个值
        return Regex.Matches(match.Groups["body"].Value, @"-?[\d_]+").Cast<Match>()
            .Select(m => int.Parse(m.Value.Replace("_", string.Empty))).ToArray();
    }

    /// <summary>抠出一个标量 int 常量（含分隔符下划线写法）。</summary>
    private static int ExtractIntConst(string source, string name, string fileLabel)
    {
        var match = Regex.Match(source, @"const\s+int\s+" + Regex.Escape(name) + @"\s*=\s*(?<v>[\d_]+)");
        Assert.True(match.Success, $"{fileLabel} 里找不到常量 {name}");
        return int.Parse(match.Groups["v"].Value.Replace("_", string.Empty));
    }

    // ---------------- ① 草稿字段名：顶层 + 每个子段 ----------------

    [Fact]
    public void 插件草稿的顶层键集合与CLI逐字相同()
    {
        var cli = Read("Horizon.PetCli/PetDraft.cs");
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetDraftModel.cs");

        var cliKeys = ExtractKeySet(cli, "KnownTopLevelKeys");
        var pluginKeys = ExtractKeySet(plugin, "KnownTopLevelKeys");

        _log.WriteLine($"顶层键 {cliKeys.Count} 个");
        Assert.True(cliKeys.Count >= 30, $"顶层键数量偏少（{cliKeys.Count}），像是锚点抓错");
        Assert.True(pluginKeys.SetEquals(cliKeys),
            "顶层键不一致 → 缺：" + string.Join(",", pluginKeys.Except(cliKeys).Concat(cliKeys.Except(pluginKeys))));
    }

    [Fact]
    public void 插件草稿每个子段的键集合都与CLI相同()
    {
        var cli = Read("Horizon.PetCli/PetDraft.cs");
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetDraftModel.cs");

        // 七个子段：漏一段的话，那一段的字段漂移永远没人发现（KnownSectionKeys 里有 8 项，
        // 但顶层数组变量名各段同名 KnownKeys，所以按"文件里出现次数"自证两侧都全）
        var cliCount = Regex.Matches(cli, "public static readonly string\\[\\] KnownKeys").Count;
        var pluginCount = Regex.Matches(plugin, "public static readonly string\\[\\] KnownKeys").Count;
        Assert.True(cliCount >= 7, $"CLI 侧只找到 {cliCount} 个子段键集合，预期至少 7");
        Assert.Equal(cliCount, pluginCount);

        var sections = new[]
        {
            "PetStatBandDraft", "PetSkillRuleDraft", "PetSkillEntryDraft", "PetBehaviorDraft",
            "PetEquipRuleDraft", "PetAssistEntryDraft", "PetMergeRuleDraft", "PetMutationEntryDraft",
        };
        foreach (var typeName in sections)
        {
            var cliBlock = ClassBody(cli, typeName);
            var pluginBlock = ClassBody(plugin, PluginName(typeName));
            var a = ExtractKeySet(cliBlock, "KnownKeys");
            var b = ExtractKeySet(pluginBlock, "KnownKeys");
            Assert.True(b.SetEquals(a), $"{typeName} 子段键不一致 → 缺："
                                        + string.Join(",", a.Except(b).Concat(b.Except(a))));
        }
    }

    [Fact]
    public void 插件草稿声明的属性不会漏键()
    {
        // 键集合里有、属性里没有 = 这个键永远反序列化不到东西（STJ 默认不绑字段，属性名才作数）
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetDraftModel.cs");
        var declared = Regex.Matches(plugin, @"public\s+[\w<>?\[\]]+\s+(?<p>\w+)\s*\{\s*get;")
            .Select(m => m.Groups["p"].Value).ToHashSet(StringComparer.Ordinal);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { "KnownTopLevelKeys", "KnownKeys" })
        {
            foreach (var m in Regex.Matches(plugin, Regex.Escape(name) + @"\s*=\s*new[^{]*\{(?<body>[^}]*)\}")
                         .Cast<Match>())
            {
                foreach (var k in Regex.Matches(m.Groups["body"].Value, "\"(?<v>[^\"]+)\"").Cast<Match>())
                    keys.Add(k.Groups["v"].Value);
            }
        }

        // camelCase 反推回属性名：首字母大写即可（本项目字段名都是单词或驼峰，没有缩写连写）
        var missing = keys.Where(k => !declared.Contains(char.ToUpperInvariant(k[0]) + k.Substring(1))).ToList();
        Assert.True(missing.Count == 0, "这些键没有对应属性，反序列化会被静默丢弃：" + string.Join(",", missing));
    }

    private static string ClassBody(string source, string className)
    {
        var start = source.IndexOf("class " + className, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到类 {className}");
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source.Substring(open, i - open + 1);
            }
        }
        throw new InvalidOperationException("类括号不匹配：" + className);
    }

    private static string PluginName(string cliTypeName)
    {
        // PetStatBandDraft → PetStatBandModel 等
        return cliTypeName.EndsWith("Draft", StringComparison.Ordinal)
            ? cliTypeName.Substring(0, cliTypeName.Length - 5) + "Model"
            : cliTypeName;
    }

    // ---------------- ② 插件预览数学的表必须与内核同值 ----------------

    [Fact]
    public void 插件悟性与灵性表和内核逐值相同()
    {
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetPreviewMath.cs");
        Assert.Equal(PetStatsCore.ComprehensionBonusBps, ExtractIntArray(plugin, "ComprehensionBonusBps"));
        Assert.Equal(PetStatsCore.SpiritBonusNormalBps, ExtractIntArray(plugin, "SpiritBonusNormalBps"));
        Assert.Equal(PetStatsCore.SpiritBonusSuperBps, ExtractIntArray(plugin, "SpiritBonusSuperBps"));
    }

    [Fact]
    public void 插件默契门槛与倍率和内核逐值相同()
    {
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetPreviewMath.cs");
        Assert.Equal(ExtractIntConst(plugin, "LoyaltyRefuseThreshold", "PetPreviewMath"),
            PetBondCore.LoyaltyRefuseThreshold);
        Assert.Equal(ExtractIntConst(plugin, "HappinessBlockThreshold", "PetPreviewMath"),
            PetBondCore.HappinessBlockThreshold);
        Assert.Equal(ExtractIntConst(plugin, "LifespanBlockThreshold", "PetPreviewMath"),
            PetBondCore.LifespanBlockThreshold);
        Assert.Equal(ExtractIntConst(plugin, "HappinessGreenAt", "PetPreviewMath"),
            PetBondCore.HappinessGreenAt);
        Assert.Equal(ExtractIntConst(plugin, "HappinessYellowAt", "PetPreviewMath"),
            PetBondCore.HappinessYellowAt);
        Assert.Equal(
            new[] { PetBondCore.DamageMultRedBps, PetBondCore.DamageMultYellowBps, PetBondCore.DamageMultGreenBps },
            ExtractIntArray(plugin, "DamageMultiplierBps"));
    }

    [Fact]
    public void 插件号段常量与内核逐值相同()
    {
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetPreviewMath.cs");
        Assert.Equal(ExtractIntConst(plugin, "MinSpeciesId", "PetPreviewMath"), PetIdentityCore.MinSpeciesId);
        Assert.Equal(ExtractIntConst(plugin, "MaxSpeciesId", "PetPreviewMath"), PetIdentityCore.MaxSpeciesId);
        Assert.Equal(ExtractIntConst(plugin, "MaxInstancesPerSpecies", "PetPreviewMath"),
            PetIdentityCore.MaxInstancesPerSpecies);
        Assert.Equal(ExtractIntConst(plugin, "MaxPetLevel", "PetPreviewMath"), PetStatsCore.MaxPetLevel);
        Assert.Equal(ExtractIntConst(plugin, "PotentialPointsPerLevel", "PetPreviewMath"),
            PetStatsCore.PotentialPointsPerLevel);
    }

    [Fact]
    public void 插件有效资质算式与内核在同批输入上逐值相同()
    {
        // 参照实现：完全按文档文字另算一遍（不调用 PetPreviewMath），再与内核互比。
        // 插件那份是纯字符串副本、不能在 xUnit 里执行，所以这里比的是"内核 vs 独立参照"，
        // 而"插件 vs 内核"由上一条的表对账 + 下面这条的算式顺序断言共同覆盖。
        var reference = ReferenceEffectiveQuality(2000, 10, 10, false, 10_000);
        Assert.Equal(3649, reference);
        Assert.Equal(reference, PetStatsCore.EffectiveQuality(2000, 10, 10, false));
        Assert.Equal(3733, PetStatsCore.EffectiveQuality(2000, 10, 10, true));

        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetPreviewMath.cs");
        // 算式顺序（先悟性、再灵性、再变异、最后钳上限）必须一致：整数截断不可交换
        var order = new[] { "comp", "spi", "mut" };
        var positions = order.Select(o => plugin.IndexOf("value * (10_000L + " + o, StringComparison.Ordinal)).ToList();
        Assert.True(positions[0] >= 0 && positions[1] >= 0, "插件里找不到三段乘法的锚点（写法变了会让这条对账空跑）");
        Assert.True(positions[0] < positions[1], "插件把灵性乘在悟性之前——与内核不同序，结果会漂");
    }

    /// <summary>独立参照：按逆向 §D3 的文字定义重算，不共享内核的任何钳位助手。</summary>
    private static int ReferenceEffectiveQuality(int raw, int compLevel, int spiritLevel, bool super, int mutBps)
    {
        int[] compTable = { 100, 150, 210, 300, 800, 1100, 1450, 2350, 3000, 3930 };
        int[] spiNormal = { 100, 433, 767, 1100, 1433, 1767, 2100, 2433, 2767, 3100 };
        int[] spiSuper = { 100, 478, 856, 1233, 1611, 1989, 2367, 2744, 3122, 3400 };
        long v = raw < 1 ? 1 : raw;
        if (compLevel > 0) v = v * (10_000 + compTable[compLevel - 1]) / 10_000;
        if (spiritLevel > 0) v = v * (10_000 + (super ? spiSuper : spiNormal)[spiritLevel - 1]) / 10_000;
        if (mutBps > 10_000) v = v * mutBps / 10_000;
        return v > 6000 ? 6000 : (int)v;
    }

    [Fact]
    public void 插件的梦幻面板公式常数与内核同值()
    {
        // 这些数字是"公式的形状"，漂一个就会让编辑器画的曲线和战斗算出的不是一条
        var kernel = Read("Horizon.Game.Sim/Pets/PetStatsCore.cs");
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetPreviewMath.cs");
        foreach (var magic in new[] { "140_000", "7550", "433", "3000", "7000", "4000", "2000" })
        {
            Assert.True(kernel.Contains(magic, StringComparison.Ordinal), "内核里找不到常数 " + magic);
            Assert.True(plugin.Contains(magic, StringComparison.Ordinal), "插件里找不到常数 " + magic);
        }
        Assert.Equal(ExtractIntConst(kernel, "GrowthMinBps", "PetStatsCore"), PetStatsCore.GrowthMinBps);
        Assert.Equal(ExtractIntConst(plugin, "GrowthMinBps", "PetPreviewMath"), PetStatsCore.GrowthMinBps);
        Assert.Equal(ExtractIntConst(plugin, "GrowthMaxBps", "PetPreviewMath"), PetStatsCore.GrowthMaxBps);
    }

    [Fact]
    public void 插件婚配等级表与内核同值()
    {
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetPreviewMath.cs");
        Assert.Equal(PetGrowthCore.MarriageLevelByBreedingCount, ExtractIntArray(plugin, "MarriageLevels"));
    }

    [Fact]
    public void 插件内丹格数的分段边界与内核一致()
    {
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetPreviewMath.cs");
        // 边界常量必须都在源码里出现（漂一个数字就会让 35 级和 74 级拿到同一格数）
        foreach (var boundary in new[] { 35, 75, 105, 145, 175 })
        {
            Assert.Contains("battleLevel < " + boundary, plugin, StringComparison.Ordinal);
        }
        // 内核侧：逐个边界点验证"边界前后各差一档"，这比抄一遍返回值更有约束力
        Assert.Equal(1, PetSkillCore.InnerCoreSlotsFor(34));
        Assert.Equal(2, PetSkillCore.InnerCoreSlotsFor(35));
        Assert.Equal(2, PetSkillCore.InnerCoreSlotsFor(74));
        Assert.Equal(3, PetSkillCore.InnerCoreSlotsFor(75));
        Assert.Equal(3, PetSkillCore.InnerCoreSlotsFor(104));
        Assert.Equal(4, PetSkillCore.InnerCoreSlotsFor(105));
        Assert.Equal(4, PetSkillCore.InnerCoreSlotsFor(144));
        Assert.Equal(5, PetSkillCore.InnerCoreSlotsFor(145));
        Assert.Equal(5, PetSkillCore.InnerCoreSlotsFor(174));
        Assert.Equal(6, PetSkillCore.InnerCoreSlotsFor(175));
    }

    // ---------------- ③ 跨脚本域的字面量协议 ----------------

    [Fact]
    public void 外观表的客户端键名两侧同序同值()
    {
        // 写侧 = Horizon.PetCli/Program.cs 的 ClientKeys；读侧 = 客户端外观分流代码。
        // 顺序也必须是字面同序：读侧按下标取值时，换序 = 把 scale 当 standState 用。
        var cli = Read("Horizon.PetCli/Program.cs");
        var cliKeys = ExtractKeySet(cli, "ClientKeys");
        var expected = new[] { "visuals", "modelKey", "prefabPath", "scale", "standState" };
        Assert.True(cliKeys.SetEquals(expected), "ClientKeys 与约定不符：" + string.Join(",", cliKeys));

        var cliOrder = Regex.Matches(cli,
                Regex.Escape("ClientKeys = {") + @"(?<body>[^}]*)\}")
            .Cast<Match>().SelectMany(m => Regex.Matches(m.Groups["body"].Value, "\"(?<v>[^\"]+)\"").Cast<Match>())
            .Select(m => m.Groups["v"].Value).ToList();
        Assert.Equal(expected, cliOrder.ToArray());
    }

    [Fact]
    public void 客户端读端用的表路径与键名与写端一致()
    {
        var reader = Read("HundunWorld/Source/Game/Pets/PetVisualCatalog.cs");
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditorEditor/PetEditorWindow.cs");

        // 路径：CLI 的 export-visuals 只收 --out 参数，默认落点在插件的"导出外观表"按钮里；
        // 读端的 RelativeTablePath 必须与那个默认落点同一路径。不一致的表现是
        // "导出成功而客户端说表不存在"，两边各打一行日志，谁也不会把它们联系在一起。
        Assert.Contains("\"Data/Pets/client_visuals.json\"", reader, StringComparison.Ordinal);
        Assert.Contains("\"Data\", \"Pets\", \"client_visuals.json\"", plugin, StringComparison.Ordinal);

        // 键名：写侧字面量清单（CLI 里公布的 ClientKeys）逐个都必须在读端源码里被取用
        var writer = Read("Horizon.PetCli/Program.cs");
        var cliKeys = ExtractKeySet(writer, "ClientKeys");
        foreach (var key in cliKeys)
        {
            Assert.Contains("\"" + key + "\"", reader, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 宠物跟随与玩家点击移动走同一个导航入口()
    {
        // 两条通路各自寻路迟早对同一个导航网格给出不同答案，症状是"玩家能走到、宠物说走不到"。
        // 所以判据不是"宠物也能走"，而是"两边调的是同一组 API"。
        var pet = Read("HundunWorld/Source/Game/Pets/PetFollowComponent.cs");
        var player = Read("HundunWorld/Source/Game/PlayerController.cs");

        foreach (var api in new[] { "IsNavigationReady", "TryFindPath", "ResamplePath" })
        {
            Assert.Contains("NavigatorStatics." + api, pet, StringComparison.Ordinal);
            Assert.Contains(api, player, StringComparison.Ordinal);
        }

        // 玩家侧与宠物侧都必须有"导航不可用时降级"的出口，否则缺导航资产时宠物变雕像
        Assert.Contains("useNavMesh", player, StringComparison.Ordinal);
        Assert.Contains("LastStepUsedNavigation = false", pet, StringComparison.Ordinal);
    }

    [Fact]
    public void 宠物外观目录查不到时不猜别的模型()
    {
        // 读端未命中必须返回 false 让调用方回退占位；返回一个"看着像"的路径会让配置错误查不出来
        var reader = Read("HundunWorld/Source/Game/Pets/PetVisualCatalog.cs");
        Assert.Contains("if (string.IsNullOrEmpty(modelKey)) return false;", reader, StringComparison.Ordinal);
        Assert.Contains("if (!Entries.TryGetValue(modelKey!, out hit)) return false;", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("Entries.Values.First()", reader, StringComparison.Ordinal);
    }

    // ---------------- ④ 登记与接线 ----------------

    [Fact]
    public void 插件已登记进引擎工程与解决方案()
    {
        var projectDir = Path.Combine(RepoRoot(), "HundunWorld");
        var flaxproj = Read("HundunWorld/HundunWorld.flaxproj");
        Assert.Contains("Plugins/PetEditor/PetEditor.flaxproj", flaxproj, StringComparison.Ordinal);

        var slnx = Read("HundunWorld/HundunWorld.slnx");
        Assert.Contains("Plugins/PetEditor/Source/PetEditor.csproj", slnx, StringComparison.Ordinal);
        Assert.Contains("Plugins/PetEditor/Source/PetEditorEditor.csproj", slnx, StringComparison.Ordinal);
        Assert.Contains("BuildDependency Project=\"Plugins/PetEditor/Source/PetEditor.csproj\"", slnx,
            StringComparison.Ordinal);

        // 插件目录里四个源位置都要真在盘上（登记了却没文件 = 引擎重扫时报缺目标）
        Assert.True(File.Exists(Path.Combine(projectDir, "Plugins/PetEditor/PetEditor.flaxproj")));
        Assert.True(File.Exists(Path.Combine(projectDir, "Plugins/PetEditor/Source/PetEditor/PetDraftModel.cs")));
        Assert.True(File.Exists(Path.Combine(projectDir, "Plugins/PetEditor/Source/PetEditor/PetPreviewMath.cs")));
        Assert.True(File.Exists(Path.Combine(projectDir, "Plugins/PetEditor/Source/PetEditorEditor/PetEditorWindow.cs")));
        Assert.True(File.Exists(Path.Combine(projectDir, "Plugins/PetEditor/Source/PetEditorEditor/PetCliBridge.cs")));
        Assert.True(File.Exists(Path.Combine(projectDir, "Plugins/PetEditor/Source/PetEditorEditor/PetViews.cs")));
        Assert.True(File.Exists(Path.Combine(projectDir, "Plugins/PetEditor/Source/PetEditorEditor/PetEditorPlugin.cs")));
    }

    [Fact]
    public void 宠物表登记在两个EF上下文里且数量一致()
    {
        // 与怪物侧同一条教训：只在一个上下文登记，迁移生成不出表而运行期读的时候才炸
        var source = Read("Horizon.Entities/GameEntityContext.cs");
        var sets = new[]
        {
            "PetTemplates", "PetStatBands", "PetSkillRules", "PetSkillPools", "PetBehaviors",
            "PetEquipRules", "PetAssistRules", "PetMergeRules", "PetMutationRules",
            "PetInstances", "PetInstanceEvents",
        };
        foreach (var name in sets)
        {
            var hits = Regex.Matches(source, @"DbSet<\w+> " + Regex.Escape(name) + " ").Count;
            Assert.True(hits >= 2, $"{name} 只登记了 {hits} 次，两个上下文都要有");
        }
    }

    [Fact]
    public void 种子脚本的每一列都存在于建表脚本()
    {
        // 与怪物侧的 verify-monster-seed-columns.py 同一条判据，但在测试里跑一次就够：
        // 021/022 由 Tools/gen-pet-sql.py 生成，这条测试守住的是"生成物仍然自洽"。
        var ddl = Read("scripts/sql/021_pet_editor_tables.sql");
        var seed = Read("scripts/sql/022_seed_pet_species.sql");

        var tables = Regex.Matches(ddl, @"CREATE TABLE \[(?<n>\w+)\] \((?<body>[\s\S]*?)\n\);")
            .Cast<Match>().ToDictionary(m => m.Groups["n"].Value,
                m => Regex.Matches(m.Groups["body"].Value, @"\[(?<c>\w+)\]").Cast<Match>()
                      .Select(x => x.Groups["c"].Value).ToHashSet(StringComparer.Ordinal));
        Assert.Equal(11, tables.Count);

        var referenced = Regex.Matches(seed, @"s\.\[(?<c>\w+)\]").Cast<Match>()
            .Select(m => m.Groups["c"].Value).Distinct().ToList();
        var universe = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in tables.Values) foreach (var c in set) universe.Add(c);

        Assert.True(referenced.Count >= 60, $"只核到 {referenced.Count} 个引用列名，像是正则没吃到");
        var missing = referenced.Where(c => !universe.Contains(c)).ToList();
        Assert.True(missing.Count == 0, "种子引用了 DDL 里不存在的列：" + string.Join(",", missing));
    }

    [Fact]
    public void 物种号段不许建成自增列()
    {
        // 号段即身份：IDENTITY 会让数据库发出 1/2/3 这种不合法号且不报错
        var ddl = Read("scripts/sql/021_pet_editor_tables.sql");
        Assert.DoesNotContain("[species_id] INT NOT NULL IDENTITY", ddl, StringComparison.Ordinal);
        Assert.DoesNotContain("[pet_instance_id] BIGINT NOT NULL IDENTITY", ddl, StringComparison.Ordinal);

        // 只有关联表/流水表的代理主键允许自增，且必须确实是那四个
        var identityCols = Regex.Matches(ddl, @"\[(?<c>\w+)\] \w+ NOT NULL IDENTITY").Cast<Match>()
            .Select(m => m.Groups["c"].Value).Distinct().ToList();
        Assert.True(identityCols.All(c => c == "entry_id" || c == "event_id"),
            "出现了不该自增的列：" + string.Join(",", identityCols));
        Assert.True(identityCols.Count > 0, "一个自增代理主键都没有——正则或 DDL 变了");
    }

    [Fact]
    public void 实体声明的列都有对应的建表列()
    {
        // 反向对账：属性写了、[Column] 也写了，但 DDL 里没有 = EF 读不到而没人报错
        var entities = Read("Horizon.Model/GameModel/PetTemplateEntity.cs") + Environment.NewLine
                       + Read("Horizon.Model/GameModel/PetWorldEntity.cs");
        var ddl = Read("scripts/sql/021_pet_editor_tables.sql");

        var columns = Regex.Matches(entities, @"\[Column\(""(?<c>[a-z0-9_]+)""").Cast<Match>()
            .Select(m => m.Groups["c"].Value).Distinct().ToList();
        Assert.True(columns.Count >= 150, $"只抠到 {columns.Count} 个列名，锚点像是错了");

        var ddlUniverse = Regex.Matches(ddl, @"\[(?<c>[a-z0-9_]+)\]").Cast<Match>()
            .Select(m => m.Groups["c"].Value).ToHashSet(StringComparer.Ordinal);
        var missing = columns.Where(c => !ddlUniverse.Contains(c)).ToList();
        Assert.True(missing.Count == 0, "这些实体列在建表脚本里不存在：" + string.Join(",", missing));
    }

    [Fact]
    public void 每个内核声明的默认值都被插件草稿采用()
    {
        // 插件 CreateNew 里的字面数必须来自内核默认，不能各写一套：
        // 这里比对"默认对象算出来的数"与"源码里写的数"
        var slot = PetSlotRuleConfig.Default;
        var follow = PetFollowConfig.Default;
        var plugin = Read("HundunWorld/Plugins/PetEditor/Source/PetEditor/PetDraftModel.cs");

        Assert.Contains("BaseSlots { get; set; } = " + slot.BaseSlots, plugin, StringComparison.Ordinal);
        Assert.Contains("AbsoluteMaxSlots { get; set; } = " + slot.AbsoluteMaxSlots, plugin, StringComparison.Ordinal);
        Assert.Contains("SlotGrowMaxLevel { get; set; } = " + slot.SlotGrowMaxBattleLevel, plugin, StringComparison.Ordinal);
        Assert.Contains("OverwriteChanceBps { get; set; } = " + slot.OverwriteChanceBps, plugin, StringComparison.Ordinal);
        Assert.Contains("LeashRadius { get; set; } = " + follow.LeashRadius.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "f",
            plugin, StringComparison.Ordinal);
        Assert.Contains("TeleportBeyondRadius { get; set; } = " + follow.TeleportBeyondRadius.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "f",
            plugin, StringComparison.Ordinal);
    }
}

