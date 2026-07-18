using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// Narrative UI 开发者设置。对应 UE5 UNarrativeUIDeveloperSettings（继承 UDeveloperSettings）。
    /// 用于全局 UI 样式（主色、反色等）配置。
    ///
    /// 移植简化点：
    /// 1. UE5 中为 UCLASS(config=Engine, defaultconfig) 的 UDeveloperSettings 派生类，
    ///    Flax 中以 [Serializable] plain class + 静态 Instance 单例实现（参考 NarrativeCombatDeveloperSettings）。
    /// 2. UE5 中 FLinearColor → Flax Color。
    /// 3. 默认颜色值与 UE5 构造函数一致。
    /// </summary>
    [Serializable]
    public class NarrativeUIDeveloperSettings
    {
        /// <summary>
        /// UI 元素的主色调。对应 UE5 UIPrimaryColor。
        /// 默认值 FLinearColor(0.000607, 0.672443, 0.168269, 1.000000)。
        /// </summary>
        public Color UIPrimaryColor = new Color(0.000607f, 0.672443f, 0.168269f, 1.000000f);

        /// <summary>
        /// UI 元素的反色调。对应 UE5 UIInvertColor。
        /// 默认值 FLinearColor(0.000000, 0.009721, 0.059511, 1.000000)。
        /// </summary>
        public Color UIInvertColor = new Color(0.000000f, 0.009721f, 0.059511f, 1.000000f);

        /// <summary>
        /// UI 元素的反色强调色。对应 UE5 UIInvertAccentColor。
        /// 默认值 FLinearColor(0.000000, 0.058326, 0.178533, 1.000000)。
        /// </summary>
        public Color UIInvertAccentColor = new Color(0.000000f, 0.058326f, 0.178533f, 1.000000f);

        /// <summary>单例实例。</summary>
        public static NarrativeUIDeveloperSettings Instance { get; set; } = LoadDefault();

        /// <summary>构造函数。对应 UE5 UNarrativeUIDeveloperSettings 构造函数的默认颜色值。</summary>
        public NarrativeUIDeveloperSettings()
        {
            // 默认颜色值已在字段初始化器中设置，与 UE5 构造函数一致。
        }

        private static NarrativeUIDeveloperSettings LoadDefault()
        {
            // Flax-不兼容: UE5 的 UDeveloperSettings 在 Flax 无对应物，保留占位。原文 TODO: 从 Flax 引擎配置或 JSON 加载。暂时返回默认实例。
            var settings = new NarrativeUIDeveloperSettings();
            NarrativeLog.Log("NarrativeUIDeveloperSettings 已使用默认值初始化。");
            return settings;
        }
    }
}
