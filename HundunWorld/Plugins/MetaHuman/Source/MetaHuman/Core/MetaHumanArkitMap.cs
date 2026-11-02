using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaHuman
{
    /// <summary>一条 ARKit 通道 → 某个 GUI 控制的反解关系。</summary>
    public class MetaHumanArkitEntry
    {
        [JsonPropertyName("arkit")]
        public string Arkit;

        /// <summary>exact=与 UE 的 raw 控制名逐字同名；authored=按语义人为指定（表里逐条标出）。</summary>
        [JsonPropertyName("basis")]
        public string Basis;

        [JsonPropertyName("raw")]
        public string Raw;

        [JsonPropertyName("guiControl")]
        public string GuiControl;

        [JsonPropertyName("guiRange")]
        public List<float> GuiRange;

        /// <summary>[From, To, Slope, Cut]，与 GUI→raw 表里的段同义。</summary>
        [JsonPropertyName("segment")]
        public List<float> Segment;
    }

    /// <summary>
    /// Apple ARKit 面部通道名 → 本插件 GUI 控制的对照层。
    ///
    /// 外部追踪给的是 ARKit 名（eyeBlinkLeft、jawOpen…），动画器吃的是 MetaHuman 的 GUI 控制名。
    /// 换算不是"名字差不多就当同一个"：每条都带 UE 权威表里的原始线性段，
    /// 用 gui = (v - Cut) / Slope 反解，并要求反解结果整段落在那一段区间内，
    /// 否则这条对应根本不该收（ARKit 的一半值域会被折线截掉）。
    /// 特别注意 blink/widen 这一类：MetaHuman 用同一个 GUI 控制的正负半轴分别驱动
    /// eyeBlinkL 与 eyeWidenL，所以 eyeWideLeft=1 反解出来是 GUI 值 -1，直接照搬 1 就变成眨眼。
    /// </summary>
    public class MetaHumanArkitMap
    {
        [JsonPropertyName("channel_count")]
        public int ChannelCount;

        [JsonPropertyName("entry_count")]
        public int EntryCount;

        [JsonPropertyName("entries")]
        public List<MetaHumanArkitEntry> Entries;

        [JsonPropertyName("declined")]
        public Dictionary<string, string> Declined;

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            IncludeFields = true,
        };

        private Dictionary<string, List<MetaHumanArkitEntry>> _byChannel;

        public int ExactCount
        {
            get { return CountByBasis("exact"); }
        }

        public int AuthoredCount
        {
            get { return CountByBasis("authored"); }
        }

        private int CountByBasis(string basis)
        {
            int n = 0;
            if (Entries == null)
            {
                return 0;
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Basis == basis)
                {
                    n++;
                }
            }

            return n;
        }

        public static MetaHumanArkitMap FromJson(string json)
        {
            MetaHumanArkitMap map = JsonSerializer.Deserialize<MetaHumanArkitMap>(json, Opts);
            if (map == null)
            {
                throw new InvalidDataException("ARKit 对照表反序列化得到 null");
            }

            map.BuildIndex();
            return map;
        }

        public static MetaHumanArkitMap LoadFromFile(string path)
        {
            return FromJson(File.ReadAllText(path));
        }

        public void BuildIndex()
        {
            _byChannel = new Dictionary<string, List<MetaHumanArkitEntry>>(StringComparer.Ordinal);
            if (Entries == null)
            {
                Entries = new List<MetaHumanArkitEntry>();
                return;
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                MetaHumanArkitEntry entry = Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Arkit))
                {
                    continue;
                }

                List<MetaHumanArkitEntry> bucket;
                if (!_byChannel.TryGetValue(entry.Arkit, out bucket))
                {
                    bucket = new List<MetaHumanArkitEntry>();
                    _byChannel[entry.Arkit] = bucket;
                }

                bucket.Add(entry);
            }
        }

        public bool TryGetEntries(string channel, out List<MetaHumanArkitEntry> entries)
        {
            entries = null;
            if (_byChannel == null || string.IsNullOrEmpty(channel))
            {
                return false;
            }

            return _byChannel.TryGetValue(channel, out entries);
        }

        /// <summary>这个通道名在表里但被明确判为不可接（含原因）。</summary>
        public bool IsDeclined(string channel)
        {
            return Declined != null && !string.IsNullOrEmpty(channel) && Declined.ContainsKey(channel);
        }

        /// <summary>把一个 ARKit 值反解成该段的 GUI 值；反解越出段区间时返回 false（不硬接）。</summary>
        public static bool TryInverseGuiValue(MetaHumanArkitEntry entry, float arkitValue, out float guiValue)
        {
            guiValue = 0f;
            if (entry == null || entry.Segment == null || entry.Segment.Count < 4)
            {
                return false;
            }

            float from = entry.Segment[0];
            float to = entry.Segment[1];
            float slope = entry.Segment[2];
            float cut = entry.Segment[3];
            if (Math.Abs(slope) < 1e-9f)
            {
                return false;
            }

            float value = (arkitValue - cut) / slope;
            // 段的右端是开区间，正好取到 To 时按 To 前一点算（下限一侧允许相等）
            if (value < from - 1e-5f || value > to + 1e-5f)
            {
                return false;
            }

            guiValue = value;
            return true;
        }

        /// <summary>
        /// 一批 ARKit 通道值 → 一批 GUI 控制值。
        /// 同一个 GUI 控制被多个通道写到时取较大的一次（与表情层叠加同规则），并返回被丢弃的通道数，
        /// 让"喂进来但没接上"这件事能被调用方看到。
        /// </summary>
        public Dictionary<string, float> ToGuiControls(Dictionary<string, float> arkitValues, out int unmatchedChannels, out int skippedDeclinedChannels, out int appliedEntries)
        {
            unmatchedChannels = 0;
            skippedDeclinedChannels = 0;
            appliedEntries = 0;
            Dictionary<string, float> guiOut = new Dictionary<string, float>(StringComparer.Ordinal);
            if (arkitValues == null)
            {
                return guiOut;
            }

            foreach (KeyValuePair<string, float> kv in arkitValues)
            {
                List<MetaHumanArkitEntry> entries;
                if (!TryGetEntries(kv.Key, out entries))
                {
                    // 已知判不可接 ≠ 陌生通道：前者是分寸，后者是数据出错，两者要能分开统计
                    if (IsDeclined(kv.Key))
                    {
                        skippedDeclinedChannels++;
                    }
                    else
                    {
                        unmatchedChannels++;
                    }

                    continue;
                }

                bool any = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    float guiValue;
                    if (!TryInverseGuiValue(entries[i], kv.Value, out guiValue))
                    {
                        continue;
                    }

                    float existing;
                    if (guiOut.TryGetValue(entries[i].GuiControl, out existing))
                    {
                        // 幅度取大者：两个通道都推同一根控制时，保留更强的那次，而不是相加或覆盖
                        guiValue = Math.Abs(guiValue) > Math.Abs(existing) ? guiValue : existing;
                    }

                    guiOut[entries[i].GuiControl] = guiValue;
                    any = true;
                    appliedEntries++;
                }

                if (!any)
                {
                    unmatchedChannels++;
                }
            }

            return guiOut;
        }

        /// <summary>表自身的结构校验，返回问题清单（空表示通过）。</summary>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();
            if (Entries == null || Entries.Count == 0)
            {
                errors.Add("表里没有任何对应");
                return errors;
            }

            if (Entries.Count != EntryCount)
            {
                errors.Add("声明的 entry_count 与实际条数不符：" + EntryCount + " 对 " + Entries.Count);
            }

            HashSet<string> channels = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Entries.Count; i++)
            {
                MetaHumanArkitEntry entry = Entries[i];
                if (string.IsNullOrEmpty(entry.Arkit) || string.IsNullOrEmpty(entry.GuiControl) || string.IsNullOrEmpty(entry.Raw))
                {
                    errors.Add("第 " + i + " 条缺字段");
                    continue;
                }

                if (entry.Basis != "exact" && entry.Basis != "authored")
                {
                    errors.Add(entry.Arkit + " 的依据标注非法：" + entry.Basis);
                }

                if (entry.Segment == null || entry.Segment.Count < 4)
                {
                    errors.Add(entry.Arkit + " 的换算段不足四项");
                    continue;
                }

                channels.Add(entry.Arkit);

                // 端点必须能反解回来，否则这张表在运行期会静默丢通道
                float low;
                float high;
                if (!TryInverseGuiValue(entry, 0f, out low) || !TryInverseGuiValue(entry, 1f, out high))
                {
                    errors.Add(entry.Arkit + " → " + entry.Raw + " 的 [0,1] 反解越出段区间");
                }
            }

            if (ChannelCount != channels.Count)
            {
                errors.Add("声明的 channel_count 与实际覆盖的通道数不符：" + ChannelCount + " 对 " + channels.Count);
            }

            return errors;
        }
    }
}
