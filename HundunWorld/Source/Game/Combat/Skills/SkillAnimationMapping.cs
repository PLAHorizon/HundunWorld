using System;
using System.Collections.Generic;
using FlaxEngine;
using Game.Character.Attributes;

namespace Game.Combat.Skills
{
    /// <summary>
    /// 技能动画映射配置
    /// 将技能ID映射到具体的动画资源和动画参数
    /// </summary>
    public class SkillAnimationMapping : Script
    {
        /// <summary>
        /// 技能动画配置数据
        /// </summary>
        [Serializable]
        public class SkillAnimConfig
        {
            [Tooltip("技能ID")]
            public int SkillId;
            
            [Tooltip("技能名称")]
            public string SkillName;
            
            [Tooltip("动画名称（对应动画图中的状态）")]
            public string AnimationName;
            
            [Tooltip("前摇时间（秒）")]
            public float StartupTime = 0.3f;
            
            [Tooltip("激活时间（秒）")]
            public float ActiveTime = 0.2f;
            
            [Tooltip("后摇时间（秒）")]
            public float RecoveryTime = 0.3f;
            
            [Tooltip("是否循环播放")]
            public bool Loop = false;
            
            [Tooltip("播放速度")]
            public float PlaybackSpeed = 1.0f;
            
            [Tooltip("五行属性")]
            public WuxingElement Element = WuxingElement.None;
            
            [Tooltip("技能类型标签（用于动画分类）")]
            public SkillAnimationType AnimationType = SkillAnimationType.MeleeAttack;
        }

        /// <summary>
        /// 技能动画类型
        /// </summary>
        public enum SkillAnimationType
        {
            /// <summary>近战攻击</summary>
            MeleeAttack,
            /// <summary>远程攻击</summary>
            RangedAttack,
            /// <summary>法术施放</summary>
            SpellCast,
            /// <summary>范围攻击</summary>
            AreaAttack,
            /// <summary>辅助技能</summary>
            Support,
            /// <summary>防御技能</summary>
            Defense,
            /// <summary>移动技能</summary>
            Movement,
            /// <summary>变身技能</summary>
            Transform
        }

        [Header("动画配置库")]
        [Tooltip("技能动画配置列表")]
        public List<SkillAnimConfig> SkillAnimations = new List<SkillAnimConfig>();

        // 单例
        private static SkillAnimationMapping _instance;
        public static SkillAnimationMapping Instance => _instance;

        // 快速查找字典
        private Dictionary<int, SkillAnimConfig> _skillAnimDict;
        private Dictionary<WuxingElement, List<SkillAnimConfig>> _elementAnimDict;

        public override void OnEnable()
        {
            if (_instance == null)
            {
                _instance = this;
                InitializeAnimationDatabase();
            }
            else if (_instance != this)
            {
                Destroy(this);
            }
        }

        /// <summary>
        /// 初始化动画数据库
        /// </summary>
        private void InitializeAnimationDatabase()
        {
            _skillAnimDict = new Dictionary<int, SkillAnimConfig>();
            _elementAnimDict = new Dictionary<WuxingElement, List<SkillAnimConfig>>();

            // 初始化五行分类字典
            foreach (WuxingElement element in Enum.GetValues(typeof(WuxingElement)))
            {
                _elementAnimDict[element] = new List<SkillAnimConfig>();
            }

            // 构建快速查找索引
            foreach (var config in SkillAnimations)
            {
                _skillAnimDict[config.SkillId] = config;
                _elementAnimDict[config.Element].Add(config);
            }

            Debug.Log($"[SkillAnimationMapping] 初始化完成，加载 {SkillAnimations.Count} 个技能动画配置");
        }

        /// <summary>
        /// 根据技能ID获取动画配置
        /// </summary>
        public SkillAnimConfig GetAnimationConfig(int skillId)
        {
            if (_skillAnimDict.TryGetValue(skillId, out var config))
                return config;
            
            Debug.LogWarning($"[SkillAnimationMapping] 未找到技能ID {skillId} 的动画配置");
            return null;
        }

        /// <summary>
        /// 根据技能名称获取动画配置
        /// </summary>
        public SkillAnimConfig GetAnimationConfigByName(string skillName)
        {
            return SkillAnimations.Find(c => c.SkillName == skillName);
        }

        /// <summary>
        /// 获取指定五行属性的所有技能动画
        /// </summary>
        public List<SkillAnimConfig> GetAnimationsByElement(WuxingElement element)
        {
            if (_elementAnimDict.TryGetValue(element, out var configs))
                return new List<SkillAnimConfig>(configs);
            
            return new List<SkillAnimConfig>();
        }

        /// <summary>
        /// 获取指定类型的所有技能动画
        /// </summary>
        public List<SkillAnimConfig> GetAnimationsByType(SkillAnimationType type)
        {
            return SkillAnimations.FindAll(c => c.AnimationType == type);
        }

        /// <summary>
        /// 添加或更新技能动画配置
        /// </summary>
        public void RegisterSkillAnimation(SkillAnimConfig config)
        {
            if (_skillAnimDict.ContainsKey(config.SkillId))
            {
                // 更新现有配置
                int index = SkillAnimations.FindIndex(c => c.SkillId == config.SkillId);
                if (index >= 0)
                {
                    SkillAnimations[index] = config;
                }
                _skillAnimDict[config.SkillId] = config;
            }
            else
            {
                // 添加新配置
                SkillAnimations.Add(config);
                _skillAnimDict[config.SkillId] = config;
                _elementAnimDict[config.Element].Add(config);
            }
        }

