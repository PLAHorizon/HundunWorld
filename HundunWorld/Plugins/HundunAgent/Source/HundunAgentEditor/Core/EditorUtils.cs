using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FlaxEngine;

namespace HundunAgent.Core
{
    /// <summary>
    /// 编辑器通用工具：Actor 查找、JSON 参数解析、反射赋值、工作区路径解析。
    /// 除 <see cref="ResolveWorkspacePath"/> 与参数解析外，其余方法均假设已在主线程执行。
    /// </summary>
    public static class EditorUtils
    {
        /// <summary>Flax 工程目录（含 Content / Source / Logs），已规范化分隔符与末尾符。</summary>
        /// <remarks>
        /// <c>Globals.ProjectFolder</c> 的分隔符是本机混用的（如 <c>C:\Works/.../HundunWorld</c>），
        /// 直接做前缀比较会全部失败，因此一律先过 <see cref="Path.GetFullPath"/>。
        /// </remarks>
        public static string ProjectRoot => Path.GetFullPath(Globals.ProjectFolder);

        /// <summary>
        /// 工作区根：Flax 工程的上一级目录（本仓库根，设计稿与本机分析产物都在这里）。
        /// </summary>
        public static string WorkspaceRoot
        {
            get
            {
                var root = ProjectRoot;
                var parent = Directory.GetParent(root);
                return parent != null ? parent.FullName : root;
            }
        }

        /// <summary>
        /// 解析「工程内相对路径 / 工作区相对路径 / 绝对路径」为绝对路径，并约束结果落在
        /// <see cref="ProjectRoot"/> 或 <see cref="WorkspaceRoot"/> 之内。
        /// </summary>
        /// <remarks>
        /// 先按工程目录解析，失败再回退工作区根：设计稿（<c>hundun-yy-ui/</c>）与临时分析产物
        /// （<c>.compile-temp/</c>、<c>.dbg/</c>）都位于工程目录之外、工作区之内。
        /// </remarks>
        public static string ResolveWorkspacePath(string path, bool mustExist = false)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("缺少 path 参数");

            var isRooted = Path.IsPathRooted(path);
            var full = isRooted
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(ProjectRoot, path));

            if (!IsInside(full, ProjectRoot))
            {
                var workspace = WorkspaceRoot;
                var candidate = isRooted ? full : Path.GetFullPath(Path.Combine(workspace, path));
                if (!IsInside(candidate, workspace))
                    throw new UnauthorizedAccessException("路径越出工作区范围: " + path);
                full = candidate;
            }

            if (mustExist && !File.Exists(full) && !Directory.Exists(full))
                throw new ArgumentException("路径不存在: " + path);

            return full;
        }

        /// <summary>把绝对路径转为「工程根相对」或「工作区根相对」的展示用路径（正斜杠）。</summary>
        public static string ToDisplayPath(string fullPath)
        {
            var project = ProjectRoot;
            if (IsInside(fullPath, project))
                return Path.GetRelativePath(project, fullPath).Replace('\\', '/');

            var workspace = WorkspaceRoot;
            if (IsInside(fullPath, workspace))
                return Path.GetRelativePath(workspace, fullPath).Replace('\\', '/');

            return fullPath.Replace('\\', '/');
        }

        /// <summary>判断路径是否等于 root 或位于 root 之下（已规范化的两侧）。</summary>
        public static bool IsInside(string fullPath, string root)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(root))
                return false;
            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                return true;
            return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
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

        /// <summary>读取字符串数组参数，同时接受单个字符串（自动包装为单元素表）。</summary>
        public static List<string> GetStringArray(JsonElement obj, string name)
        {
            var list = new List<string>();
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v))
                return list;

            if (v.ValueKind == JsonValueKind.String)
            {
                var single = v.GetString();
                if (!string.IsNullOrEmpty(single))
                    list.Add(single);
                return list;
            }

            if (v.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in v.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                        list.Add(item.GetString());
                }
            }

            return list;
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
            if (targetType == typeof(LocalizedString))
            {
                // Flax UI 控件的 Text 等属性为 LocalizedString（支持字符串隐式转换）
                return (LocalizedString)(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText());
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
