using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// UI Prefab 组件库构建工具：为 Actor 挂脚本组件、写脚本字段/属性，
    /// 以及「UIControl + 控件 + Profile + EventBinder → 保存 Prefab」的一键批量构建。
    /// 支撑 Content/Prefabs/UI/ 组件库的资产生产。
    /// </summary>
    public static class UiPrefabTools
    {
        /// <summary>构建 Prefab 时使用的临时工厂画布名（不保存到场景）。</summary>
        private const string FactoryCanvasName = "PrefabFactoryCanvas";

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_script_add",
                Description = "为指定 Actor 添加脚本组件（如 UIComponentProfile 子类 / UIEventBinder），已存在同类型脚本时直接复用。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\",\"description\":\"Actor 名称或 Guid\"},\"scriptType\":{\"type\":\"string\",\"description\":\"脚本类型名（短名或全名），如 InkButtonProfile / HundunWorld.Game.UI.Prefabs.UIEventBinder\"}},\"required\":[\"actor\",\"scriptType\"]}",
                Undoable = true,
                Execute = UiScriptAddAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_script_set",
                Description = "设置 Actor 上脚本组件的序列化字段/属性（public 字段与可写属性均可，支持 List、枚举、Actor 引用、资产路径、Color/Float2）。scriptType 缺省时取 Actor 上第一个匹配脚本。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\"},\"scriptType\":{\"type\":\"string\",\"description\":\"可选，脚本类型名\"},\"properties\":{\"type\":\"object\",\"description\":\"字段/属性键值对\"}},\"required\":[\"actor\",\"properties\"]}",
                Undoable = true,
                Execute = UiScriptSetAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_prefab_build",
                Description = "一键构建 UI 组件 Prefab：在临时工厂画布下创建 UIControl Actor → 绑定控件类型并应用属性 → 挂 Profile/EventBinder 脚本并配置 → 保存为 .prefab 资产 → 清理临时 Actor。spec: {path, name, controlType, controlProperties, bounds, anchorPreset, scripts:[{type,properties}]}。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"spec\":{\"type\":\"object\"}},\"required\":[\"spec\"]}",
                Undoable = true,
                Execute = UiPrefabBuildAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_prefab_batch",
                Description = "批量构建 UI 组件 Prefab：传入 spec 数组，逐项执行 ui_prefab_build 的构建流程，返回每项的成功/失败报告。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"specs\":{\"type\":\"array\"}},\"required\":[\"specs\"]}",
                Undoable = true,
                Execute = UiPrefabBatchAsync
            });
        }

        // ==================== Handlers ====================

        private static async Task<object> UiScriptAddAsync(JsonElement args)
        {
            var actorRef = EditorUtils.GetString(args, "actor");
            var scriptType = EditorUtils.GetString(args, "scriptType");
            if (string.IsNullOrEmpty(actorRef) || string.IsNullOrEmpty(scriptType))
                throw new ArgumentException("缺少 actor / scriptType 参数");

            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var actor = ResolveActor(actorRef);
                var type = ResolveScriptType(scriptType);
                var existing = actor.GetScript(type);
                if (existing != null)
                    return new { status = "exists", script = type.Name };
                var added = actor.AddScript(type);
                if (added == null)
                    throw new InvalidOperationException("AddScript 失败: " + type.FullName);
                return new { status = "added", script = type.Name, actor = actor.Name };
            });

            ToolRegistry.OnUndoableAction();
            return result;
        }

        private static async Task<object> UiScriptSetAsync(JsonElement args)
        {
            var actorRef = EditorUtils.GetString(args, "actor");
            var scriptType = EditorUtils.GetString(args, "scriptType");
            if (!args.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("缺少 properties 对象");

            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var actor = ResolveActor(actorRef);
                var script = FindScript(actor, scriptType);
                if (script == null)
                    throw new ArgumentException("Actor 上找不到脚本: " + (scriptType ?? "<第一个脚本>") + " (" + actor.Name + ")");

                var changed = ApplyMembers(script, props);
                TryRefresh(script);
                return new { status = "updated", actor = actor.Name, script = script.GetType().Name, changed };
            });

            ToolRegistry.OnUndoableAction();
            return result;
        }

        private static async Task<object> UiPrefabBuildAsync(JsonElement args)
        {
            if (!args.TryGetProperty("spec", out var spec) || spec.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("缺少 spec 对象");

            var result = await MainThread.InvokeAsync<object>(() => BuildOne(spec));
            ToolRegistry.OnUndoableAction();
            return result;
        }

        private static async Task<object> UiPrefabBatchAsync(JsonElement args)
        {
            if (!args.TryGetProperty("specs", out var specs) || specs.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("缺少 specs 数组");

            var reports = new List<object>();
            await MainThread.InvokeAsync<object>(() =>
            {
                foreach (var spec in specs.EnumerateArray())
                {
                    var name = EditorUtils.GetString(spec, "name") ?? EditorUtils.GetString(spec, "path");
                    try
                    {
                        reports.Add(new { spec = name, result = BuildOne(spec) });
                    }
                    catch (Exception ex)
                    {
                        reports.Add(new { spec = name, error = ex.Message });
                    }
                }
                return null;
            });
            ToolRegistry.OnUndoableAction();
            return reports;
        }

        // ==================== 构建核心（主线程调用） ====================

        private static object BuildOne(JsonElement spec)
        {
            var editor = FlaxEditor.Editor.Instance;

            var relPath = EditorUtils.GetString(spec, "path");
            if (string.IsNullOrEmpty(relPath))
                throw new ArgumentException("spec.path 缺失（如 Content/Prefabs/UI/Btn_InkButton.prefab）");
            var name = EditorUtils.GetString(spec, "name");
            var controlTypeStr = EditorUtils.GetString(spec, "controlType");
            var parentRef = EditorUtils.GetString(spec, "parent");

            // 1. 定位/创建工厂画布
            UICanvas canvas;
            if (!string.IsNullOrEmpty(parentRef))
            {
                canvas = ResolveActor(parentRef) as UICanvas
                         ?? throw new ArgumentException("parent 不是 UICanvas: " + parentRef);
            }
            else
            {
                canvas = UiDesignTools.FindCanvas(FactoryCanvasName);
                if (canvas == null)
                {
                    canvas = Actor.New<UICanvas>();
                    canvas.Name = FactoryCanvasName;
                    editor.SceneEditing.Spawn(canvas, EditorUtils.RequireScene(), -1, false);
                }
            }

            // 2. 创建 UIControl Actor
            var actor = Actor.New<UIControl>();
            actor.Name = UiDesignTools.MakeUniqueName(
                !string.IsNullOrEmpty(name) ? name : (controlTypeStr ?? "UIControl") + "_Prefab");
            editor.SceneEditing.Spawn(actor, canvas, -1, false);

            try
            {
                // 3. 绑定控件并应用属性
                if (!string.IsNullOrEmpty(controlTypeStr))
                {
                    var controlType = UiDesignTools.ResolveControlType(controlTypeStr);
                    actor.Control = (Control)Activator.CreateInstance(controlType);

                    var changed = new List<object>();
                    var restorers = new List<Action>();
                    if (spec.TryGetProperty("anchorPreset", out var anchorEl) && anchorEl.ValueKind == JsonValueKind.String)
                        UiDesignTools.ApplyAnchorPreset(actor.Control, anchorEl.GetString(), changed, restorers);
                    if (spec.TryGetProperty("bounds", out var boundsEl) && boundsEl.ValueKind == JsonValueKind.Object)
                        UiDesignTools.ApplyBounds(actor.Control, boundsEl, changed, restorers);
                    if (spec.TryGetProperty("controlProperties", out var cProps) && cProps.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in cProps.EnumerateObject())
                            UiDesignTools.ApplyControlProperty(actor.Control, prop.Name, prop.Value, changed, restorers);
                    }
                }

                // 4. 挂脚本（Profile / EventBinder）并写参数
                var scriptReports = new List<object>();
                if (spec.TryGetProperty("scripts", out var scriptsEl) && scriptsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in scriptsEl.EnumerateArray())
                    {
                        var typeName = EditorUtils.GetString(s, "type");
                        if (string.IsNullOrEmpty(typeName))
                            continue;
                        var type = ResolveScriptType(typeName);
                        var instance = actor.GetScript(type) ?? actor.AddScript(type);
                        if (instance == null)
                            throw new InvalidOperationException("AddScript 失败: " + type.FullName);

                        var changed = new List<object>();
                        if (s.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                            changed = ApplyMembers(instance, props);
                        TryRefresh(instance);
                        scriptReports.Add(new { script = type.Name, changed });
                    }
                }

                // 5. 保存为 Prefab 资产（注意：CreatePrefab 只能调用一次——重试会把已转为实例的 Actor 嵌套成循环引用）
                var fullPath = Path.Combine(Globals.ProjectFolder, relPath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                PrefabManager.CreatePrefab(actor, relPath.Replace('\\', '/'), true);
                if (!File.Exists(fullPath))
                    throw new InvalidOperationException("预制体创建失败: " + fullPath);
                editor.ContentDatabase.Rebuild(true);

                return new
                {
                    status = "created",
                    prefab = relPath,
                    actor = actor.Name,
                    controlType = actor.Control?.GetType().Name,
                    scripts = scriptReports
                };
            }
            finally
            {
                // 6. 清理临时 Actor（画布保留复用，场景不保存）
                try
                {
                    var node = editor.Scene.GetActorNode(actor);
                    if (node != null)
                    {
                        editor.SceneEditing.Select(node, false);
                        editor.SceneEditing.Delete();
                    }
                }
                catch
                {
                    // 清理失败不影响 Prefab 资产
                }
            }
        }

        // ==================== 反射辅助 ====================

        private static Actor ResolveActor(string nameOrId)
        {
            var actor = EditorUtils.FindActor(nameOrId);
            if (actor == null)
                throw new ArgumentException("Actor 不存在: " + nameOrId);
            return actor;
        }

        private static Type ResolveScriptType(string typeName)
        {
            // IsTypeFromGameScripts 过滤热重载后残留的旧 AssemblyLoadContext 类型
            var type = EditorUtils.FindType(typeName);
            if (type == null || (typeof(Script).IsAssignableFrom(type) && !Scripting.IsTypeFromGameScripts(type)))
            {
                type = null;
                Type fallback = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName().Name;
                    if (name == null || !name.EndsWith(".CSharp", StringComparison.Ordinal))
                        continue;
                    try
                    {
                        foreach (var candidate in asm.GetTypes())
                        {
                            if (candidate.IsPublic && !candidate.IsAbstract &&
                                typeof(Script).IsAssignableFrom(candidate) &&
                                candidate.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (Scripting.IsTypeFromGameScripts(candidate))
                                {
                                    type = candidate;
                                    fallback = fallback ?? candidate;
                                }
                                else if (fallback == null)
                                {
                                    fallback = candidate;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // 某些动态程序集枚举类型会抛异常
                    }
                    if (type != null)
                        break;
                }
                type ??= fallback;
            }
            if (type == null)
                throw new ArgumentException("找不到脚本类型: " + typeName);
            if (!typeof(Script).IsAssignableFrom(type))
                throw new ArgumentException(typeName + " 不是 Script 子类");
            return type;
        }

        private static Script FindScript(Actor actor, string typeName)
        {
            if (!string.IsNullOrEmpty(typeName))
            {
                var type = ResolveScriptType(typeName);
                return actor.GetScript(type);
            }
            foreach (var s in actor.GetScripts(typeof(Script)))
            {
                if (s is Script script)
                    return script;
            }
            return null;
        }

        /// <summary>把 JSON 对象写入脚本的 public 字段与可写属性，返回变更清单。</summary>
        private static List<object> ApplyMembers(object target, JsonElement props)
        {
            var type = target.GetType();
            var changed = new List<object>();
            foreach (var prop in props.EnumerateObject())
            {
                object value;
                MemberInfo member = null;

                var field = type.GetField(prop.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null && !field.IsInitOnly)
                {
                    value = ConvertValue(field.FieldType, prop.Value);
                    field.SetValue(target, value);
                    member = field;
                }
                else
                {
                    var property = type.GetProperty(prop.Name,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (property == null || !property.CanWrite)
                        throw new ArgumentException(type.Name + " 上不存在可写字段/属性: " + prop.Name);
                    value = ConvertValue(property.PropertyType, prop.Value);
                    property.SetValue(target, value);
                    member = property;
                }

                changed.Add(new { member = member.Name, value = Describe(value) });
            }
            return changed;
        }

        /// <summary>Profile 提供 RefreshComponent() 时调用，使改动即时应用到控件预览。</summary>
        private static void TryRefresh(object script)
        {
            var method = script.GetType().GetMethod("RefreshComponent",
                BindingFlags.Public | BindingFlags.Instance);
            method?.Invoke(script, null);
        }

        /// <summary>JSON → 目标类型转换（在 EditorUtils 基础上扩展 List 与 Actor 引用）。</summary>
        private static object ConvertValue(Type targetType, JsonElement value)
        {
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elemType = targetType.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(targetType);
                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in value.EnumerateArray())
                        list.Add(ConvertValue(elemType, item));
                }
                return list;
            }

            if (typeof(Actor).IsAssignableFrom(targetType))
            {
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
                    return null;
                return EditorUtils.FindActor(value.GetString())
                       ?? throw new ArgumentException("找不到 Actor: " + value.GetString());
            }

            if (value.ValueKind == JsonValueKind.String && targetType == typeof(string))
                return value.GetString();

            return EditorUtils.ConvertJsonValue(targetType, value);
        }

        private static object Describe(object value)
        {
            if (value is IList list)
                return list.Count + " 项";
            return value?.ToString();
        }
    }
}
