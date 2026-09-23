using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// UI 设计自动化工具：UICanvas / UIControl 搭建、控件属性与布局、控件类型发现、
    /// UI 树探查（含 PIE 运行时控件）、设计 Token（InkWashTheme / UIStyleTokens）查询与应用。
    /// 对齐项目 UI 规范：独立命名的 UICanvas（如 InkWashUICanvas）+ 设计 Token，禁止硬编码色值。
    /// </summary>
    public static class UiDesignTools
    {
        /// <summary>常用控件类型（ui_control_types 可查询全部引擎控件）。</summary>
        private static readonly string[] PreferredControlTypes =
        {
            "Button", "Label", "Image", "Panel", "ContainerControl",
            "VerticalPanel", "HorizontalPanel", "UniformGridPanel", "GridPanel", "DockPanel",
            "CheckBox", "ProgressBar", "Slider", "TextBox", "ListBox", "DropPanel"
        };

        /// <summary>项目设计 Token 主题（短名 → 完整类型名）。</summary>
        private static readonly Dictionary<string, string> TokenThemes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "InkWashTheme", "HundunWorld.Game.UI.StyleSystem.InkWashTheme" },
            { "ChineseClassicalTheme", "HundunWorld.Game.UI.StyleSystem.ChineseClassicalTheme" },
            { "KungfuTheme", "HundunWorld.Game.UI.StyleSystem.KungfuTheme" },
            { "UIStyleTokens", "HundunWorld.Game.UI.UIStyleTokens" },
        };

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_canvas_create",
                Description = "创建或查找 UICanvas（缺省名 InkWashUICanvas，遵循项目独立画布约定）。renderMode 可选 ScreenSpace(默认)/WorldSpace/GPUTexture。同名画布已存在时直接复用（reuse=false 可强制新建）。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"renderMode\":{\"type\":\"string\"},\"reuse\":{\"type\":\"boolean\"}}}",
                Undoable = true,
                Execute = UiCanvasCreateAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_control_create",
                Description = "在 UICanvas（或父 UIControl 容器）下创建 UI 控件。type 为控件类型名（Button/Label/Image/Panel/VerticalPanel/UniformGridPanel/CheckBox/ProgressBar/Slider/TextBox 等，可用 ui_control_types 查询）。支持 bounds{x,y,width,height}、anchorPreset 锚点与 properties 属性字典。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\",\"description\":\"控件类型名，如 Button/Label/Image/Panel\"},\"name\":{\"type\":\"string\"},\"parent\":{\"type\":\"string\",\"description\":\"父级 UICanvas 或 UIControl 名称/Guid，缺省用 InkWashUICanvas（无则取场景第一个 UICanvas）\"},\"order\":{\"type\":\"integer\",\"description\":\"在父级中的排序位，缺省追加\"},\"bounds\":{\"type\":\"object\",\"description\":\"{x,y,width,height}\"},\"anchorPreset\":{\"type\":\"string\",\"description\":\"锚点预设，如 TopLeft/Center/Stretch/BottomRight\"},\"properties\":{\"type\":\"object\",\"description\":\"控件属性键值对（反射写入，如 {\\\"Text\\\":\\\"开始\\\",\\\"BackgroundColor\\\":\\\"#C8A858\\\"}）\"}},\"required\":[\"type\"]}",
                Undoable = true,
                Execute = UiControlCreateAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_control_set",
                Description = "设置 UIControl 控件的属性/布局：properties 属性字典、bounds{x,y,width,height}、anchorPreset 锚点。三者可任选。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"UIControl Actor 名称或 Guid\"},\"properties\":{\"type\":\"object\"},\"bounds\":{\"type\":\"object\"},\"anchorPreset\":{\"type\":\"string\"}},\"required\":[\"target\"]}",
                Undoable = true,
                Execute = UiControlSetAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_control_types",
                Description = "列出引擎全部可用 UI 控件类型（名称/是否容器/是否推荐），或传入 type 查询某控件类型的可写属性清单，供选择与属性探查。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\",\"description\":\"可选，指定控件类型名则返回其可写属性清单\"}}}",
                Execute = UiControlTypesAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_tree",
                Description = "输出 UICanvas 的 UI 层级树：UIControl Actor 树 + canvas.GUI 运行时控件树（含代码在运行时添加的纯运行时控件，PIE 中尤其实用）。含控件类型、bounds、可见性、Text/颜色等关键属性。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"canvas\":{\"type\":\"string\",\"description\":\"可选，指定 UICanvas 名称/Guid，缺省输出全部\"},\"maxDepth\":{\"type\":\"integer\",\"description\":\"最大深度，默认6\"},\"includeRuntime\":{\"type\":\"boolean\",\"description\":\"是否包含 canvas.GUI 运行时控件树，默认true\"}}}",
                Execute = UiTreeAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_token_list",
                Description = "枚举项目设计 Token（InkWashTheme / UIStyleTokens / ChineseClassicalTheme / KungfuTheme）的全部 Token 名称与值，支持按主题与类型（color/number/string）过滤。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"theme\":{\"type\":\"string\",\"description\":\"可选：InkWashTheme/UIStyleTokens/ChineseClassicalTheme/KungfuTheme，缺省全部\"},\"kind\":{\"type\":\"string\",\"description\":\"可选：color/number/string/asset\"}}}",
                Execute = UiTokenListAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_token_apply",
                Description = "把设计 Token 应用到控件属性（如 token=BgInk + property=BackgroundColor），对齐项目「禁止硬编码色值」规范。theme 可选，缺省按 InkWashTheme→UIStyleTokens→其他主题顺序查找。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"UIControl Actor 名称或 Guid\"},\"property\":{\"type\":\"string\",\"description\":\"控件属性名，如 BackgroundColor/TextColor/Color\"},\"token\":{\"type\":\"string\",\"description\":\"Token 字段名，如 BgInk/GoldPrimary\"},\"theme\":{\"type\":\"string\",\"description\":\"可选主题名\"}},\"required\":[\"target\",\"property\",\"token\"]}",
                Undoable = true,
                Execute = UiTokenApplyAsync
            });
        }

        // ==================== Handlers ====================

        private static async Task<object> UiCanvasCreateAsync(JsonElement args)
        {
            var name = EditorUtils.GetString(args, "name", "InkWashUICanvas");
            var renderModeStr = EditorUtils.GetString(args, "renderMode", "ScreenSpace");
            var reuse = EditorUtils.GetBool(args, "reuse", true);

            bool created = false;
            var result = await MainThread.InvokeAsync<object>(() =>
            {
                if (reuse)
                {
                    var existing = FindCanvas(name);
                    if (existing != null)
                    {
                        return new
                        {
                            status = "reused",
                            name = existing.Name,
                            id = existing.ID.ToString(),
                            created = false,
                            renderMode = existing.RenderMode.ToString()
                        };
                    }
                }

                var editor = FlaxEditor.Editor.Instance;
                var canvas = Actor.New<UICanvas>();
                canvas.Name = name;
                canvas.RenderMode = ParseRenderMode(renderModeStr);
                editor.SceneEditing.Spawn(canvas, EditorUtils.RequireScene(), -1, false);
                created = true;

                return new
                {
                    status = "created",
                    name = canvas.Name,
                    id = canvas.ID.ToString(),
                    created = true,
                    renderMode = canvas.RenderMode.ToString()
                };
            });

            if (created)
                ToolRegistry.OnUndoableAction();
            return result;
        }

        private static async Task<object> UiControlCreateAsync(JsonElement args)
        {
            var typeStr = EditorUtils.GetString(args, "type");
            if (string.IsNullOrEmpty(typeStr))
                throw new ArgumentException("缺少 type 参数（可用 ui_control_types 查询全部控件类型）");

            var controlType = ResolveControlType(typeStr);
            var nameReq = EditorUtils.GetString(args, "name");
            var parentRef = EditorUtils.GetString(args, "parent");
            var anchorStr = EditorUtils.GetString(args, "anchorPreset");
            var order = EditorUtils.GetInt(args, "order", -1);
            var hasBounds = args.TryGetProperty("bounds", out var boundsEl) && boundsEl.ValueKind == JsonValueKind.Object;
            var props = args.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;

            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;

                // 定位父级（UICanvas 或 UIControl）
                Actor parentActor;
                if (!string.IsNullOrEmpty(parentRef))
                {
                    parentActor = EditorUtils.FindActor(parentRef);
                    if (parentActor == null)
                        throw new ArgumentException("父级不存在: " + parentRef);
                }
                else
                {
                    var defaultCanvas = FindCanvas("InkWashUICanvas") ?? FindCanvas(null);
                    if (defaultCanvas == null)
                        throw new InvalidOperationException("场景中没有 UICanvas，请先调用 ui_canvas_create");
                    parentActor = defaultCanvas;
                }

                var parentUic = parentActor as UIControl;
                if (parentUic != null)
                {
                    if (!(parentUic.Control is ContainerControl))
                        throw new ArgumentException("父控件不是容器类型，无法承载子控件: " + parentActor.Name +
                            " (" + (parentUic.Control?.GetType().Name ?? "未绑定控件") +
                            ")，请使用 Panel/VerticalPanel/ContainerControl 等容器");
                }
                else if (!(parentActor is UICanvas))
                {
                    throw new ArgumentException("parent 必须是 UICanvas 或 UIControl 类型的 Actor，当前为 " +
                        parentActor.GetType().Name + ": " + parentActor.Name);
                }

                var actor = Actor.New<UIControl>();
                actor.Name = string.IsNullOrEmpty(nameReq) ? MakeUniqueName(typeStr + "_AI") : nameReq;

                // 先 Spawn（走编辑器流程：Undo 记录 + 场景树刷新），再绑定 Control（父容器联动生效）
                editor.SceneEditing.Spawn(actor, parentActor, order, false);
                actor.Control = (Control)Activator.CreateInstance(controlType);

                var changed = new List<object>();
                try
                {
                    if (anchorStr != null)
                        ApplyAnchorPreset(actor.Control, anchorStr, changed, null);
                    if (hasBounds)
                        ApplyBounds(actor.Control, boundsEl, changed, null);
                    if (props.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in props.EnumerateObject())
                            ApplyControlProperty(actor.Control, prop.Name, prop.Value, changed, null);
                    }
                }
                catch (Exception ex)
                {
                    // 控件已创建（可用 ui_control_set 补齐属性或 actor_delete 清理）
                    throw new InvalidOperationException("控件已创建 (" + actor.Name + "，id=" + actor.ID + ")，但参数应用失败: " + ex.Message);
                }

                var control = actor.Control;
                return new
                {
                    status = "created",
                    actor = actor.Name,
                    id = actor.ID.ToString(),
                    controlType = control.GetType().Name,
                    parent = parentActor.Name,
                    bounds = FmtBounds(control.Bounds),
                    changed
                };
            });

            ToolRegistry.OnUndoableAction();
            return result;
        }

        private static Task<object> UiControlSetAsync(JsonElement args)
        {
            var targetRef = EditorUtils.GetString(args, "target");
            if (string.IsNullOrEmpty(targetRef))
                throw new ArgumentException("缺少 target 参数");
            var anchorStr = EditorUtils.GetString(args, "anchorPreset");
            var hasBounds = args.TryGetProperty("bounds", out var boundsEl) && boundsEl.ValueKind == JsonValueKind.Object;
            var props = args.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;

            if (!hasBounds && anchorStr == null && props.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("至少提供 properties / bounds / anchorPreset 之一");

            return MainThread.InvokeAsync<object>(() =>
            {
                var actor = EditorUtils.FindActor(targetRef) as UIControl;
                if (actor == null)
                    throw new ArgumentException("UIControl 不存在: " + targetRef + "（ui_tree 可查看控件层级）");
                var control = actor.Control;
                if (control == null)
                    throw new InvalidOperationException("该 UIControl 未绑定控件: " + actor.Name);

                var changed = new List<object>();
                var restorers = new List<Action>();

                if (anchorStr != null)
                    ApplyAnchorPreset(control, anchorStr, changed, restorers);
                if (hasBounds)
                    ApplyBounds(control, boundsEl, changed, restorers);
                if (props.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in props.EnumerateObject())
                        ApplyControlProperty(control, prop.Name, prop.Value, changed, restorers);
                }

                AgentUndo.Record("AI: 设置控件属性 (" + actor.Name + ")", () =>
                {
                    foreach (var restore in restorers)
                        restore();
                }, null);

                return new
                {
                    status = "set",
                    actor = actor.Name,
                    controlType = control.GetType().Name,
                    changed
                };
            });
        }

        private static Task<object> UiControlTypesAsync(JsonElement args)
        {
            var typeName = EditorUtils.GetString(args, "type");

            return Task.FromResult<object>(RunIo(() =>
            {
                if (!string.IsNullOrEmpty(typeName))
                {
                    var t = ResolveControlType(typeName);
                    var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(x => x.CanWrite && x.GetIndexParameters().Length == 0)
                        .Select(x => new { name = x.Name, type = x.PropertyType.Name })
                        .OrderBy(x => x.name)
                        .ToList();
                    return new
                    {
                        type = t.Name,
                        fullName = t.FullName,
                        isContainer = typeof(ContainerControl).IsAssignableFrom(t),
                        writablePropertyCount = props.Count,
                        writableProperties = props
                    };
                }

                var asm = typeof(Control).Assembly;
                var types = new List<Type>();
                foreach (var t in asm.GetTypes())
                {
                    try
                    {
                        if (t.IsAbstract || !t.IsPublic || t.Namespace == null || !t.Namespace.StartsWith("FlaxEngine"))
                            continue;
                        if (!typeof(Control).IsAssignableFrom(t))
                            continue;
                        types.Add(t);
                    }
                    catch
                    {
                        // 忽略反射异常类型
                    }
                }

                var list = types.OrderBy(t => t.Name).Select(t => new
                {
                    name = t.Name,
                    fullName = t.FullName,
                    isContainer = typeof(ContainerControl).IsAssignableFrom(t),
                    recommended = PreferredControlTypes.Contains(t.Name, StringComparer.OrdinalIgnoreCase)
                }).ToList();
                return new { count = list.Count, types = list };
            }));
        }

        private static Task<object> UiTreeAsync(JsonElement args)
        {
            var canvasRef = EditorUtils.GetString(args, "canvas");
            var maxDepth = Math.Clamp(EditorUtils.GetInt(args, "maxDepth", 6), 1, 12);
            var includeRuntime = EditorUtils.GetBool(args, "includeRuntime", true);

            return MainThread.InvokeAsync<object>(() =>
            {
                var canvases = new List<UICanvas>();
                foreach (var scene in Level.Scenes)
                {
                    if (scene != null)
                        CollectCanvases(scene, canvases);
                }

                if (!string.IsNullOrEmpty(canvasRef))
                {
                    canvases = canvases.Where(c =>
                        c.Name.Equals(canvasRef, StringComparison.OrdinalIgnoreCase) ||
                        c.ID.ToString() == canvasRef).ToList();
                    if (canvases.Count == 0)
                        throw new ArgumentException("未找到 UICanvas: " + canvasRef + "（可先 ui_canvas_create）");
                }

                var budget = new[] { 500 };
                var output = new List<object>();

                foreach (var canvas in canvases)
                {
                    var actorMap = new Dictionary<Control, string>();
                    var actorTree = BuildUiActorNode(canvas, 0, maxDepth, budget, actorMap);

                    object runtimeTree = null;
                    if (includeRuntime && canvas.GUI != null)
                        runtimeTree = BuildRuntimeNode(canvas.GUI, 0, maxDepth, budget, actorMap);

                    output.Add(new
                    {
                        name = canvas.Name,
                        id = canvas.ID.ToString(),
                        renderMode = canvas.RenderMode.ToString(),
                        actorTree,
                        runtimeTree
                    });
                }

                return new
                {
                    isPlayMode = Engine.IsPlayMode,
                    canvasCount = output.Count,
                    truncated = budget[0] <= 0,
                    canvases = output,
                    note = "runtimeTree 为 canvas.GUI 根下控件（含代码在运行时直接添加的纯运行时控件，PIE 中可见）；带 actor 字段的节点由 UIControl Actor 承载"
                };
            });
        }

        private static Task<object> UiTokenListAsync(JsonElement args)
        {
            var themeFilter = EditorUtils.GetString(args, "theme");
            var kindFilter = EditorUtils.GetString(args, "kind");

            return MainThread.InvokeAsync<object>(() =>
            {
                var themes = new List<object>();
                var matchedTheme = false;

                foreach (var kv in TokenThemes)
                {
                    if (!string.IsNullOrEmpty(themeFilter) && !kv.Key.Equals(themeFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    matchedTheme = true;

                    var type = EditorUtils.FindType(kv.Value);
                    if (type == null)
                    {
                        themes.Add(new
                        {
                            theme = kv.Key,
                            fullName = kv.Value,
                            found = false,
                            note = "类型未找到（游戏脚本可能尚未编译或类名已变更）"
                        });
                        continue;
                    }

                    var tokens = new List<object>();
                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                    {
                        object value;
                        try { value = field.GetValue(null); } catch { continue; }
                        if (value == null) continue;

                        var kind = ClassifyToken(value);
                        if (!string.IsNullOrEmpty(kindFilter) && !kind.Equals(kindFilter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        tokens.Add(new { name = field.Name, kind, value = FormatTokenValue(value) });
                    }

                    themes.Add(new
                    {
                        theme = kv.Key,
                        fullName = type.FullName,
                        found = true,
                        count = tokens.Count,
                        tokens
                    });
                }

                if (!matchedTheme)
                    throw new ArgumentException("未知主题: " + themeFilter + "（可用: " + string.Join("/", TokenThemes.Keys) + "）");

                return new { themes };
            });
        }

        private static Task<object> UiTokenApplyAsync(JsonElement args)
        {
            var targetRef = EditorUtils.GetString(args, "target");
            var propName = EditorUtils.GetString(args, "property");
            var tokenName = EditorUtils.GetString(args, "token");
            var themeName = EditorUtils.GetString(args, "theme");

            if (string.IsNullOrEmpty(targetRef) || string.IsNullOrEmpty(propName) || string.IsNullOrEmpty(tokenName))
                throw new ArgumentException("缺少 target / property / token 参数");

            return MainThread.InvokeAsync<object>(() =>
            {
                var actor = EditorUtils.FindActor(targetRef) as UIControl;
                if (actor == null)
                    throw new ArgumentException("UIControl 不存在: " + targetRef);
                var control = actor.Control;
                if (control == null)
                    throw new InvalidOperationException("该 UIControl 未绑定控件: " + actor.Name);

                // 查找 Token
                FieldInfo field = null;
                var resolvedTheme = (string)null;
                foreach (var kv in TokenThemes)
                {
                    if (!string.IsNullOrEmpty(themeName) && !kv.Key.Equals(themeName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var type = EditorUtils.FindType(kv.Value);
                    if (type == null) continue;
                    var f = type.GetField(tokenName, BindingFlags.Public | BindingFlags.Static);
                    if (f != null)
                    {
                        field = f;
                        resolvedTheme = kv.Key;
                        break;
                    }
                }

                if (field == null)
                    throw new ArgumentException("未找到 Token: " + tokenName +
                        (string.IsNullOrEmpty(themeName) ? "" : "（主题 " + themeName + "）") + "，可用 ui_token_list 查询");

                var tokenValue = field.GetValue(null);

                var prop = control.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite)
                    throw new ArgumentException("控件 " + control.GetType().Name + " 上不存在可写属性 '" + propName +
                        "'。可写属性示例: " + DescribeWritableProps(control.GetType()));

                var converted = CoerceTokenValue(tokenValue, prop.PropertyType);
                if (converted == null)
                    throw new ArgumentException("Token 类型不兼容: " + tokenValue.GetType().Name +
                        " → " + prop.PropertyType.Name);

                var oldValue = prop.GetValue(control);
                prop.SetValue(control, converted);

                AgentUndo.Record("AI: 应用 Token " + tokenName + " → " + propName + " (" + actor.Name + ")", () =>
                {
                    try { prop.SetValue(control, oldValue); } catch { }
                }, null);

                return new
                {
                    status = "applied",
                    actor = actor.Name,
                    controlType = control.GetType().Name,
                    property = propName,
                    token = tokenName,
                    theme = resolvedTheme,
                    value = FormatTokenValue(converted)
                };
            });
        }

        // ==================== 内部辅助 ====================

        internal static UICanvas FindCanvas(string name)
        {
            foreach (var scene in Level.Scenes)
            {
                if (scene == null) continue;
                var found = FindCanvasInTree(scene, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static UICanvas FindCanvasInTree(Actor root, string name)
        {
            if (root is UICanvas canvas && !(root is Scene))
            {
                if (string.IsNullOrEmpty(name) || canvas.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return canvas;
            }
            foreach (var child in root.Children)
            {
                var found = FindCanvasInTree(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static void CollectCanvases(Actor root, List<UICanvas> output)
        {
            if (root is UICanvas canvas && !(root is Scene))
                output.Add(canvas);
            foreach (var child in root.Children)
                CollectCanvases(child, output);
        }

        private static CanvasRenderMode ParseRenderMode(string mode)
        {
            switch ((mode ?? "").ToLowerInvariant())
            {
                case "screenspace":
                case "screenspaceoverlay":
                case "screenspacecamera":
                    return CanvasRenderMode.ScreenSpace;
                case "worldspace":
                    return CanvasRenderMode.WorldSpace;
                case "gputexture":
                    return CanvasRenderMode.GPUTexture;
                default:
                    throw new ArgumentException("无效 renderMode: " + mode + "（可用: ScreenSpace/WorldSpace/GPUTexture）");
            }
        }

        internal static Type ResolveControlType(string name)
        {
            var asm = typeof(Control).Assembly;

            var t = asm.GetType("FlaxEngine.GUI." + name, false);
            if (t != null && !t.IsAbstract && typeof(Control).IsAssignableFrom(t))
                return t;

            foreach (var candidate in asm.GetTypes())
            {
                try
                {
                    if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                        candidate.IsPublic && !candidate.IsAbstract &&
                        typeof(Control).IsAssignableFrom(candidate))
                        return candidate;
                }
                catch
                {
                    // 忽略反射异常类型
                }
            }

            // 项目脚本程序集中的自定义控件（如 InkButton/RoundedPanel 等 Game.CSharp 类型）；
            // IsTypeFromGameScripts 过滤热重载后残留的旧 AssemblyLoadContext 类型
            Type projectFallback = null;
            foreach (var projectAsm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (projectAsm == asm)
                    continue;
                try
                {
                    foreach (var candidate in projectAsm.GetTypes())
                    {
                        if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                            candidate.IsPublic && !candidate.IsAbstract &&
                            typeof(Control).IsAssignableFrom(candidate))
                        {
                            if (Scripting.IsTypeFromGameScripts(candidate))
                                return candidate;
                            projectFallback ??= candidate;
                        }
                    }
                }
                catch
                {
                    // 某些动态程序集枚举类型会抛异常
                }
            }
            if (projectFallback != null)
                return projectFallback;

            throw new ArgumentException("未知控件类型: " + name + "（可用 ui_control_types 查询全部）");
        }

        internal static string MakeUniqueName(string baseName)
        {
            if (!ActorNameExists(baseName))
                return baseName;

            for (var i = 2; i < 1000; i++)
            {
                var candidate = baseName + "_" + i;
                if (!ActorNameExists(candidate))
                    return candidate;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static bool ActorNameExists(string name)
        {
            foreach (var scene in Level.Scenes)
            {
                if (scene == null) continue;
                if (FindActorByName(scene, name) != null)
                    return true;
            }
            return false;
        }

        private static Actor FindActorByName(Actor root, string name)
        {
            if (!(root is Scene) && string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase))
                return root;
            foreach (var child in root.Children)
            {
                var found = FindActorByName(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        internal static void ApplyBounds(Control control, JsonElement bounds, List<object> changed, List<Action> restorers)
        {
            var old = control.Bounds;
            var rect = new Rectangle(
                EditorUtils.GetNum(bounds, "x", old.X),
                EditorUtils.GetNum(bounds, "y", old.Y),
                GetNumAlt(bounds, old.Width, "width", "w"),
                GetNumAlt(bounds, old.Height, "height", "h"));

            control.Bounds = rect;
            if (restorers != null)
                restorers.Add(() => { try { control.Bounds = old; } catch { } });
            changed.Add(new { property = "bounds", value = FmtBounds(rect) });
        }

        private static float GetNumAlt(JsonElement obj, float defaultValue, params string[] names)
        {
            foreach (var name in names)
            {
                if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
                    return v.GetSingle();
            }
            return defaultValue;
        }

        internal static void ApplyAnchorPreset(Control control, string anchorStr, List<object> changed, List<Action> restorers)
        {
            var prop = typeof(Control).GetProperty("AnchorPreset");
            var enumType = prop.PropertyType;

            object preset;
            try
            {
                preset = Enum.Parse(enumType, anchorStr, true);
            }
            catch
            {
                throw new ArgumentException("无效 anchorPreset: " + anchorStr +
                    "（可用: " + string.Join("/", Enum.GetNames(enumType)) + "）");
            }

            var old = prop.GetValue(control);
            prop.SetValue(control, preset);
            if (restorers != null)
                restorers.Add(() => { try { prop.SetValue(control, old); } catch { } });
            changed.Add(new { property = "anchorPreset", value = preset.ToString() });
        }

        internal static void ApplyControlProperty(Control control, string name, JsonElement value, List<object> changed, List<Action> restorers)
        {
            var prop = control.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite)
                throw new ArgumentException("控件 " + control.GetType().Name + " 上不存在可写属性 '" + name +
                    "'。可写属性示例: " + DescribeWritableProps(control.GetType()));

            var oldValue = prop.GetValue(control);
            object newValue;
            try
            {
                newValue = EditorUtils.ConvertJsonValue(prop.PropertyType, value);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("属性 " + name + " 值转换失败: " + ex.Message);
            }

            prop.SetValue(control, newValue);
            if (restorers != null)
            {
                var capturedProp = prop;
                var capturedOld = oldValue;
                restorers.Add(() => { try { capturedProp.SetValue(control, capturedOld); } catch { } });
            }
            changed.Add(new { property = name, value = SafeStr(newValue) });
        }

        private static string DescribeWritableProps(Type t)
        {
            var names = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.CanWrite && x.GetIndexParameters().Length == 0)
                .Select(x => x.Name)
                .Take(30)
                .ToList();
            return string.Join(", ", names);
        }

        private static object BuildUiActorNode(Actor actor, int depth, int maxDepth, int[] budget, Dictionary<Control, string> actorMap)
        {
            budget[0]--;
            var uic = actor as UIControl;
            var control = uic?.Control;
            if (control != null && !actorMap.ContainsKey(control))
                actorMap[control] = actor.Name;

            var node = new Dictionary<string, object>
            {
                { "name", actor.Name },
                { "id", actor.ID.ToString() },
                { "type", actor.GetType().Name },
                { "controlType", control?.GetType().Name },
                { "active", actor.IsActive }
            };

            if (control != null)
            {
                node["bounds"] = FmtBounds(control.Bounds);
                AddKeyProps(node, control);
            }

            if (depth < maxDepth && budget[0] > 0)
            {
                var children = new List<object>();
                foreach (var child in actor.Children)
                {
                    if (budget[0] <= 0) break;
                    if (child is UIControl)
                        children.Add(BuildUiActorNode(child, depth + 1, maxDepth, budget, actorMap));
                }
                node["children"] = children;
            }

            return node;
        }

        private static object BuildRuntimeNode(Control control, int depth, int maxDepth, int[] budget, Dictionary<Control, string> actorMap)
        {
            budget[0]--;
            var node = new Dictionary<string, object>
            {
                { "type", control.GetType().Name },
                { "visible", control.Visible },
                { "bounds", FmtBounds(control.Bounds) }
            };

            var name = GetPropString(control, "Name");
            if (!string.IsNullOrEmpty(name))
                node["name"] = name;

            AddKeyProps(node, control);

            if (actorMap.TryGetValue(control, out var actorName))
                node["actor"] = actorName;
            else
                node["runtimeOnly"] = true;

            if (depth < maxDepth && budget[0] > 0 && control is ContainerControl container)
            {
                var children = new List<object>();
                foreach (var child in container.Children)
                {
                    if (budget[0] <= 0) break;
                    children.Add(BuildRuntimeNode(child, depth + 1, maxDepth, budget, actorMap));
                }
                node["children"] = children;
            }

            return node;
        }

        private static void AddKeyProps(Dictionary<string, object> node, Control control)
        {
            var text = GetPropString(control, "Text");
            if (!string.IsNullOrEmpty(text))
                node["text"] = text.Length > 80 ? text.Substring(0, 80) : text;

            var bg = GetPropColor(control, "BackgroundColor");
            if (bg.HasValue)
                node["backgroundColor"] = ColorToHex(bg.Value);

            var textColor = GetPropColor(control, "TextColor") ?? GetPropColor(control, "Color");
            if (textColor.HasValue)
                node["textColor"] = ColorToHex(textColor.Value);
        }

        private static string GetPropString(object target, string name)
        {
            try
            {
                var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.CanRead && p.PropertyType == typeof(string))
                    return (string)p.GetValue(target);
            }
            catch
            {
            }
            return null;
        }

        private static Color? GetPropColor(object target, string name)
        {
            try
            {
                var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.CanRead && p.PropertyType == typeof(Color))
                    return (Color)p.GetValue(target);
            }
            catch
            {
            }
            return null;
        }

        private static string ClassifyToken(object value)
        {
            if (value is Color || value is Color32)
                return "color";
            if (value is float || value is double || value is int || value is long || value is short || value is byte)
                return "number";
            if (value is string)
                return "string";
            if (value is Asset)
                return "asset";
            return value.GetType().Name;
        }

        private static string FormatTokenValue(object value)
        {
            try
            {
                if (value is Color color)
                    return ColorToHex(color);
                if (value is Color32 c32)
                    return ColorToHex(new Color(c32.R / 255f, c32.G / 255f, c32.B / 255f, c32.A / 255f));
                if (value is float f)
                    return Math.Round(f, 3).ToString();
                if (value is double d)
                    return Math.Round(d, 3).ToString();
                if (value is Asset asset)
                    return asset.Path;
                return value.ToString();
            }
            catch
            {
                return value?.GetType().Name;
            }
        }

        private static object CoerceTokenValue(object value, Type targetType)
        {
            if (value == null)
                return null;
            if (targetType.IsInstanceOfType(value))
                return value;

            try
            {
                if (value is Color color)
                {
                    if (targetType == typeof(Color32))
                        return new Color32((byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255), (byte)(color.A * 255));
                    if (targetType == typeof(Vector4))
                        return new Vector4(color.R, color.G, color.B, color.A);
                    if (targetType == typeof(Float4))
                        return new Float4(color.R, color.G, color.B, color.A);
                }

                if (value is float || value is double || value is int)
                {
                    if (targetType == typeof(float)) return Convert.ToSingle(value);
                    if (targetType == typeof(double)) return Convert.ToDouble(value);
                    if (targetType == typeof(int)) return Convert.ToInt32(value);
                }
            }
            catch
            {
            }

            return null;
        }

        private static string ColorToHex(Color c)
        {
            var r = (int)Math.Round(Mathf.Clamp(c.R, 0f, 1f) * 255);
            var g = (int)Math.Round(Mathf.Clamp(c.G, 0f, 1f) * 255);
            var b = (int)Math.Round(Mathf.Clamp(c.B, 0f, 1f) * 255);
            var a = (int)Math.Round(Mathf.Clamp(c.A, 0f, 1f) * 255);
            return a >= 255
                ? "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2")
                : "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2") + a.ToString("X2");
        }

        private static object FmtBounds(Rectangle r)
        {
            return new { x = r.X, y = r.Y, width = r.Width, height = r.Height };
        }

        private static string SafeStr(object value)
        {
            try { return value?.ToString(); } catch { return value?.GetType().Name; }
        }

        private static object RunIo(Func<object> action)
        {
            // 纯反射/文件 IO 在调用线程执行（工具运行于服务器线程），避免占用主线程
            return action();
        }
    }
}
