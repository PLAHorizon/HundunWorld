using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
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
                Description = "直接解析 .flax 资产文件的二进制内容（不依赖内容数据库，可用于 .backup/损坏副本）：存储头与版本、资产 Guid（同时给出 .NET 形式与引擎日志形式）与类型名、" +
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
                Description = "在资产文件（含 .backup 副本）中按字节定位 Guid，用于「某资产引用的 Guid 指向谁 / 是否已失效」。自动尝试三种字节布局：" +
                              "dotnet（C#/编辑器 UI 里的 Guid 字符串）、flax（引擎日志里打印的 32 位十六进制）、literal（字面顺序）。只读。",
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

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "animation_inventory",
                Description = "剪辑资产清单：逐段列出动画资产的时长(帧/fps/秒)、通道数与通道骨骼名，并与目标 SkinnedModel 的节点名做大小写不敏感重合统计。" +
                              "引擎的按名自动重定向只认名字（SkinnedModel::GetSkeletonMapping → CompareIgnoreCase），重合为 0 就意味着这批剪辑必须先建重定向映射才能用。" +
                              "通道名在托管层读不到（Animation 只暴露 Length/Duration/FPS/Info），故直接解二进制字节。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"dir\":{\"type\":\"string\",\"description\":\"动画资产目录，默认 Content/Character/Models/Pro Magic Pack\"}," +
                                  "\"target\":{\"type\":\"string\",\"description\":\"目标蒙皮模型路径，默认 Content/Character/Models/Character_1.flax\"}," +
                                  "\"includeNames\":{\"type\":\"boolean\",\"description\":\"是否输出通道骨骼名，默认 true\"}," +
                                  "\"nameLimit\":{\"type\":\"integer\",\"description\":\"每段列出的骨骼名条数上限，默认 60\"}," +
                                  "\"maxFiles\":{\"type\":\"integer\",\"description\":\"最多处理的文件数，默认 200\"}}}",
                Execute = AnimationInventoryAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "skeleton_rename",
                Description = "把 SkinnedModel 骨架节点按表改名（默认 dryRun=true 只出计划不落盘），用于「动画剪辑按名自动重定向」前的命名对齐：" +
                              "引擎只比名字，剪辑通道名与骨架节点名不同名就完全不生效。改名会同时作用在同名多副本上（蒙皮按 NodeIndex 引用，改名不影响蒙皮）。" +
                              "落盘前自检：被改名节点的祖先链上若还有另一个同样会被改名的节点，同一旋转会叠加两次（双重变形），此时拒绝写盘。" +
                              "会先备份 .pre-skeleton-rename.bak。默认目标表 = Pro Magic Pack 的 UE4/Mixamo 通道名。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"path\":{\"type\":\"string\",\"description\":\"SkinnedModel 资产路径，默认 Content/Character/Models/Character_1.flax\"}," +
                                  "\"dryRun\":{\"type\":\"boolean\",\"description\":\"只出计划不写文件，默认 true\"}}," +
                                  "\"required\":[]}",
                Dangerous = true,
                Undoable = false,
                Execute = SkeletonRenameAsync
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
            var floatMin = (float)EditorUtils.GetDouble(args, "floatMin", 0.0001);
            var floatMax = (float)EditorUtils.GetDouble(args, "floatMax", 100000.0);
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
                var entryBytes = Sub(data, pos, 16);
                var entryGuid = new Guid(entryBytes);
                var typeName = ReadChar16(data, pos + 16, TypeNameChars);
                var address = BitConverter.ToUInt32(data, pos + 16 + TypeNameChars * 2);
                pos += EntrySize;

                entries.Add(new Dictionary<string, object>
                {
                    { "index", i },
                    { "guid", entryGuid.ToString() },
                    { "guidFlaxLog", ToFlaxLogGuid(entryBytes) },
                    { "rawBytes", ToHex(entryBytes) },
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
            result["guidFlaxLog"] = ToFlaxLogGuid(Sub(data, p, 16));
            result["rawBytes"] = ToHex(Sub(data, p, 16));
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
        /// 生成一个 Guid 的三种字节布局候选：
        /// dotnet  — Guid 字符串（D/N/B/P 任一形式）按 .NET 规则解析后的字节布局；
        ///           C# 代码里的 Guid 常量、编辑器 UI、内容数据库都用这种字符串，文件里也就是它；
        /// flax    — 引擎 C++ 侧 4 个小端 uint32 的布局（引擎日志里打印的 32 位十六进制）；
        /// literal — 输入字符串按十六进制字面顺序直接落成的字节（少数直接写文本的场景）。
        /// 三者都试，避免两侧字符串形式不同导致漏判。
        /// </summary>
        private static Dictionary<string, object> ParseGuidBytes(string raw)
        {
            var clean = new string((raw ?? "").Where(Uri.IsHexDigit).ToArray());
            if (clean.Length != 32)
                return null;

            var literal = new byte[16];
            for (var i = 0; i < 16; i++)
                literal[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);

            // flax：每 8 位十六进制（4 字节）视为一个 uint32，按小端写入
            var flax = new byte[16];
            for (var group = 0; group < 4; group++)
            {
                for (var b = 0; b < 4; b++)
                    flax[group * 4 + b] = literal[group * 4 + (3 - b)];
            }

            var parsed = new Guid(clean);
            var variants = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("dotnet", parsed.ToByteArray()),
                new KeyValuePair<string, byte[]>("flax", flax),
                new KeyValuePair<string, byte[]>("literal", literal)
            };

            return new Dictionary<string, object>
            {
                { "guid", parsed.ToString() },
                { "variants", variants }
            };
        }

        /// <summary>Flax 引擎 C++ 侧 Guid 的 32 位十六进制形式（4 个小端 uint32 依次拼接）。</summary>
        private static string ToFlaxLogGuid(byte[] raw16)
        {
            var sb = new StringBuilder(32);
            for (var group = 0; group < 4; group++)
            {
                var value = (uint)(raw16[group * 4]
                                   | (raw16[group * 4 + 1] << 8)
                                   | (raw16[group * 4 + 2] << 16)
                                   | (raw16[group * 4 + 3] << 24));
                sb.Append(value.ToString("x8"));
            }
            return sb.ToString();
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
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

        // ==================== animation_inventory ====================

        /// <remarks>
        /// Flax 往流里写字符串用的是「int32 字符数 + 每字符 uint16 与 lock 异或」的形式
        /// （引擎 Source/Engine/Serialization/Stream.cpp:593 Write(StringView, int16 lock)）：
        /// 骨架节点名 lock=71，动画通道名 lock=172。所以骨骼名在 .flax 里搜不到任何明文。
        /// 而托管层又拿不到动画通道（Animation 只暴露 Length/Duration/FPS/Info，通道名与曲线都是 native 私有），
        /// 于是「这批剪辑到底绑在哪套骨架上」只能按这个编码自行解码。
        /// </remarks>
        private const int NodeNameLock = 71;
        private const int ChannelNameLock = 172;

        private static Task<object> AnimationInventoryAsync(JsonElement args)
        {
            var dirArg = EditorUtils.GetString(args, "dir", "Content/Character/Models/Pro Magic Pack");
            var targetArg = EditorUtils.GetString(args, "target", "Content/Character/Models/Character_1.flax");
            var includeNames = EditorUtils.GetBool(args, "includeNames", true);
            var nameLimit = Math.Clamp(EditorUtils.GetInt(args, "nameLimit", 60), 0, 500);
            var maxFiles = Math.Clamp(EditorUtils.GetInt(args, "maxFiles", 200), 1, 2000);

            // 目标骨架经内容系统加载，必须留在主线程；文件字节读取量小（数十个剪辑），一并在此完成
            return MainThread.InvokeAsync<object>(() =>
            {
                var warnings = new List<string>();
                var targetNodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 用工程内相对路径加载：传绝对路径会让同一文件被登记成第二个包，
                // 触发编辑器 "Founded duplicated asset … Changing asset id"，把资产身份改掉。
                var targetAsset = Content.Load(targetArg, 15000);
                if (targetAsset is SkinnedModel targetModel)
                {
                    var nodes = targetModel.Nodes;
                    if (nodes != null)
                    {
                        foreach (var node in nodes)
                            if (!string.IsNullOrEmpty(node.Name))
                                targetNodeNames.Add(node.Name);
                    }
                }
                else
                {
                    warnings.Add("目标资产加载失败或不是 SkinnedModel: " + targetArg + "，重合统计不可信");
                }

                var root = EditorUtils.ResolveWorkspacePath(dirArg, true);
                var files = Directory.Exists(root)
                    ? Directory.GetFiles(root, "*.flax", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
                    : new List<string>();
                if (files.Count == 0)
                    throw new ArgumentException("目录里没有 .flax 文件: " + EditorUtils.ToDisplayPath(root));

                var clips = new List<object>();
                var unionNames = new List<string>();
                var unionSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var signatures = new HashSet<string>(StringComparer.Ordinal);
                var skipped = 0;

                foreach (var file in files.Take(maxFiles))
                {
                    byte[] data;
                    try
                    {
                        data = File.ReadAllBytes(file);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add("读取失败 " + Path.GetFileName(file) + ": " + ex.Message);
                        continue;
                    }

                    var chunks = ReadChunkTable(data, out var typeName, out var guid);
                    if (!string.Equals(typeName, "FlaxEngine.Animation", StringComparison.Ordinal))
                    {
                        skipped++;
                        continue;
                    }
                    if (chunks.Count == 0)
                    {
                        warnings.Add("块表解析失败: " + Path.GetFileName(file));
                        continue;
                    }

                    var decoded = DecodeAnimationClip(data, chunks[0], targetNodeNames);
                    decoded["file"] = Path.GetFileName(file);
                    decoded["guid"] = guid;

                    if (decoded.TryGetValue("channelNames", out var raw) && raw is List<string> names)
                    {
                        foreach (var n in names)
                            if (unionSeen.Add(n))
                                unionNames.Add(n);
                        signatures.Add(string.Join("|", names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
                        if (!includeNames)
                            decoded["channelNames"] = names.Count;
                        else if (names.Count > nameLimit)
                            decoded["channelNames"] = names.Take(nameLimit).ToList();
                    }

                    clips.Add(decoded);
                }

                var matchedUnion = unionNames.Where(n => targetNodeNames.Contains(n)).ToList();
                var failed = clips.Count(c => ((Dictionary<string, object>)c).ContainsKey("error"));

                var conclusion = failed > 0
                    ? "有 " + failed + " 个剪辑解析失败，清单不完整"
                    : matchedUnion.Count == 0
                        ? "通道名与目标骨架 0 重合：引擎的按名自动重定向接不上，必须先建 SkeletonRetarget 名称映射（或改用同名骨架）"
                        : matchedUnion.Count + "/" + unionNames.Count + " 通道名能在目标骨架上找到，其余需映射";

                return new
                {
                    dir = EditorUtils.ToDisplayPath(root),
                    target = EditorUtils.ToDisplayPath(EditorUtils.ResolveWorkspacePath(targetArg, true)),
                    targetSkeletonNodeCount = targetNodeNames.Count,
                    filesScanned = files.Count,
                    skippedNonAnimation = skipped,
                    clipCount = clips.Count,
                    distinctSkeletonSignatures = signatures.Count,
                    unionChannelNameCount = unionNames.Count,
                    unionChannelNames = includeNames ? unionNames.Take(nameLimit).ToList() : (object)unionNames.Count,
                    matchedWithTarget = matchedUnion,
                    matchCountWithTarget = matchedUnion.Count,
                    conclusion,
                    clips,
                    warnings
                };
            });
        }

        /// <summary>取 FlaxStorage v9 块表：各 chunk 的 (address,size)，并带回第一个资产条目的 Guid 与类型名。</summary>
        private static List<(uint Address, uint Size)> ReadChunkTable(byte[] data, out string typeName, out string guid)
        {
            typeName = null;
            guid = null;
            var result = new List<(uint, uint)>();

            if (data.Length < 28 || BitConverter.ToInt32(data, 0) != StorageMagicCode)
                return result;
            var assetsCount = BitConverter.ToInt32(data, 24);
            if (assetsCount < 1 || assetsCount > 64)
                return result;

            guid = new Guid(Sub(data, 28, 16)).ToString();
            typeName = ReadChar16(data, 28 + 16, TypeNameChars);

            var pos = 28 + assetsCount * EntrySize;
            if (pos + 4 > data.Length)
                return result;
            var chunksCount = BitConverter.ToInt32(data, pos);
            pos += 4;
            if (chunksCount < 0 || chunksCount > 64)
                return result;

            for (var i = 0; i < chunksCount && pos + 12 <= data.Length; i++)
            {
                result.Add(((uint)BitConverter.ToUInt32(data, pos), (uint)BitConverter.ToUInt32(data, pos + 4)));
                pos += 12;
            }

            return result;
        }

        private static Dictionary<string, object> DecodeAnimationClip(byte[] data, (uint Address, uint Size) chunk, HashSet<string> targetNodeNames)
        {
            var result = new Dictionary<string, object>();
            var start = (int)chunk.Address;
            var end = (int)Math.Min((long)chunk.Address + chunk.Size, data.Length);

            if (end - start < 33)
            {
                result["error"] = "数据块过小";
                return result;
            }

            var version = BitConverter.ToInt32(data, start);
            if (version < 100 || version > 103)
            {
                result["error"] = "动画头版本 " + version + " 不在已知范围(100~103)";
                return result;
            }

            // v103: int32 版本 + double 时长(帧) + double fps + byte RootMotionFlags + String RootNodeName(lock 13) + int32 通道数
            var frames = BitConverter.ToDouble(data, start + 4);
            var fps = BitConverter.ToDouble(data, start + 12);
            var pos = start + 21;
            if (!TryReadLockedName(data, pos, 13, out var rootNode, out pos))
            {
                if (pos + 4 > end || BitConverter.ToInt32(data, pos) != 0)
                {
                    result["error"] = "RootNodeName 解析失败";
                    return result;
                }
                rootNode = string.Empty;
                pos += 4;
            }

            var declaredChannels = BitConverter.ToInt32(data, pos);
            pos += 4;
            if (declaredChannels < 0 || declaredChannels > 4096)
            {
                result["error"] = "通道数异常: " + declaredChannels;
                return result;
            }

            // 通道记录 = 名字 + Position/Rotation(/Scale) 曲线；曲线头是 [int32 插值][int32 关键帧数][float 首帧时间]。
            // 空曲线会被 SkipEmptyCurves 整个省掉，所以逐字节步进找「像名字且后面跟着合法曲线头」的位置，而不是硬算步长。
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var o = pos; o + 16 < end; o++)
            {
                if (!TryReadLockedName(data, o, ChannelNameLock, out var name, out var after))
                    continue;

                var interpolation = BitConverter.ToInt32(data, after);
                var keyframes = BitConverter.ToInt32(data, after + 4);
                var firstTime = BitConverter.ToSingle(data, after + 8);
                if (interpolation < 0 || interpolation > 2 || keyframes < 1 || keyframes > 20000)
                    continue;
                if (Math.Abs(firstTime) > 1e-3f)
                    continue;

                if (seen.Add(name))
                    names.Add(name);
                o = after - 1;
            }

            var matched = names.Where(n => targetNodeNames.Contains(n)).ToList();

            result["assetVersion"] = version;
            result["frames"] = frames;
            result["fps"] = fps;
            result["lengthSeconds"] = fps > 0.0001 ? Math.Round(frames / fps, 3) : 0.0;
            result["rootMotionNode"] = rootNode;
            result["declaredChannels"] = declaredChannels;
            result["decodedChannelCount"] = names.Count;
            result["channelCountMatches"] = names.Count == declaredChannels;
            result["matchCountWithTarget"] = matched.Count;
            result["matchedChannels"] = matched;
            result["channelNames"] = names;
            return result;
        }

        /// <summary>读取「int32 字符数 + 每字符 uint16 ^= lock」形式的混淆串；解出非法字符即视为该偏移不是字符串。</summary>
        private static bool TryReadLockedName(byte[] data, int offset, int lockKey, out string name, out int next)
        {
            name = null;
            next = offset;

            if (offset + 4 > data.Length)
                return false;

            var chars = BitConverter.ToInt32(data, offset);
            if (chars < 1 || chars > 96 || offset + 4 + chars * 2 > data.Length)
                return false;

            var buffer = new StringBuilder(chars);
            for (var i = 0; i < chars; i++)
            {
                var raw = (ushort)(data[offset + 4 + i * 2] | (data[offset + 5 + i * 2] << 8));
                var c = (char)(raw ^ (ushort)lockKey);
                if (!IsBoneNameChar(c))
                    return false;
                buffer.Append(c);
            }

            name = buffer.ToString();
            next = offset + 4 + chars * 2;
            return true;
        }

        private static bool IsBoneNameChar(char c)
        {
            return c >= 'a' && c <= 'z' ||
                   c >= 'A' && c <= 'Z' ||
                   c >= '0' && c <= '9' ||
                   c == '_' || c == '-' || c == '.' || c == ':' || c == '(' || c == ')' || c == '/' || c == ' ';
        }

        // ==================== skeleton_rename ====================

        /// <summary>
        /// Pro Magic Pack 剪辑通道名 → Rigify 形变骨基名。脊柱分段含义取自 CharacterPreview3D.cs:219 的既有注释
        /// （DEF-spine=骨盆，.002/.003=胸，.004=上胸，.005=neck，.006=head）；
        /// 剪辑里没有对应物的 Scart1/Scart2 不列，DEF-spine.004、各 .001 扭转骨、DEF-palm.*、*_end 保持原名留在绑定姿势。
        /// </summary>
        private static readonly (string Channel, string DefBase)[] SpineAndLimbRenameTable =
        {
            ("Hips", "DEF-spine"),
            ("Spine", "DEF-spine.001"),
            ("Spine1", "DEF-spine.002"),
            ("Spine2", "DEF-spine.003"),
            ("Neck", "DEF-spine.005"),
            ("Head", "DEF-spine.006"),

            ("LeftShoulder", "DEF-shoulder.L"),
            ("RightShoulder", "DEF-shoulder.R"),
            ("LeftArm", "DEF-upper_arm.L"),
            ("RightArm", "DEF-upper_arm.R"),
            ("LeftForeArm", "DEF-forearm.L"),
            ("RightForeArm", "DEF-forearm.R"),
            ("LeftHand", "DEF-hand.L"),
            ("RightHand", "DEF-hand.R"),

            ("LeftUpLeg", "DEF-thigh.L"),
            ("RightUpLeg", "DEF-thigh.R"),
            ("LeftLeg", "DEF-shin.L"),
            ("RightLeg", "DEF-shin.R"),
            ("LeftFoot", "DEF-foot.L"),
            ("RightFoot", "DEF-foot.R"),
            ("LeftToeBase", "DEF-toe.L"),
            ("RightToeBase", "DEF-toe.R"),
        };

        private static readonly string[] FingerChannels = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        private static readonly string[] FingerDefNames = { "thumb", "f_index", "f_middle", "f_ring", "f_pinky" };

        /// <summary>展开成「骨架现有节点名 → 剪辑通道名」的对表（含左右手五指各 3 节）。</summary>
        private static List<(string From, string To)> BuildSkeletonRenamePairs()
        {
            var pairs = SpineAndLimbRenameTable.Select(r => (From: r.DefBase, To: r.Channel)).ToList();

            var sides = new[] { ("L", "Left"), ("R", "Right") };
            foreach (var side in sides)
            {
                for (var f = 0; f < FingerChannels.Length; f++)
                {
                    for (var j = 1; j <= 3; j++)
                        pairs.Add((From: "DEF-" + FingerDefNames[f] + ".0" + j + "." + side.Item1,
                                   To: side.Item2 + "Hand" + FingerChannels[f] + j));
                }
            }

            return pairs;
        }

        private static Task<object> SkeletonRenameAsync(JsonElement args)
        {
            var pathArg = EditorUtils.GetString(args, "path", "Content/Character/Models/Character_1.flax");
            var dryRun = EditorUtils.GetBool(args, "dryRun", true);

            return MainThread.InvokeAsync<object>(() =>
            {
                var full = EditorUtils.ResolveWorkspacePath(pathArg, true);
                var data = File.ReadAllBytes(full);
                var notes = new List<string>();

                if (data.Length < 28 || BitConverter.ToInt32(data, 0) != StorageMagicCode)
                    throw new ArgumentException("不是 FlaxStorage 文件: " + pathArg);

                var assetsCount = BitConverter.ToInt32(data, 24);
                if (assetsCount < 1 || assetsCount > 64)
                    throw new ArgumentException("资产条目数异常: " + assetsCount);
                var assetGuid = new Guid(Sub(data, 28, 16));
                // 内容系统只认「工程内相对路径」这种拼法。传绝对路径会被当成另一个包登记，
                // 触发 "Founded duplicated asset ... Changing asset id"，把资产身份改掉。
                var relativeAssetPath = EditorUtils.ToDisplayPath(full);

                var tablePos = 28 + assetsCount * EntrySize;
                var chunksCount = BitConverter.ToInt32(data, tablePos);
                if (chunksCount < 1 || chunksCount > 64)
                    throw new ArgumentException("块表异常: " + chunksCount);

                var addrs = new uint[chunksCount];
                var sizes = new uint[chunksCount];
                for (var i = 0; i < chunksCount; i++)
                {
                    addrs[i] = BitConverter.ToUInt32(data, tablePos + 4 + i * 12);
                    sizes[i] = BitConverter.ToUInt32(data, tablePos + 4 + i * 12 + 4);
                }

                var chunk0 = (int)addrs[0];
                var chunk0Size = (int)sizes[0];
                // 只做「chunk0 之后各块紧随其后」这种连续布局，否则放弃（不猜偏移）
                for (var i = 1; i < chunksCount; i++)
                {
                    if (addrs[i] != addrs[i - 1] + sizes[i - 1])
                        throw new ArgumentException("块数据不连续（chunk" + i + "），本工具不支持该布局，请改用编辑器改名");
                }
                if (addrs[chunksCount - 1] + sizes[chunksCount - 1] != (uint)data.Length)
                    throw new ArgumentException("最后一个块未触及文件尾，布局与预期不符");

                // 解析节点数组：int32 数量 @chunk0+198，随后每条 = 4(父) + 40(Transform) + 名字(lock 71)
                var nodesCount = BitConverter.ToInt32(data, chunk0 + 198);
                if (nodesCount < 1 || nodesCount > 8192)
                    throw new ArgumentException("节点数异常: " + nodesCount + "，chunk0 偏移假设不成立");

                var pos = chunk0 + 202;
                var parents = new int[nodesCount];
                var names = new string[nodesCount];
                var heads = new byte[nodesCount][];
                var nameRaw = new byte[nodesCount][];
                for (var i = 0; i < nodesCount; i++)
                {
                    parents[i] = BitConverter.ToInt32(data, pos);
                    if (!TryReadLockedName(data, pos + 44, NodeNameLock, out var nm, out var next))
                        throw new ArgumentException("第 " + i + " 个节点名解析失败，chunk0 布局与预期不符");
                    names[i] = nm;
                    heads[i] = Sub(data, pos, 44);
                    nameRaw[i] = Sub(data, pos + 44, next - pos - 44);
                    pos = next;
                }

                var bonesStart = pos;
                var bonesCount = BitConverter.ToInt32(data, bonesStart);
                var tail = Sub(data, bonesStart, chunk0 + chunk0Size - bonesStart);

                var pairs = BuildSkeletonRenamePairs();

                var applied = new Dictionary<int, string>();
                var plan = new List<object>();
                var missing = new List<string>();
                var clashes = new List<string>();

                foreach (var pair in pairs)
                {
                    var hits = new List<int>();
                    for (var i = 0; i < nodesCount; i++)
                        if (string.Equals(names[i], pair.From, StringComparison.OrdinalIgnoreCase))
                            hits.Add(i);

                    if (hits.Count == 0)
                    {
                        missing.Add(pair.From);
                        continue;
                    }

                    foreach (var idx in hits)
                        applied[idx] = pair.To;

                    plan.Add(new { from = pair.From, to = pair.To, copies = hits.Count, nodeIndices = hits.Take(4).ToList() });
                }

                var afterNames = new List<string>();
                for (var i = 0; i < nodesCount; i++)
                    afterNames.Add(applied.TryGetValue(i, out var t) ? t : names[i]);

                // 真正的双重变形条件：同一个通道名落在同一条祖先链的两个节点上（父子都收到同一旋转）。
                // 注意 DEF-spine → DEF-spine.001 这种「父子各接不同通道」是正常骨骼链，不算冲突。
                var aliases = new List<object>();
                foreach (var group in afterNames.Where(n => !string.IsNullOrEmpty(n))
                                                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    var members = new List<int>();
                    for (var i = 0; i < nodesCount; i++)
                        if (string.Equals(afterNames[i], group.Key, StringComparison.OrdinalIgnoreCase))
                            members.Add(i);

                    var sameChainPair = new List<string>();
                    foreach (var a in members)
                    {
                        var walk = parents[a];
                        var guard = 0;
                        while (walk >= 0 && walk < nodesCount && guard++ < nodesCount)
                        {
                            if (members.Contains(walk))
                            {
                                sameChainPair.Add(afterNames[a] + ": #" + a + " ← 祖先 #" + walk);
                                break;
                            }
                            walk = parents[walk];
                        }
                    }

                    if (sameChainPair.Count > 0)
                        clashes.AddRange(sameChainPair);
                    // 只关心「改名引入的名字」在骨架里撞到的节点（原有的 ×2 是 rig 本身的左右两副本，不必刷屏）
                    if (members.Count > 1 && applied.Values.Contains(group.Key, StringComparer.OrdinalIgnoreCase))
                        aliases.Add(new { name = group.Key, nodes = members.Take(8).ToList(), sameChain = sameChainPair });
                }
                var introduced = new HashSet<string>(applied.Values, StringComparer.OrdinalIgnoreCase);
                var duplicates = afterNames.Where(n => !string.IsNullOrEmpty(n) && introduced.Contains(n))
                                           .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                                           .Where(g => g.Count() > 1)
                                           .Select(g => g.Key + "×" + g.Count())
                                           .OrderBy(x => x, StringComparer.Ordinal)
                                           .ToList();

                var result = new Dictionary<string, object>
                {
                    { "path", EditorUtils.ToDisplayPath(full) },
                    { "assetGuid", assetGuid.ToString() },
                    { "nodesCount", nodesCount },
                    { "bonesCount", bonesCount },
                    { "chunk0Address", chunk0 },
                    { "chunk0Size", chunk0Size },
                    { "tableRows", plan.Count },
                    { "nodesToRename", applied.Count },
                    { "tableRowsNotFoundInSkeleton", missing },
                    { "sameChainChannelClashes", clashes.Distinct().Take(20).ToList() },
                    { "introducedNameCollisions", duplicates },
                    { "collisionDetails", aliases },
                    { "dryRun", dryRun },
                    { "plan", plan },
                    { "notes", notes }
                };

                if (dryRun)
                {
                    result["next"] = "确认 plan / introducedNameCollisions / sameChainChannelClashes 后用 dryRun=false 落盘";
                    return result;
                }
                if (clashes.Count > 0)
                {
                    result["aborted"] = "存在祖先链冲突（同一通道会叠加两次），未写盘";
                    return result;
                }

                // 重建 chunk0：前缀原样 + 逐条节点（只替换名字字段）+ 骨骼数组及其后全部原样
                if (applied.Count == 0)
                {
                    notes.Add("没有需要改名的节点（多半已经改过），跳过写盘，只做引擎重载校验");
                    return EngineVerify(result, relativeAssetPath, applied, notes);
                }

                var rebuilt = new MemoryStream(chunk0Size + 4096);
                var prefix = Sub(data, chunk0, 202);
                rebuilt.Write(prefix, 0, prefix.Length);
                for (var i = 0; i < nodesCount; i++)
                {
                    rebuilt.Write(heads[i], 0, heads[i].Length);

                    if (!applied.TryGetValue(i, out var newName))
                    {
                        rebuilt.Write(nameRaw[i], 0, nameRaw[i].Length);
                        continue;
                    }

                    rebuilt.Write(BitConverter.GetBytes(newName.Length), 0, 4);
                    foreach (var ch in newName)
                    {
                        var c = (ushort)(ch ^ NodeNameLock);
                        rebuilt.WriteByte((byte)(c & 0xFF));
                        rebuilt.WriteByte((byte)(c >> 8));
                    }
                }
                rebuilt.Write(tail, 0, tail.Length);
                var newChunk0 = rebuilt.ToArray();

                var trailing = data.Length - (chunk0 + chunk0Size);
                var outData = new byte[chunk0 + newChunk0.Length + trailing];
                Array.Copy(data, 0, outData, 0, chunk0);
                Array.Copy(newChunk0, 0, outData, chunk0, newChunk0.Length);
                Array.Copy(data, chunk0 + chunk0Size, outData, chunk0 + newChunk0.Length, trailing);

                for (var i = 0; i < chunksCount; i++)
                {
                    var field = tablePos + 4 + i * 12;
                    var addr = i == 0 ? addrs[0] : (uint)(chunk0 + newChunk0.Length + (addrs[i] - chunk0 - chunk0Size));
                    var size = i == 0 ? (uint)newChunk0.Length : sizes[i];
                    Buffer.BlockCopy(BitConverter.GetBytes(addr), 0, outData, field, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(size), 0, outData, field + 4, 4);
                }

                // 写盘前自检：新字节必须能被同一套规则重新解出，且改名条数一致
                var reNodes = BitConverter.ToInt32(outData, chunk0 + 198);
                if (reNodes != nodesCount)
                {
                    result["aborted"] = "自检失败：节点数 " + reNodes + " != " + nodesCount + "，未写盘";
                    return result;
                }
                var p2 = chunk0 + 202;
                var verified = 0;
                for (var i = 0; i < reNodes; i++)
                {
                    if (!TryReadLockedName(outData, p2 + 44, NodeNameLock, out _, out var next2))
                    {
                        result["aborted"] = "自检失败：第 " + i + " 个节点名解不出，未写盘";
                        return result;
                    }
                    if (applied.ContainsKey(i))
                        verified++;
                    p2 = next2;
                }
                if (verified != applied.Count)
                {
                    result["aborted"] = "自检失败：改名命中 " + verified + " != 计划 " + applied.Count + "，未写盘";
                    return result;
                }
                // 节点区走完必须正好落在骨骼计数上，且骨骼数不变（证明拼接没有错位）
                if (p2 >= chunk0 + newChunk0.Length || BitConverter.ToInt32(outData, p2) != bonesCount)
                {
                    result["aborted"] = "自检失败：新字节的骨骼区起点不符（p2=" + p2 + "），未写盘";
                    return result;
                }

                // 编辑器可能以内存映射持有该资产文件；先让引擎卸载，否则写盘会报
                // 「请求的操作无法在使用用户映射区域打开的文件上执行」
                try
                {
                    var held = Content.Load(relativeAssetPath, 20000);
                    if (held != null)
                        Content.UnloadAsset(held);
                }
                catch
                {
                    // 卸载失败也继续写：失败会由下一条的 IOException 暴露
                }

                var backup = full + ".pre-skeleton-rename.bak";
                if (!File.Exists(backup))
                    File.Copy(full, backup);
                File.WriteAllBytes(full, outData);

                result["written"] = true;
                result["backup"] = EditorUtils.ToDisplayPath(backup);
                result["newChunk0Size"] = newChunk0.Length;
                result["newFileSize"] = outData.Length;

                return EngineVerify(result, relativeAssetPath, applied, notes);
            });
        }

        /// <summary>
        /// 让引擎重新解析改写后的文件：能加载出来 = 二进制没写坏；同时回报引擎侧实际看到的新节点名。
        /// </summary>
        private static object EngineVerify(Dictionary<string, object> result, string assetPath, Dictionary<int, string> applied, List<string> notes)
        {
            try
            {
                var model = Content.Load(assetPath, 20000) as SkinnedModel;
                model?.Reload();
                model = Content.Load(assetPath, 20000) as SkinnedModel;

                var engineNodes = model?.Nodes;
                var engineNames = engineNodes != null
                    ? new HashSet<string>(engineNodes.Where(n => !string.IsNullOrEmpty(n.Name)).Select(n => n.Name),
                                         StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var landed = applied.Count == 0
                    ? engineNames.Where(n => n == "Hips" || n == "Spine" || n == "LeftHandIndex1" || n == "RightHandPinky3")
                                 .ToList()
                    : applied.Values.Distinct().Where(engineNames.Contains).ToList();

                result["engineReloaded"] = model != null && model.IsLoaded;
                result["engineNodeCount"] = engineNodes != null ? engineNodes.Length : 0;
                result["engineBoneCount"] = model?.Bones != null ? model.Bones.Length : 0;
                result["renamedNamesVisibleInEngine"] = landed.Count;
                result["renamedSampleVisible"] = landed.Take(10);
            }
            catch (Exception ex)
            {
                notes.Add("引擎重载校验失败: " + ex.Message + "（文件已写；重启编辑器后需确认该资产能正常加载）");
            }

            notes.Add("若之后重新导入 Character_1.fbx，节点名会退回 DEF-*，需要再跑一次本工具");
            return result;
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

        private static object RunIo(Func<object> action)
        {
            // 纯文件 IO，在服务器线程执行，不占用编辑器主线程
            return action();
        }
    }
}
