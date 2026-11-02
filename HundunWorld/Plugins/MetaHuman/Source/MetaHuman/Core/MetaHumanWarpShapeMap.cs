using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaHuman
{
    /// <summary>
    /// 一条 warp 形变器名 → 资产里真实 blendshape 名的对照。
    /// 右岸是**清单**而不是单值：同一个形变在真头上同时挂在 head 与 cartilage 等多具网格上，
    /// 那是正确语义（各写回端拿自己名录取到的那根），不是"选不出来"的歧义。
    /// </summary>
    public class MetaHumanWarpShapeEntry
    {
        [JsonPropertyName("warp")] public string Warp;
        [JsonPropertyName("shapes")] public List<string> Shapes = new List<string>();
        [JsonPropertyName("basis")] public string Basis;
        [JsonPropertyName("meshes")] public List<string> Meshes = new List<string>();
    }

    /// <summary>被判定不接的 warp 名（有记录的判定，不是缺数据）。</summary>
    public class MetaHumanWarpDeclined
    {
        [JsonPropertyName("warp")] public string Warp;
        [JsonPropertyName("reason")] public string Reason;
    }

    /// <summary>规则吃不透、如实登记为翻不出来的 warp 名。</summary>
    public class MetaHumanWarpUnmapped
    {
        [JsonPropertyName("warp")] public string Warp;
        [JsonPropertyName("reason")] public string Reason;
        [JsonPropertyName("candidates")] public List<string> Candidates = new List<string>();
    }

    /// <summary>
    /// 第三命名族的对照表：控制表里那 29 个 `head_wmN_*` 形变器名，两张 morph→GUI 表都不认它
    /// （它们的左岸分别是 .dna 的 GUI 控制名与 UE 网格形状名），所以控制驱动表情时
    /// 这些名字一路走到写回端也只会被记成"缺失"。本表补的就是这一跳。
    ///
    /// 名字由 <c>Tools/MhWarpShapeMapGen.py</c> 从两份权威名录生成（warp 侧取
    /// Face_Control_Table 的 MorphCatalog + Controls[].MorphTargets，形状侧取真头 blendshape 名录），
    /// 规则只认"归一后 token 集相等"与"真子集且尾段唯一"两档，
    /// 吃不透的一律进 unmapped —— 绝不靠词形猜测把错形变画进脸里。
    /// </summary>
    public class MetaHumanWarpShapeMap
    {
        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            // 本装配里几张表都拿公开字段当载体，STJ 默认 IncludeFields=false ——
            // 少了这行会"反序列化成功但每个字段都是初值"，校验因两边同为 0 而照绿（今天就被这条抓过一次）
            IncludeFields = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        [JsonPropertyName("source")] public string Source;
        [JsonPropertyName("note")] public string Note;
        [JsonPropertyName("rule")] public string Rule;
        [JsonPropertyName("warpCount")] public int WarpCount;
        [JsonPropertyName("entryCount")] public int EntryCount;
        [JsonPropertyName("declinedCount")] public int DeclinedCount;
        [JsonPropertyName("unmappedCount")] public int UnmappedCount;
        [JsonPropertyName("entries")] public List<MetaHumanWarpShapeEntry> Entries = new List<MetaHumanWarpShapeEntry>();
        [JsonPropertyName("declined")] public List<MetaHumanWarpDeclined> Declined = new List<MetaHumanWarpDeclined>();
        [JsonPropertyName("unmapped")] public List<MetaHumanWarpUnmapped> Unmapped = new List<MetaHumanWarpUnmapped>();

        private Dictionary<string, List<string>> _byWarp;

        public static MetaHumanWarpShapeMap FromJson(string json)
        {
            return JsonSerializer.Deserialize<MetaHumanWarpShapeMap>(json, ReadOptions);
        }

        /// <summary>
        /// 把一个 warp 形变器名翻成候选形状名清单；表里没有就返回空清单（调用方据此继续走原有通路）。
        /// 返回的是副本，调用方改动不会影响表。
        /// </summary>
        public List<string> ShapesForWarp(string warpName)
        {
            EnsureIndex();

            List<string> found;
            if (string.IsNullOrEmpty(warpName) || !_byWarp.TryGetValue(warpName, out found))
            {
                return new List<string>();
            }

            return new List<string>(found);
        }

        /// <summary>这个 warp 名是否被显式判定为不接（与"表里没这条"是两回事）。</summary>
        public bool IsDeclined(string warpName)
        {
            if (string.IsNullOrEmpty(warpName))
            {
                return false;
            }

            for (int i = 0; i < Declined.Count; i++)
            {
                if (Declined[i] != null && Declined[i].Warp == warpName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>表里登记的全部 warp 名（entries + declined + unmapped），供断言与体检用。</summary>
        public List<string> AllWarps()
        {
            List<string> all = new List<string>();
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i] != null && !string.IsNullOrEmpty(Entries[i].Warp)) all.Add(Entries[i].Warp);
            }
            for (int i = 0; i < Declined.Count; i++)
            {
                if (Declined[i] != null && !string.IsNullOrEmpty(Declined[i].Warp)) all.Add(Declined[i].Warp);
            }
            for (int i = 0; i < Unmapped.Count; i++)
            {
                if (Unmapped[i] != null && !string.IsNullOrEmpty(Unmapped[i].Warp)) all.Add(Unmapped[i].Warp);
            }
            return all;
        }

        /// <summary>
        /// 装载即校验：条目数与清单要对得上、warp 名不许跨清单重复出现、
        /// 每条候选必须非空且 basis 只能是规则里那两档。
        /// 返回值非空即由调用方大声报错，不放行半坏的表。
        /// </summary>
        public List<string> Validate()
        {
            List<string> problems = new List<string>();

            if (EntryCount != Entries.Count)
            {
                problems.Add("entryCount=" + EntryCount + " 与 entries 实际条数 " + Entries.Count + " 不符");
            }

            if (DeclinedCount != Declined.Count)
            {
                problems.Add("declinedCount=" + DeclinedCount + " 与 declined 实际条数 " + Declined.Count + " 不符");
            }

            if (UnmappedCount != Unmapped.Count)
            {
                problems.Add("unmappedCount=" + UnmappedCount + " 与 unmapped 实际条数 " + Unmapped.Count + " 不符");
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> warps = AllWarps();
            for (int i = 0; i < warps.Count; i++)
            {
                if (!seen.Add(warps[i]))
                {
                    problems.Add("同一个 warp 名出现在多张清单里：" + warps[i] + "（判定/未映射/已映射必须互斥）");
                }
            }

            if (WarpCount != warps.Count)
            {
                problems.Add("warpCount=" + WarpCount + " 与三张清单合计 " + warps.Count + " 不符");
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                MetaHumanWarpShapeEntry entry = Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Warp))
                {
                    problems.Add("第 " + i + " 条缺 warp 名");
                    continue;
                }

                if (entry.Shapes == null || entry.Shapes.Count == 0)
                {
                    problems.Add(entry.Warp + " 的候选形状清单为空：要么给名字要么进 unmapped");
                    continue;
                }

                for (int k = 0; k < entry.Shapes.Count; k++)
                {
                    if (string.IsNullOrEmpty(entry.Shapes[k]))
                    {
                        problems.Add(entry.Warp + " 第 " + k + " 个候选是空名字");
                    }
                }

                if (entry.Basis != "guiReferee" && entry.Basis != "exact" && entry.Basis != "contains")
                {
                    problems.Add(entry.Warp + " 的 basis=" + entry.Basis
                                 + " 不在规则允许的档位（guiReferee/exact/contains）");
                }
            }

            for (int i = 0; i < Declined.Count; i++)
            {
                if (Declined[i] == null || string.IsNullOrEmpty(Declined[i].Reason))
                {
                    problems.Add("declined 第 " + i + " 条没有写理由：declined 是有记录的判定，不能只留名字");
                }
            }

            for (int i = 0; i < Unmapped.Count; i++)
            {
                if (Unmapped[i] == null || string.IsNullOrEmpty(Unmapped[i].Reason))
                {
                    problems.Add("unmapped 第 " + i + " 条没有写理由");
                }
            }

            return problems;
        }

        private void EnsureIndex()
        {
            if (_byWarp != null)
            {
                return;
            }

            _byWarp = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < Entries.Count; i++)
            {
                MetaHumanWarpShapeEntry entry = Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Warp) || entry.Shapes == null)
                {
                    continue;
                }

                _byWarp[entry.Warp] = entry.Shapes;
            }
        }
    }
}
