using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaHuman
{
    /// <summary>一条 GUI→raw 线性段：GUI 值落在 [From,To) 时给目标 raw 累加 Slope*值+Cut。</summary>
    public struct MetaHumanGuiToRawSegment
    {
        public int GuiIndex;
        public int RawIndex;
        public float From;
        public float To;
        public float Slope;
        public float Cut;
    }

    /// <summary>
    /// UE5.8 MetaHumanCoreTech 的 GUI 表情控制 → raw 表情控制换算表（174 个 GUI 控制、251 个 raw 控制）。
    /// 语义逐条对齐源里的 GuiToRawControlsUtils::ConvertGuiToRawControls：
    /// 每个 raw 先置 0，再按表序累加；区间是半开的 [From,To)；
    /// 只有 From 正好等于该 GUI 控制的量程下限时才钳到下限，只有 To 正好等于量程上限时才钳到上限，
    /// 落在这三个条件之外的段什么都不加（不是钳位，是"该段不参与"）。
    /// 表与黄金样例都由 Tools/MetaHumanExtractGuiMap.py 从引擎源里抽出，换算结果用 4×251 个期望值核对。
    /// </summary>
    public class MetaHumanGuiToRawMap
    {
        [JsonPropertyName("gui_controls")]
        public List<string> GuiControls;

        [JsonPropertyName("raw_controls")]
        public List<string> RawControls;

        [JsonPropertyName("gui_ranges")]
        public List<List<float>> GuiRanges;

        [JsonPropertyName("gui_defaults")]
        public List<float> GuiDefaults;

        [JsonPropertyName("mappings")]
        public List<Row> Mappings;

        /// <summary>JSON 里的映射行，字段顺序与源里的 struct 一致。</summary>
        public class Row
        {
            [JsonPropertyName("guiIndex")]
            public int GuiIndex;

            [JsonPropertyName("rawIndex")]
            public int RawIndex;

            [JsonPropertyName("from")]
            public float From;

            [JsonPropertyName("to")]
            public float To;

            [JsonPropertyName("slope")]
            public float Slope;

            [JsonPropertyName("cut")]
            public float Cut;
        }

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            IncludeFields = true,
        };

        private Dictionary<string, int> _guiIndexByName;
        private Dictionary<string, int> _rawIndexByName;
        private MetaHumanGuiToRawSegment[] _segments;
        private float[] _rangeLow;
        private float[] _rangeHigh;
        private float[] _defaultValue;

        public int GuiCount
        {
            get { return GuiControls == null ? 0 : GuiControls.Count; }
        }

        public int RawCount
        {
            get { return RawControls == null ? 0 : RawControls.Count; }
        }

        public int SegmentCount
        {
            get { return _segments == null ? 0 : _segments.Length; }
        }

        /// <summary>最近一次换算里各分支命中次数，用来发现「代码在跑但分支从没进过」。</summary>
        public int HitsInSpan;
        public int HitsClampLow;
        public int HitsClampHigh;
        public int HitsIgnored;

        public static MetaHumanGuiToRawMap FromJson(string json)
        {
            MetaHumanGuiToRawMap map = JsonSerializer.Deserialize<MetaHumanGuiToRawMap>(json, Opts);
            if (map == null)
            {
                throw new InvalidDataException("GUI→raw 换算表反序列化得到 null");
            }

            map.BuildIndex();
            return map;
        }

        public static MetaHumanGuiToRawMap LoadFromFile(string path)
        {
            return FromJson(File.ReadAllText(path));
        }

        public void BuildIndex()
        {
            if (GuiControls == null || RawControls == null || Mappings == null)
            {
                throw new InvalidDataException("GUI→raw 换算表缺字段（gui_controls/raw_controls/mappings）");
            }

            int guiCount = GuiControls.Count;
            if (GuiRanges == null || GuiRanges.Count != guiCount)
            {
                throw new InvalidDataException("量程条数 " + (GuiRanges == null ? 0 : GuiRanges.Count) + " 与 GUI 控制数 " + guiCount + " 不一致");
            }

            if (GuiDefaults == null || GuiDefaults.Count != guiCount)
            {
                throw new InvalidDataException("缺省值个数 " + (GuiDefaults == null ? 0 : GuiDefaults.Count) + " 与 GUI 控制数 " + guiCount + " 不一致");
            }

            _guiIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < guiCount; i++)
            {
                _guiIndexByName[GuiControls[i]] = i;
            }

            _rawIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < RawControls.Count; i++)
            {
                _rawIndexByName[RawControls[i]] = i;
            }

            _rangeLow = new float[guiCount];
            _rangeHigh = new float[guiCount];
            _defaultValue = new float[guiCount];
            for (int i = 0; i < guiCount; i++)
            {
                List<float> range = GuiRanges[i];
                if (range == null || range.Count != 2)
                {
                    throw new InvalidDataException("第 " + i + " 个 GUI 控制的量程不是两个数");
                }

                _rangeLow[i] = range[0];
                _rangeHigh[i] = range[1];
                _defaultValue[i] = GuiDefaults[i];
            }

            _segments = new MetaHumanGuiToRawSegment[Mappings.Count];
            for (int i = 0; i < Mappings.Count; i++)
            {
                Row row = Mappings[i];
                if (row.GuiIndex < 0 || row.GuiIndex >= guiCount)
                {
                    throw new InvalidDataException("映射 " + i + " 的 GUI 下标越界：" + row.GuiIndex);
                }

                if (row.RawIndex < 0 || row.RawIndex >= RawControls.Count)
                {
                    throw new InvalidDataException("映射 " + i + " 的 raw 下标越界：" + row.RawIndex);
                }

                _segments[i] = new MetaHumanGuiToRawSegment
                {
                    GuiIndex = row.GuiIndex,
                    RawIndex = row.RawIndex,
                    From = row.From,
                    To = row.To,
                    Slope = row.Slope,
                    Cut = row.Cut,
                };
            }
        }

        public bool TryGetGuiIndex(string controlName, out int index)
        {
            index = -1;
            if (_guiIndexByName == null || string.IsNullOrEmpty(controlName))
            {
                return false;
            }

            return _guiIndexByName.TryGetValue(controlName, out index);
        }

        public bool TryGetRawIndex(string controlName, out int index)
        {
            index = -1;
            if (_rawIndexByName == null || string.IsNullOrEmpty(controlName))
            {
                return false;
            }

            return _rawIndexByName.TryGetValue(controlName, out index);
        }

        public string RawName(int index)
        {
            return (index >= 0 && index < RawCount) ? RawControls[index] : null;
        }

        public string GuiName(int index)
        {
            return (index >= 0 && index < GuiCount) ? GuiControls[index] : null;
        }

        public float GuiRangeLow(int index)
        {
            return _rangeLow[index];
        }

        public float GuiRangeHigh(int index)
        {
            return _rangeHigh[index];
        }

        /// <summary>把输入表里没写的 GUI 控制填成源里的缺省值，与 ConvertGuiControlsToArray 同义。</summary>
        public float[] MaterializeGuiValues(Dictionary<string, float> guiControls)
        {
            float[] values = new float[GuiCount];
            for (int i = 0; i < GuiCount; i++)
            {
                float v;
                if (guiControls != null && guiControls.TryGetValue(GuiControls[i], out v))
                {
                    values[i] = v;
                }
                else
                {
                    values[i] = _defaultValue[i];
                }
            }

            return values;
        }

        /// <summary>核心循环：按表序累加，与引擎实现同序，保证多条写同一 raw 时的浮点结果也一致。</summary>
        public void ConvertInto(float[] guiValues, float[] rawOutput)
        {
            if (guiValues == null || guiValues.Length != GuiCount)
            {
                throw new InvalidDataException("传入的 GUI 值个数是 " + (guiValues == null ? -1 : guiValues.Length) + "，应为 " + GuiCount);
            }

            if (rawOutput == null || rawOutput.Length != RawCount)
            {
                throw new InvalidDataException("输出缓冲长度是 " + (rawOutput == null ? -1 : rawOutput.Length) + "，应为 " + RawCount);
            }

            for (int i = 0; i < rawOutput.Length; i++)
            {
                rawOutput[i] = 0f;
            }

            HitsInSpan = 0;
            HitsClampLow = 0;
            HitsClampHigh = 0;
            HitsIgnored = 0;

            for (int i = 0; i < _segments.Length; i++)
            {
                MetaHumanGuiToRawSegment segment = _segments[i];
                float guiValue = guiValues[segment.GuiIndex];
                float low = _rangeLow[segment.GuiIndex];
                float high = _rangeHigh[segment.GuiIndex];

                bool below = segment.From == low && guiValue < segment.From;
                bool above = segment.To == high && guiValue >= segment.To;

                if (segment.From <= guiValue && guiValue < segment.To)
                {
                    rawOutput[segment.RawIndex] += segment.Slope * guiValue + segment.Cut;
                    HitsInSpan++;
                }
                else if (below)
                {
                    rawOutput[segment.RawIndex] += segment.Slope * segment.From + segment.Cut;
                    HitsClampLow++;
                }
                else if (above)
                {
                    rawOutput[segment.RawIndex] += segment.Slope * segment.To + segment.Cut;
                    HitsClampHigh++;
                }
                else
                {
                    HitsIgnored++;
                }
            }
        }

        public Dictionary<string, float> Convert(Dictionary<string, float> guiControls)
        {
            float[] values = MaterializeGuiValues(guiControls);
            float[] raw = new float[RawCount];
            ConvertInto(values, raw);

            Dictionary<string, float> result = new Dictionary<string, float>(RawCount);
            for (int i = 0; i < RawCount; i++)
            {
                result[RawControls[i]] = raw[i];
            }

            return result;
        }

        /// <summary>某个 GUI 控制换算后会写到哪些 raw 控制（含系数），供控制表与诊断用。</summary>
        public List<MetaHumanGuiToRawSegment> SegmentsOfGui(int guiIndex)
        {
            List<MetaHumanGuiToRawSegment> list = new List<MetaHumanGuiToRawSegment>();
            for (int i = 0; i < _segments.Length; i++)
            {
                if (_segments[i].GuiIndex == guiIndex)
                {
                    list.Add(_segments[i]);
                }
            }

            return list;
        }

        /// <summary>表自身的结构校验，返回问题清单（空表示通过）。</summary>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();
            if (GuiCount == 0 || RawCount == 0)
            {
                errors.Add("表里 GUI 或 raw 控制为空");
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < GuiCount; i++)
            {
                if (!seen.Add(GuiControls[i]))
                {
                    errors.Add("GUI 控制名重复：" + GuiControls[i]);
                }
            }

            seen.Clear();
            for (int i = 0; i < RawCount; i++)
            {
                if (!seen.Add(RawControls[i]))
                {
                    errors.Add("raw 控制名重复：" + RawControls[i]);
                }
            }

            // 同一 (gui,raw) 出现多段是合法的折线，但两段区间不能重叠：
            // 重叠时两段都命中、结果按表序累加，等于把同一个值算了两遍。
            Dictionary<long, List<int>> byPair = new Dictionary<long, List<int>>();
            for (int i = 0; i < _segments.Length; i++)
            {
                long key = (long)_segments[i].GuiIndex * 100000L + _segments[i].RawIndex;
                List<int> items;
                if (!byPair.TryGetValue(key, out items))
                {
                    items = new List<int>();
                    byPair[key] = items;
                }

                items.Add(i);
            }

            foreach (KeyValuePair<long, List<int>> kv in byPair)
            {
                for (int a = 0; a < kv.Value.Count; a++)
                {
                    MetaHumanGuiToRawSegment sa = _segments[kv.Value[a]];
                    for (int b = a + 1; b < kv.Value.Count; b++)
                    {
                        MetaHumanGuiToRawSegment sb = _segments[kv.Value[b]];
                        bool overlaps = sa.From < sb.To - 1e-6f && sb.From < sa.To - 1e-6f;
                        if (overlaps)
                        {
                            errors.Add("折线段区间重叠：" + GuiControls[sa.GuiIndex] + " → " + RawControls[sa.RawIndex]);
                        }
                    }
                }
            }

            return errors;
        }
    }
}
