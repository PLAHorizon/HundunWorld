using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Horizon.MonsterCli;

/// <summary>
/// 文本源文件里的一行（草稿侧的最小事实，刻意不依赖 EF 实体——库里**没有**文本表）。
/// </summary>
/// <param name="MonsterId">怪物编号（决定导出顺序，避免"同样数据每次导出顺序不同"的无意义 diff）。</param>
/// <param name="SayId">文本号：行为表 <c>aggro_say_id</c> 指的就是它，**线上也只发它**。</param>
/// <param name="Group">组（AC 的 <c>group</c>；本作先按"一组一条"用）。</param>
/// <param name="Seq">组内序号（稳定序用）。</param>
/// <param name="Text">文案本体。</param>
/// <param name="Emote">起手动作（AC 的 <c>emote</c>；0 = 不做动作）。</param>
public readonly record struct MonsterTextRow(
    int MonsterId, int SayId, int Group, int Seq, string Text, int Emote);

/// <summary>文本源的读取回执。</summary>
public sealed class MonsterTextSourceReceipt
{
    public bool Ok { get; set; }

    public string Path { get; set; } = string.Empty;

    /// <summary>读到的行数（0 是合法状态：还没人配文本）。</summary>
    public List<MonsterTextRow> Rows { get; } = new List<MonsterTextRow>();

    /// <summary>逐条点名的问题（坏行**跳过并点名**，不让它变成"这条文本永远念不出来"的静默）。</summary>
    public List<string> Problems { get; } = new List<string>();

    /// <summary>文件不存在 = 如实的"还没配文本"，与"文件坏了读不出"必须能分开。</summary>
    public bool FileMissing { get; set; }
}

/// <summary>客户端文本表的构建结果。</summary>
public sealed class MonsterTextsResult
{
    public string Json { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<string> Problems { get; } = new List<string>();

    /// <summary>
    /// (怪物, 文本号) 重复的行数。
    /// <para>单独计数：客户端按 <c>(modelKey, sayId)</c> 查，重复号的表现是"后一条把前一条顶掉"——
    /// 策划在源文件里加了一句喊话而游戏里念的是另一句，这种错只有一份计数能抓住。</para>
    /// </summary>
    public int DuplicateSayIds { get; set; }
}

/// <summary>
/// 文本源的**写入**回执（面板改一句台词时唯一的落盘出口）。
/// <para>与读侧回执分开：读侧的 <c>Rows</c> 是"文件里有什么"，这里的 <c>Action</c> 是"这次改了什么"。
/// 面板需要后者才能在回执里说"改完了"还是"其实没落盘"——只报"成功"的那类回执会把 no-op 与
/// 真写入压成同一个绿（本工程栽过：回执只报个数，读者把 0 读成一切正常）。</para>
/// </summary>
public sealed class MonsterTextWriteReceipt
{
    public bool Ok { get; set; }
    public string Path { get; set; } = string.Empty;

    /// <summary><c>upsert</c> / <c>delete</c> / <c>noop</c>（清空一格而它本来就空）。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>写完后文件里还剩几行（让人看见"这一改动的总体积"，不是只看这一行）。</summary>
    public int RowsAfter { get; set; }

    /// <summary>这一号最终落成的台词组（0 = 不成组）。回执要念值，否则"写进了哪一组"只能靠再读一遍文件。</summary>
    public int Group { get; set; }

