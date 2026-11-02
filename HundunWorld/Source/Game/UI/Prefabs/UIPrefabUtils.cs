using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>
    /// Prefab 控件注册表。
    /// Flax 的 <see cref="Control"/> 无稳定可序列化的名字来源，
    /// 由 <see cref="UIComponentProfile"/> 在绑定控件时以「控件名（ControlName / 宿主 Actor 名）」为键注册，
    /// 供 <see cref="UIEventBinder"/>、<see cref="UIAnimationProfile"/> 等按名字解析目标控件。
    /// </summary>
    public static class UIPrefabRegistry
    {
        private static readonly Dictionary<string, Control> Entries = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        /// <summary>注册（重名时后者覆盖前者，符合 Prefab 实例后 Spawn 覆盖预览的直觉）</summary>
        public static void Register(string key, Control control)
        {
            if (string.IsNullOrEmpty(key) || control == null)
                return;
            Entries[key] = control;
        }

        /// <summary>注销（仅当当前登记的就是该控件时移除，防止误删同名新控件）</summary>
        public static void Unregister(string key, Control control)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (Entries.TryGetValue(key, out var existing) && ReferenceEquals(existing, control))
                Entries.Remove(key);
        }

        /// <summary>按名字解析控件；名字为空返回 null。已失效的登记项会被顺带清理。</summary>
        public static Control Resolve(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            if (!Entries.TryGetValue(key, out var control))
                return null;
            if (control == null || control.IsDisposing)
            {
                Entries.Remove(key);
                return null;
            }
            return control;
        }
    }

    /// <summary>
    /// Prefab 组件库通用辅助：主题令牌解析、锚点应用、反射取值/赋值的字符串解析。
    /// </summary>
    public static class UIPrefabUtils
    {
        private static Dictionary<string, Color> _tokenColors;

        /// <summary>
        /// 按名称解析 <see cref="StyleSystem.InkWashTheme"/> 中的静态 Color 字段（主题令牌）。
        /// 名称不区分大小写；解析失败返回 false（禁止硬编码色值，所有 Inspector 颜色配置走令牌名）。
        /// </summary>
        public static bool TryResolveColorToken(string tokenName, out Color color)
        {
            color = Color.Transparent;
            if (string.IsNullOrWhiteSpace(tokenName))
                return false;

            if (_tokenColors == null)
            {
                _tokenColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in InkWashThemeProbe.Type.GetFields(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (field.FieldType == typeof(Color))
                        _tokenColors[field.Name] = (Color)field.GetValue(null);
                }
            }

            if (_tokenColors.TryGetValue(tokenName.Trim(), out color))
                return true;

            color = default;
            return false;
        }

        /// <summary>按名称应用锚点预设；空名或非法名不生效，返回是否成功。</summary>
        public static bool TryApplyAnchor(Control control, string anchorName)
        {
            if (control == null || string.IsNullOrWhiteSpace(anchorName))
                return false;
            if (Enum.TryParse(anchorName.Trim(), true, out AnchorPresets preset))
            {
                control.AnchorPreset = preset;
                return true;
            }
            return false;
        }

        /// <summary>应用尺寸覆写（值 &lt;= 0 表示保持原尺寸）。</summary>
        public static void ApplySize(Control control, float width, float height)
        {
            if (control == null)
                return;
            if (width > 0f && height > 0f)
                control.Size = new Float2(width, height);
            else if (width > 0f)
                control.Width = width;
            else if (height > 0f)
                control.Height = height;
        }

        /// <summary>
        /// 将字符串解析为属性值并写入目标属性。
        /// 支持 string / bool / int / float / enum / Float2("x,y") / Vector2 / Color("r,g,b[,a]"，0~1)。
        /// </summary>
        public static bool TrySetProperty(object target, string propertyName, string stringValue)
        {
            if (target == null || string.IsNullOrEmpty(propertyName))
                return false;

            var type = target.GetType();
            var prop = type.GetProperty(propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                return false;

            try
            {
                var converted = ConvertString(prop.PropertyType, stringValue);
                if (converted == null && prop.PropertyType.IsValueType)
                    return false;
                prop.SetValue(target, converted);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UIPrefabUtils] 设置属性失败 {type.Name}.{propertyName}={stringValue}: {ex.Message}");
                return false;
            }
        }

        /// <summary>字符串 → 目标类型值转换（枚举不区分大小写）。</summary>
        public static object ConvertString(Type targetType, string value)
        {
            value = value == null ? string.Empty : value.Trim();

            if (targetType == typeof(string))
                return value;
            if (targetType == typeof(bool))
                return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                       (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("True", StringComparison.OrdinalIgnoreCase));
            if (targetType == typeof(int))
                return int.Parse(value);
            if (targetType == typeof(float))
                return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

            if (targetType.IsEnum)
                return Enum.Parse(targetType, value, true);

            if (targetType == typeof(Float2))
            {
                var parts = value.Split(',');
                return new Float2(float.Parse(parts[0]), parts.Length > 1 ? float.Parse(parts[1]) : 0f);
            }
            if (targetType == typeof(Vector2))
            {
                var parts = value.Split(',');
                return new Vector2(float.Parse(parts[0]), parts.Length > 1 ? float.Parse(parts[1]) : 0f);
            }
            if (targetType == typeof(Color))
            {
                var parts = value.Split(',');
                if (parts.Length < 3)
                    return null;
                return new Color(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]),
                    parts.Length > 3 ? float.Parse(parts[3]) : 1f);
            }
            return null;
        }

        /// <summary>在本 Actor 及其父链上查找宿主 <see cref="UIControl"/>。</summary>
        public static UIControl FindHostUIControl(Actor actor)
        {
            for (var a = actor; a != null; a = a.Parent)
            {
                if (a is UIControl uiControl)
                    return uiControl;
            }
            return null;
        }

        /// <summary>在本 Actor 及其父链上查找 <see cref="UICanvas"/>。</summary>
        public static UICanvas FindCanvasActor(Actor actor)
        {
            for (var a = actor; a != null; a = a.Parent)
            {
                if (a is UICanvas canvas)
                    return canvas;
            }
            return null;
        }
    }

    /// <summary>
    /// InkWashTheme 类型探针（隔离反射目标，便于将来主题类改名）。
    /// </summary>
    internal static class InkWashThemeProbe
    {
        public static readonly Type Type = typeof(StyleSystem.InkWashTheme);
    }
}
