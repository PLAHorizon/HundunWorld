using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 开发工作流增强工具：源码检索与文件符号大纲（只读）。
    /// 与 code_list / code_read / code_write / code_build_wait 共同构成开发闭环。
    /// </summary>
    public static class DevWorkflowTools
    {
        private const int MaxScanLines = 2000000;

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "code_search",
                Description = "在源码目录中检索内容（默认 Source，可指定 root，如 Plugins/NarrativePro/Source）。query 支持普通文本或正则（isRegex=true）。返回 file:line:text 列表（maxResults 默认50，上限200）。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"isRegex\":{\"type\":\"boolean\"},\"ignoreCase\":{\"type\":\"boolean\",\"description\":\"默认true\"},\"root\":{\"type\":\"string\",\"description\":\"相对项目根的目录，默认 Source\"},\"maxResults\":{\"type\":\"integer\"}},\"required\":[\"query\"]}",
                Execute = CodeSearchAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "code_outline",
                Description = "输出单个源码文件的符号大纲（class/struct/interface/enum 与公开/私有方法签名及行号），辅助快速导航。path 相对项目根或 Source 目录。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}",
                Execute = CodeOutlineAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "code_search_multi",
                Description = "一次调用批量检索多个字面量/正则（这是 code_search 做不到的：后者只接受单个 query，无法回答「这一批文案/锚点分别落地了没有」）。" +
                              "每个 query 返回前若干命中 file:line:text，未命中则 found=false。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"queries\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"待检索的字面量或正则列表\"}," +
                                  "\"root\":{\"type\":\"string\",\"description\":\"相对项目根的目录，默认 Source\"}," +
                                  "\"fileGlob\":{\"type\":\"string\",\"description\":\"文件名通配，默认 *.cs\"}," +
                                  "\"isRegex\":{\"type\":\"boolean\",\"description\":\"默认 false\"}," +
                                  "\"ignoreCase\":{\"type\":\"boolean\",\"description\":\"默认 true\"}," +
                                  "\"maxHitsPerQuery\":{\"type\":\"integer\",\"description\":\"每个 query 的命中上限，默认 1，上限 20\"}}," +
                                  "\"required\":[\"queries\"]}",
                Execute = CodeSearchMultiAsync
            });
        }

        // ==================== Handlers ====================

        private static Task<object> CodeSearchAsync(JsonElement args)
        {
            var query = EditorUtils.GetString(args, "query");
            if (string.IsNullOrEmpty(query))
                throw new ArgumentException("缺少 query 参数");
            var isRegex = EditorUtils.GetBool(args, "isRegex", false);
            var ignoreCase = EditorUtils.GetBool(args, "ignoreCase", true);
            var rootRel = EditorUtils.GetString(args, "root", "Source");
            var maxResults = Math.Clamp(EditorUtils.GetInt(args, "maxResults", 50), 1, 200);

            return Task.FromResult<object>(RunIo(() =>
            {
                var projectFolder = Path.GetFullPath(Globals.ProjectFolder);
                var root = Path.GetFullPath(Path.Combine(projectFolder, rootRel));
                if (!root.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("检索范围越出项目目录: " + rootRel);
                if (!Directory.Exists(root))
                    throw new ArgumentException("目录不存在: " + rootRel);

                Regex re;
                try
                {
                    var pattern = isRegex ? query : Regex.Escape(query);
                    re = new Regex(pattern,
                        (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException("正则无效: " + ex.Message);
                }

                var matches = new List<object>();
                var filesScanned = 0;
                var linesScanned = 0;

                foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                        file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                        continue;

                    filesScanned++;
                    string[] lines;
                    try { lines = File.ReadAllLines(file); } catch { continue; }

                    for (var i = 0; i < lines.Length; i++)
                    {
                        linesScanned++;
                        if (linesScanned > MaxScanLines)
                            goto Done;
                        if (!re.IsMatch(lines[i]))
                            continue;

                        var rel = file.Substring(projectFolder.Length + 1).Replace('\\', '/');
                        var text = lines[i].Trim();
                        matches.Add(new
                        {
                            file = rel,
                            line = i + 1,
                            text = text.Length > 300 ? text.Substring(0, 300) : text
                        });

                        if (matches.Count >= maxResults)
                            goto Done;
                    }
                }

                Done:
                return new
                {
                    root = rootRel,
                    filesScanned,
                    linesScanned,
                    matchCount = matches.Count,
                    truncated = matches.Count >= maxResults || linesScanned > MaxScanLines,
                    matches
                };
            }));
        }

        private static Task<object> CodeOutlineAsync(JsonElement args)
        {
            var relPath = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(relPath))
                throw new ArgumentException("缺少 path 参数");

            return Task.FromResult<object>(RunIo(() =>
            {
                var projectFolder = Path.GetFullPath(Globals.ProjectFolder);
                var full = Path.IsPathRooted(relPath)
                    ? Path.GetFullPath(relPath)
                    : Path.GetFullPath(Path.Combine(projectFolder, relPath));
                if (!full.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("路径越出项目目录: " + relPath);
                if (!File.Exists(full))
                    throw new ArgumentException("文件不存在: " + relPath);

                var lines = File.ReadAllLines(full);

                var typeRe = new Regex(
                    @"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|static|sealed|abstract|partial)\s+)*(?<kind>class|struct|interface|enum)\s+(?<name>\w+)");
                var methodRe = new Regex(
                    @"^\s*(?:public|protected|private|internal)(?:\s+static)?(?:\s+override)?(?:\s+virtual)?(?:\s+async)?(?:\s+sealed)?(?:\s+extern)?\s+[\w<>\[\],\.\?]+\s+(?<name>\w+)\s*\(");

                var types = new List<object>();
                var members = new List<object>();

                for (var i = 0; i < lines.Length && types.Count + members.Count < 400; i++)
                {
                    var line = lines[i];

                    var tm = typeRe.Match(line);
                    if (tm.Success)
                    {
                        types.Add(new { kind = tm.Groups["kind"].Value, name = tm.Groups["name"].Value, line = i + 1 });
                        continue;
                    }

                    var mm = methodRe.Match(line);
                    if (mm.Success)
                    {
                        var signature = line.Trim();
                        members.Add(new
                        {
                            name = mm.Groups["name"].Value,
                            line = i + 1,
                            signature = signature.Length > 200 ? signature.Substring(0, 200) : signature
                        });
                    }
                }

                var rel = full.Substring(projectFolder.Length + 1).Replace('\\', '/');
                return new
                {
                    path = rel,
                    totalLines = lines.Length,
                    typeCount = types.Count,
                    memberCount = members.Count,
                    types,
                    members
                };
            }));
        }

        private static object RunIo(Func<object> action)
        {
            // 文件 IO 在调用线程执行（工具运行于服务器线程），避免占用主线程
            return action();
        }

        // ==================== 批量字面量探针 ====================

        private static Task<object> CodeSearchMultiAsync(JsonElement args)
        {
            var queries = EditorUtils.GetStringArray(args, "queries");
            if (queries.Count == 0)
                throw new ArgumentException("缺少 queries 参数");
            var rootRel = EditorUtils.GetString(args, "root", "Source");
            var fileGlob = EditorUtils.GetString(args, "fileGlob", "*.cs");
            var isRegex = EditorUtils.GetBool(args, "isRegex", false);
            var ignoreCase = EditorUtils.GetBool(args, "ignoreCase", true);
            var maxHitsPerQuery = Math.Clamp(EditorUtils.GetInt(args, "maxHitsPerQuery", 1), 1, 20);

            return Task.FromResult<object>(RunIo(() =>
            {
                var projectFolder = EditorUtils.ProjectRoot;
                var root = Path.GetFullPath(Path.Combine(projectFolder, rootRel ?? "Source"));
                if (!EditorUtils.IsInside(root, projectFolder))
                    throw new UnauthorizedAccessException("检索范围越出项目目录: " + rootRel);
                if (!Directory.Exists(root))
                    throw new ArgumentException("目录不存在: " + rootRel);

                var matchers = new List<(string query, Regex regex)>();
                foreach (var query in queries)
                {
                    if (string.IsNullOrEmpty(query))
                        continue;
                    Regex re;
                    try
                    {
                        re = new Regex(isRegex ? query : Regex.Escape(query),
                            (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException("正则无效（" + query + "）: " + ex.Message);
                    }
                    matchers.Add((query, re));
                }

                var hits = matchers.ToDictionary(m => m.query, _ => new List<object>(), StringComparer.Ordinal);
                var filesScanned = 0;

                foreach (var file in Directory.EnumerateFiles(root, fileGlob, SearchOption.AllDirectories))
                {
                    var sep = Path.DirectorySeparatorChar;
                    if (file.Contains(sep + "obj" + sep) || file.Contains(sep + "bin" + sep))
                        continue;

                    filesScanned++;
                    string[] lines;
                    try { lines = File.ReadAllLines(file); } catch { continue; }
                    var rel = EditorUtils.ToDisplayPath(file);

                    for (var i = 0; i < lines.Length; i++)
                    {
                        foreach (var (query, regex) in matchers)
                        {
                            var bucket = hits[query];
                            if (bucket.Count >= maxHitsPerQuery || !regex.IsMatch(lines[i]))
                                continue;

                            var text = lines[i].Trim();
                            bucket.Add(new
                            {
                                file = rel,
                                line = i + 1,
                                text = text.Length > 300 ? text.Substring(0, 300) : text
                            });
                        }
                    }

                    if (matchers.All(m => hits[m.query].Count >= maxHitsPerQuery))
                        break;
                }

                var results = new List<object>();
                foreach (var (query, _) in matchers)
                {
                    var bucket = hits[query];
                    results.Add(new
                    {
                        query,
                        found = bucket.Count > 0,
                        hitCount = bucket.Count,
                        hits = bucket
                    });
                }

                return new
                {
                    root = rootRel,
                    filesScanned,
                    queryCount = matchers.Count,
                    missingCount = results.Count(r => !(bool)r.GetType().GetProperty("found").GetValue(r)),
                    results
                };
            }));
        }
    }
}
