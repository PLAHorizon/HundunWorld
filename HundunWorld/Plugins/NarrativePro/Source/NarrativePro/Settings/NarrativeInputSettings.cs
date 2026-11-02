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

        /// <summary>
        /// 配置文件默认路径。对应 UE5 EnhancedInputUserSettings（config=GameUserSettings）的落盘位置。
        /// </summary>
        public const string DefaultConfigPath = "Content/NarrativePro/Settings/NarrativeInputSettings.json";

        /// <summary>单例实例。</summary>
        public static NarrativeInputSettings Instance { get; set; } = LoadDefault();

        /// <summary>读取默认配置：优先 JSON 配置文件，缺失或损坏时回退到构造默认值。</summary>
        private static NarrativeInputSettings LoadDefault()
        {
            return LoadFromFile(DefaultConfigPath) ?? new NarrativeInputSettings();
        }

        /// <summary>
        /// 从 JSON 文件加载设置。文件不存在返回 null（调用方按默认值处理），
        /// 解析失败写警告后返回 null，不拖垮启动。
        /// </summary>
        public static NarrativeInputSettings LoadFromFile(string jsonFilePath)
        {
            if (string.IsNullOrEmpty(jsonFilePath)) return null;

            try
            {
                // 相对路径按工程根目录归一（见 NarrativeDataPaths）。
                jsonFilePath = NarrativePro.Data.NarrativeDataPaths.Resolve(jsonFilePath);
                if (!System.IO.File.Exists(jsonFilePath)) return null;

                string json = System.IO.File.ReadAllText(jsonFilePath);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var loaded = System.Text.Json.JsonSerializer.Deserialize<NarrativeInputSettings>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    IncludeFields = true
                });
                if (loaded != null)
                {
                    NarrativeLog.Log($"NarrativeInputSettings 已从 {jsonFilePath} 加载。");
                }
                return loaded;
            }
            catch (System.Exception ex)
            {
                NarrativeLog.LogWarning($"加载输入设置 '{jsonFilePath}' 失败，改用默认值: {ex.Message}");
                return null;
            }
        }

        /// <summary>把当前实例写回 JSON 文件（对应 UE5 ApplySettings 的持久化语义）。</summary>
        public void SaveToFile(string jsonFilePath = DefaultConfigPath)
        {
            if (string.IsNullOrEmpty(jsonFilePath)) return;

            try
            {
                var directory = System.IO.Path.GetDirectoryName(jsonFilePath);
                if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);

                string json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    IncludeFields = true
                });
                System.IO.File.WriteAllText(jsonFilePath, json);
            }
            catch (System.Exception ex)
            {
                NarrativeLog.LogWarning($"保存输入设置 '{jsonFilePath}' 失败: {ex.Message}");
            }
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
