using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Settings
{
    /// <summary>
    /// Narrative 输入设置。对应 UE5 UNarrativeInputSettings。
    /// UE5 中继承 UEnhancedInputUserSettings（config=GameUserSettings），用于扩展瞄准灵敏度等设置。
    /// Flax 中以 [Serializable] 类 + 静态 Instance 单例实现。
    /// </summary>
    [Serializable]
    public class NarrativeInputSettings
    {
        /// <summary>瞄准灵敏度。对应 UE5 AimSensitivity（config, SaveGame）。</summary>
        public float AimSensitivity = 1.0f;

        /// <summary>是否反转垂直轴。对应 UE5 bInvertVertical（config, SaveGame）。</summary>
        public bool bInvertVertical = false;

        /// <summary>是否反转水平轴。对应 UE5 bInvertHorizontal（config, SaveGame）。</summary>
        public bool bInvertHorizontal = false;

        /// <summary>单例实例。</summary>
        public static NarrativeInputSettings Instance { get; set; } = LoadDefault();

        private static NarrativeInputSettings LoadDefault()
        {
            // TODO [需接入设置加载系统]: 从 Flax 用户配置或 JSON 文件加载持久化设置。暂时返回默认实例。
            var settings = new NarrativeInputSettings();
            NarrativeLog.Log("NarrativeInputSettings 已使用默认值初始化。");
            return settings;
        }

        /// <summary>
        /// 设置瞄准灵敏度。对应 UE5 SetAimSensitivity。
        /// </summary>
        public void SetAimSensitivity(float NewAimSensitivity)
        {
            AimSensitivity = NewAimSensitivity;
            NarrativeLog.Log($"瞄准灵敏度已设置为 {NewAimSensitivity}。");
        }

        /// <summary>
        /// 获取瞄准灵敏度。对应 UE5 GetAimSensitivity。
        /// </summary>
        public float GetAimSensitivity()
        {
            return AimSensitivity;
        }

        /// <summary>
        /// 设置是否反转垂直轴。对应 UE5 SetInvertVertical。
        /// </summary>
        public void SetInvertVertical(bool NewInvertVertical)
        {
            bInvertVertical = NewInvertVertical;
            NarrativeLog.Log($"垂直轴反转已设置为 {NewInvertVertical}。");
        }

        /// <summary>
        /// 获取是否反转垂直轴。对应 UE5 GetInvertVertical。
        /// </summary>
        public bool GetInvertVertical()
        {
            return bInvertVertical;
        }

        /// <summary>
        /// 设置是否反转水平轴。对应 UE5 SetInvertHorizontal。
        /// </summary>
        public void SetInvertHorizontal(bool NewInvertHorizontal)
        {
            bInvertHorizontal = NewInvertHorizontal;
            NarrativeLog.Log($"水平轴反转已设置为 {NewInvertHorizontal}。");
        }

        /// <summary>
        /// 获取是否反转水平轴。对应 UE5 GetInvertHorizontal。
        /// </summary>
        public bool GetInvertHorizontal()
        {
            return bInvertHorizontal;
        }
    }
}