        /// <summary>
        /// 预加载默认技能动画配置
        /// </summary>
        public void LoadDefaultSkillAnimations()
        {
            SkillAnimations.Clear();

            // ==================== 金系技能 ====================
            // 金刚掌 - 单掌攻击
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 1001,
                SkillName = "金刚掌",
                AnimationName = "Standing 1H Magic Attack 01",
                StartupTime = 0.25f,
                ActiveTime = 0.15f,
                RecoveryTime = 0.2f,
                Element = WuxingElement.Metal,
                AnimationType = SkillAnimationType.MeleeAttack,
                PlaybackSpeed = 1.2f
            });

            // 金蛇剑法 - 三连斩
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 1002,
                SkillName = "金蛇剑法",
                AnimationName = "Standing 2H Magic Attack 01",
                StartupTime = 0.2f,
                ActiveTime = 0.1f,
                RecoveryTime = 0.15f,
                Element = WuxingElement.Metal,
                AnimationType = SkillAnimationType.MeleeAttack,
                PlaybackSpeed = 1.5f
            });

            // 金钟罩 - 防御姿态
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 1003,
                SkillName = "金钟罩",
                AnimationName = "Standing Block Idle",
                StartupTime = 0.3f,
                ActiveTime = 0.2f,
                RecoveryTime = 0.2f,
                Element = WuxingElement.Metal,
                AnimationType = SkillAnimationType.Defense,
                Loop = false
            });

            // ==================== 木系技能 ====================
            // 青木藤缠 - 控制施法
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 2001,
                SkillName = "青木藤缠",
                AnimationName = "Standing 2H Cast Spell 01",
                StartupTime = 0.3f,
                ActiveTime = 0.2f,
                RecoveryTime = 0.25f,
                Element = WuxingElement.Wood,
                AnimationType = SkillAnimationType.SpellCast
            });

            // 春回大地 - 治疗施法
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 2002,
                SkillName = "春回大地",
                AnimationName = "Standing 2H Cast Spell 01",
                StartupTime = 0.5f,
                ActiveTime = 0.3f,
                RecoveryTime = 0.3f,
                Element = WuxingElement.Wood,
                AnimationType = SkillAnimationType.Support,
                PlaybackSpeed = 0.9f
            });

            // ==================== 水系技能 ====================
            // 寒冰掌 - 冰系攻击
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 3001,
                SkillName = "寒冰掌",
                AnimationName = "Standing 1H Magic Attack 02",
                StartupTime = 0.3f,
                ActiveTime = 0.2f,
                RecoveryTime = 0.25f,
                Element = WuxingElement.Water,
                AnimationType = SkillAnimationType.MeleeAttack
            });

            // 水愈术 - 治疗施法
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 3002,
                SkillName = "水愈术",
                AnimationName = "standing 1H cast spell 01",
                StartupTime = 0.4f,
                ActiveTime = 0.3f,
                RecoveryTime = 0.3f,
                Element = WuxingElement.Water,
                AnimationType = SkillAnimationType.Support
            });

            // ==================== 火系技能 ====================
            // 烈焰掌 - 火系掌法
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 4001,
                SkillName = "烈焰掌",
                AnimationName = "Standing 1H Magic Attack 03",
                StartupTime = 0.3f,
                ActiveTime = 0.15f,
                RecoveryTime = 0.2f,
                Element = WuxingElement.Fire,
                AnimationType = SkillAnimationType.MeleeAttack,
                PlaybackSpeed = 1.1f
            });

            // 火球术 - 远程投掷
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 4002,
                SkillName = "火球术",
                AnimationName = "Standing 2H Magic Attack 02",
                StartupTime = 0.5f,
                ActiveTime = 0.2f,
                RecoveryTime = 0.3f,
                Element = WuxingElement.Fire,
                AnimationType = SkillAnimationType.RangedAttack
            });

            // 烈焰风暴 - 范围攻击
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 4003,
                SkillName = "烈焰风暴",
                AnimationName = "Standing 2H Magic Area Attack 01",
                StartupTime = 0.8f,
                ActiveTime = 0.5f,
                RecoveryTime = 0.4f,
                Element = WuxingElement.Fire,
                AnimationType = SkillAnimationType.AreaAttack,
                PlaybackSpeed = 0.95f
            });

            // ==================== 土系技能 ====================
            // 岩甲术 - 防御姿态
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 5001,
                SkillName = "岩甲术",
                AnimationName = "Standing Block Start",
                StartupTime = 0.3f,
                ActiveTime = 0.2f,
                RecoveryTime = 0.25f,
                Element = WuxingElement.Earth,
                AnimationType = SkillAnimationType.Defense
            });

            // 地裂术 - 地面攻击
            SkillAnimations.Add(new SkillAnimConfig
            {
                SkillId = 5002,
                SkillName = "地裂术",
                AnimationName = "Standing 2H Magic Area Attack 02",
                StartupTime = 0.6f,
                ActiveTime = 0.3f,
                RecoveryTime = 0.35f,
                Element = WuxingElement.Earth,
                AnimationType = SkillAnimationType.AreaAttack
            });

            InitializeAnimationDatabase();
            Debug.Log($"[SkillAnimationMapping] 预加载 {SkillAnimations.Count} 个默认技能动画配置");
        }

        public override void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            
            base.OnDestroy();
        }
    }
}
