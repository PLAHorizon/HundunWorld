using System;
using System.Collections.Generic;
using NarrativePro.Core;
using NarrativePro.Data;
using NarrativePro.Tales.Data;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 数据任务（<see cref="NarrativeDataTask"/>）资产的运行时缓存注册表。
    /// 对应 UE5 <c>UNarrativeTaskManager</c>（NarrativeTaskManager.h/.cpp），
    /// UE5 中为 GameInstanceSubsystem，通过 AssetRegistry 一次性扫描所有
    /// <c>UNarrativeDataTask</c> 资产并按 TaskName 缓存；Flax 无子系统/资产注册表等价物，
    /// 故实现为静态注册表（script singleton），首次访问或插件初始化时调用
    /// <see cref="CacheNarrativeTasks"/>，语义与 UE5 一致：
    /// <list type="bullet">
    /// <item>重复名保留首个并告警（UE5: "found duplicate Narrative Event assets"）。</item>
    /// <item><see cref="GetTask"/> 未命中时记录错误并返回 null（UE5: "Has it been renamed?"）。</item>
    /// <item><see cref="Deinitialize"/> 清空缓存（UE5 Deinitialize 中 NarrativeTaskMap.Empty()）。</item>
    /// </list>
    /// </summary>
    public static class NarrativeTaskManager
    {
        /// <summary>TaskName（去空格）→ 任务资产。对应 UE5 <c>NarrativeTaskMap</c>。</summary>
        private static readonly Dictionary<string, NarrativeDataTask> TaskMap =
            new Dictionary<string, NarrativeDataTask>(StringComparer.OrdinalIgnoreCase);

        private static bool bCached = false;

        /// <summary>UE5 用 TaskName 去空格作键；此处同名处理保持一致。</summary>
        private static string NormalizeKey(string taskName)
        {
            if (string.IsNullOrEmpty(taskName)) return "";
            return taskName.Replace(" ", "");
        }

        /// <summary>
        /// 扫描配置的数据任务目录并缓存全部任务资产。对应 UE5 <c>CacheNarrativeTasks</c>。
        /// 幂等：已缓存则跳过，与 UE5 Initialize 只跑一次一致。
        /// </summary>
        public static void CacheNarrativeTasks()
        {
            if (bCached) return;
            bCached = true;

            var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
            string dir = settings?.DefaultDataTaskDirectory ?? "Content/NarrativePro/DataTasks";

            Dictionary<string, NarrativeDataTask> loaded;
            try
            {
                loaded = NarrativeDataLoader.LoadAllDataTasks(dir);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"NarrativeTaskManager: failed scanning data task directory '{dir}': {ex.Message}");
                return;
            }

            // UE5: map 缓存资产，重复名字保留首个并告警。
            foreach (var kv in loaded)
            {
                NarrativeDataTask task = kv.Value;
                if (task == null) continue;

                string key = string.IsNullOrEmpty(task.TaskName) ? NormalizeKey(kv.Key) : NormalizeKey(task.TaskName);
                if (key.Length == 0) continue;

                if (TaskMap.ContainsKey(key))
                {
                    NarrativeLog.LogWarning($"Narrative found duplicate Narrative Event assets both named {task.TaskName} when scanning for events. Please ensure all events are uniquely named.");
                    continue;
                }
                TaskMap[key] = task;
            }
        }

        /// <summary>按名称取任务资产（走缓存，不重复读盘）。对应 UE5 <c>GetTask</c>。</summary>
        public static NarrativeDataTask GetTask(string eventName)
        {
            if (!bCached) CacheNarrativeTasks();
            if (string.IsNullOrEmpty(eventName)) return null;

            string key = NormalizeKey(eventName);
            if (TaskMap.TryGetValue(key, out var task) && task != null)
            {
                return task;
            }

            NarrativeLog.LogError($"Tried getting event {eventName} but couldn't find it. (Has it been renamed?)");
            return null;
        }

        /// <summary>注册/覆盖单个任务资产（供代码侧动态注入任务时使用）。</summary>
        public static void RegisterTask(NarrativeDataTask task)
        {
            if (task == null) return;
            if (!bCached) CacheNarrativeTasks();
            string key = NormalizeKey(task.TaskName);
            if (key.Length == 0) return;
            TaskMap[key] = task;
        }

        /// <summary>清空缓存。对应 UE5 <c>Deinitialize</c>。</summary>
        public static void Deinitialize()
        {
            TaskMap.Clear();
            bCached = false;
        }
    }
}
