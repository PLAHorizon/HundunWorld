using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaHuman
{
    /// <summary>
    /// 一次「保存」产出的外观快照：可写盘、可上线、可回填到 state。
    /// 只含偏离默认值的差量 + 模板标识 + 校验和；模板换版靠 TemplateVersion 判定兼容性。
    /// </summary>
    [Serializable]
    public class MetaHumanLook
    {
        public string TemplateId { get; set; }

        /// <summary>导出时的模板版本字符串，用于回读时判定兼容。</summary>
        public string TemplateVersion { get; set; }

        /// <summary>导出时模板的参数个数：紧凑编码的下标上限，回读时用来发现槽位漂移。</summary>
        public int TemplateParamCount { get; set; }

        public Dictionary<string, float> Values { get; set; }

        public bool LinkSymmetric { get; set; }

        /// <summary>该快照源自哪个预设（可空），供 UI 显示「基于某某模板」。</summary>
        public string SourcePresetId { get; set; }

        /// <summary>已发布位：置位后这条快照就是终版，任何回溯入口都该关掉。</summary>
        public bool Published { get; set; }

        /// <summary>导出时间（UTC ticks），回溯界面按它显示步骤时间。</summary>
        public long SavedAtUtcTicks { get; set; }

        /// <summary>
        /// 客户端存档修订号：每次保存/发布自增，用来判断两份外观谁更新。
        /// 0 表示"旧格式/没有修订号"，此时调用方回退到 <see cref="SavedAtUtcTicks"/> 比较。
        /// </summary>
        public long Revision { get; set; }

        /// <summary>差量内容的校验和（FNV-1a），网络侧用它做幂等与去重。</summary>
        public uint Checksum { get; set; }

        public MetaHumanLook()
        {
            Values = new Dictionary<string, float>();
            LinkSymmetric = true;
        }

        private static readonly JsonSerializerOptions JsonOpts = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.WriteIndented = false;
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        public static MetaHumanLook Capture(MetaHumanState state, string sourcePresetId)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            MetaHumanLook look = new MetaHumanLook();
            look.TemplateId = state.Template.TemplateId;
            look.TemplateVersion = state.Template.Version != null ? state.Template.Version.ToString() : "0";
            look.TemplateParamCount = state.Template.Parameters.Count;
            look.LinkSymmetric = state.LinkSymmetric;
            look.SourcePresetId = sourcePresetId;
            look.SavedAtUtcTicks = DateTime.UtcNow.Ticks;

            foreach (KeyValuePair<string, float> kv in state.ModifiedValues())
            {
                look.Values[kv.Key] = kv.Value;
            }

            look.Checksum = ComputeChecksum(look.Values);
            return look;
        }

        public void RestoreInto(MetaHumanState state)
        {
            state.LinkSymmetric = LinkSymmetric;
            state.Reset();
            state.ClearDirty();

            foreach (KeyValuePair<string, float> kv in Values)
            {
                state.SetRaw(kv.Key, kv.Value);
            }

            // SetRaw 不记脏：恢复后必须整体重解，调用方在此之后应做全量 Solve。
            state.EnforceConstraints();
        }

        public static uint ComputeChecksum(IDictionary<string, float> values)
        {
            // FNV-1a 32：与运行时无关、可跨端复算，因此服务端能独立算一遍做对账。
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offsetBasis;

            List<string> keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);

            for (int i = 0; i < keys.Count; i++)
            {
                byte[] name = Encoding.UTF8.GetBytes(keys[i]);
                for (int b = 0; b < name.Length; b++)
                {
                    hash = (hash ^ name[b]) * prime;
                }

                byte[] bits = BitConverter.GetBytes(values[keys[i]]);
                for (int b = 0; b < bits.Length; b++)
                {
                    hash = (hash ^ bits[b]) * prime;
                }
            }

            return hash;
        }

        public bool VerifyChecksum()
        {
            return ComputeChecksum(Values) == Checksum;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, JsonOpts);
        }

        /// <summary>
        /// 读档该用哪一份外观：<b>已发布优先</b>（"发布后不再可回溯"这条契约在读侧的落地——
        /// 一个已发布的角色重登时必须回到发布那一版，而不是本机留下的更晚草稿），
        /// 没有任何已发布条目时才用最新一条。
        /// 判据放在纯核里是为了能被离线门钉住：这段逻辑原本长在 Actor 的读档动作里，
        /// 一旦写反（比如按"最新"取胜）玩家脸回到草稿、而服务端每次都拒改，现场只会表现为"我改不动脸"。
        /// </summary>
        /// <param name="history">该角色的全部存档，按写入先后排列（越靠后越新）。</param>
        /// <param name="preferPublished">关掉时退化为"永远用最新一条"。</param>
        /// <returns><paramref name="history"/> 里的下标；清单为空或全为 null 时返回 -1。</returns>
        public static int SelectRestoreIndex(List<MetaHumanLook> history, bool preferPublished)
        {
            if (history == null || history.Count == 0)
            {
                return -1;
            }

            if (preferPublished)
            {
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    if (history[i] != null && history[i].Published)
                    {
                        return i;
                    }
                }
            }

            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i] != null)
                {
                    return i;
                }
            }

            return -1;
        }

        public static MetaHumanLook FromJson(string json)
        {
            MetaHumanLook look = JsonSerializer.Deserialize<MetaHumanLook>(json, JsonOpts);
            if (look == null)
            {
                throw new InvalidDataException("外观 JSON 反序列化得到 null");
            }

            if (look.Values == null)
            {
                look.Values = new Dictionary<string, float>();
            }

            return look;
        }

        private const uint Magic = 0x4B4C484Du; // 'MHKL'
        private const byte BinaryVersion = 2;
        private const byte BinaryVersionLegacy = 1;

        /// <summary>
        /// 紧凑二进制编码：每个差量 = 4 字节值 + 2 字节长度前缀 + UTF-8 键名，
        /// 对应「玩家改了几个数值就传几个数」的网络预算。
        /// </summary>
        public byte[] ToBytes()
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(BinaryVersion);
                WriteString(w, TemplateId);
                WriteString(w, TemplateVersion);
                WriteString(w, SourcePresetId);
                w.Write(TemplateParamCount);
                w.Write(LinkSymmetric ? (byte)1 : (byte)0);
                w.Write(Published ? (byte)1 : (byte)0);
                w.Write(SavedAtUtcTicks);
                w.Write(Revision);
                w.Write(Values.Count);

                foreach (KeyValuePair<string, float> kv in Values)
                {
                    w.Write(kv.Value);
                    WriteString(w, kv.Key);
                }

                w.Write(Checksum);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static MetaHumanLook FromBytes(byte[] data)
        {
            if (data == null || data.Length < 4)
            {
                throw new InvalidDataException("外观字节流为空");
            }

            using (MemoryStream ms = new MemoryStream(data))
            using (BinaryReader r = new BinaryReader(ms, Encoding.UTF8))
            {
                uint magic = r.ReadUInt32();
                if (magic != Magic)
                {
                    throw new InvalidDataException("外观字节流魔数不匹配：" + magic);
                }

                byte version = r.ReadByte();
                if (version != BinaryVersion && version != BinaryVersionLegacy)
                {
                    throw new InvalidDataException("外观字节流版本 " + version + " 不被支持");
                }

                MetaHumanLook look = new MetaHumanLook();
                look.TemplateId = ReadString(r);
                look.TemplateVersion = ReadString(r);
                look.SourcePresetId = ReadString(r);
                look.TemplateParamCount = r.ReadInt32();
                look.LinkSymmetric = r.ReadByte() == 1;
                look.Published = r.ReadByte() == 1;
                look.SavedAtUtcTicks = r.ReadInt64();

                // v1 没有修订号：保持 0，让调用方回退到 ticks 比较（不能用 ticks 冒充修订号，
                // 否则旧档会和新的小整数计数混在一条时间轴上）。
                look.Revision = version >= 2 ? r.ReadInt64() : 0;

                int count = r.ReadInt32();
                if (count < 0 || count > 4096)
                {
                    throw new InvalidDataException("外观差量数量非法：" + count);
                }

                look.Values = new Dictionary<string, float>();
                for (int i = 0; i < count; i++)
                {
                    float value = r.ReadSingle();
                    string key = ReadString(r);
                    look.Values[key] = value;
                }

                look.Checksum = r.ReadUInt32();
                return look;
            }
        }

        /// <summary>
        /// 回读校验：模板必须认得每个键，且下标不得越过导出时记录的参数个数。
        /// 槽位漂移不报错就会让所有旧存档整体平移，这里选择大声失败。
        /// </summary>
        public List<string> ValidateAgainst(MetaHumanTemplate template)
        {
            List<string> problems = new List<string>();
            if (template == null)
            {
                problems.Add("模板为空");
                return problems;
            }

            if (!string.IsNullOrEmpty(TemplateId) && template.TemplateId != TemplateId)
            {
                problems.Add("模板 Id 不匹配：快照 " + TemplateId + "，当前 " + template.TemplateId);
            }

            if (TemplateParamCount > 0 && template.Parameters.Count < TemplateParamCount)
            {
                problems.Add("当前模板参数数 " + template.Parameters.Count + " 少于导出时的 " + TemplateParamCount + "，槽位可能已漂移");
            }

            foreach (KeyValuePair<string, float> kv in Values)
            {
                MetaHumanParameter p = template.Find(kv.Key);
                if (p == null)
                {
                    problems.Add("快照引用了当前模板不存在的参数 " + kv.Key);
                }
                else if (kv.Value < p.Min || kv.Value > p.Max)
                {
                    problems.Add("快照值 " + kv.Value + " 越出 " + kv.Key + " 的区间 [" + p.Min + "," + p.Max + "]");
                }
            }

            if (!VerifyChecksum())
            {
                problems.Add("校验和不匹配，数据可能损坏或被篡改");
            }

            return problems;
        }

        private static void WriteString(BinaryWriter w, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                w.Write((ushort)0);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new InvalidDataException("字符串过长：" + value);
            }

            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        private static string ReadString(BinaryReader r)
        {
            ushort length = r.ReadUInt16();
            if (length == 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(r.ReadBytes(length));
        }
    }
}
