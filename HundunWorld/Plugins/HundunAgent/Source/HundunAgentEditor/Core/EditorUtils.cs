using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FlaxEngine;

namespace HundunAgent.Core
{
    /// <summary>
    /// 编辑器通用工具：Actor 查找、JSON 参数解析、反射赋值。
    /// 所有方法均假设已在主线程执行（除显式说明）。
    /// </summary>
    public static class EditorUtils
    {
        /// <summary>
        /// 按名称（不区分大小写，支持模糊后缀匹配）或 Guid 字符串查找 Actor。
        /// </summary>
        public static Actor FindActor(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId))
                return null;

            var scenes = Level.Scenes;
            if (scenes == null)
                return null;

            // 优先精确 Guid
            if (Guid.TryParse(nameOrId, out var guid))
            {
                foreach (var scene in scenes)
                {
                    if (scene == null) continue;
                    var byId = FindActorById(scene, guid);
                    if (byId != null)
                        return byId;
                }
            }

            // 精确名称
            foreach (var scene in scenes)
            {
                if (scene == null) continue;
                var exact = FindActorByName(scene, nameOrId, false);
                if (exact != null)
                    return exact;
            }

            // 包含匹配
            foreach (var scene in scenes)
            {
                if (scene == null) continue;
                var fuzzy = FindActorByName(scene, nameOrId, true);
                if (fuzzy != null)
                    return fuzzy;
            }

