using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace QuestEditor
{
    /// <summary>
    /// 与「NarrativePro 叙事资产编辑器」的桥。两侧插件分属不同装配，这里反射解耦：
    /// 叙事编辑器没装/没加载时，调用方拿到一条明确理由，而不是编译依赖或 TypeLoadException。
    /// </summary>
    internal static class NarrativeLink
    {
        private const string StoreTypeName = "NarrativePro.EditorTools.NarrativeAssetStore";
        private const string PluginTypeName = "NarrativePro.EditorTools.NarrativeProAuthoringEditorPlugin";

        /// <summary>
        /// 打开该任务模板的叙事图；没有就一键生成骨架（开始→完成，questTemplateId 已填好）再打开。
        /// 成功返回 null，失败返回给用户看的理由。
        /// </summary>
        public static string OpenOrCreateForTemplate(int questId)
        {
            var store = FindType(StoreTypeName);
            if (store == null) return "叙事资产编辑器插件未加载（找不到 NarrativePro.EditorTools.NarrativeAssetStore）。";

            try
            {
                var findByTemplate = store.GetMethod("QuestAssetsForTemplate", BindingFlags.Public | BindingFlags.Static);
                if (findByTemplate == null) return "叙事资产编辑器版本过旧：缺少 QuestAssetsForTemplate。";

                // 签名带可选参数 (int templateId, string directory = null)：反射必须两个实参都给。
                var paths = findByTemplate.Invoke(null, new object[] { questId, null }) as List<string>;
                string assetName;

                if (paths != null && paths.Count > 0)
                {
                    assetName = Path.GetFileNameWithoutExtension(paths[0]);
                }
                else
                {
                    var create = store.GetMethod("CreateQuestAssetWithTemplate", BindingFlags.Public | BindingFlags.Static);
                    if (create == null) return "叙事资产编辑器版本过旧：缺少 CreateQuestAssetWithTemplate。";

                    // 签名 (int templateId, string assetName, out string path, out string assetId, string directory = null)
                    object[] arguments = { questId, "Q" + questId, null, null, null };
                    if (create.Invoke(null, arguments) is string createError && createError != null) return createError;
                    assetName = arguments[3] as string ?? "Q" + questId;
                }

                var plugin = FindType(PluginTypeName);
                var open = plugin?.GetMethod("OpenAssetByName", BindingFlags.Public | BindingFlags.Static);
                if (open == null) return "叙事资产编辑器插件未就绪：缺少 OpenAssetByName。";

                object opened = open.Invoke(null, new object[] { assetName });
                bool ok = opened is bool value && value;
                return ok
                    ? null
                    : "叙事资产 " + assetName + " 没能打开（叙事编辑器里有未保存修改时先保存或放弃，再试一次）。";
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                return "打开叙事编辑器失败：" + inner.Message;
            }
        }

        /// <summary>该模板已有叙事图资产时返回资产名，否则 null（找不到插件/出错也返回 null）。</summary>
        public static string TryGetAssetName(int questId)
        {
            var store = FindType(StoreTypeName);
            var find = store?.GetMethod("QuestAssetsForTemplate", BindingFlags.Public | BindingFlags.Static);
            if (find == null || questId <= 0) return null;

            try
            {
                var paths = find.Invoke(null, new object[] { questId, null }) as List<string>;
                return paths != null && paths.Count > 0 ? Path.GetFileNameWithoutExtension(paths[0]) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>模板 id → 叙事资产名（一次反射扫描；叙事插件没加载时返回空表）。</summary>
        public static Dictionary<int, string> TryGetAssetMap()
        {
            var store = FindType(StoreTypeName);
            var map = store?.GetMethod("TemplateAssetMap", BindingFlags.Public | BindingFlags.Static);
            if (map == null) return new Dictionary<int, string>();

            try
            {
                return map.Invoke(null, new object[] { null }) as Dictionary<int, string> ?? new Dictionary<int, string>();
            }
            catch
            {
                return new Dictionary<int, string>();
            }
        }

        private static Type FindType(string fullName)
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
    }
}
