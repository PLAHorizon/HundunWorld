using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 设计稿 ↔ 实现 审计：把设计 HTML 的结构/文案/交互锚点，与 C# 实现源码做覆盖比对。
    /// 用于回答「这一页设计稿里的东西落地了没有」——文案、dom-id 锚点、示例数值三类分别判定。
    ///
    /// 三类的口径差异（与既有审计脚本保持一致）：
    ///   copy  — 区分性文案（长度≥3 且含中文，或含数字）是否作为字面量出现在实现里；
    ///   domId — 设计稿 data-dom-id / id 是否在实现源码里出现；
    ///   data  — 示例数值（≥3 位数字）是否落地；实现侧常以插值/拼接生成，
    ///           因此把「值已存在、只是字面形式不同」与「值确实缺失」分开报告。
    /// </summary>
    public static class UiDesignAuditTools
    {
        private static readonly Regex TokenRegex = new Regex(@"(?s)<[^>]+>|[^<]+", RegexOptions.Compiled);
        private static readonly Regex TagNameRegex = new Regex(@"^</?\s*([A-Za-z][A-Za-z0-9:_-]*)", RegexOptions.Compiled);
        private static readonly Regex AttrRegex = new Regex(
            @"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>=]+))",
            RegexOptions.Compiled);
        private static readonly Regex NoisyRegex = new Regex(@"(?is)<(script|style|head)\b.*?</\1>", RegexOptions.Compiled);
        private static readonly Regex CommentRegex = new Regex(@"(?is)<!--.*?-->", RegexOptions.Compiled);
        private static readonly Regex BodyRegex = new Regex(@"(?is)<body[^>]*>(.*)</body>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex MarkerClassRegex = new Regex(
            "(panel|card|title|btn|button|badge|tag|tab|bar|list|grid|row|col|item|header|footer|sidebar|nav|" +
            "hud|popup|modal|dialog|toast|slot|icon|ring|avatar|portrait|progress|rail|banner|section|group|" +
            "label|value|stat|attr|cell|chip|top|bottom|left|right|center)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DecoDomIdRegex = new Regex(
            "(overlay|backdrop|scrim|glow|bg-|background|::|^page$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DataLikeRegex = new Regex(
            "^(?=.*[0-9])[\\d\\s,./%+:×xX\\-—→]*\\s*(?:\\s*(?:秒|人|层|px|ms|m|kg|个|项|次|点|/|·)\\s*)*$",
            RegexOptions.Compiled);
        private static readonly Regex NumberTokenRegex = new Regex(
            "\\d[\\d,]*(?:\\.\\d+)?\\s*(?:%|mm|ms|m|s|秒|分|时|天|人|件|级|点|GB|fps|°)?", RegexOptions.Compiled);
        private static readonly Regex CSharpLiteralRegex = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        private static readonly HashSet<string> VoidTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "br", "hr", "img", "input", "meta", "link", "source", "path", "circle", "line", "rect", "use", "svg"
        };

        private static readonly HashSet<string> StructTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "div", "section", "header", "footer", "nav", "aside", "main", "article", "ul", "ol", "li",
            "button", "a", "h1", "h2", "h3", "h4", "h5", "h6", "span", "p", "label", "input", "table",
            "tr", "td", "th", "canvas", "select", "option", "textarea"
        };

        private static readonly HashSet<string> StopCopy = new HashSet<string>(StringComparer.Ordinal)
        {
            "返回", "确定", "取消", "关闭", "下一步", "继续", "上一页", "下一页"
        };

        private const string DefaultDesignDir = "hundun-yy-ui/pages";
        private const string DefaultImplGlob = "Source/**/*.cs";

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_design_extract",
                Description = "解析设计稿 HTML，提取「结构大纲 + 文案清单 + 交互锚点(data-dom-id/id)」，供与 C# 实现比对。" +
                              "page 缺省时处理整个设计目录。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"page\":{\"type\":\"string\",\"description\":\"页名（如 combat-hud）或 .html 路径；缺省处理 dir 下全部页面\"}," +
                                  "\"dir\":{\"type\":\"string\",\"description\":\"设计稿目录，默认 hundun-yy-ui/pages\"}," +
                                  "\"mode\":{\"type\":\"string\",\"description\":\"outline/copy/domIds/all（默认 all）\"}," +
                                  "\"maxLines\":{\"type\":\"integer\",\"description\":\"每页大纲行数上限，默认 400\"}," +
                                  "\"maxCopy\":{\"type\":\"integer\",\"description\":\"每页文案条数上限，默认 400\"}," +
                                  "\"maxDomIds\":{\"type\":\"integer\",\"description\":\"每页锚点条数上限，默认 300\"}}",
                Execute = UiDesignExtractAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_design_gap",
                Description = "设计稿与实现代码的差距审计（替代一次性分析脚本）：文案覆盖率与真实缺口、交互锚点落地情况、示例数值缺失/形式差异三类。" +
                              "impl 指定「该页实现文件」的 glob，implAll 指定「全部实现文件」用于识别「已在别的页面实现」。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"page\":{\"type\":\"string\",\"description\":\"页名或 .html 路径；缺省处理 dir 下全部页面\"}," +
                                  "\"dir\":{\"type\":\"string\",\"description\":\"设计稿目录，默认 hundun-yy-ui/pages\"}," +
                                  "\"impl\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"该页实现文件 glob，默认 Source/**/*.cs\"}," +
                                  "\"implAll\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"全部实现文件 glob，默认与 impl 相同\"}," +
                                  "\"mode\":{\"type\":\"string\",\"description\":\"copy/domId/data/all（默认 all）\"}," +
                                  "\"maxItems\":{\"type\":\"integer\",\"description\":\"每类缺口列表条数上限，默认 60\"}," +
                                  "\"minCoverage\":{\"type\":\"number\",\"description\":\"只返回覆盖率低于该值的页面（0~100），默认 0 表示全部\"}}",
                Execute = UiDesignGapAsync
            });
        }

        // ==================== ui_design_extract ====================

        private static Task<object> UiDesignExtractAsync(JsonElement args)
        {
            var pageArg = EditorUtils.GetString(args, "page");
            var dirArg = EditorUtils.GetString(args, "dir", DefaultDesignDir);
            var mode = (EditorUtils.GetString(args, "mode", "all") ?? "all").ToLowerInvariant();
            var maxLines = Math.Clamp(EditorUtils.GetInt(args, "maxLines", 400), 1, 5000);
            var maxCopy = Math.Clamp(EditorUtils.GetInt(args, "maxCopy", 400), 1, 5000);
            var maxDomIds = Math.Clamp(EditorUtils.GetInt(args, "maxDomIds", 300), 1, 2000);

            return Task.FromResult<object>(RunIo(() =>
            {
                var files = ResolveDesignPages(dirArg, pageArg);
                var pages = new List<object>();

                foreach (var file in files)
                {
                    var outline = ParseHtml(File.ReadAllText(file), maxLines, maxCopy, maxDomIds);
                    var page = Path.GetFileNameWithoutExtension(file);
                    var entry = new Dictionary<string, object>
                    {
                        { "page", page },
                        { "designPath", EditorUtils.ToDisplayPath(file) }
                    };

                    if (mode == "outline" || mode == "all")
                    {
                        entry["lineCount"] = outline.Lines.Count;
                        entry["lines"] = outline.Lines;
                    }
                    if (mode == "copy" || mode == "all")
                    {
                        entry["copyCount"] = outline.Copy.Count;
                        entry["copy"] = outline.Copy;
                    }
                    if (mode == "domIds" || mode == "all")
                    {
                        entry["domIdCount"] = outline.DomIds.Count;
                        entry["domIds"] = outline.DomIds;
                    }

                    pages.Add(entry);
                }

                return new { pageCount = pages.Count, mode, pages };
            }));
        }

        // ==================== ui_design_gap ====================

        private static Task<object> UiDesignGapAsync(JsonElement args)
        {
            var pageArg = EditorUtils.GetString(args, "page");
            var dirArg = EditorUtils.GetString(args, "dir", DefaultDesignDir);
            var implGlobs = EditorUtils.GetStringArray(args, "impl");
            if (implGlobs.Count == 0)
                implGlobs = new List<string> { DefaultImplGlob };
            var implAllGlobs = EditorUtils.GetStringArray(args, "implAll");
            if (implAllGlobs.Count == 0)
                implAllGlobs = implGlobs;
            var mode = (EditorUtils.GetString(args, "mode", "all") ?? "all").ToLowerInvariant();
            var maxItems = Math.Clamp(EditorUtils.GetInt(args, "maxItems", 60), 1, 500);
            var minCoverage = GetDouble(args, "minCoverage", 0.0);

            return Task.FromResult<object>(RunIo(() =>
            {
                var designFiles = ResolveDesignPages(dirArg, pageArg);
                var implFiles = ExpandGlobs(implGlobs);
                var implAllFiles = ExpandGlobs(implAllGlobs);

                // 读取实现源码（own 集按页签名缓存，global 集整体缓存）
                var implSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var globalLiterals = new HashSet<string>(StringComparer.Ordinal);
                var globalRaw = new StringBuilder();

                foreach (var file in implAllFiles)
                {
                    var src = ReadTextSafe(file);
                    globalRaw.Append(src).Append('\n');
                }
                var globalNormalized = NormalizeForDigits(globalRaw.ToString());
                var globalDigits = DigitsOnly(globalRaw.ToString());

                var pages = new List<object>();

                foreach (var designFile in designFiles)
                {
                    var page = Path.GetFileNameWithoutExtension(designFile);
                    var outline = ParseHtml(File.ReadAllText(designFile), 2000, 2000, 1000);
                    var ownSource = new StringBuilder();

                    foreach (var file in implFiles)
                    {
                        if (!implSources.TryGetValue(file, out var src))
                        {
                            src = ReadTextSafe(file);
                            implSources[file] = src;
                        }
                        ownSource.Append(src).Append('\n');
                    }
                    var ownText = ownSource.ToString();
                    var ownLiterals = ExtractCSharpLiterals(ownText);
                    globalLiterals.UnionWith(ownLiterals);

                    var entry = new Dictionary<string, object>
                    {
                        { "page", page },
                        { "designPath", EditorUtils.ToDisplayPath(designFile) },
                        { "implFileCount", implFiles.Count }
                    };

                    var coverageOfInterest = 100.0;

                    if (mode == "copy" || mode == "all")
                    {
                        var copyResult = AnalyzeCopy(outline.Copy, ownLiterals, globalLiterals, maxItems);
                        entry["copy"] = copyResult;
                        coverageOfInterest = (double)((Dictionary<string, object>)copyResult)["coverage"];
                    }

                    if (mode == "domId" || mode == "all")
                        entry["domIds"] = AnalyzeDomIds(outline.DomIds, ownText, maxItems);

                    if (mode == "data" || mode == "all")
                        entry["data"] = AnalyzeData(outline.Copy, ownText, globalNormalized, globalDigits, maxItems);

                    if (mode == "copy" || mode == "all")
                    {
                        if (minCoverage > 0 && coverageOfInterest >= minCoverage)
                            continue;
                    }

                    pages.Add(entry);
                }

                return new
                {
                    mode,
                    designDir = dirArg,
                    implGlobs,
                    implFileCount = implFiles.Count,
                    implAllFileCount = implAllFiles.Count,
                    pageCount = pages.Count,
                    pages
                };
            }));
        }

        /// <summary>文案覆盖：区分「真实缺口」与「设计稿示例数值」。</summary>
        private static object AnalyzeCopy(List<string> copy, HashSet<string> ownLiterals, HashSet<string> globalLiterals, int maxItems)
        {
            var distinct = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in copy)
            {
                var text = raw.Trim();
                if (text.Length == 0 || StopCopy.Contains(text) || !seen.Add(text))
                    continue;
                distinct.Add(text);
            }

            var significant = distinct.Where(IsSignificantCopy).ToList();
            var hit = new List<string>();
            var dataGaps = new List<string>();
            var realGaps = new List<string>();
            var globalHits = new List<string>();

            foreach (var text in significant)
            {
                if (ownLiterals.Contains(text))
                {
                    hit.Add(text);
                    continue;
                }

                if (IsDataLike(text))
                {
                    dataGaps.Add(text);
                    continue;
                }

                realGaps.Add(text);
                if (globalLiterals.Contains(text))
                    globalHits.Add(text);
            }

            var coverage = significant.Count == 0 ? 100.0 : Math.Round(100.0 * hit.Count / significant.Count, 1);

            return new Dictionary<string, object>
            {
                { "distinctCopy", distinct.Count },
                { "significantCount", significant.Count },
                { "hitCount", hit.Count },
                { "coverage", coverage },
                { "realGapCount", realGaps.Count },
                { "realGaps", Take(realGaps, maxItems) },
                { "dataGapCount", dataGaps.Count },
                { "dataGaps", Take(dataGaps, maxItems) },
                { "globalHitCount", globalHits.Count },
                { "globalHits", Take(globalHits, maxItems) }
            };
        }

        /// <summary>交互锚点落地：设计稿 dom-id 是否作为字符串字面量出现在实现里。</summary>
        private static object AnalyzeDomIds(List<string> domIds, string ownText, int maxItems)
        {
            var anchors = domIds
                .Where(id => !string.IsNullOrEmpty(id) && !DecoDomIdRegex.IsMatch(id))
                .Distinct()
                .ToList();

            var missing = anchors
                .Where(id => !ownText.Contains("\"" + id + "\"") && !ownText.Contains("'" + id + "'"))
                .ToList();

            return new
            {
                total = anchors.Count,
                landed = anchors.Count - missing.Count,
                missingCount = missing.Count,
                missing = Take(missing, maxItems)
            };
        }

        /// <summary>示例数值：把设计稿数值归一化为纯数字后查找，区分「值缺失」与「形式差异」。</summary>
        private static object AnalyzeData(List<string> copy, string ownText, string globalNormalized, string globalDigits, int maxItems)
        {
            var tokens = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in copy)
            {
                foreach (Match m in NumberTokenRegex.Matches(text))
                {
                    var token = m.Value.Trim();
                    if (token.Length > 0 && seen.Add(token))
                        tokens.Add(token);
                }
            }

            var missing = new List<string>();
            var formOnly = new List<string>();
            var considered = 0;

            foreach (var token in tokens)
            {
                var digits = DigitsOnly(token);
                if (digits.Length < 3)
                    continue;
                considered++;

                // 值以「原始形式」或「任意拼接形式」出现都算已落地
                if (globalNormalized.Contains(digits) || globalDigits.Contains(digits))
                    formOnly.Add(token);
                else
                    missing.Add(token);
            }

            return new
            {
                considered,
                missingCount = missing.Count,
                missing = Take(missing, maxItems),
                formOnlyCount = formOnly.Count,
                formOnly = Take(formOnly, maxItems)
            };
        }

        // ==================== HTML 解析 ====================

        private sealed class HtmlOutline
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<string> Copy = new List<string>();
            public readonly List<string> DomIds = new List<string>();
        }

        private static HtmlOutline ParseHtml(string source, int maxLines, int maxCopy, int maxDomIds)
        {
            var outline = new HtmlOutline();
            if (string.IsNullOrEmpty(source))
                return outline;

            var bodyMatch = BodyRegex.Match(source);
            var body = bodyMatch.Success ? bodyMatch.Groups[1].Value : source;
            body = NoisyRegex.Replace(body, " ");
            body = CommentRegex.Replace(body, " ");

            var stack = new List<string>();
            var bufferOwner = -1;

            foreach (Match token in TokenRegex.Matches(body))
            {
                if (!token.Value.StartsWith("<", StringComparison.Ordinal))
                {
                    var text = WhitespaceRegex.Replace(token.Value, " ").Trim();
                    if (text.Length == 0)
                        continue;

                    if (bufferOwner >= 0 && bufferOwner < outline.Lines.Count)
                    {
                        var current = outline.Lines[bufferOwner];
                        if (!current.EndsWith("|" + text, StringComparison.Ordinal))
                            outline.Lines[bufferOwner] = current + " | " + text;
                    }

                    if ((HasCjk(text) || text.Length > 3) && outline.Copy.Count < maxCopy)
                        outline.Copy.Add(text);
                    continue;
                }

                var nameMatch = TagNameRegex.Match(token.Value);
                if (!nameMatch.Success)
                    continue;

                var name = nameMatch.Groups[1].Value.ToLowerInvariant();
                var isClose = token.Value.StartsWith("</", StringComparison.Ordinal);

                if (isClose)
                {
                    for (var i = stack.Count - 1; i >= 0; i--)
                    {
                        var open = stack[i];
                        stack.RemoveAt(i);
                        if (open == name)
                            break;
                    }
                    continue;
                }

                stack.Add(name);
                if (VoidTags.Contains(name))
                    continue;

                var attrs = ParseAttributes(token.Value);
                var cssClass = attrs.TryGetValue("class", out var cls) ? cls : null;
                var domId = attrs.TryGetValue("data-dom-id", out var dom) ? dom
                    : attrs.TryGetValue("id", out var id) ? id : null;

                var keep = StructTags.Contains(name) || !string.IsNullOrWhiteSpace(cssClass) || !string.IsNullOrEmpty(domId);
                if (!keep || outline.Lines.Count >= maxLines)
                    continue;

                var indent = new string(' ', Math.Min(stack.Count, 40) * 2);
                var label = name;

                if (!string.IsNullOrEmpty(domId))
                {
                    label += " #" + domId;
                    if (outline.DomIds.Count < maxDomIds)
                        outline.DomIds.Add(domId);
                }

                var marked = ShortMarkerClasses(cssClass);
                if (marked != null)
                    label += " ." + marked;

                if (attrs.TryGetValue("aria-label", out var aria) && !string.IsNullOrEmpty(aria))
                    label += " [aria=\"" + aria + "\"]";

                outline.Lines.Add(indent + label);
                bufferOwner = outline.Lines.Count - 1;
            }

            return outline;
        }

        private static Dictionary<string, string> ParseAttributes(string rawTag)
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var nameEnd = rawTag.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            var rest = nameEnd >= 0 ? rawTag.Substring(nameEnd) : "";
            foreach (Match m in AttrRegex.Matches(rest))
            {
                var value = m.Groups[2].Success ? m.Groups[2].Value
                    : m.Groups[3].Success ? m.Groups[3].Value
                    : m.Groups[4].Value;
                attrs[m.Groups[1].Value] = value;
            }

            return attrs;
        }

        private static string ShortMarkerClasses(string cssClass)
        {
            if (string.IsNullOrWhiteSpace(cssClass))
                return null;

            var marked = cssClass
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(c => MarkerClassRegex.IsMatch(c))
                .ToList();

            if (marked.Count == 0)
                return null;

            var joined = string.Join(" ", marked);
            return joined.Length > 80 ? joined.Substring(0, 80) : joined;
        }

        // ==================== 文案口径 ====================

        private static bool HasCjk(string text)
        {
            foreach (var c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                    return true;
            }
            return false;
        }

        private static bool IsSignificantCopy(string text)
        {
            if (text.Length >= 3 && HasCjk(text))
                return true;
            foreach (var c in text)
            {
                if (char.IsDigit(c))
                    return true;
            }
            return false;
        }

        /// <summary>判断文案是否属于「设计稿示例数值」（运行时由数据/插值生成，不计入文案缺口）。</summary>
        private static bool IsDataLike(string text)
        {
            var trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0)
                return false;
            if (DataLikeRegex.IsMatch(trimmed))
                return true;

            var stripped = Regex.Replace(trimmed, "[0-9]+", "");
            stripped = Regex.Replace(stripped, "[\\s,./%+:×xX\\-—→·秒人层项次点个]", "");
            var hasDigit = trimmed.Any(char.IsDigit);
            return stripped.Length <= 3 && hasDigit;
        }

        private static string DigitsOnly(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (char.IsDigit(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>去掉空白/逗号/引号，便于 "12,450" 与 12450 互相匹配。</summary>
        private static string NormalizeForDigits(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c) || c == ',' || c == '"' || c == '\'')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static HashSet<string> ExtractCSharpLiterals(string source)
        {
            var literals = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in CSharpLiteralRegex.Matches(source))
            {
                var value = m.Groups[1].Value.Trim();
                if (value.Length > 0)
                    literals.Add(value);
            }
            return literals;
        }

        // ==================== 文件与 glob ====================

        private static List<string> ResolveDesignPages(string dirArg, string pageArg)
        {
            if (!string.IsNullOrEmpty(pageArg))
            {
                var direct = EditorUtils.ResolveWorkspacePath(pageArg);
                if (File.Exists(direct))
                    return new List<string> { direct };

                var byDir = EditorUtils.ResolveWorkspacePath(Path.Combine(dirArg ?? DefaultDesignDir, pageArg + ".html"));
                if (File.Exists(byDir))
                    return new List<string> { byDir };

                var byDirRaw = EditorUtils.ResolveWorkspacePath(Path.Combine(dirArg ?? DefaultDesignDir, pageArg));
                if (File.Exists(byDirRaw))
                    return new List<string> { byDirRaw };

                throw new ArgumentException("找不到设计稿页面: " + pageArg);
            }

            var dir = EditorUtils.ResolveWorkspacePath(dirArg ?? DefaultDesignDir, true);
            if (!Directory.Exists(dir))
                throw new ArgumentException("设计稿目录不存在: " + dirArg);

            var files = Directory.GetFiles(dir, "*.html").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (files.Count == 0)
                throw new ArgumentException("设计稿目录下没有 .html: " + dirArg);
            return files;
        }

        private static List<string> ExpandGlobs(List<string> globs)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var glob in globs)
            {
                if (string.IsNullOrWhiteSpace(glob))
                    continue;

                var normalized = glob.Replace('\\', '/');
                var wildcardIndex = normalized.IndexOfAny(new[] { '*', '?' });

                string basePart;
                string patternPart;
                if (wildcardIndex < 0)
                {
                    basePart = normalized;
                    patternPart = "";
                }
                else
                {
                    var lastSlash = normalized.LastIndexOf('/', wildcardIndex);
                    basePart = lastSlash >= 0 ? normalized.Substring(0, lastSlash) : "";
                    patternPart = lastSlash >= 0 ? normalized.Substring(lastSlash + 1) : normalized;
                }

                var baseFull = string.IsNullOrEmpty(basePart)
                    ? EditorUtils.WorkspaceRoot
                    : EditorUtils.ResolveWorkspacePath(basePart);

                if (File.Exists(baseFull))
                {
                    if (seen.Add(baseFull))
                        results.Add(baseFull);
                    continue;
                }

                if (!Directory.Exists(baseFull))
                    continue;

                if (string.IsNullOrEmpty(patternPart))
                {
                    foreach (var file in SafeEnumerate(baseFull))
                    {
                        if (seen.Add(file))
                            results.Add(file);
                    }
                    continue;
                }

                var regex = GlobToRegex(patternPart);
                foreach (var file in SafeEnumerate(baseFull))
                {
                    var relative = Path.GetRelativePath(baseFull, file).Replace('\\', '/');
                    if (regex.IsMatch(relative) && seen.Add(file))
                        results.Add(file);
                }
            }

            return results;
        }

        private static IEnumerable<string> SafeEnumerate(string root)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
            }
            catch
            {
                yield break;
            }

            foreach (var file in files)
            {
                var sep = Path.DirectorySeparatorChar;
                if (file.Contains(sep + "obj" + sep) || file.Contains(sep + "bin" + sep) ||
                    file.Contains(sep + ".git" + sep) || file.Contains(sep + "Cache" + sep))
                    continue;
                yield return file;
            }
        }

        private static Regex GlobToRegex(string pattern)
        {
            var sb = new StringBuilder("^");
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (c == '*')
                {
                    var isDoubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
                    if (isDoubleStar)
                    {
                        i++;
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                        {
                            i++;
                            sb.Append("(?:.*/)?");
                        }
                        else
                        {
                            sb.Append(".*");
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }
            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string ReadTextSafe(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch
            {
                return "";
            }
        }

        private static List<string> Take(List<string> items, int max)
        {
            return items.Count > max ? items.Take(max).ToList() : items;
        }

        private static double GetDouble(JsonElement obj, string name, double defaultValue)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetDouble();
            return defaultValue;
        }

        private static object RunIo(Func<object> action)
        {
            // 纯文件 IO，在服务器线程执行，不占用编辑器主线程
            return action();
        }
    }
}
