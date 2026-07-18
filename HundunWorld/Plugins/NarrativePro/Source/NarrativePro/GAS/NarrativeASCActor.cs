using FlaxEngine;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 带 ASC 的 Actor。对应 UE5 ANarrativeASCActor。
    /// 自带 AbilitySystemComponent 和一些属性，用于非角色（如可破坏物）需要 GAS 的场景。
    /// 简化点：
    /// - 移除 IAbilitySystemInterface（用 GetAbilitySystemComponent 方法替代）
    /// - 移除网络复制
    /// </summary>
    public class NarrativeASCActor : Actor
    {
        /// <summary>默认属性初始化效果路径。</summary>
        public string DefaultAttributesEffectPath = "";

        /// <summary>ASC 等级。</summary>
        public int Level = 1;

        /// <summary>ASC 实例（运行时获取）。</summary>
        private NarrativeAbilitySystemComponent _asc;

        /// <summary>获取 ASC。</summary>
        public virtual NarrativeAbilitySystemComponent GetAbilitySystemComponent()
        {
            if (_asc == null)
            {
                _asc = GetScript<NarrativeAbilitySystemComponent>();
                if (_asc == null)
                {
                    _asc = AddScript<NarrativeAbilitySystemComponent>();
                    if (_asc != null)
                    {
                        _asc.DefaultAttributesEffectPath = DefaultAttributesEffectPath;
                        _asc.Level = Level;
                    }
                }
            }
            return _asc;
        }

        /// <summary>初始化属性。</summary>
        public virtual void InitializeAttributes()
        {
            var asc = GetAbilitySystemComponent();
            asc?.InitializeAttributes();
        }

        /// <summary>处理死亡（BlueprintNativeEvent 等价，可重写）。</summary>
        public virtual void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC)
        {
            NarrativePro.Core.NarrativeLog.Log($"[NarrativeASCActor] {Name} handling death of {killedActor?.Name}");
        }

        // Flax Actor 没有 OnEnable/OnDisable 重写（用 OnBeginPlay）
    }
}
