using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 资产二进制取证：不依赖内容数据库、直接解 .flax 文件字节。
    /// 用于诊断「导入后模型躺倒 / 尺度异常 / 资产引用对不上」这类问题——
    /// 现场常常是内容数据库里已没有的 .backup / 损坏副本，只能看原始字节。
    ///
    /// 文件格式（FlaxStorage v9，已对照引擎源码 Source/Engine/Content/Storage/FlaxStorage.cpp 核实）：
    ///   0    int32  MagicCode(1180124739)
    ///   4    uint32 Version(=9)
    ///   8    CustomData { int32 ContentKey; int32[3] 未用 }        —— 16 字节
    ///   24   int32  AssetsCount
    ///   28   Entry[AssetsCount] { Guid(16) TypeName(char16[64]) Address(uint32) }  —— 每条 148 字节
    ///        int32  ChunksCount
    ///        Chunk[ChunksCount] { uint32 Address; uint32 Size; int32 Flags }       —— 每条 12 字节
    ///        AssetHeader[AssetsCount] @Entry.Address:
    ///            Guid(16) TypeName(char16[64]) SerializedVersion(uint32)
    ///            ChunkMapping(int32[16]) CustomData(len+bytes) HashCode(uint32)
    ///            Metadata(len+json) Dependencies(count + count*24)
    /// 其中 Metadata 就是导入设置 JSON（ImportPath / Scale / Rotation / ImportLODs / Type ...），
    /// 也就是「采样值」——模型躺倒与尺度异常的根因通常就写在这里。
    /// </summary>
    public static class AssetForensicsTools
    {
        private const int StorageMagicCode = 1180124739;
        private const int SupportedStorageVersion = 9;
        private const int TypeNameChars = 64;
        private const int AssetDataChunks = 16;
        private const int EntrySize = 16 + TypeNameChars * 2 + 4;
        private const long MaxFullScanBytes = 64L * 1024 * 1024;
        private const long MaxHeadBytes = 8L * 1024 * 1024;

        private static readonly string[] DefaultKeywords =
        {
            "FlaxEngine.", "SkinnedModel", "Model", "AnimationGraph", "Material", "Texture",
            "ImportPath", "Scale", "Rotation", "Translation"
        };

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "asset_inspect",
                Description = "直接解析 .flax 资产文件的二进制内容（不依赖内容数据库，可用于 .backup/损坏副本）：存储头与版本、资产 Guid 与类型名、" +
                              "内嵌的导入设置 JSON（ImportPath/Scale/Rotation/ImportLODs 等，即「模型为何躺倒/尺度异常」的根因所在）、浮点值直方图（定位异常缩放常量）、" +
                              "可打印字符串与关键字计数。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"path\":{\"type\":\"string\",\"description\":\"工程内相对路径 / 工作区相对路径 / 绝对路径\"}," +
                                  "\"keywords\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"额外统计出现次数的关键字，缺省使用一组资产类型名\"}," +
                                  "\"includeStrings\":{\"type\":\"boolean\",\"description\":\"是否提取可打印字符串，默认 true\"}," +
                                  "\"includeFloats\":{\"type\":\"boolean\",\"description\":\"是否统计浮点值直方图，默认 true\"}," +
                                  "\"floatMin\":{\"type\":\"number\",\"description\":\"浮点直方图下界(不含)，默认 0.0001\"}," +
                                  "\"floatMax\":{\"type\":\"number\",\"description\":\"浮点直方图上界(不含)，默认 100000\"}," +
                                  "\"maxStrings\":{\"type\":\"integer\",\"description\":\"字符串条数上限，默认 40\"}," +
                                  "\"maxFloats\":{\"type\":\"integer\",\"description\":\"浮点分组数上限，默认 25\"}}," +
                                  "\"required\":[\"path\"]}",
                Execute = AssetInspectAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "asset_guid_find",
                Description = "在资产文件（含 .backup 副本）中按字节定位 Guid，用于「某资产引用的 Guid 指向谁 / 是否已失效」。自动尝试两种字节序变体：" +
                              "string（D 格式字面顺序）与 le（Guid.ToByteArray 的混合端序）。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"guids\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"一个或多个 Guid（D 格式或 32 位十六进制）\"}," +
                                  "\"root\":{\"type\":\"string\",\"description\":\"搜索根目录，默认 Content（工程内相对）\"}," +
                                  "\"extensions\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"文件后缀过滤，如 ['.flax']；缺省扫描全部文件\"}," +
                                  "\"includeBackups\":{\"type\":\"boolean\",\"description\":\"是否包含 .backup/.bak 等副本，默认 true\"}," +
                                  "\"maxHits\":{\"type\":\"integer\",\"description\":\"命中条数上限，默认 50\"}," +
                                  "\"maxFileBytes\":{\"type\":\"integer\",\"description\":\"单文件扫描上限(字节)，默认 67108864\"}}," +
                                  "\"required\":[\"guids\"]}",
                Execute = AssetGuidFindAsync
            });
        }

        // ==================== asset_inspect ====================

        private static Task<object> AssetInspectAsync(JsonElement args)
        {
            var pathArg = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(pathArg))
                throw new ArgumentException("缺少 path 参数");

            var keywords = EditorUtils.GetStringArray(args, "keywords");
            if (keywords.Count == 0)
                keywords = DefaultKeywords.ToList();
            var includeStrings = EditorUtils.GetBool(args, "includeStrings", true);
            var includeFloats = EditorUtils.GetBool(args, "includeFloats", true);
            var floatMin = (float)GetDouble(args, "floatMin", 0.0001);
            var floatMax = (float)GetDouble(args, "floatMax", 100000.0);
            var maxStrings = Math.Clamp(EditorUtils.GetInt(args, "maxStrings", 40), 1, 500);
            var maxFloats = Math.Clamp(EditorUtils.GetInt(args, "maxFloats", 25), 1, 200);

            return Task.FromResult<object>(RunIo(() =>
            {
                var full = EditorUtils.ResolveWorkspacePath(pathArg, true);
                if (!File.Exists(full))
                    throw new ArgumentException("不是文件: " + pathArg);

                var info = new FileInfo(full);
                var warnings = new List<string>();
                var data = ReadFileBounded(full, info.Length, warnings);

                var storage = ParseStorage(data, warnings);

                var keywordCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var kw in keywords)
                {
                    if (string.IsNullOrEmpty(kw))
                        continue;
                    keywordCounts[kw] = CountOccurrences(data, Encoding.UTF8.GetBytes(kw));
                }

                var strings = includeStrings ? ExtractStrings(data, 6, maxStrings) : new List<string>();
                var floats = includeFloats
                    ? BuildFloatHistogram(data, floatMin, floatMax, maxFloats)
                    : new List<object>();

                // 头部解析拿不到 Metadata 时（旧版本 / 头部损坏）退化为直接找导入设置 JSON
                if (storage == null && data.Length > 0)
                {
                    var fallback = FindImportOptionsJson(data);
                    if (fallback != null)
                    {
                        storage = new Dictionary<string, object> { { "importOptionsFallback", fallback } };
                        warnings.Add("存储头无法解析，导入设置 JSON 由全文件搜索得到（偏移不可信）");
                    }
                }

                return new
                {
                    path = EditorUtils.ToDisplayPath(full),
                    sizeBytes = info.Length,
                    lastWriteUtc = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    storage,
                    keywordCounts,
                    strings,
                    floats,
                    warnings
                };
            }));
        }

        /// <summary>解析 FlaxStorage v9 头部并返回全部资产条目（含导入设置 JSON）。</summary>
        private static object ParseStorage(byte[] data, List<string> warnings)
        {
            if (data.Length < 28)
            {
                warnings.Add("文件不足 28 字节，不是有效的 FlaxStorage 文件");
                return null;
            }

            var magic = BitConverter.ToInt32(data, 0);
            if (magic != StorageMagicCode)
            {
                warnings.Add("存储魔数不匹配（期望 " + StorageMagicCode + "，实际 " + magic + "），非 FlaxStorage 文件");
                return null;
            }

            var version = BitConverter.ToUInt32(data, 4);
            var assetsCount = BitConverter.ToInt32(data, 24);
            if (assetsCount < 0 || assetsCount > 64)
            {
                warnings.Add("资产条目数异常: " + assetsCount);
                return null;
            }

            var header = new Dictionary<string, object>
            {
                { "magicOk", true },
                { "storageVersion", version },
                { "assetsCount", assetsCount },
                { "contentKey", BitConverter.ToInt32(data, 8) }
            };

            if (version != SupportedStorageVersion)
            {
                warnings.Add("存储版本 " + version + " 与已知的 v" + SupportedStorageVersion + " 布局不同，仅返回基础字段");
                return header;
            }

            var entries = new List<object>();
            var pos = 28;

            for (var i = 0; i < assetsCount && pos + EntrySize <= data.Length; i++)
            {
                var entryGuid = new Guid(Sub(data, pos, 16));
                var typeName = ReadChar16(data, pos + 16, TypeNameChars);
                var address = BitConverter.ToUInt32(data, pos + 16 + TypeNameChars * 2);
                pos += EntrySize;

                entries.Add(new Dictionary<string, object>
                {
                    { "index", i },
                    { "guid", entryGuid.ToString() },
                    { "typeName", typeName },
                    { "address", address }
                });
            }

            header["entries"] = entries;

            if (pos + 4 > data.Length)
            {
                warnings.Add("头部在块表之前被截断");
                return header;
            }

            var chunksCount = BitConverter.ToInt32(data, pos);
            pos += 4;
            var chunks = new List<object>();
            for (var i = 0; i < chunksCount && pos + 12 <= data.Length; i++)
            {
                chunks.Add(new
                {
                    address = BitConverter.ToUInt32(data, pos),
                    size = BitConverter.ToUInt32(data, pos + 4),
                    flags = BitConverter.ToInt32(data, pos + 8)
                });
                pos += 12;
            }
            header["chunksCount"] = chunksCount;
            header["chunks"] = chunks;

            // 资产头（重复存了 Guid/TypeName，另有 SerializedVersion 与 Metadata）
            var details = new List<object>();
            for (var i = 0; i < entries.Count; i++)
            {
                var address = Convert.ToUInt32(((Dictionary<string, object>)entries[i])["address"]);
                details.Add(ParseAssetHeader(data, address, warnings));
            }
            header["assets"] = details;

            return header;
        }

        /// <summary>解析单个资产头，重点取出 Metadata（导入设置 JSON）与依赖。</summary>
        private static object ParseAssetHeader(byte[] data, uint address, List<string> warnings)
        {
            var result = new Dictionary<string, object>();
            var p = (int)address;

            if (p <= 0 || p + 16 + TypeNameChars * 2 + 4 > data.Length)
            {
                result["error"] = "资产头越界（address=" + address + "）";
                return result;
            }

            result["guid"] = new Guid(Sub(data, p, 16)).ToString();
            p += 16;
            result["typeName"] = ReadChar16(data, p, TypeNameChars);
            p += TypeNameChars * 2;

            result["serializedVersion"] = BitConverter.ToUInt32(data, p);
            p += 4;

            p += AssetDataChunks * 4; // 块索引映射

            if (p + 4 > data.Length)
                return result;
            var customDataLength = BitConverter.ToInt32(data, p);
            p += 4 + customDataLength;

            if (p + 4 > data.Length)
                return result;
            result["headerHashCode"] = BitConverter.ToUInt32(data, p);
            p += 4;

            if (p + 4 > data.Length)
                return result;
            var metadataLength = BitConverter.ToInt32(data, p);
            p += 4;

            if (metadataLength < 0 || p + metadataLength > data.Length)
            {
                warnings.Add("Metadata 长度越界: " + metadataLength);
                return result;
            }

            var metadataText = Encoding.UTF8.GetString(data, p, metadataLength);
            p += metadataLength;

            object metadata = metadataText;
            try
            {
                using var doc = JsonDocument.Parse(metadataText);
                metadata = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                warnings.Add("Metadata 不是合法 JSON（" + ex.Message + "），按原文返回");
            }

            result["metadataLength"] = metadataLength;
            result["metadata"] = metadata;

            if (p + 4 <= data.Length)
            {
                var depsCount = BitConverter.ToInt32(data, p);
                result["dependenciesCount"] = depsCount;
            }

            return result;
        }

        // ==================== asset_guid_find ====================

        private static Task<object> AssetGuidFindAsync(JsonElement args)
        {
            var guids = EditorUtils.GetStringArray(args, "guids");
            if (guids.Count == 0)
                throw new ArgumentException("缺少 guids 参数");
            var rootArg = EditorUtils.GetString(args, "root", "Content");
            var extensions = EditorUtils.GetStringArray(args, "extensions");
            var includeBackups = EditorUtils.GetBool(args, "includeBackups", true);
            var maxHits = Math.Clamp(EditorUtils.GetInt(args, "maxHits", 50), 1, 500);
            var maxFileBytes = EditorUtils.GetInt(args, "maxFileBytes", 64 * 1024 * 1024);

            return Task.FromResult<object>(RunIo(() =>
            {
                var root = EditorUtils.ResolveWorkspacePath(rootArg, true);

                var samples = new List<Dictionary<string, object>>();
                foreach (var raw in guids)
                {
                    var parsed = ParseGuidBytes(raw);
                    if (parsed == null)
                        throw new ArgumentException("无法解析 Guid: " + raw);
                    samples.Add(parsed);
                }

                var hits = new List<object>();
                var filesScanned = 0;
                var filesSkipped = 0;

                foreach (var file in EnumerateFiles(root, extensions, includeBackups))
                {
                    FileInfo fi;
                    try { fi = new FileInfo(file); } catch { continue; }
                    if (fi.Length == 0 || fi.Length > maxFileBytes)
                    {
                        filesSkipped++;
                        continue;
                    }

                    byte[] bytes;
                    try { bytes = File.ReadAllBytes(file); }
                    catch
                    {
                        filesSkipped++;
                        continue;
                    }

                    filesScanned++;
                    var span = new ReadOnlySpan<byte>(bytes);
                    var display = EditorUtils.ToDisplayPath(file);

                    foreach (var sample in samples)
                    {
                        foreach (var variant in (List<KeyValuePair<string, byte[]>>)sample["variants"])
                        {
                            var offset = span.IndexOf(variant.Value);
                            if (offset < 0)
                                continue;
                            hits.Add(new
                            {
                                file = display,
                                offset,
                                guid = sample["guid"],
                                variant = variant.Key
                            });
                            if (hits.Count >= maxHits)
                                goto Done;
                        }
                    }
                }

            Done:
                return new
                {
                    root = EditorUtils.ToDisplayPath(root),
                    filesScanned,
                    filesSkipped,
                    truncated = hits.Count >= maxHits,
                    hitCount = hits.Count,
                    hits
                };
            }));
        }

        /// <summary>
        /// 生成一个 Guid 的两种字节序变体：
        /// string — D 格式字面顺序（文件里直接按文本 hex 写入时）；
        /// le     — Guid.ToByteArray()（前三段小端的混合端序，Flax 序列化的实际写法）。
        /// </summary>
        private static Dictionary<string, object> ParseGuidBytes(string raw)
        {
            var clean = new string((raw ?? "").Where(Uri.IsHexDigit).ToArray());
            if (clean.Length != 32)
                return null;

            var literal = new byte[16];
            for (var i = 0; i < 16; i++)
                literal[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);

            var guid = new Guid(literal);
            var variants = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("string", literal),
                new KeyValuePair<string, byte[]>("le", guid.ToByteArray())
            };

            return new Dictionary<string, object>
            {
                { "guid", guid.ToString() },
                { "variants", variants }
            };
        }

        private static IEnumerable<string> EnumerateFiles(string root, List<string> extensions, bool includeBackups)
        {
            if (File.Exists(root))
            {
                yield return root;
                yield break;
            }

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
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                    continue;

                if (!includeBackups)
                {
                    var ext = Path.GetExtension(file);
                    if (ext.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
                        ext.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".backup", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (extensions != null && extensions.Count > 0)
                {
                    var ext = Path.GetExtension(file);
                    var matched = extensions.Any(e =>
                        ext.Equals(e, StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals("." + e.TrimStart('.'), StringComparison.OrdinalIgnoreCase));
                    if (!matched)
                        continue;
                }

                yield return file;
            }
        }

        // ==================== 通用辅助 ====================

        private static byte[] ReadFileBounded(string full, long length, List<string> warnings)
        {
            if (length <= MaxFullScanBytes)
                return File.ReadAllBytes(full);

            warnings.Add("文件超过 " + (MaxFullScanBytes / 1024 / 1024) + "MB，仅读取前 " +
                         (MaxHeadBytes / 1024 / 1024) + "MB（字符串/浮点统计不完整）");
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[Math.Min(length, MaxHeadBytes)];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = fs.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                    break;
                read += n;
            }
            if (read == buffer.Length)
                return buffer;
            return buffer.Take(read).ToArray();
        }

        private static byte[] Sub(byte[] data, int offset, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(data, offset, result, 0, length);
            return result;
        }

        /// <summary>按 char16(UTF-16LE) 读取定长字段，遇到 NUL 截断。</summary>
        private static string ReadChar16(byte[] data, int offset, int chars)
        {
            var sb = new StringBuilder(chars);
            for (var i = 0; i < chars; i++)
            {
                var p = offset + i * 2;
                if (p + 2 > data.Length)
                    break;
                var c = (char)(data[p] | (data[p + 1] << 8));
                if (c == '\0')
                    break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static int CountOccurrences(byte[] data, byte[] pattern)
        {
            if (pattern.Length == 0)
                return 0;
            var count = 0;
            var span = new ReadOnlySpan<byte>(data);
            var index = span.IndexOf(pattern);
            while (index >= 0)
            {
                count++;
                span = span.Slice(index + pattern.Length);
                index = span.IndexOf(pattern);
            }
            return count;
        }

        private static List<string> ExtractStrings(byte[] data, int minLength, int limit)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();

            for (var i = 0; i <= data.Length; i++)
            {
                var b = i < data.Length ? data[i] : (byte)0;
                if (b >= 0x20 && b < 0x7f)
                {
                    sb.Append((char)b);
                    continue;
                }

                if (sb.Length >= minLength && seen.Add(sb.ToString()))
                {
                    result.Add(sb.ToString());
                    if (result.Count >= limit)
                        return result;
                }
                sb.Clear();
            }

            return result;
        }

        /// <summary>
        /// 4 字节对齐扫描 float32，只统计 (min, max) 区间内的高频值。
        /// 用于找「异常缩放常量」（例如被误写成 0.0001 或 100 的模型 Scale）。
        /// </summary>
        private static List<object> BuildFloatHistogram(byte[] data, float min, float max, int limit)
        {
            var counts = new Dictionary<float, int>();
            for (var offset = 0; offset + 4 <= data.Length; offset += 4)
            {
                var value = BitConverter.ToSingle(data, offset);
                if (float.IsNaN(value) || float.IsInfinity(value))
                    continue;
                if (value <= min || value >= max)
                    continue;
                counts.TryGetValue(value, out var count);
                counts[value] = count + 1;
            }

            return counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(limit)
                .Select(kv => (object)new { value = kv.Key, count = kv.Value })
                .ToList();
        }

        /// <summary>退路：在全文件里找 {"ImportPath"...} 并做括号配平截取。</summary>
        private static object FindImportOptionsJson(byte[] data)
        {
            var text = Encoding.UTF8.GetString(data);
            var start = text.IndexOf("{\"ImportPath\"", StringComparison.Ordinal);
            if (start < 0)
                return null;

            var json = ExtractJsonObject(text, start);
            if (json == null)
                return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return json;
            }
        }

        private static string ExtractJsonObject(string text, int start)
        {
            if (start < 0 || start >= text.Length || text[start] != '{')
                return null;

            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(start, i - start + 1);
                }
            }

            return null;
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
