using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 日志诊断工具：读取项目 Logs 目录下的运行日志（编辑器/游戏共用，Log_*.txt），
    /// 支持尾部读取、跨文件正则扫描与错误归组，供 AI 在「运行 → 观察 → 修正」循环中诊断问题。
    /// 日志行格式：[ 00:00:00.012 ]: [Info|Warning|Error|Fatal] ...
    ///
    /// 编码注意：日志以 UTF-16LE 写入（带 BOM），但「增强日志桥」等少数写入方会把 UTF-8 字节
    /// 直接写进同一条流，读出来就成了乱码。这类行可用「按 UTF-16LE 编回字节 → 按 UTF-8 解码」
    /// 还原（见 <see cref="RepairMixedLine"/>），<c>log_scan</c> / <c>log_tail</c> 默认开启。
    /// </summary>
    public static class LogTools
    {
        private const int MaxTailLines = 500;
        private const int MaxFilterScanLines = 3000;
        private const int MaxReadBytes = 64 * 1024 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "log_tail",
                Description = "读取最新（或指定）运行日志的尾部内容。支持 filter（error/warning/info/all）与 keyword 过滤，lines 默认80（上限500）。" +
                              "自动识别 UTF-16/UTF-8 编码并还原混合编码乱码行。编辑器运行中亦可读取。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"lines\":{\"type\":\"integer\",\"description\":\"返回行数，默认80，上限500\"}," +
                                  "\"filter\":{\"type\":\"string\",\"description\":\"all（默认）/error/warning/info\"}," +
                                  "\"keyword\":{\"type\":\"string\",\"description\":\"可选关键字过滤\"}," +
                                  "\"file\":{\"type\":\"string\",\"description\":\"可选，日志文件名或路径，缺省取最新\"}," +
                                  "\"encoding\":{\"type\":\"string\",\"description\":\"auto（默认）/utf8/utf16\"}," +
                                  "\"repairMixed\":{\"type\":\"boolean\",\"description\":\"是否还原混合编码乱码行，默认 true\"}}",
                Execute = LogTailAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "log_errors",
                Description = "扫描最近 N 个（默认3）运行日志，提取错误/致命错误行及堆栈上下文，按消息模式归组摘要（含计数与首个样例）。用于快速定位运行问题。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"files\":{\"type\":\"integer\",\"description\":\"扫描文件数，默认3，上限10\"},\"includeWarnings\":{\"type\":\"boolean\",\"description\":\"是否包含Warning，默认false\"},\"maxGroups\":{\"type\":\"integer\",\"description\":\"摘要组数上限，默认20\"}}}",
                Execute = LogErrorsAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "log_scan",
                Description = "跨多个历史日志做正则/关键字扫描（这是 log_tail 做不到的：log_tail 只看最新日志的尾部）。返回 file:line:内容，可选上下文行与按归一化文本归组计数。" +
                              "自动识别编码并还原混合编码乱码行。用于追踪「某个状态在多次运行里出现过几次 / 上次是什么时候」。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"pattern\":{\"type\":\"string\",\"description\":\"检索内容\"}," +
                                  "\"isRegex\":{\"type\":\"boolean\",\"description\":\"是否按正则解释，默认 true\"}," +
                                  "\"ignoreCase\":{\"type\":\"boolean\",\"description\":\"默认 true\"}," +
                                  "\"file\":{\"type\":\"string\",\"description\":\"只扫单个日志（文件名或路径）\"}," +
                                  "\"files\":{\"type\":\"integer\",\"description\":\"扫描最近 N 个日志，默认 3，上限 50；传 0 表示全部\"}," +
                                  "\"contextLines\":{\"type\":\"integer\",\"description\":\"每条命中附加的后续上下文行数，默认 0，上限 5\"}," +
                                  "\"maxMatches\":{\"type\":\"integer\",\"description\":\"命中上限，默认 100，上限 500\"}," +
                                  "\"group\":{\"type\":\"boolean\",\"description\":\"是否按归一化文本归组计数，默认 false\"}," +
                                  "\"encoding\":{\"type\":\"string\",\"description\":\"auto（默认）/utf8/utf16\"}," +
                                  "\"repairMixed\":{\"type\":\"boolean\",\"description\":\"是否还原混合编码乱码行，默认 true\"}}," +
                                  "\"required\":[\"pattern\"]}",
                Execute = LogScanAsync
            });
        }

        // ==================== Handlers ====================

        private static Task<object> LogTailAsync(JsonElement args)
        {
            var lines = Math.Clamp(EditorUtils.GetInt(args, "lines", 80), 1, MaxTailLines);
            var filter = EditorUtils.GetString(args, "filter");
            var keyword = EditorUtils.GetString(args, "keyword");
            var fileArg = EditorUtils.GetString(args, "file");
            var encodingKind = EditorUtils.GetString(args, "encoding", "auto");
            var repairMixed = EditorUtils.GetBool(args, "repairMixed", true);

            return Task.FromResult<object>(RunIo(() =>
            {
                var full = ResolveLogFile(fileArg);

                var hasFilter = !string.IsNullOrEmpty(filter) &&
                                !filter.Equals("all", StringComparison.OrdinalIgnoreCase);
                var hasKeyword = !string.IsNullOrEmpty(keyword);
                var scanLines = (hasFilter || hasKeyword) ? MaxFilterScanLines : lines;

                var tail = ReadLines(full, encodingKind, repairMixed, MaxReadBytes, out var encodingUsed);
                if (tail.Count > scanLines)
                    tail = tail.Skip(tail.Count - scanLines).ToList();

                var filtered = new List<string>();
                foreach (var line in tail)
                {
                    if (hasFilter && !MatchesLevel(line, filter))
                        continue;
                    if (hasKeyword && line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    filtered.Add(line);
                }

                var returned = filtered.Count > lines
                    ? filtered.Skip(filtered.Count - lines).ToList()
                    : filtered;

                return new
                {
                    file = full,
                    encoding = encodingUsed,
                    totalScanned = tail.Count,
                    matched = filtered.Count,
                    returnedCount = returned.Count,
                    lines = returned
                };
            }));
        }

        private static Task<object> LogErrorsAsync(JsonElement args)
        {
            var fileCount = Math.Clamp(EditorUtils.GetInt(args, "files", 3), 1, 10);
            var includeWarnings = EditorUtils.GetBool(args, "includeWarnings", false);
            var maxGroups = Math.Clamp(EditorUtils.GetInt(args, "maxGroups", 20), 1, 50);

            return Task.FromResult<object>(RunIo(() =>
            {
                var files = RecentLogFiles(fileCount);
                var groups = new Dictionary<string, ErrorGroup>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    var all = ReadLines(file.FullName, "auto", true, MaxReadBytes, out _);
                    for (var i = 0; i < all.Count; i++)
                    {
                        var line = all[i];
                        var isError = line.Contains("]: [Error]") || line.Contains("]: [Fatal]");
                        var isWarning = includeWarnings && line.Contains("]: [Warning]");
                        if (!isError && !isWarning)
                            continue;

                        // 收集后续上下文行（直到下一条带时间戳的行），捕获堆栈
                        var context = new List<string>();
                        for (var j = i + 1; j < all.Count && context.Count < 12; j++)
                        {
                            var next = all[j];
                            if (IsLogHeader(next))
                                break;
                            context.Add(next.TrimEnd());
                        }

                        var key = Normalize(line);
                        if (!groups.TryGetValue(key, out var group))
                        {
                            group = new ErrorGroup { Sample = line, File = file.Name };
                            groups[key] = group;
                        }
                        group.Count++;
                        if (group.Context.Count == 0 && context.Count > 0)
                            group.Context = context;
                    }
                }

                var sorted = groups.Values
                    .OrderByDescending(g => g.Count)
                    .Take(maxGroups)
                    .Select(g => new
                    {
                        message = g.Sample,
                        count = g.Count,
                        firstFile = g.File,
                        context = g.Context
                    })
                    .ToList();

                return new
                {
                    filesScanned = files.Select(f => f.Name).ToList(),
                    totalErrorGroups = groups.Count,
                    showing = sorted.Count,
                    errors = sorted
                };
            }));
        }

        private static Task<object> LogScanAsync(JsonElement args)
        {
            var pattern = EditorUtils.GetString(args, "pattern");
            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("缺少 pattern 参数");

            var isRegex = EditorUtils.GetBool(args, "isRegex", true);
            var ignoreCase = EditorUtils.GetBool(args, "ignoreCase", true);
            var fileArg = EditorUtils.GetString(args, "file");
            var fileCount = Math.Clamp(EditorUtils.GetInt(args, "files", 3), 0, 50);
            var contextLines = Math.Clamp(EditorUtils.GetInt(args, "contextLines", 0), 0, 5);
            var maxMatches = Math.Clamp(EditorUtils.GetInt(args, "maxMatches", 100), 1, 500);
            var group = EditorUtils.GetBool(args, "group", false);
            var encodingKind = EditorUtils.GetString(args, "encoding", "auto");
            var repairMixed = EditorUtils.GetBool(args, "repairMixed", true);

            return Task.FromResult<object>(RunIo(() =>
            {
                Regex re;
                try
                {
                    re = new Regex(isRegex ? pattern : Regex.Escape(pattern),
                        (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException("正则无效: " + ex.Message);
                }

                var files = string.IsNullOrEmpty(fileArg)
                    ? RecentLogFiles(fileCount == 0 ? int.MaxValue : fileCount)
                    : new List<FileInfo> { new FileInfo(ResolveLogFile(fileArg)) };

                var matches = new List<object>();
                var groups = new Dictionary<string, ScanGroup>(StringComparer.OrdinalIgnoreCase);
                var scanned = new List<object>();
                var totalMatches = 0;
                var truncated = false;

                foreach (var file in files)
                {
                    if (file == null || !file.Exists)
                        continue;

                    var lines = ReadLines(file.FullName, encodingKind, repairMixed, MaxReadBytes, out var encodingUsed);
                    var fileHits = 0;

                    for (var i = 0; i < lines.Count; i++)
                    {
                        if (!re.IsMatch(lines[i]))
                            continue;

                        fileHits++;
                        totalMatches++;
                        var text = Trim(lines[i], 400);

                        if (matches.Count < maxMatches)
                        {
                            List<string> context = null;
                            if (contextLines > 0)
                            {
                                context = new List<string>();
                                for (var j = i + 1; j < lines.Count && context.Count < contextLines; j++)
                                    context.Add(Trim(lines[j], 300));
                            }

                            matches.Add(new
                            {
                                file = file.Name,
                                line = i + 1,
                                text,
                                context
                            });
                        }
                        else
                        {
                            truncated = true;
                        }

                        if (group)
                        {
                            var key = Normalize(text);
                            if (!groups.TryGetValue(key, out var g))
                            {
                                g = new ScanGroup { Sample = text, File = file.Name };
                                groups[key] = g;
                            }
                            g.Count++;
                        }
                    }

                    scanned.Add(new { file = file.Name, encoding = encodingUsed, lineCount = lines.Count, hits = fileHits });
                }

                var groupList = groups.Values
                    .OrderByDescending(g => g.Count)
                    .Take(50)
                    .Select(g => new { sample = g.Sample, count = g.Count, firstFile = g.File })
                    .ToList();

                return new
                {
                    pattern,
                    filesScanned = scanned,
                    totalMatches,
                    truncated,
                    matches,
                    groups = groupList
                };
            }));
        }

        // ==================== 内部辅助 ====================

        private sealed class ErrorGroup
        {
            public string Sample;
            public string File;
            public int Count;
            public List<string> Context = new List<string>();
        }

        private sealed class ScanGroup
        {
            public string Sample;
            public string File;
            public int Count;
        }

        private static string LogDir => Path.Combine(Globals.ProjectFolder, "Logs");

        private static List<FileInfo> RecentLogFiles(int count)
        {
            var dir = LogDir;
            if (!Directory.Exists(dir))
                throw new InvalidOperationException("日志目录不存在: " + dir);

            var files = new DirectoryInfo(dir).GetFiles("Log_*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (files.Count == 0)
                throw new InvalidOperationException("未找到日志文件（Log_*.txt）");

            if (count < files.Count)
                files = files.Take(count).ToList();
            return files;
        }

        private static string ResolveLogFile(string fileArg)
        {
            if (string.IsNullOrEmpty(fileArg))
            {
                var latest = RecentLogFiles(1)[0];
                return latest.FullName;
            }

            if (Path.IsPathRooted(fileArg) && File.Exists(fileArg))
                return fileArg;

            var inLogDir = Path.Combine(LogDir, fileArg);
            if (File.Exists(inLogDir))
                return inLogDir;

            var inProject = Path.Combine(Globals.ProjectFolder, fileArg);
            if (File.Exists(inProject))
                return inProject;

            throw new ArgumentException("日志文件不存在: " + fileArg);
        }

        /// <summary>
        /// 读取日志尾部最多 maxBytes 字节（FileShare 允许编辑器运行中读取），
        /// 自动或按需确定编码，并还原混合编码行。
        /// </summary>
        private static List<string> ReadLines(string fullPath, string encodingKind, bool repairMixed, long maxBytes, out string encodingUsed)
        {
            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var encoding = ResolveEncoding(fs, encodingKind, out var bomLength, out encodingUsed);

                var start = Math.Max(bomLength, fs.Length - maxBytes);
                // UTF-16 必须按码元对齐，否则整段错位
                if (encoding is UnicodeEncoding && (start - bomLength) % 2 != 0)
                    start--;

                fs.Seek(start, SeekOrigin.Begin);

                var all = new List<string>();
                using (var reader = new StreamReader(fs, encoding, false))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        all.Add(repairMixed ? RepairMixedLine(line) : line);
                }

                // 丢弃可能被截断的首行
                if (start > bomLength && all.Count > 0)
                    all.RemoveAt(0);

                return all;
            }
        }

        /// <summary>
        /// 探测编码：BOM 优先，其次看「每第二个字节为 0」判断 UTF-16LE，其余按 UTF-8。
        /// </summary>
        private static Encoding ResolveEncoding(FileStream fs, string kind, out int bomLength, out string name)
        {
            var normalized = (kind ?? "auto").Trim().ToLowerInvariant();
            if (normalized == "utf8")
            {
                bomLength = ReadBomLength(fs, new byte[] { 0xEF, 0xBB, 0xBF });
                name = "utf8";
                return new UTF8Encoding(false);
            }
            if (normalized == "utf16")
            {
                bomLength = ReadBomLength(fs, new byte[] { 0xFF, 0xFE });
                name = "utf16le";
                return Encoding.Unicode;
            }

            var probe = new byte[4];
            var read = fs.Read(probe, 0, probe.Length);
            fs.Seek(0, SeekOrigin.Begin);

            if (read >= 3 && probe[0] == 0xEF && probe[1] == 0xBB && probe[2] == 0xBF)
            {
                bomLength = 3;
                name = "utf8";
                return new UTF8Encoding(false);
            }
            if (read >= 2 && probe[0] == 0xFF && probe[1] == 0xFE)
            {
                bomLength = 2;
                name = "utf16le";
                return Encoding.Unicode;
            }
            if (read >= 2 && probe[0] == 0xFE && probe[1] == 0xFF)
            {
                bomLength = 2;
                name = "utf16be";
                return Encoding.BigEndianUnicode;
            }

            // 无 BOM：ASCII 文本走 UTF-16 时高位字节为 0
            if (read >= 2 && probe[1] == 0)
            {
                bomLength = 0;
                name = "utf16le(no-bom)";
                return Encoding.Unicode;
            }

            bomLength = 0;
            name = "utf8";
            return new UTF8Encoding(false);
        }

        private static int ReadBomLength(FileStream fs, byte[] bom)
        {
            var probe = new byte[bom.Length];
            var read = fs.Read(probe, 0, probe.Length);
            fs.Seek(0, SeekOrigin.Begin);
            if (read != bom.Length)
                return 0;
            for (var i = 0; i < bom.Length; i++)
                if (probe[i] != bom[i])
                    return 0;
            return bom.Length;
        }

        /// <summary>
        /// 还原混合编码行：把该行按 UTF-16LE 编回字节，再按 UTF-8 严格解码。
        /// 严格解码失败说明这本来就是正常的 UTF-16 行，原样返回。
        /// </summary>
        private static string RepairMixedLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return line;

            try
            {
                var bytes = Encoding.Unicode.GetBytes(line);
                var repaired = StrictUtf8.GetString(bytes);
                return repaired.Length > 0 ? repaired : line;
            }
            catch (DecoderFallbackException)
            {
                return line;
            }
        }

        private static bool MatchesLevel(string line, string filter)
        {
            switch ((filter ?? "").ToLowerInvariant())
            {
                case "error":
                    return line.Contains("]: [Error]") || line.Contains("]: [Fatal]");
                case "warning":
                    return line.Contains("]: [Warning]");
                case "info":
                    return line.Contains("]: [Info]");
                default:
                    return true;
            }
        }

        private static bool IsLogHeader(string line)
        {
            return line.Length > 12 && line[0] == '[' && line.IndexOf("]: [", StringComparison.Ordinal) > 0;
        }

        /// <summary>归一化错误消息（数字折叠为 #）用于归组。</summary>
        private static string Normalize(string line)
        {
            var idx = line.IndexOf("]: ", StringComparison.Ordinal);
            var msg = idx >= 0 ? line.Substring(idx + 3) : line;

            var sb = new StringBuilder(msg.Length);
            foreach (var ch in msg)
            {
                if (char.IsDigit(ch))
                {
                    if (sb.Length == 0 || sb[sb.Length - 1] != '#')
                        sb.Append('#');
                }
                else
                {
                    sb.Append(ch);
                }
            }

            return sb.Length > 200 ? sb.ToString(0, 200) : sb.ToString();
        }

        private static string Trim(string text, int max)
        {
            text = text?.Trim() ?? "";
            return text.Length > max ? text.Substring(0, max) : text;
        }

        private static object RunIo(Func<object> action)
        {
            // 文件 IO 在调用线程执行（工具运行于服务器线程），避免占用主线程
            return action();
        }
    }
}
