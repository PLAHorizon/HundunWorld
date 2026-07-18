using System;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 属性数据。对应 UE5 FGameplayAttributeData。
    /// 由 BaseValue（基础值）和 CurrentValue（当前值）组成。
    /// CurrentValue = BaseValue + 所有修饰器累加值（在 ASC 中计算）。
    /// 简化点：移除 UE5 复制（ReplicatedUsing），改为本地属性 + 事件回调。
    /// </summary>
    [Serializable]
    public class AttributeData
    {
        /// <summary>基础值（修饰器在 BaseValue 上叠加得到 CurrentValue）。</summary>
        public float BaseValue = 0f;

        /// <summary>当前值（运行时由 ASC 计算并刷新）。</summary>
        public float CurrentValue = 0f;

        public AttributeData() { }

        public AttributeData(float initialValue)
        {
            BaseValue = initialValue;
            CurrentValue = initialValue;
        }

        /// <summary>设置基础值并同步当前值（无修饰器时使用）。</summary>
        public void SetBaseValue(float newValue, bool bRecalculate = true)
        {
            BaseValue = newValue;
            if (bRecalculate)
            {
                CurrentValue = newValue;
            }
        }

        /// <summary>直接设置当前值（修饰器外部修改）。</summary>
        public void SetCurrentValue(float newValue)
        {
            CurrentValue = newValue;
        }

        /// <summary>隐式 float 转换，简化使用。</summary>
        public static implicit operator float(AttributeData data) => data?.CurrentValue ?? 0f;
    }

    /// <summary>
    /// 属性修饰器类型。对应 UE5 EGameplayModOp。
    /// </summary>
    public enum EGameplayModOp : byte
    {
        /// <summary>加法：Current = Base + Magnitude</summary>
        Add = 0,
        /// <summary>乘法：Current = Base * Magnitude</summary>
        Multiply = 1,
        /// <summary>除法：Current = Base / Magnitude</summary>
        Divide = 2,
        /// <summary>覆盖：Current = Magnitude</summary>
        Override = 3
    }

    /// <summary>
    /// 属性修饰器。对应 UE5 FGameplayModifierInfo。
    /// 描述一个效果如何修改属性。
    /// </summary>
    [Serializable]
    public class GameplayModifierInfo
    {
        /// <summary>属性名（如 "Health"、"MaxHealth"）。</summary>
        public string AttributeName = "";

        /// <summary>修饰操作类型。</summary>
        public EGameplayModOp ModifierOp = EGameplayModOp.Add;

        /// <summary>修饰量级（加法/乘法因子/覆盖值）。</summary>
        public float Magnitude = 0f;
    }
}
