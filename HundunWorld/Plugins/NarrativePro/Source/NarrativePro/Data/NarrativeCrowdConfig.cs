using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Data
{
    /// <summary>
    /// 群体配置里的一条生成项。
    /// <para>
    /// <c>bIsPed</c> 为真时走 Mass 群集通路（<see cref="AI.Mass.Peds.MassPedSimulation.SpawnPedGroup"/>，
    /// 由 <see cref="AI.Mass.Peds.MassPedSpawnerSubsystem"/> 在近景按需生成 Actor），
    /// 否则用 <see cref="AI.NarrativeCharacterSubsystem.SpawnNPC"/> 直接生成一个可交互 NPC。
    /// </para>
    /// </summary>
    [Serializable]
    public class NpcCrowdEntryJson
    {
        /// <summary>NPC 定义 ID（对应 Content/NarrativePro/NPCs/&lt;id&gt;.json）。</summary>
        public string NpcId = "";

        /// <summary>生成数量（行人时为实体数）。</summary>
        public int Count = 1;

        /// <summary>是否走群集（ped）通路。</summary>
        public bool bIsPed = false;

        /// <summary>是否使用显式偏移（false 时在半径内随机散布）。</summary>
        public bool bUseOffset = false;

        /// <summary>相对生成中心的偏移。</summary>
        public Vector3 Offset = Vector3.Zero;

        /// <summary>随机散布半径（覆盖配置级半径；&lt;=0 时取配置级）。</summary>
        public float SpreadRadius = 0f;
    }

    /// <summary>
    /// 群体生成配置的磁盘形态。
    /// 对应 UE5 由关卡里摆放 AMassSpawner / ANPCSpawner 的做法 —— 本游戏不从场景实例化插件对象，
    /// 因此改由 JSON 驱动、由 <see cref="NarrativePro.CommonUI.NarrativeRuntimeBootstrap"/>
    /// 在玩法场景挂到运行期宿主上。
    /// </summary>
    [Serializable]
    public class NpcCrowdConfigJson
    {
        /// <summary>配置名（日志与诊断用）。</summary>
        public string Name = "";

        /// <summary>是否启用（false 时该文件被跳过）。</summary>
        public bool bEnabled = true;

        /// <summary>固定生成中心（世界坐标）。<see cref="bFollowLocalPlayer"/> 为 false 时生效。</summary>
        public Vector3 SpawnCenter = Vector3.Zero;

        /// <summary>是否以本地玩家/宿主位置为中心（优先于 <see cref="SpawnCenter"/>）。</summary>
        public bool bFollowLocalPlayer = true;

        /// <summary>默认散布半径。</summary>
        public float SpawnRadius = 240f;

        /// <summary>生成项。</summary>
        public List<NpcCrowdEntryJson> Entries = new List<NpcCrowdEntryJson>();
    }

    /// <summary>
    /// JSON 驱动的 NPC / 群集生成清单加载器。
    /// 磁盘资产：<c>Content/NarrativePro/Crowd/*.json</c>。
    /// </summary>
    public static class NarrativeCrowdConfig
    {
        /// <summary>群体配置目录（工程相对路径）。</summary>
        public const string DefaultCrowdDirectory = "Content/NarrativePro/Crowd";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static readonly List<NpcCrowdConfigJson> _configs = new List<NpcCrowdConfigJson>();
        private static bool _bLoaded;

        /// <summary>已加载的启用配置（只读视图）。</summary>
        public static IReadOnlyList<NpcCrowdConfigJson> Configs => _configs;

        /// <summary>是否存在任何可用的群体生成配置（不触发磁盘读取时使用）。</summary>
        public static bool HasAny()
        {
            if (!_bLoaded) LoadAll();
            return _configs.Count > 0;
        }

        /// <summary>幂等加载目录内全部配置。</summary>
        public static List<NpcCrowdConfigJson> LoadAll(bool force = false)
        {
            if (_bLoaded && !force) return _configs;
            _bLoaded = true;
            if (force) _configs.Clear();

            string resolved = NarrativeDataPaths.ResolveDirectory(DefaultCrowdDirectory);
            if (!Directory.Exists(resolved))
            {
                NarrativeLog.Log(
                    $"[Data] 群体配置目录不存在: '{resolved}'（配置值: {DefaultCrowdDirectory}）——不生成 NPC/行人。");
                return _configs;
            }

            foreach (var file in Directory.GetFiles(resolved, "*.json"))
            {
                var config = LoadFile(file);
                if (config == null) continue;
                if (!config.bEnabled)
                {
                    NarrativeLog.Log($"[Data] 群体配置 '{file}' 已标记 bEnabled=false，跳过。");
                    continue;
                }
                if (config.Entries == null || config.Entries.Count == 0)
                {
                    NarrativeLog.LogWarning($"[Data] 群体配置 '{file}' 没有任何生成项，跳过。");
                    continue;
                }
                _configs.Add(config);
            }

            int entries = 0;
            foreach (var config in _configs) entries += config.Entries.Count;
            NarrativeLog.Log($"[Data] NarrativeCrowdConfig: 群体配置 {_configs.Count} 份、生成项 {entries} 条（目录 {resolved}）。");
            return _configs;
        }

        /// <summary>加载单份配置（解析失败返回 null）。</summary>
        public static NpcCrowdConfigJson LoadFile(string jsonFilePath)
        {
            string resolved = NarrativeDataPaths.Resolve(jsonFilePath);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
            {
                NarrativeLog.LogError(
                    $"[Data] 群体配置 JSON 缺失: '{resolved}'（原始路径: {jsonFilePath}）。Has it been renamed or moved?");
                return null;
            }

            try
            {
                var config = JsonSerializer.Deserialize<NpcCrowdConfigJson>(File.ReadAllText(resolved), Options);
                if (config != null && string.IsNullOrEmpty(config.Name))
                    config.Name = Path.GetFileNameWithoutExtension(resolved);
                return config;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Data] 群体配置 '{resolved}' 解析失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>清缓存（切场景 / 热重载）。</summary>
        public static void ClearCache()
        {
            _configs.Clear();
            _bLoaded = false;
        }
    }
}