    /// <summary>逐条点名的问题（读侧跳过的行会在这里复述——重写会把它们一起带走，不能说谎）。</summary>
    public List<string> Problems { get; } = new List<string>();
}

/// <summary>
/// 怪物文本的源文件与两张出口（服务端内容包 / 客户端解析表）。
/// <para>
/// <b>为什么走旁挂文件而不是库里一张表</b>：这是记录过的口径（决策清单 D15）——文本是**展示物**，
/// 与外观表同一形状：库里只存"这只怪进战要念第几号"，文案随内容包发布。
/// 于是"改一句台词"不需要迁移、不需要发版改库，而线上协议也永远不会带文案
/// （带文案 = 把展示文本当身份用，翻译一次作废全部旧引用——这条工程里栽过）。
/// </para>
/// <para>
/// <b>两张出口必须读同一个源</b>：<c>export-content</c> 把行并进服务端内容包（DS 用它判
/// "这一号存不存在"并在进战时发号），<c>export-texts</c> 把同一批行投影成客户端解析表。
/// 两边各读一份文件的话，就会出现"服务端认得有这一号、客户端查不到文案"的半截状态——
/// 那与"没配文本"在玩家那边同形（都是沉默），所以这里刻意只有一个入口函数 <see cref="LoadSource"/>。
/// </para>
/// </summary>
public static class MonsterTextsExport
{
    /// <summary>客户端读侧使用的键（与 <c>MonsterTextCatalog</c> 一一对应，改名要两侧同时改）。</summary>
    public static readonly string[] ClientKeys = { "texts", "modelKey", "sayId", "text", "emote" };

    /// <summary>源文件的默认位置（与外观表、内容快照同目录，插件面板也写这里）。</summary>
    /// <remarks>这是**相对仓库根**的一串，不能直接拿去读写文件——见 <see cref="ResolveSourcePath"/>。</remarks>
    public const string DefaultSourcePath = "HundunWorld/Data/Monsters/monster_texts.json";

    /// <summary>客户端文本表的默认位置（同样一串相对仓库根，用法同上）。</summary>
    public const string DefaultClientTablePath = "HundunWorld/Data/Monsters/client_texts.json";

    /// <summary>文本源文件那串相对路径定成绝对路径。</summary>
    public static string ResolveSourcePath(string? given) => ResolveRelativeToRepo(DefaultSourcePath, given);

    /// <summary>
    /// 取命令行里的 <c>--texts</c>：**两种写法都要认**（`--texts &lt;路径&gt;` 与 `--texts=&lt;路径&gt;`）。
    /// <para>为什么这条要单独成函数并回问题：只认空格写法时，`--texts=…` 会被<b>静默忽略</b>、
    /// 落回默认源文件——实测因此把两条测试台词写进了真正的 <c>monster_texts.json</c>。
    /// 回执里的 path 字段还看起来是对的（它是解析后的默认值），这是最难查的一类写错地方。</para>
    /// </summary>
    /// <returns><c>false</c> = 给了 <c>--texts</c> 却没带值（空值与"没写这个参数"必须是两件事）。</returns>
    public static bool TryGetTextsOption(IReadOnlyList<string> args, out string? path, out string? problem)
    {
        path = null;
        problem = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i] ?? string.Empty;
            if (arg.StartsWith("--texts=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg.Substring("--texts=".Length).Trim();
                if (value.Length == 0)
                {
                    problem = "--texts= 后面是空的：要缺省就别写这个参数（空值不许被当成默认路径）";
                    return false;
                }
                path = value;
                return true;
            }
            if (string.Equals(arg, "--texts", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count || (args[i + 1] ?? string.Empty).StartsWith("--", StringComparison.Ordinal))
                {
                    problem = "--texts 后面没跟路径（下一个参数是另一个开关）：这次什么都没写";
                    return false;
                }
                path = args[i + 1];
                return true;
            }
        }
        return true;
    }

