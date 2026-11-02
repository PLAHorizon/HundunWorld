// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系详见 NarrativeStableActor.cs 顶部说明。
//
// UE5 中 USaveSystemDeveloperSettings 通过 config=Engine 持久化，并由 GetDefault<>() 读取
// （见 NarrativeSaveSubsystem.cpp:1040-1053 用 SaveGameClass 决定存档对象类型）。
// Flax 没有开发者设置系统，这里用 JSON 文件实现同等能力：
//   工程 Saved/SaveSystemDeveloperSettings.json 优先，其次 %LOCALAPPDATA%/HundunWorld/SaveSystem/…，
//   都不存在时写出默认值，便于策划/程序就地修改。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativePro.Core;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 存档系统开发者设置。对应 UE5 USaveSystemDeveloperSettings。
    /// </summary>
    [Serializable]
    public class SaveSystemDeveloperSettings
    {
        /// <summary>配置文件名。对应 UE5 config 文件角色。</summary>
        public const string SettingsFileName = "SaveSystemDeveloperSettings.json";

        /// <summary>共享存档目录（相对工程 Saved 目录，或绝对路径）。对应 UE5 SharedSavesDirectory。</summary>
        public string SharedSavesDirectory = "SharedSaves";

        /// <summary>存档文件根目录覆盖。为空时使用 %LOCALAPPDATA%/HundunWorld/SaveSystem。</summary>
        public string SaveDirectoryOverride = string.Empty;

        /// <summary>
        /// 存档对象类型路径（Type.FullName 或 "FullName, Assembly"）。对应 UE5 SaveGameClass（FSoftClassPath）。
        /// 默认指向 NarrativePro.SaveSystem.NarrativeSave。
        /// </summary>
        public string SaveGameClassPath = "NarrativePro.SaveSystem.NarrativeSave, NarrativePro";

        /// <summary>默认存档名。对应 UE5 Save(SaveName="NarrativeSave")。</summary>
        public string DefaultSaveName = "NarrativeSave";

        /// <summary>默认存档槽。</summary>
        public int DefaultSaveSlot = 0;

        /// <summary>是否在场景加载时自动初始化存档系统（对应 UE5 GameMode::InitGame 调用）。</summary>
        public bool bAutoInitializeOnSceneLoaded = true;

        /// <summary>GameMode 选项串里没有 SaveGameName 时，是否自动尝试续读默认槽（对应 UE5 bAutoLoadFirstSaveInEditor 的意图）。</summary>
        public bool bAutoLoadDefaultSlot = true;

        /// <summary>是否在场景卸载时自动落盘（对应 UE5 关卡切换前静默保存）。</summary>
        public bool bAutoSaveOnSceneUnloading = true;

        /// <summary>自动存档间隔（秒）。<=0 表示关闭定时自动存档。</summary>
        public float AutoSaveIntervalSeconds = 120f;

        /// <summary>不参与存档的场景名关键字（登录、创建角色、主菜单等）。</summary>
        public List<string> ExcludedSceneNames = new List<string>
        {
            "Login",
            "CharacterCreator",
            "MainMenu",
        };

        /// <summary>是否把 Actor 上其它脚本的状态也作为组件记录归档（保守默认关闭，开启后覆盖全部脚本）。</summary>
        public bool bArchiveAllSavableScripts = false;

        /// <summary>单例实例。对应 UE5 GetDefault&lt;USaveSystemDeveloperSettings&gt;()。</summary>
        public static SaveSystemDeveloperSettings Instance { get; set; } = LoadDefault();

        /// <summary>读取设置文件（存在则反序列化，不存在则写出默认值）。</summary>
        private static SaveSystemDeveloperSettings LoadDefault()
        {
            SaveSystemDeveloperSettings settings = null;

            foreach (var path in CandidateSettingsPaths())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    string json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    settings = JsonSerializer.Deserialize<SaveSystemDeveloperSettings>(json, new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                        // 本设置类的可配项全是公开字段（:29-67），System.Text.Json 默认不绑字段，
                        // 少了这行就等于"文件读到了但每一项都用代码默认值"——作者改设置无任何效果。
                        IncludeFields = true,
                    });

                    if (settings != null)
                    {
                        NarrativeLog.Log($"[SaveSystem] SaveSystemDeveloperSettings 已从 {path} 加载");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"[SaveSystem] 读取开发者设置失败 {path}: {ex.Message}");
                }
            }

            if (settings == null)
            {
                settings = new SaveSystemDeveloperSettings();
                NarrativeLog.Log("[SaveSystem] SaveSystemDeveloperSettings 使用默认值。");
            }

            settings.EnsureDefaults();
            settings.TryWriteIfMissing();
            return settings;
        }

        /// <summary>补齐被手工编辑删空的字段。</summary>
        private void EnsureDefaults()
        {
            if (string.IsNullOrEmpty(SaveGameClassPath))
            {
                SaveGameClassPath = "NarrativePro.SaveSystem.NarrativeSave, NarrativePro";
            }
            if (string.IsNullOrEmpty(DefaultSaveName))
            {
                DefaultSaveName = "NarrativeSave";
            }
            if (ExcludedSceneNames == null)
            {
                ExcludedSceneNames = new List<string>();
            }
        }

        private void TryWriteIfMissing()
        {
            try
            {
                string dir = SettingsDirectory();
                string path = Path.Combine(dir, SettingsFileName);
                if (File.Exists(path)) return;

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    IncludeFields = true,
                }));
            }
            catch (Exception ex)
            {
                // 设置文件写出失败不影响运行
                NarrativeLog.LogWarning($"[SaveSystem] 开发者设置写出失败: {ex.Message}");
            }
        }

        private static IEnumerable<string> CandidateSettingsPaths()
        {
            yield return Path.Combine(SaveDirectory(), SettingsFileName);
        }

        /// <summary>设置目录（与存档目录一致，便于随存档一起备份）。</summary>
        public static string SettingsDirectory()
        {
            return SaveDirectory();
        }

        /// <summary>
        /// 存档根目录。对应 UE5 UGameplayStatics 的存档目录。
        /// 优先使用 SaveDirectoryOverride，其次 LOCALAPPDATA/HundunWorld/SaveSystem，最后当前工作目录。
        /// </summary>
        public static string SaveDirectory()
        {
            var instance = Instance;
            if (instance != null && !string.IsNullOrEmpty(instance.SaveDirectoryOverride))
            {
                return instance.SaveDirectoryOverride;
            }

            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = Environment.CurrentDirectory;
            }
            return Path.Combine(baseDir, "HundunWorld", "SaveSystem");
        }

        /// <summary>共享存档目录（绝对路径）。对应 UE5 SharedSavesDirectory。</summary>
        public static string ResolveSharedSavesDirectory()
        {
            var instance = Instance;
            if (instance == null) return SaveDirectory();

            string value = instance.SharedSavesDirectory;
            if (string.IsNullOrEmpty(value)) return SaveDirectory();

            if (Path.IsPathRooted(value)) return value;
            return Path.Combine(SaveDirectory(), value);
        }

        /// <summary>
        /// 解析 SaveGameClassPath 为具体存档对象类型。对应 UE5 SaveGameClass.TryLoadClass&lt;UNarrativeSave&gt;()。
        /// 解析失败或类型不合约定时回退到 typeof(NarrativeSave)。
        /// </summary>
        public static Type ResolveSaveGameClass()
        {
            string path = Instance?.SaveGameClassPath;
            if (string.IsNullOrEmpty(path)) return typeof(NarrativeSave);

            Type type = null;
            try
            {
                type = Type.GetType(path, false);

                if (type == null)
                {
                    // 兼容仅写类型全名的配置
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = asm.GetType(path, false);
                        if (type != null) break;
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 解析存档类型失败 {path}: {ex.Message}");
                type = null;
            }

            if (type == null)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 未找到存档类型 {path}，回退 NarrativeSave");
                return typeof(NarrativeSave);
            }

            if (!typeof(NarrativeSave).IsAssignableFrom(type))
            {
                NarrativeLog.LogWarning($"[SaveSystem] 存档类型 {type.FullName} 未继承 NarrativeSave，回退默认类型");
                return typeof(NarrativeSave);
            }

            return type;
        }
    }
}
