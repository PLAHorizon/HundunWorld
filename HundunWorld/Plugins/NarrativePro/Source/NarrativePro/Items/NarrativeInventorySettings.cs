using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// Narrative 背包插件的配置设置。适配 UE5 UNarrativeInventorySettings。
    /// UE5 中为 config=Engine, defaultconfig 的 UObject（DefaultEngine.ini 配置），
    /// Flax 无 ini 配置体系，等价实现为 [Serializable] 单例 + JSON 配置文件。
    /// </summary>
    [System.Serializable]
    public class NarrativeInventorySettings
    {
        /// <summary>是否允许同一物品存在多个堆叠？</summary>
        public bool bAllowMultipleStacks { get; set; } = false;

        /// <summary>
        /// 配置文件默认路径。对应 UE5 的 config=Engine 落盘位置。
        /// 该文件不存在时按默认值运行（等价 UE5 无 ini 覆盖时用 CDO 默认）。
        /// </summary>
        public const string DefaultConfigPath = "Content/NarrativePro/Settings/NarrativeInventorySettings.json";

        /// <summary>单例实例。</summary>
        public static NarrativeInventorySettings Instance { get; set; } = LoadDefault();

        public NarrativeInventorySettings()
        {
            // 默认构造
        }

        /// <summary>读取默认配置：优先 JSON 配置文件，缺失或损坏时回退到构造默认值。</summary>
        private static NarrativeInventorySettings LoadDefault()
        {
            return LoadFromFile(DefaultConfigPath) ?? new NarrativeInventorySettings();
        }

        /// <summary>
        /// 从 JSON 文件加载设置。文件不存在返回 null（调用方按默认值处理），
        /// 解析失败写一条警告后同样返回 null，不让配置错误拖垮启动。
        /// </summary>
        public static NarrativeInventorySettings LoadFromFile(string jsonFilePath)
        {
            if (string.IsNullOrEmpty(jsonFilePath)) return null;

            try
            {
                if (!System.IO.File.Exists(jsonFilePath)) return null;

                string json = System.IO.File.ReadAllText(jsonFilePath);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var loaded = System.Text.Json.JsonSerializer.Deserialize<NarrativeInventorySettings>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    IncludeFields = true
                });
                if (loaded != null)
                {
                    NarrativeLog.Log($"NarrativeInventorySettings 已从 {jsonFilePath} 加载。");
                }
                return loaded;
            }
            catch (System.Exception ex)
            {
                NarrativeLog.LogWarning($"加载背包设置 '{jsonFilePath}' 失败，改用默认值: {ex.Message}");
                return null;
            }
        }

        /// <summary>把当前实例写回 JSON 文件（编辑器/调试面板改完设置后持久化）。</summary>
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
                NarrativeLog.LogWarning($"保存背包设置 '{jsonFilePath}' 失败: {ex.Message}");
            }
        }
    }
}