    /// <summary>
    /// 同一套算法，但把两个"起点"当参数传进来——**顺序可测**的那一份。
    /// <para>公开那两个入口只能读进程自己的 <c>AppContext.BaseDirectory</c> 与
    /// <c>Directory.GetCurrentDirectory()</c>，而测试进程里这两者常常是同一个目录
    /// ⇒ "先当前目录还是先往上走"这个顺序在测试里根本分不开（反证 R2 第一版就是这样跑出绿的：
    /// 它是这一几何下的**等价变异体**）。把起点抽成参数之后，两条顺序各能造出真候选，判据才有牙。</para>
    /// </summary>
    /// <param name="defaultRelative">没给路径时用的那一串（相对仓库根）。</param>
    /// <param name="given">调用方显式给的路径（可为空/相对/绝对）。</param>
    /// <param name="currentDirectory">按当前目录算的起点。</param>
    /// <param name="baseDirectory">往上走找仓库锚点的起点。</param>
    public static string ResolveFrom(string? defaultRelative, string? given,
                                     string currentDirectory, string baseDirectory)
    {
        var explicitGiven = !string.IsNullOrWhiteSpace(given);
        var relative = explicitGiven ? given!.Trim()
                                     : (string.IsNullOrWhiteSpace(defaultRelative) ? DefaultSourcePath : defaultRelative!);
        if (Path.IsPathRooted(relative)) return relative;

        var normalized = relative.Replace('/', Path.DirectorySeparatorChar)
                                 .Replace('\\', Path.DirectorySeparatorChar);
        var fromCwd = Path.GetFullPath(Path.Combine(currentDirectory, normalized));

        // **显式给的相对串一律按当前目录算**（标准 CLI 语义）：往上找仓库那一步只服务于
        // "缺省位置"——那一串不是调用方选的，而是我们替他写的约定（相对仓库根），
        // 所以才有"当前目录里没有就去仓库根找"的必要。
        // 调用方自己写出来的路径再去改道，表现是"我指的那份没被读、读的是另一份"——
        // 那比原来的 bug 更难查（反证 R3 就是钉这一条）。
        if (explicitGiven) return fromCwd;

        var cwdParent = Path.GetDirectoryName(fromCwd);
        if (File.Exists(fromCwd) || (!string.IsNullOrEmpty(cwdParent) && Directory.Exists(cwdParent)))
            return fromCwd;

        for (var dir = new DirectoryInfo(baseDirectory); dir != null; dir = dir.Parent)
        {
            var probe = Path.Combine(dir.FullName, normalized);
            if (File.Exists(probe)) return probe;
            var anchorDir = Path.GetDirectoryName(probe);
            if (!string.IsNullOrEmpty(anchorDir) && Directory.Exists(anchorDir)) return probe;
        }
        return fromCwd;
    }

    /// <summary>客户端文本表那串相对路径定成绝对路径。</summary>
    public static string ResolveClientTablePath(string? given) => ResolveRelativeToRepo(DefaultClientTablePath, given);

