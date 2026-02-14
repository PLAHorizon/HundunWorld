using FlaxEngine;
using Game.Combat.Skills;

namespace Game.Combat
{
    /// <summary>
    /// 技能动画系统初始化器
    /// 在游戏启动时自动初始化技能动画映射配置
    /// </summary>
    public class SkillAnimationSystemInitializer : Script
    {
        [Header("初始化设置")]
        [Tooltip("是否在Start时自动初始化")]
        public bool AutoInitialize = true;

        [Tooltip("是否加载默认技能配置")]
        public bool LoadDefaultSkills = true;

        [Tooltip("是否显示初始化日志")]
        public bool ShowInitLog = true;

        [Header("引用")]
        [Tooltip("SkillAnimationMapping实例（可选，留空自动查找或创建）")]
        public SkillAnimationMapping MappingInstance;

        private bool _isInitialized = false;

        /// <summary>
        /// 启动时初始化
        /// </summary>
        public override void OnStart()
        {
            if (AutoInitialize)
            {
                InitializeAnimationSystem();
            }
        }

        /// <summary>
        /// 初始化动画系统
        /// </summary>
        public void InitializeAnimationSystem()
        {
            if (_isInitialized)
            {
                Debug.LogWarning("[SkillAnimationSystem] 已经初始化过，跳过");
                return;
            }

            if (ShowInitLog)
                Debug.Log("[SkillAnimationSystem] 开始初始化技能动画系统...");

            // 1. 获取或创建SkillAnimationMapping实例
            if (MappingInstance == null)
            {
                MappingInstance = SkillAnimationMapping.Instance;
                
                if (MappingInstance == null)
                {
                    // 在当前Actor上添加SkillAnimationMapping
                    var existingScript = Actor.GetScript<SkillAnimationMapping>();
                    if (existingScript == null)
                    {
                        MappingInstance = new SkillAnimationMapping();
                        Actor.AddScript(MappingInstance.GetType());
                    }
                    else
                    {
                        MappingInstance = existingScript;
                    }
                    
                    if (ShowInitLog)
                        Debug.Log("[SkillAnimationSystem] 创建了新的SkillAnimationMapping实例");
                }
                else
                {
                    if (ShowInitLog)
                        Debug.Log("[SkillAnimationSystem] 找到现有的SkillAnimationMapping实例");
                }
            }

            // 2. 加载默认技能配置
            if (LoadDefaultSkills && MappingInstance != null)
            {
                MappingInstance.LoadDefaultSkillAnimations();
                
                if (ShowInitLog)
                    Debug.Log("[SkillAnimationSystem] 已加载默认技能配置");
            }

            // 3. 验证初始化结果
            if (MappingInstance != null && MappingInstance.SkillAnimations.Count > 0)
            {
                _isInitialized = true;
                
                if (ShowInitLog)
                {
                    Debug.Log($"[SkillAnimationSystem] ✅ 初始化完成！");
                    Debug.Log($"[SkillAnimationSystem] 加载了 {MappingInstance.SkillAnimations.Count} 个技能动画配置");
                    
                    // 输出技能列表
                    foreach (var config in MappingInstance.SkillAnimations)
                    {
                        Debug.Log($"  - [{config.SkillId}] {config.SkillName} ({config.Element}) → {config.AnimationName}");
                    }
                }
            }
            else
            {
                Debug.LogError("[SkillAnimationSystem] ❌ 初始化失败！未能加载技能配置");
            }
        }

        /// <summary>
        /// 手动重新初始化（用于测试）
        /// </summary>
        public void Reinitialize()
        {
            _isInitialized = false;
            InitializeAnimationSystem();
        }

        /// <summary>
        /// 获取动画映射实例
        /// </summary>
        public static SkillAnimationMapping GetMappingInstance()
        {
            var instance = SkillAnimationMapping.Instance;
            if (instance == null)
            {
                Debug.LogWarning("[SkillAnimationSystem] SkillAnimationMapping未初始化，尝试查找...");
                // 尝试在场景中查找所有Actor的脚本
                var scene = Level.FindActor<Scene>();
                if (scene != null)
                {
                    var actors = scene.GetChildren<Actor>();
                    foreach (var actor in actors)
                    {
                        var script = actor.GetScript<SkillAnimationMapping>();
                        if (script != null)
                        {
                            instance = script;
                            Debug.Log("[SkillAnimationSystem] 找到SkillAnimationMapping实例");
                            break;
                        }
                    }
                }
            }
            return instance;
        }
    }
}
