using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;
// 插件的纯数学镜像被原样编进本工程（csproj 的 Linked/MonsterPreviewMath），这里起个别名直接引用类型。
using MonsterPreviewMath = MonsterEditor.MonsterPreviewMath;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 怪物编辑器的 JSON 契约闸门：Flax 插件侧镜像模型与 CLI 侧模型的键集合必须逐字相同。
/// <para>
/// <b>为什么用源码文本比对</b>：插件的模型与界面是 Flax 脚本工程，带着引擎类型，不能被 xUnit 引用；
/// 而字段名一旦漂移（比如 <c>chanceBps</c> 写成 <c>chanceBP</c>），
/// <c>System.Text.Json</c> 会**静默丢弃**它 —— 两侧同为初值时自校验反而全绿，
/// 是最难发现的一类缺陷。文本比对不需要跨装配引用就能拦住它。
/// </para>
/// <para>
/// <b>但文本比对只能证明"名字与常量相同"，证明不了"式子跑得出同一个数"</b>：
/// 所以插件里那份<b>不引用引擎类型</b>的纯数学镜像（<c>MonsterPreviewMath.cs</c>）由 csproj
/// 原样链接进来，见「插件镜像的曲线函数被真的调用并与内核逐点同数」那条。
/// 镜像文件里出现引擎类型就会让那条链接失败——这是刻意的：它必须保持可执行。
/// </para>
/// <para>
/// <b>空转防护</b>：断言"解析到的键数量"必须落在下限之上。扫不到东西的解析器会让所有用例
/// 都变成"比较两个空集合"，那就是永绿的假闸门。
/// </para>
/// </summary>
[Collection("monster-content")]
public class MonsterEditorContractTests
{
    private readonly ITestOutputHelper _log;

    public MonsterEditorContractTests(ITestOutputHelper log) => _log = log;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static readonly string PluginModelPath = Path.Combine(
        "HundunWorld", "Plugins", "MonsterEditor", "Source", "MonsterEditor", "MonsterDraftModel.cs");

    private static readonly string CliModelPath = Path.Combine(
        "Horizon.MonsterCli", "MonsterDraft.cs");