    /// <summary>
    /// 把"相对仓库根的一串"定成一个**确定的绝对路径**（<see cref="ResolveSourcePath"/> 与
    /// <see cref="ResolveClientTablePath"/> 共用这一份算法，不留两遍）。
    /// <para><b>为什么必须有这一步</b>：面板起的 CLI 把 <c>WorkingDirectory</c> 设成可执行文件所在的
    /// <c>Horizon.MonsterCli/bin/Debug/net10.0</c>（见 <c>MonsterCliBridge.Run</c>），
    /// 而默认位置是相对仓库根的一串 ⇒ 直接拿它读写会算成
    /// <c>bin/Debug/net10.0/HundunWorld/Data/Monsters/monster_texts.json</c>：
    /// 读的时候报"源文件不存在，视为还没配文本"（面板上一句都不显示），
    /// 写的时候**在 bin 目录下面长出一棵假树**，而真正那份文件一个字没变。
    /// 手敲 CLI 时工作目录就是仓库根，所以这条路只在面板里坏——正是"只测了成功路径"那一族。</para>
    /// <para>定法：给了绝对路径就照用；否则先按当前目录算（人在仓库根敲），
    /// 找不到再从可执行文件往上走，认**仓库里有 <c>HundunWorld/Data/Monsters</c> 这个目录**为锚点
    /// （锚点是那个目录而不是去找解决方案文件，因为要落的就是这一份文件所在处）。
    /// 两处都没有时返回按当前目录算出的绝对路径——新建文件该落在调用方看得见的地方，
    /// 而不是悄悄落在 bin 底下。</para>
    /// </summary>
    private static string ResolveRelativeToRepo(string defaultRelative, string? given)
        => ResolveFrom(defaultRelative, given, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class SourceFileShape
    {
        public List<TextRowShape> Texts { get; set; } = new();
    }

    private sealed class TextRowShape
    {
        public int MonsterId { get; set; }
        public int SayId { get; set; }
        public int Group { get; set; }
        public int Seq { get; set; }
        public string? Text { get; set; }
        public int Emote { get; set; }
    }

    /// <summary>
    /// 读文本源文件。**文件不存在不是错误**（那是"还没人配文本"的如实状态），
    /// 但要在回执里说清楚，否则"读不到"与"读到 0 行"会被同一句"没有文本"糊过去。
    /// </summary>
    public static MonsterTextSourceReceipt LoadSource(string path)
    {
        var receipt = new MonsterTextSourceReceipt { Path = path ?? string.Empty };
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            receipt.Ok = true;
            receipt.FileMissing = true;
            receipt.Problems.Add("文本源文件不存在：" + path + "（视为还没配文本，不是错误）");
            return receipt;
        }

        try
        {
            var shape = JsonSerializer.Deserialize<SourceFileShape>(File.ReadAllText(path), ReadOptions);
            if (shape == null)
            {
                receipt.Ok = false;
                receipt.Problems.Add("文本源文件解析失败（内容为空或不是预期形状）：" + path);
                return receipt;
            }
            var seen = new HashSet<(int, int)>();
            foreach (var r in shape.Texts)
            {
                var text = (r.Text ?? string.Empty).Trim();
                if (r.MonsterId <= 0)
                {
                    receipt.Problems.Add($"跳过一行：monster_id={r.MonsterId} 非法（文本号 {r.SayId}）");
                    continue;
                }
                if (r.SayId <= 0)
                {
                    // 0 是"没配"的哨兵（与行为表那一格同一口径）：让它占一行会让引用检查永远查不出悬空。
                    receipt.Problems.Add($"跳过一行：怪物 {r.MonsterId} 的文本号是 0（0 = 没配，不许占一条）");
                    continue;
                }
                if (text.Length == 0)
                {
                    receipt.Problems.Add($"跳过一行：怪物 {r.MonsterId} 的文本号 {r.SayId} 是空文案（配了指纹也念不出东西）");
                    continue;
                }
                if (!seen.Add((r.MonsterId, r.SayId)))
                {
                    receipt.Problems.Add($"怪物 {r.MonsterId} 的文本号 {r.SayId} 重复：保留第一条，后一条被忽略");
                    continue;
                }
                receipt.Rows.Add(new MonsterTextRow(r.MonsterId, r.SayId, r.Group, r.Seq, text, r.Emote));
            }
            receipt.Ok = true;
            return receipt;
        }
        catch (Exception ex)
        {
            receipt.Ok = false;
            receipt.Problems.Add("文本源文件读取失败：" + ex.GetBaseException().Message);
            return receipt;
        }
    }

