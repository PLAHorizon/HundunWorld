using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Horizon.Game.Sim.Items;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 物品编辑器的**跨宿主协议对账**：Flax 插件的脚本域引不到仓库工程，所以草稿字段名、
/// 显示表与内核常量在插件里各有一份字面副本。这份副本不会编译失败，只会在运行时静默丢字段或
/// 显示与内核不一致的数值——因此两侧的等价性必须由测试按**源文本**断言。
/// <para>
/// 为什么不是"读一侧调另一侧"：插件程序集不能被 xUnit 引用（它由 Flax.Build 产出、
/// 引用集是引擎给的那一套），所以这里只能读源文件、抠字面量。
/// </para>
/// </summary>
public class ItemEditorContractTests
{
    private readonly ITestOutputHelper _log;

    public ItemEditorContractTests(ITestOutputHelper log) => _log = log;

    private const string CliDraftPath = "Horizon.ItemCli/ItemDraft.cs";
    private const string PluginDraftPath = "HundunWorld/Plugins/ItemEditor/Source/ItemEditor/ItemDraftModel.cs";
    private const string PluginMathPath = "HundunWorld/Plugins/ItemEditor/Source/ItemEditor/ItemPreviewMath.cs";
    private const string PluginWindowPath = "HundunWorld/Plugins/ItemEditor/Source/ItemEditorEditor/ItemEditorWindow.cs";
    private const string OverlayPath = "HundunWorld/Source/Game/Inventory/ItemCatalogOverlay.cs";
    private const string StorePath = "Horizon.ItemCli/ItemStore.cs";

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

    /// <summary>从源文本里抠出一个字符串数组字面量的元素集合（<c>new string[] { … }</c> 与 <c>{ … }</c> 都吃）。</summary>
    private static List<string> ExtractStringList(string source, string variableName)
    {
        var match = Regex.Match(source,
            Regex.Escape(variableName) + @"\s*=\s*(?:new\s+(?:string|System\.Collections\.Generic\.List)<string>\s*)?\{(?<body>[^}]*)\}");
        Assert.True(match.Success, $"在源文本里找不到 {variableName} 的数组字面量（改名或换写法会让本断言变成空跑）");
        var items = Regex.Matches(match.Groups["body"].Value, "\"(?<v>[^\"]+)\"")
            .Cast<Match>().Select(m => m.Groups["v"].Value).ToList();
        Assert.True(items.Count > 0, $"{variableName} 抠出来是 0 个元素——尺子错了");
        return items;
    }

    private static HashSet<string> ExtractKeySet(string source, string variableName)
        => new(ExtractStringList(source, variableName), StringComparer.Ordinal);

    /// <summary>抠出一个 int 数组字面量（分隔下划线先去掉，否则 10_000 会被抠成 10 和 000）。</summary>
    private static int[] ExtractIntArray(string source, string variableName)
    {
        var match = Regex.Match(source,
            Regex.Escape(variableName) + @"\s*=\s*(?:new\s+int\[\]\s*)?\{(?<body>[^}]*)\}");
        Assert.True(match.Success, $"找不到 int[] 字面量 {variableName}");
        return Regex.Matches(match.Groups["body"].Value, @"-?[\d_]+").Cast<Match>()
            .Select(m => int.Parse(m.Value.Replace("_", string.Empty))).ToArray();
    }

