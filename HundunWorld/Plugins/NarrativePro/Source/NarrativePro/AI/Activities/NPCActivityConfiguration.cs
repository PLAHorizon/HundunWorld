using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NarrativePro.Core;
using NarrativePro.Data;

namespace NarrativePro.AI.Activities
{
    /// <summary>
    /// NPC 活动配置。对应 UE5 UNPCActivityConfiguration。
    /// 数据资产，定义 NPC 可执行的活动和使用的目标生成器。
    /// </summary>
    [Serializable]
    public class NPCActivityConfiguration
    {
        /// <summary>重新评分目标的间隔（秒）。值越小响应越快但性能消耗越大</summary>
        public float RescoreInterval = 1.0f;

        /// <summary>默认活动类型路径列表</summary>
        public List<string> DefaultActivities = new List<string>();

        /// <summary>目标生成器类型路径列表</summary>
        public List<string> GoalGenerators = new List<string>();
    }

    /// <summary>
    /// 活动配置 / 活动调度的 JSON 加载器。
    /// <para>
    /// 对应 UE5 生成路径：NPC 定义引用的 <c>NPCActivityConfiguration</c> 数据资产在生成时
    /// 被同步加载并交给 <c>SetActivityConfiguration</c>（NPCActivityComponent.cpp:601-623），
    /// 调度资产逐条加载后交给 <c>AddActivitySchedule</c>（:568-591）。
    /// Flax 无 DataAsset/资产引用体系，按本插件既有的 JSON 数据约定落盘
    /// （与 Items/Quests 同一目录树 <c>Content/NarrativePro/</c>，路径经 <see cref="NarrativeDataPaths"/> 归一）。
    /// </para>
    /// </summary>
    public static class NPCActivityConfigurationLoader
    {
        /// <summary>活动配置与调度 JSON 的默认数据目录。</summary>
        public const string DefaultDirectory = "Content/NarrativePro/Activities";

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, NPCActivityConfiguration> _configCache =
            new Dictionary<string, NPCActivityConfiguration>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, NPCActivitySchedule> _scheduleCache =
            new Dictionary<string, NPCActivitySchedule>(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// 加载活动配置 JSON（带路径缓存，幂等）。资产缺失按 UE5 软引用缺失口径写 Error 并返回 null，
        /// 由调用方决定降级行为，不抛异常打断生成流程。
        /// </summary>
        public static NPCActivityConfiguration Load(string path, bool forceReload = false)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string resolved = NarrativeDataPaths.ResolveForRead(path, "NPC activity configuration");

            lock (_lock)
            {
                if (!forceReload && _configCache.TryGetValue(resolved, out var cached)) return cached;
            }

            NPCActivityConfiguration config = null;
            try
            {
                if (!File.Exists(resolved))
                {
                    NarrativeLog.LogError($"[Activities] 活动配置 JSON 缺失: '{resolved}' (原始路径: {path})。Has it been renamed or moved?");
                    return null;
                }
                string json = File.ReadAllText(resolved);
                config = JsonSerializer.Deserialize<NPCActivityConfiguration>(json, Options);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Activities] 活动配置加载失败（{path}）：{ex.Message}");
                return null;
            }

            if (config != null)
            {
                lock (_lock) { _configCache[resolved] = config; }
            }
            return config;
        }

        /// <summary>
        /// 加载活动调度 JSON。调度行为是多态集合（UE5 用 TSubclassOf 表达），
        /// JSON 里每个行为带 <c>BehaviorType</c>（CLR 类名/全名），经 <see cref="NPCActivityFactory"/>
        /// 反射创建具体实例，再用活动系统同一套字段回灌器还原其余字段。
        /// </summary>
        public static NPCActivitySchedule LoadSchedule(string path, bool forceReload = false)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string resolved = NarrativeDataPaths.ResolveForRead(path, "NPC activity schedule");

            lock (_lock)
            {
                if (!forceReload && _scheduleCache.TryGetValue(resolved, out var cached)) return cached;
            }

            if (!File.Exists(resolved))
            {
                NarrativeLog.LogError($"[Activities] 活动调度 JSON 缺失: '{resolved}' (原始路径: {path})。Has it been renamed or moved?");
                return null;
            }

            NPCActivitySchedule schedule = new NPCActivitySchedule();
            schedule.ScheduleAssetPath = path;
            try
            {
                string json = File.ReadAllText(resolved);
                using (var document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        NarrativeLog.LogError($"[Activities] 活动调度 JSON 根节点不是对象：{resolved}");
                        return null;
                    }

                    if (root.TryGetProperty("ScheduleAssetPath", out var assetPath) &&
                        assetPath.ValueKind == JsonValueKind.String)
                    {
                        string declared = assetPath.GetString();
                        if (!string.IsNullOrEmpty(declared)) schedule.ScheduleAssetPath = declared;
                    }

                    if (root.TryGetProperty("Behaviors", out var behaviors) &&
                        behaviors.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var behaviorElement in behaviors.EnumerateArray())
                        {
                            if (behaviorElement.ValueKind != JsonValueKind.Object) continue;

                            string behaviorType = null;
                            if (behaviorElement.TryGetProperty("BehaviorType", out var typeElement) &&
                                typeElement.ValueKind == JsonValueKind.String)
                            {
                                behaviorType = typeElement.GetString();
                            }
                            if (string.IsNullOrEmpty(behaviorType))
                            {
                                NarrativeLog.LogWarning($"[Activities] 调度 {path} 中跳过缺少 BehaviorType 的行为项");
                                continue;
                            }

                            var behavior = NPCActivityFactory.CreateScheduleBehavior(behaviorType);
                            if (behavior == null)
                            {
                                NarrativeLog.LogWarning($"[Activities] 无法解析调度行为类型 '{behaviorType}'（调度 {path}），该项被跳过");
                                continue;
                            }

                            // 与存档回灌同一套字段级还原：把 JSON 里的公开字段（StartTime/EndTime/Goal 载荷等）写入实例
                            NarrativeActivitySerializer.OverwriteFrom(Encoding.UTF8.GetBytes(behaviorElement.GetRawText()), behavior);
                            schedule.Activities.Add(behavior);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Activities] 活动调度加载失败（{path}）：{ex.Message}");
                return null;
            }

            lock (_lock) { _scheduleCache[resolved] = schedule; }
            return schedule;
        }

        /// <summary>清空缓存（数据热重载后调用）。</summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _configCache.Clear();
                _scheduleCache.Clear();
            }
        }
    }
}
