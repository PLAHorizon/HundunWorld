using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;

namespace HundunWorld.Game.Selection
{
    /// <summary>
    /// 宠物外观目录：把服务端下发的 <c>model_key</c> 映射到客户端 Prefab 路径。
    /// <para>
    /// <b>为什么要这么一层间接</b>：编辑器里配的是 <c>model_key</c>（策划可读的稳定标识）与可选的
    /// <c>prefab_guid</c>。数据库与线上包都不能指望"GUID 在这台机器上解析得开"
    /// （本工程实测过：路径拼写即资产身份，重启后按 ID 加载更稳）。所以这里只把
    /// <c>model_key → 路径</c> 放在一张随包发布的表里，路径再经既有的
    /// <c>TryGetAssetGuidFromPath</c> 解析成真实 GUID。
    /// </para>
    /// <para>
    /// <b>查不到时的口径</b>：返回空 → 调用方回退到 <c>FlaxActorSyncSystem.MonsterPrefabPath</c>
    /// → 再回退到角色占位 Prefab（即今天的行为）。<b>绝不静默换成别的怪的模样</b>：
    /// 配置漏了该表现为"这只宠还是占位人形 + 一条警告"，而不是随机长成新样子。
    /// </para>
    /// </summary>
    public static class PetVisualCatalog
    {
        /// <summary>表文件（相对工程根）。由宠物编辑器/CLI 从库里导出，随包发布。</summary>
        public const string RelativeTablePath = "Data/Pets/client_visuals.json";

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;
        private static string _source = string.Empty;

        private struct Entry
        {
            public string PrefabPath;
            public float Scale;
            public int StandState;
        }

        /// <summary>已加载的条目数（诊断与自检用；0 表示表缺失或为空，一切回退占位）。</summary>
        public static int Count => Entries.Count;

        /// <summary>表来源（诊断用：路径 or "未加载"）。</summary>
        public static string Source => _source;

        /// <summary>强制重载（编辑器/热更后用；正常情况首次访问即加载）。</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Entries.Clear();
            _source = "未加载";

            var root = Globals.ProjectFolder;
            if (string.IsNullOrEmpty(root)) return;

            var path = Path.Combine(root, RelativeTablePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                _source = "表不存在：" + path;
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    _source = "根节点不是对象";
                    return;
                }
                if (!doc.RootElement.TryGetProperty("visuals", out var visuals)
                    || visuals.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    _source = "缺少 visuals 数组";
                    return;
                }

                foreach (var item in visuals.EnumerateArray())
                {
                    var key = ReadString(item, "modelKey");
                    if (string.IsNullOrEmpty(key)) continue;
                    var entry = new Entry
                    {
                        PrefabPath = ReadString(item, "prefabPath"),
                        Scale = ReadFloat(item, "scale", 0f),
                        StandState = ReadInt(item, "standState", -1),
                    };
                    if (string.IsNullOrEmpty(entry.PrefabPath)) continue;
                    Entries[key] = entry;
                }
                _source = path + "（" + Entries.Count + " 条）";
            }
            catch (Exception ex)
            {
                Entries.Clear();
                _source = "解析失败：" + ex.GetBaseException().Message;
                Debug.LogWarning($"[PetVisualCatalog] 外观表解析失败，全部宠物回退占位 Prefab：{_source}");
            }
        }

        /// <summary>
        /// 查该模型键的 Prefab 路径。
        /// </summary>
        /// <param name="modelKey">服务端下发的模型键（空/未命中都返回 false）。</param>
        /// <param name="prefabPath">命中的路径。</param>
        /// <param name="scale">体型缩放（&lt;=0 = 未配，调用方保留下发值）。</param>
        /// <param name="standState">待机姿态（-1 = 未配）。</param>
        public static bool TryResolve(string? modelKey, out string prefabPath, out float scale, out int standState)
        {
            prefabPath = string.Empty;
            scale = 0f;
            standState = -1;
            if (string.IsNullOrEmpty(modelKey)) return false;

            EnsureLoaded();
            Entry hit;
            if (!Entries.TryGetValue(modelKey!, out hit)) return false;
            prefabPath = hit.PrefabPath;
            scale = hit.Scale;
            standState = hit.StandState;
            return true;
        }

        private static string ReadString(System.Text.Json.JsonElement e, string name)
        {
            System.Text.Json.JsonElement child;
            if (!e.TryGetProperty(name, out child) || child.ValueKind != System.Text.Json.JsonValueKind.String)
                return string.Empty;
            return child.GetString() ?? string.Empty;
        }

        private static float ReadFloat(System.Text.Json.JsonElement e, string name, float fallback)
        {
            System.Text.Json.JsonElement child;
            if (!e.TryGetProperty(name, out child) || child.ValueKind != System.Text.Json.JsonValueKind.Number)
                return fallback;
            return child.GetSingle();
        }

        private static int ReadInt(System.Text.Json.JsonElement e, string name, int fallback)
        {
            System.Text.Json.JsonElement child;
            if (!e.TryGetProperty(name, out child) || child.ValueKind != System.Text.Json.JsonValueKind.Number)
                return fallback;
            return child.GetInt32();
        }
    }
}