    /// <summary>
    /// 构建**客户端解析表**（<c>(modelKey, sayId) → 文案/动作</c>）。
    /// <para>键用 <c>modelKey</c> 而不是怪物编号：客户端手里的是快照里带来的模型键
    /// （外观表同一口径），而编号在跨库/跨环境时不稳定。</para>
    /// </summary>
    /// <param name="rows">源文件的行。</param>
    /// <param name="modelKeysByMonsterId">怪物编号 → 模型键（CLI 从库里投影；查不到的行点名并跳过）。</param>
    public static MonsterTextsResult Build(
        IEnumerable<MonsterTextRow> rows,
        IReadOnlyDictionary<int, string> modelKeysByMonsterId)
    {
        var result = new MonsterTextsResult();
        var entries = new List<Dictionary<string, object?>>();
        foreach (var row in rows.OrderBy(r => r.MonsterId).ThenBy(r => r.SayId))
        {
            if (!modelKeysByMonsterId.TryGetValue(row.MonsterId, out var key) || string.IsNullOrWhiteSpace(key))
            {
                result.Problems.Add($"怪物 {row.MonsterId} 的文本号 {row.SayId} 没有 model_key：客户端无从解析，跳过");
                continue;
            }
            entries.Add(new Dictionary<string, object?>
            {
                ["modelKey"] = key.Trim(),
                ["sayId"] = row.SayId,
                ["text"] = row.Text,
                ["emote"] = row.Emote,
                ["group"] = row.Group,
                ["seq"] = row.Seq,
            });
        }

        result.Count = entries.Count;
        if (entries.Count == 0)
        {
            // 与外观表同一句总账："没有一行"与"有行但都不可解析"在玩家那边都是沉默，
            // 但在源文件侧是两件事，不能让 problems 为空被读成"一切正常"。
            result.Problems.Add("文本表里没有任何条目：进战喊话通路是通的，但没有一句可念的文案");
        }

        var payload = new Dictionary<string, object?>
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["texts"] = entries,
            ["problems"] = result.Problems,
        };
        result.Json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        return result;
    }

    /// <summary>
    /// 写文本源文件（面板/CLI 唯一的落盘出口）。
    /// <para>格式只有这一份写法：<see cref="LoadSource"/> 认得它，<c>export-content</c> 与
    /// <c>export-texts</c> 都从它读。插件那边不许自己拼 JSON——两份写法迟早与读侧分家，
    /// 而分家的表现是"面板写了、导出读不到"，策划只会认为导出坏了。</para>
    /// </summary>
    public static MonsterTextWriteReceipt WriteSource(string path, IEnumerable<MonsterTextRow> rows)
    {
        var receipt = new MonsterTextWriteReceipt { Path = path ?? string.Empty };
        if (string.IsNullOrWhiteSpace(path))
        {
            receipt.Ok = false;
            receipt.Problems.Add("没有给出文本源文件路径：什么都没写");
            return receipt;
        }
        var ordered = rows.Where(r => r.MonsterId > 0 && r.SayId > 0 && !string.IsNullOrWhiteSpace(r.Text))
                          .OrderBy(r => r.MonsterId).ThenBy(r => r.SayId).ToList();
        var payload = new Dictionary<string, object?>
        {
            ["texts"] = ordered.Select(r => new Dictionary<string, object?>
            {
                ["monsterId"] = r.MonsterId,
                ["sayId"] = r.SayId,
                ["group"] = r.Group,
                ["seq"] = r.Seq,
                ["text"] = r.Text.Trim(),
                ["emote"] = r.Emote,
            }).ToList(),
        };
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
            receipt.Ok = true;
            receipt.RowsAfter = ordered.Count;
            return receipt;
        }
        catch (Exception ex)
        {
            receipt.Ok = false;
            receipt.Problems.Add("文本源文件写入失败：" + ex.GetBaseException().Message);
            return receipt;
        }
    }

    /// <summary>
    /// 增改一行文本（<c>text</c> 为空白 = 撤销这一号）。
    /// <para>先整体读一遍再改：直接追写会造出重复号（读侧只保留第一条，第二条被静默忽略，
    /// 而策划看到的是"我改了台词而游戏里念的还是旧的"）。读侧跳过的坏行会复述进回执——
    /// 重写会把它们一起带走，这件事必须说出来而不是替用户决定。</para>
    /// <para><paramref name="group"/>：AC 的 <c>creature_text.group</c>。同一只怪同一组里的多句
    /// 由运行期用 tick 盐挑一句（`MonsterContent.ResolveSayId`）。<c>null</c> = 不改现有那一行的组
    /// （面板/CLI 只改文案时不该把组悄悄清掉）；0 = 显式移出组（不成组）。新行的组内序号自动取该组最大值 +1，
    /// 这样"组里的第几句"永远按写入顺序稳定，改文案不会把它挪到别人前面。</para>
    /// </summary>
    public static MonsterTextWriteReceipt UpsertText(string path, int monsterId, int sayId, string? text, int emote = 0, int? group = null)
    {
        var receipt = new MonsterTextWriteReceipt { Path = path ?? string.Empty };
        if (monsterId <= 0)
        {
            receipt.Ok = false;
            receipt.Problems.Add($"怪物编号 {monsterId} 非法：文本是挂在某只怪名下的，编号为空写不进去");
            return receipt;
        }
        if (group.HasValue && group.Value < 0)
        {
            receipt.Ok = false;
            receipt.Problems.Add($"台词组号 {group.Value} 非法：0 = 不成组，负数没有含义");
            return receipt;
        }
        var trimmed = (text ?? string.Empty).Trim();
        if (sayId <= 0 && trimmed.Length > 0)
        {
            receipt.Ok = false;
            receipt.Problems.Add($"文本号 {sayId} 非法：0 是「没配」的哨兵，占一行会让引用检查永远查不出悬空");
            return receipt;
        }

        var loaded = LoadSource(path);
        if (!loaded.Ok)
        {
            receipt.Ok = false;
            receipt.Problems.Add("文本源文件读不出，拒绝覆盖（否则会连带丢掉里面读得好的行）：" + string.Join("；", loaded.Problems));
            return receipt;
        }
        foreach (var p in loaded.Problems)
        {
            if (loaded.FileMissing && p.StartsWith("文本源文件不存在", StringComparison.Ordinal)) continue;
            receipt.Problems.Add("重写会把这一行一起带走（读侧本来就没采纳它）：" + p);
        }

        var kept = loaded.Rows.Where(r => !(r.MonsterId == monsterId && r.SayId == sayId)).ToList();
        var existed = kept.Count != loaded.Rows.Count;
        var previous = loaded.Rows.FirstOrDefault(r => r.MonsterId == monsterId && r.SayId == sayId);
        // 组：没显式给就沿用这一号原来的组（只改文案不该把它悄悄移出组）；新行默认 0 = 不成组。
        var effectiveGroup = group ?? (existed ? previous.Group : 0);
        receipt.Group = effectiveGroup;
        MonsterTextWriteReceipt written;
        if (trimmed.Length == 0)
        {
            receipt.Action = existed ? "delete" : "noop";
            if (!existed)
            {
                // 清空一格而它本来就空：不落盘也不算失败，但要如实说"没改"，
                // 否则面板回执里的"成功"会让人以为刚刚那次编辑生效了。
                receipt.Ok = true;
                receipt.RowsAfter = loaded.Rows.Count;
                return receipt;
            }
            written = WriteSource(path, kept);
        }
        else
        {
            // 组内序号：同一只怪同一组里的第几句（稳定序，运行期按它排）。
            // 新行取该组现有最大 +1；同一号在同一组里改文案时**保留原序号**，否则改一次词就会换位置。
            var seq = 0;
            if (effectiveGroup > 0)
            {
                var maxSeq = 0;
                foreach (var r in kept)
                {
                    if (r.MonsterId == monsterId && r.Group == effectiveGroup && r.Seq > maxSeq) maxSeq = r.Seq;
                }
                seq = existed && previous.Group == effectiveGroup && previous.Seq > 0
                    ? previous.Seq
                    : maxSeq + 1;
            }
            kept.Add(new MonsterTextRow(monsterId, sayId, effectiveGroup, seq, trimmed, emote));
            receipt.Action = "upsert";
            written = WriteSource(path, kept);
        }
        // Action 与"读侧跳过的行"住在外层那份上：WriteSource 只看这次给它的行，
        // 直接返回它会把"到底改了什么"丢掉（回执只剩个数，正是本工程栽过的那种）。
        written.Action = receipt.Action;
        written.Group = receipt.Group;
        foreach (var p in receipt.Problems) written.Problems.Add(p);
        return written;
    }
}
