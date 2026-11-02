using System;
using System.Reflection;
using FlaxEngine;
using FlaxEngine.GUI;

namespace QuestEditor
{
    /// <summary>
    /// 把 NarrativePro 插件的"内嵌叙事面板"以反射方式装进任务编辑器。
    /// 为什么走反射：两个插件是独立装配，任务编辑器不该因为叙事编辑器没编好就起不来；
    /// 面板本体仍是叙事插件里的同一份实现（图/检查器/校验/撤销），这里只做薄薄的调用转发。
    /// </summary>
    internal static class NarrativePanelLink
    {
        private const string PanelTypeName = "NarrativePro.EditorTools.NarrativeQuestPanel";

        public static bool IsAvailable => FindType(PanelTypeName) != null;

        /// <summary>
        /// 创建一个面板（返回 Control，任务编辑器直接 AddChild 即可）。失败返回 null 并给出理由。
        /// </summary>
        public static Control Create(out string error)
        {
            error = null;
            var type = FindType(PanelTypeName);
            if (type == null)
            {
                error = "叙事资产编辑器插件未加载（找不到 NarrativeQuestPanel）。";
                return null;
            }

            try
            {
                var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                if (create == null)
                {
                    error = "叙事面板版本过旧：缺少 Create()。";
                    return null;
                }

                var control = create.Invoke(null, null) as Control;
                if (control == null) error = "叙事面板创建失败。";
                return control;
            }
            catch (Exception ex)
            {
                error = "创建叙事面板失败：" + Unwrap(ex).Message;
                return null;
            }
        }

        /// <summary>调面板上的方法；返回它的返回值（void 返回 null）。失败时 result=null 并给出理由。</summary>
        public static object Invoke(object panel, string method, out string error, params object[] arguments)
        {
            error = null;
            if (panel == null)
            {
                error = "叙事面板未创建";
                return null;
            }

            try
            {
                var target = panel.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
                if (target == null)
                {
                    error = "叙事面板缺少方法 " + method + "（版本不匹配）";
                    return null;
                }
                return target.Invoke(panel, arguments);
            }
            catch (Exception ex)
            {
                error = method + " 失败：" + Unwrap(ex).Message;
                return null;
            }
        }

        private static Exception Unwrap(Exception ex) =>
            ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;

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