            return null;
        }

        private static Actor FindActorById(Actor root, Guid id)
        {
            if (root.ID == id)
                return root;
            foreach (var child in root.Children)
            {
                var found = FindActorById(child, id);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Actor FindActorByName(Actor root, string name, bool contains)
        {
            if (contains)
            {
                if (root.Name != null && root.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 && !(root is Scene))
                    return root;
            }
            else
            {
                if (string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase) && !(root is Scene))
                    return root;
            }

            foreach (var child in root.Children)
            {
                var found = FindActorByName(child, name, contains);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>确保存在已加载场景，返回第一个场景。</summary>
        public static Scene RequireScene()
        {
            var scenes = Level.Scenes;
            if (scenes == null || scenes.Length == 0 || scenes[0] == null)
                throw new InvalidOperationException("当前没有打开的场景，请先用 scene_load 打开场景");
            return scenes[0];
        }

        /// <summary>读取 {x,y,z}（兼容大写）。</summary>
        public static Vector3 GetVector3(JsonElement obj, Vector3 defaultValue = default)
        {
            if (obj.ValueKind != JsonValueKind.Object)
                return defaultValue;
            return new Vector3(
                GetNum(obj, "x", defaultValue.X),
                GetNum(obj, "y", defaultValue.Y),
                GetNum(obj, "z", defaultValue.Z));
        }

        public static float GetNum(JsonElement obj, string name, float defaultValue = 0f)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetSingle();
            var upper = char.ToUpperInvariant(name[0]) + name.Substring(1);
            if (obj.TryGetProperty(upper, out var v2) && v2.ValueKind == JsonValueKind.Number)
                return v2.GetSingle();
            return defaultValue;
        }

        public static string GetString(JsonElement obj, string name, string defaultValue = null)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return defaultValue;
        }

        public static bool GetBool(JsonElement obj, string name, bool defaultValue = false)
        {
            if (obj.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.False) return false;
            }
            return defaultValue;
        }

        public static int GetInt(JsonElement obj, string name, int defaultValue = 0)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
            return defaultValue;
        }

        /// <summary>
        /// 将 JSON 值转换为目标 C# 类型（反射属性赋值用）。
        /// 支持：基础类型 / enum（名称或数字）/ string / Vector2/3/4 / Color / Guid / Asset（按路径加载）。
        /// </summary>
        public static object ConvertJsonValue(Type targetType, JsonElement value)
        {
            if (targetType == typeof(int)) return value.GetInt32();
            if (targetType == typeof(float)) return value.GetSingle();
            if (targetType == typeof(double)) return value.GetDouble();
            if (targetType == typeof(bool)) return value.GetBoolean();
            if (targetType == typeof(string)) return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            if (targetType == typeof(Guid)) return Guid.Parse(value.GetString());

            if (targetType.IsEnum)
            {
                if (value.ValueKind == JsonValueKind.Number)
                    return Enum.ToObject(targetType, value.GetInt64());
                return Enum.Parse(targetType, value.GetString(), true);
            }

            if (targetType == typeof(Vector2))
                return new Vector2(GetNum(value, "x"), GetNum(value, "y"));
            if (targetType == typeof(Vector3))
                return GetVector3(value);
            if (targetType == typeof(Vector4))
                return new Vector4(GetNum(value, "x"), GetNum(value, "y"), GetNum(value, "z"), GetNum(value, "w"));
            if (targetType == typeof(Float2))
                return new Float2(GetNum(value, "x"), GetNum(value, "y"));
            if (targetType == typeof(Float3))
                return new Float3(GetNum(value, "x"), GetNum(value, "y"), GetNum(value, "z"));
            if (targetType == typeof(Float4))
                return new Float4(GetNum(value, "x"), GetNum(value, "y"), GetNum(value, "z"), GetNum(value, "w"));
            if (targetType == typeof(Quaternion))
            {
                // 支持欧拉角 {pitch,yaw,roll} 或四元数 {x,y,z,w}
                if (value.TryGetProperty("w", out _))
                    return new Quaternion(GetNum(value, "x"), GetNum(value, "y"), GetNum(value, "z"), GetNum(value, "w"));
                return Quaternion.Euler(GetNum(value, "x"), GetNum(value, "y"), GetNum(value, "z"));
            }
            if (targetType == typeof(Color))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var hex = value.GetString();
                    if (Color.TryParse(hex, out var parsed))
                        return parsed;
                    throw new ArgumentException("无法解析颜色: " + hex);
                }
                return new Color(GetNum(value, "r"), GetNum(value, "g"), GetNum(value, "b"), GetNum(value, "a", 1f));
            }
            if (targetType == typeof(Color32))
            {
                var c = (Color)ConvertJsonValue(typeof(Color), value);
                return new Color32((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255), (byte)(c.A * 255));
            }

            // Asset 引用：按路径加载
            if (typeof(Asset).IsAssignableFrom(targetType) && value.ValueKind == JsonValueKind.String)
            {
                var path = value.GetString();
                if (string.IsNullOrEmpty(path))
                    return null;
                var asset = Content.Load(path, 10000.0);
                if (asset == null)
                    throw new ArgumentException("资产加载失败: " + path);
                if (!targetType.IsInstanceOfType(asset))
                {
                    // 尝试按目标类型重新加载
                    var typed = LoadAssetTyped(targetType, path);
                    if (typed != null)
                        return typed;
                    throw new ArgumentException("资产类型不匹配: " + path + " 实际为 " + asset.GetType().Name + "，需要 " + targetType.Name);
                }
                return asset;
            }

            throw new NotSupportedException("不支持的属性类型: " + targetType.Name);
        }

        /// <summary>按具体 Asset 子类型加载（Content.Load 泛型反射版本）。</summary>
        public static Asset LoadAssetTyped(Type assetType, string path)
        {
            var loadMethod = typeof(Content).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Load" && m.IsGenericMethod && m.GetParameters().Length == 2);
            if (loadMethod == null)
                return null;
            var generic = loadMethod.MakeGenericMethod(assetType);
            return generic.Invoke(null, new object[] { path, 10000.0 }) as Asset;
        }

        /// <summary>
        /// 通过反射为对象设置属性（支持常见类型与 Asset 引用）。
        /// 返回旧值（用于 Undo）。
        /// </summary>
        public static object SetPropertyByReflection(object target, string propertyName, JsonElement value)
        {
            var prop = target.GetType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                throw new ArgumentException(target.GetType().Name + " 上不存在可写属性: " + propertyName);

            var oldValue = prop.GetValue(target);
            var newValue = ConvertJsonValue(prop.PropertyType, value);
            prop.SetValue(target, newValue);
            return oldValue;
        }

        /// <summary>在程序集范围内查找 Flax 或项目类型（按简单名）。</summary>
        public static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType("FlaxEngine." + typeName, false);
                    if (type != null)
                        return type;
                    type = assembly.GetType(typeName, false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // 某些动态程序集枚举类型会抛异常
                }
            }
            return null;
        }
    }
}
