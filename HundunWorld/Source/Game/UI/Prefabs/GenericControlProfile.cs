using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>
    /// 通用控件 Prefab 参数档案：按类型名创建任意自定义控件，
    /// 并以「属性名 → 字符串值」反射写入（支持 string/bool/int/float/enum/Float2/Color）。
    /// 用于尚未提供专用 Profile 的中低频组件（列表、雷达图、小地图等），
    /// 保证它们也能先 Prefab 化复用，后续按需升级为专用 Profile。
    /// </summary>
    [ExecuteInEditMode]
    public class GenericControlProfile : UIComponentProfile
    {
        /// <summary>目标控件类型全名（如 HundunWorld.Game.UI.Ink.InkTabButton）</summary>
        [EditorOrder(100), EditorDisplay("通用控件"), Tooltip("控件类型全名（含命名空间），类型须有公共无参构造函数")]
        public string ControlTypeName = string.Empty;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(101), EditorDisplay("布局")]
        public float Width = -1f;

        /// <summary>高度覆写，&lt;=0 保持原高</summary>
        [EditorOrder(102), EditorDisplay("布局")]
        public float Height = -1f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(103), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <summary>是否可见</summary>
        [EditorOrder(104), EditorDisplay("布局")]
        public bool Visible = true;

        /// <summary>附加属性名列表（与属性值按下标一一对应）</summary>
        [EditorOrder(105), EditorDisplay("属性"), Tooltip("属性名（公共可写属性），与下方值列表一一对应")]
        public System.Collections.Generic.List<string> PropertyNames = new System.Collections.Generic.List<string>();

        /// <summary>附加属性值（字符串表示，自动转换类型）</summary>
        [EditorOrder(106), EditorDisplay("属性"), Tooltip("属性值字符串：支持 true/false、数字、枚举名、\"x,y\"(Float2)、\"r,g,b,a\"(Color,0~1)")]
        public System.Collections.Generic.List<string> PropertyValues = new System.Collections.Generic.List<string>();

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            var type = ResolveType(ControlTypeName);
            if (type == null)
            {
                Debug.LogWarning($"[GenericControlProfile] 无法解析控件类型: \"{ControlTypeName}\"（Actor={Actor?.Name}）");
                return null;
            }

            try
            {
                return (Control)Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GenericControlProfile] 创建控件 {ControlTypeName} 失败（需公共无参构造）: {ex.Message}");
                return null;
            }
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            control.Visible = Visible;

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);

            var count = Math.Min(
                PropertyNames?.Count ?? 0,
                PropertyValues?.Count ?? 0);
            for (var i = 0; i < count; i++)
            {
                if (!string.IsNullOrEmpty(PropertyNames[i]))
                    UIPrefabUtils.TrySetProperty(control, PropertyNames[i].Trim(), PropertyValues[i]);
            }
        }

        /// <summary>在当前已加载脚本程序集中解析类型（支持全名或「Assembly.短名」省略命名空间匹配）。</summary>
        public static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            typeName = typeName.Trim();
            var type = Type.GetType(typeName, false);
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false);
                if (type != null)
                    return type;
            }

            // 允许只填短类名：在所有脚本程序集中按 Name 匹配
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = assembly.GetName().Name;
                if (name == null || (!name.EndsWith(".CSharp", StringComparison.Ordinal) && name != "Game"))
                    continue;
                foreach (var candidate in assembly.GetTypes())
                {
                    if (candidate.Name == typeName && typeof(Control).IsAssignableFrom(candidate))
                        return candidate;
                }
            }
            return null;
        }
    }
}