    private static int ExtractIntConst(string source, string name, string fileLabel)
    {
        var match = Regex.Match(source, @"const\s+int\s+" + Regex.Escape(name) + @"\s*=\s*(?<v>[\d_]+)");
        Assert.True(match.Success, $"{fileLabel} 里找不到常量 {name}");
        return int.Parse(match.Groups["v"].Value.Replace("_", string.Empty));
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
        => cliTypeName.EndsWith("Draft", StringComparison.Ordinal)
            ? cliTypeName.Substring(0, cliTypeName.Length - 5) + "Model"
            : cliTypeName;

    // ---------------- ① 草稿字段名：顶层 + 每个子段 ----------------

    [Fact]
    public void 插件草稿的顶层键集合与CLI逐字相同()
    {
        var cli = Read(CliDraftPath);
        var plugin = Read(PluginDraftPath);

        var cliKeys = ExtractKeySet(cli, "KnownTopLevelKeys");
        var pluginKeys = ExtractKeySet(plugin, "KnownTopLevelKeys");

        _log.WriteLine($"顶层键 {cliKeys.Count} 个");
        Assert.True(cliKeys.Count >= 60, $"顶层键数量偏少（{cliKeys.Count}），像是锚点抓错");
        Assert.True(pluginKeys.SetEquals(cliKeys),
            "顶层键不一致 → 缺：" + string.Join(",", pluginKeys.Except(cliKeys).Concat(cliKeys.Except(pluginKeys))));
    }

    [Fact]
    public void 插件草稿每个子段的键集合都与CLI相同()
    {
        var cli = Read(CliDraftPath);
        var plugin = Read(PluginDraftPath);

        var cliCount = Regex.Matches(cli, "public static readonly string\\[\\] KnownKeys").Count;
        var pluginCount = Regex.Matches(plugin, "public static readonly string\\[\\] KnownKeys").Count;
        // 九个段类：属性/伤害/插槽/法术/出处/套装/套装档/词缀/多语言。
        // 注意 KnownSectionKeys 有 11 项（set.tiers.stats 与 affix.stats 复用属性段），所以别再拿 11 当期望值。
        Assert.True(cliCount >= 9, $"CLI 侧只找到 {cliCount} 个子段键集合，预期至少 9");
        Assert.Equal(9, cliCount);
        Assert.Equal(cliCount, pluginCount);

        var sections = new[]
        {
            "ItemStatDraft", "ItemDamageDraft", "ItemSocketDraft", "ItemSpellDraft",
            "ItemSourceDraft", "ItemSetDraft", "ItemSetTierDraft", "ItemAffixDraft",
            "ItemLocalizationDraft",
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
        var plugin = Read(PluginDraftPath);
        var declared = Regex.Matches(plugin, @"public\s+[\w<>?\[\]]+\s+(?<p>\w+)\s*\{\s*get;")
            .Select(m => m.Groups["p"].Value).ToHashSet(StringComparer.Ordinal);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { "KnownTopLevelKeys", "KnownKeys" })
        {
            foreach (var m in Regex.Matches(plugin, Regex.Escape(name) + @"\s*=\s*(?:new[^{]*)?\{(?<body>[^}]*)\}")
                         .Cast<Match>())
            {
                foreach (var k in Regex.Matches(m.Groups["body"].Value, "\"(?<v>[^\"]+)\"").Cast<Match>())
                    keys.Add(k.Groups["v"].Value);
            }
        }

        Assert.True(keys.Count >= 60, $"键总数偏少（{keys.Count}）——锚点抓错了，下面的断言会空跑");
        var missing = keys.Where(k => !declared.Contains(char.ToUpperInvariant(k[0]) + k.Substring(1))).ToList();
        Assert.True(missing.Count == 0, "这些键没有对应属性，反序列化会被静默丢弃：" + string.Join(",", missing));
    }

    // ---------------- ② 新建草稿的默认值必须一致 ----------------

    [Fact]
    public void 新建草稿的默认数值两侧相同()
    {
        var cli = ClassBody(Read(CliDraftPath), "ItemDraft");
        var plugin = ClassBody(Read(PluginDraftPath), "ItemDraftModel");
        var cliCreate = MethodBody(cli, "CreateNew");
        var pluginCreate = MethodBody(plugin, "CreateNew");

        var numericKeys = new[]
        {
            "ItemClass", "Quality", "Rarity", "ItemLevel", "InventoryType", "Element", "BindType",
            "LevelRequire", "MaxStack", "ResistanceElement", "PublishState", "ContentVersion",
        };
        foreach (var key in numericKeys)
        {
            var a = ExtractAssignment(cliCreate, key);
            var b = ExtractAssignment(pluginCreate, key);
            Assert.True(a.Length > 0, "CLI CreateNew 里找不到 " + key);
            Assert.True(b.Length > 0, "插件 CreateNew 里找不到 " + key);
            Assert.Equal(a, b);
        }

        Assert.Contains("Content/Icons/Equipment/DefaultBody.flax", cliCreate);
        Assert.Contains("Content/Icons/Equipment/DefaultBody.flax", pluginCreate);
    }

    private static string MethodBody(string classBody, string methodName)
    {
        var start = classBody.IndexOf(" " + methodName + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到方法 {methodName}");
        var open = classBody.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < classBody.Length; i++)
        {
            if (classBody[i] == '{') depth++;
            else if (classBody[i] == '}')
            {
                depth--;
                if (depth == 0) return classBody.Substring(open, i - open + 1);
            }
        }
        throw new InvalidOperationException("方法括号不匹配：" + methodName);
    }

    private static string ExtractAssignment(string body, string key)
    {
        var match = Regex.Match(body, @"\b" + Regex.Escape(key) + @"\s*=\s*(?<v>-?[\d_]+|true|false)");
        return match.Success ? match.Groups["v"].Value.Replace("_", string.Empty) : string.Empty;
    }

    // ---------------- ③ 插件显示表与内核逐值相同 ----------------

    [Fact]
    public void 插件品质表与内核逐值相同()
    {
        var plugin = Read(PluginMathPath);
        Assert.Equal(ItemCore.QualityNames, ExtractStringList(plugin, "QualityNames"));
        Assert.Equal(ItemCore.QualityColorHexes, ExtractStringList(plugin, "QualityColorHexes"));
        Assert.Equal(ItemCore.QualityMultiplierBpsTable, ExtractIntArray(plugin, "QualityMultiplierBpsTable"));
    }

    [Fact]
    public void 插件部位与大类名表与内核逐值相同()
    {
        var plugin = Read(PluginMathPath);
        Assert.Equal(ItemCore.InventoryTypeNames, ExtractStringList(plugin, "InventoryTypeNames"));
        Assert.Equal(ItemCore.ClassNames, ExtractStringList(plugin, "ClassNames"));
    }

    [Fact]
    public void 插件属性名表与内核逐值相同()
    {
        var plugin = Read(PluginMathPath);
        Assert.Equal(ItemCore.StatTypeNames, ExtractStringList(plugin, "StatTypeNames"));
        Assert.Equal(ItemCore.StatTypeNames.Length, ItemCore.StatTypeNames.Distinct().Count());
    }

    [Fact]
    public void 插件常量与内核相同()
    {
        var plugin = Read(PluginMathPath);
        Assert.Equal(ItemCore.Version, ExtractIntConst(plugin, "CoreVersion", "ItemPreviewMath"));
        Assert.Equal(ItemSpec.MaxItemLevel, ExtractIntConst(plugin, "MaxItemLevel", "ItemPreviewMath"));
        Assert.Equal(ItemCore.MaxRequiredLevel, ExtractIntConst(plugin, "MaxRequiredLevel", "ItemPreviewMath"));
        Assert.Equal(ItemSpec.MaxStatLines, ExtractIntConst(plugin, "MaxStatLines", "ItemPreviewMath"));
        Assert.Equal(ItemSpec.MaxDamageLines, ExtractIntConst(plugin, "MaxDamageLines", "ItemPreviewMath"));
        Assert.Equal(ItemSpec.MaxSockets, ExtractIntConst(plugin, "MaxSockets", "ItemPreviewMath"));
        Assert.Equal(ItemSpec.MaxSpellLines, ExtractIntConst(plugin, "MaxSpellLines", "ItemPreviewMath"));
        Assert.Equal(ItemSpec.MaxStackLimit, ExtractIntConst(plugin, "MaxStackLimit", "ItemPreviewMath"));
    }

    [Fact]
    public void 插件预算与价格公式与内核同形()
    {
        // 插件那份不能执行，只能按源文本确认"关键算式顺序"没被改写：
        // 预算必须是 ilvl*5 + ilvl*ilvl/4 再乘品质系数；售价必须是买价/4。
        var plugin = Read(PluginMathPath);
        Assert.Contains("(long)ilvl * 5L + (long)ilvl * ilvl / 4L", plugin);
        Assert.Contains("QualityMultiplierBps(quality) / 10_000L", plugin);
        Assert.Contains("Math.Max(1, buyPrice / 4)", plugin);
        Assert.Contains("(shield ? 4 : 3) / 2", plugin);

        var kernel = Read("Horizon.Game.Sim/Items/ItemCore.cs");
        Assert.Contains("(long)ilvl * 5L + (long)ilvl * ilvl / 4L", kernel);
        Assert.Contains("QualityMultiplierBps(quality) / 10_000L", kernel);
        Assert.Contains("Math.Max(1, buyPrice / 4)", kernel);
        Assert.Contains("(shield ? 4 : 3) / 2", kernel);
    }

    // ---------------- ④ 客户端覆盖层认的键都真的被读 ----------------

    [Fact]
    public void 覆盖层读取的键覆盖导出器声明的全部键()
    {
        var keys = ExtractStringList(Read(StorePath), "ClientKeys");
        var overlay = Read(OverlayPath);
        Assert.True(keys.Count >= 10, $"ClientKeys 只有 {keys.Count} 个，锚点抓错");

        var missing = keys.Where(k => !overlay.Contains("\"" + k + "\"")).ToList();
        Assert.True(missing.Count == 0,
            "导出器声明了这些键但覆盖层没读（跨进程协议会静默丢字段）：" + string.Join(",", missing));
    }

    [Fact]
    public void 读库时老种子的属性JSON不会被抹掉()
    {
        // 老物品只有 base_attributes JSON，没有属性行。读草稿必须回退解析 JSON，
        // 否则"打开再保存"会把一件有属性的装备静默清空。
        var store = Read(StorePath);
        Assert.Contains("ItemCore.ParseAttributeJson(row.BaseAttributes)", store);
        Assert.Contains("ItemCore.BuildAttributeJson(spec.Stats)", store);
    }

    // ---------------- ⑤ 编辑器页签与设计一致 ----------------

    [Fact]
    public void 草稿每个可编辑的标量键都有写回点()
    {
        // 一次真实缺陷：`subType` 有控件、但 CommitAll 里没赋值（改了不生效，且没有任何报错）。
        // 这里对**每个顶层键**问一句"窗口里有没有 d.<Pascal> = "。
        // 排除项都写明理由，不能只是"测试不方便"：
        //  · publishState / contentVersion：由发布动词与 store 管理，草稿编辑不直接改；
        //  · localizations：本期只有表与 CLI，没有编辑面板（设计 §6.2 未列该页签）。
        var window = Read(PluginWindowPath);
        var draft = Read(CliDraftPath);
        var keys = ExtractStringList(draft, "KnownTopLevelKeys");
        var nonEditable = new HashSet<string>(StringComparer.Ordinal)
        {
            "publishState", "contentVersion", "localizations",
        };

        var missing = new List<string>();
        foreach (var key in keys)
        {
            if (nonEditable.Contains(key)) continue;
            var property = char.ToUpperInvariant(key[0]) + key.Substring(1);
            if (!Regex.IsMatch(window, @"\bd\." + Regex.Escape(property) + @"\s*="))
                missing.Add(key);
        }

        Assert.True(missing.Count == 0,
            "这些键在编辑器里没有写回点（改了不生效）：" + string.Join(",", missing));
        Assert.True(keys.Count >= 60, $"键集合只有 {keys.Count} 个，锚点可能抓错");
    }

    [Fact]
    public void 物品编辑器有十二个页签()
    {
        var window = Read(PluginWindowPath);
        var tabs = ExtractStringList(window, "TabTitles");
        Assert.Equal(12, tabs.Count);
        Assert.Contains("基础", tabs);
        Assert.Contains("属性", tabs);
        Assert.Contains("插槽", tabs);
        Assert.Contains("词缀", tabs);
        Assert.Contains("套装", tabs);
        Assert.Contains("出处", tabs);
    }

    // ---------------- ⑥ 插件必须在主工程引用清单里，否则 Tools 菜单根本不出现 ----------------

    [Fact]
    public void 插件已在主工程引用清单里()
    {
        // 真实故障（2026-10-27）：源码齐、离线 csc 全绿，但编辑器 Tools 里没有"物品编辑器"。
        // 根因是 HundunWorld.flaxproj 的 References 是**显式清单**，插件不在里面就永远不会被发现；
        // 再叠加"没有 Binaries 产物"。这条闸门只看引用，Binaries 属构建产物（不入库）。
        var project = Read("HundunWorld/HundunWorld.flaxproj");
        Assert.Contains("Plugins/ItemEditor/ItemEditor.flaxproj", project);
        Assert.Contains("\"Name\": \"$(ProjectPath)/Plugins/ItemEditor/ItemEditor.flaxproj\"", project);
    }

    [Fact]
    public void 工具条按钮宽度必须显式给()
    {
        // 真实故障：Button(x, y, 0f, h) 在 Flax 里不会按文字撑开，固定 x + 0 宽会让
        // "刷新目录/保存到库"压到下一个按钮上（截图可见）。这条闸门逐条读 BuildToolbar 的宽度实参。
        var window = Read(PluginWindowPath);
        var body = MethodBody(ClassBody(window, "ItemEditorWindow"), "BuildToolbar");
        var widths = Regex.Matches(body, @"MakeToolButton\(ref x,\s*[\d.]+f,\s*(?<w>[\d.]+)f,")
            .Cast<Match>().Select(m => float.Parse(m.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.True(widths.Count >= 15, $"只找到 {widths.Count} 个工具按钮，锚点可能抓错");
        Assert.All(widths, w => Assert.True(w > 0f, "工具按钮宽度必须 > 0（0 宽不会按文字撑开）"));
        Assert.DoesNotContain(widths, w => w < 40f);
    }
}
