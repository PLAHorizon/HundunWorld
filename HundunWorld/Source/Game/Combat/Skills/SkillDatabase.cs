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

                // 1. 尝试从 JSON 文件加载
                bool loaded = TryLoadFromJson();

                // 2. 如果 JSON 加载失败或为空，使用内置默认技能
                if (!loaded || _skills.Count == 0)
                {
                    LoadBuiltInSkills();
                    Debug.Log($"[SkillDatabase] 使用内置技能数据 ({_skills.Count} 个技能)");
                }
                else
                {
                    Debug.Log($"[SkillDatabase] 从 JSON 加载了 {_skills.Count} 个技能");
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
                var skills = JsonSerializer.Deserialize<List<SkillConfig>>(json, _jsonOptions);
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
                ComboNextId = 1003, ComboWindow = 0.8f, ComboIndex = 1,
                RequiredLevel = 1, MaxLevel = 10, DamagePerLevel = 0.12f
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 1003, SkillName = "万剑归宗", Description = "连招终结技，内力爆发化为无数剑气横扫前方。",
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

            // ===== 水属性（柔韧/控制）=====
            RegisterSkill(new SkillConfig
            {
                SkillId = 2001, SkillName = "冰霜箭", Description = "凝聚水属性内力射出冰箭，减速目标。",
                Element = "Water", Type = "ActiveAttack", DamageMultiplier = 1.2f,
                EnergyCost = 25f, Cooldown = 2.5f, Range = 20f, CastTime = 0.6f,
                RangeType = "Single", CritRate = 0.08f,
                CastAnimation = "Cast_Projectile", CastSound = "/Game/Audio/Skills/Water_Arrow",
                HitSound = "/Game/Audio/Skills/Water_Freeze", VfxPath = "/Game/VFX/Water_IceArrow",
                ComboNextId = 0, ComboIndex = 0,
                RequiredLevel = 1, MaxLevel = 10, DamagePerLevel = 0.1f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Slow", Duration = 3f, Value = 0.4f, Chance = 0.8f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 2002, SkillName = "寒冰领域", Description = "在目标区域召唤寒冰领域，持续冻伤范围内敌人。",
                Element = "Water", Type = "Control", DamageMultiplier = 0.6f,
                EnergyCost = 45f, Cooldown = 12f, Range = 15f, CastTime = 1.0f,
                RangeType = "Circle", AoeRadius = 6f, CritRate = 0.05f,
                CastAnimation = "Cast_AoE_Ground", CastSound = "/Game/Audio/Skills/Water_Domain",
                HitSound = "/Game/Audio/Skills/Water_FreezeLoop", VfxPath = "/Game/VFX/Water_IceDomain",
                RequiredLevel = 8, MaxLevel = 10, DamagePerLevel = 0.08f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Slow", Duration = 5f, Value = 0.6f, Chance = 1.0f },
                    new SkillEffectConfig { EffectType = "DoT", Duration = 5f, Value = 15f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 2003, SkillName = "冰封万里", Description = "水属性终结技，将大范围敌人冻结为冰雕。",
                Element = "Water", Type = "Ultimate", DamageMultiplier = 2.5f,
                EnergyCost = 70f, Cooldown = 25f, Range = 18f, CastTime = 1.5f,
                RangeType = "Circle", AoeRadius = 10f, CritRate = 0.1f,
                CastAnimation = "Ultimate_Freeze", CastSound = "/Game/Audio/Skills/Water_Ultimate",
                HitSound = "/Game/Audio/Skills/Water_Shatter", VfxPath = "/Game/VFX/Water_AbsoluteFreeze",
                RequiredLevel = 15, MaxLevel = 10, DamagePerLevel = 0.18f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Stun", Duration = 3f, Value = 1f, Chance = 0.7f }
                }
            });

            // ===== 火属性（爆发/持续伤害）=====
            RegisterSkill(new SkillConfig
            {
                SkillId = 3001, SkillName = "火球术", Description = "凝聚火属性内力形成火球投掷，命中后爆炸。",
                Element = "Fire", Type = "ActiveAttack", DamageMultiplier = 1.5f,
                EnergyCost = 30f, Cooldown = 3f, Range = 15f, CastTime = 0.8f,
                RangeType = "Circle", AoeRadius = 3f, CritRate = 0.12f,
                CastAnimation = "Cast_Projectile_Fire", CastSound = "/Game/Audio/Skills/Fire_Ball",
                HitSound = "/Game/Audio/Skills/Fire_Explosion", VfxPath = "/Game/VFX/Fire_Fireball",
                ComboNextId = 0, ComboIndex = 0,
                RequiredLevel = 1, MaxLevel = 10, DamagePerLevel = 0.12f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Burn", Duration = 4f, Value = 10f, Chance = 0.6f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 3002, SkillName = "烈焰旋风", Description = "召唤火焰旋风向前推进，灼烧路径上所有敌人。",
                Element = "Fire", Type = "ActiveAttack", DamageMultiplier = 2.0f,
                EnergyCost = 40f, Cooldown = 8f, Range = 12f, CastTime = 0.5f,
                RangeType = "Line", AoeRadius = 2f, CritRate = 0.1f,
                CastAnimation = "Cast_Line_Fire", CastSound = "/Game/Audio/Skills/Fire_Whirlwind",
                HitSound = "/Game/Audio/Skills/Fire_BurnLoop", VfxPath = "/Game/VFX/Fire_Whirlwind",
                RequiredLevel = 6, MaxLevel = 10, DamagePerLevel = 0.15f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Burn", Duration = 3f, Value = 20f, Chance = 0.9f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 3003, SkillName = "焚天灭地", Description = "火属性终结技，召唤陨石雨覆盖大范围区域。",
                Element = "Fire", Type = "Ultimate", DamageMultiplier = 4.0f,
                EnergyCost = 80f, Cooldown = 30f, Range = 20f, CastTime = 2.0f,
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

            // ===== 木属性（治疗/辅助）=====
            RegisterSkill(new SkillConfig
            {
                SkillId = 4001, SkillName = "春风化雨", Description = "以木属性内力治愈自身或队友，持续恢复生命。",
                Element = "Wood", Type = "Support", DamageMultiplier = 0.8f,
                EnergyCost = 35f, Cooldown = 6f, Range = 10f, CastTime = 1.0f,
                RangeType = "Single", CritRate = 0f,
                CastAnimation = "Cast_Heal", CastSound = "/Game/Audio/Skills/Wood_Heal",
                HitSound = "", VfxPath = "/Game/VFX/Wood_Heal",
                RequiredLevel = 3, MaxLevel = 10, DamagePerLevel = 0.1f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "HoT", Duration = 5f, Value = 20f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 4002, SkillName = "万物复苏", Description = "木属性大范围治疗，恢复范围内所有友方生命。",
                Element = "Wood", Type = "Support", DamageMultiplier = 1.2f,
                EnergyCost = 55f, Cooldown = 15f, Range = 12f, CastTime = 1.5f,
                RangeType = "Circle", AoeRadius = 8f, CritRate = 0f,
                CastAnimation = "Cast_AoE_Heal", CastSound = "/Game/Audio/Skills/Wood_AoEHeal",
                HitSound = "", VfxPath = "/Game/VFX/Wood_Rejuvenation",
                RequiredLevel = 12, MaxLevel = 10, DamagePerLevel = 0.12f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "HoT", Duration = 8f, Value = 15f, Chance = 1.0f },
                    new SkillEffectConfig { EffectType = "Cleanse", Duration = 0f, Value = 1f, Chance = 1.0f }
                }
            });

            // ===== 土属性（防御/控制）=====
            RegisterSkill(new SkillConfig
            {
                SkillId = 5001, SkillName = "磐石护体", Description = "以土属性内力凝聚护盾，吸收 incoming 伤害。",
                Element = "Earth", Type = "Support", DamageMultiplier = 0f,
                EnergyCost = 30f, Cooldown = 10f, Range = 0f, CastTime = 0.5f,
                RangeType = "Single", CritRate = 0f,
                CastAnimation = "Cast_Shield", CastSound = "/Game/Audio/Skills/Earth_Shield",
                HitSound = "", VfxPath = "/Game/VFX/Earth_Shield",
                RequiredLevel = 2, MaxLevel = 10, DamagePerLevel = 0f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Shield", Duration = 8f, Value = 100f, Chance = 1.0f }
                }
            });
            RegisterSkill(new SkillConfig
            {
                SkillId = 5002, SkillName = "地裂山崩", Description = "以重击震裂大地，击飞并眩晕前方敌人。",
                Element = "Earth", Type = "Control", DamageMultiplier = 1.8f,
                EnergyCost = 40f, Cooldown = 8f, Range = 8f, CastTime = 0.7f,
                RangeType = "Sector", AoeAngle = 90f, CritRate = 0.1f, KnockbackForce = 12f,
                CastAnimation = "Attack_GroundSlam", CastSound = "/Game/Audio/Skills/Earth_Slam",
                HitSound = "/Game/Audio/Skills/Earth_Rumble", VfxPath = "/Game/VFX/Earth_GroundSlam",
                RequiredLevel = 5, MaxLevel = 10, DamagePerLevel = 0.14f,
                Effects = new List<SkillEffectConfig>
                {
                    new SkillEffectConfig { EffectType = "Stun", Duration = 2f, Value = 1f, Chance = 0.6f }
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
