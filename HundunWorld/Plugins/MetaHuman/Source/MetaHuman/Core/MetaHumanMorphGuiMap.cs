using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaHuman
{
    /// <summary>一条 morph→GUI 对照。</summary>
    public class MetaHumanMorphGuiEntry
    {
        [JsonPropertyName("morph")] public string Morph;
        [JsonPropertyName("morphFamily")] public string MorphFamily;
        [JsonPropertyName("side")] public string Side;
        [JsonPropertyName("gui")] public string Gui;
        [JsonPropertyName("basis")] public string Basis;
        [JsonPropertyName("note")] public string Note;
    }

    /// <summary>一条明知不接的 morph（带理由，不静默丢弃）。</summary>
    public class MetaHumanMorphDeclined
    {
        [JsonPropertyName("morph")] public string Morph;
        [JsonPropertyName("reason")] public string Reason;
    }

    /// <summary>
    /// 真 MetaHuman 头的 blendshape 名字桥：morph 名 → UE 的 GUI 表情控制名。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么要单独一张表：<c>docs/metahuman/head_vocabulary_coverage.md</c> 量过——
    /// 资产里的 morph 名与 251 个 raw 控制名**只有 1 个同名**，归一后也只有 3 个
    /// （<c>head_wm1_blink_L</c> ↔ <c>CTRL_expressions_eyeBlinkL</c> 是两套命名族）。
    /// 所以表情要走 blendshape 落地时，"按同名自动接"这条路根本走不通，必须查表。
    /// </para>
    /// <para>
    /// 表由 <c>Tools/MetaHumanBuildMorphGuiMap.py</c> 生成并做过三条校验；这里的
    /// <see cref="Validate"/> 是**同一三条的第二遍**：加载时再核一次，
    /// 这样表被人工改坏（改了侧标、写了个不存在的 GUI 名）会在装载点就报出来，
    /// 而不是等到运行期某个 morph 静默不生效。
    /// </para>
    /// </remarks>
    public class MetaHumanMorphGuiMap
    {
        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        [JsonPropertyName("source")] public string Source;
        [JsonPropertyName("note")] public string Note;
        [JsonPropertyName("entryCount")] public int EntryCount;
        [JsonPropertyName("entries")] public List<MetaHumanMorphGuiEntry> Entries = new List<MetaHumanMorphGuiEntry>();
        [JsonPropertyName("declined")] public List<MetaHumanMorphDeclined> Declined = new List<MetaHumanMorphDeclined>();
        [JsonPropertyName("unmapped")] public List<string> Unmapped = new List<string>();

        private Dictionary<string, MetaHumanMorphGuiEntry> _byMorph;
        private Dictionary<string, List<string>> _byGui;

        /// <summary>
        /// 反查：一个 GUI 控制该驱动哪些 morph（多条 morph 可以共用一个控制，如 cheekRaise 三个变体）。
        /// 运行期写 blendshape 权重用的就是这个方向——上游来的是 GUI 值，不是 morph 名。
        /// </summary>
        public List<string> MorphsForGui(string guiControlName)
        {
            if (string.IsNullOrEmpty(guiControlName))
            {
                return new List<string>();
            }

            EnsureReverseIndexed();

            List<string> morphs;
            return _byGui.TryGetValue(guiControlName, out morphs) ? morphs : new List<string>();
        }

        /// <summary>
        /// 把一个"想要但资产里没有"的形状名，按这张表翻译成资产里真实存在的那个 blendshape 名。
        /// </summary>
        /// <remarks>
        /// 存在的理由：产品写回端（<c>MetaHumanAppearanceApplier</c>）是拿模板给的形状名
        /// 去资产名录里直查，查不到就跳过 —— 而 13.13 量过 morph 名与 raw/控制名不同源
        /// （同名命中 1/37），所以真头一来，整条头部自定义通路会"一根都不生效"却只报跳过。
        /// 这里给一次基于表的翻译：②先按表正查（morph→GUI）与反查（GUI→morph）绕一圈，
        /// ③再按归一化名（剥 CTRL_expressions_ 前缀、剥宿主网格前缀、去左右侧标记、忽略下划线与大小写）
        /// 对上的也算命中。翻不出来就返回 null，让调用方如实计入 skipped——不猜一个近似名。
        /// </remarks>
        public string ResolveAssetMorph(string wantedName, ICollection<string> availableMorphNames)
        {
            if (string.IsNullOrEmpty(wantedName) || availableMorphNames == null || availableMorphNames.Count == 0)
            {
                return null;
            }

            if (availableMorphNames.Contains(wantedName))
            {
                return null; // 本来就有，不需要翻译（调用方按原名字直接写）
            }

            EnsureIndexed();
            EnsureReverseIndexed();

            // 1) wanted 是表里的 morph 名：它对应的 GUI 控制名下还挂着别的 morph，也许有在资产里
            MetaHumanMorphGuiEntry asMorph;
            if (_byMorph.TryGetValue(wantedName, out asMorph))
            {
                string hit = FirstAvailable(MorphsForGui(asMorph.Gui), availableMorphNames);
                if (hit != null)
                {
                    return hit;
                }
            }

            // 2) wanted 是 GUI 控制名：反查它名下的 morph
            string byGui = FirstAvailable(MorphsForGui(wantedName), availableMorphNames);
            if (byGui != null)
            {
                return byGui;
            }

            // 3) 归一化兜底：把两边都折成"无宿主前缀、无侧标、无下划线、小写"的核心词再比
            string wantedCore = Core(wantedName);
            foreach (string candidate in availableMorphNames)
            {
                if (!string.IsNullOrEmpty(wantedCore) && string.Equals(Core(candidate), wantedCore, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string FirstAvailable(List<string> names, ICollection<string> available)
        {
            if (names == null)
            {
                return null;
            }

            for (int i = 0; i < names.Count; i++)
            {
                if (available.Contains(names[i]))
                {
                    return names[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 折成核心词：去 CTRL_ 前缀、去宿主网格前缀、去左右侧标记、去下划线与通道后缀、小写。
        /// 这里刻意不用 System.Text.RegularExpressions —— 插件的 Flax.Build 目标没有引用
        /// System.Text.RegularExpressions 程序集（dotnet 门能过、插件门 CS1069），归一化规则又都是
        /// 定长的前后缀，手写的等价实现反而更短更快。
        /// </summary>
        private static string Core(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            string s = name;
            int dot = s.IndexOf('.');
            if (dot > 0)
            {
                s = s.Substring(0, dot); // 通道后缀 .ty/.rotx 之类
            }

            s = StripStart(s, "CTRL_expressions_");
            s = StripStart(s, "CTRL_L_");
            s = StripStart(s, "CTRL_R_");
            s = StripHostPrefix(s);
            s = StripSideTail(s);
            s = s.Replace("_", "").Replace("CTRL", "").ToLowerInvariant();
            return s;
        }

        private static string StripStart(string s, string prefix)
        {
            return s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? s.Substring(prefix.Length) : s;
        }

        /// <summary>去掉宿主网格前缀 head_/face_ + cm/wm + 一串数字 + '_'（等价于 ^(head|face)_(cm|wm)\d+_）。</summary>
        private static string StripHostPrefix(string s)
        {
            string head;
            if (s.StartsWith("head_", StringComparison.OrdinalIgnoreCase))
            {
                head = "head_";
            }
            else if (s.StartsWith("face_", StringComparison.OrdinalIgnoreCase))
            {
                head = "face_";
            }
            else
            {
                return s;
            }

            int i = head.Length;
            if (s.Length < i + 2)
            {
                return s;
            }

            string kind = s.Substring(i, 2).ToLowerInvariant();
            if (kind != "cm" && kind != "wm")
            {
                return s;
            }

            i += 2;
            int digitsStart = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                i++;
            }

            if (i == digitsStart || i >= s.Length || s[i] != '_')
            {
                return s;
            }

            return s.Substring(i + 1);
        }

        /// <summary>去掉尾部的左右侧标记 '_l'/'_r'（等价于 _(l|r)$）。</summary>
        private static string StripSideTail(string s)
        {
            if (s.Length < 2 || s[s.Length - 2] != '_')
            {
                return s;
            }

            char side = char.ToLowerInvariant(s[s.Length - 1]);
            return side == 'l' || side == 'r' ? s.Substring(0, s.Length - 2) : s;
        }

        private void EnsureReverseIndexed()
        {
            // 与正查同一件事：条目数变了就必须重建，否则新加的那条永远查不到（表现是"面板有滑杆但脸不动"）。
            int groups = 0;
            if (_byGui != null)
            {
                foreach (KeyValuePair<string, List<string>> kv in _byGui)
                {
                    groups += kv.Value.Count;
                }
            }

            if (_byGui != null && groups == (Entries?.Count ?? 0))
            {
                return;
            }

            _byGui = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (Entries == null)
            {
                return;
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                MetaHumanMorphGuiEntry entry = Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Gui) || string.IsNullOrEmpty(entry.Morph))
                {
                    continue;
                }

                List<string> list;
                if (!_byGui.TryGetValue(entry.Gui, out list))
                {
                    list = new List<string>();
                    _byGui[entry.Gui] = list;
                }

                if (!list.Contains(entry.Morph))
                {
                    list.Add(entry.Morph);
                }
            }
        }

        public static MetaHumanMorphGuiMap FromJson(string json)
        {
            return JsonSerializer.Deserialize<MetaHumanMorphGuiMap>(json, ReadOptions);
        }

        private void EnsureIndexed()
        {
            // 与模板索引同一个坑：只看"建没建过索引"不够，条目被追加过就必须重建，
            // 否则新加的 morph 查不到，表现是"面板上有这根滑杆但推不动脸"。
            if (_byMorph != null && _byMorph.Count == (Entries?.Count ?? 0))
            {
                return;
            }

            _byMorph = new Dictionary<string, MetaHumanMorphGuiEntry>(StringComparer.Ordinal);
            if (Entries != null)
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    MetaHumanMorphGuiEntry entry = Entries[i];
                    if (entry != null && !string.IsNullOrEmpty(entry.Morph))
                    {
                        _byMorph[entry.Morph] = entry;
                    }
                }
            }
        }

        /// <summary>按 morph 名查 GUI 控制名。查不到返回 false（调用方要如实计数，不能当 0 处理）。</summary>
        public bool TryGetGuiControl(string morphName, out string gui)
        {
            gui = null;
            if (string.IsNullOrEmpty(morphName))
            {
                return false;
            }

            EnsureIndexed();
            if (_byMorph.TryGetValue(morphName, out MetaHumanMorphGuiEntry entry))
            {
                gui = entry.Gui;
                return !string.IsNullOrEmpty(gui);
            }

            return false;
        }

        /// <summary>这个 morph 是被"有意不接"的（有理由），还是单纯没进表。</summary>
        public bool IsDeclined(string morphName)
        {
            if (Declined == null)
            {
                return false;
            }

            for (int i = 0; i < Declined.Count; i++)
            {
                if (Declined[i] != null && string.Equals(Declined[i].Morph, morphName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 装载期复核：每个 GUI 名都要在权威 GUI→raw 表里存在且至少有一段。
        /// 只满足"名字存在"是不够的——接得上名字但落不到 raw，等于没有这条通路。
        /// </summary>
        public List<string> Validate(MetaHumanGuiToRawMap guiToRaw)
        {
            var problems = new List<string>();

            if (guiToRaw == null)
            {
                problems.Add("没给权威 GUI→raw 表，无法复核（空表不能算通过）");
                return problems;
            }

            EnsureIndexed();

            if (EntryCount != (Entries?.Count ?? 0))
            {
                problems.Add("entryCount 与实际条目数不符：" + EntryCount + " vs " + (Entries?.Count ?? 0));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (Entries != null)
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    MetaHumanMorphGuiEntry entry = Entries[i];
                    if (entry == null || string.IsNullOrEmpty(entry.Morph) || string.IsNullOrEmpty(entry.Gui))
                    {
                        problems.Add("第 " + i + " 条缺 morph 或 gui");
                        continue;
                    }

                    if (!seen.Add(entry.Morph))
                    {
                        problems.Add(entry.Morph + "：同一个 morph 出现两次（后一条会覆盖前一条的语义）");
                    }

                    int guiIndex = GuiIndexOf(guiToRaw, entry.Gui);
                    if (guiIndex < 0)
                    {
                        problems.Add(entry.Morph + " → " + entry.Gui + "：GUI 名不在权威 GUI→raw 表里");
                        continue;
                    }

                    if (guiToRaw.SegmentsOfGui(guiIndex).Count == 0)
                    {
                        problems.Add(entry.Morph + " → " + entry.Gui + "：查得到名字但没有任何一段 raw 换算");
                    }

                    // 侧标：morph 的侧要与 GUI 前缀一致；中央morph 只许指到中央控制
                    if ((entry.Side == "L" || entry.Side == "R") &&
                        !entry.Gui.StartsWith("CTRL_" + entry.Side + "_", StringComparison.Ordinal))
                    {
                        problems.Add(entry.Morph + "：侧标 " + entry.Side + " 与 GUI 前缀不一致 -> " + entry.Gui);
                    }

                    if (entry.Side == "C" &&
                        !entry.Gui.StartsWith("CTRL_C_", StringComparison.Ordinal) &&
                        !entry.Gui.StartsWith("CTRL_neck", StringComparison.Ordinal))
                    {
                        problems.Add(entry.Morph + "：中央 morph 指到了带侧标的控制 -> " + entry.Gui);
                    }
                }
            }

            if (Declined != null)
            {
                for (int i = 0; i < Declined.Count; i++)
                {
                    if (Declined[i] == null || string.IsNullOrEmpty(Declined[i].Reason))
                    {
                        problems.Add("declined 第 " + i + " 条没有理由（拒绝必须可追溯）");
                    }
                }
            }

            return problems;
        }

        /// <summary>权威表只给了 GuiName(i)，所以按名扫一遍拿下标。</summary>
        private static int GuiIndexOf(MetaHumanGuiToRawMap guiToRaw, string gui)
        {
            int count = guiToRaw.GuiCount;
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(guiToRaw.GuiName(i), gui, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
