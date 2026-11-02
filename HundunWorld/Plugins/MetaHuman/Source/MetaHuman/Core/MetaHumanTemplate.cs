using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaHuman
{
    /// <summary>
    /// 数字人模板：开发期由编辑器产出、运行期驱动玩家自定义的唯一数据源。
    /// 它把「玩家能改的数值」与「资产里真实存在的 blendshape/骨骼/材质参数」之间的映射表固化下来，
    /// 因此运行期不需要任何反射或名字猜测。
    /// </summary>
    [Serializable]
    public class MetaHumanTemplate
    {
        public string TemplateId { get; set; }

        public string DisplayName { get; set; }

        public MetaHumanVersion Version { get; set; }

        public string Author { get; set; }

        /// <summary>源资产路径（Flax 资产相对路径），由编辑器绑定时写入。</summary>
        public string HeadAsset { get; set; }
        public string BodyAsset { get; set; }
        public string EyeLeftAsset { get; set; }
        public string EyeRightAsset { get; set; }
        public string TeethAsset { get; set; }
        public string EyelashAsset { get; set; }
        public string HairAsset { get; set; }

        /// <summary>LOD 档数；UE 侧 MetaHuman 到 LOD7，模板记录实际导入到的档数。</summary>
        public int LodCount { get; set; }

        /// <summary>
        /// 骨架自动绑定的槽位表：规范槽位名 → 候选骨骼名（按序尝试）。
        /// 自动绑骨只认这张表，不认显示名。
        /// </summary>
        public Dictionary<string, List<string>> SkeletonSlotMap { get; set; }

        /// <summary>资产里真实可用的 blendshape 名录（LOD0），由编辑器扫描 SkinnedModel.BlendShapes 得到。</summary>
        public List<string> MorphCatalog { get; set; }

        /// <summary>有序参数表；Index 即紧凑快照的下标，装配时回填，顺序变化等同于破坏兼容。</summary>
        public List<MetaHumanParameter> Parameters { get; set; }

        public List<MetaHumanPreset> Presets { get; set; }

        public List<MetaHumanConstraint> Constraints { get; set; }

        /// <summary>导入该模板的资产时必须开启 blendshape 导入；默认关会导致所有形状参数静默失效。</summary>
        public bool RequiresBlendShapeImport { get; set; }

        [NonSerialized]
        private Dictionary<string, MetaHumanParameter> _byId;

        [NonSerialized]
        private bool _indexed;

        [NonSerialized]
        private int _indexedCount = -1;

        public MetaHumanTemplate()
        {
            Version = new MetaHumanVersion(1, 0, 0);
            SkeletonSlotMap = new Dictionary<string, List<string>>();
            MorphCatalog = new List<string>();
            Parameters = new List<MetaHumanParameter>();
            Presets = new List<MetaHumanPreset>();
            Constraints = new List<MetaHumanConstraint>();
            LodCount = 1;
            RequiresBlendShapeImport = true;
        }

        private static readonly JsonSerializerOptions JsonOpts = CreateJsonOptions();

        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.WriteIndented = true;
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new JsonStringEnumConverter());
            // MetaHumanVec3/MetaHumanColor 是公开字段的结构体：STJ 默认只绑属性，
            // 不打开这个开关效果轴会静默读成 (0,0,0) —— 数值全对、模型一动不动。
            options.IncludeFields = true;
            return options;
        }

        public void EnsureIndexed()
        {
            // 只认「已经建过索引」是不够的：编辑器回填 morph 会往 Parameters 里追加参数，
            // 那时 _indexed 已经是 true，早退就让新滑杆进不了 _byId ——
            // 界面上看得见、拖上去毫无反应，正是最难查的那类缺陷。条目数一变就重建。
            if (_indexed && _byId != null && _indexedCount == Parameters.Count)
            {
                return;
            }

            _byId = new Dictionary<string, MetaHumanParameter>();
            for (int i = 0; i < Parameters.Count; i++)
            {
                MetaHumanParameter p = Parameters[i];
                p.Index = i;
                if (!string.IsNullOrEmpty(p.Id))
                {
                    _byId[p.Id] = p;
                }
            }

            _indexedCount = Parameters.Count;
            _indexed = true;
        }

        public MetaHumanParameter Find(string paramId)
        {
            EnsureIndexed();
            MetaHumanParameter p;
            return paramId != null && _byId.TryGetValue(paramId, out p) ? p : null;
        }

        public int IndexOf(string paramId)
        {
            MetaHumanParameter p = Find(paramId);
            return p == null ? -1 : p.Index;
        }

        public IEnumerable<MetaHumanParameter> All()
        {
            EnsureIndexed();
            return Parameters;
        }

        /// <summary>按区域筛选（玩家界面的页签划分依据）。</summary>
        public List<MetaHumanParameter> InRegion(MetaHumanRegion region)
        {
            EnsureIndexed();
            List<MetaHumanParameter> result = new List<MetaHumanParameter>();
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (Parameters[i].Region == region)
                {
                    result.Add(Parameters[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// 玩家界面的头身分界：除 Body 区域以外一律算头部，按固定显示顺序排好。
        /// 分界由数据自己决定，不接受调用方自带区域清单 —— 清单漏一个区域，那批滑杆就整组不进界面，
        /// 而页签、状态栏、自检全都一切如常（本项目脸模板里 Forehead/Scalp/Teeth/Tongue/Hair/Skin
        /// 共 154 条、占 26%，就是被一份十条的旧清单漏掉的）。
        /// 顺序表里没有的区域排到最后，但照样会显示出来。
        /// </summary>
        public void PartitionForUI(List<MetaHumanParameter> head, List<MetaHumanParameter> body)
        {
            EnsureIndexed();
            for (int i = 0; i < Parameters.Count; i++)
            {
                MetaHumanParameter parameter = Parameters[i];
                if (parameter == null)
                {
                    continue;
                }

                if (parameter.Region == MetaHumanRegion.Body)
                {
                    body.Add(parameter);
                }
                else
                {
                    head.Add(parameter);
                }
            }

            head.Sort(delegate (MetaHumanParameter a, MetaHumanParameter b)
            {
                int rankA = HeadRegionRank(a.Region);
                int rankB = HeadRegionRank(b.Region);
                if (rankA != rankB)
                {
                    return rankA.CompareTo(rankB);
                }

                return a.Index.CompareTo(b.Index);
            });
        }

        /// <summary>头部区域的显示顺序；不在表里的排最后（仍然显示，不再被静默丢掉）。</summary>
        public static int HeadRegionRank(MetaHumanRegion region)
        {
            for (int i = 0; i < HeadRegionOrder.Length; i++)
            {
                if (HeadRegionOrder[i] == region)
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        private static readonly MetaHumanRegion[] HeadRegionOrder =
        {
            MetaHumanRegion.Head, MetaHumanRegion.Forehead, MetaHumanRegion.Brow, MetaHumanRegion.Eye,
            MetaHumanRegion.EyeShell, MetaHumanRegion.Nose, MetaHumanRegion.Lip, MetaHumanRegion.Chin,
            MetaHumanRegion.Cheek, MetaHumanRegion.Ear, MetaHumanRegion.Jaw, MetaHumanRegion.Teeth,
            MetaHumanRegion.Tongue, MetaHumanRegion.Scalp, MetaHumanRegion.Hair, MetaHumanRegion.Skin,
            MetaHumanRegion.Neck,
        };
        /// <summary>按层级筛选（semantic 上普通界面，advanced 只在专家模式里出现）。</summary>
        public List<MetaHumanParameter> InGroup(string group)
        {
            EnsureIndexed();
            List<MetaHumanParameter> result = new List<MetaHumanParameter>();
            for (int i = 0; i < Parameters.Count; i++)
            {
                string g = string.IsNullOrEmpty(Parameters[i].Group) ? "semantic" : Parameters[i].Group;
                if (g == group)
                {
                    result.Add(Parameters[i]);
                }
            }

            return result;
        }

        public List<MetaHumanParameter> InKind(MetaHumanParamKind kind)
        {
            EnsureIndexed();
            List<MetaHumanParameter> result = new List<MetaHumanParameter>();
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (Parameters[i].Kind == kind)
                {
                    result.Add(Parameters[i]);
                }
            }

            return result;
        }

        public MetaHumanPreset FindPreset(string presetId)
        {
            for (int i = 0; i < Presets.Count; i++)
            {
                if (Presets[i].Id == presetId)
                {
                    return Presets[i];
                }
            }

            return null;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, JsonOpts);
        }

        public static MetaHumanTemplate FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new ArgumentException("模板 JSON 为空", "json");
            }

            MetaHumanTemplate template = JsonSerializer.Deserialize<MetaHumanTemplate>(json, JsonOpts);
            if (template == null)
            {
                throw new InvalidDataException("模板 JSON 反序列化得到 null");
            }

            template.EnsureIndexed();
            return template;
        }

        public void SaveToFile(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 默认就是 UTF-8 无 BOM：显式传 UTF8Encoding 需要额外程序集引用，而 BOM 会弄坏后续解析
            File.WriteAllText(path, ToJson());
        }

        public static MetaHumanTemplate LoadFromFile(string path)
        {
            return FromJson(File.ReadAllText(path));
        }

        /// <summary>
        /// 模板自检。返回错误清单（空表示通过）。
        /// 这一层存在的意义：映射表写错时，表现是「改了数值模型没反应」这种极难定位的静默失效，
        /// 所以装配期就把「引用了不存在的 morph / 骨骼 / 重复 Id / 区间非法」全部拦下来。
        /// </summary>
        public List<string> Validate()
        {
            EnsureIndexed();
            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(TemplateId))
            {
                errors.Add("TemplateId 为空");
            }

            HashSet<string> seen = new HashSet<string>();
            HashSet<string> morphSet = new HashSet<string>(MorphCatalog);

            for (int i = 0; i < Parameters.Count; i++)
            {
                MetaHumanParameter p = Parameters[i];
                string where = "参数[" + i + "] " + (p.Id ?? "<无 Id>");

                if (string.IsNullOrEmpty(p.Id))
                {
                    errors.Add(where + "：缺少 Id");
                    continue;
                }

                if (!seen.Add(p.Id))
                {
                    errors.Add(where + "：Id 重复");
                }

                if (p.Max < p.Min)
                {
                    errors.Add(where + "：Max < Min");
                }
                else if (p.Default < p.Min || p.Default > p.Max)
                {
                    errors.Add(where + "：Default 越界 [" + p.Min + "," + p.Max + "]");
                }

                if (!string.IsNullOrEmpty(p.PairId) && Find(p.PairId) == null)
                {
                    errors.Add(where + "：PairId 指向不存在的参数 " + p.PairId);
                }

                if ((p.Side == MetaHumanSide.Left || p.Side == MetaHumanSide.Right) && string.IsNullOrEmpty(p.PairId))
                {
                    errors.Add(where + "：侧向参数必须声明 PairId，否则联动会单边失效");
                }

                if (p.Effects == null || p.Effects.Count == 0)
                {
                    errors.Add(where + "：没有任何效果，该数值改了不会有变化");
                    continue;
                }

                for (int e = 0; e < p.Effects.Count; e++)
                {
                    MetaHumanEffect effect = p.Effects[e];
                    if (string.IsNullOrEmpty(effect.Target))
                    {
                        errors.Add(where + "：效果缺少 Target");
                        continue;
                    }

                    if (effect.Kind == MetaHumanEffectKind.Morph && morphSet.Count > 0 && !morphSet.Contains(effect.Target))
                    {
                        errors.Add(where + "：引用了 MorphCatalog 里不存在的 blendshape " + effect.Target);
                    }

                    // 贴图效果必须至少给一张图：Off/On 都空时解算会把该目标从贴图表里摘掉，
                    // 于是这根滑杆拖到底也换不了任何东西 —— 与"参数没有效果"同一类静默失效。
                    if (effect.Kind == MetaHumanEffectKind.MaterialTexture
                        && string.IsNullOrEmpty(effect.TextureOff)
                        && string.IsNullOrEmpty(effect.TextureOn))
                    {
                        errors.Add(where + "：贴图效果没指定任何一张贴图（TextureOff/TextureOn 都空），这一档不会写任何东西");
                    }
                }
            }

            for (int c = 0; c < Constraints.Count; c++)
            {
                MetaHumanConstraint constraint = Constraints[c];
                if (constraint.ParamIds == null || constraint.ParamIds.Count == 0)
                {
                    errors.Add("约束[" + c + "]：参数列表为空");
                    continue;
                }

                for (int k = 0; k < constraint.ParamIds.Count; k++)
                {
                    if (Find(constraint.ParamIds[k]) == null)
                    {
                        errors.Add("约束[" + c + "]：引用了不存在的参数 " + constraint.ParamIds[k]);
                    }
                }
            }

            for (int i = 0; i < Presets.Count; i++)
            {
                MetaHumanPreset preset = Presets[i];
                if (preset == null)
                {
                    continue;
                }

                // 死预设：既不声明"回默认"，又一条值都没有 —— 玩家点了以后什么都没发生，且不报错。
                if (!preset.ResetToDefault && preset.Values.Count == 0)
                {
                    errors.Add("预设[" + i + "]（" + preset.Id + "）是死的：既没标 ResetToDefault 也没有任何值");
                }

                foreach (KeyValuePair<string, float> kv in Presets[i].Values)
                {
                    MetaHumanParameter p = Find(kv.Key);
                    if (p == null)
                    {
                        errors.Add("预设[" + i + "]：引用了不存在的参数 " + kv.Key);
                    }
                    else if (kv.Value < p.Min || kv.Value > p.Max)
                    {
                        errors.Add("预设[" + i + "]：" + kv.Key + " 的值 " + kv.Value + " 越界");
                    }
                }
            }

            foreach (KeyValuePair<string, List<string>> slot in SkeletonSlotMap)
            {
                if (slot.Value == null || slot.Value.Count == 0)
                {
                    errors.Add("骨架槽位 " + slot.Key + "：候选名为空");
                }
            }

            return errors;
        }

        /// <summary>
        /// 多模板合成一个角色模板（脸 + 身 + 发…）。参数 Id 全局唯一是硬要求：
        /// 快照按 Id 存差量，撞名会让两个模板的同号参数互相覆盖。
        /// </summary>
        public static MetaHumanTemplate Merge(IEnumerable<MetaHumanTemplate> parts)
        {
            MetaHumanTemplate merged = new MetaHumanTemplate();
            List<string> ids = new List<string>();
            StringBuilder idText = new StringBuilder();

            foreach (MetaHumanTemplate part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                if (idText.Length > 0)
                {
                    idText.Append('+');
                }

                idText.Append(part.TemplateId);
                ids.Add(part.TemplateId);

                for (int i = 0; i < part.Parameters.Count; i++)
                {
                    merged.Parameters.Add(part.Parameters[i]);
                }

                for (int i = 0; i < part.Presets.Count; i++)
                {
                    merged.Presets.Add(part.Presets[i]);
                }

                for (int i = 0; i < part.Constraints.Count; i++)
                {
                    merged.Constraints.Add(part.Constraints[i]);
                }

                foreach (string morph in part.MorphCatalog)
                {
                    if (!merged.MorphCatalog.Contains(morph))
                    {
                        merged.MorphCatalog.Add(morph);
                    }
                }

                foreach (KeyValuePair<string, List<string>> slot in part.SkeletonSlotMap)
                {
                    if (!merged.SkeletonSlotMap.ContainsKey(slot.Key))
                    {
                        merged.SkeletonSlotMap[slot.Key] = slot.Value;
                    }
                }

                if (string.IsNullOrEmpty(merged.HeadAsset))
                {
                    merged.HeadAsset = part.HeadAsset;
                }

                if (string.IsNullOrEmpty(merged.BodyAsset))
                {
                    merged.BodyAsset = part.BodyAsset;
                }

                merged.LodCount = Math.Max(merged.LodCount, part.LodCount);
                merged.RequiresBlendShapeImport = merged.RequiresBlendShapeImport || part.RequiresBlendShapeImport;
            }

            merged.TemplateId = idText.ToString();
            merged.DisplayName = string.Join(" + ", ids.ToArray());
            merged.Version = new MetaHumanVersion(1, 0, 0);
            merged.EnsureIndexed();
            return merged;
        }

        /// <summary>模板里出现过的全部骨骼目标（供绑骨核对与「骨骼是否存在」检查）。</summary>
        public HashSet<string> BoneTargets()
        {
            EnsureIndexed();
            HashSet<string> result = new HashSet<string>();
            for (int i = 0; i < Parameters.Count; i++)
            {
                List<MetaHumanEffect> effects = Parameters[i].Effects;
                for (int e = 0; e < effects.Count; e++)
                {
                    if (effects[e].Kind == MetaHumanEffectKind.BoneScale || effects[e].Kind == MetaHumanEffectKind.BoneTranslate)
                    {
                        result.Add(effects[e].Target);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 这份模板会写到哪些材质参数名上。**三条材质通道都要算进来**：
        /// 少算贴图那一档，调用方就会以为"这具角色没有材质需求"，于是根本不分配材质实例，
        /// 结果浮点/颜色照写、贴图静默不写（通道存在但没有消费者）。
        /// </summary>
        public HashSet<string> MaterialTargets()
        {
            EnsureIndexed();
            HashSet<string> result = new HashSet<string>();
            for (int i = 0; i < Parameters.Count; i++)
            {
                List<MetaHumanEffect> effects = Parameters[i].Effects;
                for (int e = 0; e < effects.Count; e++)
                {
                    if (effects[e].Kind == MetaHumanEffectKind.MaterialFloat
                        || effects[e].Kind == MetaHumanEffectKind.MaterialColor
                        || effects[e].Kind == MetaHumanEffectKind.MaterialTexture)
                    {
                        result.Add(effects[e].Target);
                    }
                }
            }

            return result;
        }
    }
}
