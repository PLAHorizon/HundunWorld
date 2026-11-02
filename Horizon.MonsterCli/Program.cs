using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Monsters;
using Horizon.QuestCli;

namespace Horizon.MonsterCli;

/// <summary>
/// 怪物模板的命令行入口：怪物编辑器（Flax 插件）以"短生命周期子进程"的方式调用它，
/// 由它把**校验**和**写库**这两件只在服务端一侧才说得清的事做完。
/// <para>
/// 输出恒为一段 JSON（UTF-8），字段固定 <c>ok/verb/message/data/warnings</c>；出错也走同一段 JSON，
/// 退出码非 0。与 <c>Horizon.QuestCli</c> 逐字同构，编辑器的解析代码因此可以照搬而不用重新发明。
/// </para>
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions OutputOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static TextWriter _stdout = Console.Out;
    private static TextReader _stdin = Console.In;

    /// <summary>
    /// 显式接管两条标准流（与被重定向句柄上 setter 抛 IOException 的历史教训同源：
    /// 悄悄退回控制台码页会让中文名字被读坏，而"库写进去了但名字是乱码"正是那种最难查的坏法）。
    /// </summary>
    private static void InitializeStreams()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
        _stdin = new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: true);
    }

    public static int Main(string[] args)
    {
        var verb = (args.Length > 0 ? args[0] : "help").Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        try
        {
            try
            {
                InitializeStreams();
            }
            catch (Exception streamFailure)
            {
                Console.Error.WriteLine("标准流接管失败：" + streamFailure.Message);
            }

            return verb switch
            {
                "help" or "--help" or "-h" => Run(verb, true,
                    "怪物模板 CLI：catalog | list | get <id> | validate - | save - | publish <id> | retire <id> | delete <id> | "
                    + "draft save|list|get|delete | preview-xp --mob L --player L [--rank R] [--sweep] | preview-drop --mob <id> | "
                    + "export-content --out <path> | export-visuals --out <path> | audit | "
                    + "export-texts [--out <path>] [--texts <源文件>] | set-text --monster=N --say=N --text=<文案> [--emote=N] [--group=N] | get-texts [--monster=N] | "
                    + "bake-nav --out <path> --width N --height M --cell 米 --origin X Y --mask - | spawn list [--mob <id>]", null),
                "catalog" => RunCatalog(),
                "audit" => RunAudit(),
                "list" => RunList(),
                "get" => RunGet(rest),
                "validate" => RunValidate(rest),
                "save" => RunSave(rest),
                "publish" => RunSetState(rest, 1),
                "retire" => RunRetire(rest),
                "delete" => RunDelete(rest),
                "draft" => RunDraft(rest),
                "preview-xp" => RunPreviewXp(rest),
                "preview-drop" => RunPreviewDrop(rest),
                "export-content" => RunExportContent(rest),
                "export-visuals" => RunExportVisuals(rest),
                "export-texts" => RunExportTexts(rest),
                "set-text" => RunSetText(rest),
                "get-texts" => RunGetTexts(rest),
                "bake-nav" => RunBakeNav(rest),
                "spawn" => RunSpawn(rest),
                _ => Run(verb, false, "未知动词：" + verb + "（跑 help 看用法）", null),
            };
        }
        catch (Exception ex)
        {
            // 上报必须带 errorType/errorStack：只回 ex.Message 的话，同一个神秘异常会被我猜三次。
            return Run(verb, false, "执行失败：" + ex.GetBaseException().Message, new Dictionary<string, object?>
            {
                ["errorType"] = ex.GetType().FullName,
                ["errorStack"] = ex.ToString(),
            });
        }
    }

    // ---------- 动词 ----------

    private static int RunCatalog()
    {
        if (!TryOpenStore(out var store, out var connError))
        {
            // 连不上库时 catalog 仍要回代码目录里的怪（编辑器的"新建"依赖它），并把库不可用作为 warning 显形。
            return Run("catalog", true, "数据库不可达，仅返回代码目录（" + connError + "）", new Dictionary<string, object?>
            {
                ["monsters"] = MonsterTemplateCatalog.RegisteredTemplates(),
                ["dbReachable"] = false,
            }, new List<string> { "数据库不可达：" + connError });
        }

        var templates = store!.PublishedTemplates();
        var data = new Dictionary<string, object?>
        {
            ["monsters"] = templates.Select(t => new
            {
                t.Id, t.Name, Rank = (int)t.Rank, t.LevelBase, t.LevelMin, t.LevelMax, t.WuxingDomain, t.ModelKey,
            }).ToList(),
            ["codeMonsters"] = MonsterTemplateCatalog.RegisteredTemplates(),
            ["skills"] = store.SkillIds().OrderBy(id => id).Take(400).ToList(),
            ["skillCount"] = store.SkillIds().Count,
            ["items"] = store.ItemIds().OrderBy(id => id).Take(600).ToList(),
            ["itemCount"] = store.ItemIds().Count,
            ["lootTables"] = Enumerable.Range(1, 5).Concat(Enumerable.Range(100, 6)).ToList(),
            ["segments"] = new Dictionary<string, object?>
            {
                ["normal"] = new { from = 1, to = 99999 },
                ["elite"] = new { from = 100000, to = 199999 },
                ["worldBoss"] = new { from = 200000, to = 299999 },
                ["dungeon"] = new { from = 300000, to = int.MaxValue },
            },
            ["aiProfiles"] = new[] { "0=笨(全关)", "1=战(威胁表)", "2=精(威胁+技能+走图+求援)", "3=王(全开+战术)" },
            ["maxLevel"] = MonsterValidator.MaxLevel,
            ["maxAliveLimit"] = MonsterValidator.MaxAliveLimit,
            ["maxRoutePoints"] = MonsterValidator.MaxRoutePoints,
            ["dbReachable"] = true,
        };
        return Run("catalog", true, "目录已返回", data);
    }

    private static int RunList()
    {
        if (!TryOpenStore(out var store, out var error)) return Run("list", false, error, null);
        var rows = store!.List();
        return Run("list", true, $"共 {rows.Count} 只怪（库 {store!.DatabaseName}）",
            new Dictionary<string, object?> { ["rows"] = rows, ["count"] = rows.Count, ["database"] = store!.DatabaseName });
    }

    private static int RunGet(string[] rest)
    {
        if (!TryParseInt(rest, out var id)) return Run("get", false, "用法：get <monsterId>", null);
        if (!TryOpenStore(out var store, out var error)) return Run("get", false, error, null);
        var draft = store!.FindDraft(id);
        return draft == null
            ? Run("get", false, $"怪物 {id} 不存在", null)
            : Run("get", true, $"怪物 {id} 已载入（v{draft.ContentVersion}）", new Dictionary<string, object?> { ["draft"] = draft });
    }

    private static int RunValidate(string[] rest)
    {
        if (!ReadDraft(rest, out var draft, out var readError)) return Run("validate", false, readError, null);
        TryOpenStore(out var store, out var connError);
        var refs = (IMonsterReferenceIndex?)store;
        var result = MonsterValidator.Validate(draft, refs);
        var data = new Dictionary<string, object?>
        {
            ["errors"] = result.Errors,
            ["errorPaths"] = result.ErrorPaths,
            ["warnings"] = result.Warnings,
            ["skippedChecks"] = refs == null ? 5 : 0,
        };
        return Run("validate", result.Ok, result.Ok ? "校验通过" : $"{result.Errors.Count} 条错误", data, result.Warnings);
    }

    private static int RunSave(string[] rest)
    {
        if (!ReadDraft(rest, out var draft, out var readError)) return Run("save", false, readError, null);
        if (!TryOpenStore(out var store, out var error)) return Run("save", false, error, null);

        var validation = MonsterValidator.Validate(draft!, store!);
        if (!validation.Ok)
        {
            return Run("save", false, $"校验未过，未写入（{validation.Errors.Count} 条错误）",
                new Dictionary<string, object?> { ["errors"] = validation.Errors, ["errorPaths"] = validation.ErrorPaths },
                validation.Warnings);
        }

        var result = store!.Upsert(draft!);
        var data = new Dictionary<string, object?> { ["writtenRows"] = result.WrittenRows, ["contentVersion"] = result.ContentVersion };
        return Run("save", result.Ok, result.Message, data, validation.Warnings.Concat(result.Warnings).ToList());
    }

    private static int RunSetState(string[] rest, int state)
    {
        if (!TryParseInt(rest, out var id)) return Run("publish", false, "用法：publish <monsterId>", null);
        if (!TryOpenStore(out var store, out var error)) return Run("publish", false, error, null);

        // 上线 = 校验 + 写库；校验不过就大声拒绝，别让"发布成功"与"内容有问题"同时成立。
        var draft = store!.FindDraft(id);
        if (draft == null) return Run("publish", false, $"怪物 {id} 不存在", null);
        var validation = MonsterValidator.Validate(draft, store);
        if (!validation.Ok)
            return Run("publish", false, $"校验未过，拒绝发布（{validation.Errors.Count} 条错误）",
                new Dictionary<string, object?> { ["errors"] = validation.Errors }, validation.Warnings);

        var result = store.SetPublishState(id, state);
        return Run("publish", result.Ok, result.Message,
            new Dictionary<string, object?> { ["contentVersion"] = result.ContentVersion }, validation.Warnings);
    }

    private static int RunRetire(string[] rest)
    {
        if (!TryParseInt(rest, out var id)) return Run("retire", false, "用法：retire <monsterId>", null);
        if (!TryOpenStore(out var store, out var error)) return Run("retire", false, error, null);
        var result = store!.Retire(id);
        return Run("retire", result.Ok, result.Message);
    }

    private static int RunDelete(string[] rest)
    {
        if (!TryParseInt(rest, out var id)) return Run("delete", false, "用法：delete <monsterId>（只允许删已作废的）", null);
        if (!TryOpenStore(out var store, out var error)) return Run("delete", false, error, null);
        var result = store!.Delete(id);
        return Run("delete", result.Ok, result.Message,
            result.Ok ? new Dictionary<string, object?> { ["monsterId"] = id } : null);
    }

    private static int RunDraft(string[] rest)
    {
        var sub = rest.Length > 0 ? rest[0].ToLowerInvariant() : "list";
        var tail = rest.Skip(1).ToArray();

        if (sub == "list")
        {
            if (!TryOpenDrafts(out var drafts, out var error)) return Run("draft list", false, error, null);
            var rows = drafts!.List();
            return Run("draft list", true, $"共 {rows.Count} 份草稿",
                new Dictionary<string, object?> { ["drafts"] = rows, ["ttlHours"] = (int)MonsterDraftStore.DraftTtl.TotalHours });
        }

        if (sub == "get")
        {
            if (!TryParseInt(tail, out var getId)) return Run("draft get", false, "用法：draft get <monsterId>", null);
            if (!TryOpenDrafts(out var drafts, out var error)) return Run("draft get", false, error, null);
            var raw = drafts!.GetRaw(getId);
            if (string.IsNullOrEmpty(raw)) return Run("draft get", false, $"怪物 {getId} 没有草稿", null);
            return Run("draft get", true, "草稿已取回", new Dictionary<string, object?> { ["json"] = raw });
        }

        if (sub == "delete")
        {
            if (!TryParseInt(tail, out var delId)) return Run("draft delete", false, "用法：draft delete <monsterId>", null);
            if (!TryOpenDrafts(out var drafts, out var error)) return Run("draft delete", false, error, null);
            var removed = drafts!.Delete(delId);
            return Run("draft delete", true, removed ? $"草稿 {delId} 已删" : $"草稿 {delId} 本来就不存在");
        }

        if (sub == "save")
        {
            if (!ReadDraft(tail, out var draft, out var readError)) return Run("draft save", false, readError, null);
            if (!TryOpenDrafts(out var drafts, out var error)) return Run("draft save", false, error, null);
            // 草稿也校验，但**只挡错误不挡警告**：草稿的存在意义就是"半成品可以先放着"。
            TryOpenStore(out var store, out _);
            var validation = MonsterValidator.Validate(draft, store);
            if (!validation.Ok)
                return Run("draft save", false, $"草稿有 {validation.Errors.Count} 条硬错误，未存（警告不拦）",
                    new Dictionary<string, object?> { ["errors"] = validation.Errors }, validation.Warnings);
            drafts!.Save(draft!);
            return Run("draft save", true, $"草稿已存（TTL {MonsterDraftStore.DraftTtl.TotalHours:0} 小时，每次保存续期）",
                new Dictionary<string, object?> { ["monsterId"] = draft!.MonsterId }, validation.Warnings);
        }

        return Run("draft", false, "未知子动词：" + sub + "（save | list | get | delete）", null);
    }

    /// <summary>
    /// 经验试算：把 <see cref="MonsterXpCore"/> 的结果原样回给编辑器。
    /// <para>插件里另有一份同公式的镜像用于即时画曲线；这条动词是**对拍**用的参照——
    /// 两份实现一致才允许把曲线当证据（等价测试的参照必须另写，两边同错时互比是绿的）。</para>
    /// </summary>
    private static int RunPreviewXp(string[] rest)
    {
        if (!TryGetOption(rest, "mob", out var mobLevel)) return Run("preview-xp", false, "用法：preview-xp --mob <等级> --player <等级> [--rank 0..4] [--xpBase N --xpPer N] [--xpModBps N --healthModBps N] [--sweep]", null);
        if (!TryGetOption(rest, "player", out var playerLevel)) return Run("preview-xp", false, "缺少 --player", null);
        TryGetOption(rest, "rank", out var rank);
        var sweep = rest.Any(a => string.Equals(a, "--sweep", StringComparison.OrdinalIgnoreCase));

        var xpBase = 45;
        var xpPer = 5;
        // 试算默认用旧世界系数；要按某只已入库的怪算，请用 --mob 后接真实等级并读 get 的结果。
        if (TryGetOption(rest, "xpBase", out var customBase) && customBase > 0) xpBase = customBase;
        if (TryGetOption(rest, "xpPer", out var customPer) && customPer >= 0) xpPer = customPer;
        // 逐模板两个倍率也要能对拍：编辑器把它们送到命令行来，服务端与本地镜像才算同一条曲线。
        // 缺省 10000 = 不开，与加这两格之前逐字相同。
        var xpMod = 10000;
        var healthMod = 10000;
        if (TryGetOption(rest, "xpModBps", out var customXpMod) && customXpMod >= 0) xpMod = customXpMod;
        if (TryGetOption(rest, "healthModBps", out var customHealthMod) && customHealthMod >= 0) healthMod = customHealthMod;

        var cfg = new MonsterXpConfig(xpBase, xpPer, MonsterXpConfig.DefaultRankScale(), xpMod, healthMod);
        var single = MonsterXpCore.Decompose(cfg, mobLevel, playerLevel, (MonsterRank)Math.Clamp(rank, 0, 4));
        var data = new Dictionary<string, object?>
        {
            ["monsterLevel"] = mobLevel,
            ["playerLevel"] = playerLevel,
            ["rank"] = rank,
            ["difficulty"] = single.Difficulty.ToString(),
            ["base"] = single.Base,
            ["concession"] = Math.Round(single.Concession, 4),
            ["rankScale"] = single.RankScale,
            // 两个逐模板倍率的**生效值**要回显：healthMod 回 1.0 有两种含义（没配 / 落在不生效那一段），
            // 把原始基点一起带回去，编辑器与人才分得开这两种。
            ["xpModBps"] = xpMod,
            ["healthModBps"] = healthMod,
            ["xpMod"] = Math.Round(single.XpMod, 4),
            ["healthMod"] = Math.Round(single.HealthMod, 4),
            ["final"] = single.Final,
            ["grayLevel"] = MonsterDifficultyCore.GrayLevel(playerLevel),
            ["zd"] = MonsterDifficultyCore.ZeroDifferenceValue(playerLevel),
            ["stage"] = MonsterDifficultyCore.StageIndexOf(playerLevel),
            ["maxParticipants"] = MonsterXpCore.MaxParticipants,
        };

        if (sweep)
        {
            // 曲线扫描：编辑器画"随等级差的衰减"用这条，而不是让插件自己推公式。
            var points = new List<Dictionary<string, object?>>(32);
            for (var d = -30; d <= 15; d++)
            {
                var ml = playerLevel + d;
                if (ml < 1) continue;
                var p = MonsterXpCore.Decompose(cfg, ml, playerLevel, (MonsterRank)Math.Clamp(rank, 0, 4));
                points.Add(new Dictionary<string, object?>
                {
                    ["diff"] = d,
                    ["monsterLevel"] = ml,
                    ["concession"] = Math.Round(p.Concession, 4),
                    ["final"] = p.Final,
                    ["difficulty"] = p.Difficulty.ToString(),
                });
            }
            data["curve"] = points;
        }

        return Run("preview-xp", true, $"同级/此等级差应得 {single.Final} 经验（{single.Difficulty}）", data);
    }

    /// <summary>
    /// 掉落期望：对已入库的怪，把绑定与逐件概率算成**解析期望**（不是蒙特卡洛）。
    /// <para>选解析而不是抽 10000 次：抽样的方差会让"3% 改成了 5%"在小样本下看不出来，
    /// 而解析值对每个数字都诚实；随机掷骰的验证属于运行时的金轨迹，不该在这里做。</para>
    /// </summary>
    private static int RunPreviewDrop(string[] rest)
    {
        if (!TryParseInt(rest, out var monsterId)) return Run("preview-drop", false, "用法：preview-drop --mob <id> | preview-drop <id>", null);
        if (!TryOpenStore(out var store, out var error)) return Run("preview-drop", false, error, null);

        var bindings = store!.AllLootBindings().Where(b => b.MonsterId == monsterId).OrderBy(b => b.Id).ToList();
        var bindingIds = bindings.Select(b => b.Id).ToHashSet();
        var entries = store.AllDropEntries().Where(e => bindingIds.Contains(e.BindingId)).ToList();

        // 域推导表号问**库里的草稿**，不问代码目录：编辑器新建的怪不在目录里、CLI 也没装配内容，
        // 问目录会得到 0，于是回执上说"这只怪按推导什么都不掉"而它的域明明填着 3。
        // 公式本身仍住在 MonsterTemplateCatalog（含"什么档位算首领表"那一句），这里只喂输入。
        var domainDraft = store.FindDraft(monsterId);
        var usesBossLootTable = domainDraft != null && MonsterTemplateCatalog.IsBossTableRank(domainDraft.Rank);
        var derivedTableId = domainDraft != null
            ? MonsterTemplateCatalog.DeriveLootTableId(domainDraft.WuxingDomain, usesBossLootTable)
            : MonsterTemplateCatalog.DeriveLootTableId(monsterId);

        // 行 → 绑定 DTO → 运行期快照 → 内核。这一层不做任何掉率算术：
        // 它以前自己抄了一遍"逐件概率相加"，而 group_id>0 的互斥组每次最多出一件，
        // 抄件会把"每次 1 件"的表报成"每次 5 件"——编辑器里那个数字服务端永远达不到，
        // 而库里、编辑器、快照三处的概率全都显示得对，没人会归因到试算这一侧。
        var dtos = new List<Horizon.Game.Core.Sim.Server.MonsterLootBindingSnapshotDto>();
        foreach (var b in bindings)
        {
            var dto = new Horizon.Game.Core.Sim.Server.MonsterLootBindingSnapshotDto
            {
                MonsterTemplateId = monsterId,
                LootTableId = b.LootTableId,
                RollChanceBps = b.RollChanceBps,
                MinCount = b.MinCount,
                MaxCount = b.MaxCount,
                ConditionKind = b.ConditionKind,
                QuestId = b.QuestId,
                MoneyMin = b.MoneyMin,
                MoneyMax = b.MoneyMax,
            };
            foreach (var e in entries.Where(e => e.BindingId == b.Id))
            {
                dto.Entries.Add(new Horizon.Game.Core.Sim.Server.MonsterDropSnapshotDto
                {
                    ItemId = e.ItemId,
                    ChanceBps = e.ChanceBps,
                    CountMin = e.CountMin,
                    CountMax = e.CountMax,
                    Seq = e.Seq,
                    GroupId = e.GroupId,
                    ConditionKind = e.ConditionKind,
                    QuestId = e.QuestId,
                });
            }
            dtos.Add(dto);
        }

        var summary = MonsterDropPreview.Summarize(dtos);
        var groupedTotal = summary.Bindings.Sum(r => r.GroupedEntryCount);
        var rows = summary.Bindings.Select(r => new Dictionary<string, object?>
        {
            ["lootTableId"] = r.LootTableId,
            ["rollChance"] = Math.Round(r.GateChance, 6),
            ["entryCount"] = r.EntryCount,
            // 组数与组内件数：不写出来的话，"这张表 5 件"看起来仍然是"最多 5 件"。
            ["groupCount"] = r.GroupCount,
            ["groupedEntryCount"] = r.GroupedEntryCount,
            // 表级"抽几件"= 互斥组重掷轮数区间；期望轮数是它折算进期望的那一份。
            ["rollMinCount"] = r.MinCount,
            ["rollMaxCount"] = r.MaxCount,
            ["expectedRounds"] = Math.Round(r.ExpectedRounds, 4),
            ["expectedItems"] = Math.Round(r.ExpectedItems, 6),
            ["moneyRange"] = r.MoneyMax > 0 ? $"{r.MoneyMin}-{r.MoneyMax}" : "(不掉金币)",
            ["conditionKind"] = r.ConditionKind,
            // 逐件明细：绑定级条件已被条目级覆盖过一层，只看上面那个 conditionKind 会读错。
            ["entries"] = r.Entries.Select(en => new Dictionary<string, object?>
            {
                ["itemId"] = en.ItemId,
                ["groupId"] = en.GroupId,
                ["conditionKind"] = en.ConditionKind,
                ["questId"] = en.QuestId,
                ["winChance"] = Math.Round(en.WinChanceBps / 10000.0, 6),
                ["expectedCount"] = Math.Round(en.ExpectedCount, 6),
            }).ToList(),
        }).ToList();

        var data = new Dictionary<string, object?>
        {
            ["monsterId"] = monsterId,
            ["bindings"] = rows,
            ["expectedItemsPerKill"] = Math.Round(summary.ExpectedItemsPerKill, 6),
            ["expectedItemsUncapped"] = Math.Round(summary.ExpectedItemsUncapped, 6),
            ["cappedByLimit"] = summary.CappedByLimit,
            ["perItem"] = summary.PerItem.OrderBy(kv => kv.Key).Select(kv => new Dictionary<string, object?>
            {
                ["itemId"] = kv.Key,
                ["expectedCount"] = Math.Round(kv.Value, 6),
                // 这件"会掉"的概率（基点折算成 0..1，已乘各表总闸；多表相加后按上限 1 收）。
                // 组内成员拿到的是轮到它时还剩的份额，不是它自己填的那个数字。
                ["approxChance"] = Math.Round(Math.Min(1.0,
                    (summary.PerItemChanceBps.TryGetValue(kv.Key, out var bps) ? bps : 0.0) / 10000.0), 6),
            }).ToList(),
            ["conditionalEntryCount"] = summary.ConditionalEntryCount,
            ["lootTableIdFromDomain"] = MonsterTemplateCatalog.LootTableIdOf(monsterId),
            // 纯推导号（**不看绑定**）：落库脚本用它当表号，这样号段约定只有一个主人。
            // 上面那个数一旦写进绑定就会把自己读回来，问不出"这只怪的域本来该是哪张表"。
            ["lootTableIdDerived"] = derivedTableId,
            ["lootTableDerivedFrom"] = domainDraft != null ? "库里的草稿" : "代码目录（库里没这份草稿）",
            ["usesBossLootTable"] = usesBossLootTable,
            // 逐条求和与内核整表期望是同一份数据的两条入口；不等＝有条目被跳过或两处式子分叉了。
            ["crossCheck"] = summary.CrossCheckFailed
                ? $"不一致：逐条求和 {summary.EntrySum:0.####} vs 整表 {summary.ExpectedItemsUncapped:0.####}"
                : "一致",
            ["note"] = rows.Count == 0
                ? "未配任何绑定：该怪当前按域推导表号掷落（见 lootTableIdFromDomain）"
                : groupedTotal > 0
                    ? $"已配绑定：其中 {groupedTotal} 件挂在互斥组里，同一组每次击杀最多出一件"
                    : "已配绑定：绑定优先，域推导只在无绑定时兜底",
        };
        var headline = rows.Count == 0
            ? "无绑定，走域推导"
            : $"{rows.Count} 张表，期望每次击杀 {summary.ExpectedItemsPerKill:F3} 件"
              + (summary.CappedByLimit
                  ? $"（候选期望 {summary.ExpectedItemsUncapped:F3} 件，已按一具尸体的 {MonsterLootCore.MaxNormalLootItems} 件上限截断）"
                  : string.Empty);
        return Run("preview-drop", true, headline, data);
    }

    /// <summary>
    /// 导出怪物内容快照（DsHost 的 <c>--monster-content</c> 入口）。
    /// <para>只导 <c>publish_state=1</c> 的行：把草稿导出成"运行时权威"是最难查的一类事故
    /// （表现是测试服上出现了编辑器里没发布的怪，而库里那条确实是草稿）。</para>
    /// </summary>
    private static int RunExportContent(string[] rest)
    {
        string? outPath = null;
        for (var i = 0; i < rest.Length - 1; i++)
        {
            if (string.Equals(rest[i], "--out", StringComparison.OrdinalIgnoreCase)) outPath = rest[i + 1];
        }
        if (string.IsNullOrWhiteSpace(outPath))
            return Run("export-content", false, "用法：export-content --out <路径>", null);
        if (!TryOpenStore(out var store, out var error)) return Run("export-content", false, error, null);

        var snapshot = store!.BuildContentSnapshot();

        // 文本行走旁挂源文件（库里刻意没有文本表，决策 D15：文本是展示物，线上只发文本号）。
        // **两张出口必须读同一个入口**（`MonsterTextsExport.LoadSource`）：
        // 各读一份的话会出现"服务端认得有这一号、客户端查不到文案"的半截状态，
        // 而那与"没配文本"在玩家那边同形（都是沉默）。
        if (!MonsterTextsExport.TryGetTextsOption(rest, out var textsArg, out var textsProblem))
            return Run("export-content", false, textsProblem!, null);
        var textSource = MonsterTextsExport.LoadSource(MonsterTextsExport.ResolveSourcePath(textsArg));
        foreach (var t in textSource.Rows)
        {
            snapshot.Texts.Add(new Horizon.Game.Core.Sim.Server.MonsterTextSnapshotDto
            {
                MonsterTemplateId = t.MonsterId,
                SayId = t.SayId,
                Group = t.Group,
                Seq = t.Seq,
                Text = t.Text,
                Emote = t.Emote,
            });
        }

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(outPath, snapshot.ToJson(), new UTF8Encoding(false));

        // 回执必须数技能挂载：以前它只报模板/刷怪点/路线/掉落，"库里 12 行挂载、包里一行都没有"
        // 这种形状在这行字上读不出来（与切片 39 那条"回执自己会撒谎"同族）。
        var counts = MonsterExportReceipt.Of(snapshot.Templates);

        // 导出侧要按 **DS 装载时的同一套判据**再对账一遍，不能只报库侧那份。
        // 现场：生产库里模板 4 的行为表指向巡逻线 1，而路线 1 没进这份包（只装已发布的行）
        // ⇒ DS 启动日志 `问题=1`，而这份回执 `problems=[]`。两个数不同源时，
        // "编辑器里配了线"与"游戏里那只怪在游荡"永远对不上账，而且没人会去读 DS 那一条。
        _ = snapshot.ToBundle(out var assemblyProblems);
        var problems = new List<string>(snapshot.Problems);
        foreach (var p in textSource.Problems)
        {
            if (!problems.Contains(p)) problems.Add(p);
        }
        var assemblyOnly = 0;
        foreach (var p in assemblyProblems)
        {
            if (problems.Contains(p)) continue;
            problems.Add(p);
            assemblyOnly++;
        }

        return Run("export-content", true,
            string.Format("已导出 rev={0}：模板 {1}、刷怪点 {2}、路线 {3}、掉落绑定 {4}、技能挂载 {5} 行（其中 {6} 只怪的技能位是开的）、文本 {7} 条{8}",
                snapshot.ContentRevision, snapshot.Templates.Count, snapshot.SpawnPoints.Count,
                snapshot.Routes.Count, snapshot.LootBindings.Count, counts.SkillMounts, counts.SkillCapableTemplates,
                snapshot.Texts.Count,
                assemblyOnly > 0 ? string.Format("，另有 {0} 条是**包内引用对账**才看得出的（DS 装载时会念同一条）", assemblyOnly) : string.Empty),
            new Dictionary<string, object?>
            {
                ["path"] = System.IO.Path.GetFullPath(outPath),
                ["database"] = store!.DatabaseName,
                ["templates"] = snapshot.Templates.Count,
                ["spawnPoints"] = snapshot.SpawnPoints.Count,
                ["routes"] = snapshot.Routes.Count,
                ["lootBindings"] = snapshot.LootBindings.Count,
                ["skillMounts"] = counts.SkillMounts,
                ["skillCapableTemplates"] = counts.SkillCapableTemplates,
                ["texts"] = snapshot.Texts.Count,
                ["textSource"] = textSource.Path,
                ["textSourceMissing"] = textSource.FileMissing,
                ["assemblyProblems"] = assemblyProblems.Count,
                ["problems"] = problems,
            });
    }

    /// <summary>
    /// 导出**客户端外观表**（<c>HundunWorld/Data/Monsters/client_visuals.json</c>）。
    /// <para>
    /// 为什么单独一张表而不是把 prefab GUID 直接下发：GUID 是**资产身份**，跨机器/跨打包不稳定，
    /// 而客户端按路径解析 GUID 是这工程已验证过的路子（见 <c>FlaxActorSyncSystem</c> 的
    /// <c>TryGetAssetGuidFromPath</c>）。所以线上只下发 <c>model_key</c>，
    /// "这个键对应哪个 prefab 文件"随客户端包发布。
    /// </para>
    /// <para>编辑器里填了 <c>prefab_guid</c> 而库里没有对应路径的行会被记进 problems ——
    /// 那正是"配了外观但客户端永远看不到"的那种漏配，必须显形。</para>
    /// </summary>
    /// <summary>
    /// <c>--texts</c> 的解析已经收进 <see cref="MonsterTextsExport.TryGetTextsOption"/>（单一主人）：
    /// 这里以前只认 <c>--texts &lt;路径&gt;</c> 的空格写法，<c>--texts=…</c> 会被静默忽略并落回默认源文件——
    /// 实测把两条测试台词写进了真正的 <c>monster_texts.json</c>。两种写法与"给了却没带值"的判定现在只有一处。
    /// </summary>

    /// <summary>
    /// 导出**客户端文本表**（<c>HundunWorld/Data/Monsters/client_texts.json</c>）。
    /// <para>与 <c>export-visuals</c> 同一族：线上只发号（这里是 <c>sayId</c>），
    /// "这一号对应哪句文案"随客户端包发布，所以两张出口必须读同一个源文件
    /// （<see cref="MonsterTextsExport.LoadSource"/>）——否则服务端认得有这一号而客户端查不到文案，
    /// 表现与"没配文本"同形（都是沉默）。</para>
    /// </summary>
    private static int RunExportTexts(string[] rest)
    {
        // 缺省位置**不当作"显式给的路径"传下去**（与 SourcePathArg 同一口径）：
        // 那样一来"缺省"会走"显式相对串按当前目录算"那一支，正是面板那条坏路。
        string? outPath = null;
        for (var i = 0; i < rest.Length - 1; i++)
        {
            if (string.Equals(rest[i], "--out", StringComparison.OrdinalIgnoreCase)) outPath = rest[i + 1];
        }
        // 客户端表也要走同一个"定绝对路径"的出口：手敲时当前目录是仓库根，
        // 而面板起的子进程工作目录是可执行文件所在处——不在这里定一次，
        // 表会被写到 bin 底下，而游戏读的还是旧的那份（与文本源同一形状）。
        outPath = MonsterTextsExport.ResolveClientTablePath(outPath);
        if (!TryOpenStore(out var store, out var error)) return Run("export-texts", false, error, null);

        if (!MonsterTextsExport.TryGetTextsOption(rest, out var textsArg, out var textsProblem))
            return Run("export-texts", false, textsProblem!, null);
        var sourcePath = MonsterTextsExport.ResolveSourcePath(textsArg);
        var source = MonsterTextsExport.LoadSource(sourcePath);

        var modelKeys = new Dictionary<int, string>();
        foreach (var t in store!.PublishedTemplates())
        {
            modelKeys[t.Id] = t.ModelKey ?? string.Empty;
        }
        var built = MonsterTextsExport.Build(source.Rows, modelKeys);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(outPath, built.Json, new UTF8Encoding(false));

        return Run("export-texts", source.Ok,
            string.Format("文本表已导出：{0} 条（源 {1} 行、{2} 条问题；{3}）",
                built.Count, source.Rows.Count, built.Problems.Count,
                source.FileMissing ? "源文件不存在，视为还没配文本" : "源文件 " + sourcePath),
            new Dictionary<string, object?>
            {
                ["path"] = System.IO.Path.GetFullPath(outPath),
                ["database"] = store!.DatabaseName,
                ["source"] = sourcePath,
                ["sourceRows"] = source.Rows.Count,
                ["count"] = built.Count,
                ["duplicateSayIds"] = built.DuplicateSayIds,
                ["sourceProblems"] = source.Problems,
                ["problems"] = built.Problems,
            });
    }

    private static int RunSetText(string[] rest)
    {
        if (!TryGetOption(rest, "monster", out int monsterId))
            return Run("set-text", false, "用法：set-text --monster=<编号> --say=<文本号> --text=<文案> [--emote=<动作>] [--texts <源文件>]（缺 --monster 或 --say 都不写）", null);
        if (!TryGetOption(rest, "say", out int sayId))
            return Run("set-text", false, "set-text 需要 --say=<文本号>：文本挂在号上，行为表那一格指的就是它", null);
        // 这一格**必须显式给出**：漏写 --text 与 --text=（清空）是两件事，
        // 把漏写当成清空等于让一次手滑删掉一句台词。
        if (!TryGetTextOption(rest, "text", out var text))
            return Run("set-text", false, "set-text 需要 --text=<文案>（要清空请把值写空：--text=；要走标准输入：--text=-）", null);
        // `--text=-` 从标准输入读整段文案（与 validate/save/bake-nav 的 `-` 同一口径）。
        // 面板一律走这条路：Windows 的 argv 里一句带引号或换行的台词要自己拼转义，
        // 拼错一个字符的表现是"写进去的是半句"或"起不了进程"，而管道没有这个问题。
        if (string.Equals(text, "-", StringComparison.Ordinal))
        {
            text = _stdin.ReadToEnd();
            if (string.IsNullOrWhiteSpace(text))
                return Run("set-text", false,
                    "--text=- 但标准输入是空的：拒绝把空白当成清空（那会删掉这一号文案）。" +
                    "确实要删就显式写 --text=", null);
        }
        if (!TryGetOption(rest, "emote", out int emote)) emote = 0;
        // `--group`：AC creature_text 的 group（同一事件组里多句，运行期按 tick 盐挑一句）。
        // 不给 = 沿用这一号原来的组（只改文案不该把它悄悄移出组）；给了 0 = 显式移出组。
        int? group = null;
        if (rest.Any(a => a.StartsWith("--group", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryGetOption(rest, "group", out int parsedGroup))
                return Run("set-text", false, "set-text 的 --group 要带值（0 = 不成组）", null);
            if (parsedGroup < 0)
                return Run("set-text", false, "set-text 的 --group 不得为负（0 = 不成组）", null);
            group = parsedGroup;
        }

        if (!MonsterTextsExport.TryGetTextsOption(rest, out var textsArg, out var textsProblem))
            return Run("set-text", false, textsProblem!, null);
        var sourcePath = MonsterTextsExport.ResolveSourcePath(textsArg);
        var w = MonsterTextsExport.UpsertText(sourcePath, monsterId, sayId, text, emote, group);
        // 失败那份要单独说：Action 在拒绝时是空的，套进正常模板会念出"文本 ：…"这种读不出名的回执。
        var message = w.Ok
            ? string.Format("文本 {0}：怪物 {1} 第 {2} 号 → {3}（组 {4}{5}；源里现有 {6} 行；{7}）",
                w.Action, monsterId, sayId,
                string.IsNullOrWhiteSpace(text) ? "（空，撤销这一号）" : text.Trim(),
                w.Group,
                w.Group > 0 ? "，同组多句按 tick 挑一句" : "=不成组",
                w.RowsAfter,
                w.Problems.Count > 0 ? string.Join("；", w.Problems) : "无问题")
            : string.Format("文本没写进去：{0}（源文件 {1} 未改动）",
                string.Join("；", w.Problems), sourcePath);
        return Run("set-text", w.Ok, message,
            new Dictionary<string, object?>
            {
                ["path"] = sourcePath,
                ["action"] = w.Action,
                ["monsterId"] = monsterId,
                ["sayId"] = sayId,
                ["group"] = w.Group,
                ["rowsAfter"] = w.RowsAfter,
                ["problems"] = w.Problems,
            });
    }

    /// <summary>
    /// 读文本源里某只怪的行（面板用它把"编号 → 文案"回填到格子里）。
    /// <para>不给 <c>--monster</c> 就返回全部：面板只查一只怪，而脚本要的是整份对账。</para>
    /// </summary>
    private static int RunGetTexts(string[] rest)
    {
        var hasMonster = TryGetOption(rest, "monster", out int monsterId);
        if (!MonsterTextsExport.TryGetTextsOption(rest, out var textsArg, out var textsProblem))
            return Run("get-texts", false, textsProblem!, null);
        var sourcePath = MonsterTextsExport.ResolveSourcePath(textsArg);
        var source = MonsterTextsExport.LoadSource(sourcePath);
        var rows = source.Rows.Where(r => !hasMonster || r.MonsterId == monsterId)
                              .Select(r => (object?)new Dictionary<string, object?>
                              {
                                  ["monsterId"] = r.MonsterId,
                                  ["sayId"] = r.SayId,
                                  ["group"] = r.Group,
                                  ["seq"] = r.Seq,
                                  ["text"] = r.Text,
                                  ["emote"] = r.Emote,
                              }).ToList();
        return Run("get-texts", source.Ok,
            string.Format("文本源 {0} 读到 {1} 行（本次返回 {2} 行、{3} 条问题；{4}）",
                sourcePath, source.Rows.Count, rows.Count, source.Problems.Count,
                source.FileMissing ? "源文件不存在，视为还没配文本" : "源文件在盘上"),
            new Dictionary<string, object?>
            {
                ["path"] = sourcePath,
                ["sourceRows"] = source.Rows.Count,
                ["returned"] = rows.Count,
                ["fileMissing"] = source.FileMissing,
                ["rows"] = rows,
                ["problems"] = source.Problems,
            });
    }

    private static int RunExportVisuals(string[] rest)
    {
        string? outPath = null;
        for (var i = 0; i < rest.Length - 1; i++)
        {
            if (string.Equals(rest[i], "--out", StringComparison.OrdinalIgnoreCase)) outPath = rest[i + 1];
        }
        if (string.IsNullOrWhiteSpace(outPath))
            return Run("export-visuals", false, "用法：export-visuals --out <路径>（这张表没有缺省位置：必须给 --out，" +
                   "否则不知道要落在哪个工作目录下）", null);
        if (!TryOpenStore(out var store, out var error)) return Run("export-visuals", false, error, null);

        // 内容树根：用来问一句"这条 prefab 路径在盘上真的有吗"。找不到就**大声说没查**——
        // 静默跳过与"查过、没问题"在回执里长得一样，而这一格要防的恰恰是"看起来没问题"。
        var contentRoot = FindContentRoot(rest);
        Func<string, bool>? prefabExists = contentRoot == null
            ? null
            : p =>
            {
                // 只按"根 + 去掉 Content/ 前缀的那段"这一种拼法查。
                // 以前顺手加过"再按文件名找一次"的兜底：那会把**另一个目录里的同名资产**判成命中，
                // 于是这条检查在最该报警的时候反而说"没问题"。
                var rel = p.StartsWith("Content/", StringComparison.OrdinalIgnoreCase) ? p.Substring("Content/".Length) : p;
                return System.IO.File.Exists(System.IO.Path.Combine(contentRoot, rel));
            };

        // 判据（哪些行真的能进表）住在 MonsterVisualsExport：那部分不需要连着库也能被测试断言。
        var built = MonsterVisualsExport.Build(store!.PublishedTemplates().Select(t => new MonsterVisualRow(
            t.Id, t.Name, t.ModelKey, t.PrefabGuid, t.PrefabPath, (double)t.ModelScale, t.StandState)), prefabExists);
        if (contentRoot == null)
            built.Problems.Add("prefab 路径的存在性**没查**：未能定位内容树根（用 --content-root <HundunWorld/Content> 指定）。" +
                               "这一格防的是\"配了个不存在的路径 ⇒ 客户端静默回退占位\"，没查就等于没防");

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(outPath, built.Json, new UTF8Encoding(false));

        return Run("export-visuals", true,
            string.Format("外观表已导出：{0} 条（{1} 条问题，其中 {2} 条路径在盘上查不到）",
                          built.Count, built.Problems.Count, built.MissingPrefabPaths),
            new Dictionary<string, object?>
            {
                ["path"] = System.IO.Path.GetFullPath(outPath),
                ["database"] = store!.DatabaseName,
                ["count"] = built.Count,
                ["missingPrefabPaths"] = built.MissingPrefabPaths,
                ["contentRoot"] = contentRoot,
                ["problems"] = built.Problems,
            });
    }

    /// <summary>
    /// 烘焙服务端导航网格：把编辑器（或任何采样器）逐格判出来的"能不能走"掩码装配成 <c>MONSTERNAV</c> 文件。
    /// <para>
    /// <b>为什么装配放在 CLI 而不是插件里</b>：文件格式的读侧是 <c>MonsterNavGridLoader</c>，
    /// 写侧若散在 Flax 脚本域里各拼一份字符串，漂掉一个空格只会表现为"文件写得出来、加载安静失败"。
    /// 插件只负责问引擎"这一点能走吗"，位图到这里统一装配（判据在 <c>MonsterNavBakeCore</c>，可离线断言）。
    /// </para>
    /// </summary>
    private static int RunBakeNav(string[] rest)
    {
        string outPath = null;
        string maskArg = null;
        var width = 0;
        var height = 0;
        var cell = 0f;
        var originX = 0f;
        var originY = 0f;

        const string usage = "用法：bake-nav --out <路径> --width N --height M --cell 米 --origin X Y --mask -（掩码走标准输入）或 --mask \"1010…\"";
        for (var i = 0; i < rest.Length; i++)
        {
            var key = rest[i].ToLowerInvariant();
            switch (key)
            {
                case "--out": if (i + 1 < rest.Length) outPath = rest[++i]; break;
                case "--mask": if (i + 1 < rest.Length) maskArg = rest[++i]; break;
                case "--width": if (i + 1 < rest.Length) int.TryParse(rest[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out width); break;
                case "--height": if (i + 1 < rest.Length) int.TryParse(rest[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out height); break;
                case "--cell": if (i + 1 < rest.Length) float.TryParse(rest[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out cell); break;
                case "--origin":
                    if (i + 2 < rest.Length)
                    {
                        float.TryParse(rest[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out originX);
                        float.TryParse(rest[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out originY);
                    }
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(outPath) || string.IsNullOrEmpty(maskArg))
            return Run("bake-nav", false, usage, null);

        // "-" 表示掩码从标准输入读：一整张 256×256 的位图是 6 万多字符，
        // 走命令行参数会被 Windows 的 32K 命令行长度上限和引号转义一起咬到。
        var mask = string.Equals(maskArg, "-", StringComparison.Ordinal) ? _stdin.ReadToEnd() : maskArg;

        var built = MonsterNavBakeCore.FromMask(width, height, cell, originX, originY, mask);
        if (built.TotalCells == 0 || string.IsNullOrEmpty(built.Text))
            return Run("bake-nav", false, "装配失败：" + string.Join("；", built.Problems), new Dictionary<string, object?>
            {
                ["problems"] = built.Problems,
            });

        // 全阻塞几乎必然是采样通路坏了（没有 NavMesh / 换轴反了 / 探针高度不对）。
        // 这种文件写下去只会让所有怪都退回直线移动，而"直线移动能穿墙"比"不走"更糟，所以拒写。
        if (built.WalkableCells == 0)
            return Run("bake-nav", false, "拒写：整张网格全阻塞（" + width + "×" + height + "），" +
                       "先确认场景里有没有 NavMesh、探针高度与换轴方向", new Dictionary<string, object?>
            {
                ["problems"] = built.Problems,
                ["walkable"] = 0,
                ["total"] = built.TotalCells,
            });

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(outPath, built.Text, new UTF8Encoding(false));

        return Run("bake-nav", true,
            string.Format("导航网格已写出：{0}×{1}，可走 {2} 格（{3:P1}），cell={4}m",
                built.Width, built.Height, built.WalkableCells, built.WalkableRatio, cell),
            new Dictionary<string, object?>
            {
                ["path"] = System.IO.Path.GetFullPath(outPath),
                ["width"] = built.Width,
                ["height"] = built.Height,
                ["cellSize"] = cell,
                ["walkable"] = built.WalkableCells,
                ["total"] = built.TotalCells,
                ["problems"] = built.Problems,
            });
    }

    private static int RunSpawn(string[] rest)
    {
        var sub = rest.Length > 0 ? rest[0].ToLowerInvariant() : "list";
        if (sub != "list") return Run("spawn", false, "未知子动词：" + sub + "（list）", null);
        if (!TryOpenStore(out var store, out var error)) return Run("spawn list", false, error, null);

        int? filter = null;
        if (rest.Length >= 3 && string.Equals(rest[1], "--mob", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(rest[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mob))
        {
            filter = mob;
        }

        var rows = store!.PublishedSpawnPoints()
            .Where(sp => !filter.HasValue || sp.MonsterId == filter.Value)
            .Select(sp => new
            {
                sp.Id, sp.MonsterId, sp.ZoneId, sp.ShardId, sp.X, sp.Y, sp.Z, sp.Yaw,
                sp.LevelOverride, sp.MaxAlive, sp.SlotSpacing, sp.RespawnOverrideSeconds, sp.RouteOverrideId,
            }).ToList();
        return Run("spawn list", true, $"共 {rows.Count} 个启用中的刷怪点",
            new Dictionary<string, object?> { ["rows"] = rows, ["count"] = rows.Count });
    }

    // ---------- 公共 ----------

    /// <summary>
    /// 定位内容树根（<c>.../HundunWorld/Content</c>）：先认 <c>--content-root</c>，
    /// 否则从当前目录逐级往上找。<b>找不到就返回 null</b>——调用方必须把"没查"报成一条问题，
    /// 因为"探针没跑"与"探针跑过且全对"在回执里长得一样。
    /// </summary>
    private static string? FindContentRoot(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--content-root", StringComparison.OrdinalIgnoreCase)) continue;
            var given = args[i + 1];
            return System.IO.Directory.Exists(given) ? System.IO.Path.GetFullPath(given) : null;
        }

        try
        {
            var dir = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
            for (var hop = 0; dir != null && hop < 8; hop++, dir = dir.Parent)
            {
                // 仓库根下是 HundunWorld/Content；从 Content 里跑时则当前目录本身就是根。
                var candidate = System.IO.Path.Combine(dir.FullName, "HundunWorld", "Content");
                if (System.IO.Directory.Exists(candidate)) return candidate;
                if (string.Equals(dir.Name, "Content", StringComparison.OrdinalIgnoreCase)) return dir.FullName;
            }
        }
        catch
        {
            // 路径权限/重解析失败都只意味着"没定位到根"，交给调用方去念"这条检查没跑"。
        }
        return null;
    }

    /// <summary>
    /// 内容普查：把库里**每一只已发布的怪**都过一遍编辑器自己用的那份校验器，
    /// 并把"机制就绪、内容为零"那几个读数单独列出来。
    /// <para>
    /// <b>为什么要有这条</b>：019/020 的种子是 SQL 直接写进库的，<b>绕过了校验器</b>，
    /// 于是"挂了技能但档位把技能位关着""开了技能位却一个技能没挂"这两句 warning
    /// 从来没有响过的机会。实测（2026-10-27）：11 只种子怪 <c>MonsterSkillMount</c> 零行，
    /// 而行为表 11 行 <c>ai_profile</c> 全 = 0（Dumb ⇒ UseSkills/UseNavigation/UseAssist 一起关），
    /// 所以施法条、求援、导航追击这些内核与格子<b>在真游戏里一条都不会触发</b>——
    /// 而全仓每一道代码闸门都是绿的。缺的就是"把内容本身也量一遍"的这一条。
    /// </para>
    /// <para>
    /// <b>判据要防的是"0 条发现被读成一切正常"</b>：所以先把"看了几只"报出来，
    /// 一只都没看到就直接判失败（那说明普查本身没跑起来，而不是内容很干净）。
    /// </para>
    /// </summary>
    private static int RunAudit()
    {
        if (!TryOpenStore(out var store, out var error)) return Run("audit", false, error, null);

        var templates = store!.PublishedTemplates();
        var findings = new List<string>();
        var invalid = 0;
        var rows = new List<MonsterAuditRow>();
        foreach (var t in templates)
        {
            var draft = store.FindDraft(t.Id);
            if (draft == null)
            {
                invalid++;
                findings.Add($"怪物 {t.Id}（{t.Name}）：已发布，但草稿读不出来（表间断链）");
                continue;
            }

            var validation = MonsterValidator.Validate(draft, store);
            if (!validation.Ok)
            {
                invalid++;
                foreach (var e in validation.Errors) findings.Add($"怪物 {t.Id}（{t.Name}）：{e}");
            }
            foreach (var w in validation.Warnings) findings.Add($"怪物 {t.Id}（{t.Name}）：{w}");

            var b = draft.Behavior;
            // 能力位走**同一个主人**（档位 + 逐位覆盖）：在这里再抄一遍"profile 2 就是会施法"的表，
            // 就等于再造一份会与内核静默分叉的清单。
            var flags = MonsterAiFlags.Resolve(
                (MonsterAiProfile)Math.Clamp(b.AiProfile, 0, 3),
                b.FlagUseThreat, b.FlagUseSkills, b.FlagUseNavMesh, b.FlagUseAssist, b.FlagUseTactics);
            rows.Add(new MonsterAuditRow(t.Id, t.Name, draft.Skills.Count,
                flags.UseSkills, flags.UseNavigation, flags.UseAssist,
                draft.LootBindings.Count,
                draft.LootBindings.Sum(l => l.Entries.Count),
                draft.LootBindings.Any(l => l.MoneyMin > 0 || l.MoneyMax > 0),
                b.PatrolRouteId > 0 || draft.SpawnPoints.Any(s => s.RouteOverrideId > 0),
                draft.CorpseSeconds > 0,
                draft.AggroSayId > 0 || draft.Skills.Any(s => s.StartEmote > 0),
                (float)draft.LootWindowSeconds,
                // 普通件候选数（逐件条件按"继承所属绑定"解析后数）：一具尸体只放得下 18 件，超出部分运行期丢弃。
                draft.LootBindings.Sum(l => l.Entries.Count(e =>
                    MonsterLootCore.ResolveConditionKind(e.ConditionKind, l.ConditionKind) == MonsterLootCore.ConditionNone)),
                // 互斥组条目数：这类条目才是"同组一次只出一件"那种配法。
                draft.LootBindings.Sum(l => l.Entries.Count(e => e.GroupId > 0)),
                // 按条件出账的条目数（条件不是"无条件"的都算）。
                draft.LootBindings.Sum(l => l.Entries.Count(e =>
                    MonsterLootCore.ResolveConditionKind(e.ConditionKind, l.ConditionKind) != MonsterLootCore.ConditionNone)),
                // 形状读数用：AI 档位与移动方式（它们决定"档位表/四档移动"在内容里被验证了几档）。
                b.AiProfile,
                (int)b.FollowType));
        }

        // "内容为零"那几句的判据住在 MonsterAuditCensus：那部分不需要连着库也能被测试断言。
        var (orphanRows, npcTableReadable) = store.OrphanChildRows();
        if (!npcTableReadable)
            findings.Add("NPC 模板表读不到（这个库里没建 021/022？）⇒ 孤儿的键只能并上怪物表，" +
                         "这一半判据**降级**：挂在敌对 NPC（如 500001）上的合法内容会被误报成孤儿，" +
                         "而真正的孤儿反而查不全——读数当不得真，先把那张表建起来再信这条");
        var census = MonsterAuditCensus.Build(rows, findings, orphanRows);

        // 一只都没普查到就不算通过：空结果与"查过、没问题"必须长得不一样（否则这条闸门会在
        // 最该报警的时候报绿——库被清空、发布状态写错、连接串指错库，读起来都一样）。
        return Run("audit", invalid == 0 && census.Published > 0,
            $"普查 {census.Published} 只已发布怪：校验不过 {invalid} 只、发现 {census.Findings.Count} 条；" +
            $"技能挂载共 {census.SkillMounts} 行（挂了技能的怪 {census.MonstersWithSkills} 只）、会施法 {census.SkillCapable} 只、" +
            $"走导航 {census.NavCapable} 只、会求援 {census.AssistCapable} 只；" +
            $"掉落表 {census.LootBindings} 张／逐件条目 {census.LootEntries} 行（挂了掉落的怪 {census.MonstersWithLoot} 只）、" +
            $"配金币 {census.MoneyCapable} 只、挂巡逻线 {census.RouteCapable} 只、配尸体停留 {census.CorpseCapable} 只、配文本 {census.TextCapable} 只",
            new Dictionary<string, object?>
            {
                ["published"] = census.Published,
                ["database"] = store!.DatabaseName,
                ["invalid"] = invalid,
                ["findings"] = census.Findings.Count,
                ["skillMounts"] = census.SkillMounts,
                ["monstersWithSkills"] = census.MonstersWithSkills,
                ["skillCapable"] = census.SkillCapable,
                ["navCapable"] = census.NavCapable,
                ["assistCapable"] = census.AssistCapable,
                ["lootBindings"] = census.LootBindings,
                ["lootEntries"] = census.LootEntries,
                ["monstersWithLoot"] = census.MonstersWithLoot,
                ["moneyCapable"] = census.MoneyCapable,
                ["routeCapable"] = census.RouteCapable,
                ["corpseCapable"] = census.CorpseCapable,
                ["textCapable"] = census.TextCapable,
                ["orphanRows"] = census.OrphanRows,
                ["lootWindowClosedWithDrops"] = census.LootWindowClosedWithDrops,
                ["lootCandidatesOverCap"] = census.LootCandidatesOverCap,
                ["lootGroupEntries"] = census.LootGroupEntries,
                ["lootConditionalEntries"] = census.LootConditionalEntries,
                ["distinctAiProfiles"] = census.DistinctAiProfiles,
                ["distinctFollowTypes"] = census.DistinctFollowTypes,
                ["npcTableReadable"] = npcTableReadable,
            }, census.Findings);
    }

    private static bool TryOpenStore(out MonsterStore? store, out string error)
    {
        var conn = ConnectionConfig.Resolve(out var hint);
        if (string.IsNullOrWhiteSpace(conn))
        {
            store = null;
            error = "数据库连接串不可用。" + hint;
            return false;
        }
        store = new MonsterStore(conn!);
        if (!store.TemplateTableReachable(out var tableError))
        {
            error = $"怪物表不可达（迁移脚本 scripts/sql/019_monster_editor_tables.sql 是否已执行？）：{tableError}";
            store.Dispose();
            store = null;
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryOpenDrafts(out MonsterDraftStore? drafts, out string error)
    {
        if (MonsterDraftStore.TryConnect(out drafts, out _, out error)) return true;
        return false;
    }

    /// <summary>从 stdin（"-"）或文件读草稿；顺带做**键名自证**：未知键说明两侧漂移，而 STJ 会静默丢弃它。</summary>
    private static bool ReadDraft(string[] rest, out MonsterDraft? draft, out string error)
    {
        draft = null;
        error = string.Empty;
        var source = rest.Length > 0 ? rest[0] : "-";
        string json;
        try
        {
            json = source == "-" ? _stdin.ReadToEnd() : File.ReadAllText(source);
        }
        catch (Exception ex)
        {
            error = "读取草稿失败：" + ex.GetBaseException().Message;
            return false;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "草稿为空（stdin 没有内容）。";
            return false;
        }

        var unknownKeys = FindUnknownKeys(json);
        if (unknownKeys.Count > 0)
        {
            error = "草稿含未知键，将被静默丢弃：" + string.Join(", ", unknownKeys)
                    + "（请对齐 MonsterDraft.KnownTopLevelKeys；两侧字段名是协议）";
            return false;
        }

        draft = MonsterDraft.FromJson(json);
        if (draft == null)
        {
            error = "JSON 反序列化失败";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 找出 JSON 顶层与已知键集合对不上的键名。
    /// <para>这条检查存在是因为一次真实缺陷模式：字段名漂一个字母，STJ 不报错、值全是初值，
    /// 而"两侧同为 0"让自校验反而全绿。</para>
    /// </summary>
    private static List<string> FindUnknownKeys(string json)
    {
        var unknown = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return unknown;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!MonsterDraft.KnownTopLevelKeys.Contains(prop.Name)) unknown.Add(prop.Name);
            }
        }
        catch (JsonException)
        {
            // 交给反序列化那一步去报"JSON 坏了"，这里不重复报。
        }
        return unknown;
    }

    private static bool TryParseInt(string[] args, out int value)
    {
        foreach (var a in args)
        {
            var token = a.StartsWith("--") ? a.Substring(2) : a;
            var candidate = token.Contains('=') ? token.Split('=', 2)[1] : token;
            if (int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        }
        value = 0;
        return false;
    }

    private static bool TryGetOption(string[] args, string name, out int value)
    {
        value = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        // 也接受 --name=value
        var prefix = "--" + name + "=";
        var hit = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (hit != null) return int.TryParse(hit.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        return false;
    }

    /// <summary>
    /// 字符串版选项读取：<c>--name=v</c> 与 <c>--name v</c> 都认（面板走的是 argv，脚本走的是等号形）。
    /// <para>空值（<c>--text=</c>）会返回 <c>true</c> 和空串——这不是"没给"：
    /// 清空一行文案与漏写这一格必须能分开，否则一次手滑就把台词删了。</para>
    /// <para>刻意**不与整数版重载同名**：两个重载 + 调用点写 <c>out var</c> 会让编译器无法定档
    /// （本工程实测把 preview-xp 那七处一起点红）。</para>
    /// </summary>
    private static bool TryGetTextOption(string[] args, string name, out string value)
    {
        value = string.Empty;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase)) continue;
            value = args[i + 1];
            return true;
        }
        var prefix = "--" + name + "=";
        var hit = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (hit == null) return false;
        value = hit.Substring(prefix.Length);
        return true;
    }

    private static int Run(string verb, bool ok, string message, object? data = null, List<string>? warnings = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["ok"] = ok,
            ["verb"] = verb,
            ["message"] = message,
            ["data"] = data,
            ["warnings"] = warnings != null && warnings.Count > 0 ? warnings : null,
        };
        _stdout.Write(JsonSerializer.Serialize(payload, OutputOptions));
        _stdout.Write("\n");
        return ok ? 0 : 1;
    }
}