    /// <summary>取 <c>KnownXxxKeys = new ... { "a", "b" }</c> 里的字符串字面量集合，按清单名分组。</summary>
    private static Dictionary<string, HashSet<string>> ParseKeySets(string path)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), path));
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text,
                     @"Known(?<name>\w+)Keys\s*=\s*new[^;]*?\{(?<body>[^}]*)\}",
                     RegexOptions.Singleline))
        {
            var keys = Regex.Matches(m.Groups["body"].Value, "\"(?<k>[^\"]+)\"")
                .Cast<Match>().Select(x => x.Groups["k"].Value)
                .ToHashSet(StringComparer.Ordinal);
            result[m.Groups["name"].Value] = keys;
        }
        return result;
    }

    [Fact]
    public void 两侧键清单都存在且覆盖面达标()
    {
        var plugin = ParseKeySets(PluginModelPath);
        var cli = ParseKeySets(CliModelPath);
        var pluginKeys = plugin.Values.Sum(v => v.Count);
        var cliKeys = cli.Values.Sum(v => v.Count);
        _log.WriteLine($"插件侧清单 {plugin.Count} 组 / {pluginKeys} 键；CLI 侧 {cli.Count} 组 / {cliKeys} 键");

        // 下限：顶层 + 8 类子对象，至少 110 个键（实际约 120）。低于此说明解析器什么都没抓到。
        Assert.True(plugin.Count >= 9, $"插件侧只解析到 {plugin.Count} 组键清单");
        Assert.True(cli.Count >= 9, $"CLI 侧只解析到 {cli.Count} 组键清单");
        Assert.True(pluginKeys >= 100, $"插件侧键总数 {pluginKeys} 低于 100 的下限");
        Assert.True(cliKeys >= 100, $"CLI 侧键总数 {cliKeys} 低于 100 的下限");
    }

    [Fact]
    public void 每一组同名清单的键集合逐字相同()
    {
        var plugin = ParseKeySets(PluginModelPath);
        var cli = ParseKeySets(CliModelPath);

        var shared = plugin.Keys.Intersect(cli.Keys).ToList();
        Assert.True(shared.Count >= 9, $"同名清单只有 {shared.Count} 组：两侧命名已经分叉");

        var diffs = new List<string>();
        foreach (var name in shared)
        {
            var onlyPlugin = plugin[name].Except(cli[name]).ToList();
            var onlyCli = cli[name].Except(plugin[name]).ToList();
            if (onlyPlugin.Count == 0 && onlyCli.Count == 0) continue;
            diffs.Add($"[{name}] 仅插件侧: {string.Join(",", onlyPlugin)}；仅 CLI 侧: {string.Join(",", onlyCli)}");
        }
        Assert.Empty(diffs);

        // 两侧都必须带顶层清单，且顶层里必须含"六个复合对象"的名字——漏一个就是"配了没人读"的入口
        foreach (var side in new[] { plugin, cli })
        {
            var top = side["TopLevel"];
            foreach (var required in new[] { "stats", "behavior", "xp", "skills", "lootBindings", "patrolRoute", "spawnPoints" })
                Assert.Contains(required, top);
        }
    }

    [Fact]
    public void 序列化出的字段名与清单一致_防止属性改名而清单没跟()
    {
        // CLI 侧可当真序列化：模型属性名 → camelCase 必须正好等于顶层清单。
        var draft = new Horizon.MonsterCli.MonsterDraft();
        var json = draft.ToJson();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var produced = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var declared = Horizon.MonsterCli.MonsterDraft.KnownTopLevelKeys;

        // 序列化会省略 null（WhenWritingNull），所以只断言"产出的都在清单里"，反向单独查
        var extras = produced.Except(declared).ToList();
        Assert.Empty(extras);

        // 清单里必须有产出里看不到的项，否则这份断言只是"和自己比"：
        // prefabGuid / patrolRoute 在默认草稿里是 null，会被省略。
        var omitted = declared.Except(produced).ToList();
        Assert.Contains("prefabGuid", omitted);
        Assert.Contains("patrolRoute", omitted);
    }

    [Fact]
    public void 能力位覆盖是三态而不是布尔()
    {
        // null=跟档位 / 0=强制关 / 1=强制开。用 bool? 会丢"跟档位"这一档，
        // 于是"这只小怪会求援但不会技能"配不出来（逆向文档 §9）。
        var draft = new Horizon.MonsterCli.MonsterDraft();
        var behaviorType = draft.Behavior.GetType();
        foreach (var name in new[] { "FlagUseThreat", "FlagUseSkills", "FlagUseNavMesh", "FlagUseAssist", "FlagUseTactics" })
        {
            var prop = behaviorType.GetProperty(name);
            Assert.NotNull(prop);
            Assert.Equal(typeof(int?), prop!.PropertyType);
            Assert.Null(prop.GetValue(draft.Behavior));    // 默认必须真的是"跟档位"
        }
    }

    [Fact]
    public void 校验器会拒掉静默丢字段级别的坏草稿()
    {
        // 这条断言的作用：证明校验器不是"永远返回通过"的空壳——先造一个必被拒的草稿，
        // 再证明**合法**草稿能通过（拒绝分支要有正向同夹具对照）。
        var good = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100001, Name = "对照" };
        good.Behavior.LeashRadius = 30;
        good.Behavior.AggroRadius = 10;
        var goodResult = Horizon.MonsterCli.MonsterValidator.Validate(good, null);
        Assert.True(goodResult.Ok, "合法草稿被拒：" + string.Join(";", goodResult.Errors));

        var bad = new Horizon.MonsterCli.MonsterDraft { MonsterId = 0, Name = "  " };
        bad.Behavior.LeashRadius = 5;
        bad.Behavior.AggroRadius = 20;      // 脱战半径 < 索敌半径
        bad.Stats.HpBase = 0;
        bad.Xp.XpBase = 0;
        bad.Xp.XpPerLevel = 0;
        var badResult = Horizon.MonsterCli.MonsterValidator.Validate(bad, null);
        Assert.False(badResult.Ok);
        Assert.Contains(badResult.ErrorPaths, p => p == "name");
        Assert.Contains(badResult.ErrorPaths, p => p == "monsterId");
        Assert.Contains(badResult.ErrorPaths, p => p == "behavior.leashRadius");
        Assert.Contains(badResult.ErrorPaths, p => p == "xp");
    }

    [Fact]
    public void 尸体拾取两包的编号是尾部追加且能往返()
    {
        // 2026-10-28 拍板：尸体拾取。两个包的编号钉死——插到中间会让新旧两端把请求当结果。
        Assert.Equal(17, (int)Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeRequest);
        Assert.Equal(18, (int)Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeResult);

        var request = new Horizon.Game.Message.Sync.CorpseLootTakeRequestPacket
        {
            Kind = Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeRequest,
            CorpseEntityId = 0xDEADBEEFCAFEUL,
            SlotIndex = 2,
        };
        var bytes = MemoryPack.MemoryPackSerializer.Serialize<Horizon.Game.Message.Sync.SyncPacket>(request);
        var back = MemoryPack.MemoryPackSerializer.Deserialize<Horizon.Game.Message.Sync.SyncPacket>(bytes);
        var typed = Assert.IsType<Horizon.Game.Message.Sync.CorpseLootTakeRequestPacket>(back);
        Assert.Equal(0xDEADBEEFCAFEUL, typed.CorpseEntityId);
        Assert.Equal(2, typed.SlotIndex);
        Assert.Equal(Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeRequest, typed.Kind);

        var result = new Horizon.Game.Message.Sync.CorpseLootTakeResultPacket
        {
            Kind = Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeResult,
            CorpseEntityId = 0xDEADBEEFCAFEUL,
            SlotIndex = 2,
            Status = 1,
            ItemId = 555,
            Count = 2,
            Money = 0,
        };
        var resultBack = MemoryPack.MemoryPackSerializer.Deserialize<Horizon.Game.Message.Sync.SyncPacket>(
            MemoryPack.MemoryPackSerializer.Serialize<Horizon.Game.Message.Sync.SyncPacket>(result));
        var typedResult = Assert.IsType<Horizon.Game.Message.Sync.CorpseLootTakeResultPacket>(resultBack);
        Assert.Equal(1, typedResult.Status);
        Assert.Equal(555, typedResult.ItemId);
        Assert.Equal(2, typedResult.Count);
        Assert.Equal(2, typedResult.SlotIndex);
    }

    [Fact]
    public void 拾取窗口两格按拍板口径校验且缺省就是AC的数()
    {
        // 2026-10-28 拍板：窗口 60 秒 / 距离 5.5 米（AC 缺省），且两格可逐怪配置。
        // 三条口径必须在同一处说清：缺省值 = AC；距离 0 = **非法**（关拾取请用窗口 0）；窗口 0 = 关掉，只 Warn。
        Assert.Equal(60f, Horizon.Game.Sim.Monsters.MonsterCorpseCore.DefaultLootWindowSeconds);
        Assert.Equal(5.5f, Horizon.Game.Sim.Monsters.MonsterCorpseCore.DefaultPickupDistanceMeters);

        var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100002, Name = "拾取对照" };
        draft.Behavior.LeashRadius = 30;
        draft.Behavior.AggroRadius = 10;
        // 缺省草稿的两个值就是 AC 的数（跨层：内容列缺省 / 编辑器初值 / 内核默认都取这两个数）
        Assert.Equal(60.0, draft.LootWindowSeconds);
        Assert.Equal(5.5, draft.PickupDistanceMeters);
        var ok = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.True(ok.Ok, "缺省拾取配置被拒：" + string.Join(";", ok.Errors));

        // 距离 0：Error（不是"关掉"）——与索敌半径 0 同一口径
        var zeroDistance = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100003, Name = "距离零" };
        zeroDistance.Behavior.LeashRadius = 30;
        zeroDistance.Behavior.AggroRadius = 10;
        zeroDistance.PickupDistanceMeters = 0;
        var rejected = Horizon.MonsterCli.MonsterValidator.Validate(zeroDistance, null);
        Assert.False(rejected.Ok, "距离 0 被放过了：它会静默变成'谁都够不着'");
        Assert.Contains(rejected.ErrorPaths, p => p == "pickupDistanceMeters");

        // 窗口 0：合法，但必须有 Warn（这是"明确关掉拾取"，不是配错）
        var windowOff = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100004, Name = "关拾取" };
        windowOff.Behavior.LeashRadius = 30;
        windowOff.Behavior.AggroRadius = 10;
        windowOff.LootWindowSeconds = 0;
        var warned = Horizon.MonsterCli.MonsterValidator.Validate(windowOff, null);
        Assert.True(warned.Ok, "窗口 0 被当成错：" + string.Join(";", warned.Errors));
        Assert.Contains(warned.Warnings, w => w.Contains("lootWindowSeconds"));

        // 窗口比尸体停留还长：合法，但要 Warn（尸体先没了，超出的窗口永远用不到）
        var longerThanCorpse = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100005, Name = "窗口超尸体" };
        longerThanCorpse.Behavior.LeashRadius = 30;
        longerThanCorpse.Behavior.AggroRadius = 10;
        longerThanCorpse.CorpseSeconds = 30;
        longerThanCorpse.LootWindowSeconds = 90;
        var tooLong = Horizon.MonsterCli.MonsterValidator.Validate(longerThanCorpse, null);
        Assert.True(tooLong.Ok);
        Assert.Contains(tooLong.Warnings, w => w.Contains("lootWindowSeconds"));
    }

    [Fact]
    public void 校验器的截断上限两边都量过一遍()
    {
        // "最多 N 行"这类常量：源码读一个数、数据算一个需求量，断言上限 ≥ 需求，
        // 并把实测差值打印出来（注释里的"约"不是证据）。
        var skillCap = Horizon.MonsterCli.MonsterValidator.MaxSkillMounts;
        var windowRows = 8;         // 插件窗口一次摆 8 行
        Assert.True(skillCap >= windowRows * 2, $"技能上限 {skillCap} 只够窗口 {windowRows} 行的一倍不到");
        _log.WriteLine($"技能上限={skillCap} 窗口行={windowRows} 余量={skillCap - windowRows}");
        _log.WriteLine($"掉落绑定上限={Horizon.MonsterCli.MonsterValidator.MaxLootBindings} 逐件上限={Horizon.MonsterCli.MonsterValidator.MaxDropEntries} " +
                       $"路线点上限={Horizon.MonsterCli.MonsterValidator.MaxRoutePoints} 同存活上限={Horizon.MonsterCli.MonsterValidator.MaxAliveLimit}");

        // 等级上界必须与经验/难度内核一致，否则"301 级怪"能存进库而曲线在那儿没有定义
        Assert.Equal(Horizon.Game.Sim.Monsters.MonsterDifficultyCore.MaxPlayerLevel,
                     Horizon.MonsterCli.MonsterValidator.MaxLevel);
    }

    /// <summary>
    /// 把源码里的 <c>int[] Name = { 1, 2, 3 };</c> 字面数组读出来（插件程序集不能被测试工程引用，
    /// 但它的数据表是字面量，文本层就足够对账）。
    /// </summary>
    private static Dictionary<string, int[]> ParseIntArrayTables(string path)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), path));
        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text,
                     @"int\[\]\s*(?<name>\w+)\s*=\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline))
        {
            var values = Regex.Matches(m.Groups["body"].Value, @"-?\d+")
                .Cast<Match>().Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
            result[m.Groups["name"].Value] = values;
        }
        return result;
    }

    /// <summary>把 "212f / 255f, 196f / 255f, 160f / 255f" 或 "0.72f, 0.74f, 0.78f" 解析成三个 0..1 分量。</summary>
    private static List<float> ParseRgbTokens(string text)
    {
        // "212f / 255f" 里 'f' 后面还跟着空格，所以必须**先 Trim 再削尾缀再 Trim**；
        // 顺序写反会拿 "212f " 去 float.Parse，异常信息只说"格式不对"，看不出是这个空格。
        static string Num(string raw) => raw.Trim().TrimEnd('f').Trim();

        var parts = text.Split(',');
        var values = new List<float>();
        foreach (var raw in parts)
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            var slash = token.IndexOf('/');
            if (slash > 0)
            {
                var num = float.Parse(Num(token.Substring(0, slash)), CultureInfo.InvariantCulture);
                var den = float.Parse(Num(token.Substring(slash + 1)), CultureInfo.InvariantCulture);
                values.Add(num / den);
            }
            else
            {
                values.Add(float.Parse(Num(token), CultureInfo.InvariantCulture));
            }
            if (values.Count == 3) break;
        }
        return values;
    }

    [Fact]
    public void 编辑器施法条配色与客户端五行色逐值相同()
    {
        // 插件与客户端互相引用不到，而"色表错位"没有任何一层会报错：
        // 水系怪画成火的颜色，看起来只是审美问题，实际是策划看到的预览与玩家看到的条子各说各话。
        // 所以这里把两边的数字抠出来逐值比（同"镜像分段表"那条闸门的做法）。
        var pluginPath = Path.Combine("HundunWorld", "Plugins", "MonsterEditor", "Source",
            "MonsterEditor", "MonsterPreviewMath.cs");
        var clientPath = Path.Combine("HundunWorld", "Source", "Game", "UI", "StyleSystem", "InkWashTheme.cs");
        var pluginText = File.ReadAllText(Path.Combine(RepoRoot(), pluginPath));
        var clientText = File.ReadAllText(Path.Combine(RepoRoot(), clientPath));

        // 客户端：ElementMetal/Wood/Water/Fire/Earth 五个常量（顺序 = 域号 1..5）
        var domainNames = new[] { "金", "木", "水", "火", "土" };
        var clientFields = new[] { "ElementMetal", "ElementWood", "ElementWater", "ElementFire", "ElementEarth" };
        var clientColors = new List<List<float>>();
        foreach (var field in clientFields)
        {
            var m = Regex.Match(clientText, field + @"\s*=\s*new Color\((?<rgb>[^)]*)\)");
            Assert.True(m.Success, $"客户端源里没解析到 {field}（写法变了，这条闸门正在空转）");
            clientColors.Add(ParseRgbTokens(m.Groups["rgb"].Value));
        }
        Assert.Equal(5, clientColors.Count);

        // 插件：SchoolRgb 数组的每一行（含行尾中文注释，注释才是"这行是给哪一系"的身份）
        var rows = Regex.Matches(pluginText, @"new\[\]\s*\{(?<rgb>[^}]*)\},?\s*//\s*(?<label>[^\r\n]*)")
            .Cast<Match>()
            .Select(m => (rgb: ParseRgbTokens(m.Groups["rgb"].Value), label: m.Groups["label"].Value.Trim()))
            .Where(x => x.rgb.Count == 3)
            .ToList();
        Assert.True(rows.Count >= 6, $"插件侧只抠到 {rows.Count} 行颜色，这条闸门正在空转");

        // 第 1..5 行必须是金木水火土（行序即域号），且注释必须自证是哪一系
        for (var d = 0; d < 5; d++)
        {
            var row = rows[d + 1];
            Assert.Contains(domainNames[d], row.label, StringComparison.Ordinal);
            var expected = clientColors[d];
            var actual = row.rgb;
            Assert.Equal(3, actual.Count);
            for (var c = 0; c < 3; c++)
            {
                Assert.True(Math.Abs(expected[c] - actual[c]) < 0.002f,
                    string.Format(CultureInfo.InvariantCulture,
                        "{0}系第 {1} 通道错位：客户端 {2:0.####} vs 编辑器 {3:0.####}", domainNames[d], c, expected[c], actual[c]));
            }
        }

        // 位值表与服务端 MonsterSchoolBits 逐值对账（下拉第 0 项是"未定系"这个选项，不是位）
        var options = Regex.Match(pluginText, @"SchoolMaskOptions\s*=\s*\{(?<body>[^}]*)\}");
        Assert.True(options.Success, "插件源里没解析到 SchoolMaskOptions");
        var pluginMasks = Regex.Matches(options.Groups["body"].Value, @"\d+")
            .Cast<Match>().Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(new[]
        {
            0,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Physical,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Metal,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Wood,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Water,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Fire,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Earth,
        }, pluginMasks);

        // 取位规则（"最低的五行位"）留在插件源码里，只有一行循环，且客户端与服务端各持一份同名实现；
        // 它由 施法载荷与系别位表能往返且位号与域号同源 那条断言从服务端侧钉住（0/物理/单系/多系都点过）。
    }

    [Fact]
    public void 插件镜像的分段表与内核逐字相同()
    {
        // 插件侧 MonsterPreviewMath 是内核的镜像副本（曲线预览）。
        // 副本与来源逐字相同才允许把预览当证据，否则"编辑器里看到的衰减"与"游戏里拿到的经验"会各说各话。
        var pluginPath = Path.Combine("HundunWorld", "Plugins", "MonsterEditor", "Source",
            "MonsterEditor", "MonsterPreviewMath.cs");
        var plugin = ParseIntArrayTables(pluginPath);
        foreach (var name in new[] { "StageStartLevels", "GrayGapByStage", "ZeroDifferenceByStage" })
        {
            Assert.True(plugin.ContainsKey(name), $"插件源码里没解析到 {name}（解析器或写法变了，这条闸门正在空转）");
        }

        Assert.Equal(Horizon.Game.Sim.Monsters.MonsterDifficultyCore.StageStartLevels, plugin["StageStartLevels"]);
        Assert.Equal(Horizon.Game.Sim.Monsters.MonsterDifficultyCore.GrayGapByStage, plugin["GrayGapByStage"]);
        Assert.Equal(Horizon.Game.Sim.Monsters.MonsterDifficultyCore.ZeroDifferenceByStage, plugin["ZeroDifferenceByStage"]);

        // 逐点重算镜像公式：三段表相同还不够，公式（定基、封顶、钳位）也要相同。
        // 这里**手工按文字定义**算，不调用插件也不调用内核 —— 两份实现同时被第三份参照约束。
        var starts = plugin["StageStartLevels"];
        var grayGap = plugin["GrayGapByStage"];
        var zd = plugin["ZeroDifferenceByStage"];
        for (var player = 1; player <= 300; player += 3)
        {
            var stage = 0;
            for (var i = 0; i < starts.Length; i++) if (player >= starts[i]) stage = i;
            var gray = player <= 5 ? 0 : Math.Max(0, player - grayGap[stage]);
            for (var delta = -20; delta <= 10; delta++)
            {
                var mob = player + delta;
                if (mob < 1) continue;
                long expected;
                if (mob <= gray) expected = 0;
                else
                {
                    var baseXp = 45 + 5 * mob;
                    var factor = mob > player ? 1.0 + 0.05 * Math.Min(mob - player, 4)
                                              : Math.Max(0.0, 1.0 - (double)(player - mob) / zd[stage]);
                    expected = (long)Math.Round(baseXp * factor, MidpointRounding.AwayFromZero);
                }
                var kernel = Horizon.Game.Sim.Monsters.MonsterXpCore.Compute(
                    new Horizon.Game.Sim.Monsters.MonsterXpConfig(45, 5, new[] { 1.0, 2.0, 2.5, 4.0, 4.0 }),
                    mob, player, Horizon.Game.Sim.Monsters.MonsterRank.Normal);
                Assert.Equal(expected, kernel);
            }
        }
    }

    [Fact]
    public void 插件镜像的曲线函数被真的调用并与内核逐点同数()
    {
        // 这条闸门存在的理由：MonsterPreviewMath 是内核的镜像副本，而在此之前它**只被按文本抠过常量**。
        // 常量相同证明不了式子相同——少一段截断、封顶方向反了、兜底档位写错，文本闸门全都照绿。
        // 现在这份文件被原样编进本工程（csproj 里的 Linked/MonsterPreviewMath），所以可以真的调用它。
        var players = new[] { 1, 5, 6, 20, 30, 31, 32, 60, 75, 76, 77, 100, 130, 131, 132, 200, 201, 260, 300, 301 };
        var mods = new (int XpMod, int HealthMod, string Label)[]
        {
            (10000, 10000, "全不开"),
            (20000, 10000, "双倍经验"),
            (10000, 7500, "血薄档=生效上界"),
            (10000, 7501, "血薄档=差一基点落在不生效那一段"),
            (10000, 5000, "血薄一半"),
            (15000, 6000, "两段同时开"),
            (-1, -1, "负数=坏值按没配处理"),
            (0, 10000, "经验倍率归零"),
        };

        var difficultySeen = new HashSet<int>();
        var xpPoints = 0;
        // 两段截断与"并成一次四舍五入"必须在这个网格里**真的分得开**，否则这条判据在空转。
        var twoStageDiffersFromSingle = 0;
        var healthStageActive = 0;
        var grayPoints = 0;

        foreach (var player in players)
        {
            for (var rank = 0; rank <= 4; rank++)
            {
                var kernelRank = (Horizon.Game.Sim.Monsters.MonsterRank)rank;
                for (var delta = -30; delta <= 12; delta++)
                {
                    var mob = player + delta;
                    if (mob < 1) continue;

                    var mirrorDiff = (int)MonsterPreviewMath.Of(mob, player, rank);
                    var kernelDiff = (int)Horizon.Game.Sim.Monsters.MonsterDifficultyCore.Of(mob, player, kernelRank);
                    Assert.True(mirrorDiff == kernelDiff,
                        $"难度档不一致：玩家 {player} 怪 {mob} rank {rank}，镜像 {mirrorDiff} 内核 {kernelDiff}");
                    difficultySeen.Add(kernelDiff);
                    if (kernelDiff == (int)Horizon.Game.Sim.Monsters.MonsterDifficulty.Gray) grayPoints++;

                    var mirrorFactor = MonsterPreviewMath.Concession(mob, player, rank);
                    var kernelFactor = Horizon.Game.Sim.Monsters.MonsterDifficultyCore.ConcessionFactor(mob, player, kernelRank);
                    Assert.True(Math.Abs(mirrorFactor - kernelFactor) < 1e-12,
                        $"让分系数不一致：玩家 {player} 怪 {mob} rank {rank}，镜像 {mirrorFactor:0.####} 内核 {kernelFactor:0.####}");

                    foreach (var m in mods)
                    {
                        var cfg = new Horizon.Game.Sim.Monsters.MonsterXpConfig(
                            45, 5, Horizon.Game.Sim.Monsters.MonsterXpConfig.DefaultRankScale(), m.XpMod, m.HealthMod);
                        var mirrorXp = MonsterPreviewMath.Xp(mob, player, rank, 45, 5, m.XpMod, m.HealthMod);
                        var kernelXp = Horizon.Game.Sim.Monsters.MonsterXpCore.Compute(cfg, mob, player, kernelRank);
                        xpPoints++;
                        Assert.True(mirrorXp == kernelXp,
                            $"经验镜像与内核不同数（{m.Label}）：玩家 {player} 怪 {mob} rank {rank}，镜像 {mirrorXp} 内核 {kernelXp}");

                        if (m.XpMod == 10000 && m.HealthMod == 7500)
                        {
                            var bd = Horizon.Game.Sim.Monsters.MonsterXpCore.Decompose(cfg, mob, player, kernelRank);
                            var raw = bd.Base * bd.Concession * bd.RankScale * bd.XpMod;
                            var firstStage = (long)Math.Round(raw, MidpointRounding.AwayFromZero);
                            if (firstStage != (long)Math.Round(raw * (m.HealthMod / 10000.0), MidpointRounding.AwayFromZero))
                                twoStageDiffersFromSingle++;
                            if (bd.Final != firstStage) healthStageActive++;
                        }
                    }
                }
            }
        }

        Assert.True(difficultySeen.Count >= 5, $"网格只判出 {difficultySeen.Count} 种难度档，这批等级根本没铺开");
        Assert.True(grayPoints > 0, "网格里没有灰名点：断供那一路从没比过");
        Assert.True(xpPoints >= 1000, $"只比了 {xpPoints} 个经验点，闸门铺开不够");
        Assert.True(twoStageDiffersFromSingle > 0, "这个网格分不出两段截断与一次取整的差别——比了个恒等式");
        Assert.True(healthStageActive > 0, "血薄那一段在网格里一次都没改变结果");

        // 兜底档：内核遇到不在 0..3 的档位按"战"展开，镜像必须一样（以前镜像写的是"全开"，
        // 于是编辑器把一只内核不会技能的怪画成五档全亮，而文本闸门永远看不出来）。
        foreach (var profile in new[] { 0, 1, 2, 3, 4, 9, -1 })
        {
            foreach (var ov in new (int? Threat, int? Skills, int? Nav, int? Assist, int? Tactics)[]
            {
                (null, null, null, null, null), (1, 1, 1, 1, 1), (0, 0, 0, 0, 0), (null, 1, null, 0, null),
            })
            {
                var kernel = Horizon.Game.Sim.Monsters.MonsterAiFlags.Resolve(
                    (Horizon.Game.Sim.Monsters.MonsterAiProfile)profile, ov.Threat, ov.Skills, ov.Nav, ov.Assist, ov.Tactics);
                MonsterPreviewMath.ExpandProfile(profile, ov.Threat, ov.Skills, ov.Nav, ov.Assist, ov.Tactics,
                    out var threat, out var skills, out var nav, out var assist, out var tactics);
                Assert.True(kernel.UseThreatTable == threat && kernel.UseSkills == skills && kernel.UseNavigation == nav
                           && kernel.UseAssist == assist && kernel.UseTactics == tactics,
                           $"AI 档位展开不同数：档位 {profile} 覆盖 {ov}，内核 {kernel.Describe()}，" +
                           $"镜像 threat={(threat ? 1 : 0)} skills={(skills ? 1 : 0)} nav={(nav ? 1 : 0)} " +
                           $"assist={(assist ? 1 : 0)} tactics={(tactics ? 1 : 0)}");
            }
        }

        // 系别：镜像的下拉取值表与最低位规则要和消息层的位定义同数（位号==域号那条老口径）。
        var school = new[]
        {
            Horizon.Game.Message.Sync.MonsterSchoolBits.Physical,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Metal,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Wood,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Water,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Fire,
            Horizon.Game.Message.Sync.MonsterSchoolBits.Earth,
        };
        Assert.Equal(new[] { 0 }.Concat(school).ToArray(), MonsterEditor.MonsterPreviewMath.SchoolMaskOptions);
        for (var mask = 0; mask <= 63; mask++)
        {
            Assert.Equal(Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(mask),
                         MonsterEditor.MonsterPreviewMath.FirstDomainOf(mask));
        }
    }

    /// <summary>
    /// 内容模型"每一格"的清单来源：类型上的**公开实例字段**（不含 const/静态缺省）。
    /// <para>必须是**运行期那个类型**而不是编辑期草稿：装配会把格名改掉或展开
    /// （<c>slot_seq→Slot</c>、<c>damage_scale_bps→DamageScale</c>、<c>flag_use_*→Flags</c> 那六位），
    /// 拿编辑期名字数读者会把一半健康的东西报成死配置（设计文档 §11.4 那张表记的就是这件事）。</para>
    /// </summary>
    private static List<string> ConfigFieldNames(Type configType)
        => configType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                     .Select(f => f.Name)
                     .OrderBy(n => n, StringComparer.Ordinal)
                     .ToList();

    /// <summary>逐格数"运行期读者"（点读法 <c>.格名</c>）；返回没读者的那些格，并顺带查例外是否已经烂掉。</summary>
    private static (List<string> Unread, List<string> StaleExceptions) ScanCellReaders(
        IEnumerable<string> fieldNames, string[] texts, HashSet<string> allowlisted)
    {
        var unread = new List<string>();
        var stale = new List<string>();
        foreach (var name in fieldNames)
        {
            var pat = new Regex(@"\." + Regex.Escape(name) + @"\b");
            var hits = texts.Sum(t => pat.Matches(t).Count);
            if (hits == 0) unread.Add(name);
            else if (allowlisted.Contains(name)) stale.Add(name);
        }
        return (unread, stale);
    }

    [Fact]
    public void 技能表每一格都有运行期消费者或带理由登记()
    {
        // 与下一条（行为表）同一把量尺，扫的是技能挂载。**今天只剩一格悬空**：
        // `StartEmoteId`（客户端起手动画表还没落地，本轮不造玩法，只让校验器在保存草稿时念出来）。
        // `StatusId` / `StatusDurationSeconds` 已由切片 59 接上消费者（怪物施法命中 → `ApplyPlayerStatus`），
        // 所以它们从例外清单里**删掉**——这条闸门本身会逼着做这件事：
        // "少了就是某格接上了消费者（那就把例外删掉），多了就是新造了一格死配置"。
        var root = RepoRoot();
        var texts = new[]
        {
            Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterCastingCore.cs"),
            Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterAiRuntime.cs"),
            Path.Combine(root, "Horizon.Game.Core", "Sim", "Server", "DsWorldSession.cs"),
        }.Select(File.ReadAllText).ToArray();

        var allowlisted = new HashSet<string>(StringComparer.Ordinal)
        {
            "StartEmoteId",
        };

        var fields = ConfigFieldNames(typeof(Horizon.Game.Sim.Monsters.MonsterSkillMount));
        Assert.True(fields.Count >= 18,
                    $"只反射到 {fields.Count} 个技能格（预期至少 18）：判据在退化输入上空转");

        var (unread, stale) = ScanCellReaders(fields, texts, allowlisted);
        Assert.True(unread.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(allowlisted.OrderBy(n => n, StringComparer.Ordinal)),
                    $"技能表里没人读的格变了：实测 {string.Join("、", unread)}，登记的是 {string.Join("、", allowlisted)}。" +
                    "少了就是某格接上了消费者（那就把例外删掉），多了就是新造了一格死配置");
        Assert.True(stale.Count == 0,
                    $"登记的悬空格已经有消费者了：{string.Join("、", stale)} —— 把那几行例外删掉");
    }

    [Fact]
    public void 行为表每一格都有运行期消费者()
    {
        // 切片 19/20/21/22/25 连着清的五条**同一形状**的缺陷：编辑器有格子、库有列、快照带了、装配也盖了，
        // 而最后一段没人读——策划改了什么都不发生。逐格补判据是打地鼠，这条闸门是**普查**：
        // 拿结构体的公开字段清单（那就是每一格在运行期的样子）去运行期那几个文件里数"读"，
        // 任何一格只要没有消费者就必须当场红，除非它带着**理由**登记在下面那份例外里。
        //
        // 数的是 `.字段名` 这种带点的读法，而**不看装配那份**（`MonsterBehaviorSnapshot.ToConfig` 里的
        // `cfg.X = X` 是写方）：把写方当消费者会让每一格都自动"有人读"，闸门立刻空转。
        var root = RepoRoot();
        var runtimeFiles = new[]
        {
            Path.Combine(root, "Horizon.Game.Sim", "MonsterAiCore.cs"),                       // v1
            Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterAiRuntime.cs"),        // v2
            Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterBehaviorCore.cs"),     // 各位主人
            Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterAiGate.cs"),           // 起手/脱战主人
            Path.Combine(root, "Horizon.Game.Core", "Sim", "Server", "DsWorldSession.cs"),    // 宿主与投影
            Path.Combine(root, "Horizon.Game.Core", "Sim", "Server", "MonsterSpawnPointBuilder.cs"),
        };
        var texts = runtimeFiles.Select(File.ReadAllText).ToArray();

        // 登记例外必须带理由；且例外**只许这些格**——加一行就等于承认新的一格没人读。
        // **2026-10-28 切片 69（D32a）起这张清单是空的**：最后一格 `DetectMask` 接上了判据
        // （`MonsterAiGate.CanDetectTarget`，v1/v2 的索敌都问它），所以那条例外按闸门自己的原话删掉了。
        // 空清单本身也是判据：任何一格新造出来却没人读，这里会红。
        var allowlisted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
        };

        var fields = ConfigFieldNames(typeof(Horizon.Game.Sim.Monsters.MonsterBehaviorConfig));
        // 防空转：反射读到 0 个字段时这条判据会漂亮地全绿。18 是本轮量出来的格数。
        Assert.True(fields.Count >= 18,
                    $"只反射到 {fields.Count} 个行为格（预期至少 18）：判据在退化输入上空转");

        var (unread, actuallyRead) = ScanCellReaders(
            fields, texts, new HashSet<string>(allowlisted.Keys, StringComparer.Ordinal));
        var missing = unread.Where(n => !allowlisted.ContainsKey(n)).ToList();
        Assert.True(missing.Count == 0,
                    $"行为表里有格子没有任何运行期消费者：{string.Join("、", missing)}（共 {fields.Count} 格）。" +
                    "要么接进两条 AI 通路（判据进主人），要么带着理由登记例外并写进 §11.2——" +
                    "别让它停在「配了等于没配」");
        // 例外清单不许烂掉：登记的每一格今天必须真的没人读，否则该行该删（否则下一个人会以为它仍无人读）。
        Assert.True(actuallyRead.Count == 0,
                    $"例外清单里的格子已经有消费者了：{string.Join("、", actuallyRead)} —— 把那行删掉，" +
                    "留着它会让人以为这一格仍然「配了不生效」");
        // 例外本身的规模也要可见：今天只允许 detect_mask 这一格悬空。
        Assert.True(allowlisted.Count <= 1,
                    $"例外清单已经涨到 {allowlisted.Count} 格：每加一行就是新造一格死配置，请先把判据接进去");
    }

    [Fact]
    public void 刷怪点每一格都有运行期消费者或带理由登记()
    {
        // 切片 70 的教训：`shard_id` 曾经"库有列、草稿有键、面板有格、装配还把它盖进 SpawnPointConfig"，
        // 唯一缺的是**没有一处比较它**——格子改了世界不变。这一类洞用逐格补用例是打地鼠，
        // 所以照行为表那条的量尺给刷怪点也做一次普查：拿运行期真吃的那个结构体
        // （`MonsterSpawnSnapshot`）逐格数点读法，任何一格没人读就必须带理由登记。
        //
        // `enabled` **不在这张量尺上**：它没有进结构体，而是在**导出**那一跳就被筛掉
        // （`MonsterStore` 只导 `Enabled == 1 && PublishState == 1` 的点）。补进结构体反而会
        // 造出一格"带了但没人看"的值——那是这道闸门要红的东西。
        var root = RepoRoot();
        var texts = new[]
        {
            Path.Combine(root, "Horizon.Game.Core", "Sim", "Server", "MonsterSpawnPointBuilder.cs"),
            Path.Combine(root, "Horizon.Game.Core", "Sim", "Server", "DsWorldSession.cs"),
            Path.Combine(root, "Horizon.DsHost", "Program.cs"),
            Path.Combine(root, "Horizon.DsHost", "DsHostRunner.cs"),
        }.Select(File.ReadAllText).ToArray();

        // 登记例外必须带理由；加一行就等于承认新的一格"配了等于没配"。
        var allowlisted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ZoneId"] = "今天没有分区筛选：世界按坐标与分片划片，zone 只作登记（编辑器也不给格子）",
        };

        var fields = ConfigFieldNames(typeof(Horizon.Game.Sim.Monsters.MonsterSpawnSnapshot));
        // 防空转：反射读到 0 个字段时这条判据会漂亮地全绿。13 是本轮量出来的格数。
        Assert.True(fields.Count >= 13,
                    $"只反射到 {fields.Count} 个刷怪点格（预期至少 13）：判据在退化输入上空转");

        var (unread, actuallyRead) = ScanCellReaders(
            fields, texts, new HashSet<string>(allowlisted.Keys, StringComparer.Ordinal));
        var missing = unread.Where(n => !allowlisted.ContainsKey(n)).ToList();
        Assert.True(missing.Count == 0,
                    $"刷怪点里有格子没有任何运行期消费者：{string.Join("、", missing)}（共 {fields.Count} 格）。" +
                    "要么接进装配/宿主（判据进主人），要么带着理由登记例外并写进 §11.2");
        // 例外不许烂掉：登记的每一格今天必须真的没人读，否则那行该删。
        Assert.True(actuallyRead.Count == 0,
                    $"例外清单里的格子已经有消费者了：{string.Join("、", actuallyRead)} —— 把那行删掉");
        // 例外规模也要可见：每加一行就是新造一格死配置。
        Assert.True(allowlisted.Count <= 1, $"例外清单已经涨到 {allowlisted.Count} 格，请先把判据接进去");
    }

    [Fact]
    public void 脱战与起手那三条判据只有主人没有第二份清单()
    {
        // AzerothCore 那三条（复活宽限 / 嘲讽压 leash / 世界 BOSS 不查距离）以前只写在 v2 里，
        // 而宿主在能力位全关时根本不进 v2 ⇒ 全部种子怪一条都没享受过。现在两路都调 MonsterAiGate。
        // 这条闸门钉的是"**没有第二份清单**"：同一条规则各写一遍，迟早有一处漏改，
        // 而漏改那一处玩家看得见（贴刷新点被秒咬、坦克拉出去怪半路回头）。
        var root = RepoRoot();
        var gate = File.ReadAllText(Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterAiGate.cs"));
        var v1 = File.ReadAllText(Path.Combine(root, "Horizon.Game.Sim", "MonsterAiCore.cs"));
        var v2 = File.ReadAllText(Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterAiRuntime.cs"));
        var host = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core", "Sim", "Server", "DsWorldSession.cs"));

        // 主人这一侧先把四个判据点齐——少一个，下面那些"0 命中"就只是没人写而已。
        foreach (var owner in new[] { "static bool TauntHolds(", "static bool LeashApplies(",
                                      "static bool ExceededLeash(", "static bool InRespawnGrace(",
                                      "static bool IsEngageable(", "RespawnGraceUntil(",
                                      // 回位那几格同样只有这一份主人（v1 的怪此前既不刷血也没窗）
                                      "static bool InEvadeImmunity(", "static bool BlocksNewInitiation(",
                                      "static long EvadeImmunityUntil(",
                                      // AC 把"回位中"做成状态；这条也只有这一份定义
                                      "static bool ReturningHome(", "static bool BlockedFromNewEnemy(",
                                      "static bool WipesSelectedTarget(", "static bool CanBeForcedIntoCombat(",
                                      // 受击方那一侧的"打不到"（AC 的 IsEvadingAttacks）档位比"接新敌"窄，
                                      // 必须单独一位主人，否则很容易被顺手并回 BlockedFromNewEnemy 那条式子里
                                      "static bool BlocksIncomingDamage(",
                                      // "什么算在战斗里"以前有四份 inline（宿主三处 + v2 施法一处）：
                                      // 施法档位、求援要不要抄目标、非战斗自我增益、逃跑判据问的都是同一件事，
                                      // 四份各写各的迟早有一处把 Leash 也算进去。
                                      "static bool InCombat(" })
        {
            Assert.True(gate.Contains(owner, StringComparison.Ordinal), $"MonsterAiGate 里少了 {owner}");
        }

        // 移动方式那一族（站桩 = 不产生位移）的主人在 MonsterPatrolCore 那一份里
        // （与 NavEligibleFollow / IgnoresHardCellCost 同住），两条通路都只问它。
        var patrolOwner = File.ReadAllText(Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterBehaviorCore.cs"));
        Assert.True(patrolOwner.Contains("static bool FollowProducesMovement("),
                    "「这一档会不会产生位移」没有主人了：它会长回各条通路里的 inline 比较");

        // 两路都必须把判据**交出去**（调用点各至少一处）。
        foreach (var (label, text) in new (string, string)[] { ("v1", v1), ("v2", v2) })
        {
            // 两条通路的"起手窗"入口本来就不同形（v2 分在索敌与求援两处，v1 只有一个索敌漏斗），
            // 所以这里各问各的：谁绕开主人自己写一遍，这条就红在谁身上。
            var required = label switch
            {
                // v1：起手窗一条式子（内含宽限/免疫/回位三段），回位那段用状态判据
                "v1" => new[] { "MonsterAiGate.ExceededLeash(", "MonsterAiGate.TauntHolds(",
                                "MonsterAiGate.LeashApplies(", "MonsterAiGate.BlocksNewInitiation(",
                                "MonsterAiGate.ReturningHome(", "MonsterAiGate.EvadeImmunityUntil(",
                                // 站桩档在 v1 里以前**根本没有这个输入**：位移出口必须问主人
                                "MonsterPatrolCore.FollowProducesMovement(" },
                // v2：主动通路（含求援）与"清零已选中目标"是两条**不同宽度**的判据，必须分别问主人
                "v2" => new[] { "MonsterAiGate.ExceededLeash(", "MonsterAiGate.TauntHolds(",
                                "MonsterAiGate.LeashApplies(", "MonsterAiGate.BlockedFromNewEnemy(",
                                "MonsterAiGate.WipesSelectedTarget(", "MonsterAiGate.ReturningHome(",
                                "MonsterAiGate.EvadeImmunityUntil(",
                                // 非战斗施法（slot_type=1）那一档的"不算战斗"也必须问主人
                                "MonsterAiGate.InCombat(" },
                // 宿主：嘲讽受理那条外力入口与"这一手吃不吃打"都只能问主人，不许自己比 tick 或自己判状态
                _ => new[] { "MonsterAiGate.CanBeForcedIntoCombat(", "MonsterAiGate.ReturningHome(",
                             "MonsterAiGate.BlocksIncomingDamage(", "MonsterAiGate.InCombat(",
                             // 索敌喊话只在"刚进战斗那一拍"发一条，判据同样住在主人
                             "SyncMonsterSay(" },
            };
            foreach (var call in required)
            {
                var hits = CountOccurrences(text, call);
                Assert.True(hits >= 1, $"{label} 没有调用 {call}：那条判据又变回本地一份了");
            }
        }

        // 缺省秒数四处各写一遍的话，"编辑器里写的 3 秒"和"跑起来的 3 秒"迟早分家：按值对一遍。
        var ownerSeconds = Horizon.Game.Sim.Monsters.MonsterBehaviorConfig.EvadeImmunityDefaultSeconds;
        Assert.Equal(ownerSeconds, Horizon.Game.Sim.Monsters.MonsterBehaviorConfig.Default.EvadeImmunitySeconds);
        Assert.Equal(ownerSeconds, Horizon.Game.Sim.MonsterAiConfig.Default.EvadeImmunitySeconds);
        Assert.Equal(ownerSeconds, new Horizon.Game.Core.Sim.Server.MonsterBehaviorSnapshot().EvadeImmunitySeconds);
        // 库那一格挂在**行为表实体**上（模板实体没有这一项：一只怪的免疫窗不随模板档位变）。
        Assert.Equal(ownerSeconds, new Horizon.Model.GameModel.MonsterBehaviorEntity().EvadeImmunitySeconds);
        Assert.Equal((double)ownerSeconds, new Horizon.MonsterCli.MonsterBehaviorDraft().EvadeImmunitySeconds);

        // 本地重写的那份必须一处不剩（这几条 needle 只出现在内核两个文件里，不会命中本测试自己）。
        foreach (var (label, text, inline) in new (string, string, string)[]
        {
            ("v1", v1, "distToSpawn > config.LeashRadius"),
            ("v2", v2, "distToSpawn > behavior.LeashRadius"),
            ("v2", v2, "!= MonsterRank.WorldBoss"),
            ("v2", v2, "inputs.ServerTick < state.NoInitiateUntilTick"),
            ("v2", v2, "inputs.ServerTick >= state.TauntUntilTick"),
            ("宿主", host, "_tick < EvadeImmuneUntilOf(monster)"),
            // "什么算回位中"只许主人一处定义：两路与宿主都不许自己再判一次状态
            ("v1", v1, "static bool ReturningHome("),
            ("v2", v2, "static bool ReturningHome("),
            ("宿主", host, "static bool ReturningHome("),
            ("v1", v1, "serverTick < self.NoInitiateUntilTick"),
            ("v1", v1, "serverTick < self.EvadeImmuneUntilTick"),
            ("v2", v2, "inputs.ServerTick < state.EvadeImmuneUntilTick"),
            // 免疫秒数只有一个主人：内核里再写一遍 3f 就是"编辑器里的 3 秒"与"跑起来的 3 秒"分家
            ("v1", v1, "EvadeImmunitySeconds = 3f"),
            ("v2", v2, "EvadeImmunitySeconds = 3f"),
            // 站桩那一档的判据只许问主人；两路各自 inline 比较就是"两处清单漏改一处"的起点
            ("v1", v1, "FollowType == MonsterFollowType.Static"),
            ("v2", v2, "FollowType == MonsterFollowType.Static"),
            // "在不在战斗里"以前被四处各抄一遍（宿主三处 + v2 非战斗施法一处）。
            // 针只钉 `== MonsterAiState.Attack` 这个**比较**形状：`case MonsterAiState.Attack:` 与
            // `decision.NextState = MonsterAiState.Attack;` 都不含它，所以不会拦到状态机自己的转移。
            // 真要写一条"只看 Attack 不看 Chase"的判据，就该在主人那里新添一位，而不是回到本地比。
            ("宿主", host, "== MonsterAiState.Attack"),
            ("v2", v2, "== MonsterAiState.Attack"),
            ("v1", v1, "== MonsterAiState.Attack"),
            ("v1", v2, "static bool FollowProducesMovement("),
            // v1 的缺省必须是"会动"：把缺省写成站桩会让所有没带这一格的旧内容集体定住，
            // 而那是不可见的整体手感变化。
            ("v1", v1, "FollowType = MonsterFollowType.Static"),
        })
        {
            var hits = CountOccurrences(text, inline);
            Assert.True(hits == 0, $"{label} 里还留着本地那份 {inline}（{hits} 处）：两条通路会各按一套算");
        }

        // 装配层必须把"移动方式"这一格带到**两条通路**上。
        // 2026-10-26 变更：两条通路现在共用**同一个投影出口**（BuildAiConfigFromContent），
        // 所以"两处赋值"这条尺子已经过期——现在的判据是"只许写一处，而那一处被两条通路都调到"。
        // 谁再把某一格零散盖在 tick 通路里（`cfg.X = contentCfg.Behavior.X` 那种形状），
        // 就是回到"同一管线的两份清单"，下面那组"零散覆盖必须为 0"当场红。
        Assert.True(CountOccurrences(host, "cfg.FollowType = ") == 1,
                    $"宿主给 cfg.FollowType 赋值 {CountOccurrences(host, "cfg.FollowType = ")} 处（应当恰好一处：投影那一个出口）；" +
                    "出现第二处就是又开始各写一遍");
        // 尺子要用**不带参数名的形**：上一版写成 `BuildAiConfigFromContent(contentCfg, `，
        // 而 v2 那侧的变量叫 config——反证把第二次投影加回去照样绿（针落在了实参的字面上，不是调用这件事上）。
        // 定义行本身含这个名字 ⇒ "总数 = 定义 1 + 调用 N"。
        //
        // 2026-10-26 切片 27 起，投影的**调用点是一份显式清单**（定义 1 + 调用 3）：
        //   ① tick 通路（v1 与 v2 都吃这一份输出）；② 生成时的初始血；③ 战斗层的血量上限/攻击力。
        // 三处读的都是"这只怪自己的生效等级"，所以等级区间掷出什么都不会分成两套账
        // （以前 ② ③ 读点位那份，而点位是装配期按**基准等级**算的 ⇒ "等级牌变了而血没变"）。
        // 再多一处调用必须先在这里登记它读哪一格，否则就是第四个消费者在自己挑数值来源。
        var projectionSights = CountCodeOccurrences(host, "BuildAiConfigFromContent(");
        Assert.True(projectionSights == 4,
                    $"宿主里 BuildAiConfigFromContent( 共 {projectionSights} 处（应当恰好四处：定义 + tick／生成／战斗层三个消费者）" +
                    "：多一处就是第四个消费者在两套数值里挑一套，少一处就是某条通路退回读点位那份");
        // 而"内容 → 数值"的公式在宿主只许出现一次（就在投影内部）——这才是"清单不会分家"的真正根据。
        Assert.True(CountCodeOccurrences(host, "MonsterStatsCore.Compute(") == 1,
                    "宿主里按公式算数值的地方不止投影一处：等级/档位/倍率会长出第二份清单");
        Assert.True(CountCodeOccurrences(host, "cfg.MaxHp = stats.MaxHp;") == 1,
                    "把算好的血量写进配置的地方不是一处");
        foreach (var (inline, why) in new[]
        {
            ("cfg.Rank = contentCfg.Rank;", "等级档该由投影盖，不该在 tick 里零散覆盖"),
            ("cfg.EvadeImmunitySeconds = contentCfg.Behavior", "免疫秒该由投影盖"),
            ("cfg.FollowType = contentCfg.Behavior.FollowType;", "移动方式该由投影盖"),
        })
        {
            var hits = CountOccurrences(host, inline);
            Assert.True(hits == 0, $"{inline} 还留在宿主里（{hits} 处）：{why}");
        }
        // 行为表里露出的每一格都要在投影那一个出口里出现——这一组是"值到没到"的正门，
        // 少一格就是那一格只对其中一档 AI 成立（§11.2 第 14 条原来的形状）。
        foreach (var inline in new[]
        {
            "cfg.AggroConeDegrees = behavior.AggroConeDegrees;",
            "cfg.IdleToPatrolSeconds = behavior.IdleToPatrolSeconds;",
            "cfg.EvadeImmunitySeconds = behavior.EvadeImmunitySeconds;",
            "cfg.Rank = config.Rank;",
            "cfg.AggroRadius = behavior.AggroRadius;",
            "cfg.LeashRadius = behavior.LeashRadius;",
            "cfg.WanderRadius = behavior.WanderRadius;",
            "cfg.FollowType = behavior.FollowType;",
        })
        {
            Assert.True(CountOccurrences(host, inline) == 1,
                        $"投影里找不到 {inline}（应当恰好一处）：这一格又回到只有一条通路读得到");
        }
        // 重生秒**不许**在投影里重算：点位那份带"刷怪点显式覆盖"和"限时怪不复活"的 float.MaxValue 哨兵，
        // 而两条 AI 通路都不读这一格（读的是宿主另一次点位查找）。按公式重算只会把这两样抹掉。
        Assert.True(CountOccurrences(host, "cfg.RespawnDelaySeconds = MonsterStatsCore.RespawnSeconds(") == 0,
                    "投影里在重算重生秒：会把点位那一份的覆盖值与 TTL 哨兵一起抹掉");
        // 索敌扇形的式子只有主人那一份；两路各自写 MathF.Cos(半角) 就是第二份清单。
        Assert.True(CountOccurrences(v1, "AggroConeDegrees <= 0f") == 0 && CountOccurrences(v2, "AggroConeDegrees <= 0f") == 0,
                    "两路自己判扇形缺省：扇形判据必须只在 MonsterAiGate.InAggroCone 有一份");
        Assert.True(CountOccurrences(v1, "MonsterAiGate.InAggroCone(") == 1
                    && CountOccurrences(v2, "MonsterAiGate.InAggroCone(") == 1,
                    "v1 与 v2 必须各问一次扇形主人；少一次就是那一档的怪不查角度");
        // 被动怪那一格（is_aggressive）：两路都只许问主人，v2 不许再留 inline 的合取式。
        Assert.True(CountOccurrences(v1, "MonsterAiGate.MayInitiateOnSight(") == 1
                    && CountOccurrences(v2, "MonsterAiGate.MayInitiateOnSight(") == 1,
                    "v1 与 v2 必须各问一次「会不会主动看上人」的主人；少一次就是被动档对那一通路是装饰");
        Assert.True(CountOccurrences(v2, "MonsterAiGate.WipesThreatForPassive(") == 1,
                    "v2 收尾那条「被动怪清掉选中的威胁」没走主人：三条例外（已锁定／求援／免疫窗）会各写一遍");
        // v2 里 `behavior.IsAggressive` 只许出现在**递给主人的那两个实参**上（2 次）；
        // 多一次就是那条式子又在自己那儿判了一遍（注释里提这个名字不算——needle 带 `behavior.` 前缀）。
        Assert.True(CountOccurrences(v2, "behavior.IsAggressive") == 2,
                    $"v2 里读 behavior.IsAggressive {CountOccurrences(v2, "behavior.IsAggressive")} 次（应当恰好两次，都是递给主人）" +
                    "：多出来的一次就是第二份清单");
        Assert.True(CountOccurrences(host, "cfg.IsPassive = !behavior.IsAggressive;") == 1,
                    "投影里没有把被动档盖到 v1 那份配置上（或盖了两遍）：这一格又只有 v2 读得到");
        // 索敌半径这一格（切片 23 收的口，口径 2026-10-27：0 与负数是非法值）：
        // 两路都只许问同一个主人，而 v2 那份"没配就退回宿主距离"的兜底必须不存在——
        // 那正是这次要消灭的第二种口径（同一份内容包，v1 聋、v2 看得见，两边用例各自都能绿）。
        Assert.True(CountOccurrences(v1, "MonsterAiGate.AggroRadiusUsable(") == 1
                    && CountOccurrences(v2, "MonsterAiGate.AggroRadiusUsable(") == 1,
                    "v1 与 v2 必须各问一次半径主人；少一次就是那一通路又自己判了一档");
        Assert.True(CountOccurrences(v2, "legacy.AggroRadius") == 0,
                    "v2 里还留着「退回宿主那份半径」：0 与没配又成了两种口径，两条通路必然再次分叉");
        Assert.True(CountOccurrences(v1, "config.AggroRadius <= 0f") == 0
                    && CountOccurrences(v2, "behavior.AggroRadius > 0f ?") == 0,
                    "半径的 ≤0 判据又写回通路里了：这一格的主人只能有一个");
        // 反向旗标的缺省必须等于既有行为：内核/默认值里都不许把它写成 true（那会让全部旧内容一夜变被动怪）。
        Assert.True(CountOccurrences(v1, "IsPassive = true") == 0,
                    "v1 那份配置把缺省写成了被动：所有没带这一格的旧内容与夹具会集体变成\"站着挨打不还手\"");
        // 巡逻速度那一格（walk_speed）：选择式子只在主人那有一份，两路各问一次；
        // 两路都不许再直接拿"追你的速度"去溜达。
        Assert.True(CountOccurrences(v1, "MonsterPatrolCore.WanderSpeed(") == 1
                    && CountOccurrences(v2, "MonsterPatrolCore.WanderSpeed(") == 1,
                    "v1 与 v2 必须各问一次游荡速度的主人；少一次就是那一格只对一档 AI 成立");
        Assert.True(CountOccurrences(v1, "dir * config.MoveSpeed * dt") == 0,
                    "v1 的游荡还在直接拿 move_speed 溜达：walk_speed 那一格又变成装饰");
        Assert.True(CountOccurrences(v2, "dir * behavior.WalkSpeed * dt") == 0,
                    "v2 的游荡又自己挑速度了：式子必须只在 MonsterPatrolCore.WanderSpeed 有一份");
        Assert.True(CountOccurrences(host, "cfg.WalkSpeed = behavior.WalkSpeed;") == 1,
                    "投影没把巡逻速度盖到 v1 那份配置上（或盖了两遍）");
        Assert.True(CountOccurrences(host, "LeashApplies(config.Rank)") == 0,
                    "还在按单参数问 LeashApplies：站桩那一半输入没传进去，站桩怪会被判成\"正在回位\"");

        // 巡逻线：两条通路都必须真的去问"线从哪来"，而"沿哪一段走、到没到点、停多久"
        // 只能在 MonsterPatrolCore 有一份实现——v1 里再写一遍到达距离就是第二份判据。
        //
        // 2026-10-26 变更：线表从"只有怪物内容"变成"怪物内容 → NPC 内容"两源（内容 NPC 的线存在 NPC 那边），
        // 于是取线收敛到唯一主人 ResolveRouteObject。计数只数**赋值调用**（`= ResolveRouteObject(`），
        // 不数定义行、也不数注释里的字样——上一版把三处一起数进来，闸门当场红在自己身上。
        var routeCalls = CountOccurrences(host, "= ResolveRouteObject(");
        Assert.True(routeCalls == 2,
                    $"宿主解析巡逻线 {routeCalls} 次（应当恰好两次：v1 与 v2 各一次）。" +
                    "少一次就是那条线只有另一条通路读得到——线对默认档的怪又是装饰");
        // 取线的唯一主人只允许直接查怪物内容一次（NPC 内容那一路必须走同一个主人）。
        var directMonsterRouteLookups = CountOccurrences(host, "MonsterContent.Route(");
        Assert.True(directMonsterRouteLookups == 1,
                    $"取线的唯一主人应当只直接查怪物内容一次（现在 {directMonsterRouteLookups} 次）；" +
                    "多出来的一处意味着又有人绕过 ResolveRouteObject 自己找线（NPC 内容那条就漏了）");
        Assert.True(CountOccurrences(v1, "MonsterPatrolCore.Advance(") == 1,
                    "v1 没有走线的调用点（或不走主人那条）：巡逻线对默认档再次失效");
        Assert.True(CountOccurrences(v2, "MonsterPatrolCore.Advance(") == 1,
                    "v2 那份走线调用点变了：两条通路不再共用同一个主人");
        Assert.True(CountOccurrences(v1, "ArriveDistance") == 0,
                    "v1 自己写了一份\"到点距离\"：两份到达判据会在\"离点多近算到\"上分家");
        Assert.True(CountOccurrences(host, "self.Patrol = decision.Patrol") == 1,
                    "巡逻游标的写点不是一处（或没了）：漏写会把它冻在旧值上，表现是同一个点之间来回抖");
    }

    private static int CountOccurrences(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>
    /// 只数**代码**里的出现：把每行 `//` 之后的部分先抹掉。
    /// <para>为什么需要它：计数型针曾经命中过"解释这句话的那条注释"（本轮就撞上一次——
    /// 注释里写着"闸门数 <c>MonsterStatsCore.Compute(</c>"，于是"只许一处"变成"两处"，
    /// 红在我自己写的说明上）。同族的另一面记在 [[green-selftest-can-mean-the-line-never-ran]]：
    /// 针既能命中备忘录，也能命中说明书。</para>
    /// <para>局限（用之前先看一眼）：不处理字符串字面量里的 <c>//</c>，也不处理多行注释里以 <c>//</c>
    /// 开头的续行以外的形状。要数的是"代码形状"（ASCII 方法名/赋值）时才用它。</para>
    /// </summary>
    private static int CountCodeOccurrences(string text, string needle)
    {
        var stripped = new System.Text.StringBuilder(text.Length);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            var cut = line.IndexOf("//", StringComparison.Ordinal);
            if (cut >= 0) line = line.Substring(0, cut);
            stripped.Append(line).Append('\n');
        }
        return CountOccurrences(stripped.ToString(), needle);
    }

    [Fact]
    public void 普通攻击在客户端有入口而不是只存在于上报函数里()
    {
        // 真事（2026-10-31 用户报"打不死怪"）：`CombatReporter.ReportAttack` 全仓**零调用方**——
        // 普通攻击只有定义、没有输入；玩家要么有技能在栏里，要么永远打不死怪，而所有用例都是绿的。
        // 这条只钉"入口存在且真的上报"，伤害数值仍归服务端权威。
        var selection = File.ReadAllText(Path.Combine(
            RepoRoot(), "HundunWorld", "Source", "Game", "Selection", "WorldSelectionService.cs"));
        Assert.Contains("public bool TryBasicAttackSelected()", selection);
        Assert.Contains("KeyboardKeys.H", selection);                       // 键位 H（R 归相机复位、F 归攀爬、X/T 打断/嘲讽）
        Assert.DoesNotContain("KeyboardKeys.R", selection);                 // 不许抢相机的复位键
        var camera = File.ReadAllText(Path.Combine(
            RepoRoot(), "HundunWorld", "Source", "Game", "ThirdPersonCamera.cs"));
        Assert.DoesNotContain("ResetKey = KeyboardKeys.H", camera);          // 反方向：相机也不许抢它
        Assert.True(CountOccurrences(selection, "TryBasicAttackSelected();") >= 1, "只有定义没有调用：这手根本发不出去");
        Assert.Contains("Combat.CombatReporter.ReportAttack(tag.Actor)", selection);
        Assert.Contains("先选中一个目标", selection);                        // 没选中时要说人话，不是静默
    }

    [Fact]
    public void DS的伤害与死亡包在客户端有订阅者而怪物攻击不再发给Orleans()
    {
        // 真事（2026-10-31 用户报"血量为0但血条没更新、捡不到、也没有经验"）：
        // ① `SyncPacketMessageHandler.DamageReceived/DeathReceived` 两个事件**没有任何订阅者**
        //    （CombatEffectSystem 的 Enqueue 也是零调用方）——DS 打死了怪，客户端只在下一份快照
        //    里才可能看到血量，"血条纹丝不动"就是这么来的。
        // ② 怪物攻击同时发去了 Orleans，那条链路只认 CharacterGrain，对怪物 id 回
        //    "实体未注册 / 剩余血量 0"，客户端日志每刀一条 7499 伤害，血条却不动。
        var game = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "HundunWorldGame.cs"));
        Assert.Contains("syncHandler.DamageReceived += OnDsDamageReceived", game);
        Assert.Contains("syncHandler.DeathReceived += OnDsDeathReceived", game);
        Assert.Contains("attrs.CurrentHealth = damage.RemainingHp", game);      // 立刻落到血条
        Assert.Contains("attrs.IsDead = true", game);                            // 立刻进入尸体表现

        var reporter = File.ReadAllText(Path.Combine(
            RepoRoot(), "HundunWorld", "Source", "Game", "Combat", "CombatReporter.cs"));
        Assert.Contains("sendToOrleans: !IsMonsterTarget(target)", reporter);    // 怪物只走 DS
        Assert.Contains("SendToDsWorld", reporter.Replace("ReportToDsWorld", "SendToDsWorld"));
        Assert.DoesNotContain("ReportAttackById(targetId, elementType, skillId, YawTowards(target?.Position));", reporter);
    }

    [Fact]
    public void 普攻上报的朝向必须是算出来的真朝向而不是写死的零()
    {
        // 真事（2026-10-31 自动验收）：60 刀全打到同一只怪、DS 侧 `战斗动作=60` 却一滴血不掉。
        // 根因就在这格：`AttackerYaw = 0f` 把"攻方朝向"钉成服务端平面里的 +X，
        // 而 CombatCore 的扇区判定会拿它点乘"攻方→目标"方向——怪不在正东侧就整刀作废（OutOfArc）。
        // 这条门钉的是"朝向来自几何"，不是某个具体键位或文案。
        var reporter = File.ReadAllText(Path.Combine(
            RepoRoot(), "HundunWorld", "Source", "Game", "Combat", "CombatReporter.cs"));
        Assert.True(CountCodeOccurrences(reporter, "AttackerYaw = 0f") == 0,
                    "写死 0 就是本缺陷本身：朝向必须来自几何，不能是常量");
        Assert.True(CountCodeOccurrences(reporter, "AttackerYaw = targetYaw ?? YawOfLocalPlayer()") == 1,
                    "拿到目标坐标就必须用真朝向；拿不到才退回玩家前向");
        Assert.Contains("YawTowards(target?.Position)", reporter);              // 普攻那条必须带目标坐标进来
        Assert.Contains("Math.Atan2(dy, dx)", reporter);                        // 服务端约定 atan2(dy,dx)
        Assert.Contains("to.Z - from.Z", reporter);                             // 服务端 Y = Flax Z（与移动上行同一套轴）
    }

    [Fact]
    public void 打断与嘲讽的客户端判据被真跑而两条上报也有了调用方()
    {
        // 服务端那两条受理口（Interrupt/Taunt）早就各自带拒因计数，可客户端零调用方——
        // "全量测试绿"也证明不了玩家摸得到它（见记忆：只有测试在调的 public 入口等于没接线）。
        // 这一条同时钉三件事：判据被真跑、键位不抢别人的、两条上报真的有了生产调用方。
        var rules = typeof(HundunWorld.Game.Combat.MonsterCombatActionRules);
        Assert.True(rules != null);

        // ① 真值表：打断的前提必须是"嘲讽那四条 + 在读条 + 那条可打断"，且不可用档必须有话说
        //    四旗标 = 16 档全跑（含"正在回位"那一格），少跑一档就是"某条拒绝静默回落到别的原因"。
        var tauntCombos = new List<(bool Has, bool Monster, bool Selectable, bool Unattackable)>();
        for (var m = 0; m < 16; m++)
            tauntCombos.Add(((m & 1) != 0, (m & 2) != 0, (m & 4) != 0, (m & 8) != 0));
        foreach (var c in tauntCombos)
        {
            var can = HundunWorld.Game.Combat.MonsterCombatActionRules.CanTaunt(
                c.Has, c.Monster, c.Selectable, c.Unattackable);
            Assert.True(can == (c.Has && c.Monster && c.Selectable && !c.Unattackable),
                        $"嘲讽真值表错档：has={c.Has} monster={c.Monster} selectable={c.Selectable} unattackable={c.Unattackable} 实得 {can}");
            var hint = HundunWorld.Game.Combat.MonsterCombatActionRules.TauntHint(
                c.Has, c.Monster, c.Selectable, c.Unattackable);
            Assert.True(can == (hint.Length == 0), "不可用档必须给人话原因，可用档必须闭嘴（否则提示常驻压着目标框）");

            foreach (var (casting, interruptible) in new[] { (true, true), (true, false), (false, true), (false, false) })
            {
                var canI = HundunWorld.Game.Combat.MonsterCombatActionRules.CanInterrupt(
                    c.Has, c.Monster, c.Selectable, casting, interruptible, c.Unattackable);
                // 关系断言（比抄一遍式子有用）：能打断了就一定能嘲讽；嘲讽不许看读条脸色。
                Assert.True(!canI || can, "打断放行了一个连嘲讽都不该成立的目标");
                var hintI = HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(
                    c.Has, c.Monster, c.Selectable, casting, interruptible, c.Unattackable);
                Assert.True(canI == (hintI.Length == 0), $"打断档无提示/有提示与可用性不同：{c} {casting}/{interruptible}");
                Assert.True(canI == (can && casting && interruptible),
                            $"打断真值表错档：{c} casting={casting} interruptible={interruptible} 实得 {canI}");
            }
        }
        foreach (var c in tauntCombos)
        {
            var a = HundunWorld.Game.Combat.MonsterCombatActionRules.CanTaunt(c.Has, c.Monster, c.Selectable, c.Unattackable);
            Assert.True(a == HundunWorld.Game.Combat.MonsterCombatActionRules.CanTaunt(c.Has, c.Monster, c.Selectable, c.Unattackable),
                        "嘲讽不该依赖读条状态");
        }

        // ①b 伤害那一手：它**不看**"是不是怪"（PVP/物品另说），但必须看"打得到吗"。
        //     关系断言：能出手 ⇒ 一定选中且没倒下；而"正在回位"必须单独挡住这一手。
        for (var m = 0; m < 8; m++)
        {
            var (dHas, dSel, dUn) = ((m & 1) != 0, (m & 2) != 0, (m & 4) != 0);
            var canD = HundunWorld.Game.Combat.MonsterCombatActionRules.CanDamage(dHas, dSel, dUn);
            Assert.True(canD == (dHas && dSel && !dUn),
                        $"出手真值表错档：has={dHas} selectable={dSel} unattackable={dUn} 实得 {canD}");
            var hintD = HundunWorld.Game.Combat.MonsterCombatActionRules.DamageHint(dHas, dSel, dUn);
            Assert.True(canD == (hintD.Length == 0), "出手不可用时必须念一句，可用时不许常驻");
            if (dUn && dSel && dHas)
                Assert.Equal("它正在跑回原位，这一下打不到", hintD);
            if (dUn) Assert.True(!canD, "打不到这件事必须真的挡住这一手，而不是只在提示里提一句");
        }

        // ② 原因优先级：越靠近"根本没选中"的越先说（玩家先要听见的是这一层）。
        // **必须用"多个原因同时成立"的档来测**：每档只翻一个旗标的写法，把顺序颠倒也照样全绿
        //（本轮反证 N4 就是这么绿的——判据漏掉了这条形状，不是产品缺陷）。
        Assert.Equal("没有选中目标", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(false, false, false, false, false, false));
        Assert.Equal("没有选中目标", HundunWorld.Game.Combat.MonsterCombatActionRules.TauntHint(false, false, false, false));
        Assert.Equal("嘲讽只对怪物有效", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(true, false, false, false, false, false));
        Assert.Equal("目标已经倒下了", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(true, true, false, false, false, false));
        Assert.Equal("它现在没在读条", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(true, true, true, false, false, false));
        Assert.Equal("没有选中目标", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(false, true, true, true, true, true));
        Assert.Equal("嘲讽只对怪物有效", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(true, false, true, true, true, true));
        Assert.Equal("目标已经倒下了", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(true, true, false, true, true, true));
        Assert.Equal("它现在没在读条", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(true, true, true, false, true, false));
        Assert.Equal("这条读条不可打断", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(true, true, true, true, false, false));
        // "倒下"与"回位"同时成立时先说倒下（更不可逆），"没在读条"与"回位"同时成立时先说回位（更靠近原因）
        Assert.Equal("目标已经倒下了", HundunWorld.Game.Combat.MonsterCombatActionRules.TauntHint(true, true, false, true));
        Assert.Equal("它正在跑回原位，拉不动", HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(true, true, true, false, false, true));
        Assert.Equal("目标已经倒下了", HundunWorld.Game.Combat.MonsterCombatActionRules.DamageHint(true, false, true));
        Assert.Equal("没有选中目标", HundunWorld.Game.Combat.MonsterCombatActionRules.DamageHint(false, false, true));

        // ③ 键位：判据里那两个标签必须与选中层轮询的键**同一个字母**，且这两个字母在客户端只此一处
        var root = RepoRoot();
        var selPath = Path.Combine(root, "HundunWorld", "Source", "Game", "Selection", "WorldSelectionService.cs");
        var sel = File.ReadAllText(selPath);
        foreach (var (key, method) in new[] { ("KeyboardKeys.X", "TryInterruptSelected"), ("KeyboardKeys.T", "TryTauntSelected") })
        {
            Assert.True(CountOccurrences(sel, $"Input.GetKeyDown({key})") == 1, $"{key} 在选中层不是恰好一处");
            Assert.True(sel.IndexOf(key, StringComparison.Ordinal) < sel.IndexOf(method + "();", StringComparison.Ordinal),
                        $"{key} 与 {method} 的先后不对：键位没接上那个动作");
            var filesThatUse = Directory.GetFiles(Path.Combine(root, "HundunWorld", "Source", "Game"), "*.cs", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains(key + ")")).ToList();
            Assert.True(filesThatUse.Count == 1,
                        $"{key} 被 {filesThatUse.Count} 个客户端文件用了（{string.Join(",", filesThatUse.Select(Path.GetFileName))}）：又去抢别人的键了");
        }

        // ④ 两条上报必须有**生产**调用方（这正是本轮补的那一格；以前是零）
        var callers = new[] { "ReportTauntById", "ReportInterruptById" };
        foreach (var name in callers)
        {
            var hits = Directory.GetFiles(Path.Combine(root, "HundunWorld", "Source", "Game"), "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => File.ReadAllLines(f).Where(l => l.Contains(name) && !l.TrimStart().StartsWith("///") && !l.Contains("public static void")))
                .ToList();
            Assert.True(hits.Count >= 1, $"{name} 又回到没有调用方的状态了：玩家打不到这一枪");
        }

        // ④b 两个入口都得真的走到选中层：只有快捷键、按钮是装饰，或者反着都不算通。
        var hudPath = Path.Combine(root, "HundunWorld", "Source", "Game", "UI", "Ink", "Pages", "Combat",
                                   "CombatHudTraditionalPage.cs");
        var hud = File.ReadAllText(hudPath);
        foreach (var method in new[] { "TryInterruptSelected", "TryTauntSelected" })
        {
            Assert.True(CountOccurrences(hud, method + "()") >= 1, $"目标框按钮没调 {method}：那个按钮是装饰品");
        }

        // ④c 判据只有一份：快捷键那一路与按钮那一路都必须问它
        var rulesCallsInSelector = CountOccurrences(sel, "MonsterCombatActionRules.");
        Assert.True(rulesCallsInSelector >= 4,
                    $"选中层只问了 {rulesCallsInSelector} 次判据：两个动作各要一次「能不能」与一次「为什么不能」，" +
                    "少一次就是本地又写了一遍");
        Assert.True(CountOccurrences(hud, "MonsterCombatActionRules.Can") >= 2,
                    "HUD 的亮/灰没用同一份判据（要么自己写了一遍，要么根本没判）");

        // ⑤ 这份判据必须保持可执行：引用引擎类型就等于把它从闸门里摘出去
        var rulesSrc = File.ReadAllText(Path.Combine(root, "HundunWorld", "Source", "Game", "Combat", "MonsterCombatActionRules.cs"));
        Assert.True(!rulesSrc.Contains("using FlaxEngine"), "判据文件引用了引擎类型：离线闸门将无法原样编进测试工程真跑");

        // ⑥ "回位中的怪打不到"这一条必须有头有尾：判据有主人 → 服务端两处都用它（拒伤 + 写状态位）
        //    → 两条下行通路共用同一个掩码函数 → 客户端有读端 → 技能栏真的问判据。
        //    少最后那一环，"打不到"就只活在服务端的账本里，玩家看到的仍然是"按了技能什么反应都没有"。
        var ds = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core", "Sim", "Server", "DsWorldSession.cs"));
        Assert.True(CountOccurrences(ds, "MonsterAiGate.BlocksIncomingDamage(") == 2,
                    $"宿主问「这一手打得到吗」问了 {CountOccurrences(ds, "MonsterAiGate.BlocksIncomingDamage(")} 次：" +
                    "应当恰好两次（拒伤那一路 + 写状态位那一路）。少一次就是两处各算一份，" +
                    "漏的那一处会表现成「客户端说打不到而服务端照样掉血」");
        Assert.True(CountOccurrences(ds, "StateBits = MonsterStateBitsOf(monster, _tick)") == 2,
                    "两条下行通路（周期快照与晚加入补发）不再共用同一个掩码主人：加一位状态就有一路漏改");
        Assert.True(CountOccurrences(ds, "StateBits = monster.Ai.Hp") == 0,
                    "快照里又出现自己拼的状态位（三元式那份）：它会静默丢掉 Evading 位");
        Assert.True(CountOccurrences(ds, "MonsterAiState.Leash ||") == 0,
                    "宿主自己写了一遍「回位中」的定义：主人（MonsterAiGate.ReturningHome）改档位时这里不跟");

        var sync = File.ReadAllText(Path.Combine(root, "HundunWorld", "Source", "Game", "FlaxActorSyncSystem.cs"));
        // 针必须下在**搬到标记那一句**上：文件里还有一句同形的 `attrs.Unattackable = stateAuth.IsEvading`
        // （那是"上次读到的值"备忘录）。只判 `Unattackable = stateAuth.IsEvading` 的话，
        // 把标记那一句接错源（改成 IsDead）也照样绿——本轮反证 N10 就是这么漏的。
        Assert.True(sync.Contains("stateTag.Unattackable = stateAuth.IsEvading"),
                    "客户端没把这一位搬到选中标记上：它就成了一个只写不读的状态位（本仓反复清的那一类）");

        var bar = File.ReadAllText(Path.Combine(root, "HundunWorld", "Source", "Game", "UI", "Ink",
                                               "ActionBar", "ActionBarService.cs"));
        Assert.True(CountOccurrences(bar, "MonsterCombatActionRules.CanDamage") == 1
                    && CountOccurrences(bar, "MonsterCombatActionRules.DamageHint") == 1,
                    "技能栏没问这条判据（或问了却没念原因）：玩家按下去只会看到什么都没发生");
        Assert.True(bar.Contains("selected?.Unattackable"),
                    "技能栏问了判据但没把它读到的那一位传进去：判据只能答「能」，等于白问");
        // 挡在冷却之前：先 StartCooldown 再拒 = 白扣一次冷却，比白挨一次拒绝更难归因
        Assert.True(bar.IndexOf("MonsterCombatActionRules.CanDamage", StringComparison.Ordinal)
                    < bar.IndexOf("StartCooldown(skillId)", StringComparison.Ordinal),
                    "拒伤检查排在 StartCooldown 之后：打不到的那一手会白吃一次冷却");

        var comp = File.ReadAllText(Path.Combine(root, "Horizon.Game.Message", "Sync", "Components", "SyncComponents.cs"));
        Assert.True(CountOccurrences(comp, "Evading = 1u << 5") == 1,
                    "回位那一位的位号出现了多份定义：客户端与服务端会各数各的");

        // ⑥b 同一件事不许有两套说法：技能栏挡了，目标框那两个按钮与头顶名牌也必须吃同一位。
        //     （少了这一条，玩家会看到"技能灰了而嘲讽按钮还描着金边"，而按下去照样被服务端拒。）
        Assert.True(CountOccurrences(hud, "unattackable") >= 4,
                    "目标框的亮/灰与那句提示根本没吃这一位：同一件事在 HUD 上还是旧口径");
        Assert.True(CountOccurrences(hud, "selected.Unattackable") == 1,
                    "HUD 里这一位的**来源**漂了（不再从选中标记读）：变量还在，值却是别的或恒 false");
        // 三个动作（普攻 H / 打断 X / 嘲讽 T）各读一次这一位：少一次就是那一个动作自己判。
        // 2026-10-31 补普通攻击后由 2 变 3——这条计数跟着动作数走，不许放宽成 >=1（那会放过新增动作漏读）。
        Assert.True(CountOccurrences(sel, "tag.Unattackable") == 3,
                    $"选择层把这一位传给判据传了 {CountOccurrences(sel, "tag.Unattackable")} 次（三个动作各一次）：" +
                    "少一次就是那一个动作自己判");
        var plate = File.ReadAllText(Path.Combine(root, "HundunWorld", "Source", "Game", "UI", "GameMain", "NameplateUI.cs"));
        Assert.True(plate.Contains("attrs.Unattackable"),
                    "名牌没读这一位：打不到的怪头顶仍挂着一条普通敌对血条，玩家只能靠按下去试");
    }

    /// <summary>造一组"外观配到一半"的行：四只怪分别代表 可加载 / 只填Guid / 什么都没填 / 没有模型键。</summary>
    private static Horizon.MonsterCli.MonsterVisualRow[] VisualFixture() => new[]
    {
        new Horizon.MonsterCli.MonsterVisualRow(3, "冥羽鸩", "ming_yu_zhen", "GUID-3", "Content/Game/Monsters/Zhen.prefab", 1.2, 0),
        new Horizon.MonsterCli.MonsterVisualRow(2, "石甲狰", "shi_jia_zheng", "GUID-2", null, 1.0, 0),
        new Horizon.MonsterCli.MonsterVisualRow(4, "银爪狸", "yin_zhua_li", null, "   ", 1.0, 3),
        new Horizon.MonsterCli.MonsterVisualRow(5, "无键怪", "", "GUID-5", "Content/Game/Monsters/None.prefab", 1.0, 0),
    };

    [Fact]
    public void 外观表只收能加载的条目并把漏配逐条点名()
    {
        var result = Horizon.MonsterCli.MonsterVisualsExport.Build(VisualFixture());

        // 只有"模型键 + 路径"齐备的那只进表。
        Assert.Equal(1, result.Count);

        // 三条漏配都要点名，且带得上人能认的身份（编号或模型键）——
        // 只报"有 3 条问题"等于让策划以为不影响上线。
        Assert.Equal(3, result.Problems.Count);
        Assert.Contains(result.Problems, p => p.Contains("2") && p.Contains("shi_jia_zheng") && p.Contains("只填了 Guid"));
        Assert.Contains(result.Problems, p => p.Contains("4") && p.Contains("没有 prefab 路径"));
        Assert.Contains(result.Problems, p => p.Contains("5") && p.Contains("没有 model_key"));

        // 产出的 JSON 必须被读侧认得：逐键断言，而不是"看起来差不多"。
        using var doc = System.Text.Json.JsonDocument.Parse(result.Json);
        var visuals = doc.RootElement.GetProperty("visuals");
        Assert.Equal(1, visuals.GetArrayLength());
        var row = visuals[0];
        Assert.Equal("ming_yu_zhen", row.GetProperty("modelKey").GetString());
        Assert.Equal("Content/Game/Monsters/Zhen.prefab", row.GetProperty("prefabPath").GetString());
        Assert.Equal(1.2, row.GetProperty("scale").GetDouble(), 3);
        Assert.Equal(0, row.GetProperty("standState").GetInt32());

        // 顺序按编号升序：库里返回顺序不保证，逐次导出的 diff 不该因此翻动。
        var ordered = Horizon.MonsterCli.MonsterVisualsExport.Build(new[]
        {
            new Horizon.MonsterCli.MonsterVisualRow(9, "后", "hou", null, "Content/hou.prefab", 1.0, 0),
            new Horizon.MonsterCli.MonsterVisualRow(7, "先", "xian", null, "Content/xian.prefab", 1.0, 0),
        });
        using var doc2 = System.Text.Json.JsonDocument.Parse(ordered.Json);
        var keys = doc2.RootElement.GetProperty("visuals").EnumerateArray()
            .Select(e => e.GetProperty("modelKey").GetString()).ToList();
        Assert.Equal(new[] { "xian", "hou" }, keys);
    }

    [Fact]
    public void 外观表的键名与客户端读取侧逐字相同()
    {
        // 读侧（Flax 脚本域）引用不到，只能像其它跨装配闸门那样比文本；
        // 漂一个键的后果是"导出成功、客户端读到的是一条都没有"——写侧自己永远全绿。
        var clientPath = Path.Combine("HundunWorld", "Source", "Game", "Selection", "MonsterVisualCatalog.cs");
        var text = File.ReadAllText(Path.Combine(RepoRoot(), clientPath));

        // 空转防护：读侧按字面量取键的调用必须数得出来（解析器变了这条就在空转）。
        var readCalls = Regex.Matches(text, "Read(?:String|Float|Int)\\(\\s*\\w+\\s*,\\s*\"(?<k>[^\"]+)\"")
            .Cast<Match>().Select(m => m.Groups["k"].Value).ToList();
        Assert.True(readCalls.Count >= 4, $"客户端读侧只数出 {readCalls.Count} 个取键调用，这条闸门正在空转");
        Assert.Contains("visuals", text);   // 顶层数组名单独取（不走 Read* 助手）

        foreach (var key in Horizon.MonsterCli.MonsterVisualsExport.ClientKeys)
        {
            Assert.Contains(key, readCalls.Concat(new[] { "visuals" }), StringComparer.Ordinal);
        }

        // 反向：写侧真正产出的 JSON 里，这些键一个都不能少（否则断言只证了"读侧还认得旧名字"）。
        var built = Horizon.MonsterCli.MonsterVisualsExport.Build(VisualFixture());
        using var doc = System.Text.Json.JsonDocument.Parse(built.Json);
        var row = doc.RootElement.GetProperty("visuals")[0];
        foreach (var key in new[] { "modelKey", "prefabPath", "scale", "standState" })
        {
            Assert.True(row.TryGetProperty(key, out _), $"导出的条目缺键 {key}，客户端会读成初值");
        }
    }

    [Fact]
    public void 外观表缓存按文件指纹失效而读表那条入口每次都问指纹()
    {
        // 实测到的静默失效（2026-10-27，第一次跑通两进程目视验收时撞上）：这张表的缓存是静态字段，
        // 而编辑器的脚本域跨 Play-in-Editor 会话存活。第一次进世界时表还没导出，缓存把"表不存在"
        // 记成了一辈子；随后把文件写出来、重开 PIE，日志原样复读"来源 表不存在"，怪照旧顶着占位人形。
        // 后果是"美术在编辑器里改一次外观要重启一次编辑器"，而且它**不报错**——
        // 静默失效最难查，所以这里钉四层：
        //   ① 机制层：缓存命中必须是"读过"与"文件指纹没变"的合取，只看那个布尔就红；
        //   ② 接线层：问指纹这件事发生在每次解析都会走的入口里，而不是只写在没人调的 Reload 里；
        //   ③ 效果层：文件从"没有"到"有"要算一次变化——缺失哨兵必须与字段初值同形，否则对不上；
        //   ④ 凭据层：读表要留一条带条数的日志，否则运行期无法把"没导出""导出成空表""键名漂了"分开。
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "Selection", "MonsterVisualCatalog.cs"));

        // 读侧方法体按 8 空格缩进切（这个类的成员都在嵌套命名空间里，通用助手按 4 空格找会切到文件尾）。
        string Body(string signature)
        {
            var start = text.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"读侧源码里找不到方法：{signature}");
            var rest = text.Substring(start + signature.Length);
            var cut = rest.IndexOf("\n        private static ", StringComparison.Ordinal);
            return cut < 0 ? rest : rest.Substring(0, cut);
        }

        var ensure = Body("private static void EnsureLoaded()");

        // ① 曾经的那句就是缺陷本身：只认 _loaded 的话，第一次读到什么就永久是什么。
        Assert.DoesNotContain("if (_loaded) return;", ensure);
        Assert.Matches(@"_loaded\s*&&", ensure);

        // ② 指纹必须在"缓存命中就早退"那句之前问过（顺序反了等于没问：命中路径根本不会去 stat 文件）。
        var stampCall = ensure.IndexOf("ReadStamp(", StringComparison.Ordinal);
        var hitGuard = ensure.IndexOf("_loaded &&", StringComparison.Ordinal);
        Assert.True(hitGuard >= 0, "读表入口里没有『读过且指纹没变』这条合取判据");
        var hitReturn = ensure.IndexOf("return;", hitGuard, StringComparison.Ordinal);
        Assert.True(stampCall >= 0, "读表入口没有取文件指纹（只可能靠 Reload，而那个入口全仓没有调用方）");
        Assert.True(stampCall < hitReturn, "取指纹发生在早退之后：命中缓存的那条路永远不会发现表变了");

        // ③ 时间戳与字节数两个都要——只比秒级时间戳的话，同一秒里重导出且长度没变的表会被判"没动过"。
        var stamp = Body("private static (DateTime, long) ReadStamp(");
        Assert.Contains("LastWriteTimeUtc", stamp);
        Assert.Contains("Length", stamp);
        Assert.Contains("return (DateTime.MinValue, -1L);", stamp);
        Assert.Contains("_fileStampUtc = DateTime.MinValue", text);
        Assert.Contains("_fileStampLength = -1L", text);

        // ④ 空转防护：这条日志必须带条数，且条数取自真正装进缓存的那份表（不是常量、也不是行数）。
        Assert.Contains("外观表已读入", ensure);
        Assert.Contains("Entries.Count", ensure);
    }

    [Fact]
    public void 导出回执数得出技能挂载也数得出真能施法的怪()
    {
        // 触发点：给排练库配完 11 行挂载后跑 export-content，回执那句是
        // "已导出 rev=… 模板 12、刷怪点 7、路线 1、掉落绑定 12"——**技能挂载整段不在里面**。
        // 于是"库里 11 行、包里 0 行"这种形状与"包完全正常"在这一行字上长得一样，
        // 而下游（DsHost 装配行、玩家那边的"怪会不会读条"）都只跟这句回执对齐。
        // 判据抽成 MonsterExportReceipt.Of ⇒ 能离线把四种形状各断一条。
        static Horizon.Game.Core.Sim.Server.MonsterTemplateSnapshot Mob(
            int mounts, int profile, int? skillsFlag = null)
        {
            var t = new Horizon.Game.Core.Sim.Server.MonsterTemplateSnapshot();
            t.Behavior.AiProfile = profile;
            t.Behavior.FlagUseSkills = skillsFlag;
            for (var i = 0; i < mounts; i++)
                t.Skills.Add(new Horizon.Game.Core.Sim.Server.MonsterSkillSnapshot { SkillId = 1000 + i, SlotSeq = i });
            return t;
        }
        var Of = new Func<
            System.Collections.Generic.IEnumerable<Horizon.Game.Core.Sim.Server.MonsterTemplateSnapshot>?,
            Horizon.MonsterCli.MonsterExportReceipt.Counts>(Horizon.MonsterCli.MonsterExportReceipt.Of);

        // ① 挂载为零：两个数都是 0，回执不许把"没有"写成"有内容"
        var none = Of(new[] { Mob(0, 0), Mob(0, 2) });
        Assert.Equal(0, none.SkillMounts);
        Assert.Equal(0, none.SkillCapableTemplates);

        // ② 挂了但档位关着（切片 40 量到的那个死数据形状）：行数要如实报，但"真能施法"必须是 0
        var dead = Of(new[] { Mob(2, 0) });
        Assert.Equal(2, dead.SkillMounts);
        Assert.Equal(0, dead.SkillCapableTemplates);

        // ③ 同一份挂载，把技能位单独打开 ⇒ 只变第二个数（证明第二格量的是能力位而不是行数）
        var armed = Of(new[] { Mob(2, 0, skillsFlag: 1) });
        Assert.Equal(2, armed.SkillMounts);
        Assert.Equal(1, armed.SkillCapableTemplates);

        // ④ 档位自带技能位（Skilled=2）时不用覆盖位也该算；行数按只累加、只数按只去重，两个数不是一回事
        var profiled = Of(new[] { Mob(1, 2), Mob(3, 2) });
        Assert.Equal(4, profiled.SkillMounts);
        Assert.Equal(2, profiled.SkillCapableTemplates);

        // ⑤ 档位开着但一行都没挂：不算"有内容"（校验器那句反向 warning 的镜像）
        var emptyButArmed = Of(new[] { Mob(0, 2) });
        Assert.Equal(0, emptyButArmed.SkillMounts);
        Assert.Equal(0, emptyButArmed.SkillCapableTemplates);

        // ⑥ null 输入不许把整条检查变成一次崩溃，也不许报成"有 0 只怪所以正常"
        Assert.Equal(0, Of(null).SkillMounts);
        Assert.Equal(0, Of(null).SkillCapableTemplates);

        // 接线层：这两个数必须真的进了 CLI 那句回执与 data，否则纯函数写得再好也只是摆设
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.MonsterCli", "Program.cs"));
        const string entry = "private static int RunExportContent";
        var entryAt = program.IndexOf(entry, StringComparison.Ordinal);
        Assert.True(entryAt >= 0, $"Program.cs 里找不到 {entry}");
        var afterEntry = program.Substring(entryAt);
        var nextAt = afterEntry.IndexOf("private static ", entry.Length, StringComparison.Ordinal);
        var export = nextAt < 0 ? afterEntry : afterEntry.Substring(0, nextAt);
        Assert.Contains("MonsterExportReceipt.Of", export);
        Assert.Contains("skillMounts", export);
        Assert.Contains("skillCapableTemplates", export);
        // "技能挂载"那句要钉在**格式串那一行**上，不能对整个方法体扫：
        // 上面那句注释里也写着"技能挂载"，反证 R3 把格式串里的那半句删掉后，
        // 宽判据仍然命中注释 ⇒ 绿灯，而这正是"回执不再念挂载"那个缺陷本身（注释不是给玩家看的字）。
        var formatLine = export.Split('\n')
            .FirstOrDefault(l => l.Contains("string.Format(\"已导出", StringComparison.Ordinal));
        Assert.NotNull(formatLine);
        Assert.Contains("技能挂载", formatLine);
        Assert.Contains("{5}", formatLine);
        Assert.Contains("{6}", formatLine);

        // 机制层：能力位只问唯一主人，回执侧不许再抄一张档位表
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.MonsterCli", "MonsterExportReceipt.cs"));
        Assert.Contains("MonsterAiFlags.Resolve(", receipt);
        Assert.DoesNotContain("AiProfile ==", receipt);
        Assert.DoesNotContain("AiProfile >=", receipt);
    }

    [Fact]
    public void 内容普查把机制就绪而内容为零几种读数各自念出来而空普查不许报绿()
    {
        // 这条普查第一次跑就抓到：11 只种子怪零行技能挂载、导航位与求援位全关，而全仓每一道代码闸门都是绿的
        // （种子是 SQL 绕过校验器写进库的，所以"挂了技能但档位关着"那两句 warning 从没响过的机会）。
        // 判据抽成纯函数 MonsterAuditCensus ⇒ 可以离线断言。断的是"什么时候该念哪一句"，不是"库里今天长什么样"。
        static bool Says(Horizon.MonsterCli.MonsterAuditResult r, string needle)
            => r.Findings.Any(f => f.Contains(needle, StringComparison.Ordinal));
        Horizon.MonsterCli.MonsterAuditRow Row(
            int id, int mounts = 0, bool skills = false, bool nav = false, bool assist = false,
            int lootBindings = 0, int lootEntries = 0, bool money = false,
            bool route = false, bool corpse = false, bool text = false,
            float lootWindowSeconds = 0f, int normalLootEntries = 0,
            int groupLootEntries = 0, int conditionalLootEntries = 0,
            int aiProfile = 0, int followType = 0)
            => new Horizon.MonsterCli.MonsterAuditRow(id, $"怪{id}", mounts, skills, nav, assist,
                                                      lootBindings, lootEntries, money, route, corpse, text,
                                                      lootWindowSeconds, normalLootEntries,
                                                      groupLootEntries, conditionalLootEntries, aiProfile, followType);
        // 一只"每一格都配过"的怪：④⑤ 用它来证明"内容齐了就闭嘴"。
        // 拾取窗口给了 60 秒、普通件候选 3 件（默认 0 / 0 会命中"窗口为 0"与"超上限"那两句新判据）。
        // 档位/移动方式按下标取不同值：内容是"多样"的，这两条形状判据才该闭嘴。
        Horizon.MonsterCli.MonsterAuditRow Full(int id, int mounts)
            => Row(id, mounts, true, true, true, 1, 3, true, true, true, true,
                   lootWindowSeconds: 60f, normalLootEntries: 3,
                   groupLootEntries: 2, conditionalLootEntries: 1,
                   aiProfile: id % 2 == 0 ? 3 : 2, followType: id % 2 == 0 ? 2 : 0);

        // ① 空普查必须自陈"没跑起来"，而不是留一份看起来干净的零发现
        var empty = Horizon.MonsterCli.MonsterAuditCensus.Build(Array.Empty<Horizon.MonsterCli.MonsterAuditRow>());
        Assert.Equal(0, empty.Published);
        Assert.Single(empty.Findings);
        Assert.True(Says(empty, "普查什么都没看到"), "0 只怪时这条普查要自陈没跑起来，不能报成\"内容没问题\"");

        // ② 11 只全零：七句各念一次（技能/导航/求援/掉落/巡逻线/尸体停留/文本），读数如实
        var allDumb = Horizon.MonsterCli.MonsterAuditCensus.Build(
            Enumerable.Range(1, 11).Select(i => Row(i)).ToList());
        Assert.Equal(11, allDumb.Published);
        Assert.Equal(0, allDumb.SkillMounts);
        Assert.Equal(9, allDumb.Findings.Count);   // 7 条旧 + 档位单值 + 移动方式单值
        Assert.True(Says(allDumb, "技能这一格全仓为零"));
        Assert.True(Says(allDumb, "没有一只怪开着导航位"));
        Assert.True(Says(allDumb, "没有一只怪开着求援位"));
        Assert.True(Says(allDumb, "掉落全仓为零"));
        Assert.True(Says(allDumb, "没有一只怪挂巡逻线"));
        Assert.True(Says(allDumb, "没有一只怪配尸体停留秒"));
        Assert.True(Says(allDumb, "没有一只怪配起手喊话或起手动作"));
        Assert.True(Says(allDumb, "只用了 1 个值"), "档位/移动方式都是单值时要各念一句（两句都含这句）");
        Assert.Equal(2, allDumb.Findings.Count(f => f.Contains("只用了 1 个值")));

        // ③ 有挂载但零能力位是**另一种**坏：只判"零挂载"会把它读成"有内容，挺好"
        var deadData = Horizon.MonsterCli.MonsterAuditCensus.Build(new[]
        {
            Row(1, 2, nav: true, assist: true), Row(2, 1, nav: true, assist: true),
        });
        Assert.Equal(3, deadData.SkillMounts);
        Assert.Equal(2, deadData.MonstersWithSkills);
        Assert.Equal(0, deadData.SkillCapable);
        Assert.True(Says(deadData, "这些行全部是死数据"));
        Assert.False(Says(deadData, "技能这一格全仓为零"), "有挂载时不该念零挂载那句（两种坏必须可分辨）");
        Assert.Empty(deadData.Findings.Where(f => f.Contains("导航位") || f.Contains("求援位")));

        // ③b 掉落与金币是绑定上的两格，也要能分辨：有条目、零金币 ⇒ 只念金币那句
        var lootNoMoney = Horizon.MonsterCli.MonsterAuditCensus.Build(new[]
        {
            Row(1, 1, true, true, true, lootBindings: 2, lootEntries: 5),
        });
        Assert.Equal(5, lootNoMoney.LootEntries);
        Assert.Equal(2, lootNoMoney.LootBindings);
        Assert.True(Says(lootNoMoney, "却没有一只怪配金币区间"));
        Assert.False(Says(lootNoMoney, "掉落全仓为零"), "有条目时不该念掉落为零那句");

        // ④ 内容齐了就闭嘴：这条防的是"无条件念"的实现（②防的是反方向）
        var healthy = Horizon.MonsterCli.MonsterAuditCensus.Build(new[]
        {
            Full(1, 1), Full(2, 0),
        });
        Assert.Empty(healthy.Findings);
        Assert.Equal(1, healthy.MonstersWithSkills);   // 挂载按行累加、"挂了的怪"按只累加，两个数不是一回事
        Assert.Equal(1, healthy.SkillMounts);
        Assert.Equal(2, healthy.SkillCapable);
        Assert.Equal(6, healthy.LootEntries);         // 两张表各 3 行条目
        Assert.Equal(2, healthy.MonstersWithLoot);

        // ⑤ 校验器那几句话必须原样透传：普查不许替它们做"看起来不重要就丢掉"的决定
        var passed = Horizon.MonsterCli.MonsterAuditCensus.Build(
            new[] { Full(1, 1) },
            new[] { "怪物 1：世界BOSS 不吃 leashRadius", "怪物 1：检测位无人消费" });
        Assert.Equal(2, passed.Findings.Count);
        Assert.True(Says(passed, "leashRadius") && Says(passed, "检测位"));

        // ⑥ 孤儿行要单独念：判据第一版把怪物 500001 报成了孤儿，那是**判据错**——
        //    它是 NPC 编辑器的"敌对样板"（EntityType=Monster，022 种子自带掉落），与怪物编辑器共用同一张掉落从表。
        //    ⇒ "存不存在"要问两张模板表；这类红当成数据缺陷去改检查，就是把真内容报成缺陷。
        var orphaned = Horizon.MonsterCli.MonsterAuditCensus.Build(
            new[] { Full(1, 1) }, null,
            new List<(string, int, int)> { ("掉落绑定", 7, 999999), ("逐件条目", 11, -7), ("逐件条目", 12, -7) });
        Assert.Equal(3, orphaned.OrphanRows);
        Assert.Single(orphaned.Findings);
        Assert.True(Says(orphaned, "怪物表与 NPC 表里都不存在"), "孤儿行必须点名，不能只算进内容读数");
        Assert.True(Says(orphaned, "999999"), "孤儿那句要带上挂错的编号");
        Assert.True(Says(orphaned, "逐件条目 2 行"), "同类孤儿要并成一组计数，而不是逐行刷屏");

        // ⑥b 没有孤儿时不许念这句（防"无条件念"的实现）
        var noOrphan = Horizon.MonsterCli.MonsterAuditCensus.Build(new[] { Full(1, 1) }, null, new List<(string, int, int)>());
        Assert.False(Says(noOrphan, "都不存在"));

        // ⑦ 两格"配了却拿不到 / 多出来的被丢掉"：窗口 0 与普通件候选超上限各念一句，计数分开
        var badLoot = Horizon.MonsterCli.MonsterAuditCensus.Build(new[]
        {
            Row(1, 1, true, true, true, lootBindings: 1, lootEntries: 3, money: true, route: true, corpse: true, text: true,
                lootWindowSeconds: 0f, normalLootEntries: 3),
            Row(2, 1, true, true, true, lootBindings: 1, lootEntries: 25, money: true, route: true, corpse: true, text: true,
                lootWindowSeconds: 60f, normalLootEntries: 25),
        });
        Assert.Equal(1, badLoot.LootWindowClosedWithDrops);
        Assert.Equal(1, badLoot.LootCandidatesOverCap);
        Assert.True(Says(badLoot, "可拾取窗口是 0"), "有掉落但窗口 0 必须当场念：内容配了、玩家永远拿不到");
        Assert.True(Says(badLoot, "超过一具尸体上限 18 件"), "候选超上限要念：多出来的运行期会被丢");
        Assert.True(Says(badLoot, "怪1") && Says(badLoot, "怪2(25)"), "两句都要点名到具体怪");
        Assert.False(Says(badLoot, "掉落全仓为零"), "有掉落时不该念掉落为零那句");
        // ⑦b 内容形状：有掉落但一件都没进互斥组 / 一件都不按条件出账，各念一句；⑧ 的正例（Full）一句不响
        var flatLoot = Horizon.MonsterCli.MonsterAuditCensus.Build(new[]
        {
            Row(9, 1, true, true, true, lootBindings: 1, lootEntries: 4, money: true, route: true, corpse: true, text: true,
                lootWindowSeconds: 60f, normalLootEntries: 4, groupLootEntries: 0, conditionalLootEntries: 0),
        });
        Assert.Equal(4, flatLoot.LootEntries);
        Assert.Equal(0, flatLoot.LootGroupEntries);
        Assert.Equal(0, flatLoot.LootConditionalEntries);
        Assert.True(Says(flatLoot, "没有任何一件挂在互斥组里"), "全是逐件独立掷却没有一句提示：那种\"必出 1 件\"的配法就没被写出来过");
        Assert.True(Says(flatLoot, "没有一件是按条件出账的"), "条件件一条都没有 ⇒ LootConditional 那条链没有触发者");
        Assert.False(Says(healthy, "没有任何一件挂在互斥组里"));
        Assert.False(Says(healthy, "没有一件是按条件出账的"));

        // 正例对照：窗口正常、候选不超限 ⇒ 这两句一句都不许响
        Assert.False(Says(healthy, "可拾取窗口是 0"));
        Assert.False(Says(healthy, "超过一具尸体上限"));
    }

    [Fact]
    public void 外观表为空时仍产出可读结构并显式报问题()
    {
        // "一只怪都没配"与"装配失败"必须可分辨：空表也要是一份合法 JSON + 一条说明。
        var result = Horizon.MonsterCli.MonsterVisualsExport.Build(Array.Empty<Horizon.MonsterCli.MonsterVisualRow>());
        Assert.Equal(0, result.Count);
        Assert.NotEmpty(result.Problems);
        using var doc = System.Text.Json.JsonDocument.Parse(result.Json);
        Assert.Equal(0, doc.RootElement.GetProperty("visuals").GetArrayLength());
    }

    [Fact]
    public void 击杀金币的每一跳都在盘上而校验器不再对配了金币的草稿报警()
    {
        // 金币这一路以前断在中间：编辑器能填、内核会掷、DS 有计票，但没有任何写入端，
        // 于是「配了 10~20 金」与「玩家口袋里一个铜板都没多」之间没有一条报错。
        // 链路接上以后，校验器那句「DS 侧还没有货币入账端」就成了**假话**（每填必响的噪声
        // 还会把真问题埋掉），所以这里换成两件事：① 链路上每一环逐字各存在一处，少一跳当场红；
        // ② 校验器对配了金币的草稿保持安静，而区间非法仍是硬错（那是另一件事）。
        var root = RepoRoot();
        var session = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core/Sim/Server/DsWorldSession.cs"));
        var wiring = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core/Sim/Server/OrleansGameplayWiring.cs"));
        var adapters = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core/Sim/Server/OrleansDsGrainAdapters.cs"));
        var gateway = File.ReadAllText(Path.Combine(root, "Horizon.Game.Gateway/Program.cs"));
        var types = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core/Sim/Server/DsCombatTypes.cs"));
        var uplink = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core/Sim/Server/DsUplinkProtocol.cs"));

        // 1) DS 侧：死亡按掷出的金额发**自己的一种**事件。挂在 Loot 上会被那条
        //    「ItemId/ItemCount 必须为正」的判据安静吞掉，所以它必须是自己的一档。
        // **两处**：击杀即时发放（无尸体 / 未接权威存储的旧路径）与尸体拾取（2026-10-29 拍板 D23）。
        // 两条都有判据守着（KillChain 走旧路径、接入权威存储那条走拾取路径），所以这里钉的是
        // "恰好两处、且都发同一种事件"，而不是"只有一处"——前者才描述今天的事实。
        Assert.Equal(2, CountCodeOccurrences(session, "Kind = DsGameplayEventKind.KillMoney"));
        // 2) 契约侧：金额字段存在，且金额进了幂等键——
        //    漏了这一项，同人同 tick 杀两只不同额的怪会被去重吞掉一笔真钱。
        Assert.Equal(1, CountCodeOccurrences(types, "Money.TryFormat(buf.Slice(pos), out written)"));
        // 3) 消费方：有这一档，且没接线时走「计数 + 日志」而不是静默（判据见那条跳过用例）。
        Assert.Equal(1, CountCodeOccurrences(wiring, "case DsGameplayEventKind.KillMoney:"));
        Assert.Equal(1, CountCodeOccurrences(wiring, "GoldSkippedNoWriter +="));
        Assert.Equal(1, CountCodeOccurrences(wiring, "GoldCredited +="));
        // 4) 写入端：调的是钱包那侧的加钱接口，回执要逐字对账（不是「没抛异常就算成功」），
        //    并且组合根真的把它塞进了事件消费方——只有类没有装配等于没接。
        Assert.Equal(1, CountCodeOccurrences(adapters, "GetGrain<Horizon.Orleans.Interface.ICurrencyGrain>((long)characterId)"));
        Assert.Equal(1, CountCodeOccurrences(adapters, "mutation is { Ok: true } && mutation.BalanceAfter - mutation.BalanceBefore == amount"));
        Assert.Equal(1, CountCodeOccurrences(gateway, "currency: new Horizon.Game.Core.Sim.Server.OrleansCurrencyWriter(clusterClient)"));
        // 5) 线上契约：DS 上送给网关走的是 DTO，不是内核结构体。这一跳以前没人钉，
        //    于是内核新加的金额格在 DTO 里不存在——生产链路金额归零、单测（直接喂内核事件）全绿。
        //    逐格往返与成员名集合的判据在 DsUplinkProtocolTests；这里钉"这一跳真的带着金额"。
        Assert.Equal(1, CountCodeOccurrences(uplink, "public long Money { get; set; }"));
        Assert.Equal(1, CountCodeOccurrences(uplink, "Money = Money,"));
        Assert.Equal(1, CountCodeOccurrences(uplink, "Money = e.Money,"));

        var withMoney = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100003, Name = "衔烛" };
        withMoney.ModelKey = "xian_zhu";
        withMoney.LootBindings.Add(new Horizon.MonsterCli.MonsterLootBindingDraft
        {
            LootTableId = 1,
            RollChanceBps = 10000,
            MoneyMin = 10,
            MoneyMax = 20,
            Entries = { new Horizon.MonsterCli.MonsterDropDraft { ItemId = 50001, ChanceBps = 10000 } },
        });

        var warned = Horizon.MonsterCli.MonsterValidator.Validate(withMoney, null);
        Assert.True(warned.Ok, "合法草稿被拒：" + string.Join(";", warned.Errors));
        Assert.DoesNotContain(warned.Warnings, w => w.Contains("货币入账端"));

        // 区间非法仍然是硬错，和「有没有入账端」是两件事。
        withMoney.LootBindings[0].MoneyMin = 30;
        withMoney.LootBindings[0].MoneyMax = 20;
        var inverted = Horizon.MonsterCli.MonsterValidator.Validate(withMoney, null);
        Assert.False(inverted.Ok);
        Assert.Contains(inverted.ErrorPaths, p => p.EndsWith("moneyMax"));
    }

    [Fact]
    public void 由应用赋值的业务键不许是自增列而种子要盖住所有必填列()
    {
        // 这一轮是**真跑**出来的（独立演练库，不碰生产库）：
        // 020 第一次跑出 3 条 Msg 544（往 IDENTITY 列显式插值）+ 3 条 Msg 515（NOT NULL 无默认却没给值），
        // 0 行进库；而保存路径 `new MonsterTemplateEntity { Id = draft.MonsterId }` 本来就在应用侧赋 Id
        // ⇒ 列是 IDENTITY 时 EF 会**静默丢掉应用给的值**，模板存成 id=1 而子表仍指向 100003，父子对不上且不报错。
        var root = RepoRoot();
        // 实体用 `new int Id` 隐藏基类的同名属性 ⇒ GetProperty("Id") 会抛 AmbiguousMatchException，
        // 必须按 DeclaredOnly 取"这一层自己声明的那个 Id"（否则闸门连读都读不到，红在框架而不是判据上）。
        static System.Reflection.PropertyInfo IdOf(Type t)
            => t.GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly
                                         | System.Reflection.BindingFlags.Instance)!;
        var generated = (System.Reflection.PropertyInfo p) =>
            p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedAttribute), false)
             .Cast<System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedAttribute>()
             .FirstOrDefault()?.DatabaseGeneratedOption;

        // ① 四张"键由应用赋值"的表必须是 None（否则上面那个静默错配就会发生）
        foreach (var t in new[]
        {
            typeof(Horizon.Model.GameModel.MonsterTemplateEntity),
            typeof(Horizon.Model.GameModel.MonsterBehaviorEntity),
            typeof(Horizon.Model.GameModel.MonsterSpawnPointEntity),
            typeof(Horizon.Model.GameModel.MonsterPatrolRouteEntity),
        })
        {
            var id = IdOf(t);
            Assert.True(id != null, $"{t.Name} 没有 Id 属性");
            Assert.Equal(System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedOption.None,
                         generated(id!));
        }

        // ② 另外四张是纯代理键（保存时不赋 Id），它们**必须仍是自增**——
        //    反过来钉住"这一族不是一刀切改掉的"，也让将来给它们赋 Id 的人必须先改这里。
        foreach (var t in new[]
        {
            typeof(Horizon.Model.GameModel.MonsterPatrolPointEntity),
            typeof(Horizon.Model.GameModel.MonsterSkillMountEntity),
            typeof(Horizon.Model.GameModel.MonsterLootBindingEntity),
            typeof(Horizon.Model.GameModel.MonsterDropEntryEntity),
        })
        {
            Assert.True(generated(IdOf(t)) == null
                        || generated(IdOf(t))
                             == System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedOption.Identity,
                        $"{t.Name} 的 Id 被改成了非自增，但保存路径并不赋 Id");
        }

        // ③ 建表脚本里怪物表只剩 4 个自增列，且四个业务键都不在其中
        var ddl = File.ReadAllText(Path.Combine(root, "scripts/sql/019_monster_editor_tables.sql"));
        var identityColumns = new List<string>();
        foreach (var line in ddl.Split('\n'))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line.Trim(), @"^\[(\w+)\]\s+\w+.*IDENTITY");
            if (m.Success) identityColumns.Add(m.Groups[1].Value);
        }
        Assert.Equal(4, identityColumns.Count);
        foreach (var authored in new[] { "monster_id", "spawn_point_id", "route_id" })
            Assert.DoesNotContain(authored, identityColumns);
        Assert.Contains("entry_id", identityColumns);

        // ④ 种子与建表是两份清单：每张被种的表，其"NOT NULL 且没有 DEFAULT"的列都必须出现在 INSERT 列表里。
        //    这条就是 Msg 515 那一族（后加的 xp_mod_bps/health_mod_bps 与基类的 IsValid/GameId/…）。
        var seed = File.ReadAllText(Path.Combine(root, "scripts/sql/020_seed_monster_templates.sql"));
        var required = new Dictionary<string, HashSet<string>>();
        foreach (System.Text.RegularExpressions.Match cm in
                 System.Text.RegularExpressions.Regex.Matches(
                     ddl, @"CREATE TABLE \[(\w+)\]\s*\((.*?)\n\);",
                     System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            var set = new HashSet<string>();
            foreach (var line in cm.Groups[2].Value.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line.Trim(), @"^\[(\w+)\]\s+[\w\(\),]+(.*)$");
                if (!m.Success) continue;
                var rest = m.Groups[2].Value;
                if (rest.Contains("NOT NULL") && !rest.Contains("DEFAULT")) set.Add(m.Groups[1].Value);
            }
            required[cm.Groups[1].Value] = set;
        }
        var covered = new Dictionary<string, HashSet<string>>();
        foreach (System.Text.RegularExpressions.Match mm in
                 System.Text.RegularExpressions.Regex.Matches(
                     seed, @"MERGE INTO \[(\w+)\] AS t(.*?)(?=\nMERGE INTO|\nCOMMIT;|\Z)",
                     System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            var ins = System.Text.RegularExpressions.Regex.Match(
                mm.Groups[2].Value, @"INSERT \(([^)]*)\)\s*\r?\n\s*VALUES",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            Assert.True(ins.Success, $"{mm.Groups[1].Value}：种子块里找不到 INSERT 列表");
            var names = ins.Groups[1].Value.Split('\n')
                .SelectMany(l => l.Split(','))
                .Select(s => s.Trim().TrimEnd(')'))
                .Where(s => s.Length > 0 && s != "VALUES")
                .ToHashSet(StringComparer.Ordinal);
            covered[mm.Groups[1].Value] = names;
        }
        foreach (var (table, need) in required)
        {
            if (!covered.TryGetValue(table, out var got)) continue;      // 这张表不由种子负责
            var missing = need.Where(c => !got.Contains(c)).OrderBy(c => c).ToList();
            Assert.True(missing.Count == 0,
                        $"种子 {table} 没给值而列是 NOT NULL 无默认：{string.Join(",", missing)}" +
                        "（真跑会 Msg 515、整条 MERGE 失败，而 0 行进库）");
        }
    }

    [Fact]
    public void 存库路径给自增主键的表赋编号会被库顶回来而反射取编号必须走DeclaredOnly()
    {
        // 这一条是"第一次真库逐格往返"跑出来的（Tools/db-roundtrip-check.py，演练库 Game_MonsterRT，
        // 不碰生产库）。之前这条链从没真跑过，两个缺陷因此一直看不见：
        //  ① 上一轮把"只有四张表赋 Id"当成了事实，其实**六张**都赋（NextId 写在这四张的 Add 里），
        //     库侧就是 `IDENTITY_INSERT 为 OFF 时不许插显式值`——任何带技能/掉落/巡逻线的怪**一次都存不进去**；
        //  ② NextId 里 `GetProperty("Id")` 对这些用 `public new int Id` 的类会抛 AmbiguousMatchException，
        //     而它被一句空 catch 吞掉 ⇒ 库内最大值静默算成 0 ⇒ 发号从 1 起，下一次就撞主键。
        // 另外顺手修了巡逻线那一支：草稿指名的路线号被换成另发的号，且新行只在 Id==0 时才 Add（新行永远不是 0）
        // ⇒ 那条线整条不入库，而行为表的 route_id 指着一条不存在的路线。
        var root = RepoRoot();
        var store = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "MonsterStore.cs"));

        // ① 四张仍是 IDENTITY 的子表，保存路径一律不许赋 Id（赋了就是整次保存回滚）。
        foreach (var set in new[] { "MonsterPatrolPoints", "MonsterSkillMounts",
                                    "MonsterLootBindings", "MonsterDropEntries" })
            Assert.True(CountCodeOccurrences(store, $"Id = NextId(_db.{set})") == 0,
                        $"保存路径给 {set}（仍是 IDENTITY）赋了编号：" +
                        "INSERT 会被 SQL Server 顶回来，整次保存一起回滚，表现是『编辑器点保存没反应』");

        // ② 反向对照：不许把这条读成"一律不许发号"——route_id 由应用赋值，这一格必须在。
        //    （没有这条，把 NextId 整句删掉也能全绿。）
        Assert.True(CountCodeOccurrences(store, "Id = NextId(_db.MonsterPatrolRoutes)") >= 1,
                    "巡逻线的发号没了：草稿留 routeId=0 时新线会拿 0 号入库并撞主键");

        // ③ 反射取编号必须走 DeclaredOnly，且不许留空 catch 把抛错咽成"最大值 0"。
        //    以及草稿指名的路线号必须照用（上一轮换成另发的号），新行也真要被 Add
        //    （上一轮写成 `if (routeRow.Id == 0) Add` —— 新行永远不是 0，于是那条线整条不入库）。
        Assert.True(CountCodeOccurrences(store, "IdPropertyOf(") >= 2,
                    "取 Id 的那句没走 DeclaredOnly 主人（或被绕开）：这些类都是 `new int Id`，普通 GetProperty 会抛");
        Assert.True(CountCodeOccurrences(store, "typeof(T).GetProperty(\"Id\")") == 0,
                    "NextId 里又用了不带 BindingFlags 的 GetProperty(\"Id\")：对派生类隐藏基类同名属性的形状它必抛");
        Assert.True(CountCodeOccurrences(store, "new MonsterPatrolRouteEntity { Id = route.RouteId }") == 1,
                    "草稿指名的巡逻线号又被换成 CLI 另发的号了：作者点的号与库里存的号分家");
        Assert.True(CountCodeOccurrences(store, "if (isNewRoute) _db.MonsterPatrolRoutes.Add(routeRow)") == 1,
                    "新建的巡逻线不再按\"是不是新行\"入账：漏 Add 会让行为表指着一条不存在的路线");

        // ④ 真库形状：这八张类都在派生层重新声明了 Id ⇒ 不带 DeclaredOnly 的取值一定抛。
        //    这一格断的是"为什么必须 ③"，而不是只断源码形状——把它读成注释就没人记得 ① 从哪来。
        foreach (var t in new[]
        {
            typeof(Horizon.Model.GameModel.MonsterTemplateEntity),
            typeof(Horizon.Model.GameModel.MonsterPatrolPointEntity),
            typeof(Horizon.Model.GameModel.MonsterSkillMountEntity),
            typeof(Horizon.Model.GameModel.MonsterLootBindingEntity),
            typeof(Horizon.Model.GameModel.MonsterDropEntryEntity),
            typeof(Horizon.Model.GameModel.MonsterPatrolRouteEntity),
        })
        {
            var declared = t.GetProperty("Id", System.Reflection.BindingFlags.Public
                                            | System.Reflection.BindingFlags.Instance
                                            | System.Reflection.BindingFlags.DeclaredOnly);
            Assert.NotNull(declared);
            var threw = false;
            try { t.GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance); }
            catch (System.Reflection.AmbiguousMatchException) { threw = true; }
            Assert.True(threw, $"{t.Name} 的 Id 不再是「隐藏基类同名属性」的形状：①③ 这两条判据的前提变了");
        }

        // ⑤ 效果层（机制层全绿也证明不了真库跑得通）：往返脚本还在、两跳都比、且真的把
        //    "指名路线"与"库→运行时包"这两跳带上了。少一跳就等于把"存进库"当成"游戏里有"。
        var roundtrip = File.ReadAllText(Path.Combine(root, "Tools", "db-roundtrip-check.py"));
        Assert.Contains("\"routeId\": 900901", roundtrip);
        Assert.Contains("DB-CELLS-COMPARED=", roundtrip);
        Assert.Contains("BUNDLE-CELLS-COMPARED=", roundtrip);
        Assert.Contains("export-content", roundtrip);
        // 巡逻线号如今是作者说了算的一格 ⇒ 往返目标不许再把它划成"服务端生成"而跳过。
        Assert.DoesNotContain("\"patrolRoute.routeId\"", roundtrip);
        // 建库脚本本身也要可重跑：019/020 的 SQL 是 UTF-8 无 BOM，sqlcmd 不带 -f 65001 时
        // 会把中文后面的单引号一起吞掉 ⇒ 整批当成一个字面量 ⇒ RC=0、零行输出、零行进库。
        var rehearsal = File.ReadAllText(Path.Combine(root, "Tools", "db-rehearsal-setup.sh"));
        Assert.True(System.Text.RegularExpressions.Regex.Matches(rehearsal, @"-f 65001 -i").Count == 2,
                    "演练库建脚本跑 019/020 少了 -f 65001：种子会静默零行而退出码仍是 0");
        Assert.Contains("__EFMigrationsHistory", rehearsal);
    }

    [Fact]
    public void 等级差索敌半径只有一个主人而两条通路与装配各问一次()
    {
        // 这一格是 AC 的 GetAggroRange（按目标算视野）。它最容易坏在三种形状上：
        // ① 两条通路各写一份式子（切片 21/22/23/25 全是这一族）；
        // ② 主人写了、但宿没把等级递进来 ⇒ 判据恒走"等级未知不调"那一支，等于没接（编译过、测试也可能过）；
        // ③ 换算系数在通路里再抄一遍（0.9144 漂成 1 就是"1 码当 1 米"，读数仍然单调，肉眼看不出来）。
        var root = RepoRoot();
        var gate = File.ReadAllText(Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterAiGate.cs"));
        var v1 = File.ReadAllText(Path.Combine(root, "Horizon.Game.Sim", "MonsterAiCore.cs"));
        var v2 = File.ReadAllText(Path.Combine(root, "Horizon.Game.Sim", "Monsters", "MonsterAiRuntime.cs"));
        var host = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core", "Sim", "Server", "DsWorldSession.cs"));

        Assert.True(CountCodeOccurrences(gate, "public static float EffectiveAggroRadius(") == 1,
                    "等级差索敌的式子不再只有一个主人（或被删了）：两条通路迟早各算一套");
        Assert.True(CountCodeOccurrences(v1, "MonsterAiGate.EffectiveAggroRadius(") == 1
                    && CountCodeOccurrences(v2, "MonsterAiGate.EffectiveAggroRadius(") == 1,
                    "v1 与 v2 必须各问一次主人；少一次就是那一档的怪不看等级差");

        // 反向钉子：通路里不许留旧形状（整圈共用一个半径），也不许留第二份换算。
        Assert.True(CountCodeOccurrences(v1, "config.AggroRadius * config.AggroRadius") == 0,
                    "v1 又回到\"整圈共用一个半径\"：等级差那一格对默认档失效");
        Assert.True(CountCodeOccurrences(v1, "0.9144") == 0 && CountCodeOccurrences(v2, "0.9144") == 0
                    && CountCodeOccurrences(host, "0.9144") == 0,
                    "码→米的换算在主人之外又抄了一份（四处之一）：漂一个数没人看得见");

        // 机制层之外还要问"值到没到"：宿主两处必须真的把等级递进来。
        Assert.True(CountCodeOccurrences(host, "cfg.Level = level;") == 1,
                    "投影没把生效等级带进 AI ⇒ 那条判据永远走\"等级未知不调\"，接了等于没接");
        Assert.True(CountCodeOccurrences(host, "Level = p.Level,") == 1,
                    "候选清单没带玩家等级 ⇒ 只有怪那一侧有等级，等级差恒为 0");
    }

    [Fact]
    public void 外观表会点名盘上查不到的prefab路径而探针本身两半都自证()
    {
        // 这一格防的是美术链上最难归因的那种坏：库里、内容包都逐字正确，
        // 但 prefabPath 写错一个字母 ⇒ 客户端按路径加载失败 ⇒ **静默回退占位人形**。
        // 以前的判据只问"填没填"，问不出"填了个不存在的"。
        var row = new Horizon.MonsterCli.MonsterVisualRow(
            900901, "外观探针兽", "guai", "d6d9a41b-434d-4e54-8bce-ef324708968d",
            "Content/Prefabs/Monsters/guai_monster.prefab", 1.37, 2);

        // ① 正半：探针说在 ⇒ 一条问题都不许多念（否则这条报警常驻，把真问题埋成背景噪音）
        var hit = Horizon.MonsterCli.MonsterVisualsExport.Build(new[] { row }, _ => true);
        Assert.Equal(1, hit.Count);
        Assert.Equal(0, hit.MissingPrefabPaths);
        Assert.DoesNotContain(hit.Problems, p => p.Contains("查不到"));

        // ② 反半：探针说不在 ⇒ 仍然进表（删掉它等于替客户端做决定），但必须点名 + 计数
        var miss = Horizon.MonsterCli.MonsterVisualsExport.Build(new[] { row }, _ => false);
        Assert.Equal(1, miss.Count);
        Assert.Equal(1, miss.MissingPrefabPaths);
        Assert.Contains(miss.Problems, p => p.Contains("查不到") && p.Contains(row.PrefabPath!) && p.Contains("900901"));

        // ③ 没给探针时不许假装查过（静默跳过与"跑过且全对"同形那一族）
        var noProbe = Horizon.MonsterCli.MonsterVisualsExport.Build(new[] { row });
        Assert.Equal(0, noProbe.MissingPrefabPaths);

        // ④ 调用侧：CLI 必须自己定位内容树根，定位不到就大声念"这条检查没跑"
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.MonsterCli", "Program.cs"));
        Assert.True(CountCodeOccurrences(program, "FindContentRoot(") >= 2,
                    "export-visuals 不再定位内容树根：路径存在性这条检查整条没了");
        Assert.Contains("这条检查没跑", program);
        // 拼法只许一种（根 + 去掉 Content/ 前缀）。**计数针必须能区分"这一处存在"与"只有这一处"**：
        // 上一版只数 `File.Exists(Combine(contentRoot, rel))` 出现一次，于是把判据改成
        // `那个 || 全树按文件名再找一次` 之后**针仍然数到 1、闸门仍然绿**（V4 反证跑出来的形状）。
        // 所以宽判据要用它的**签名**去挡（递归搜文件名这件事本身），而不是数窄判据的字数。
        Assert.True(CountCodeOccurrences(program, "System.IO.Path.Combine(contentRoot, rel)") == 1,
                    "prefab 路径的解析拼法不是一处");
        Assert.True(CountCodeOccurrences(program, "SearchOption.AllDirectories") == 0
                    && CountCodeOccurrences(program, "GetFileName(rel)") == 0,
                    "prefab 路径的存在性判据被加宽了（递归按文件名找）：别的目录里的同名资产会被判成命中，" +
                    "这条检查就会在最该报警的时候说\"没问题\"");
    }

    [Fact]
    public void 装载侧与作者侧都把非正数的索敌半径说成非法值()
    {
        // 口径（2026-10-27 拍板）：这一格只表达距离，0 与负数都是**非法值**——
        // "不想让它先手"由 is_aggressive 表达，"没配"由缺省（草稿与快照两份都是 10 米）表达，
        // 两者都不许用 0 糊过去。运行期那条主人（MonsterAiGate.AggroRadiusUsable）从此只兜"漏网之鱼"，
        // 所以这两侧必须各自把话说破，否则非法值会一路走到运行期、变成"这只怪聋了"而没人说。
        var snapshot = new Horizon.Game.Core.Sim.Server.MonsterContentSnapshot { ContentRevision = 1 };
        var zero = new Horizon.Game.Core.Sim.Server.MonsterTemplateSnapshot { MonsterId = 12455, Name = "半径检验兽", Rank = 1, LevelBase = 10 };
        zero.Behavior.AggroRadius = 0f;
        snapshot.Templates.Add(zero);

        var bundle = snapshot.ToBundle(out var problems);
        Assert.NotNull(bundle);
        Assert.Contains(problems, p => p.Contains("索敌半径") && p.Contains("非法值"));

        // 正向对照：填成正数不许念（否则这条提示常驻，把真问题埋成背景噪声）。
        var okSnapshot = new Horizon.Game.Core.Sim.Server.MonsterContentSnapshot { ContentRevision = 1 };
        var okTemplate = new Horizon.Game.Core.Sim.Server.MonsterTemplateSnapshot { MonsterId = 12455, Name = "半径检验兽", Rank = 1, LevelBase = 10 };
        okTemplate.Behavior.AggroRadius = 5f;
        okSnapshot.Templates.Add(okTemplate);
        var okBundle = okSnapshot.ToBundle(out var okProblems);
        Assert.NotNull(okBundle);
        Assert.DoesNotContain(okProblems, p => p.Contains("索敌半径") && p.Contains("非法值"));

        // 作者侧：0 与负数都拒收（Error，不是 Warn——这一格留 0 会真的改变行为）。
        var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100001, Name = "半径" };
        draft.ModelKey = "ban_jing";
        draft.Behavior.AggroRadius = 0;
        var rejected = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.False(rejected.Ok);
        Assert.Contains(rejected.ErrorPaths, p => p == "behavior.aggroRadius");

        // 文案本身也是判据的一部分：旧文案把 0 教成"被动怪才该这样配"，
        // 那正好是这次要消灭的第二种口径，不许它再回到草稿提示里。
        Assert.Contains(rejected.Errors, e => e.Contains("非法值"));
        Assert.DoesNotContain(rejected.Errors, e => e.Contains("只有被动怪才该这样配"));

        draft.Behavior.AggroRadius = -1;
        Assert.Contains(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).ErrorPaths,
                        p => p == "behavior.aggroRadius");

        // 反向对照：被动怪填一个**正**半径必须照常合法——
        // 这条规则说的是"半径不许是 0"，不是"被动怪的半径必须是 0"。
        var passive = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100002, Name = "被动对照" };
        passive.ModelKey = "bei_dong";
        passive.Behavior.IsAggressive = 0;
        passive.Behavior.AggroRadius = 10;
        var passiveResult = Horizon.MonsterCli.MonsterValidator.Validate(passive, null);
        Assert.True(passiveResult.Ok, "被动怪填正半径被拒：" + string.Join(";", passiveResult.Errors));
    }

    [Fact]
    public void 校验器拦住没有消费者的检测位与越界的巡逻引用()
    {
        // 判据形态：列宽允许（int）但内核没有读取方的值，必须按"有消费者的位"拒；
        // 而"引用了路线却没带路线数据"是表现为"画了巡逻线却在原地踱步"的那类静默失效，至少要念出来。
        var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100004, Name = "观星" };
        draft.ModelKey = "guan_xing";
        draft.Behavior.DetectMask = 7;   // bit0 视觉 + bit1 听觉 + bit2 隐蔽探测：全都有人读
        var ok = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.True(ok.Ok, "合法草稿被拒：" + string.Join(";", ok.Errors));

        draft.Behavior.DetectMask = 8;   // bit3：内核没人读，存得进去也读得回来，但什么都不做
        var undefinedBit = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.False(undefinedBit.Ok);
        Assert.Contains(undefinedBit.ErrorPaths, p => p == "behavior.detectMask");

        draft.Behavior.DetectMask = 1;
        draft.Behavior.WanderRadius = -1;
        Assert.Contains(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).ErrorPaths,
                        p => p == "behavior.wanderRadius");
        draft.Behavior.WanderRadius = 6;
        draft.Behavior.IdleToPatrolSeconds = -1;
        Assert.Contains(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).ErrorPaths,
                        p => p == "behavior.idleToPatrolSeconds");

        // 物种：写库时是 clamp(0,7)，越界会被**静默改写**（配 9 落成 7＝昆虫），所以必须当场拒。
        draft.Species = 9;
        Assert.Contains(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).ErrorPaths, p => p == "species");
        draft.Species = 3;   // 龙类：合法枚举内

        // 引用一条本草稿没带的路线：只 Warn（路线允许跨怪共用），但必须出现。
        draft.Behavior.IdleToPatrolSeconds = 3;
        draft.Behavior.PatrolRouteId = 4242;
        var dangling = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.True(dangling.Ok, "引用外部路线不该被拒：" + string.Join(";", dangling.Errors));
        Assert.Contains(dangling.Warnings, w => w.Contains("patrolRouteId"));

        // 正向对照：草稿自己带上这条路线后，警告必须消失。
        draft.PatrolRoute = new Horizon.MonsterCli.MonsterRouteDraft { RouteId = 4242, Mode = 1 };
        draft.PatrolRoute.Points.Add(new Horizon.MonsterCli.MonsterPointDraft { Seq = 1, X = 0, Y = 0, Z = 0 });
        draft.PatrolRoute.Points.Add(new Horizon.MonsterCli.MonsterPointDraft { Seq = 2, X = 5, Y = 0, Z = 0 });
        var withRoute = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.DoesNotContain(withRoute.Warnings, w => w.Contains("patrolRouteId"));
    }

    [Fact]
    public void 校验器指出刷怪点覆盖的路线不是本草稿画的那条()
    {
        // route_override_id 现在真的会被运行时采用（DsMonster.PatrolRouteId：覆盖 → 行为表），
        // 于是多出一条只有覆盖才会有的错配：策划画的路线与点位覆盖的不是同一条，
        // 表现是"我画的这条根本没走"。引用外部路线本身合法（可跨怪共用），所以只 Warn。
        var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100006, Name = "巡星" };
        draft.ModelKey = "xun_xing";
        draft.PatrolRoute = new Horizon.MonsterCli.MonsterRouteDraft { RouteId = 77, Mode = 1 };
        draft.PatrolRoute.Points.Add(new Horizon.MonsterCli.MonsterPointDraft { Seq = 1, X = 0, Y = 0, Z = 0 });
        draft.PatrolRoute.Points.Add(new Horizon.MonsterCli.MonsterPointDraft { Seq = 2, X = 5, Y = 0, Z = 0 });
        var sp = new Horizon.MonsterCli.MonsterSpawnDraft { SpawnPointId = 1, RouteOverrideId = 88 };
        draft.SpawnPoints.Add(sp);

        var clash = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.True(clash.Ok, "覆盖指向别条路线不该被拒：" + string.Join(";", clash.Errors));
        Assert.Contains(clash.Warnings, w => w.Contains("routeOverrideId") && w.Contains("不会生效"));

        // 正向对照一：覆盖正好指本草稿画的那条 → 这句必须消失
        sp.RouteOverrideId = 77;
        var aligned = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.DoesNotContain(aligned.Warnings, w => w.Contains("不会生效"));

        // 正向对照二：0 = 不覆盖（库里那一列的语义），同样不该念
        sp.RouteOverrideId = 0;
        var noOverride = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.DoesNotContain(noOverride.Warnings, w => w.Contains("不会生效"));
    }

    [Fact]
    public void 校验器指出世界BOSS的脱战半径是死配置()
    {
        // 运行时里世界 BOSS 根本不做"距出生点超 leash 就脱战"这条检查
        // （逆向依据：AzerothCore `Creature::CanCreatureAttack` 把整段写在 `!isWorldBoss()` 里）。
        // 于是这一格对它是**死配置**：改了游戏里什么都不发生，必须在录入当场念出来。
        var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100008, Name = "落霜" };
        draft.ModelKey = "luo_shuang";
        draft.Rank = 4;                       // 世界BOSS
        draft.Behavior.AggroRadius = 10;
        draft.Behavior.LeashRadius = 60;

        var worldBoss = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.True(worldBoss.Ok, "合法草稿被拒：" + string.Join(";", worldBoss.Errors));
        Assert.Contains(worldBoss.Warnings, w => w.Contains("leashRadius"));

        // 正向对照一：普通怪这条半径真的被读 ⇒ 这句必须消失
        draft.Rank = 0;
        Assert.DoesNotContain(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).Warnings,
                              w => w.Contains("leashRadius"));

        // 正向对照二：世界BOSS 但半径 0（本来就没配）也不该念
        draft.Rank = 4;
        draft.Behavior.LeashRadius = 0;
        Assert.DoesNotContain(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).Warnings,
                              w => w.Contains("leashRadius"));
    }

    [Fact]
    public void 校验器把求援的三种配了等于没配逐条念出来()
    {
        // 内核的进入条件是**合取**：`flags.UseThreatTable` 里面再要 `UseAssist && AssistRadius>0 && Species!=0`。
        // 少任何一项，求援都安静地什么都不做（内核没有日志，策划只能进游戏才发现），
        // 所以三条分支各配一条警告 + 一条"补上之后必须消失"的正向对照——只测一条时另外两条会永远全绿。
        // 夹具用单项覆盖把求援这一位掰开，而不是挑一个高档位：档位会顺带开出别的位，
        // 让不相干的警告一起冒出来，判据就分不清归因给谁。
        var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100007, Name = "呼朋" };
        draft.ModelKey = "hu_peng";
        draft.Behavior.AiProfile = 1;        // Combatant：威胁表开、其余关
        draft.Behavior.FlagUseAssist = 1;    // 只开求援位
        draft.Species = 3;                   // 龙类
        // 先自证夹具：按**校验器同一个调用**展开能力位。这一位没打开时，
        // 三条判据全会因为"根本没进求援分支"而空转（只有 Contains 那三条会红，但红得没有归因）。
        var opened = Horizon.Game.Sim.Monsters.MonsterAiFlags.Resolve(
            (Horizon.Game.Sim.Monsters.MonsterAiProfile)draft.Behavior.AiProfile,
            draft.Behavior.FlagUseThreat, draft.Behavior.FlagUseSkills,
            draft.Behavior.FlagUseNavMesh, draft.Behavior.FlagUseAssist, draft.Behavior.FlagUseTactics);
        Assert.True(opened.UseAssist && opened.UseThreatTable,
            "夹具没把求援位与威胁表打开：下面三条判据全会空转");
        var healthy = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.True(healthy.Ok, "合法草稿被拒：" + string.Join(";", healthy.Errors));
        Assert.DoesNotContain(healthy.Warnings, w => w.Contains("求援"));

        // ① 半径 0（与"没填"同义）
        draft.Behavior.AssistRadius = 0;
        Assert.Contains(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).Warnings,
                        w => w.Contains("求援半径是 0"));
        draft.Behavior.AssistRadius = 10;
        Assert.DoesNotContain(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).Warnings,
                              w => w.Contains("求援半径是 0"));

        // ② 物种 0 = 没配（内核绝不允许 0==0 当同族，否则全图没配物种的怪互相拉成片）
        draft.Species = 0;
        Assert.Contains(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).Warnings,
                        w => w.Contains("物种是 0"));
        draft.Species = 3;
        Assert.DoesNotContain(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).Warnings,
                              w => w.Contains("物种是 0"));

        // ③ 档位把威胁表关着：求援整段写在威胁表分支里
        draft.Behavior.AiProfile = 0;        // Dumb：五位全关
        Assert.Contains(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).Warnings,
                        w => w.Contains("威胁表关着"));
        draft.Behavior.AiProfile = 1;
        Assert.DoesNotContain(Horizon.MonsterCli.MonsterValidator.Validate(draft, null).Warnings,
                              w => w.Contains("威胁表关着"));
    }

    [Fact]
    public void 校验器把只填Guid没填路径的配法单独报出来()
    {
        // 这是"编辑器里配了外观、进游戏还是占位人形"的唯一前置信号，必须成判据而不是注释。
        var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100002, Name = "蜃景" };
        draft.ModelKey = "chi_lin_shen";
        draft.PrefabGuid = "some-guid";
        draft.Behavior.LeashRadius = 30;
        draft.Behavior.AggroRadius = 10;
        var result = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.True(result.Ok, "合法草稿被拒：" + string.Join(";", result.Errors));
        Assert.Contains(result.Warnings, w => w.Contains("prefabPath"));

        // 正向对照：补上路径后这条警告必须消失（否则它成了一条谁都读不懂的常驻噪声）。
        draft.PrefabPath = "Content/Game/Monsters/Shen.prefab";
        var fixedResult = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.DoesNotContain(fixedResult.Warnings, w => w.Contains("prefabPath"));

        // 超长路径要拒（列宽 256），不然写库时才炸，而炸点在离配置很远的地方。
        draft.PrefabPath = new string('x', 300);
        var tooLong = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.False(tooLong.Ok);
        Assert.Contains(tooLong.ErrorPaths, p => p == "prefabPath");
    }

    [Fact]
    public void 会碰怪物内容静态注册表的测试类都必须挂在串行集合上()
    {
        // 症状：装配一份只含几个模板的内容包会换掉**进程级**的 MonsterContent，
        // 同一时刻另一个测试类对 MonsterTemplateCatalog 的兜底查询就落到另一份内容上——
        // BOSS 击杀掷出空掉落，单跑永远绿、全量并行跑偶发红（实测过一次）。
        // 光"这一次改对了"不够：下一个新增的怪物测试类一样会踩。所以把规矩变成判据。
        // 这是一道**扫描型**闸门，因此必须自证扫到了东西：命中数与命中清单都要断言，
        // 否则目录解析失败时会以"0 条违规"的名义永远全绿。
        var dir = LocateGatewayTestSourceDir();
        var scanned = new System.Collections.Generic.List<string>();
        var offenders = new System.Collections.Generic.List<string>();

        foreach (var file in System.IO.Directory.GetFiles(dir, "*.cs"))
        {
            var text = System.IO.File.ReadAllText(file);
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"\bMonsterContent\.|\bMonsterTemplateCatalog\b")) continue;
            var cls = System.Text.RegularExpressions.Regex.Match(text, @"public (?:sealed )?class (\w+Tests)\b");
            if (!cls.Success) continue;   // 被引用的辅助/集合定义文件，不是测试类

            scanned.Add(cls.Groups[1].Value);
            var collections = System.Text.RegularExpressions.Regex.Matches(text, @"\[Collection\(([^)\r\n]*)\)\]");
            var serial = false;
            foreach (System.Text.RegularExpressions.Match c in collections)
            {
                var arg = c.Groups[1].Value;
                // 这里比的是**源码里的字面文本**，不是常量求值后的值：
                // `[Collection(MonsterContentStaticCollection.Name)]` 里的 arg 是那个符号串，
                // 拿 Name（"monster-content"）去 Contains 永远不命中——第一版就是这么把自己的
                // 6 个类全判成违规的（闸门红得对，因为它确实抓到了一个错，只不过错在闸门自己身上）。
                if (arg.Contains("monster-content") ||
                    arg.Contains(nameof(MonsterContentStaticCollection)) ||
                    arg.Contains("Wuxing live cluster"))   // 这两个集合的定义都是 DisableParallelization = true
                {
                    serial = true;
                    break;
                }
            }
            if (!serial) offenders.Add(cls.Groups[1].Value + " ← " + System.IO.Path.GetFileName(file));
        }

        Assert.True(scanned.Count >= 10,
            $"只扫到 {scanned.Count} 个引用怪物内容/代码目录的测试类（预期至少 10）——目录解析或判据本身坏了：{dir}");
        Assert.Empty(offenders);
    }

    [Fact]
    public void 会写脚本生成进程环境变量的测试类都必须挂在串行集合上()
    {
        // 症状（2026-10-26 全量并行跑实测）：`ScriptedSpawnConfigurationBindingTests.记录裸绑定器遇到1会抛异常这一事实`
        // 在全量跑里红"没有抛异常"，单跑永远绿。诊断结果不是绑定器变了，而是**三个测试类同时改同一批进程环境变量**
        // （一个把 ScriptedSpawn__Enabled 写成 "1"、一个写成 "true"、一个显式清空）——
        // 环境变量是进程级的，交错执行时"我清完还没等我写，对面已经写了它的值"，
        // 而每个类的 finally 又会把别人刚设的值清掉。
        // 与 monster-content 那道同一族：这一次修好不够，规矩得能被机器复查。
        var dir = LocateGatewayTestSourceDir();
        var scanned = new System.Collections.Generic.List<string>();
        var offenders = new System.Collections.Generic.List<string>();

        foreach (var file in System.IO.Directory.GetFiles(dir, "*.cs"))
        {
            var text = System.IO.File.ReadAllText(file);
            // 写进程环境变量 + 动的是脚本生成那批名字（权威名或旧回退名，字面量或 `ScriptedSpawnDiagnostics.Env*` 符号）
            // 判"有没有真的调用"要先把**字符串字面量**抹掉再找：本闸门的源码里就写着
            // "Environment.SetEnvironmentVariable(" 这个字面量（作为 Contains 的参数），
            // 第一版因此把本类判成违规——闸门红得对，只是错在闸门自己身上。
            // 名字那一侧反过来要用**原文**：真写环境变量的类多半是把 "ScriptedSpawn__Enabled" 当字面量传的。
            var stripped = System.Text.RegularExpressions.Regex.Replace(
                text, @"""(?:\\.|[^""\\\r\n])*""", "\"\"");
            if (!stripped.Contains("Environment.SetEnvironmentVariable(")) continue;
            if (!text.Contains("ScriptedSpawn__") && !text.Contains("HZN_SCRIPTED_SPAWN")
                && !text.Contains("ScriptedSpawnDiagnostics.Env")
                && !text.Contains("ScriptedSpawnDiagnostics.LegacyEnv")) continue;

            var cls = System.Text.RegularExpressions.Regex.Match(text, @"public (?:sealed )?class (\w+Tests)\b");
            if (!cls.Success) continue;   // 集合定义/辅助文件不算
            scanned.Add(cls.Groups[1].Value);

            var collections = System.Text.RegularExpressions.Regex.Matches(text, @"\[Collection\(([^)\r\n]*)\)\]");
            var serial = false;
            foreach (System.Text.RegularExpressions.Match c in collections)
            {
                var arg = c.Groups[1].Value;
                // 与另一道闸门同样的教训：比的是**源码里的字面文本**，不是常量求值后的值。
                if (arg.Contains("scripted-spawn-env") || arg.Contains(nameof(ScriptedSpawnEnvironmentCollection)))
                {
                    serial = true;
                    break;
                }
            }
            if (!serial) offenders.Add(cls.Groups[1].Value + " ← " + System.IO.Path.GetFileName(file));
        }

        // 下限"至少 2"：目前真写**进程**环境变量的两个类（配置绑定 + 诊断）。
        // 这条下限存在的理由与另一道闸门一样——扫描型闸门最常见的失败方式是"扫到 0 个也算通过"。
        Assert.True(scanned.Count >= 2,
            $"只扫到 {scanned.Count} 个写脚本生成环境变量的测试类（预期至少 2）——判据或目录解析坏了：{dir}；" +
            "扫到的：" + string.Join(",", scanned));
        Assert.Empty(offenders);
    }

    [Fact]
    public void 掉落组号在每一层的名字都是同一个东西()
    {
        // groupId / group_id 要穿过的层：编辑器模型 → 草稿 JSON → 库列 → 迁移/快照/SQL → 导出 DTO → 运行期快照。
        // 少一层的表现都不是报错：JSON 侧漏键会被 STJ 静默丢成 0（组全散），
        // 库列漏了就变成"编辑器配了组，进游戏 Invalid column name"，
        // DTO 漏了则库里存着组而运行时人人都独立掷——三处数字全都对得上。
        var root = RepoRoot();

        var plugin = ParseKeySets(PluginModelPath);
        var cli = ParseKeySets(CliModelPath);
        Assert.True(plugin.ContainsKey("Drop") && plugin["Drop"].Contains("groupId"),
            "插件侧 KnownDropKeys 没有 groupId：编辑器写下的组号会被当未知键丢弃");
        Assert.True(cli.ContainsKey("Drop") && cli["Drop"].Contains("groupId"),
            "CLI 侧 KnownDropKeys 没有 groupId：草稿里的组号会被 STJ 静默丢成 0");

        var entityText = File.ReadAllText(Path.Combine(root, "Horizon.Model", "GameModel", "MonsterWorldEntity.cs"));
        Assert.True(entityText.Contains("[Column(\"group_id\""), "实体没声明 group_id 列");

        // 四份**尚未执行**的建表产物：表还没建，所以列必须直接进 CREATE TABLE 而不是另起 ALTER。
        // 这四份是同一份模型状态的四张脸，漏一张就是"用另一张脸建表"。
        // 判据按各产物**声明一列的写法**分别匹配——只数"group_id 这个子串在不在"的话，
        // 删掉列声明而留下注释/属性行，闸门照样绿。
        var artifacts = new List<(string Label, string Path, string Needle, string? FragmentStart)>
        {
            ("迁移CreateTable", Path.Combine("Horizon.Entities", "Migrations", "Games", "20261025123917_AddMonsterEditorTables.cs"),
             "group_id = table.Column<int>", null),
            ("迁移Designer", Path.Combine("Horizon.Entities", "Migrations", "Games", "20261025123917_AddMonsterEditorTables.Designer.cs"),
             ".HasColumnName(\"group_id\")", null),
            // ModelSnapshot 是**整库**的模型快照，不是这次迁移的片段：别的会话给 NPC 那张表也加了
            // 同名的 group_id 之后，"整份文件数子串"这把尺就把两件不同的东西并成一件而报 2 次。
            // 所以这里切到逐件掉落那一个实体块里数（同名块边界就是下一个 modelBuilder.Entity）。
            ("ModelSnapshot", Path.Combine("Horizon.Entities", "Migrations", "Games", "GameEntityContextModelSnapshot.cs"),
             ".HasColumnName(\"group_id\")",
             "modelBuilder.Entity(\"Horizon.Model.GameModel.MonsterDropEntryEntity\""),
            ("SQL019建表", Path.Combine("scripts", "sql", "019_monster_editor_tables.sql"),
             "[group_id] int NOT NULL", null),
        };
        var counts = new List<string>();
        foreach (var art in artifacts)
        {
            var path = Path.Combine(root, art.Path);
            Assert.True(File.Exists(path), $"{art.Label} 的文件不在盘上：{art.Path}");
            var text = File.ReadAllText(path);
            if (art.FragmentStart != null)
            {
                var from = text.IndexOf(art.FragmentStart, StringComparison.Ordinal);
                Assert.True(from >= 0, $"{art.Label} 里找不到实体块起点 {art.FragmentStart}：这张表被改名或删掉了");
                var next = text.IndexOf("modelBuilder.Entity(\"", from + art.FragmentStart.Length, StringComparison.Ordinal);
                text = next < 0 ? text[from..] : text[from..next];
            }
            var hit = Regex.Matches(text, Regex.Escape(art.Needle)).Count;
            Assert.True(hit == 1, $"{art.Label} 里“{art.Needle}”出现 {hit} 次（应为 1）——这张表建出来会缺列或重复");
            // 同一张表里 seq 也在：证明这段扫描读到的是逐件掉落那一处，不是别处的同名串。
            Assert.True(text.Contains("seq"), $"{art.Label} 连 seq 都扫不到，读到的不是这张表");
            counts.Add($"{art.Label}={hit}");
        }
        _log.WriteLine("group_id 各产物命中：" + string.Join(" ", counts));

        // CLR 面：三处成员名必须与 JSON 键 / 库列对得上。
        Assert.True(typeof(Horizon.Game.Core.Sim.Server.MonsterDropSnapshotDto).GetProperty("GroupId") != null,
            "MonsterDropSnapshotDto 没有 GroupId 属性：导出/读回的组号会被丢成 0");
        Assert.True(typeof(Horizon.Game.Sim.Monsters.MonsterDropSnapshot).GetField("GroupId") != null,
            "MonsterDropSnapshot 没有 GroupId 字段：装配层无处可传");
        Assert.True(typeof(Horizon.Model.GameModel.MonsterDropEntryEntity).GetProperty("GroupId") != null,
            "MonsterDropEntryEntity 没有 GroupId 属性：EF 读写不到这一列");

        // 库的读写导出三处，每一处都得真的带上组号——漏一处的表现各不相同：
        //   FindDraft 漏 → 编辑器/CLI 读回草稿时组号显示为 0，一保存就把配好的组冲掉；
        //   Upsert 漏 → 库里永远是 0，草稿里配的组静默消失；
        //   BuildContentSnapshot 漏 → 库里存着组而运行时人人都独立掷，三处概率数字全都对得上。
        var storeText = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "MonsterStore.cs"));
        var storeSites = new[]
        {
            "public MonsterDraft? FindDraft",
            "public MonsterStoreResult Upsert",
            "public Horizon.Game.Core.Sim.Server.MonsterContentSnapshot BuildContentSnapshot",
        };
        var seenSites = new List<string>();
        foreach (var signature in storeSites)
        {
            var body = TakeMethodBody(storeText, signature);
            // 同一段里必须还能看到逐件掉落那层的名字，否则切到的是别的重载、这条断言就在空转。
            Assert.True(body.Contains("MonsterDrop") || body.Contains("DropEntry") || body.Contains("DropSnapshot"),
                $"方法 {signature} 的段落里看不到逐件掉落的类型，切段切错了");
            Assert.True(body.Contains("GroupId"), $"{signature} 没有搬运 GroupId");
            seenSites.Add(signature);
        }
        _log.WriteLine("搬运组号的三处读写口：" + string.Join(" / ", seenSites));
    }

    /// <summary>
    /// 取从 startMark 到它之后第一个 endMark 之间的那一段（用于把判据限定在**某一张表**的声明里）。
    /// <para>两个标记找不到都直接判失败——切段失败而继续往下数子串，会把"缺列"判成"有列"。</para>
    /// </summary>
    private static string TakeSegment(string text, string startMark, string endMark, string label)
    {
        var start = text.IndexOf(startMark, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{label} 里找不到片段起点：{startMark}");
        var end = text.IndexOf(endMark, start + startMark.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"{label} 里找不到片段终点标记（{endMark}）——不能拿整份文件当这一张表");
        return text.Substring(start, end - start);
    }

    /// <summary>取某个方法签名之后、到下一个同级成员声明之前的那一段源码。</summary>
    private static string TakeMethodBody(string text, string signature)
    {
        var start = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"源码里找不到方法签名：{signature}");
        var next = text.IndexOf("\n    public ", start + signature.Length, StringComparison.Ordinal);
        var nextPrivate = text.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        if (nextPrivate >= 0 && (next < 0 || nextPrivate < next)) next = nextPrivate;
        var end = next < 0 ? text.Length : next;
        return text.Substring(start, end - start);
    }

    [Fact]
    public void 校验器把掉落组的六种配法逐条念出来()
    {
        // 组语义是最容易"数字全对、行为完全不同"的一层：同一个 chanceBps=0，
        // 在组外是"这件永不掉"，在组内是"平分显式档剩下的余额"；组内合计超过 100% 时
        // 后排成员只拿余额而不是自己填的数。这些都不会让任何一层报错，只会让掉率静默变形。
        Horizon.MonsterCli.MonsterDraft DraftWith(params (int ItemId, int Chance, int Group)[] rows)
        {
            var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100007, Name = "校验夹具" };
            draft.ModelKey = "xian_zhu";
            var binding = new Horizon.MonsterCli.MonsterLootBindingDraft { LootTableId = 1, RollChanceBps = 10000 };
            for (var i = 0; i < rows.Length; i++)
            {
                binding.Entries.Add(new Horizon.MonsterCli.MonsterDropDraft
                {
                    ItemId = rows[i].ItemId, ChanceBps = rows[i].Chance,
                    GroupId = rows[i].Group, Seq = i + 1,
                });
            }
            draft.LootBindings.Add(binding);
            return draft;
        }

        // A 组号越界：负数会被内核当"独立掷"、>127 超出魔兽的 7 位口径——两种都必须拒。
        var negative = Horizon.MonsterCli.MonsterValidator.Validate(DraftWith((80001, 5000, -1)), null);
        Assert.False(negative.Ok);
        Assert.Contains(negative.ErrorPaths, p => p.EndsWith(".groupId"));
        var overflow = Horizon.MonsterCli.MonsterValidator.Validate(DraftWith((80001, 5000, 128)), null);
        Assert.False(overflow.Ok);
        Assert.Contains(overflow.ErrorPaths, p => p.EndsWith(".groupId"));

        var overHundred = Horizon.MonsterCli.MonsterValidator.Validate(
            DraftWith((80002, 6000, 1), (80003, 6000, 1)), null);
        Assert.True(overHundred.Ok, "该过的没过去：" + string.Join(";", overHundred.Errors));
        Assert.Contains(overHundred.Warnings, w => w.Contains("已超过 10000"));

        var shadowed = Horizon.MonsterCli.MonsterValidator.Validate(
            DraftWith((80004, 10000, 1), (80005, 5000, 1)), null);
        Assert.Contains(shadowed.Warnings, w => w.Contains("连减法都参与不了"));

        var deadEqual = Horizon.MonsterCli.MonsterValidator.Validate(
            DraftWith((80006, 10000, 1), (80007, 0, 1)), null);
        Assert.Contains(deadEqual.Warnings, w => w.Contains("一次也轮不到"));

        var allEqual = Horizon.MonsterCli.MonsterValidator.Validate(
            DraftWith((80008, 0, 2), (80009, 0, 2), (80010, 0, 2)), null);
        Assert.Contains(allEqual.Warnings, w => w.Contains("保底抽一件"));

        var zeroOutside = Horizon.MonsterCli.MonsterValidator.Validate(
            DraftWith((80011, 0, 0)), null);
        Assert.Contains(zeroOutside.Warnings, w => w.Contains("组外（groupId=0）填 0"));

        // 每条都得有"该不该念"的反面对照，否则六条警告会变成常驻背景，谁都不读。
        // 健康的"25% + 两件平分余额"一张表：六条一个都不该出现。
        var healthy = Horizon.MonsterCli.MonsterValidator.Validate(
            DraftWith((80012, 2500, 1), (80013, 0, 1), (80014, 0, 1)), null);
        Assert.True(healthy.Ok, "合法组表被拒：" + string.Join(";", healthy.Errors));
        foreach (var needle in new[] { "已超过 10000", "连减法都参与不了", "一次也轮不到", "保底抽一件", "组外（groupId=0）填 0" })
        {
            Assert.DoesNotContain(healthy.Warnings, w => w.Contains(needle));
        }

        // 六条判据不能只证"存在"，还要证它们数的是**这张表**：
        // 组级提示里必须带上组号与件数，否则一怪多组时"哪组配错了"要人自己去数行。
        Assert.Contains(deadEqual.Warnings, w => w.Contains("groupId=1") && w.Contains("2 件互斥"));
    }

    [Fact]
    public void 校验器把逐件条件的三种错配逐条念出来()
    {
        // 逐件条件这一格最容易"配了等于没配"：条件说"需持有任务"却没有任务 id 时，
        // 内核走的是「判不了→不给」那一档——表现是这件配了 100% 却一次都没发出去，
        // 而编辑器、库、快照三处的数字全都对得上。
        Horizon.MonsterCli.MonsterDraft DraftWith(params (int ItemId, int Condition, int Quest)[] rows)
        {
            var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100011, Name = "条件校验兽" };
            draft.ModelKey = "xian_zhu";
            var binding = new Horizon.MonsterCli.MonsterLootBindingDraft { LootTableId = 1, RollChanceBps = 10000 };
            for (var i = 0; i < rows.Length; i++)
            {
                binding.Entries.Add(new Horizon.MonsterCli.MonsterDropDraft
                {
                    ItemId = rows[i].ItemId, ChanceBps = 10000, Seq = i + 1,
                    ConditionKind = rows[i].Condition, QuestId = rows[i].Quest,
                });
            }
            draft.LootBindings.Add(binding);
            return draft;
        }

        var noQuestId = Horizon.MonsterCli.MonsterValidator.Validate(DraftWith((86001, 1, 0)), null);
        Assert.False(noQuestId.Ok);
        Assert.Contains(noQuestId.ErrorPaths, p => p.EndsWith(".questId"));

        var deadQuest = Horizon.MonsterCli.MonsterValidator.Validate(DraftWith((86002, 0, 90001)), null);
        Assert.True(deadQuest.Ok, "合法草稿被拒：" + string.Join(";", deadQuest.Errors));
        Assert.Contains(deadQuest.Warnings, w => w.Contains("死配置"));

        var unsupported = Horizon.MonsterCli.MonsterValidator.Validate(DraftWith((86003, 2, 0)), null);
        Assert.Contains(unsupported.Warnings, w => w.Contains("组队/稀有度"));

        var illegal = Horizon.MonsterCli.MonsterValidator.Validate(DraftWith((86004, -2, 0), (86005, 4, 0)), null);
        Assert.False(illegal.Ok);
        Assert.Equal(2, illegal.ErrorPaths.Count(p => p.EndsWith(".conditionKind")));

        // 正反对照：一件"需持有任务且带了任务 id"的条目不该被念任何一条；
        // 而"条目 -1 继承绑定"是常态配法，也不该有噪声（否则警告会变成常驻背景）。
        var healthy = Horizon.MonsterCli.MonsterValidator.Validate(DraftWith((86006, 1, 90002)), null);
        Assert.True(healthy.Ok, "合法条目被拒：" + string.Join(";", healthy.Errors));
        Assert.DoesNotContain(healthy.Warnings, w => w.Contains("死配置"));
        Assert.DoesNotContain(healthy.Warnings, w => w.Contains("组队/稀有度"));
        var inherit = Horizon.MonsterCli.MonsterValidator.Validate(DraftWith((86007, -1, 0)), null);
        Assert.True(inherit.Ok, "继承形态被拒：" + string.Join(";", inherit.Errors));
        Assert.DoesNotContain(inherit.Warnings, w => w.Contains("死配置") || w.Contains("永远不发放"));

        // 绑定级条件同样要能兜住条目：绑定=任务而条目=继承时，缺任务 id 也该当场报错。
        var bindingQuest = DraftWith((86008, -1, 0));
        bindingQuest.LootBindings[0].ConditionKind = 1;
        bindingQuest.LootBindings[0].QuestId = 0;
        var loose = Horizon.MonsterCli.MonsterValidator.Validate(bindingQuest, null);
        Assert.False(loose.Ok);
        Assert.Contains(loose.ErrorPaths, p => p.EndsWith(".questId"));
    }

    /// <summary>校验器用的最小引用索引替身：只需要任务 id 与它们的发布状态。</summary>
    private sealed class FakeQuestRefs : Horizon.MonsterCli.IMonsterReferenceIndex
    {
        private readonly Dictionary<int, int> _quests;
        public FakeQuestRefs(Dictionary<int, int> quests) => _quests = quests;
        public HashSet<int> SkillIds() => new();
        // 只登记这条用例用到的那件物品：物品存在性另有判据，不该在这里陪跑出红。
        public HashSet<int> ItemIds() => new() { 700020 };
        public HashSet<int> QuestIds() => new(_quests.Keys);
        public HashSet<int> MonsterIds() => new();
        public IReadOnlyDictionary<int, int> QuestPublishStates() => _quests;
    }

    [Fact]
    public void 待机姿态填了但客户端没有渲染端要当场念出来()
    {
        // standState 会一路下发到客户端标记（identity.StandState → SelectableEntityTag.StandState），
        // 但客户端**没有任何渲染端**读它——占位 prefab 没有坐/卧动作。不念这一句，
        // 策划填了 1/2/3 会以为"玩家能看到它坐下"，实际画面一点不变。
        var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100013, Name = "姿态兽" };
        draft.ModelKey = "xian_zhu";

        var stand = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.True(stand.Ok);
        Assert.DoesNotContain(stand.Warnings, w => w.Contains("没有渲染端"));

        draft.StandState = 1;
        var sitting = Horizon.MonsterCli.MonsterValidator.Validate(draft, null);
        Assert.True(sitting.Ok, "只是警告，不该拒：" + string.Join(";", sitting.Errors));
        Assert.Contains(sitting.Warnings, w => w.Contains("没有渲染端") && w.Contains("standState=1"));
    }

    [Fact]
    public void 条件件引用了没上线的任务要当场念出来()
    {
        // 跨层镜像常量：校验器不引用任务侧程序集，`QuestOnlineState` 是手抄的字面量。
        // 注释写"与 QuestGrain 同值"挡不住哪天任务侧改了枚举——两边类型各取一次真值直接比。
        Assert.Equal((int)Horizon.Game.Sim.Quests.QuestPublishState.Online,
                     Horizon.MonsterCli.MonsterValidator.QuestOnlineState);

        // 真实踩到的坑（2026-10-30）：墨麒麟那件挂了 publish_state=0 的任务 20001。
        // "任务存在"与"任务接得到"是两件事——QuestGrain 只认 Online 模板、目录也只下发 Online，
        // 所以那件东西谁都拿不到；而不念这一句时，库/编辑器/快照三处数字全对，表现为
        // "配了 50% 却一次都没掉过"，只能靠人想起来去查任务表。
        Horizon.MonsterCli.MonsterDraft Draft(int questId)
        {
            var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100012, Name = "上线校验兽" };
            draft.ModelKey = "xian_zhu";
            var binding = new Horizon.MonsterCli.MonsterLootBindingDraft { LootTableId = 1, RollChanceBps = 10000 };
            binding.Entries.Add(new Horizon.MonsterCli.MonsterDropDraft
            {
                ItemId = 700020, ChanceBps = 5000, Seq = 1, ConditionKind = 1, QuestId = questId,
            });
            draft.LootBindings.Add(binding);
            return draft;
        }

        var offline = Horizon.MonsterCli.MonsterValidator.Validate(
            Draft(20001), new FakeQuestRefs(new Dictionary<int, int> { { 20001, 0 } }));
        Assert.True(offline.Ok, "离线任务只是警告，不该把合法草稿拒掉：" + string.Join(";", offline.Errors));
        Assert.Contains(offline.Warnings, w => w.Contains("没有上线") && w.Contains("20001"));

        // 反面对照：上线中的任务一个"没有上线"都不该念（否则这句会变成常驻背景）。
        var online = Horizon.MonsterCli.MonsterValidator.Validate(
            Draft(20001), new FakeQuestRefs(new Dictionary<int, int> { { 20001, 1 } }));
        Assert.True(online.Ok);
        Assert.DoesNotContain(online.Warnings, w => w.Contains("没有上线"));

        // 不存在的任务仍是硬错：不能被"没上线"这条兜成警告。
        var missing = Horizon.MonsterCli.MonsterValidator.Validate(
            Draft(20001), new FakeQuestRefs(new Dictionary<int, int>()));
        Assert.False(missing.Ok);
        Assert.Contains(missing.ErrorPaths, p => p.EndsWith(".questId"));
    }

    [Fact]
    public void 逐件条件两列在每一层的名字都是同一个东西()
    {
        // condition_kind / quest_id 要穿过的层与 group_id 一样多，漏一处的表现也一样安静：
        // JSON 侧漏键 → STJ 丢成初值（若初值不是"继承"就整表变无条件）；库列漏了 → 建表缺列；
        // 导出漏了 → 库里存着任务条件而运行时按绑定出账。
        var root = RepoRoot();
        var plugin = ParseKeySets(PluginModelPath);
        var cli = ParseKeySets(CliModelPath);
        foreach (var side in new[] { plugin, cli })
        {
            Assert.True(side["Drop"].Contains("conditionKind"), "有一侧的 KnownDropKeys 没有 conditionKind");
            Assert.True(side["Drop"].Contains("questId"), "有一侧的 KnownDropKeys 没有 questId");
            Assert.True(side["Drop"].Contains("groupId"), "有一侧的 KnownDropKeys 没有 groupId");
        }

        // 实体侧的列名**不能按整份文件的子串判**：绑定表早就有同名的 condition_kind/quest_id 两列，
        // 把逐件表那一列改名成 condition_idx，整份文件里那两个子串照样还在（另一张表那份），闸门全绿。
        // 所以读**这个类型上的属性**映射到的列名：漂移一个字符就当场红。
        var columnMap = new (string Property, string Column, int Order)[]
        {
            ("ItemId", "item_id", 3),
            ("ChanceBps", "chance_bps", 4),
            ("CountMin", "count_min", 5),
            ("CountMax", "count_max", 6),
            ("Seq", "seq", 7),
            ("GroupId", "group_id", 8),
            ("ConditionKind", "condition_kind", 9),
            ("QuestId", "quest_id", 10),
        };
        var entityText = File.ReadAllText(Path.Combine(root, "Horizon.Model", "GameModel", "MonsterWorldEntity.cs"));
        foreach (var row in columnMap)
        {
            var prop = typeof(Horizon.Model.GameModel.MonsterDropEntryEntity).GetProperty(row.Property);
            Assert.True(prop != null, $"MonsterDropEntryEntity 没有属性 {row.Property}");
            var column = prop!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), false)
                .Cast<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>().FirstOrDefault();
            Assert.True(column != null, $"{row.Property} 没有 [Column]：它会用属性名当列名，与库里的列对不上");
            Assert.Equal(row.Column, column!.Name);
            Assert.Equal(row.Order, column.Order);
        }
        // 缺省值也在这个类型上钉住：ConditionKind 必须是 -1（继承），0 是"无条件"。
        var freshEntity = new Horizon.Model.GameModel.MonsterDropEntryEntity();
        Assert.Equal(-1, freshEntity.ConditionKind);
        Assert.Equal(0, freshEntity.QuestId);
        Assert.Equal(0, freshEntity.GroupId);
        Assert.True(entityText.Contains("public int ConditionKind { get; set; } = -1;"),
                    "实体源码里的 ConditionKind 默认值写法变了：请同步这条断言（缺省必须是继承哨兵）");

        // 判据必须**限定在逐件掉落那张表的片段里**：condition_kind/quest_id 这两列名
        // 在绑定表里也叫这个名（绑定级条件是历史形态），整份文件数子串会把两张表一起数进去，
        // 于是"逐件表缺列"也能被绑定表那份同名声明凑够数——闸门绿而表建错。
        var artifacts = new Dictionary<string, (string Path, string Start, string End, string[] Needles)>
        {
            ["迁移CreateTable"] = (Path.Combine("Horizon.Entities", "Migrations", "Games", "20261025123917_AddMonsterEditorTables.cs"),
                                   "name: \"Game_HunduShijie_MonsterDropEntry\"", "constraints: table =>",
                                   new[] { "condition_kind = table.Column<int>", "quest_id = table.Column<int>", "group_id = table.Column<int>" }),
            ["迁移Designer"] = (Path.Combine("Horizon.Entities", "Migrations", "Games", "20261025123917_AddMonsterEditorTables.Designer.cs"),
                                "Entity(\"Horizon.Model.GameModel.MonsterDropEntryEntity\"", "modelBuilder.Entity(\"Horizon.Model.GameModel.MonsterLootBindingEntity\"",
                                new[] { ".HasColumnName(\"condition_kind\")", ".HasColumnName(\"quest_id\")", ".HasColumnName(\"group_id\")" }),
            ["ModelSnapshot"] = (Path.Combine("Horizon.Entities", "Migrations", "Games", "GameEntityContextModelSnapshot.cs"),
                                 "Entity(\"Horizon.Model.GameModel.MonsterDropEntryEntity\"", "modelBuilder.Entity(\"Horizon.Model.GameModel.MonsterLootBindingEntity\"",
                                 new[] { ".HasColumnName(\"condition_kind\")", ".HasColumnName(\"quest_id\")", ".HasColumnName(\"group_id\")" }),
            ["SQL019建表"] = (Path.Combine("scripts", "sql", "019_monster_editor_tables.sql"),
                              "CREATE TABLE [Game_HunduShijie_MonsterDropEntry] (", ");",
                              new[] { "[condition_kind] int NOT NULL", "[quest_id] int NOT NULL", "[group_id] int NOT NULL" }),
        };
        var report = new List<string>();
        foreach (var kv in artifacts)
        {
            var path = Path.Combine(root, kv.Value.Path);
            Assert.True(File.Exists(path), $"{kv.Key} 不在盘上：{kv.Value.Path}");
            var segment = TakeSegment(File.ReadAllText(path), kv.Value.Start, kv.Value.End, kv.Key);
            foreach (var needle in kv.Value.Needles)
            {
                var hits = Regex.Matches(segment, Regex.Escape(needle)).Count;
                Assert.True(hits == 1, $"{kv.Key} 的逐件掉落表片段里“{needle}”命中 {hits} 次（应为 1）");
            }
            // 同一片段里必须还能看到 seq 与 item_id：切段切错了的话这些断言会在空转中通过。
            Assert.True(segment.Contains("seq") && segment.Contains("item_id"), $"{kv.Key} 切到的不是逐件掉落那张表");
            report.Add($"{kv.Key}={kv.Value.Needles.Length}");
        }
        _log.WriteLine("逐件掉落表片段内逐列核对：" + string.Join(" ", report));

        // 默认值要用**实例**读：C# 的属性初始化器编进构造函数里，反射的 PropertyInfo.DefaultValue 拿不到它。
        var freshDto = new Horizon.Game.Core.Sim.Server.MonsterDropSnapshotDto();
        Assert.Equal(-1, freshDto.ConditionKind);
        Assert.Equal(0, freshDto.QuestId);
        Assert.Equal(0, freshDto.GroupId);   // 组号的缺省反过来必须是 0（独立掷），两边各钉各的
        Assert.True(typeof(Horizon.Game.Sim.Monsters.MonsterDropSnapshot).GetField("ConditionKind") != null,
                    "运行期快照结构没有 ConditionKind 字段");
        Assert.True(typeof(Horizon.Game.Sim.Monsters.MonsterDropSnapshot).GetField("QuestId") != null,
                    "运行期快照结构没有 QuestId 字段");

        // 库的读/写/导出三处都得真的搬运这两格（漏一处的表现各不相同，见上一族那条断言的注释）。
        var storeText = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "MonsterStore.cs"));
        var entryRead = TakeMethodBody(storeText, "public MonsterDraft? FindDraft");
        Assert.True(entryRead.Contains("ConditionKind = e.ConditionKind"), "FindDraft 没把逐件条件读回草稿：一保存就冲掉");
        Assert.True(entryRead.Contains("QuestId = e.QuestId"), "FindDraft 没把逐件任务 id 读回草稿");
        var entryWrite = TakeMethodBody(storeText, "public MonsterStoreResult Upsert");
        Assert.True(entryWrite.Contains("ConditionKind = Math.Clamp(e.ConditionKind"), "Upsert 没落逐件条件");
        Assert.True(entryWrite.Contains("QuestId = e.QuestId"), "Upsert 没落逐件任务 id");
        var entryExport = TakeMethodBody(storeText, "public Horizon.Game.Core.Sim.Server.MonsterContentSnapshot BuildContentSnapshot");
        Assert.True(entryExport.Contains("ConditionKind = e.ConditionKind"), "导出没带逐件条件：库里配的任务件到运行时按绑定出账");
        Assert.True(entryExport.Contains("QuestId = e.QuestId"), "导出没带逐件任务 id");

        // 解析规则只有一个主人：校验器与试算都不许再自己写一遍"条目为 -1 才退回绑定"。
        var validatorText = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "MonsterValidator.cs"));
        Assert.Equal(1, Regex.Matches(validatorText, "ResolveConditionKind\\(").Count);
        Assert.False(validatorText.Contains("e.ConditionKind < 0 ? lb.ConditionKind"), "校验器自己抄了一遍继承规则");
        var previewText = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "MonsterDropPreview.cs"));
        Assert.Equal(1, Regex.Matches(previewText, "ResolveConditionKind\\(").Count);
    }

    [Fact]
    public void 插件的键名自证覆盖每一张键清单()
    {
        // 插件在发请求前会把自己的 JSON 与协议清单对一遍（STJ 对不认识的键是静默丢弃：
        // "保存成功、库里全是默认值"就是这么来的）。以前它只查 stats/behavior/xp 三层，
        // 而真正会漂的 chanceBps/groupId/conditionKind 住在 skills、lootBindings、entries、points 里。
        // 这道闸门守的是**反向的漏配**：清单存在却没人按它检查——那和没有清单一样，但看起来是齐的。
        var root = RepoRoot();
        var windowPath = Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor", "Source",
            "MonsterEditorEditor", "MonsterEditorWindow.cs");
        Assert.True(File.Exists(windowPath), "找不到编辑器窗口源码：" + windowPath);
        var windowText = File.ReadAllText(windowPath);

        var modelPath = PluginModelPath;
        var lists = ParseKeySets(modelPath).Keys.ToList();
        Assert.True(lists.Count >= 9, $"插件侧只解析到 {lists.Count} 张键清单（顶层 + 8 类子对象）");

        var unused = new List<string>();
        foreach (var name in lists)
        {
            // 每张清单都必须在窗口源码里被引用（TopLevel 那份是入口，其余八张走键名自证）。
            var token = "Known" + name + "Keys";
            var hits = Regex.Matches(windowText, Regex.Escape(token)).Count;
            if (hits == 0) unused.Add(token);
            Assert.True(hits >= 1, $"{token} 在插件源码里一次都没被引用：这张清单是死配置");
        }
        Assert.Empty(unused);

        // 空集兜底那条不许被"返回 null 就跳过"替代：认不出清单名时必须拦下发送。
        Assert.True(windowText.Contains("return new HashSet<string>(StringComparer.Ordinal);"),
                    "AllowedKeysFor 没有「认不出就返回空集」的兜底：漏配一张清单会变成静默放行");

        // 两层深的那两处必须显式往下走（键清单是平面的，嵌套关系要有人接线）。
        Assert.True(windowText.Contains("KnownDropKeys") && windowText.Contains("\"entries\""),
                    "掉落条目那一层没接进键名自证：条目里的 chanceBps/groupId/conditionKind 漂了没人拦");
        Assert.True(windowText.Contains("KnownPointKeys") && windowText.Contains("\"points\""),
                    "巡逻点那一层没接进键名自证");
    }

    [Fact]
    public void 校验器把两个经验倍率配了却没作用逐条念出来()
    {
        // 这两格的坑不是越界，而是"看着配了、数字一点没变"：
        // 血薄削减只认 0..7500（9000 与不填一样），而 xpMod 填 0 是真的归零（不是"没配"）。
        Horizon.MonsterCli.MonsterDraft Draft(int xpMod, int healthMod, int xpBase = 45, int xpPer = 5)
        {
            var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100021, Name = "倍率校验兽" };
            draft.ModelKey = "xian_zhu";
            draft.Xp.XpBase = xpBase;
            draft.Xp.XpPerLevel = xpPer;
            draft.Xp.XpModBps = xpMod;
            draft.Xp.HealthModBps = healthMod;
            return draft;
        }

        var deadHealth = Horizon.MonsterCli.MonsterValidator.Validate(Draft(10000, 9000), null);
        Assert.True(deadHealth.Ok, "合法草稿被拒：" + string.Join(";", deadHealth.Errors));
        Assert.Contains(deadHealth.Warnings, w => w.Contains("不生效"));

        var zeroXp = Horizon.MonsterCli.MonsterValidator.Validate(Draft(0, 10000), null);
        Assert.Contains(zeroXp.Warnings, w => w.Contains("一点经验都不给"));
        // 基准本身没配反（同级算得出经验），是倍率归的零 ⇒ 只能是提示，不能是"配反了"那句错。
        Assert.DoesNotContain(zeroXp.Errors, e => e.Contains("被配反了"));

        var badBase = Horizon.MonsterCli.MonsterValidator.Validate(Draft(10000, 10000, 0, 0), null);
        Assert.False(badBase.Ok);
        Assert.Contains(badBase.Errors, e => e.Contains("被配反了"));

        var negatives = Horizon.MonsterCli.MonsterValidator.Validate(Draft(-5, -1), null);
        Assert.False(negatives.Ok);
        Assert.Contains(negatives.ErrorPaths, p => p.EndsWith("xpModBps"));
        Assert.Contains(negatives.ErrorPaths, p => p.EndsWith("healthModBps"));

        // 正反对照：默认两格（都 10000）与 7500 那一档的合法配置都不该有噪声。
        foreach (var health in new[] { 10000, 7500, 5000 })
        {
            var quiet = Horizon.MonsterCli.MonsterValidator.Validate(Draft(10000, health), null);
            Assert.True(quiet.Ok, "合法草稿被拒：" + string.Join(";", quiet.Errors));
            Assert.DoesNotContain(quiet.Warnings, w => w.Contains("不生效"));
            Assert.DoesNotContain(quiet.Warnings, w => w.Contains("一点经验都不给"));
        }
    }

    [Fact]
    public void 两个经验倍率穿过每一层时名字都是同一个东西()
    {
        // xp_mod_bps / health_mod_bps 与上一条族一样要穿六层；这两处还多一条：**镜像副本**。
        // 插件里另有一份本地曲线（MonsterPreviewMath）用来即时画，它对拍的参照是服务端的 preview-xp。
        // 只要镜像少了这两段，编辑器曲线与服务端给的数就会差出一截，而"一致/不一致"是窗口自己说的。
        var root = RepoRoot();

        var plugin = ParseKeySets(PluginModelPath);
        var cli = ParseKeySets(CliModelPath);
        Assert.True(plugin["Xp"].Contains("xpModBps") && plugin["Xp"].Contains("healthModBps"), "插件侧 xp 键清单没补这两格");
        Assert.True(cli["Xp"].Contains("xpModBps") && cli["Xp"].Contains("healthModBps"), "CLI 侧 xp 键清单没补这两格");
        Assert.Equal(cli["Xp"], plugin["Xp"], StringComparer.Ordinal);

        // 实体列名/列序号/缺省值：按类型反射读，不在整份文件里数子子串（另一张表可能同名）。
        foreach (var row in new (string Property, string Column, int Order)[]
        {
            ("XpBase", "xp_base", 21), ("XpPerLevel", "xp_per_level", 22),
            ("XpModBps", "xp_mod_bps", 29), ("HealthModBps", "health_mod_bps", 30),
        })
        {
            var prop = typeof(Horizon.Model.GameModel.MonsterTemplateEntity).GetProperty(row.Property);
            Assert.True(prop != null, $"MonsterTemplateEntity 没有属性 {row.Property}");
            var column = prop!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), false)
                .Cast<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>().FirstOrDefault();
            Assert.True(column != null, $"{row.Property} 没有 [Column]");
            Assert.Equal(row.Column, column!.Name);
            Assert.Equal(row.Order, column.Order);
        }
        var freshTemplate = new Horizon.Model.GameModel.MonsterTemplateEntity();
        Assert.Equal(10000, freshTemplate.XpModBps);   // 缺省必须是"不开"，不能是 0（=一点经验都不给）
        Assert.Equal(10000, freshTemplate.HealthModBps);

        // 四份未执行的建表产物：限定在**模板那张表**的片段里逐列对。
        var artifacts = new (string Label, string Path, string Start, string End)[]
        {
            ("迁移CreateTable", Path.Combine("Horizon.Entities", "Migrations", "Games", "20261025123917_AddMonsterEditorTables.cs"),
             "name: \"Game_HunduShijie_MonsterTemplate\"", "constraints: table =>"),
            ("迁移Designer", Path.Combine("Horizon.Entities", "Migrations", "Games", "20261025123917_AddMonsterEditorTables.Designer.cs"),
             "Entity(\"Horizon.Model.GameModel.MonsterTemplateEntity\"", "modelBuilder.Entity(\"Horizon.Model.GameModel.QuestTemplateEntity\""),
            ("ModelSnapshot", Path.Combine("Horizon.Entities", "Migrations", "Games", "GameEntityContextModelSnapshot.cs"),
             "Entity(\"Horizon.Model.GameModel.MonsterTemplateEntity\"", "modelBuilder.Entity(\"Horizon.Model.GameModel.QuestTemplateEntity\""),
            ("SQL019建表", Path.Combine("scripts", "sql", "019_monster_editor_tables.sql"),
             "CREATE TABLE [Game_HunduShijie_MonsterTemplate] (", ");"),
        };
        var report = new List<string>();
        foreach (var a in artifacts)
        {
            var fullPath = Path.Combine(root, a.Path);
            Assert.True(File.Exists(fullPath), $"{a.Label} 不在盘上：{a.Path}");
            var segment = TakeSegment(File.ReadAllText(fullPath), a.Start, a.End, a.Label);
            foreach (var needle in new[] { "xp_mod_bps", "health_mod_bps" })
            {
                var hits = Regex.Matches(segment, Regex.Escape(needle)).Count;
                Assert.True(hits >= 1, $"{a.Label} 的模板表片段里没有 {needle}（这张表建出来会缺列）");
            }
            report.Add(a.Label);
        }
        _log.WriteLine("模板表片段里两列都在：" + string.Join(" / ", report));

        // 库的读/写/导出三处 + 命令行试算 + 插件本地镜像：**两个键各自都要在三处出现**，
        // 只断言其中一个（我第一版就只查了 HealthModBps）会让"导出漏带 XpModBps"这一改没人发现。
        var storeText = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "MonsterStore.cs"));
        var storeSites = new (string Label, string Signature, string[][] Needles)[]
        {
            ("FindDraft", "public MonsterDraft? FindDraft",
                new[] { new[] { "XpModBps = t.XpModBps" }, new[] { "HealthModBps = t.HealthModBps" } }),
            ("Upsert", "public MonsterStoreResult Upsert",
                new[] { new[] { "template.XpModBps = draft.Xp.XpModBps" }, new[] { "template.HealthModBps = draft.Xp.HealthModBps" } }),
            ("BuildContentSnapshot", "public Horizon.Game.Core.Sim.Server.MonsterContentSnapshot BuildContentSnapshot",
                new[] { new[] { "XpModBps = t.XpModBps" }, new[] { "HealthModBps = t.HealthModBps" } }),
        };
        foreach (var site in storeSites)
        {
            var body = TakeMethodBody(storeText, site.Signature);
            foreach (var needle in site.Needles)
            {
                Assert.True(body.Contains(needle[0]), $"{site.Label} 没搬运 {needle[0]}：那一跳会把倍率丢成缺省值");
            }
        }
        var cliText = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "Program.cs"));
        Assert.True(cliText.Contains("--xpModBps") && cliText.Contains("xpMod, healthMod"),
                    "preview-xp 不接受这两个倍率 ⇒ 对拍只能在「没开倍率」的曲线上比，开了就没人管");
        var snapshotDto = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core", "Sim", "Server", "MonsterContentSnapshot.cs"));
        Assert.True(snapshotDto.Contains("MonsterXpConfig.DefaultRankScale()"),
                    "装配层还在自己抄 rank 倍率表：那份字面量现在只有一个主人");
        Assert.Equal(0, Regex.Matches(snapshotDto + cliText,
            Regex.Escape("new double[] { 1.0, 2.0, 2.5, 4.0, 4.0 }")).Count);

        // 插件的本地镜像必须实现同样两段，且那个"只认 ≤7500"的上界与内核同值。
        var previewMath = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor", "Source",
            "MonsterEditor", "MonsterPreviewMath.cs"));
        Assert.True(previewMath.Contains("HealthModEffectiveCapBps"), "插件镜像没有血薄削减那一段（曲线会自报一个服务端给不出的数）");
        var pluginCap = Regex.Match(previewMath, @"HealthModEffectiveCapBps\s*=\s*(\d+)");
        Assert.True(pluginCap.Success, "插件镜像里的上界不是字面常量，闸门读不到它");
        Assert.Equal(Horizon.Game.Sim.Monsters.MonsterXpCore.HealthModEffectiveCapBps,
                     int.Parse(pluginCap.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.True(previewMath.Contains("xpModBps") && previewMath.Contains("healthModBps"),
                    "镜像的 Xp() 没接这两个参数");
        var windowText = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor", "Source",
            "MonsterEditorEditor", "MonsterEditorWindow.cs"));
        Assert.True(windowText.Contains("--healthModBps") && windowText.Contains("_healthModBox"),
                    "编辑器没把这两格送到对拍命令行，或根本没有格子");
    }

    [Fact]
    public void 校验器说清表级件数区间只管互斥组()
    {
        // min_count/max_count 这两格长期是死配置（没人读）。现在它接的是互斥组的重掷轮数，
        // 所以"这张表没有互斥组却配了抽几件"仍然是没有作用对象——这一半必须当场念出来，
        // 否则就是下一轮的"我配了抽 3 件为什么每次只掉一件"。
        Horizon.MonsterCli.MonsterDraft Table(int minCount, int maxCount, params (int ItemId, int Chance, int Group)[] rows)
        {
            var draft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100031, Name = "件数校验兽" };
            draft.ModelKey = "xian_zhu";
            var binding = new Horizon.MonsterCli.MonsterLootBindingDraft
            {
                LootTableId = 1, RollChanceBps = 10000, MinCount = minCount, MaxCount = maxCount,
            };
            for (var i = 0; i < rows.Length; i++)
            {
                binding.Entries.Add(new Horizon.MonsterCli.MonsterDropDraft
                {
                    ItemId = rows[i].ItemId, ChanceBps = rows[i].Chance, GroupId = rows[i].Group, Seq = i + 1,
                });
            }
            draft.LootBindings.Add(binding);
            return draft;
        }

        var noGroup = Horizon.MonsterCli.MonsterValidator.Validate(
            Table(1, 3, (90001, 5000, 0), (90002, 5000, 0)), null);
        Assert.True(noGroup.Ok, "合法草稿被拒：" + string.Join(";", noGroup.Errors));
        Assert.Contains(noGroup.Warnings, w => w.Contains("没有作用对象"));

        var inverted = Horizon.MonsterCli.MonsterValidator.Validate(Table(4, 2, (90003, 5000, 1)), null);
        Assert.False(inverted.Ok);
        Assert.Contains(inverted.ErrorPaths, p => p.EndsWith(".maxCount"));

        var overCap = Horizon.MonsterCli.MonsterValidator.Validate(Table(1, 99, (90004, 5000, 1)), null);
        Assert.Contains(overCap.Warnings, w => w.Contains("钳到"));

        // 正反对照：区间作用在真有互斥组的表上、以及退回单轮时，都不该有这两条噪声。
        var grouped = Horizon.MonsterCli.MonsterValidator.Validate(Table(1, 3, (90005, 5000, 1), (90006, 5000, 1)), null);
        Assert.True(grouped.Ok, "合法草稿被拒：" + string.Join(";", grouped.Errors));
        Assert.DoesNotContain(grouped.Warnings, w => w.Contains("没有作用对象"));
        Assert.DoesNotContain(grouped.Warnings, w => w.Contains("钳到"));
        var single = Horizon.MonsterCli.MonsterValidator.Validate(Table(1, 1, (90007, 5000, 0)), null);
        Assert.DoesNotContain(single.Warnings, w => w.Contains("没有作用对象"));
    }

    /// <summary>从测试运行目录向上找到本测试工程的源码目录；找不到就大声失败（绝不静默跳过）。</summary>
    private static string LocateGatewayTestSourceDir()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            if (string.Equals(dir.Name, "Horizon.Game.Gateway.Tests", System.StringComparison.OrdinalIgnoreCase)
                && System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "MonsterEditorContractTests.cs")))
                return dir.FullName;
        }
        throw new System.InvalidOperationException(
            "找不到 Horizon.Game.Gateway.Tests 的源码目录；这道扫描闸门不能退化成空跑。");
    }

    [Fact]
    public void 文本源文件是一张表两张出口而坏行逐条点名()
    {
        // 文本这一格走"旁挂源文件 + 线上只发号"（决策 D15）：库里没有文本表，
        // 而**两张出口**（服务端内容包 / 客户端解析表）必须读同一个源，
        // 否则会出现"服务端认得有这一号、客户端查不到文案"——那与"没配文本"在玩家那边同形。
        var repo = RepoRoot();
        var sourcePath = Path.Combine(repo, "HundunWorld", "Data", "Monsters", "monster_texts.json");
        var receipt = Horizon.MonsterCli.MonsterTextsExport.LoadSource(sourcePath);
        Assert.True(receipt.Ok, "真源文件读不出来：" + string.Join(" | ", receipt.Problems));
        Assert.False(receipt.FileMissing);
        // **不许把行数钉死**：这一格的断言是"源读得出来、坏行会被点名"，不是"今天库里恰好有几句话"。
        // 写死 2 的结果是——切片 66 给排练库补了 5 句真文案、重新导出之后，这条闸门红在一个完全正确的内容变更上
        // （那种红会训练人忽略闸门）。防空转只需要"至少有一行"。
        Assert.NotEmpty(receipt.Rows);
        Assert.Empty(receipt.Problems);

        // 坏行：非法编号 / 号是 0 哨兵 / 空文案 / 同 (怪,号) 重复 ⇒ 各点名一次，只留一条好行
        var tmp = Path.Combine(Path.GetTempPath(), "hzn-texts-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(tmp, "{\"texts\":["
            + "{\"monsterId\":0,\"sayId\":1,\"text\":\"编号非法\"},"
            + "{\"monsterId\":6,\"sayId\":0,\"text\":\"号是哨兵\"},"
            + "{\"monsterId\":6,\"sayId\":2,\"text\":\"   \"},"
            + "{\"monsterId\":6,\"sayId\":3,\"text\":\"第一句\"},"
            + "{\"monsterId\":6,\"sayId\":3,\"text\":\"重复的一句\"}]}");
        var bad = Horizon.MonsterCli.MonsterTextsExport.LoadSource(tmp);
        Assert.True(bad.Ok);
        Assert.Single(bad.Rows);
        Assert.Equal(3, bad.Rows[0].SayId);
        Assert.Equal(4, bad.Problems.Count);
        Assert.Contains(bad.Problems, p => p.Contains("monster_id=0"));
        Assert.Contains(bad.Problems, p => p.Contains("文本号是 0"));
        Assert.Contains(bad.Problems, p => p.Contains("空文案"));
        Assert.Contains(bad.Problems, p => p.Contains("重复"));

        try
        {
            // 客户端表按 (modelKey, sayId) 查：查不到 modelKey 的行必须点名并跳过（不静默丢）
            var keys = new Dictionary<int, string> { [6] = "rui_jin_ye_cha" };
            var built = Horizon.MonsterCli.MonsterTextsExport.Build(
                new[]
                {
                    new Horizon.MonsterCli.MonsterTextRow(6, 3, 0, 0, "锐金出鞘", 1),
                    new Horizon.MonsterCli.MonsterTextRow(7, 1, 0, 0, "没有模型键的一句", 0),
                },
                keys);
            Assert.Equal(1, built.Count);
            Assert.Contains(built.Problems, p => p.Contains("没有 model_key"));
            foreach (var k in Horizon.MonsterCli.MonsterTextsExport.ClientKeys)
            {
                Assert.Contains("\"" + k + "\"", built.Json);
            }

            // 一行都没有时不许把"problems 为空"读成一切正常
            var none = Horizon.MonsterCli.MonsterTextsExport.Build(
                Array.Empty<Horizon.MonsterCli.MonsterTextRow>(), keys);
            Assert.Equal(0, none.Count);
            Assert.Contains(none.Problems, p => p.Contains("没有任何条目"));

            // 读侧的键名是跨脚本域的字面量协议：漂一个键 = 客户端永远查不到，而编译照过
            var catalog = Path.Combine(repo, "HundunWorld", "Source", "Game", "Selection", "MonsterTextCatalog.cs");
            Assert.True(File.Exists(catalog), "客户端读侧不存在：" + catalog);
            var text = File.ReadAllText(catalog);
            foreach (var k in new[] { "texts", "modelKey", "sayId", "text", "emote" })
            {
                Assert.Contains("\"" + k + "\"", text);
            }
            // 缓存必须按文件指纹失效（外观表那条"静态缓存跨 PIE 存活把'表不存在'记成一辈子"的教训）
            Assert.Contains("LastWriteTimeUtc", text);
            Assert.Contains("DateTime.MinValue", text);
            Assert.Contains("条目 {Entries.Count}", text);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* 临时文件清不掉不影响判据 */ }
        }
    }

    [Fact]
    public void 文本这一族三个动词在命令行与面板两侧都真的接上了()
    {
        // 为什么钉"两侧都有"：文案这条路有**三个入口**（写源文件 / 读源文件回填格子 / 重导客户端表），
        // 少接一个的表现都是静默的——面板能写而格子里永远是空的、或源文件新了而游戏念旧的。
        // 这类"跨进程/跨脚本域的接线"靠编译证不了（两边互相引用不到），只能按调用形状数。
        var repo = RepoRoot();
        var program = File.ReadAllText(Path.Combine(repo, "Horizon.MonsterCli", "Program.cs"));
        var window = File.ReadAllText(Path.Combine(repo, "HundunWorld", "Plugins", "MonsterEditor",
            "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));

        // ① CLI 侧：三个动词各只有**一个**分发项（写两处就是两条通路各走一套，迟早漏改一处）。
        foreach (var dispatch in new[] { "\"set-text\" =>", "\"get-texts\" =>", "\"export-texts\" =>" })
        {
            var hits = CountCodeOccurrences(program, dispatch);
            Assert.True(hits == 1, $"CLI 分发里 {dispatch} 出现 {hits} 处（应当恰好 1 处）");
        }
        // ② CLI 侧：文案那一格必须走标准输入，且空 stdin 要被拒绝——
        //    漏传参数与"我是真想删"在 argv 上同形，那会一次手滑删掉一句台词。
        Assert.Equal(1, CountCodeOccurrences(program, "text = _stdin.ReadToEnd();"));
        Assert.True(CountCodeOccurrences(program, "但标准输入是空的") >= 1,
                    "空 stdin 那条拒绝话术不在代码里（只在注释里说过）：拒绝分支压根没写");

        // ③ 面板侧：三个动词都得有**发出方**（只有 CLI 认得而没人发，等于这条链对策划不存在）。
        Assert.True(CountCodeOccurrences(window, "export-texts --out") >= 1,
                    "面板没发过 export-texts：源文件写了而客户端表不会重导，游戏里仍一句都不念");
        Assert.True(CountCodeOccurrences(window, "set-text --monster=") >= 1,
                    "面板没发过 set-text：文案那一格就是装饰");
        Assert.True(CountCodeOccurrences(window, "get-texts --monster=") >= 1,
                    "面板没发过 get-texts：打开草稿时格子里永远是空的");

        // ④ 面板写的路径与客户端读侧的路径是**两份字符串**（脚本域互相引用不到）：
        //    漂一个字的话导出会成功而客户端读的是另一个文件——与外观表同一族，按字面钉一次。
        var catalog = Path.Combine(repo, "HundunWorld", "Source", "Game", "Selection", "MonsterTextCatalog.cs");
        Assert.Contains("client_texts.json", File.ReadAllText(catalog));
        Assert.Contains("client_texts.json", window);

        // ⑤ "缺省位置"只许内核那一份知道：调用方一旦能把缺省串当**显式路径**传进解析函数，
        //    "显式相对串按当前目录算"这条规则就会把缺省那一支一起带坏
        //    （切片 55 就这么把已修好的坏路又走回去一次，而且是单测全绿的情况下）。
        Assert.Equal(0, CountCodeOccurrences(program, "SourcePathArg(rest, "));
        Assert.Equal(0, CountCodeOccurrences(program, "DefaultSourcePath"));
        Assert.Equal(0, CountCodeOccurrences(program, "DefaultClientTablePath"));
        // 缺省与显式都从同一个口子过：四个动词各一处，写成"三处"就是有一条漏了改写。
        // 切片 73 起这个口子是 `TryGetTextsOption`（两种写法都认 + 空值拒绝），旧的只认空格写法的
        // `SourcePathArg` 已删——留着它就是两份解析，`--texts=…` 会继续被静默忽略。
        Assert.Equal(4, CountCodeOccurrences(program, "TryGetTextsOption(rest, out var textsArg, out var textsProblem)"));
        Assert.Equal(0, CountCodeOccurrences(program, "SourcePathArg("));
    }

    [Fact]
    public void 巡逻点级停留秒面板可写而覆盖式子只有内核那一份()
    {
        // 这一格的**数据面早就通了**（库列 → 草稿 → 快照 → 内核字段，往返由 db-roundtrip-check 的
        // points[i].pauseSeconds 覆盖），缺的两半是：① 内核那行覆盖从没被跑过（切片 54 新加的用例）；
        // ② 面板写不进去（以前只在选中提示里读得到）。这条闸门钉第②半的"真的接上了"，
        // 并钉住第①半的前提：**覆盖式子不许长出第二份**——v1/v2 各自再抄一遍，
        // 就会在"留空与 0 谁赢"上分家，而那是玩家看得见的（一路走 vs 到点站住）。
        var repo = RepoRoot();
        var window = File.ReadAllText(Path.Combine(repo, "HundunWorld", "Plugins", "MonsterEditor",
            "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));
        var kernel = File.ReadAllText(Path.Combine(repo, "Horizon.Game.Sim", "Monsters", "MonsterBehaviorCore.cs"));
        var v1 = File.ReadAllText(Path.Combine(repo, "Horizon.Game.Sim", "MonsterAiCore.cs"));
        var v2 = File.ReadAllText(Path.Combine(repo, "Horizon.Game.Sim", "Monsters", "MonsterAiRuntime.cs"));

        // ① 内核那一份覆盖式子恰好一处，两路 AI 都不许自己判
        Assert.Equal(1, CountCodeOccurrences(kernel, "point.PauseSeconds >= 0f ? point.PauseSeconds : route.PauseAtPointSeconds"));
        Assert.Equal(0, CountCodeOccurrences(v1, "PauseAtPointSeconds"));
        Assert.Equal(0, CountCodeOccurrences(v2, "PauseAtPointSeconds"));

        // ② 面板必须有一个**按下去就写**的入口（只有格子没有落笔 = 又一件装饰）。
        //    两个按钮（写入选中点 / 改为跟路线）**共用同一条落笔**：所以是
        //    "事件挂上去恰好一处" + "第二条入口以调用形式出现至少一处"。
        //    （第一版我把后者写成 >=2，红出来才发现自己数错了入口形状——这条红是闸门在拦我，
        //    不是产品缺一段：两个入口若各写一份赋值，"留空怎么算"就会分家。）
        Assert.True(CountCodeOccurrences(window, "Clicked += ApplyPointPause;") == 1,
                    "点级停留没有写入口：这一格对策划仍然不可达");
        Assert.True(CountCodeOccurrences(window, "ApplyPointPause();") >= 1,
                    "改为跟路线那条入口没走同一条落笔：两份实现迟早会在留空怎么算上分家");

        // ③ 写进去的必须**只有选中的那一个点**：按 index 取点、赋值 pauseSeconds。
        //    （写成遍历整条线就是"一格改动串到全部点"，而那在编辑器里看不见。）
        Assert.True(CountCodeOccurrences(window, "route.Points[_pickedPointIndex]") >= 1,
                    "没按选中点取那一格：写入选中点可能改到别的点上去");
        Assert.True(CountCodeOccurrences(window, "point.PauseSeconds =") >= 2,
                    "点级停留没落到草稿（null 与有值两支都要写）");

        // ④ 选中时要**回填**那一格，否则格子上留着的是上一个点的秒数，按下去就把上一个点盖到这一个点上
        Assert.True(CountCodeOccurrences(window, "_pointPauseBox.Text =") >= 1,
                    "选中点时没回填停留那一格：串格改动会静默写进别的点");

        // ⑤ 面板与校验器对"负数"要同口径：库里那一列存得下负数，而校验器拒绝，
        //    所以面板必须在落草稿之前就归成 null（留空），而不是让人按下保存到库才看见红。
        Assert.Contains("要默认值请留空而不是写 -1", File.ReadAllText(
            Path.Combine(repo, "Horizon.MonsterCli", "MonsterValidator.cs")));
        Assert.True(CountCodeOccurrences(window, "if (parsed < 0d)") == 1,
                    "面板没把负数收成留空：那会写出一条校验器必拒的草稿");
    }
    [Fact]
    public void 状态强度这一格在实体快照模型草稿DTOCLI与内核五处同名且列名一致()
    {
        // 这一格要穿过五层：实体列 → EF 模型快照 → 内容快照 DTO → 草稿键 → 运行期挂载结构体。
        // 少接一层就是"编辑器/库里填了而运行期读不到"，而这五层里**任何一层单独看都编译得过**。
        var root = RepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(root, Path.Combine(parts)));

        var entity = Read("Horizon.Model", "GameModel", "MonsterTemplateEntity.cs");
        var efSnapshot = Read("Horizon.Entities", "Migrations", "Games", "GameEntityContextModelSnapshot.cs");
        var contentSnapshot = Read("Horizon.Game.Core", "Sim", "Server", "MonsterContentSnapshot.cs");
        var draft = Read("Horizon.MonsterCli", "MonsterDraft.cs");
        var mount = Read("Horizon.Game.Sim", "Monsters", "MonsterCastingCore.cs");
        var cliStore = Read("Horizon.MonsterCli", "MonsterStore.cs");
        var sql = Read("scripts", "sql", "026_add_monster_status_magnitude.sql");

        // ① 实体：属性 + 列名。列名在这一层出现**两次**是正常的（`[Column]` 与 `[TableDescription]`
        //    各一次——这一族每一列都是这个形状）；出现三次就说明有人又抄了一遍。
        Assert.Equal(2, CountCodeOccurrences(entity, "status_magnitude_bps"));
        Assert.Contains("public int StatusMagnitudeBps", entity);
        // ② EF 模型快照：少了它下一次 migrations add 会把这列当成"要删"
        Assert.Contains("b.Property<int>(\"StatusMagnitudeBps\")", efSnapshot);
        Assert.Contains(".HasColumnName(\"status_magnitude_bps\")", efSnapshot);
        // ③ 内容快照 DTO + 投影到运行期
        Assert.Contains("public int StatusMagnitudeBps", contentSnapshot);
        Assert.Contains("StatusMagnitudeBps = StatusMagnitudeBps,", contentSnapshot);
        // ④ 草稿键（编辑器/命令行认的是这一串，不是属性名）
        Assert.Contains("public int StatusMagnitudeBps", draft);
        Assert.Contains("\"statusMagnitudeBps\"", draft);
        // ⑤ 运行期挂载结构体那一格
        Assert.Contains("public int StatusMagnitudeBps", mount);
        // ⑥ 两个读写口（实体→草稿、草稿→实体）都要搬这一格：
        //    各出现一次，少了就是"存得进读不回"（或者反过来）。
        Assert.Equal(2, CountCodeOccurrences(cliStore, "StatusMagnitudeBps = m.StatusMagnitudeBps"));
        Assert.Equal(1, CountCodeOccurrences(cliStore, "StatusMagnitudeBps = s.StatusMagnitudeBps"));
        // ⑦ DDL 只生成不执行：脚本文本钉住"幂等 + 缺省 0"，并**不许**出现 DROP TABLE 那种不可逆动作。
        Assert.Contains("[status_magnitude_bps]", sql);
        Assert.Contains("DEFAULT (0)", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
    }
    [Fact]
    public void 战术位关着时保持距离与逃跑阈值要当场念()
    {
        // 风筝（保持距离）与残血逃跑只在**战术位开着**的那一支里被读（v2：`flags.UseTactics && …`），
        // v1 通路根本不实现它们。档位 0/1/2 展开后 tactics=关 ⇒ 这三格配了没人问：
        // 表现是"远程怪照样贴脸、残血也站着打"，而策划会先去查射程和血量。
        // 判据必须问**展开后的那一位**而不是档位号：档位 1 + 战术位强制开 = 同样生效。
        static Horizon.MonsterCli.MonsterValidationResult Run(int profile, int? tacticsOverride,
                                                              double rMin, double rMax, int flee)
        {
            var d = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100014, Name = "战术位校验" };
            d.Behavior.AiProfile = profile;
            d.Behavior.FlagUseTactics = tacticsOverride;
            d.Behavior.AggroRadius = 10;
            d.Behavior.LeashRadius = 30;
            d.Behavior.MeleeSwingSeconds = 1.5f;
            d.Behavior.RangedMinDistance = rMin;
            d.Behavior.RangedMaxDistance = rMax;
            d.Behavior.FleeHpPercent = flee;
            return Horizon.MonsterCli.MonsterValidator.Validate(d, null);
        }

        bool Says(Horizon.MonsterCli.MonsterValidationResult r, string key)
            => r.Warnings.Any(w => w.Contains(key, StringComparison.Ordinal));

        Assert.True(Says(Run(1, null, 5, 20, 0), "behavior.useTactics"),
                    "档位 1 配了保持距离却不念：这三格运行时没人读，远程怪会贴脸");
        Assert.True(Says(Run(1, null, 0, 0, 30), "behavior.useTactics"),
                    "档位 1 配了逃跑阈值却不念：残血时它不会跑");
        Assert.True(!Says(Run(3, null, 5, 20, 30), "behavior.useTactics"),
                    "档位 3 本来就读这三格，却还在报警");
        Assert.True(!Says(Run(1, 2, 5, 20, 30), "behavior.useTactics"),
                    "战术位强制开也读这三格——按档位号判会误报这种合法配法");
        Assert.True(!Says(Run(1, null, 0, 0, 0), "behavior.useTactics"),
                    "什么都没配也念：这条 Warn 会在全部草稿上长鸣，把真问题埋成噪声");
    }

    [Fact]
    public void 面板技能行把控制状态三格真的摆出来并提交()
    {
        // 这三格以前刻意没有输入框（"没有消费者就不给格子"）；切片 59/60/62 之后三个都有了消费者，
        // 所以格子要开出来，并且**开在能被提交的那条路上**——摆一个没人读的控件比不摆更糟。
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Plugins", "MonsterEditor",
                                                    "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));

        Assert.Contains("_statusDrop = MakeDrop(", window);
        Assert.Contains("_statusSecondsBox = new TextBox(", window);
        Assert.Contains("_statusMagnitudeBox = new TextBox(", window);
        // 下拉项顺序必须与词表一致（下标即 status_id，中间再插一层翻译表就会与库里存的号分家）
        Assert.Contains("new[] { \"无\", \"眩晕\", \"沉默\", \"定身\", \"减速\" }", window);
        // 读回一处、提交三处（号/时长/强度各一处）
        Assert.Equal(1, CountCodeOccurrences(window, "_statusDrop.SelectedIndex = used"));
        Assert.Equal(1, CountCodeOccurrences(window, "Model.StatusId = _statusDrop.SelectedIndex"));
        Assert.Equal(1, CountCodeOccurrences(window, "Model.StatusMagnitudeBps = ParseIntLocal("));
        Assert.Equal(1, CountCodeOccurrences(window, "Model.StatusDurationSeconds = ParseDoubleLocal("));
        // 行高要装得下第四行：y+66 再加 20 高 = 86，Height 小于它就是最后一格被裁掉（看得见布局看不见控件）。
        Assert.Contains("public const float Height = 90f;", window);
    }
    [Fact]
    public void 三个导出与整库普查的回执都念出它读的是哪个库()
    {
        // 面板的三个导出按钮与"内容普查"都读 **CLI 连的那个库**（环境变量/缺省决定），
        // 而排练库与生产库导出的回执原来长得一模一样——"以为在导排练库、其实导的是生产库"。
        var root = RepoRoot();
        var program = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "Program.cs"));
        // 四处回执各带一格 database（export-content / export-texts / export-visuals / audit），list 也带
        Assert.Equal(5, CountCodeOccurrences(program, "[\"database\"] = store!.DatabaseName"));
        var store = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "MonsterStore.cs"));
        Assert.Contains("public string DatabaseName", store);
        // 解析连接串而不是等连接打开：回执要能在"连都没连"时说清目标库
        Assert.Contains("SqlConnectionStringBuilder(connectionString)", store);

        var window = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor",
                                                    "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));
        Assert.Contains("private void LogReceiptDatabase(MonsterCliResult result)", window);
        // 三个导出 + 普查 = 四处调用（少一处就是那一格又不说库名）
        Assert.Equal(4, CountCodeOccurrences(window, "LogReceiptDatabase(result);"));
    }

    [Fact]
    public void 面板内容普查把每一条发现都念出来而不是只回一个数()
    {
        // `audit` 的正文在 CLI 顶层 `warnings` 里，计数只是 `data.findings`。
        // 面板桥原来只收 `data` ⇒ 面板最多能回"发现 9 条"，而人需要知道是哪 9 条。
        var root = RepoRoot();
        var bridge = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor",
                                                     "Source", "MonsterEditorEditor", "MonsterCliBridge.cs"));
        Assert.Contains("public System.Collections.Generic.List<string> Warnings", bridge);
        Assert.Contains("root.TryGetProperty(\"warnings\"", bridge);
        // 只钉"提到过 warnings"是空转：把那一行改成 `if (false && …)` 照样命中（等价变异体，实测过）。
        // 真正要钉的是**解析出来的东西被收进了结果**——这一行没了，面板就只剩一个计数。
        Assert.Contains("warnings.ValueKind == JsonValueKind.Array", bridge);
        Assert.Contains("result.Warnings.Add(warning.GetString())", bridge);

        var window = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor",
                                                    "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));
        Assert.Contains("AddToolbarButton(1456f, \"内容普查\"", window);
        Assert.Contains("MonsterCli.RunAsync(\"audit\"", window);
        Assert.Contains("foreach (var warning in result.Warnings)", window);
        // 降级读数必须明说（孤儿行这一格在 NPC 表缺失时查不全）
        Assert.Contains("npcTableReadable", window);
        // 十二格计数里至少要有这几个（少念一格＝那一层的内容从视野里消失）
        foreach (var key in new[] { "published", "invalid", "skillCapable", "navCapable", "assistCapable",
                                    "monstersWithLoot", "routeCapable", "corpseCapable", "textCapable" })
            Assert.Contains($"N(\"{key}\")", window);
    }

    [Fact]
    public void 面板能把运行时内容包导出来而不是只停在库里()
    {
        // 保存/发布只到库；游戏读的是 `export-content` 那份包（DsHost 的 `--monster-content`）。
        // 以前最后一个动作只能离开面板去敲 CLI——"库里有、世界里没有"就发生在这一步，
        // 而面板上每个按钮都刚回"成功"。
        var root = RepoRoot();
        var window = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor",
                                                    "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));
        Assert.Contains("AddToolbarButton(1368f, \"导出内容包\"", window);
        Assert.Contains("export-content --out", window);
        Assert.Contains("\"monster_content.json\"", window);
        // 回执必须逐格念（只报"成功"等于没报）：模板/刷怪点/路线/掉落/挂载/文本
        foreach (var key in new[] { "templates", "spawnPoints", "routes", "lootBindings", "skillMounts", "texts" })
            Assert.Contains($"N(\"{key}\")", window);
        // CLI 侧动词存在且只分发一处
        var program = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "Program.cs"));
        Assert.Equal(1, CountCodeOccurrences(program, "\"export-content\" =>"));
    }

    [Fact]
    public void 应援台词那一格与它的组号镜像也对得上服务端()
    {
        // 与临终那格同形：面板引用不到 Horizon.Game.Sim，组号是第二份字面量；
        // 写进错组的表现是"面板回成功、被叫起来时一句不念"。
        Assert.Equal(Horizon.Game.Sim.Monsters.MonsterContent.AssistSayGroup,
                     MonsterEditor.MonsterTextGroupMath.AssistGroup);

        var root = RepoRoot();
        var window = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor",
                                                    "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));
        Assert.Contains("_assistText = new TextBox(", window);
        Assert.Equal(1, CountCodeOccurrences(window, "MonsterTextGroupMath.SayIdOfGroup(_sayRows, MonsterTextGroupMath.AssistGroup)"));
        Assert.Equal(1, CountCodeOccurrences(window, "MonsterTextGroupMath.AssistGroup.ToString(CultureInfo.InvariantCulture)"));
        // 三个台词格串在同一条保存链上：进战 → 临终 → 应援（少接一格＝那一格只在"保存到库"时才落盘）
        Assert.Contains("WriteDeathTextIfChanged(() => WriteAssistTextIfChanged(onCompleted))", window);
    }

    [Fact]
    public void 临终台词那一格与组号镜像都对得上服务端()
    {
        // 死亡台词没有行为表指针，靠"落在组 2"被挑（`MonsterContent.ResolveDeathSayId`）。
        // 面板引用不到 Horizon.Game.Sim，所以组号是第二份字面量——两份必须相等，否则面板写进组 3
        // 而运行期只认组 2：台词写成功了、游戏里一句不念，且两边各自的回执都写着 ok。
        Assert.Equal(Horizon.Game.Sim.Monsters.MonsterContent.DeathSayGroup,
                     MonsterEditor.MonsterTextGroupMath.DeathGroup);

        var rows = new List<MonsterEditor.MonsterTextGroupMath.Row>
        {
            new MonsterEditor.MonsterTextGroupMath.Row(1, 0, 0, "进战"),
            new MonsterEditor.MonsterTextGroupMath.Row(2, MonsterEditor.MonsterTextGroupMath.DeathGroup, 1, "临终"),
        };
        Assert.Equal(2, MonsterEditor.MonsterTextGroupMath.SayIdOfGroup(rows, MonsterEditor.MonsterTextGroupMath.DeathGroup));
        Assert.Equal(0, MonsterEditor.MonsterTextGroupMath.SayIdOfGroup(rows, 7));

        var root = RepoRoot();
        var window = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor",
                                                    "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));
        Assert.Contains("_deathText = new TextBox(", window);
        Assert.Equal(1, CountCodeOccurrences(window, "MonsterTextGroupMath.SayIdOfGroup(_sayRows, MonsterTextGroupMath.DeathGroup)"));
        Assert.Equal(1, CountCodeOccurrences(window, "MonsterTextGroupMath.DeathGroup.ToString(CultureInfo.InvariantCulture)"));
        // 两次保存都必须先写进战再写临终（少接一处＝那一格只在"保存到库"时才落盘，按钮看上去一样）。
        // 计数含定义处 1 次 + 两个保存入口各 1 次 = 3。
        Assert.Equal(3, CountCodeOccurrences(window, "WriteSayTextsIfChanged("));
    }

    [Fact]
    public void 台词组的下一个号与组现状由同一份镜像算()
    {
        // "同组再加一句"要算下一个可用号：拿**行数 +1** 在删过句子的怪身上必然撞号，
        // 而撞号的表现是 set-text **覆盖**掉已有那句（策划以为加了一句，其实改了一句）。
        var rows = new List<MonsterEditor.MonsterTextGroupMath.Row>
        {
            new MonsterEditor.MonsterTextGroupMath.Row(77, 7, 1, "受死吧"),
            new MonsterEditor.MonsterTextGroupMath.Row(79, 7, 3, "血肉之躯"),   // 78 被删过：号必须跳过它
            new MonsterEditor.MonsterTextGroupMath.Row(90, 0, 0, "单句"),
        };
        Assert.Equal(91, MonsterEditor.MonsterTextGroupMath.NextSayId(rows));
        Assert.Equal(1, MonsterEditor.MonsterTextGroupMath.NextSayId(new List<MonsterEditor.MonsterTextGroupMath.Row>()));

        Assert.Equal(7, MonsterEditor.MonsterTextGroupMath.GroupOf(rows, 77));
        Assert.Equal(0, MonsterEditor.MonsterTextGroupMath.GroupOf(rows, 90));
        Assert.Equal(0, MonsterEditor.MonsterTextGroupMath.GroupOf(rows, 12345));   // 号不存在 = 不成组

        // 组现状按 (Seq, SayId) 排：77(Seq1) 在 79(Seq3) 前面，且句序是"第几句"而不是行序
        var text = MonsterEditor.MonsterTextGroupMath.DescribeGroup(rows, 7);
        Assert.Contains("第1句 #77", text);
        Assert.Contains("第2句 #79", text);
        Assert.Contains("共 2 句", text);
        Assert.DoesNotContain("#90", text);
        // 组 0 / 空组都不许返回空串：空串在界面上与"控件没接上"同形
        Assert.False(string.IsNullOrWhiteSpace(MonsterEditor.MonsterTextGroupMath.DescribeGroup(rows, 0)));
        Assert.False(string.IsNullOrWhiteSpace(MonsterEditor.MonsterTextGroupMath.DescribeGroup(rows, 123)));
    }

    [Fact]
    public void 面板台词组那一格真的接进了set_text与回读()
    {
        // 运行期已经会按组挑一句（`MonsterContent.ResolveSayId`），但如果作者侧写不进组，
        // 那条玩法只有手改 JSON 才用得上 —— 与"配置格没人写"是同一类洞。
        // 三处缺一不可：面板有格、CLI 能收 --group、回读时把组带回来（否则一保存就把它清成 0）。
        var root = RepoRoot();
        var window = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor",
                                                    "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));
        Assert.Contains("_sayGroupBox = Number(", window);
        Assert.Contains("--group=", window);
        Assert.Contains("ReadInt(row, \"group\")", window);
        // "同组再加一句"：一次只编一条台词时，多句一组必须有一个按下去就换号的动作，
        // 否则只能手抄号（抄重号会被 set-text 覆盖掉已有那句）。
        Assert.Contains("_sayGroupInfo = new Label(", window);
        Assert.Contains("addSayLine.Clicked += AddSayLineToGroup;", window);
        // 三处都要用同一份镜像算下一个号："同组再加一句"、"临终首次落号"、"应援首次落号"
        // （按行数 +1 那种算法在删过句子的怪身上必撞号，撞号＝覆盖别的一句）。
        Assert.Equal(3, CountCodeOccurrences(window, "MonsterTextGroupMath.NextSayId(_sayRows)"));
        // 只改文案而组没动时不许重写（重写会把读侧跳过的坏行一起带走）
        Assert.Contains("&& !groupChanged", window);

        var program = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "Program.cs"));
        Assert.Contains("TryGetOption(rest, \"group\", out int parsedGroup)", program);
        Assert.Contains("[\"group\"] = w.Group", program);

        var export = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "MonsterTextsExport.cs"));
        Assert.Contains("int? group = null", export);
        Assert.Contains("group ?? (existed ? previous.Group : 0)", export);
    }

    [Fact]
    public void 面板刷怪行把分片号摆出来并提交()
    {
        // shard_id 是运行期**真在读**的一格：DsWorldSession 对每个刷怪点做
        // `ShardId != 0 && ShardId != 本片 ⇒ 跳过`。以前只有草稿键与库列、面板没有入口
        // ⇒ 多片开世界时"这条线只刷二区"只能靠 CLI 手写 JSON。
        // 三处缺一不可：控件在（摆出来）、提交在（按下去就写）、行高装得下（不被裁掉）。
        var root = RepoRoot();
        var window = File.ReadAllText(Path.Combine(root, "HundunWorld", "Plugins", "MonsterEditor",
                                                    "Source", "MonsterEditorEditor", "MonsterEditorWindow.cs"));
        Assert.Contains("_shardBox = new TextBox(", window);
        Assert.Equal(1, CountCodeOccurrences(window, "Model.ShardId = ParseLongLocal(_shardBox.Text)"));
        Assert.Equal(1, CountCodeOccurrences(window, "_shardBox.Text = m != null && m.ShardId != 0"));
        // ShardId 是 long：走 int 解析器会在多片号段上静默截断/解析失败
        Assert.Contains("private static long ParseLongLocal(string text)", window);
        // 三行：y+44 再加 20 = 64，Height 小于它就是第三行被裁掉（看得见布局看不见控件）
        Assert.Contains("public const float Height = 66f;", window);
        // 面板写完还得有真写方落库，否则只是写进一份没人读的草稿
        var store = File.ReadAllText(Path.Combine(root, "Horizon.MonsterCli", "MonsterStore.cs"));
        Assert.Equal(1, CountCodeOccurrences(store, "row.ShardId = sp.ShardId;"));
    }

    [Fact]
    public void 客户端的控制状态位真的接到了输入闸口而不是只镜像了一份()
    {
        // 期 2a 的判据：服务端把 `Stunned`/`Rooted` 写进快照之后，客户端必须
        // ① 认得出这两个位（访问器与位定义同名）、② 镜像进唯一的 ControlStateManager、
        // ③ 在**产生移动输入的那一处**把腿拦下来，且 ④ 两侧用的是同一个 id（EntityId）。
        // 少了 ③ 就只是"把值搬进一个没人问的容器"——那一族的绿灯最贵。
        var root = RepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(root, Path.Combine(parts)));

        var components = Read("Horizon.Game.Message", "Sync", "Components", "SyncComponents.cs");
        Assert.Contains("public bool IsStunned", components);
        Assert.Contains("public bool IsRooted", components);
        Assert.Contains("(StateBits & EntityStateBits.Stunned) != 0", components);
        Assert.Contains("(StateBits & EntityStateBits.Rooted) != 0", components);

        var sync = Read("HundunWorld", "Source", "Game", "LocalPlayerActorSyncSystem.cs");
        Assert.Equal(1, CountCodeOccurrences(sync, "MirrorBit(control, characterId, ControlState.Stunned"));
        Assert.Equal(1, CountCodeOccurrences(sync, "MirrorBit(control, characterId, ControlState.Rooted"));
        // 心跳之间不许闪：TTL 必须**长于**快照心跳（1 秒）。这条挡的是"TTL 写 0.5 秒，1Hz 快照下每秒抖一下"。
        Assert.Contains("AuthoritativeStateTtlSeconds = 1.5f", sync);

        var controller = Read("HundunWorld", "Source", "Game", "PlayerController.cs");
        Assert.Contains("HasControlState(characterId, ControlState.Stunned)", controller);
        Assert.Contains("HasControlState(characterId, ControlState.Rooted)", controller);
        // 输入真的被清零（而不是只算了个布尔没人用）
        Assert.Contains("input.MoveX = movementLocked ? 0f :", controller);
        Assert.Contains("input.MoveY = movementLocked ? 0f :", controller);
        // 连跳/轻功那两位也要一起清（服务端抹的是 MoveX/MoveY + JumpOrLeapBits 同一组）
        Assert.Contains("if (movementLocked) inputBits = 0;", controller);

        // 两侧 id 同源：都取 NetworkIdentityComponent.EntityId（与 DS 的 characterId == entityId 口径一致）
        Assert.Contains("Get<NetworkIdentityComponent>(_localPlayerEntity).EntityId", sync);
        Assert.Contains("Get<NetworkIdentityComponent>(_localPlayerEntity).EntityId", controller);
    }
    [Fact]
    public void 沉默这一位从DS一路走到技能栏的拒绝且挡在冷却之前()
    {
        // 沉默与眩晕是**两个不同的后果**（一个只封嘴、一个连腿一起封），所以必须是两位。
        // 这条判据顺着链路走一遍：DS 写位 → 快照位定义 → 客户端镜像 → 技能栏拒绝，且拒绝要早于 StartCooldown。
        var root = RepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(root, Path.Combine(parts)));

        var components = Read("Horizon.Game.Message", "Sync", "Components", "SyncComponents.cs");
        Assert.Contains("public const uint Silenced = 1u << 7;", components);
        Assert.Contains("public bool IsSilenced", components);

        var session = Read("Horizon.Game.Core", "Sim", "Server", "DsWorldSession.cs");
        Assert.Contains("MonsterStatusKind.Silence))", session);
        Assert.Contains("bits |= EntityStateBits.Silenced;", session);

        var sync = Read("HundunWorld", "Source", "Game", "LocalPlayerActorSyncSystem.cs");
        Assert.Equal(1, CountCodeOccurrences(sync, "MirrorBit(control, characterId, ControlState.Silenced"));

        var bar = Read("HundunWorld", "Source", "Game", "UI", "Ink", "ActionBar", "ActionBarService.cs");
        Assert.Contains("HasControlState(localId, ControlState.Stunned)", bar);
        Assert.Contains("HasControlState(localId, ControlState.Silenced)", bar);
        // 拒绝必须**早于** StartCooldown：白吃一次冷却比一次明确的拒绝更难归因（与本文件里"打不到就别发"同一理由）。
        var refusal = bar.IndexOf("被沉默，无法施法", StringComparison.Ordinal);
        var start = bar.LastIndexOf("StartCooldown(skillId);", StringComparison.Ordinal);
        Assert.True(refusal > 0 && start > 0 && refusal < start,
                    $"沉默/眩晕的拒绝没挡在 StartCooldown 之前（refusal@{refusal}、start@{start}）：玩家会白吃一次冷却");
    }
}
