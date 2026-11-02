using FlaxEngine;
using Game.Character.Attributes;
using Horizon.Game.Message.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HundunWorld.Game.Combat.Skills
{
    /// <summary>
    /// 技能配置数据（JSON 可序列化）
    /// </summary>
    [Serializable]
    public class SkillConfig
    {
        [JsonPropertyName("id")]
        public int SkillId { get; set; }

        [JsonPropertyName("name")]
        public string SkillName { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("icon")]
        public string IconPath { get; set; } = "";

        [JsonPropertyName("element")]
        public string Element { get; set; } = "None";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "ActiveAttack";

        [JsonPropertyName("damageMultiplier")]
        public float DamageMultiplier { get; set; } = 1.0f;

        [JsonPropertyName("energyCost")]
        public float EnergyCost { get; set; } = 0f;

        [JsonPropertyName("cooldown")]
        public float Cooldown { get; set; } = 1f;

        [JsonPropertyName("range")]
        public float Range { get; set; } = 5f;

        [JsonPropertyName("castTime")]
        public float CastTime { get; set; } = 0f;

        [JsonPropertyName("rangeType")]
        public string RangeType { get; set; } = "Single";

        [JsonPropertyName("aoeRadius")]
        public float AoeRadius { get; set; } = 0f;

        [JsonPropertyName("aoeAngle")]
        public float AoeAngle { get; set; } = 90f;

        [JsonPropertyName("requiredLevel")]
        public int RequiredLevel { get; set; } = 1;

        [JsonPropertyName("maxLevel")]
        public int MaxLevel { get; set; } = 10;

        [JsonPropertyName("critRate")]
        public float CritRate { get; set; } = 0.05f;

        [JsonPropertyName("critMultiplier")]
        public float CritMultiplier { get; set; } = 1.5f;

        /// <summary>连招：下一招技能ID（0=无连招）</summary>
        [JsonPropertyName("comboNextId")]
        public int ComboNextId { get; set; } = 0;

        /// <summary>连招：窗口时间（秒），超时则连招中断</summary>
        [JsonPropertyName("comboWindow")]
        public float ComboWindow { get; set; } = 0.8f;

        /// <summary>连招：在连招链中的序号（0=起手招）</summary>
        [JsonPropertyName("comboIndex")]
        public int ComboIndex { get; set; } = 0;

        /// <summary>施法动画名称</summary>
        [JsonPropertyName("castAnimation")]
        public string CastAnimation { get; set; } = "";

        /// <summary>施法音效路径</summary>
        [JsonPropertyName("castSound")]
        public string CastSound { get; set; } = "";

        /// <summary>命中音效路径</summary>
        [JsonPropertyName("hitSound")]
        public string HitSound { get; set; } = "";

        /// <summary>特效路径</summary>
        [JsonPropertyName("vfxPath")]
        public string VfxPath { get; set; } = "";

        /// <summary>附带效果列表</summary>
        [JsonPropertyName("effects")]
        public List<SkillEffectConfig> Effects { get; set; } = new List<SkillEffectConfig>();

        /// <summary>等级缩放：每级伤害加成</summary>
        [JsonPropertyName("damagePerLevel")]
        public float DamagePerLevel { get; set; } = 0.1f;

        /// <summary>是否可移动施法</summary>
        [JsonPropertyName("canMoveWhileCasting")]
        public bool CanMoveWhileCasting { get; set; } = false;

        /// <summary>击退力度（0=无击退）</summary>
        [JsonPropertyName("knockbackForce")]
        public float KnockbackForce { get; set; } = 0f;

        /// <summary>阶位：1白 2黄 3玄 4地 5天 6道（相生演化，超越天阶）7混沌。0=不属于五行阶梯（旧技能/身法）。</summary>
        [JsonPropertyName("grade")]
        public int Grade { get; set; } = 0;

        /// <summary>阶内境界序号：1初 2中 3极。三境各是一式独立技能，不是同一技的等级。</summary>
        [JsonPropertyName("gradeStage")]
        public int GradeStage { get; set; } = 0;

        /// <summary>技系标识（五行_阶，如 Metal_4），供技能树把同阶三式归成一组。</summary>
        [JsonPropertyName("seriesId")]
        public string SeriesId { get; set; } = "";

        /// <summary>相生对序号（1金水 2水木 3木火 4火土 5土金），非演化式为 0。</summary>
        [JsonPropertyName("pair")]
        public int Pair { get; set; } = 0;

        /// <summary>阶内领悟门槛（该行五行亲和需达到此值）。</summary>
        [JsonPropertyName("comprehensionRequire")]
        public int ComprehensionRequire { get; set; } = 0;

        /// <summary>前置招式 id（同阶上一境，或上一阶极境；0=无）。</summary>
        [JsonPropertyName("preSkillId")]
        public int PreSkillId { get; set; } = 0;

        /// <summary>演化式回指被它融合掉的两个源技 id；非演化式为空数组。</summary>
        [JsonPropertyName("evolveFrom")]
        public int[] EvolveFrom { get; set; } = System.Array.Empty<int>();

        /// <summary>研读此式所需的累计周天圆满次数（§6.2）；0 = 无此门槛。</summary>
        [JsonPropertyName("loopNeed")]
        public int LoopNeed { get; set; } = 0;

        /// <summary>所需的圆满道阶<b>系</b>数（一对三境俱全算一系）；0 = 无此门槛。只有混沌终式用。</summary>
        [JsonPropertyName("needPairsCompleted")]
        public int NeedPairsCompleted { get; set; } = 0;

        /// <summary>
        /// 服务端 <c>learn_cost</c> 的 §14 动态门槛（好感 / 材料 / 基础属性）。
        /// <para>
        /// 客户端只用来<b>显示还差什么</b>，一律不参与放行——判定在 SkillGrain。
        /// 这里也不报"当前值"：好感度还没有下行通道，显示一个猜出来的 0 比不显示更容易骗到人。
        /// </para>
        /// </summary>
        [JsonPropertyName("learnCost")]
        public SkillLearnCostDto LearnCost { get; set; }

        /// <summary>learn_cost 的服务端语义（§14）。字段名与 JSON 键一一对应，别在客户端另立一套叫法。</summary>
        public sealed class SkillLearnCostDto
        {
            [JsonPropertyName("favorNeed")] public SkillFavorNeedDto FavorNeed { get; set; }
            [JsonPropertyName("materials")] public List<SkillMaterialNeedDto> Materials { get; set; }
            [JsonPropertyName("attrNeed")] public SkillAttrNeedDto AttrNeed { get; set; }
        }

        public sealed class SkillFavorNeedDto
        {
            /// <summary>1金 2木 3水 4火 5土。</summary>
            [JsonPropertyName("domain")] public int Domain { get; set; }
            /// <summary>true = 五行域都要达到 Min（混沌终式）。</summary>
            [JsonPropertyName("allDomains")] public bool AllDomains { get; set; }
            [JsonPropertyName("min")] public int Min { get; set; }
        }

        public sealed class SkillMaterialNeedDto
        {
            [JsonPropertyName("itemId")] public int ItemId { get; set; }
            [JsonPropertyName("count")] public int Count { get; set; }
        }

        public sealed class SkillAttrNeedDto
        {
            [JsonPropertyName("strength")] public int Strength { get; set; }
            [JsonPropertyName("agility")] public int Agility { get; set; }
            [JsonPropertyName("health")] public int Health { get; set; }
            [JsonPropertyName("intelligence")] public int Intelligence { get; set; }
        }

        // ===== 运行时辅助方法 =====

        public WuxingElement GetWuxingElement() => Element switch
        {
            "Metal" => WuxingElement.Metal,
            "Wood" => WuxingElement.Wood,
            "Water" => WuxingElement.Water,
            "Fire" => WuxingElement.Fire,
            "Earth" => WuxingElement.Earth,
            _ => WuxingElement.None
        };

        public SkillType GetSkillType() => Type switch
        {
            "ActiveAttack" => SkillType.ActiveAttack,
            "Control" => SkillType.Control,
            "Dash" => SkillType.Dash,
            "Support" => SkillType.Support,
            "Ultimate" => SkillType.Ultimate,
            _ => SkillType.ActiveAttack
        };

        public HundunWorld.Game.ECS.Components.RangeType GetRangeType() => RangeType switch
        {
            "Circle" => HundunWorld.Game.ECS.Components.RangeType.Circle,
            "Sector" => HundunWorld.Game.ECS.Components.RangeType.Sector,
            "Rectangle" => HundunWorld.Game.ECS.Components.RangeType.Rectangle,
            "Line" => HundunWorld.Game.ECS.Components.RangeType.Line,
            _ => HundunWorld.Game.ECS.Components.RangeType.Single
        };

        /// <summary>计算指定等级的伤害倍率</summary>
        public float GetDamageMultiplierAtLevel(int level)
        {
            return DamageMultiplier + DamagePerLevel * (level - 1);
        }
    }

    /// <summary>
    /// 技能附带效果配置
    /// </summary>
    [Serializable]
    public class SkillEffectConfig
    {
        [JsonPropertyName("type")]
        public string EffectType { get; set; } = "";

        [JsonPropertyName("duration")]
        public float Duration { get; set; } = 0f;

        [JsonPropertyName("value")]
        public float Value { get; set; } = 0f;

        [JsonPropertyName("chance")]
        public float Chance { get; set; } = 1.0f;
    }

    /// <summary>
    /// <c>skills.json</c> 的文件根。数组本体不能作为根：Flax 会把 Content 下每个 .json 当 JSON 存储资产
    /// 探测成员，rapidjson 对非对象根直接断言崩溃。
    /// </summary>
    [Serializable]
    public class SkillCatalogFile
    {
        [JsonPropertyName("Skills")]
        public List<SkillConfig> Skills { get; set; } = new List<SkillConfig>();
    }

    /// <summary>
    /// 数据驱动的技能数据库。
    /// 支持从 JSON 文件加载技能配置，也包含内置默认技能作为回退。
    /// </summary>
    public static class SkillDatabase
    {
        private static Dictionary<int, SkillConfig> _skills = new Dictionary<int, SkillConfig>();
        private static bool _initialized = false;
        private static readonly object _lock = new object();

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>技能数据文件路径</summary>
        public static string SkillDataPath = "Content/GameData/Skills/skills.json";

        /// <summary>是否已初始化</summary>
        public static bool IsInitialized => _initialized;

        /// <summary>已加载技能数量</summary>
        public static int Count => _skills.Count;

        /// <summary>
        /// 初始化技能数据库（从 JSON 加载 + 内置回退）
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;

                _skills.Clear();

                // 先铺内置表再叠加 JSON：内置那份带着 SkillBase 运行时用的旧 id（1001-6002），
                // 若 JSON 一到就把整张表换掉，那些旧 id 会在目录里凭空消失（GetSkill 返回 null）。
                // 叠加语义与 ApplyServerTemplates 一致：同 id 以数据文件为准，文件里没有的行保留。
                LoadBuiltInSkills();

                // 1. 尝试从 JSON 文件加载
                bool loaded = TryLoadFromJson();

                // 2. 数据文件缺失时只剩内置那 26 条，日志要说明清楚，别让人以为读到了技能树
                if (!loaded)
                {
                    Debug.Log($"[SkillDatabase] 未读到 skills.json，仅使用内置技能数据 ({_skills.Count} 个技能)");
                }
                else
                {
                    Debug.Log($"[SkillDatabase] 内置表 + skills.json 叠加后共 {_skills.Count} 个技能");
                }

                _initialized = true;
            }
        }

        /// <summary>
        /// 从 JSON 文件加载技能数据
        /// </summary>
        private static bool TryLoadFromJson()
        {
            try
            {
                string fullPath = Path.Combine(Globals.ProjectFolder, SkillDataPath);
                if (!File.Exists(fullPath))
                {
                    Debug.Log($"[SkillDatabase] 技能数据文件不存在: {fullPath}");
                    return false;
                }

                string json = File.ReadAllText(fullPath);
                // 文件根必须是对象（{"Skills":[...]}）：Flax 启动时会把 Content 下每个 .json 当 JSON 存储资产
                // 探测根成员，rapidjson 的 FindMember 对数组根直接断言崩编辑器。网络侧的裸数组走 LoadFromJsonString。
                var catalog = JsonSerializer.Deserialize<SkillCatalogFile>(json, _jsonOptions);
                var skills = catalog?.Skills;
                if (skills == null || skills.Count == 0) return false;

                foreach (var skill in skills)
                {
                    if (skill.SkillId > 0)
                    {
                        _skills[skill.SkillId] = skill;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillDatabase] JSON 加载失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从 JSON 字符串加载技能（用于网络同步或热更新）
        /// </summary>
        public static void LoadFromJsonString(string json)
        {
            try
            {
                var skills = JsonSerializer.Deserialize<List<SkillConfig>>(json, _jsonOptions);
                if (skills == null) return;

                lock (_lock)
                {
                    foreach (var skill in skills)
                    {
                        if (skill.SkillId > 0)
                        {
                            _skills[skill.SkillId] = skill;
                        }
                    }
                }
                Debug.Log($"[SkillDatabase] 热更新加载了 {skills.Count} 个技能");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SkillDatabase] 热更新加载失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取技能配置
        /// </summary>
        public static SkillConfig GetSkill(int skillId)
        {
            if (!_initialized) Initialize();
            return _skills.TryGetValue(skillId, out var skill) ? skill : null;
        }

        /// <summary>
        /// 获取所有技能
        /// </summary>
        public static List<SkillConfig> GetAllSkills()
        {
            if (!_initialized) Initialize();
            return _skills.Values.ToList();
        }

        /// <summary>
        /// 按五行属性筛选技能
        /// </summary>
        public static List<SkillConfig> GetSkillsByElement(WuxingElement element)
        {
            if (!_initialized) Initialize();
            string elementName = element.ToString();
            return _skills.Values.Where(s => s.Element == elementName).ToList();
        }

        /// <summary>
        /// 按技能类型筛选
        /// </summary>
        public static List<SkillConfig> GetSkillsByType(SkillType type)
        {
            if (!_initialized) Initialize();
            string typeName = type.ToString();
            return _skills.Values.Where(s => s.Type == typeName).ToList();
        }

        /// <summary>
        /// 获取连招链（从起手招开始）
        /// </summary>
        public static List<SkillConfig> GetComboChain(int startSkillId)
        {
            if (!_initialized) Initialize();
            var chain = new List<SkillConfig>();
            int currentId = startSkillId;
            int safety = 0;

            while (currentId > 0 && safety < 20)
            {
                if (!_skills.TryGetValue(currentId, out var skill)) break;
                chain.Add(skill);
                currentId = skill.ComboNextId;
                safety++;
            }
            return chain;
        }

        /// <summary>
        /// 获取指定等级可用的技能
        /// </summary>
        public static List<SkillConfig> GetAvailableSkills(int playerLevel)
        {
            if (!_initialized) Initialize();
            return _skills.Values.Where(s => s.RequiredLevel <= playerLevel).ToList();
        }

        /// <summary>
        /// 注册/覆盖单个技能（运行时动态添加）
        /// </summary>
        public static void RegisterSkill(SkillConfig skill)
        {
            if (skill == null || skill.SkillId <= 0) return;
            lock (_lock)
            {
                _skills[skill.SkillId] = skill;
            }
        }

        /// <summary>
        /// 用服务端下行的技能表覆盖本地这一份。
        /// <para>
        /// 只覆盖两端重叠的字段（名字/描述/五行/消耗/冷却/施法/射程/等级上限/学习等级），
        /// 连招链、特效、音效、Vfx、暴击这些服务端没有的字段<b>原样保留</b>——
        /// 覆盖它们等于把客户端唯一的数值抹掉。服务端多出来的技能按默认值新建一条。
        /// </para>
        /// </summary>
        /// <returns>(被改写的行数, 新增的行数)</returns>
        public static (int Updated, int Added) ApplyServerTemplates(
            System.Collections.Generic.IList<Horizon.Game.Message.Network.SkillTemplateEntryInfo> templates)
        {
            Initialize();
            if (templates == null || templates.Count == 0) return (0, 0);

            int updated = 0, added = 0;
            foreach (var t in templates)
            {
                if (t == null || t.SkillId <= 0) continue;

                if (_skills.TryGetValue(t.SkillId, out var existing))
                {
                    existing.SkillName = t.SkillName;
                    existing.Description = t.Description;
                    existing.Element = ElementName(t.Element);
                    existing.EnergyCost = t.EnergyCost;
                    existing.Cooldown = t.Cooldown;
                    existing.CastTime = t.CastTime;
                    existing.Range = t.AttackRange;
                    if (t.MaxLevel > 0) existing.MaxLevel = t.MaxLevel;
                    if (t.LearnLevelRequire > 0) existing.RequiredLevel = t.LearnLevelRequire;
                    ApplyLadder(existing, t);
                    updated++;
                }
                else
                {
                    // 服务端有、本地没有：这正说明 skills.json 没跟着内容表重导。
                    // 把阶梯语义一并建出来，否则这一式会因为 Grade=0 被技能树当作"不在五行阶梯上"直接跳过。
                    var cfg = new SkillConfig
                    {
                        SkillId = t.SkillId,
                        SkillName = t.SkillName,
                        Description = t.Description,
                        Element = ElementName(t.Element),
                        EnergyCost = t.EnergyCost,
                        Cooldown = t.Cooldown,
                        CastTime = t.CastTime,
                        Range = t.AttackRange,
                        MaxLevel = t.MaxLevel > 0 ? t.MaxLevel : 10,
                        RequiredLevel = t.LearnLevelRequire,
                    };
                    ApplyLadder(cfg, t);
                    RegisterSkill(cfg);
                    added++;
                }
            }
            return (updated, added);
        }

        /// <summary>
        /// 把下行项里的五行阶梯语义（阶/境/技系/倍率/前置/演化门槛）并入本地配置。
        /// <para>
        /// 只在服务端确实带了阶位时才覆盖：旧技能行的 <c>skill_grade</c> 是 0，无条件写回会把
        /// skills.json 已经画好的格子抹成"不属于五行树"。门槛同理只补缺——本地已有非零值时留本地的，
        /// 一次下行不该把门禁表清空。
        /// </para>
        /// </summary>
        private static void ApplyLadder(SkillConfig cfg, Horizon.Game.Message.Network.SkillTemplateEntryInfo t)
        {
            if (cfg == null || t == null) return;

            if (t.SkillGrade > 0)
            {
                cfg.Grade = t.SkillGrade;
                cfg.GradeStage = t.GradeStage;
                if (!string.IsNullOrEmpty(t.SeriesId)) cfg.SeriesId = t.SeriesId;
                if (t.DamageMultiplier > 0) cfg.DamageMultiplier = (float)t.DamageMultiplier;
            }

            if (t.PreSkillId > 0) cfg.PreSkillId = t.PreSkillId;
            if (string.IsNullOrEmpty(t.LearnCostJson)) return;

            // 演化消耗与周天门槛都塞在 learn_cost 一列里（服务端同款读法见 WuxingEvolveRules.Parse）。
            // 透传 JSON 而不是拆成一个个列：以后加一条门槛不用再动一次网络契约。
            try
            {
                using var doc = JsonDocument.Parse(t.LearnCostJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

                if (doc.RootElement.TryGetProperty("consume", out var consume)
                    && consume.ValueKind == JsonValueKind.Array)
                {
                    var ids = new List<int>();
                    foreach (var item in consume.EnumerateArray())
                        if (item.TryGetInt32(out var id) && id > 0) ids.Add(id);
                    if (ids.Count > 0) cfg.EvolveFrom = ids.ToArray();
                }

                cfg.LoopNeed = ReadPositiveInt(doc.RootElement, "loopNeed", cfg.LoopNeed);
                cfg.NeedPairsCompleted = ReadPositiveInt(doc.RootElement, "needPairsCompleted", cfg.NeedPairsCompleted);
                cfg.ComprehensionRequire = ReadPositiveInt(doc.RootElement, "affinityNeed", cfg.ComprehensionRequire);
            }
            catch (JsonException)
            {
                // learn_cost 也用来存普通式的开销描述，脏值不该连带丢掉上面已经并好的阶梯字段
            }
        }

        private static int ReadPositiveInt(JsonElement root, string name, int fallback) =>
            root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) && parsed > 0 ? parsed : fallback;

        /// <summary>服务端行号（1..5 = 金木水火土，0 = 无属性）→ 本地元素名。</summary>
        private static string ElementName(int element) => element switch
        {
            1 => "Metal",
            2 => "Wood",
            3 => "Water",
            4 => "Fire",
            5 => "Earth",
            _ => "None",
        };

        /// <summary>
        /// 内置默认技能数据（JSON 加载失败时的回退）
        /// </summary>
        private static void LoadBuiltInSkills()
        {
            // ===== 金属性（刚猛/外功）=====
            RegisterSkill(new SkillConfig
            {
                SkillId = 1001, SkillName = "裂金斩", Description = "以刚猛内力凝于剑锋，劈出金色剑气。",
                Element = "Metal", Type = "ActiveAttack", DamageMultiplier = 1.5f,
                EnergyCost = 30f, Cooldown = 3f, Range = 5f, CastTime = 0.4f,
                RangeType = "Sector", AoeAngle = 60f, CritRate = 0.15f,
                CastAnimation = "Attack_Slash_01", CastSound = "/Game/Audio/Skills/Metal_Slash",
                HitSound = "/Game/Audio/Skills/Metal_Hit", VfxPath = "/Game/VFX/Metal_Slash",
                ComboNextId = 1002, ComboWindow = 1.0f, ComboIndex = 0,
                RequiredLevel = 1, MaxLevel = 10, DamagePerLevel = 0.12f
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 1002, SkillName = "断岳击", Description = "裂金斩后续招， upward 挑击将敌人击飞。",
                Element = "Metal", Type = "ActiveAttack", DamageMultiplier = 1.8f,
                EnergyCost = 25f, Cooldown = 0f, Range = 4f, CastTime = 0.3f,
                RangeType = "Single", CritRate = 0.1f, KnockbackForce = 8f,
                CastAnimation = "Attack_Uppercut", CastSound = "/Game/Audio/Skills/Metal_Uppercut",
                HitSound = "/Game/Audio/Skills/Metal_Hit_Heavy", VfxPath = "/Game/VFX/Metal_Launch",
                ComboNextId = 1004, ComboWindow = 0.8f, ComboIndex = 1,
                RequiredLevel = 1, MaxLevel = 10, DamagePerLevel = 0.12f
            });
            // 1003 是金钟罩（运行时 MetalSkills.JinZhongZhao 实现的就是它，动画表 1003 也是 Defense），
            // 万剑归宗没有运行时实现，改到 1004 与它对调（服务端 010 脚本同步搬迁）。
            RegisterSkill(new SkillConfig
            {
                SkillId = 1003, SkillName = "金钟罩", Description = "运起金钟罩，5 秒内减少 50% 所受伤害。",
                Element = "Metal", Type = "Support", DamageMultiplier = 0f,
                EnergyCost = 80f, Cooldown = 20f, Range = 0f, CastTime = 0.5f,
                RangeType = "Single", CritRate = 0f,
                CastAnimation = "Cast_Defense", CastSound = "/Game/Audio/Skills/Metal_Shield",
                HitSound = "", VfxPath = "/Game/VFX/Metal_GoldenBell",
                ComboNextId = 0, ComboIndex = 0,
                RequiredLevel = 5, MaxLevel = 10, DamagePerLevel = 0f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "DamageReduction", Duration = 5f, Value = 0.5f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 1004, SkillName = "万剑归宗", Description = "连招终结技，内力爆发化为无数剑气横扫前方。",
                Element = "Metal", Type = "Ultimate", DamageMultiplier = 3.5f,
                EnergyCost = 60f, Cooldown = 15f, Range = 12f, CastTime = 0.8f,
                RangeType = "Rectangle", AoeRadius = 12f, CritRate = 0.25f, CritMultiplier = 2.0f,
                CastAnimation = "Ultimate_SwordStorm", CastSound = "/Game/Audio/Skills/Metal_Ultimate",
                HitSound = "/Game/Audio/Skills/Metal_Explosion", VfxPath = "/Game/VFX/Metal_SwordStorm",
                ComboNextId = 0, ComboIndex = 2,
                RequiredLevel = 5, MaxLevel = 10, DamagePerLevel = 0.2f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Stun", Duration = 1.5f, Value = 1f, Chance = 0.3f }
                }
            });

            // ===== 木属性（束缚/治疗/持续恢复）=====
            // 段表 id→(名字, 五行) 与运行时 WuxingSystem/WoodSkills.cs 及服务端 007 seed 逐行一致；
            // 数值取运行时 SkillData（那才是真正施放与服务端结算用的那份），
            // 只有连招/特效/音效/Vfx/暴击这些运行时没有对应字段的扩展项留在这一层。
            RegisterSkill(new SkillConfig
            {
                SkillId = 2001, SkillName = "青木藤缠", Description = "召唤藤蔓束缚敌人，造成持续伤害和定身效果",
                Element = "Wood", Type = "Control", DamageMultiplier = 0.5f,
                EnergyCost = 70f, Cooldown = 10f, Range = 15f, CastTime = 0.3f,
                RangeType = "Single", CritRate = 0f,
                CastAnimation = "Cast_Entangle", CastSound = "/Game/Audio/Skills/Wood_Entangle",
                HitSound = "/Game/Audio/Skills/Wood_Strangle", VfxPath = "/Game/VFX/Wood_Entangle",
                RequiredLevel = 5, MaxLevel = 10, DamagePerLevel = 0.1f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Root", Duration = 2f, Value = 1f, Chance = 0.9f },
                    new SkillEffectConfig { EffectType = "DoT", Duration = 5f, Value = 12f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 2002, SkillName = "春回大地", Description = "范围治疗友军，恢复生命值并持续恢复",
                Element = "Wood", Type = "Support", DamageMultiplier = 0f,
                EnergyCost = 120f, Cooldown = 30f, Range = 0f, CastTime = 1.0f,
                RangeType = "Circle", AoeRadius = 8f, CritRate = 0f,
                CastAnimation = "Cast_AoE_Heal", CastSound = "/Game/Audio/Skills/Wood_AoEHeal",
                HitSound = "", VfxPath = "/Game/VFX/Wood_Rejuvenation",
                RequiredLevel = 10, MaxLevel = 10, DamagePerLevel = 0f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "HoT", Duration = 8f, Value = 15f, Chance = 1.0f },
                    new SkillEffectConfig { EffectType = "Cleanse", Duration = 0f, Value = 1f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 2003, SkillName = "万木森罗", Description = "召唤树木持续攻击范围内的敌人",
                Element = "Wood", Type = "ActiveAttack", DamageMultiplier = 0.8f,
                EnergyCost = 180f, Cooldown = 25f, Range = 20f, CastTime = 1.5f,
                RangeType = "Circle", AoeRadius = 10f, CritRate = 0.05f,
                CastAnimation = "Cast_AoE_Summon", CastSound = "/Game/Audio/Skills/Wood_Grove",
                HitSound = "/Game/Audio/Skills/Wood_BranchHit", VfxPath = "/Game/VFX/Wood_Grove",
                RequiredLevel = 15, MaxLevel = 10, DamagePerLevel = 0.12f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "DoT", Duration = 8f, Value = 18f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 2004, SkillName = "生生不息", Description = "激活生命之力，持续恢复生命和内力，并提升移动速度",
                Element = "Wood", Type = "Support", DamageMultiplier = 0f,
                EnergyCost = 250f, Cooldown = 60f, Range = 0f, CastTime = 0.5f,
                RangeType = "Single", CritRate = 0f, CanMoveWhileCasting = true,
                CastAnimation = "Cast_Buff_Life", CastSound = "/Game/Audio/Skills/Wood_Lifeburst",
                HitSound = "", VfxPath = "/Game/VFX/Wood_Lifeburst",
                RequiredLevel = 20, MaxLevel = 10, DamagePerLevel = 0f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "HoT", Duration = 10f, Value = 30f, Chance = 1.0f },
                    new SkillEffectConfig { EffectType = "Haste", Duration = 10f, Value = 0.2f, Chance = 1.0f }
                }
            });

            // ===== 水属性（柔韧/控制/治疗）=====
            RegisterSkill(new SkillConfig
            {
                SkillId = 3001, SkillName = "寒冰掌", Description = "掌心凝聚寒冰之力，攻击敌人并减速",
                Element = "Water", Type = "ActiveAttack", DamageMultiplier = 1.2f,
                EnergyCost = 60f, Cooldown = 6f, Range = 8f, CastTime = 0.3f,
                RangeType = "Single", CritRate = 0.08f,
                CastAnimation = "Attack_Palm", CastSound = "/Game/Audio/Skills/Water_Palm",
                HitSound = "/Game/Audio/Skills/Water_Freeze", VfxPath = "/Game/VFX/Water_IcePalm",
                RequiredLevel = 5, MaxLevel = 10, DamagePerLevel = 0.1f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Slow", Duration = 3f, Value = 0.4f, Chance = 0.8f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 3002, SkillName = "水愈术", Description = "引导水之灵力，快速恢复生命值",
                Element = "Water", Type = "Support", DamageMultiplier = 0f,
                EnergyCost = 100f, Cooldown = 15f, Range = 15f, CastTime = 0.8f,
                RangeType = "Single", CritRate = 0f,
                CastAnimation = "Cast_Heal", CastSound = "/Game/Audio/Skills/Water_Heal",
                HitSound = "", VfxPath = "/Game/VFX/Water_Heal",
                RequiredLevel = 10, MaxLevel = 10, DamagePerLevel = 0f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "HoT", Duration = 5f, Value = 20f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 3003, SkillName = "冰封千里", Description = "大范围冰冻敌人，解冻后持续减速",
                Element = "Water", Type = "Ultimate", DamageMultiplier = 0.8f,
                EnergyCost = 200f, Cooldown = 30f, Range = 20f, CastTime = 2.0f,
                RangeType = "Circle", AoeRadius = 10f, CritRate = 0.1f,
                CastAnimation = "Ultimate_Freeze", CastSound = "/Game/Audio/Skills/Water_Ultimate",
                HitSound = "/Game/Audio/Skills/Water_Shatter", VfxPath = "/Game/VFX/Water_AbsoluteFreeze",
                RequiredLevel = 15, MaxLevel = 10, DamagePerLevel = 0.18f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Stun", Duration = 3f, Value = 1f, Chance = 0.7f },
                    new SkillEffectConfig { EffectType = "Slow", Duration = 5f, Value = 0.6f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 3004, SkillName = "滔天巨浪", Description = "召唤巨大水浪向前冲击，击退范围内所有敌人",
                Element = "Water", Type = "ActiveAttack", DamageMultiplier = 1.8f,
                EnergyCost = 180f, Cooldown = 25f, Range = 0f, CastTime = 1.2f,
                RangeType = "Line", AoeRadius = 4f, CritRate = 0.1f, KnockbackForce = 10f,
                CastAnimation = "Cast_Wave", CastSound = "/Game/Audio/Skills/Water_Wave",
                HitSound = "/Game/Audio/Skills/Water_Crash", VfxPath = "/Game/VFX/Water_Tsunami",
                RequiredLevel = 20, MaxLevel = 10, DamagePerLevel = 0.15f
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 3005, SkillName = "水月镜花", Description = "创造水之幻影分身，提升闪避率并协同攻击",
                Element = "Water", Type = "Support", DamageMultiplier = 0.5f,
                EnergyCost = 150f, Cooldown = 35f, Range = 0f, CastTime = 1.0f,
                RangeType = "Single", CritRate = 0f, CanMoveWhileCasting = true,
                CastAnimation = "Cast_Buff_Mirror", CastSound = "/Game/Audio/Skills/Water_Mirror",
                HitSound = "", VfxPath = "/Game/VFX/Water_MirrorImage",
                RequiredLevel = 18, MaxLevel = 10, DamagePerLevel = 0.1f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Evasion", Duration = 10f, Value = 0.3f, Chance = 1.0f }
                }
            });

            // ===== 火属性（爆发/持续伤害）=====
            RegisterSkill(new SkillConfig
            {
                SkillId = 4001, SkillName = "烈焰掌", Description = "掌心凝聚烈焰攻击敌人，造成燃烧效果",
                Element = "Fire", Type = "ActiveAttack", DamageMultiplier = 1.3f,
                EnergyCost = 75f, Cooldown = 7f, Range = 8f, CastTime = 0.3f,
                RangeType = "Single", CritRate = 0.1f,
                CastAnimation = "Attack_FlamePalm", CastSound = "/Game/Audio/Skills/Fire_Palm",
                HitSound = "/Game/Audio/Skills/Fire_Immolate", VfxPath = "/Game/VFX/Fire_FlamePalm",
                RequiredLevel = 5, MaxLevel = 10, DamagePerLevel = 0.12f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Burn", Duration = 4f, Value = 12f, Chance = 0.7f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 4002, SkillName = "火球术", Description = "投掷火球攻击目标位置，造成范围爆炸伤害",
                Element = "Fire", Type = "ActiveAttack", DamageMultiplier = 1.5f,
                EnergyCost = 110f, Cooldown = 10f, Range = 25f, CastTime = 0.8f,
                RangeType = "Circle", AoeRadius = 3f, CritRate = 0.12f,
                CastAnimation = "Cast_Projectile_Fire", CastSound = "/Game/Audio/Skills/Fire_Ball",
                HitSound = "/Game/Audio/Skills/Fire_Explosion", VfxPath = "/Game/VFX/Fire_Fireball",
                RequiredLevel = 10, MaxLevel = 10, DamagePerLevel = 0.12f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Burn", Duration = 4f, Value = 10f, Chance = 0.6f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 4003, SkillName = "炎龙出海", Description = "释放炽热火龙直线冲击，穿透敌人造成伤害",
                Element = "Fire", Type = "ActiveAttack", DamageMultiplier = 2.0f,
                EnergyCost = 160f, Cooldown = 18f, Range = 0f, CastTime = 1.0f,
                RangeType = "Line", AoeRadius = 2f, CritRate = 0.1f, KnockbackForce = 6f,
                CastAnimation = "Cast_Line_Fire", CastSound = "/Game/Audio/Skills/Fire_Dragon",
                HitSound = "/Game/Audio/Skills/Fire_BurnLoop", VfxPath = "/Game/VFX/Fire_DragonWave",
                RequiredLevel = 15, MaxLevel = 10, DamagePerLevel = 0.15f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Burn", Duration = 3f, Value = 20f, Chance = 0.9f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 4004, SkillName = "焚天灭地", Description = "召唤天火降临，造成毁灭性的范围爆炸伤害",
                Element = "Fire", Type = "Ultimate", DamageMultiplier = 3.5f,
                EnergyCost = 300f, Cooldown = 40f, Range = 30f, CastTime = 2.5f,
                RangeType = "Circle", AoeRadius = 12f, CritRate = 0.2f, CritMultiplier = 2.0f,
                CastAnimation = "Ultimate_Meteor", CastSound = "/Game/Audio/Skills/Fire_Ultimate",
                HitSound = "/Game/Audio/Skills/Fire_MeteorImpact", VfxPath = "/Game/VFX/Fire_MeteorRain",
                RequiredLevel = 20, MaxLevel = 10, DamagePerLevel = 0.25f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Burn", Duration = 6f, Value = 30f, Chance = 1.0f },
                    new SkillEffectConfig { EffectType = "Stun", Duration = 1f, Value = 1f, Chance = 0.4f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 4005, SkillName = "凤凰涅槃", Description = "死亡后化身火凤凰复活，满状态复活并对周围敌人造成伤害",
                Element = "Fire", Type = "Ultimate", DamageMultiplier = 2.0f,
                EnergyCost = 2000f, Cooldown = 300f, Range = 0f, CastTime = 0f,
                RangeType = "Circle", AoeRadius = 8f, CritRate = 0.2f, CritMultiplier = 2.0f,
                CastAnimation = "Cast_Resurrection", CastSound = "/Game/Audio/Skills/Fire_Phoenix",
                HitSound = "/Game/Audio/Skills/Fire_Rebirth", VfxPath = "/Game/VFX/Fire_Phoenix",
                RequiredLevel = 25, MaxLevel = 10, DamagePerLevel = 0.3f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Revive", Duration = 0f, Value = 1f, Chance = 1.0f }
                }
            });

            // ===== 土属性（防御/控制）=====
            RegisterSkill(new SkillConfig
            {
                SkillId = 5001, SkillName = "岩甲术", Description = "凝聚岩石护甲，大幅提升防御力，但降低移动速度",
                Element = "Earth", Type = "Support", DamageMultiplier = 0f,
                EnergyCost = 80f, Cooldown = 20f, Range = 0f, CastTime = 0.5f,
                RangeType = "Single", CritRate = 0f,
                CastAnimation = "Cast_Shield", CastSound = "/Game/Audio/Skills/Earth_Shield",
                HitSound = "", VfxPath = "/Game/VFX/Earth_RockArmor",
                RequiredLevel = 5, MaxLevel = 10, DamagePerLevel = 0f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Shield", Duration = 12f, Value = 150f, Chance = 1.0f },
                    new SkillEffectConfig { EffectType = "Slow", Duration = 12f, Value = 0.2f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 5002, SkillName = "地刺术", Description = "从地面召唤岩石尖刺连续攻击敌人",
                Element = "Earth", Type = "ActiveAttack", DamageMultiplier = 1.4f,
                EnergyCost = 100f, Cooldown = 12f, Range = 15f, CastTime = 0.5f,
                RangeType = "Circle", AoeRadius = 4f, CritRate = 0.1f,
                CastAnimation = "Cast_GroundSpikes", CastSound = "/Game/Audio/Skills/Earth_Spike",
                HitSound = "/Game/Audio/Skills/Earth_Rumble", VfxPath = "/Game/VFX/Earth_Spikes",
                RequiredLevel = 10, MaxLevel = 10, DamagePerLevel = 0.14f
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 5003, SkillName = "山岳护盾", Description = "生成坚固护盾吸收伤害，护盾破碎时反伤周围敌人",
                Element = "Earth", Type = "Support", DamageMultiplier = 0f,
                EnergyCost = 150f, Cooldown = 30f, Range = 0f, CastTime = 0.8f,
                RangeType = "Circle", AoeRadius = 6f, CritRate = 0f,
                CastAnimation = "Cast_Shield_Greater", CastSound = "/Game/Audio/Skills/Earth_Bulwark",
                HitSound = "", VfxPath = "/Game/VFX/Earth_MountainShield",
                RequiredLevel = 15, MaxLevel = 10, DamagePerLevel = 0f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Shield", Duration = 15f, Value = 300f, Chance = 1.0f },
                    new SkillEffectConfig { EffectType = "Reflect", Duration = 15f, Value = 0.3f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 5004, SkillName = "地动山摇", Description = "猛击地面引发地震，击飞并眩晕范围内所有敌人",
                Element = "Earth", Type = "Control", DamageMultiplier = 2.2f,
                EnergyCost = 250f, Cooldown = 35f, Range = 0f, CastTime = 1.5f,
                RangeType = "Circle", AoeRadius = 8f, CritRate = 0.1f, KnockbackForce = 12f,
                CastAnimation = "Attack_GroundSlam", CastSound = "/Game/Audio/Skills/Earth_Slam",
                HitSound = "/Game/Audio/Skills/Earth_Rumble", VfxPath = "/Game/VFX/Earth_GroundSlam",
                RequiredLevel = 20, MaxLevel = 10, DamagePerLevel = 0.16f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Stun", Duration = 2f, Value = 1f, Chance = 0.8f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 5005, SkillName = "泰山压顶", Description = "召唤巨大岩石从天而降，压制目标区域的敌人",
                Element = "Earth", Type = "ActiveAttack", DamageMultiplier = 2.5f,
                EnergyCost = 200f, Cooldown = 28f, Range = 20f, CastTime = 1.0f,
                RangeType = "Circle", AoeRadius = 6f, CritRate = 0.12f,
                CastAnimation = "Cast_Rockfall", CastSound = "/Game/Audio/Skills/Earth_Weight",
                HitSound = "/Game/Audio/Skills/Earth_Crush", VfxPath = "/Game/VFX/Earth_Rockfall",
                RequiredLevel = 18, MaxLevel = 10, DamagePerLevel = 0.18f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Stun", Duration = 1.5f, Value = 1f, Chance = 0.5f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 5006, SkillName = "厚土载物", Description = "与大地融为一体，免疫控制，大幅提升防御并持续恢复生命",
                Element = "Earth", Type = "Ultimate", DamageMultiplier = 0f,
                EnergyCost = 500f, Cooldown = 120f, Range = 0f, CastTime = 1.5f,
                RangeType = "Single", CritRate = 0f,
                CastAnimation = "Cast_Buff_Gaia", CastSound = "/Game/Audio/Skills/Earth_Gaia",
                HitSound = "", VfxPath = "/Game/VFX/Earth_Gaia",
                RequiredLevel = 25, MaxLevel = 10, DamagePerLevel = 0f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Immune", Duration = 12f, Value = 1f, Chance = 1.0f },
                    new SkillEffectConfig { EffectType = "HoT", Duration = 12f, Value = 40f, Chance = 1.0f }
                }
            });

            // ===== 位移技能 =====
            RegisterSkill(new SkillConfig
            {
                SkillId = 6001, SkillName = "凌波微步", Description = "以精妙身法瞬间位移到目标位置，短暂无敌。",
                Element = "Water", Type = "Dash", DamageMultiplier = 0f,
                EnergyCost = 20f, Cooldown = 6f, Range = 10f, CastTime = 0f,
                RangeType = "Single", CritRate = 0f, CanMoveWhileCasting = true,
                CastAnimation = "Dash_Blink", CastSound = "/Game/Audio/Skills/Dash_Blink",
                HitSound = "", VfxPath = "/Game/VFX/Dash_Afterimage",
                RequiredLevel = 4, MaxLevel = 5, DamagePerLevel = 0f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Invincible", Duration = 0.5f, Value = 1f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 6002, SkillName = "风驰电掣", Description = "化为疾风向前突进，路径上敌人受到切割伤害。",
                Element = "Wood", Type = "Dash", DamageMultiplier = 1.0f,
                EnergyCost = 25f, Cooldown = 5f, Range = 12f, CastTime = 0f,
                RangeType = "Line", AoeRadius = 1.5f, CritRate = 0.15f, CanMoveWhileCasting = true,
                CastAnimation = "Dash_Charge", CastSound = "/Game/Audio/Skills/Dash_Wind",
                HitSound = "/Game/Audio/Skills/Wind_Cut", VfxPath = "/Game/VFX/Dash_WindSlash",
                RequiredLevel = 7, MaxLevel = 5, DamagePerLevel = 0.1f
            });
        }
    }
}
