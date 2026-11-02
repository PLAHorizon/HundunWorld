#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace NarrativePro.EditorTools
{
    /// <summary>
    /// 与「任务编辑器」(QuestEditor 插件) 的桥。两个插件分属不同装配，这里用反射解耦：
    /// 任务编辑器没装/没加载时，调用方拿到一条明确错误，而不是编译依赖或 TypeLoadException。
    /// </summary>
    internal static class QuestEditorLink
    {
        private const string WindowTypeName = "QuestEditor.QuestEditorWindow";
        private const string CliTypeName = "QuestEditor.QuestCli";

        /// <summary>任务编辑器目录里的一行（只取跨编辑器联动要用的字段）。</summary>
        internal sealed class CatalogTemplate
        {
            public int Id;
            public string Name = "";
            public int PublishState;
            public bool Valid = true;
        }

        /// <summary>任务编辑器插件是否已加载（决定"在任务编辑器打开"按钮是否可用）。</summary>
        public static bool IsAvailable => FindType(WindowTypeName) != null;

        /// <summary>只唤起任务编辑器窗口（不选具体模板）。</summary>
        public static string Show()
        {
            var type = FindType(WindowTypeName);
            if (type == null) return "任务编辑器插件未加载（找不到 QuestEditor.QuestEditorWindow）。";

            try
            {
                type.GetMethod("ShowWindow", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                return null;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                return "打开任务编辑器失败：" + inner.Message;
            }
        }

        /// <summary>唤起任务编辑器并选中指定模板 id；成功返回 null，失败返回给用户看的理由。</summary>
        public static string OpenTemplate(int templateId)
        {
            var type = FindType(WindowTypeName);
            if (type == null) return "任务编辑器插件未加载（找不到 QuestEditor.QuestEditorWindow）。";

            try
            {
                type.GetMethod("ShowWindow", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);

                object instance = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                if (instance == null) return "任务编辑器已唤起，但拿不到窗口实例。";

                var select = type.GetMethod("SelectQuestById", BindingFlags.Public | BindingFlags.Instance);
                if (select == null) return "任务编辑器窗口版本过旧：缺少 SelectQuestById。";

                object result = select.Invoke(instance, new object[] { templateId });
                if (result is bool ok && !ok) return $"任务编辑器已打开，但目录里没有任务 {templateId}。";
                return null;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                return "打开任务编辑器失败：" + inner.Message;
            }
        }

        /// <summary>目录 id 列表（审计/校验用）。CLI 不可用时返回 null 并给出原因。</summary>
        public static List<int> TryGetCatalogIds(out string error)
        {
            var rows = TryGetCatalog(out error);
            if (rows == null) return null;

            var ids = new List<int>();
            foreach (var row in rows) ids.Add(row.Id);
            return ids;
        }

        /// <summary>
        /// 取任务编辑器目录（id / 名称 / 发布状态 / 是否有效）。**每次都会起一次 CLI 子进程**，
        /// 只在用户点「刷新目录」「校验」或自动化命令里调用，别放进逐帧/逐次重建的路径。
        /// </summary>
        public static List<CatalogTemplate> TryGetCatalog(out string error)
        {
            error = null;
            var cli = FindType(CliTypeName);
            if (cli == null)
            {
                error = "任务编辑器插件未加载";
                return null;
            }

            try
            {
                var run = cli.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(string) }, null);
                if (run == null)
                {
                    error = "QuestCli.Run 签名变化";
                    return null;
                }

                object result = run.Invoke(null, new object[] { "catalog", null });
                if (result == null)
                {
                    error = "CLI 无返回";
                    return null;
                }

                var resultType = result.GetType();
                bool ok = (bool)(resultType.GetField("Ok")?.GetValue(result) ?? false);
                bool hasData = (bool)(resultType.GetField("HasData")?.GetValue(result) ?? false);
                string message = resultType.GetField("Message")?.GetValue(result) as string ?? "";
                if (!ok || !hasData)
                {
                    error = string.IsNullOrEmpty(message) ? "CLI 未返回目录" : message;
                    return null;
                }

                // catalog 的 data 是个对象，任务清单在 data.quests[]；
                // 不能指望 HasData 覆盖数组型 data —— 那个桥只在 data 是对象时才置 HasData。
                var data = (JsonElement)resultType.GetField("Data").GetValue(result);
                if (data.ValueKind != JsonValueKind.Object ||
                    !data.TryGetProperty("quests", out var quests) ||
                    quests.ValueKind != JsonValueKind.Array)
                {
                    error = "catalog 返回里没有 quests 数组";
                    return null;
                }

                var rows = new List<CatalogTemplate>();
                foreach (var row in quests.EnumerateArray())
                {
                    if (!row.TryGetProperty("questId", out var idProperty) || !idProperty.TryGetInt32(out int id)) continue;

                    var entry = new CatalogTemplate { Id = id };
                    if (row.TryGetProperty("questName", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                        entry.Name = nameProperty.GetString() ?? "";
                    if (row.TryGetProperty("publishState", out var stateProperty) && stateProperty.TryGetInt32(out int state))
                        entry.PublishState = state;
                    if (row.TryGetProperty("valid", out var validProperty) &&
                        (validProperty.ValueKind == JsonValueKind.False)) entry.Valid = false;
                    rows.Add(entry);
                }
                return rows;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>按类型全名在已加载装配里找类型（跨插件反射共用）。</summary>
        internal static Type Find(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null) return type;
                }
                catch
                {
                    // 个别装配的 GetType 会抛类型加载异常，跳过它继续找
                }
            }
            return null;
        }

        private static Type FindType(string fullName) => Find(fullName);
    }
}
#endif
