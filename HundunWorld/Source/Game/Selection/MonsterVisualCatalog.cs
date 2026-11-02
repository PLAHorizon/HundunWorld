using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;

namespace HundunWorld.Game.Selection
{
    /// <summary>
    /// 怪物外观目录：把服务端下发的 <c>model_key</c> 映射到客户端 Prefab 路径。
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
    /// 配置漏了该表现为"这只怪还是占位人形 + 一条警告"，而不是随机长成新样子。
    /// </para>
    /// </summary>
    public static class MonsterVisualCatalog
    {
        /// <summary>表文件（相对工程根）。由怪物编辑器/CLI 从库里导出，随包发布。</summary>
        public const string RelativeTablePath = "Data/Monsters/client_visuals.json";

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;
        private static string _source = string.Empty;

        /// <summary>
        /// 上次读表时那条文件的时间戳/字节数（两者配成一对：同一秒里被重写但长度没变的情况极少，
        /// 而"长度变了时间戳没变"也不需要额外防——写文件必然动时间戳）。
        /// <para>
        /// **为什么要有这一层失效判据**：编辑器里的装配域（scripting domain）跨 Play-in-Editor 会话存活，
        /// 而这张表的缓存是静态字段。实测（2026-10-27）：外观表第一次导出时目录里还没有那个文件，
        /// 缓存把"表不存在"记成了一辈子；随后把文件写出来、重开 PIE，11 只怪照旧回退占位 Prefab，
        /// 日志原样复读"来源 表不存在"。也就是说**美术链在编辑器里改一次要重启一次编辑器才见效**，
        /// 而"改了没反应"正是这张表最贵的失败模式（它不会报错，只会让怪一直长得像人形）。
        /// 所以这里不靠布尔标记、也不靠 `Reload()`（那个入口全仓没有任何调用方），
        /// 而是每次查询都拿文件指纹比一下：变了就重读。
        /// </para>
        /// </summary>
        private static DateTime _fileStampUtc = DateTime.MinValue;
        private static long _fileStampLength = -1L;

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
            var root = Globals.ProjectFolder;
            if (string.IsNullOrEmpty(root)) return;

            var path = Path.Combine(root, RelativeTablePath.Replace('/', Path.DirectorySeparatorChar));

            // 缓存命中当且仅当"以前读过"且"这条文件本身没动过"。只判 _loaded 会把它读成一辈子：
            // 表是编辑器/CLI 在跑的过程中才导出来的，那一之后所有怪都会一直顶着占位外观。
            var stamp = ReadStamp(path);
            if (_loaded && stamp.Item1 == _fileStampUtc && stamp.Item2 == _fileStampLength) return;

            var previousCount = Entries.Count;
            _loaded = true;
            _fileStampUtc = stamp.Item1;
            _fileStampLength = stamp.Item2;
            Entries.Clear();
            _source = "未加载";

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
                // 这条是"外观链到底通没通"的唯一运行期凭据：条数与来源都打出来，
                // 才能把"表没导出""导出的是空表""键名对不上"三种情况分开学。
                Debug.Log($"[MonsterVisualCatalog] 外观表已读入：{_source}（此前 {previousCount} 条）");
            }
            catch (Exception ex)
            {
                Entries.Clear();
                _source = "解析失败：" + ex.GetBaseException().Message;
                Debug.LogWarning($"[MonsterVisualCatalog] 外观表解析失败，全部怪物回退占位 Prefab：{_source}");
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

        /// <summary>
        /// 取这条表的指纹（最后写入时间 + 字节数）。文件不存在回一个固定哨兵值，
        /// 于是"原本没有、后来被导出来了"天然算一次变化。
        /// 读指纹本身失败（文件正被导出进程写着）时回**上次**的指纹：
        /// 宁可晚一只怪生效，也不要在战斗流程里抛异常。
        /// </summary>
        private static (DateTime, long) ReadStamp(string path)
        {
            try
            {
                if (!File.Exists(path)) return (DateTime.MinValue, -1L);
                var info = new FileInfo(path);
                return (info.LastWriteTimeUtc, info.Length);
            }
            catch (Exception)
            {
                return (_fileStampUtc, _fileStampLength);
            }
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
