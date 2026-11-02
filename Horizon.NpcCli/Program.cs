using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Monsters;
using Horizon.Game.Sim.Npcs;
using Horizon.QuestCli;

namespace Horizon.NpcCli;

/// <summary>
/// NPC 模板的命令行入口：NPC 编辑器（Flax 插件）以"短生命周期子进程"的方式调用它。
/// <para>
/// 输出恒为一段 JSON（UTF-8），字段固定 <c>ok/verb/message/data/warnings</c>；出错也走同一段 JSON，退出码非 0。
/// 与 <c>Horizon.QuestCli</c> / <c>Horizon.MonsterCli</c> 逐字同构——编辑器的解析代码可以照搬。
/// </para>
/// <para>
/// <b>preview-* 一律从 stdin 收草稿（参数 <c>-</c>）而不查库</b>：编辑器在用户还没保存时就要看到
/// "这件货卖多少钱 / 头顶会画什么 / 缺哪几样材料"，那时库里没有这份数据；离线预览也是唯一能不连库跑的部分。
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
            InitializeStreams();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("标准流接管失败：" + ex.Message);
        }

        try
        {
            return verb switch
            {
                "help" or "--help" or "-h" => Run(verb, true, HelpText(), null),
                "catalog" => RunCatalog(),
                "get" => RunGet(rest),
                "validate" => RunValidate(rest),
                "save" => RunSave(rest),
                "publish" => RunSetState(rest, 1),
                "retire" => RunSetState(rest, 2),
                "allocate-id" => RunAllocateId(rest),
                "preview-indicator" => RunPreviewIndicator(rest),
                "preview-vendor" => RunPreviewVendor(rest),
                "preview-train" => RunPreviewTrain(rest),
                "preview-craft" => RunPreviewCraft(rest),
                "preview-xp" => RunPreviewXp(rest),
                "preview-schedule" => RunPreviewSchedule(rest),
                "preview-gossip" => RunPreviewGossip(rest),
                "preview-services" => RunPreviewServices(rest),
                "preview-drop" => RunPreviewDrop(rest),
                "preview-route" => RunPreviewRoute(rest),
                "quest-chain" => RunQuestChain(rest),
                "export-content" => RunExportContent(rest),
                "export-visuals" => RunExportVisuals(rest),
                _ => Run(verb, false, "未知动词：" + verb + "（跑 help 看用法）", null),
            };
        }
        catch (Exception ex)
        {
            // 未捕获异常也必须走 JSON 信封：编辑器只解析一行 JSON，裸栈会让它显示"无输出"。
            return Run(verb, false, $"未处理异常：{ex.GetType().Name}: {ex.Message}", null);
        }
    }

    private static string HelpText() =>
        "NPC 编辑器 CLI。动词："
        + "catalog | get <id> | validate - | save - --expect-version <n> | publish <id> | retire <id> | "
        + "allocate-id --segment service|hostile | "
        + "preview-indicator - --player <lvl> [--accepted] [--done] [--gate-fail] [--turnin-elsewhere] | "
        + "preview-vendor - [--gold <n>] | preview-train - [--gold <n>] | preview-craft - [--have item:count,...] [--skill <n>] | "
        + "preview-xp --npc-level <n> --player-level <n> [--pvp] [--rank <0-4>] | "
        + "preview-schedule - [--t <seconds>] | preview-gossip - [--player <lvl>] | "
        + "preview-services - [--player <lvl>] [--turnin n] [--offer n] [--progress n] [--locked n] | "
        + "preview-drop - | preview-route - [--route-index <n>] [--speed <m/s>] | quest-chain <npcId> | export-content --out <path> [--drafts]";

    private static int Run(string verb, bool ok, string message, object? data, string[]? warnings = null)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["ok"] = ok,
            ["verb"] = verb,
            ["message"] = message,
            ["data"] = data,
            ["warnings"] = warnings ?? Array.Empty<string>(),
        };

        _stdout.WriteLine(JsonSerializer.Serialize(envelope, OutputOptions));
        return ok ? 0 : 1;
    }

    // ────────────────────────────── 参数与输入 ──────────────────────────────

    private static Dictionary<string, string?> ParseArgs(string[] rest)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rest.Length; i++)
        {
            var a = rest[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                map["_pos" + i] = a;
                continue;
            }

            if (i + 1 < rest.Length && !rest[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                map[a] = rest[++i];
            }
            else
            {
                map[a] = "1";
            }
        }

        return map;
    }

    private static int IntArg(Dictionary<string, string?> map, string key, int fallback)
        => map.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static long LongArg(Dictionary<string, string?> map, string key, long fallback)
        => map.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static float FloatArg(Dictionary<string, string?> map, string key, float fallback)
        => map.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    /// <summary>从 stdin 读草稿。**空 stdin 也要回明确的错误**：编辑器传空时会看到 "draft_empty" 而不是一串异常。</summary>
    private static NpcDraft? ReadDraft(out string error)
    {
        error = string.Empty;
        var json = _stdin.ReadToEnd();
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "draft_empty";
            return null;
        }

        var draft = NpcDraft.FromJson(json);
        if (draft is null)
        {
            error = "draft_json_invalid";
            return null;
        }

        if (draft.Template is null)
        {
            error = "draft_template_missing";
            return null;
        }

        return draft;
    }

    private static bool TryOpenStore(out NpcStore? store, out string error)
    {
        store = null;
        error = string.Empty;

        var connectionString = ConnectionConfig.Resolve(out var hint);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error = "找不到数据库连接串。设置 DB_GAME_CONNECTION_STRING 或配置 .env。" + hint;
            return false;
        }

        store = new NpcStore(connectionString);
        return true;
    }

    // ────────────────────────────── 读 ──────────────────────────────

    /// <summary>目录：硬编码 1/2/3 + 已装配内容。<b>不连库</b>——编辑器下拉框在连不上库时也要能用。</summary>
    private static int RunCatalog()
    {
        // 目录**以库为准**（含草稿）：CLI 是新进程、从不装配内容，只读 RegisteredTemplates 会永远只列 1/2/3。
        // 库不可用时才退回静态登记表，并把原因写进 warnings（别让"库连不上"与"没有 NPC"同形）。
        var warnings = new List<string>();
        object[] data;
        if (TryOpenStore(out var store, out var openError))
        {
            using (store)
            {
                var rows = store!.ListTemplates();
                data = rows.Select(x => (object)new
                {
                    npcId = x.NpcId,
                    name = x.Name,
                    title = x.Title,
                    modelKey = x.ModelKey,
                    wuxingDomain = x.WuxingDomain,
                    segment = SegmentOf(x.NpcId),
                    publishState = x.PublishState,
                }).ToArray();
            }

            return Run("catalog", true, $"共 {data.Length} 个已登记 NPC", data, warnings.ToArray());
        }

        warnings.Add(openError ?? "库不可用，已退回静态登记表");
        var fallback = NpcTemplateCatalog.RegisteredTemplates();
        data = fallback.Select(x => (object)new
        {
            npcId = x.TemplateId,
            name = x.Name,
            title = x.Title,
            modelKey = x.ModelKey,
            wuxingDomain = x.WuxingDomain,
            segment = SegmentOf(x.TemplateId),
            publishState = (byte)0,
        }).ToArray();

        return Run("catalog", true, $"共 {data.Length} 个已登记 NPC（静态表兜底）", data, warnings.ToArray());
    }

    /// <summary>号段（设计文档 §1.3）：1–299999 存量、400000–499999 服务型、500000–599999 敌对型。</summary>
    private static string SegmentOf(int npcId) => npcId switch
    {
        >= 400000 and < 500000 => "service",
        >= 500000 and < 600000 => "hostile",
        > 0 => "legacy",
        _ => "invalid",
    };

    private static int RunGet(string[] rest)
    {
        if (rest.Length < 1 || !int.TryParse(rest[0], out var npcId))
            return Run("get", false, "用法：get <npcId>", null);

        if (!TryOpenStore(out var store, out var error)) return Run("get", false, error, null);
        using (store)
        {
            var draft = store!.LoadDraft(npcId);
            if (draft is null) return Run("get", false, $"NPC {npcId} 不存在", null);
            return Run("get", true, "ok", draft);
        }
    }

    // ────────────────────────────── 校验 ──────────────────────────────

    /// <summary>
    /// 草稿校验：**与 DsHost 装配走同一个 <c>NpcContentLoader.Compile</c>**，
    /// 所以"编辑器说合法、游戏装不上"这类分叉在结构上不存在。
    /// </summary>
    private static int RunValidate(string[] rest)
    {
        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("validate", false, readError, null);

        var (_, receipt) = NpcContentLoader.Compile(draft.ToSnapshot());

        // 货架经济闸门要问**物品表**（结构检查只看得见 override 之间的关系）。
        // 库不可用时如实说"没查价格"，不假装已经校验过。
        var warnings = new List<string>();
        if (TryOpenStore(out var store, out var openError))
        {
            using (store)
            {
                receipt.Problems.AddRange(ValidateVendorPrices(draft, store!));
            }

            receipt.ProblemCount = receipt.Problems.Count;
        }
        else
        {
            warnings.Add(openError ?? "库不可用，货架价格未校验");
        }

        var data = new
        {
            npcId = draft.Template!.NpcId,
            problemCount = receipt.ProblemCount,
            problems = receipt.Problems.ToArray(),
            // 摘位不是错误，但必须让作者看见：编辑器里勾着位、世界里却没入口，是"配了没反应"的典型。
            prunedBits = receipt.PrunedServiceBits,
            prunedServices = NpcValidateReport.PrunedServices(receipt),
            receipt = receipt.Describe(),
        };

        return Run("validate", receipt.ProblemCount == 0,
            NpcValidateReport.Message(receipt), data, warnings.ToArray());
    }

    /// <summary>
    /// 货架经济闸门（真库）：物品必须存在、买价必须能解析为正数、卖价不许高于买价。
    /// <para>价格解析规则与运行期同一处（商人 override 优先，其次物品模板价）——
    /// 这里只做"先问同一份数据"，不另抄一套公式。</para>
    /// </summary>
    private static List<string> ValidateVendorPrices(NpcDraft draft, NpcStore store)
    {
        var problems = new List<string>();
        var rows = draft.VendorRows;
        if (rows == null || rows.Count == 0) return problems;

        var npcId = draft.Template?.NpcId ?? 0;
        var prices = store.ItemPrices(rows.Select(r => r.ItemId));
        foreach (var row in rows)
        {
            if (row == null || row.ItemId <= 0) continue;   // 结构检查已报 bad_item
            if (!prices.TryGetValue(row.ItemId, out var price))
            {
                problems.Add($"vendor_unknown_item:{npcId}:{row.ItemId}");
                continue;
            }

            var buy = row.BuyPriceOverride > 0 ? row.BuyPriceOverride : price.Buy;
            var sell = row.SellPriceOverride > 0 ? row.SellPriceOverride : price.Sell;
            if (buy <= 0) problems.Add($"vendor_no_buy_price:{npcId}:{row.ItemId}");
            if (buy > 0 && sell > buy) problems.Add($"vendor_sell_above_buy:{npcId}:{row.ItemId}:{sell}>{buy}");
        }

        return problems;
    }

    // ────────────────────────────── 写 ──────────────────────────────

    private static int RunSave(string[] rest)
    {
        var map = ParseArgs(rest);
        var expected = IntArg(map, "--expect-version", -1);

        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("save", false, readError, null);

        // 先校验再写库：坏草稿进库等于把问题推给运行期。
        var (_, receipt) = NpcContentLoader.Compile(draft.ToSnapshot());
        if (receipt.ProblemCount > 0)
        {
            return Run("save", false, $"草稿有 {receipt.ProblemCount} 个问题，未写库", receipt.Problems.ToArray());
        }

        if (!TryOpenStore(out var store, out var openError)) return Run("save", false, openError, null);
        using (store)
        {
            // 经济闸门：价格/物品关系必须问物品表；坏行进库等于把"买不了"推给玩家。
            var priceProblems = ValidateVendorPrices(draft, store!);
            if (priceProblems.Count > 0)
                return Run("save", false, $"货架有 {priceProblems.Count} 个经济问题，未写库", priceProblems.ToArray());

            var result = store!.Upsert(draft, expected);
            var data = new { result.Message, writtenRows = result.WrittenRows, npcId = draft.Template!.NpcId };
            return Run("save", result.Ok, result.Message, data);
        }
    }

    private static int RunSetState(string[] rest, byte state)
    {
        var verb = state == 1 ? "publish" : "retire";
        if (rest.Length < 1 || !int.TryParse(rest[0], out var npcId)) return Run(verb, false, $"用法：{verb} <npcId>", null);

        if (!TryOpenStore(out var store, out var openError)) return Run(verb, false, openError, null);
        using (store)
        {
            var result = store!.SetPublishState(npcId, state);
            return Run(verb, result.Ok, result.Message, new { npcId, published = state == 1, writtenRows = result.WrittenRows });
        }
    }

    /// <summary>
    /// 分配一个新 NPC 编号。编辑器「新建」先要号再摆空白草稿——手填号会撞号，
    /// 而撞号在保存链路里只表现为覆盖（乐观锁管的是版本，不管"这个号本来就有人"）。
    /// <para><b>legacy 1–3 不再分配</b>：那三个号被任务表写着，界面里也不该出现"新建一个铸剑师"。</para>
    /// </summary>
    private static int RunAllocateId(string[] rest)
    {
        var map = ParseArgs(rest);
        var name = (map.TryGetValue("--segment", out var raw) ? raw : "service")?.Trim().ToLowerInvariant();
        var segment = name switch
        {
            "service" => NpcIdSegment.Service,
            "hostile" => NpcIdSegment.Hostile,
            _ => NpcIdSegment.OutOfRange,
        };
        if (segment == NpcIdSegment.OutOfRange)
            return Run("allocate-id", false, "用法：allocate-id --segment service|hostile（legacy 1–3 保留，不再分配）", null);

        if (!TryOpenStore(out var store, out var openError)) return Run("allocate-id", false, openError, null);
        using (store)
        {
            var result = store!.AllocateId(segment);
            var data = result.Ok
                ? new { npcId = result.NpcId, segment = NpcIdSegmentCore.NameOf(segment) }
                : null;
            return Run("allocate-id", result.Ok, result.Message, data);
        }
    }

    // ────────────────────────────── 预览（离线，不连库） ──────────────────────────────

    /// <summary>
    /// 头顶指示器预览。
    /// <para>草稿里的任务关系逐条按开关组合成"玩家态"，再走 <see cref="NpcQuestIndicatorCore"/> 求四态与数字前缀。
    /// 这些开关是**给编辑器演示/单测用的模拟输入**，运行期的玩家态来自任务库。</para>
    /// </summary>
    private static int RunPreviewIndicator(string[] rest)
    {
        var map = ParseArgs(rest);
        var player = IntArg(map, "--player", 1);
        var accepted = map.ContainsKey("--accepted");
        var done = map.ContainsKey("--done");
        var gateFail = map.ContainsKey("--gate-fail");
        var turnInElsewhere = map.ContainsKey("--turnin-elsewhere");

        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("preview-indicator", false, readError, null);

        var rows = new List<NpcQuestRow>(draft.QuestLinks.Count);
        foreach (var link in draft.QuestLinks)
        {
            rows.Add(new NpcQuestRow(
                link.QuestId,
                isGiverHere: link.Role == 1 || link.Role == 0,
                isTurnInHere: !turnInElsewhere && (link.Role == 2 || link.Role == 0),
                accepted: accepted,
                objectivesDone: done && accepted,
                gatePassed: !gateFail));
        }

        var counts = NpcQuestIndicatorCore.Accumulate(rows, out var ignored);
        var kind = NpcQuestIndicatorCore.Resolve(counts);

        var data = new
        {
            playerLevel = player,
            kind = kind.ToString(),
            glyph = NpcQuestIndicatorCore.GlyphOf(kind),
            prefix = NpcQuestIndicatorCore.PrefixCountOf(counts),
            counts = counts.Describe(),
            ignoredRows = ignored,
            lines = rows.Select(r => new { r.QuestId, bucket = NpcQuestIndicatorCore.Classify(r).ToString() }).ToArray(),
        };

        return Run("preview-indicator", true, NpcQuestIndicatorCore.Describe(counts), data);
    }

    /// <summary>
    /// 商人预览：逐行算"玩家付多少 / 收多少 / 还有没有货"。
    /// <para><b>离线限制</b>：物品模板价读不到（不连库），所以本动词只看草稿里的覆盖价。
    /// 覆盖价缺失时会如实报 <c>NoBuyPrice</c>——那正是运行期会发生的判定，不是预览的缺陷。</para>
    /// </summary>
    private static int RunPreviewVendor(string[] rest)
    {
        var map = ParseArgs(rest);
        var gold = LongArg(map, "--gold", long.MaxValue / 4);

        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("preview-vendor", false, readError, null);

        var lines = new List<object>(draft.VendorRows.Count);
        foreach (var v in draft.VendorRows.OrderBy(x => x.Slot))
        {
            var row = new NpcVendorRow(v.ItemId, v.Slot, v.BuyPriceOverride, v.SellPriceOverride, v.StockMax, v.RestockSeconds);
            var item = new NpcItemPriceRow(0, 0); // 模板价离线不可得
            var stock = NpcCommerceCore.ResolveStock(in row, 0, 0, 0);
            var buy = NpcCommerceCore.ResolveBuyPrice(in row, in item);
            var sell = NpcCommerceCore.ResolveSellPrice(in row, in item);
            var tryBuy = NpcCommerceCore.TryBuy(in row, in item, in stock, gold, 1);

            lines.Add(new
            {
                slot = v.Slot,
                itemId = v.ItemId,
                buy = buy.Describe(),
                sell = sell.Describe(),
                stock = stock.Describe(),
                tryBuy = tryBuy.Describe(),
            });
        }

        return Run("preview-vendor", true, $"共 {lines.Count} 行货架（模板价离线不可得，仅算覆盖价）", lines.ToArray());
    }

    /// <summary>
    /// 训练师预览。<b>离线无法得到游戏门槛结论</b>（那要问 SkillGrain），
    /// 所以一律按"门槛已过"算，并在 message 里写明这一点——
    /// 假装能算门槛才是更坏的做法（编辑器会显示"可学"而游戏里灰着）。
    /// </summary>
    private static int RunPreviewTrain(string[] rest)
    {
        var map = ParseArgs(rest);
        var gold = LongArg(map, "--gold", long.MaxValue / 4);

        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("preview-train", false, readError, null);

        var lines = new List<object>(draft.TrainerRows.Count);
        foreach (var t in draft.TrainerRows.OrderBy(x => x.Seq))
        {
            var row = new NpcTrainerRow(t.SkillId, (NpcTrainerService)t.ServiceKind, t.CostGold, t.CostItemId, t.CostItemCount, t.Seq);
            var verdict = NpcTrainerCore.Evaluate(in row, gameGatePassed: true, gameGateCode: 0, buyerGold: gold, haveCostItem: int.MaxValue / 4);
            lines.Add(new { seq = t.Seq, skillOrRecipeId = t.SkillId, service = t.ServiceKind, verdict = verdict.Describe() });
        }

        return Run("preview-train", true,
            "共 " + lines.Count + " 行教学（离线预览按『游戏门槛已过』计；真实门槛由 SkillGrain.CheckLearnGates 判）",
            lines.ToArray());
    }

    /// <summary>制作预览：缺料清单 + 成功档。<c>--have item:count,...</c> 模拟背包。</summary>
    private static int RunPreviewCraft(string[] rest)
    {
        var map = ParseArgs(rest);
        var skill = IntArg(map, "--skill", 0);

        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("preview-craft", false, readError, null);

        var have = new List<NpcHeldItem>();
        if (map.TryGetValue("--have", out var haveArg) && !string.IsNullOrWhiteSpace(haveArg))
        {
            foreach (var part in haveArg.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out var itemId) && int.TryParse(kv[1], out var count))
                    have.Add(new NpcHeldItem(itemId, count));
            }
        }

        var lines = new List<object>(draft.Recipes.Count);
        foreach (var r in draft.Recipes)
        {
            var needs = r.Materials.Select(m => new NpcMaterialNeed(m.ItemId, m.Count)).ToArray();
            var check = NpcCraftCore.CheckMaterials(needs, have);

            var curve = new NpcCraftChanceCurve(r.QualityGapCaps.ToArray(), r.QualityChanceBps.ToArray());
            var tier = curve.TierFor(Math.Max(0, r.SkillReq - skill));

            lines.Add(new
            {
                recipeId = r.RecipeId,
                check = check.Describe(),
                shortfalls = check.Shortfalls.Select(s => s.Describe()).ToArray(),
                chanceTier = tier.ToString(),
                chanceBps = curve.ChanceBpsFor(tier),
                curveUsable = curve.IsUsable,
            });
        }

        return Run("preview-craft", true, $"共 {lines.Count} 张配方", lines.ToArray());
    }

    /// <summary>
    /// 掉落预览（离线，读 stdin 草稿）：逐件给**精确命中概率**与期望件数，外加每次击杀的期望件数。
    /// <para>用的是漏洞内核自己的解析口径（<c>MonsterLootCore.EntryWinChanceBps</c>），
    /// 不在 CLI 里重算一遍概率——重算的那份在互斥组上会系统性高估（同组每次最多一件）。</para>
    /// </summary>
    private static int RunPreviewDrop(string[] rest)
    {
        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("preview-drop", false, readError, null);

        var bindings = new List<MonsterLootBindingSnapshot>(draft.LootBindings.Count);
        foreach (var b in draft.LootBindings) bindings.Add(b.ToSnapshot());

        var items = new List<object>();
        int moneyMin = 0, moneyMax = 0;

        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            moneyMin += binding.MoneyMin;
            moneyMax += binding.MoneyMax;

            for (var e = 0; e < binding.Entries.Length; e++)
            {
                var entry = binding.Entries[e];
                items.Add(new
                {
                    itemId = entry.ItemId,
                    chanceBps = entry.ChanceBps,
                    winChanceBps = MonsterLootCore.EntryWinChanceBps(binding, e),
                    expectedCount = MonsterLootCore.EntryExpectedItems(binding, e),
                    seq = entry.Seq,
                    groupId = entry.GroupId,
                });
            }
        }

        var data = new
        {
            bindings = bindings.Count,
            items,
            expectedItemsPerKill = MonsterLootCore.ExpectedItemsPerKill(bindings),
            moneyMin,
            moneyMax,
        };

        return Run("preview-drop", true,
            $"绑定 {bindings.Count} 份、条目 {items.Count} 件（期望每杀 {data.expectedItemsPerKill:0.###} 件）", data);
    }

    /// <summary>
    /// 路线试算：一个循环有多远、多久、会不会有问题（编辑器「刷点·路线」页的"试算"按钮）。
    /// <para>顺序与停留由巡逻内核现算（<see cref="NpcRoutePreviewCore"/>），这里只负责把草稿翻译成输入：
    /// 点按 <c>seq</c> 排、走法与停留下限取草稿、速度优先 <c>--speed</c> 否则模板步行速度。</para>
    /// </summary>
    private static int RunPreviewRoute(string[] rest)
    {
        var map = ParseArgs(rest);
        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("preview-route", false, readError, null);

        var routes = draft.Routes ?? new List<NpcRouteSnapshot>();
        var index = IntArg(map, "--route-index", 0);
        if (index < 0 || index >= routes.Count)
            return Run("preview-route", false, $"草稿里没有第 {index} 条路线（共 {routes.Count} 条）", null);

        var route = routes[index];
        var ordered = route.Points.OrderBy(p => p.Seq).ToArray();
        var points = new Horizon.Game.Sim.Monsters.MonsterPatrolPoint[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            points[i] = new Horizon.Game.Sim.Monsters.MonsterPatrolPoint(
                ordered[i].X, ordered[i].Y, ordered[i].Z, ordered[i].PauseSeconds);
        }

        var speed = FloatArg(map, "--speed", draft.Template is { SpeedWalk: > 0f } t ? t.SpeedWalk : 1.6f);
        var mode = (Horizon.Game.Sim.Monsters.MonsterPatrolMode)Math.Clamp((int)route.Mode, 0, 2);
        var preview = NpcRoutePreviewCore.Preview(points, mode, route.PauseAtPointSeconds, speed);

        var data = new
        {
            routeIndex = index,
            pathId = route.PathId,
            mode = mode.ToString(),
            speed,
            preview.Points,
            preview.Arrivals,
            totalMetres = (float)Math.Round(preview.TotalMetres, 2),
            minLegMetres = (float)Math.Round(preview.MinLegMetres, 2),
            maxLegMetres = (float)Math.Round(preview.MaxLegMetres, 2),
            pauseSeconds = (float)Math.Round(preview.PauseSeconds, 2),
            cycleSeconds = preview.CycleSeconds >= 0f ? (float)Math.Round(preview.CycleSeconds, 2) : -1f,
            preview.Closed,
            warnings = preview.Warnings,
        };

        var message = preview.CycleSeconds >= 0f
            ? $"循环 {preview.TotalMetres:0.#} 米 / {preview.CycleSeconds:0.#} 秒（停 {preview.PauseSeconds:0.#} 秒；问题 {preview.Warnings.Length} 条）"
            : $"循环 {preview.TotalMetres:0.#} 米（没配步行速度，时间不可估；问题 {preview.Warnings.Length} 条）";

        return Run("preview-route", preview.Warnings.Length == 0, message, data,
            preview.Warnings.Length == 0 ? null : preview.Warnings);
    }

    /// <summary>击杀经验预览（含 PVP 分支：同一让分曲线，目标等级换成对方等级）。</summary>
    private static int RunPreviewXp(string[] rest)
    {
        var map = ParseArgs(rest);
        var npcLevel = IntArg(map, "--npc-level", 0);
        var playerLevel = IntArg(map, "--player-level", 0);
        var pvp = map.ContainsKey("--pvp");
        var rank = (MonsterRank)IntArg(map, "--rank", 0);

        if (npcLevel <= 0 || playerLevel <= 0)
            return Run("preview-xp", false, "用法：preview-xp --npc-level <n> --player-level <n> [--pvp] [--rank 0-4]", null);

        var source = pvp ? NpcKillSource.PvpPlayer : NpcKillSource.HostileNpc;
        var verdict = NpcKillCore.Evaluate(source, MonsterXpConfig.Default, npcLevel, playerLevel, rank);

        var data = new
        {
            source = source.ToString(),
            verdict = verdict.Describe(),
            xp = verdict.Xp,
            difficulty = verdict.Difficulty.ToString(),
            reason = verdict.Reason.ToString(),
        };

        return Run("preview-xp", true, verdict.Describe(), data);
    }

    /// <summary>作息预览：给定周期内秒，返回生效档/路线/姿态。</summary>
    private static int RunPreviewSchedule(string[] rest)
    {
        var map = ParseArgs(rest);
        var t = IntArg(map, "--t", 0);
        var cycle = IntArg(map, "--cycle", NpcScheduleCore.DefaultDayCycleSeconds);

        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("preview-schedule", false, readError, null);

        var slots = draft.ScheduleRows
            .OrderBy(x => x.Seq)
            .Select(x => new NpcScheduleSlot(x.FromSeconds, x.ToSeconds, x.RouteId, (NpcPosture)x.Posture, x.GossipGroupId))
            .ToList();

        var verdict = NpcScheduleCore.SlotAt(slots, t, cycle);
        var uncovered = NpcScheduleCore.UncoveredSeconds(slots, cycle);

        var data = new
        {
            secondsOfDay = verdict.SecondsOfDay,
            matched = verdict.Matched,
            slotIndex = verdict.SlotIndex,
            routeId = verdict.RouteId,
            posture = verdict.Posture.ToString(),
            gossipGroupId = verdict.GossipGroupId,
            reason = verdict.Reason.ToString(),
            badSlots = verdict.BadSlots,
            uncoveredSeconds = uncovered,
            describe = verdict.Describe(),
        };

        return Run("preview-schedule", uncovered == 0, verdict.Describe(), data);
    }

    /// <summary>
    /// 服务入口预览：这位 NPC 对"给定玩家态"会露出哪些入口、其余为什么不可见。
    /// <para>判据全在 <see cref="NpcServiceCore"/>；这里只把编译后的内容包与玩家态喂进去并打印。</para>
    /// </summary>
    private static int RunPreviewServices(string[] rest)
    {
        var map = ParseArgs(rest);
        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("preview-services", false, readError, null);

        var (bundle, receipt) = NpcContentLoader.Compile(draft.ToSnapshot());
        var player = IntArg(map, "--player", 1);
        var quests = new NpcQuestCounts(
            IntArg(map, "--turnin", 0),
            IntArg(map, "--offer", 0),
            IntArg(map, "--progress", 0),
            IntArg(map, "--locked", 0));

        var preview = NpcServicePreviewCore.Build(bundle, draft.Template!.NpcId, player, quests);
        if (preview.Entries == null)
        {
            return Run("preview-services", false,
                "这位 NPC 没有通过装配校验（先 validate）", new { problemCount = receipt.ProblemCount });
        }

        var items = new object[preview.Entries.Count];
        for (var i = 0; i < items.Length; i++)
        {
            var e = preview.Entries[i];
            items[i] = new { bit = e.Bit.ToString(), visible = e.Visible, reason = e.Reason.ToString() };
        }

        var data = new
        {
            npcId = preview.NpcId,
            declared = preview.DeclaredFlags,
            effective = preview.EffectiveFlags,
            visible = preview.VisibleCount,
            quests = preview.QuestCounts.Describe(),
            items,
        };

        return Run("preview-services", true,
            $"可见入口 {preview.VisibleCount} 个（生效位图 0x{preview.EffectiveFlags:X}）", data);
    }

    /// <summary>对话菜单预览：按玩家等级筛可见项，并核对"动作位是否开了服务位"。</summary>
    private static int RunPreviewGossip(string[] rest)
    {
        var map = ParseArgs(rest);
        var player = IntArg(map, "--player", 1);

        var draft = ReadDraft(out var readError);
        if (draft is null) return Run("preview-gossip", false, readError, null);

        var flags = draft.Template!.ServiceFlags;
        var options = draft.GossipRows
            .OrderBy(x => x.GroupId).ThenBy(x => x.Seq)
            .Select(x => new NpcGossipOption(x.Seq, x.GroupId, x.TextId, (NpcGossipAction)x.ActionKind, x.ActionArg, x.ReqMinLevel, x.ReqMaxLevel))
            .ToList();

        var visible = NpcGossipCore.FilterVisible(options, player);
        var badActions = visible.Where(o => !NpcGossipCore.ActionServiceAvailable(in o, flags)).Select(o => o.Describe()).ToArray();
        // 台词正文按 (组, 序) 回填：预览也要能看出"这一项玩家会读到哪句话"（023 未执行时一律空串）。
        var textByKey = draft.GossipRows
            .GroupBy(x => (x.GroupId, x.Seq))
            .ToDictionary(g => g.Key, g => g.First().Text ?? string.Empty);

        var data = new
        {
            total = options.Count,
            visible = visible.Count,
            hiddenByLevel = options.Count - visible.Count,
            items = visible.Select(o => new
            {
                o.Seq, o.GroupId, action = o.Action.ToString(), o.ActionArg, o.MinLevel, o.MaxLevel,
                text = textByKey.TryGetValue((o.GroupId, o.Seq), out var text) ? text : string.Empty,
            }).ToArray(),
            actionBitMismatch = badActions,
        };

        return Run("preview-gossip", badActions.Length == 0,
            badActions.Length == 0 ? $"可见 {visible.Count}/{options.Count} 项" : $"{badActions.Length} 项动作引用了未开启的服务位",
            data);
    }

    // ────────────────────────────── 导出 ──────────────────────────────

    /// <summary>
    /// 导出客户端 NPC 立场表（名牌绿/黄/红）。
    /// <para><b>连不上库时用硬编码三条兜底</b>，并在回执里写明 <c>source=catalog_fallback</c>：
    /// 假装成功会让人以为"库里配的 NPC 也已经进了这张表"。</para>
    /// </summary>
    private static int RunExportVisuals(string[] rest)
    {
        var map = ParseArgs(rest);
        if (!map.TryGetValue("--out", out var outPath) || string.IsNullOrWhiteSpace(outPath))
            return Run("export-visuals", false, "用法：export-visuals --out <path>", null);

        string json;
        int count;
        string source;
        var fallbackReason = string.Empty;

        // 注意：TryOpenStore 只验证"连接串找得到"，**真正连库是在查询时**。
        // 021 还没执行时这里会抛 SqlException（对象名无效），必须同样走兜底，
        // 否则"表不存在"会被当成未处理异常——而这一步恰恰是最需要给出可用产物的时候。
        if (TryOpenStore(out var store, out var openError))
        {
            using (store)
            {
                try
                {
                    var snapshot = store!.LoadSnapshot(publishedOnly: true);
                    json = NpcVisualsExport.Build(snapshot, out count);
                    source = "db";
                }
                catch (Exception ex)
                {
                    json = NpcVisualsExport.BuildFallback(out count);
                    source = "catalog_fallback";
                    fallbackReason = ex.GetBaseException().Message;
                }
            }
        }
        else
        {
            json = NpcVisualsExport.BuildFallback(out count);
            source = "catalog_fallback";
            fallbackReason = openError;
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, json, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            return Run("export-visuals", false, $"写文件失败：{ex.GetType().Name}: {ex.Message}", null);
        }

        var data = new
        {
            outPath = Path.GetFullPath(outPath),
            count,
            source,
            fallbackReason,
            keys = NpcVisualsExport.ClientKeys,
        };

        return Run("export-visuals", true,
            fallbackReason.Length == 0
                ? $"已导出 {count} 条（来源 {source}）"
                : $"已导出 {count} 条（来源 {source}：{fallbackReason}）",
            data);
    }

    /// <summary>
    /// 导出内容快照（DsHost 的输入）。<b>默认只导已发布</b>；<c>--drafts</c> 连草稿一起导（仅供检查）。
    /// 导出结果与运行期装配复用同一个 <c>NpcContentLoader.Compile</c>，导出时就把问题数报出来。
    /// </summary>
    /// <summary>
    /// 某 NPC 的任务链（编辑器「任务链」面板吃这份 JSON）。
    /// <para>
    /// 节点含**传递前置**：只列本 NPC 直接挂的任务，链头那几环会消失，
    /// 运营看到的是一条从中间开始的任务线。排序与连边统一交给 <c>QuestChainCore</c>。
    /// </para>
    /// </summary>
    private static int RunQuestChain(string[] rest)
    {
        if (rest.Length == 0 || !int.TryParse(rest[0], out var npcId) || npcId <= 0)
            return Run("quest-chain", false, "用法：quest-chain <npcId>", null);

        if (!TryOpenStore(out var store, out var openError))
            return Run("quest-chain", false, openError, null);

        using (store)
        {
            try
            {
                var (nodes, loadProblems, source, briefs) = store!.LoadQuestChain(npcId);
                var chain = Horizon.Game.Sim.Quests.QuestChainCore.Build(nodes);
                foreach (var p in loadProblems) chain.Problems.Add(p);

                var data = new
                {
                    npcId,
                    source,
                    nodes = chain.Nodes.Select(n =>
                    {
                        briefs.TryGetValue(n.QuestId, out var brief);
                        // 目标文本走权威映射器（QuestWireMapper）解析 objectives JSON，
                        // 再由 QuestChainText 拼成"×N"这种行——插件只逐行打印，不自己拼。
                        var objectives = Horizon.Game.Core.Handlers.QuestWireMapper
                            .ObjectivesFromTemplateJson(brief?.ObjectivesJson)
                            .Select(o => QuestChainText.ObjectiveLine(o.Description, o.Required))
                            .ToArray();

                        return new
                        {
                            questId = n.QuestId,
                            name = n.Name,
                            description = brief?.Description ?? string.Empty,
                            objectives,
                            giverTemplateId = n.GiverTemplateId,
                            turnInTemplateId = n.TurnInTemplateId,
                            prerequisites = n.Prerequisites,
                            orderSeq = n.OrderSeq,
                        };
                    }).ToArray(),
                    edges = chain.Edges.Select(e => new { from = e.FromQuestId, to = e.ToQuestId }).ToArray(),
                    roots = chain.Roots.ToArray(),
                    topologicalOrder = chain.TopologicalOrder.ToArray(),
                    problems = chain.Problems.ToArray(),
                };

                var message = chain.Nodes.Count == 0
                    ? $"npc#{npcId} 没有任务链（链接表里没有已启用的行）"
                    : chain.Describe();
                return Run("quest-chain", chain.Problems.Count == 0, message, data,
                    chain.Problems.Count == 0 ? null : chain.Problems.ToArray());
            }
            catch (Exception ex)
            {
                // 连库失败要如实回（与 export-visuals 同一条纪律：TryOpenStore 只验证连接串找得到）。
                return Run("quest-chain", false, $"读库失败：{ex.GetBaseException().Message}", null);
            }
        }
    }

    private static int RunExportContent(string[] rest)
    {
        var map = ParseArgs(rest);
        if (!map.TryGetValue("--out", out var outPath) || string.IsNullOrWhiteSpace(outPath))
            return Run("export-content", false, "用法：export-content --out <path> [--drafts]", null);

        if (!TryOpenStore(out var store, out var openError)) return Run("export-content", false, openError, null);
        using (store)
        {
            var snapshot = store!.LoadSnapshot(publishedOnly: !map.ContainsKey("--drafts"));
            var (_, receipt) = NpcContentLoader.Compile(snapshot);

            Array.ForEach(receipt.Problems.ToArray(), p => snapshot.Problems.Add(p));

            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, snapshot.ToJson(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                return Run("export-content", false, $"写文件失败：{ex.GetType().Name}: {ex.Message}", null);
            }

            var data = new
            {
                outPath = Path.GetFullPath(outPath),
                receipt = receipt.Describe(),
                problems = receipt.Problems.ToArray(),
                skipped = snapshot.Skipped.ToArray(),
            };

            // 有问题仍然写文件（可能是"缺美术"这类已知状态），但 ok=false 让调用方必须看见。
            return Run("export-content", receipt.ProblemCount == 0, receipt.Describe(), data, snapshot.Skipped.ToArray());
        }
    }
}
