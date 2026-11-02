using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.Scripting;
using FlaxEditor.Surface;
using FlaxEditor.Surface.Elements;
using FlaxEditor.Windows.Assets;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 通用材质图构建工具（nodeKey 扩展）：用一份**声明式图描述**把节点连进材质输出。
    ///
    /// 为什么要有它：`material_graph_add_params` 只能"参数直连某个输入"，
    /// 而头部外观需要的是**链**（采样 → 取分量 → 混合 → 输入）。
    /// 规格来自 UE 的 `M_Head_Baked`（见 docs/metahuman/ue_runtime/ue_head_material_graph.json），
    /// 但工具本身不内置任何 MetaHuman 语义——它只认下面这套通用 kind，
    /// 这样"链长什么样"是**数据**（可审、可改），不是埋在 C# 里的魔法。
    ///
    /// 节点 kind（`nodes` 数组，每项有 `key`）：
    ///   param   {param, component?}         Get Parameter；输出=按参数类型取箱（贴图取 `Color`，即**采样结果**本身）
    ///   unpack  {src, component:X|Y|Z|W}    Unpack Float4；输出=指定分量(Float)
    ///   pack3   {x,y,z}                     Pack Float3；输出 Value(Float3)
    ///   mul/add/sub/div/power {a,b}         Math 二元；输出 Result
    ///   one_minus/saturate {a}              Math 一元；输出 Result
    ///   lerp    {a,b,alpha}                 Math Lerp；输出 Result
    /// **没有 `sample`**：Flax 里贴图参数的 `Color` 输出已经是采样结果（见 `Archetypes/Parameters.cs:270-280`），
    /// 再接一个 Sample Texture 节点会让生成器在两个节点间互相递归、**崩掉编辑器**（2026-10-16 实测）。
    /// `outputs`: [{input:"Color"|"Normal"|"Roughness"|... , from:"key"}] 接到材质输出针。
    ///
    /// 复用与幂等：`param` 会复用已存在的同名 Get Parameter 节点；同 key 重复调用不会攒重复节点。
    /// 落盘：只有 `apply=true` 且有实质改动才 `window.Save()`，保存后**复核运行时参数表**。
    /// </summary>
    public static class MaterialGraphBuildTool
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "material_graph_build",
                Description = "按声明式描述在材质图里建链并接到材质输出。nodes 每项 {key,kind,...}，"
                    + "kind 支持 param/ref/unpack/pack3/mul/add/sub/div/power/one_minus/saturate/lerp；"
                    + "ref 用 param 填已存在节点下标（与 material_graph_dump 的 nodeIndex 一致）复用其输出；"
                    + "outputs 为 [{input,from}]（input 用 Color/Normal/Roughness/Mask/AO/Emissive/Metalness/Specular/Opacity 等）；"
                    + "接目标节点可用 node+pin（按标题找已存在节点）、nodeIndex+pin（按 material_graph_dump 的下标，同名同参数多份时用它）"
                    + "或 nodeKey+pin（接本次新建节点的输入针，如贴图参数的 UVs）。"
                    + "贴图参数的 Color 输出已是采样结果，不要再接 Sample Texture（会崩编辑器）。"
                    + "apply 缺省 false（只算不写，且不标脏）。落盘后回执会复核运行时参数表。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"path\":{\"type\":\"string\"},"
                    + "\"nodes\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
                    + "\"key\":{\"type\":\"string\"},\"kind\":{\"type\":\"string\"},\"param\":{\"type\":\"string\"},"
                    + "\"src\":{\"type\":\"string\"},\"a\":{\"type\":\"string\"},\"b\":{\"type\":\"string\"},\"code\":{\"type\":\"string\"},"
                    + "\"alpha\":{\"type\":\"string\"},\"x\":{\"type\":\"string\"},\"y\":{\"type\":\"string\"},"
                    + "\"z\":{\"type\":\"string\"},\"component\":{\"type\":\"string\"},\"channels\":{\"type\":\"string\"}},"
                    + "\"required\":[\"key\",\"kind\"]}},"
                    + "\"outputs\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
                    + "\"input\":{\"type\":\"string\"},\"from\":{\"type\":\"string\"},\"node\":{\"type\":\"string\"},"
                    + "\"matchParam\":{\"type\":\"string\"},\"pin\":{\"type\":\"string\"}},\"required\":[\"from\"]}},"
                    + "\"disconnect\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},"
                    + "\"apply\":{\"type\":\"boolean\"}},"
                    + "\"required\":[\"path\",\"nodes\"]}",
                Execute = BuildAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "material_graph_node_types",
                Description = "列出材质图可用节点原型（组名/标题/TypeID/输入输出针名），用于确认 UV、Length、Smoothstep 等节点能否程序化创建。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filter\":{\"type\":\"string\"},\"max\":{\"type\":\"integer\"}}}",
                Execute = ListTypesAsync
            });
        }

        private static Task<object> ListTypesAsync(JsonElement args)
        {
            string filter = (EditorUtils.GetString(args, "filter") ?? string.Empty).ToLowerInvariant();
            int max = Math.Clamp(EditorUtils.GetInt(args, "max", 400), 1, 2000);
            return MainThread.InvokeAsync<object>(() =>
            {
                var items = new List<object>();
                int groups = 0;
                foreach (GroupArchetype g in NodeFactory.DefaultGroups)
                {
                    if (g == null)
                    {
                        continue;
                    }
                    groups++;
                    foreach (NodeArchetype a in g.Archetypes)
                    {
                        if (a == null)
                        {
                            continue;
                        }
                        string title = a.Title ?? string.Empty;
                        if (filter.Length > 0 && !title.ToLowerInvariant().Contains(filter))
                        {
                            continue;
                        }
                        var ins = new List<string>();
                        var outs = new List<string>();
                        var elements = new List<object>();
                        if (a.Elements != null)
                        {
                            foreach (var el in a.Elements)
                            {
                                bool isIn = el.Type == NodeElementType.Input;
                                bool isOut = el.Type == NodeElementType.Output;
                                if (isIn)
                                {
                                    ins.Add(el.Text);
                                }
                                else if (isOut)
                                {
                                    outs.Add(el.Text);
                                }
                                if (isIn || isOut)
                                {
                                    elements.Add(new { boxId = el.BoxID, text = el.Text, output = isOut });
                                }
                            }
                        }
                        items.Add(new { group = g.Name, title, typeId = a.TypeID, inputs = ins, outputs = outs, elements });
                        if (items.Count >= max)
                        {
                            break;
                        }
                    }
                    if (items.Count >= max)
                    {
                        break;
                    }
                }
                return new { groups, count = items.Count, items };
            });
        }

        private sealed class Spec
        {
            public string Key;
            public string Kind;
            public string Param;
            public string Src;
            public string A;
            public string B;
            public string Alpha;
            public string X;
            public string Y;
            public string Z;
            public string Component;
            public string Channels;
            public string Code;
            public int? Components;
            public double? Value;
        }

        private sealed class Slot
        {
            public SurfaceNode Node;
            public Box Out;
        }

        /// <summary>
        /// 输出接线：默认接材质输入（input/from）；也支持接**已存在节点**的指定针
        /// （node=节点标题，matchParam=该节点某个输入当前连着的参数名，pin=要接的针名）。
        /// 用途：把 CM 混合结果插回过妆前的 Pack Float3，而不是替换 Color 输出。
        /// </summary>
        private sealed class OutputSpec
        {
            public string Input;
            public string From;
            public string Node;
            public string MatchParam;
            public string Pin;
            /// <summary>本次新建节点的 key：把 from 接到该节点的输入针（如贴图参数的 UVs）。</summary>
            public string NodeKey;
            /// <summary>已存在节点的下标（与 material_graph_dump 的 nodeIndex 一致）：同名同参数节点有多份时用它消歧。</summary>
            public int? NodeIndex;
            /// <summary>来源取自已存在节点的指定输出针（与 from 二选一），用于直接改接老链、不再复制节点。</summary>
            public int? FromNodeIndex;
            public string FromPin;
        }

        private static string Str(JsonElement obj, string name)
        {
            JsonElement v;
            if (obj.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }

        private static int? IntOrNull(JsonElement obj, string name)
        {
            JsonElement v;
            if (obj.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
            return null;
        }

        private static double? DblOrNull(JsonElement obj, string name)
        {
            JsonElement v;
            if (obj.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Number)
                return v.GetDouble();
            return null;
        }

        private static async Task<object> BuildAsync(JsonElement args)
        {
            var pathArg = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(pathArg))
                throw new ArgumentException("缺少 path");

            JsonElement nodesEl;
            if (!args.TryGetProperty("nodes", out nodesEl) || nodesEl.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("缺少 nodes 数组");

            var specs = new List<Spec>();
            foreach (JsonElement n in nodesEl.EnumerateArray())
            {
                var s = new Spec
                {
                    Key = Str(n, "key"),
                    Kind = Str(n, "kind"),
                    Param = Str(n, "param"),
                    Src = Str(n, "src"),
                    A = Str(n, "a"),
                    B = Str(n, "b"),
                    Alpha = Str(n, "alpha"),
                    X = Str(n, "x"),
                    Y = Str(n, "y"),
                    Z = Str(n, "z"),
                    Component = Str(n, "component"),
                    Channels = Str(n, "channels"),
                    Code = Str(n, "code"),
                    Components = IntOrNull(n, "components"),
                    Value = DblOrNull(n, "value"),
                };
                if (string.IsNullOrEmpty(s.Key) || string.IsNullOrEmpty(s.Kind))
                    throw new ArgumentException("nodes 里每项都要有 key 与 kind");
                if (specs.Any(o => o.Key == s.Key))
                    throw new ArgumentException("nodes 里 key 重复: " + s.Key);
                specs.Add(s);
            }

            var outputs = new List<OutputSpec>();
            JsonElement outsEl;
            if (args.TryGetProperty("outputs", out outsEl) && outsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement o in outsEl.EnumerateArray())
                {
                    var spec = new OutputSpec
                    {
                        Input = Str(o, "input"),
                        From = Str(o, "from"),
                        Node = Str(o, "node"),
                        MatchParam = Str(o, "matchParam"),
                        Pin = Str(o, "pin"),
                        NodeKey = Str(o, "nodeKey"),
                        NodeIndex = IntOrNull(o, "nodeIndex"),
                        FromNodeIndex = IntOrNull(o, "fromNodeIndex"),
                        FromPin = Str(o, "fromPin"),
                    };
                    if ((!string.IsNullOrWhiteSpace(spec.From) || spec.FromNodeIndex.HasValue) &&
                        (!string.IsNullOrWhiteSpace(spec.Input) ||
                         !string.IsNullOrWhiteSpace(spec.Node) ||
                         !string.IsNullOrWhiteSpace(spec.NodeKey) ||
                         spec.NodeIndex.HasValue))
                        outputs.Add(spec);
                }
            }

            bool apply = args.TryGetProperty("apply", out var ap) && ap.ValueKind == JsonValueKind.True;

            var disconnect = new List<string>();
            JsonElement discEl;
            if (args.TryGetProperty("disconnect", out discEl) && discEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement d in discEl.EnumerateArray())
                {
                    if (d.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d.GetString()))
                        disconnect.Add(d.GetString());
                }
            }

            var normalized = pathArg.Replace('\\', '/');
            MaterialWindow window = null;
            var slotList = new List<KeyValuePair<string, Slot>>();
            var spawned = new List<string>();
            var reused = new List<string>();
            var wired = new List<string>();
            // 诊断：材质根 Normal(8) 输入在“接线后 / 保存后”各有多少来源。
            // 用来区分“没接上”“接上了但保存没提交”“保存提交了但序列化丢连接”三种情况。
            int rootNormalAfterWire = -1;
            int rootNormalAfterSave = -1;
            bool editedAfterSave = false;
            // 着色模型会决定根输入是否启用：CustomLit/Unlit 下 Normal 不参与编译，
            // 接上去也像素不变。回执带出来，避免再把“没效果”误判成“没接上”。
            bool rootNormalActive = false;
            bool rootColorActive = false;
            var errors = new List<string>();
            var warnings = new List<string>();
            int waited = 0;

            // ① 开窗并等 surface 就绪（Surface.Enabled 由 OnSurfaceEditingStart 置真）
            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var assetItem = editor.ContentDatabase.Find(pathArg) as AssetItem
                                ?? editor.ContentDatabase.Find(normalized) as AssetItem;
                if (assetItem == null)
                {
                    var loaded = Content.Load<MaterialBase>(normalized, 10000.0);
                    if (loaded == null || !loaded.IsLoaded)
                        throw new ArgumentException("材质加载失败: " + pathArg);
                    assetItem = editor.ContentDatabase.Find(loaded.ID) as AssetItem;
                    if (assetItem == null)
                    {
                        editor.ContentDatabase.Rebuild(true);
                        assetItem = editor.ContentDatabase.Find(loaded.ID) as AssetItem;
                    }
                }
                if (assetItem == null)
                    throw new ArgumentException("找不到 AssetItem: " + pathArg);

                Content.Load<MaterialBase>(normalized, 10000.0);
                window = new MaterialWindow(editor, assetItem);
                editor.Windows.Open(window);
            });

            while (waited < 240)
            {
                if (await MainThread.InvokeAsync(() => window != null && window.Surface != null && window.Surface.Enabled))
                    break;
                waited++;
            }
            if (!await MainThread.InvokeAsync(() => window != null && window.Surface != null && window.Surface.Enabled))
                throw new InvalidOperationException("材质窗口 surface 未就绪（" + waited + " 帧）⇒ 不建图、不保存");

            // ② 建节点 + 连线（同一 tick）
            await MainThread.InvokeAsync(() =>
            {
                var surface = window.Surface;
                var ctx = surface.RootContext;
                var byKey = new Dictionary<string, Slot>();

                // **dry-run 只做静态校验，一个节点也不建**。
                // 教训（2026-10-16，两次）：建节点/改参数都会把 surface 标脏，编辑器随后自己存盘，
                // 于是"只算不写"的 dry-run 会把半成品（游离节点、43 个参数）写进资产。
                // 所以这里的实现是"绝不落盘"：校验 archetype/端口/参数/来源 key/材质输入名是否都成立，
                // 但不碰图。dry-run 回执里 plannedWired 是**打算**接的线，不表示已经接上。
                if (!apply)
                {
                    ValidateSpec(surface, specs, outputs);
                    warnings.Add("dry-run：只做静态校验（原型/端口/参数/来源 key/材质输入名），未建任何节点、未连线");
                    return;
                }

                GroupArchetype paramGroup = FindGroup("Parameters");
                NodeArchetype paramArch = paramGroup == null ? null : FindArch(paramGroup, "Get Parameter", 1);
                if (paramArch == null)
                    throw new InvalidOperationException("找不到 Parameters 组的 Get Parameter 原型");

                int idx = 0;
                foreach (var s in specs)
                {
                    var pos = new Float2(260f * (idx % 6), 200f * (idx / 6));
                    idx++;
                    SurfaceNode node = null;
                    Box outBox = null;

                    switch (s.Kind)
                    {
                        case "param":
                        {
                            var sp = surface.GetParameter(s.Param);
                            if (sp == null)
                            {
                                var names = string.Join(", ", surface.Parameters.Select(p => p.Name));
                                throw new ArgumentException("材质里没有参数 " + s.Param + "（可用: " + names + "）");
                            }
                            node = FindParameterNode(surface, paramArch, sp.ID);
                            if (node != null)
                            {
                                reused.Add(s.Key);
                            }
                            else
                            {
                                node = ctx.SpawnNode(paramGroup, paramArch, pos, new object[] { sp.ID });
                                if (node == null)
                                    throw new InvalidOperationException("Get Parameter 节点创建失败: " + s.Param);
                                spawned.Add(s.Key);
                            }
                            // 针是建节点时**动态生成**的（archetype.Elements 里只有 ComboBox），
                            // 所以按参数类型查表取箱（ResolveParamOutputBox）。
                            // 注意：贴图参数的 `Color` 输出**本身就是采样结果**，
                            // 所以不需要（也**不能**）再接一个 Sample Texture 节点。
                            outBox = node.GetBox(ResolveParamOutputBox(sp.Type, s.Component));
                            break;
                        }
                        case "ref":
                        {
                            int refIndex = int.Parse(s.Param);
                            var refNode = surface.Nodes[refIndex];
                            outBox = FindOutputBox(refNode);
                            if (outBox == null)
                                throw new InvalidOperationException("ref 节点没有输出箱: " + s.Param);
                            node = refNode;
                            reused.Add(s.Key);
                            break;
                        }
                        case "unpack":
                        {
                            var g = FindGroup("Packing");
                            var a = g == null ? null : FindArch(g, "Unpack Float4");
                            if (a == null) throw new InvalidOperationException("找不到 Unpack Float4 原型");
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            Connect(ResolveOut(byKey, s.Src), node.GetBox(FindPort(a, "Value", false)));
                            var comp = string.IsNullOrEmpty(s.Component) ? "X" : s.Component.ToUpperInvariant();
                            outBox = node.GetBox(FindPort(a, comp, true));
                            break;
                        }
                        case "unpack3":
                        {
                            var g = FindGroup("Packing");
                            var a = g == null ? null : FindArch(g, "Unpack Float3");
                            if (a == null) throw new InvalidOperationException("找不到 Unpack Float3 原型");
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            Connect(ResolveOut(byKey, s.Src), node.GetBox(FindPort(a, "Value", false)));
                            var comp = string.IsNullOrEmpty(s.Component) ? "X" : s.Component.ToUpperInvariant();
                            outBox = node.GetBox(FindPort(a, comp, true));
                            break;
                        }
                        case "pack3":
                        case "pack4":
                        {
                            var g = FindGroup("Packing");
                            var title = s.Kind == "pack3" ? "Pack Float3" : "Pack Float4";
                            var a = g == null ? null : FindArch(g, title);
                            if (a == null) throw new InvalidOperationException("找不到 " + title + " 原型");
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            Connect(ResolveOut(byKey, s.X), node.GetBox(FindPort(a, "X", false)));
                            Connect(ResolveOut(byKey, s.Y), node.GetBox(FindPort(a, "Y", false)));
                            Connect(ResolveOut(byKey, s.Z), node.GetBox(FindPort(a, "Z", false)));
                            if (s.Kind == "pack4")
                                Connect(ResolveOut(byKey, s.Alpha), node.GetBox(FindPort(a, "W", false)));
                            outBox = node.GetBox(FindPort(a, "Value", true));
                            break;
                        }
                        case "one_minus":
                        case "saturate":
                        {
                            var title = s.Kind == "one_minus" ? "One Minus" : "Saturate";
                            var g = FindGroup("Math");
                            var a = g == null ? null : FindArch(g, title);
                            if (a == null) throw new InvalidOperationException("找不到 Math 原型 " + title);
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            Connect(ResolveOut(byKey, s.A), node.GetBox(FindPort(a, "A", false)));
                            outBox = node.GetBox(FindPort(a, null, true));
                            break;
                        }
                        case "lerp":
                        {
                            var g = FindGroup("Math");
                            var a = g == null ? null : FindArch(g, "Lerp");
                            if (a == null) throw new InvalidOperationException("找不到 Lerp 原型");
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            Connect(ResolveOut(byKey, s.A), node.GetBox(FindPort(a, "A", false)));
                            Connect(ResolveOut(byKey, s.B), node.GetBox(FindPort(a, "B", false)));
                            Connect(ResolveOut(byKey, s.Alpha), node.GetBox(FindPort(a, "Alpha", false)));
                            outBox = node.GetBox(FindPort(a, null, true));
                            break;
                        }
                        case "mul": case "add": case "sub": case "div": case "power":
                        {
                            var title = s.Kind == "mul" ? "Multiply" : s.Kind == "add" ? "Add"
                                : s.Kind == "sub" ? "Subtract" : s.Kind == "div" ? "Divide" : "Power";
                            var g = FindGroup("Math");
                            var a = g == null ? null : FindArch(g, title);
                            if (a == null) throw new InvalidOperationException("找不到 Math 原型 " + title);
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            Connect(ResolveOut(byKey, s.A), node.GetBox(FindPort(a, "A", false)));
                            Connect(ResolveOut(byKey, s.B), node.GetBox(FindPort(a, "B", false)));
                            outBox = node.GetBox(FindPort(a, null, true));
                            break;
                        }
                        case "constant":
                        {
                            var g = FindGroup("Constants");
                            var a = g == null ? null : FindArch(g, "Float");
                            if (a == null) throw new InvalidOperationException("找不到 Constants 的 Float 原型");
                            object[] vals = null;
                            if (a.DefaultValues != null)
                            {
                                vals = (object[])a.DefaultValues.Clone();
                                if (vals.Length > 0 && s.Value.HasValue)
                                    vals[0] = (float)s.Value.Value;
                            }
                            node = ctx.SpawnNode(g, a, pos, vals);
                            spawned.Add(s.Key);
                            outBox = node.GetBox(FindPort(a, null, true));
                            break;
                        }
                        case "uv":
                        {
                            var g = FindGroup("Textures");
                            var a = g == null ? null : FindArch(g, "Texcoords");
                            if (a == null) throw new InvalidOperationException("找不到 Textures 的 Texcoords 原型");
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            outBox = node.GetBox(FindPort(a, s.Component, true));
                            break;
                        }
                        case "dist":
                        {
                            var g = FindGroup("Math");
                            var a = g == null ? null : FindArch(g, "Distance");
                            if (a == null) throw new InvalidOperationException("找不到 Math 原型 Distance");
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            Connect(ResolveOut(byKey, s.A), node.GetBox(FindPort(a, "A", false)));
                            Connect(ResolveOut(byKey, s.B), node.GetBox(FindPort(a, "B", false)));
                            outBox = node.GetBox(FindPort(a, null, true));
                            break;
                        }
                        case "smoothstep":
                        {
                            var g = FindGroup("Math");
                            var a = g == null ? null : FindArch(g, "Smoothstep");
                            if (a == null) throw new InvalidOperationException("找不到 Math 原型 Smoothstep");
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            Connect(ResolveOut(byKey, s.A), node.GetBox(FindPort(a, "Min", false)));
                            Connect(ResolveOut(byKey, s.B), node.GetBox(FindPort(a, "Max", false)));
                            Connect(ResolveOut(byKey, s.Src), node.GetBox(FindPort(a, "Value", false)));
                            outBox = node.GetBox(FindPort(a, null, true));
                            break;
                        }
                        case "const2":
                        {
                            var g = FindGroup("Constants");
                            var a = g == null ? null : FindArch(g, "Float2");
                            if (a == null) throw new InvalidOperationException("找不到 Constants 原型 Float2");
                            object[] vals = null;
                            if (a.DefaultValues != null)
                            {
                                vals = (object[])a.DefaultValues.Clone();
                                // Float2 的 Values[0] 是 Vector2（不是一个 float 数组）：按两个 float 塞会静默变成 (0,0)。
                                if (vals.Length >= 1 && !string.IsNullOrEmpty(s.Code))
                                {
                                    string[] parts = s.Code.Split(',');
                                    if (parts.Length >= 2)
                                    {
                                        float vx = float.Parse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                                        float vy = float.Parse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                                        vals[0] = new Vector2(vx, vy);
                                    }
                                }
                            }
                            node = ctx.SpawnNode(g, a, pos, vals);
                            spawned.Add(s.Key);
                            outBox = node.GetBox(FindPort(a, "Value", true));
                            break;
                        }
                        case "custom":
                        {
                            // 自定义 HLSL 节点（Material/Custom Code，TypeID=8）：用于逐字移植 UE 函数，
                            // 避免把数学近似写成“看起来差不多”的节点链。
                            var g = FindGroup("Material");
                            var a = g == null ? null : FindArch(g, "Custom Code");
                            if (a == null) throw new InvalidOperationException("找不到 Material 的 Custom Code 原型");
                            object[] vals = null;
                            if (a.DefaultValues != null)
                            {
                                vals = (object[])a.DefaultValues.Clone();
                                if (vals.Length > 0 && !string.IsNullOrEmpty(s.Code))
                                    vals[0] = s.Code;
                            }
                            node = ctx.SpawnNode(g, a, pos, vals);
                            spawned.Add(s.Key);
                            if (!string.IsNullOrEmpty(s.A))
                                Connect(ResolveOut(byKey, s.A), node.GetBox(FindPort(a, "Input0", false)));
                            if (!string.IsNullOrEmpty(s.B))
                                Connect(ResolveOut(byKey, s.B), node.GetBox(FindPort(a, "Input1", false)));
                            outBox = node.GetBox(FindPort(a, "Output0", true));
                            break;
                        }
                        case "length":
                        {
                            var g = FindGroup("Math");
                            var a = g == null ? null : FindArch(g, "Length");
                            if (a == null) throw new InvalidOperationException("找不到 Math 的 Length 原型");
                            node = ctx.SpawnNode(g, a, pos, null);
                            spawned.Add(s.Key);
                            Connect(ResolveOut(byKey, s.A), node.GetBox(FindPort(a, "A", false)));
                            outBox = node.GetBox(FindPort(a, null, true));
                            break;
                        }
                        case "normalize":
                        {
                            // Flax 没有 Normalize 原型 ⇒ a / length(a)
                            var gM = FindGroup("Math");
                            var lenA = gM == null ? null : FindArch(gM, "Length");
                            var divA = gM == null ? null : FindArch(gM, "Divide");
                            if (lenA == null || divA == null)
                                throw new InvalidOperationException("normalize 需要 Math 的 Length + Divide 原型");
                            var srcA = ResolveOut(byKey, s.A);
                            var lnN = ctx.SpawnNode(gM, lenA, pos + new Float2(200, 0), null);
                            spawned.Add(s.Key + ":length");
                            Connect(srcA, lnN.GetBox(FindPort(lenA, "A", false)));
                            var dvN = ctx.SpawnNode(gM, divA, pos + new Float2(400, 0), null);
                            spawned.Add(s.Key);
                            Connect(srcA, dvN.GetBox(FindPort(divA, "A", false)));
                            Connect(lnN.GetBox(FindPort(lenA, null, true)), dvN.GetBox(FindPort(divA, "B", false)));
                            node = dvN;
                            outBox = dvN.GetBox(FindPort(divA, null, true));
                            break;
                        }
                        case "dot":
                        {
                            // Flax 没有 Dot 原型 ⇒ 展开成 Σ 拆分量×拆分量（默认 4 个分量，掩码是 RGBA 点积）
                            int nComp = s.Components ?? 4;
                            var gP = FindGroup("Packing");
                            var gM = FindGroup("Math");
                            var upA = gP == null ? null : FindArch(gP, "Unpack Float4");
                            var mulA = gM == null ? null : FindArch(gM, "Multiply");
                            var addA = gM == null ? null : FindArch(gM, "Add");
                            if (upA == null || mulA == null || addA == null)
                                throw new InvalidOperationException("dot 需要 Unpack Float4 + Multiply + Add 原型");
                            var boxA = ResolveOut(byKey, s.A);
                            var boxB = ResolveOut(byKey, s.B);
                            var uA = ctx.SpawnNode(gP, upA, pos + new Float2(0, 0), null);
                            var uB = ctx.SpawnNode(gP, upA, pos + new Float2(0, 140), null);
                            spawned.Add(s.Key + ":unpackA");
                            spawned.Add(s.Key + ":unpackB");
                            Connect(boxA, uA.GetBox(FindPort(upA, "Value", false)));
                            Connect(boxB, uB.GetBox(FindPort(upA, "Value", false)));
                            Box acc = null;
                            for (int k = 0; k < nComp; k++)
                            {
                                string comp = k == 0 ? "X" : k == 1 ? "Y" : k == 2 ? "Z" : "W";
                                var mulK = ctx.SpawnNode(gM, mulA, pos + new Float2(220, k * 90), null);
                                spawned.Add(s.Key + ":mul" + comp);
                                Connect(uA.GetBox(FindPort(upA, comp, true)), mulK.GetBox(FindPort(mulA, "A", false)));
                                Connect(uB.GetBox(FindPort(upA, comp, true)), mulK.GetBox(FindPort(mulA, "B", false)));
                                var mBox = mulK.GetBox(FindPort(mulA, null, true));
                                if (acc == null)
                                {
                                    acc = mBox;
                                }
                                else
                                {
                                    var addK = ctx.SpawnNode(gM, addA, pos + new Float2(440, k * 90), null);
                                    spawned.Add(s.Key + ":add" + comp);
                                    Connect(acc, addK.GetBox(FindPort(addA, "A", false)));
                                    Connect(mBox, addK.GetBox(FindPort(addA, "B", false)));
                                    acc = addK.GetBox(FindPort(addA, null, true));
                                }
                            }
                            node = null;
                            outBox = acc;
                            break;
                        }
                        default:
                            throw new ArgumentException("不认识的 kind: " + s.Kind + "（key=" + s.Key + "）");
                    }

                    if (outBox == null)
                        throw new InvalidOperationException("节点 " + s.Key + " 取不到输出针（原型=" + s.Kind + "）");
                    byKey[s.Key] = new Slot { Node = node, Out = outBox };
                    slotList.Add(new KeyValuePair<string, Slot>(s.Key, byKey[s.Key]));
                }

                // ③ 接到材质输出
                var root = FindMaterialRoot(surface);
                if (root == null)
                    throw new InvalidOperationException("图里没有材质输出节点");
                // 断开材质输入上的全部连线（用于清掉探针/旧接线，Flax 允许一个输入挂多个源）。
                // 必须**先断开再接线**：反过来的顺序会把本次刚接上的源一并清掉（2026-10-20 实测）。
                foreach (var name in disconnect)
                {
                    if (!TryMapMaterialInput(name, out int discId))
                        throw new ArgumentException("不认识的材质输入名: " + name);
                    var dstPin = root.GetBox(discId);
                    if (dstPin == null)
                        throw new InvalidOperationException("材质输出取不到输入针: " + name);
                    for (int i = dstPin.Connections.Count - 1; i >= 0; i--)
                    {
                        var other = dstPin.Connections[i];
                        other.Connections.Remove(dstPin);
                        other.OnConnectionsChanged();
                        other.ConnectionTick();
                    }
                    dstPin.Connections.Clear();
                    dstPin.OnConnectionsChanged();
                    dstPin.ConnectionTick();
                    wired.Add("disconnect " + name);
                }

                foreach (var o in outputs)
                {
                    // 来源：本次新建的 key，或已存在节点的指定输出针（fromNodeIndex+fromPin）。
                    Box srcBox;
                    string srcLabel;
                    if (o.FromNodeIndex.HasValue)
                    {
                        int srcIndex = o.FromNodeIndex.Value;
                        if (srcIndex < 0 || srcIndex >= surface.Nodes.Count)
                            throw new InvalidOperationException("fromNodeIndex 越界: " + srcIndex);
                        var srcNode = surface.Nodes[srcIndex];
                        if (srcNode == null)
                            throw new InvalidOperationException("fromNodeIndex 指向空节点: " + srcIndex);
                        if (string.IsNullOrWhiteSpace(o.FromPin))
                            throw new ArgumentException("outputs 用 fromNodeIndex 时必须给 fromPin: " + srcIndex);
                        int srcPinId = FindPort(srcNode.Archetype, o.FromPin, true);
                        srcBox = srcNode.GetBox(srcPinId);
                        if (srcBox == null)
                            throw new InvalidOperationException("fromNodeIndex 取不到输出针: "
                                + srcIndex + "." + o.FromPin);
                        srcLabel = "#" + srcIndex + "." + o.FromPin;
                    }
                    else
                    {
                        Slot slot;
                        if (!byKey.TryGetValue(o.From, out slot))
                            throw new ArgumentException("outputs 引用了不存在的 key: " + o.From);
                        srcBox = slot.Out;
                        srcLabel = o.From;
                    }

                    // 接本次新建节点的输入针（例如把 UV*30 接回贴图参数的 UVs）
                    if (!string.IsNullOrWhiteSpace(o.NodeKey))
                    {
                        Slot targetSlot;
                        if (!byKey.TryGetValue(o.NodeKey, out targetSlot) || targetSlot.Node == null)
                            throw new InvalidOperationException("nodeKey 指向的节点不可用: " + o.NodeKey);
                        // pin 允许直接写**箱 ID**（Get Parameter 的 UVs 在不同参数类型下文本会变，
                        // 按名找会失败；贴图参数 UVs 固定是箱 0，见 Archetypes/Parameters.cs:273）。
                        int pinId = int.TryParse(o.Pin, out int explicitPin)
                            ? explicitPin
                            : FindPort(targetSlot.Node.Archetype, o.Pin, false);
                        var dstPin = targetSlot.Node.GetBox(pinId);
                        if (dstPin == null)
                            throw new InvalidOperationException("nodeKey 目标取不到输入针: "
                                + o.NodeKey + "." + o.Pin);
                        Connect(srcBox, dstPin);
                        wired.Add(o.NodeKey + "." + o.Pin + " <- " + srcLabel + " (nodeKey)");
                        continue;
                    }

                    // 接已存在节点的指定针（按 dump 的 nodeIndex 定位；同名同参数节点有多个时用它消歧）
                    if (o.NodeIndex.HasValue)
                    {
                        int index = o.NodeIndex.Value;
                        if (index < 0 || index >= surface.Nodes.Count)
                            throw new InvalidOperationException("nodeIndex 越界: " + index
                                + "（节点数 " + surface.Nodes.Count + "）");
                        var target = surface.Nodes[index];
                        if (target == null)
                            throw new InvalidOperationException("nodeIndex 指向空节点: " + index);
                        int pinId = FindPort(target.Archetype, o.Pin, false);
                        var dstPin = target.GetBox(pinId);
                        if (dstPin == null)
                            throw new InvalidOperationException("nodeIndex 目标取不到输入针: "
                                + index + "." + o.Pin);
                        Connect(srcBox, dstPin);
                        wired.Add(index + "." + o.Pin + " <- " + srcLabel + " (nodeIndex)");
                        continue;
                    }

                    // 接已存在节点的指定针（例如把混合结果插回 Pack Float3 的 X/Y/Z）
                    if (!string.IsNullOrWhiteSpace(o.Node))
                    {
                        var target = FindNodeByTitleLinkedParam(surface, o.Node, o.MatchParam);
                        if (target == null)
                            throw new InvalidOperationException("找不到目标节点: " + o.Node
                                + "（matchParam=" + o.MatchParam + "）");
                        int pinId = FindPort(target.Archetype, o.Pin, false);
                        var dstPin = target.GetBox(pinId);
                        if (dstPin == null)
                            throw new InvalidOperationException("目标节点取不到输入针: " + o.Node + "." + o.Pin);
                        Connect(srcBox, dstPin);
                        wired.Add(o.Node + "." + o.Pin + " <- " + srcLabel + " (match " + o.MatchParam + ")");
                        continue;
                    }

                    if (!TryMapMaterialInput(o.Input, out int inputId))
                        throw new ArgumentException("不认识的材质输入名: " + o.Input);
                    var dst = root.GetBox(inputId);
                    if (dst == null)
                        throw new InvalidOperationException("材质输出取不到输入针: " + o.Input + " id=" + inputId);
                    Connect(srcBox, dst);
                    wired.Add(o.Input + " <- " + srcLabel);
                }

                // **只有 apply=true 才把 surface 标脏**。dry-run 若也标脏，编辑器会在窗口关闭/焦点变化时
                // 自作主张把半成品图存盘（2026-10-16 实测：一次 dry-run 就这样落了一个游离节点，
                // 而那个节点随后让材质编译器递归崩溃）。dry-run 的语义必须是"绝不落盘"。
                if (apply)
                {
                    ctx.MarkAsModified(true);
                    surface.MarkAsEdited();
                    var normalPin = root.GetBox(8);
                    var colorPin = root.GetBox(1);
                    rootNormalAfterWire = normalPin != null ? normalPin.Connections.Count : -1;
                    rootNormalActive = normalPin != null && normalPin.IsActive;
                    rootColorActive = colorPin != null && colorPin.IsActive;
                }
            });

            // ④ 落盘 + 复核运行时参数表
            bool didWrite = apply && (spawned.Count > 0 || wired.Count > 0);
            if (didWrite)
            {
                await MainThread.InvokeAsync(() => window.Save());
                await MainThread.InvokeAsync(() =>
                {
                    try
                    {
                        var s = window.Surface;
                        if (s == null)
                            return;
                        var r = FindMaterialRoot(s);
                        var pin = r?.GetBox(8);
                        rootNormalAfterSave = pin != null ? pin.Connections.Count : -1;
                        editedAfterSave = s.IsEdited;
                    }
                    catch
                    {
                    }
                });
            }

            var runtimeAfterSave = new List<string>();
            await MainThread.InvokeAsync(() =>
            {
                var loadedAsset = Content.Load<MaterialBase>(normalized, 10000.0);
                if (loadedAsset != null && loadedAsset.IsLoaded && loadedAsset.Parameters != null)
                    foreach (MaterialParameter mp in loadedAsset.Parameters)
                        runtimeAfterSave.Add(mp.Name);
            });

            // 收尾关窗：同一资产若同时存在多个 MaterialWindow，旧窗口会在失焦/关闭时把自己
            // 的旧图存盘，把本次接线覆盖掉（实测：回执 ok、文件 +5 字节、连接消失）。
            await MainThread.InvokeAsync(() =>
            {
                try
                {
                    if (window != null)
                        window.Close();
                }
                catch
                {
                    // 关窗失败不影响本次结果
                }
            });

            return new
            {
                status = !apply ? "dry-run" : (didWrite ? "ok" : "ok/no-op"),
                path = normalized,
                applied = apply,
                didWrite,
                surfaceReadyAfterFrames = waited,
                spawnedCount = spawned.Count,
                spawned,
                reusedCount = reused.Count,
                reused,
                wiredCount = wired.Count,
                wired,
                rootNormalAfterWire,
                rootNormalAfterSave,
                editedAfterSave,
                rootNormalActive,
                rootColorActive,
                runtimeParamCount = runtimeAfterSave.Count,
                runtimeParamNames = runtimeAfterSave,
                errors,
                warnings,
                note = !apply
                    ? "dry-run：只做静态校验，**未建任何节点/未连线**（改图会被编辑器自动存盘，实测踩过）。"
                    : (didWrite
                        ? "已 window.Save() 落盘并复核运行时参数表（runtimeParamNames）。"
                          + "接线是否真生效仍要另测：用 Tools/FlaxMaterialGraphScan.py（连接解析补齐后）或像素目视。"
                        : "apply=true 但没有实质改动，未触发保存。"),
            };
        }

        // ==================== 辅助 ====================

        /// <summary>
        /// dry-run 的静态校验：把"建图时会用到的每一样东西"都查一遍，但**不碰图**。
        /// 查：kind 认不认、原型找不找得到、端口名对不对、来源 key 有没有定义在前、
        /// 参数在不在材质里、材质输出节点在不在、outputs 的名字与来源 key 有效。
        /// </summary>
        private static void ValidateSpec(VisjectSurface surface, List<Spec> specs,
                                        List<OutputSpec> outputs)
        {
            if (FindMaterialRoot(surface) == null)
                throw new InvalidOperationException("图里没有材质输出节点（原型或节点缺失）");

            var known = new HashSet<string>();
            Action<string> needSrc = k =>
            {
                if (string.IsNullOrEmpty(k))
                    throw new ArgumentException("缺少来源 key");
                if (!known.Contains(k))
                    throw new ArgumentException("引用了尚未定义（或顺序在前）的 key: " + k);
            };

            foreach (var s in specs)
            {
                Action<GroupArchetype, string> needArch = (g, title) =>
                {
                    if (g == null)
                        throw new InvalidOperationException("找不到组（kind=" + s.Kind + " key=" + s.Key + "）");
                    if (FindArch(g, title) == null)
                        throw new InvalidOperationException("找不到原型 " + title
                            + "（kind=" + s.Kind + " key=" + s.Key + "）");
                };

                switch (s.Kind)
                {
                    case "param":
                    {
                        var sp = surface.GetParameter(s.Param);
                        if (sp == null)
                        {
                            var names = string.Join(", ", surface.Parameters.Select(p => p.Name));
                            throw new ArgumentException("材质里没有参数 " + s.Param + "（可用: " + names + "）");
                        }
                        ResolveParamOutputBox(sp.Type, s.Component);     // 端口名合法性
                        break;
                    }
                    case "ref":
                    {
                        // 引用已存在节点的输出（param 填节点下标，与 material_graph_dump 的 nodeIndex 同一套）。
                        // 用途：复用图里已算好的动画掩码，避免把 41 个权重与 8 张掩码贴图再算一遍。
                        if (!int.TryParse(s.Param, out int refIndex)
                            || refIndex < 0 || refIndex >= surface.Nodes.Count)
                            throw new ArgumentException("ref 的 param 必须是有效节点下标: " + s.Param);
                        if (FindOutputBox(surface.Nodes[refIndex]) == null)
                            throw new ArgumentException("ref 节点没有输出箱: " + s.Param);
                        break;
                    }
                    case "unpack":
                        needArch(FindGroup("Packing"), "Unpack Float4");
                        needSrc(s.Src);
                        break;
                    case "unpack3":
                        needArch(FindGroup("Packing"), "Unpack Float3");
                        needSrc(s.Src);
                        break;
                    case "pack3":
                    case "pack4":
                        needArch(FindGroup("Packing"), s.Kind == "pack3" ? "Pack Float3" : "Pack Float4");
                        needSrc(s.X); needSrc(s.Y); needSrc(s.Z);
                        if (s.Kind == "pack4") needSrc(s.Alpha);
                        break;
                    case "one_minus":
                    case "saturate":
                        needArch(FindGroup("Math"), s.Kind == "one_minus" ? "One Minus" : "Saturate");
                        needSrc(s.A);
                        break;
                    case "lerp":
                        needArch(FindGroup("Math"), "Lerp");
                        needSrc(s.A); needSrc(s.B); needSrc(s.Alpha);
                        break;
                    case "mul": case "add": case "sub": case "div": case "power":
                        needArch(FindGroup("Math"),
                            s.Kind == "mul" ? "Multiply" : s.Kind == "add" ? "Add"
                            : s.Kind == "sub" ? "Subtract" : s.Kind == "div" ? "Divide" : "Power");
                        needSrc(s.A); needSrc(s.B);
                        break;
                    case "constant":
                        needArch(FindGroup("Constants"), "Float");
                        break;
                    case "uv":
                        needArch(FindGroup("Textures"), "Texcoords");
                        break;
                    case "custom":
                        needArch(FindGroup("Material"), "Custom Code");
                        if (!string.IsNullOrEmpty(s.A)) needSrc(s.A);
                        if (!string.IsNullOrEmpty(s.B)) needSrc(s.B);
                        break;
                    case "length":
                        needArch(FindGroup("Math"), "Length");
                        needSrc(s.A);
                        break;
                    case "normalize":
                        needArch(FindGroup("Math"), "Length");
                        needArch(FindGroup("Math"), "Divide");
                        needSrc(s.A);
                        break;
                    case "dot":
                        needArch(FindGroup("Packing"), "Unpack Float4");
                        needArch(FindGroup("Math"), "Multiply");
                        needArch(FindGroup("Math"), "Add");
                        needSrc(s.A); needSrc(s.B);
                        break;
                    case "dist":
                        needArch(FindGroup("Math"), "Distance");
                        needSrc(s.A); needSrc(s.B);
                        break;
                    case "smoothstep":
                        needArch(FindGroup("Math"), "Smoothstep");
                        needSrc(s.A); needSrc(s.B); needSrc(s.Src);
                        break;
                    case "const2":
                        needArch(FindGroup("Constants"), "Float2");
                        break;
                    default:
                        throw new ArgumentException("不认识的 kind: " + s.Kind + "（key=" + s.Key + "）");
                }

                known.Add(s.Key);
            }

            foreach (var o in outputs)
            {
                if (o.FromNodeIndex.HasValue)
                {
                    if (string.IsNullOrWhiteSpace(o.FromPin))
                        throw new ArgumentException("outputs 用 fromNodeIndex 时必须给 fromPin");
                    if (string.IsNullOrWhiteSpace(o.Pin) &&
                        string.IsNullOrWhiteSpace(o.Input) &&
                        string.IsNullOrWhiteSpace(o.Node) &&
                        string.IsNullOrWhiteSpace(o.NodeKey) &&
                        !o.NodeIndex.HasValue)
                        throw new ArgumentException("outputs 缺少目标（input/node/nodeKey/nodeIndex）");
                    continue;
                }

                if (o.NodeIndex.HasValue)
                {
                    if (string.IsNullOrWhiteSpace(o.Pin))
                        throw new ArgumentException("outputs 用 nodeIndex 时必须给 pin: " + o.NodeIndex);
                    if (!known.Contains(o.From))
                        throw new ArgumentException("outputs 引用了不存在的 key: " + o.From);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(o.NodeKey))
                {
                    if (!known.Contains(o.NodeKey))
                        throw new ArgumentException("outputs 的 nodeKey 不在本次 nodes 里（或顺序在前）: " + o.NodeKey);
                    if (string.IsNullOrWhiteSpace(o.Pin))
                        throw new ArgumentException("outputs 用 nodeKey 时必须给 pin: " + o.NodeKey);
                    if (!known.Contains(o.From))
                        throw new ArgumentException("outputs 引用了不存在的 key: " + o.From);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(o.Node))
                {
                    if (string.IsNullOrWhiteSpace(o.Pin))
                        throw new ArgumentException("outputs 接目标节点时必须给 pin: " + o.Node);
                    if (!known.Contains(o.From))
                        throw new ArgumentException("outputs 引用了不存在的 key: " + o.From);
                    continue;
                }

                if (!TryMapMaterialInput(o.Input, out _))
                    throw new ArgumentException("不认识的材质输入名: " + o.Input);
                if (!known.Contains(o.From))
                    throw new ArgumentException("outputs 引用了不存在的 key: " + o.From);
            }
        }

        private static GroupArchetype FindGroup(string name)
        {
            foreach (GroupArchetype g in NodeFactory.DefaultGroups)
                if (g != null && g.Name == name)
                    return g;
            return null;
        }

        private static NodeArchetype FindArch(GroupArchetype group, string title, int typeId = -1)
        {
            foreach (NodeArchetype a in group.Archetypes)
            {
                if (a == null)
                    continue;
                if (typeId >= 0 && a.TypeID == typeId)
                    return a;
                if (a.Title == title)
                    return a;
                if (a.AlternativeTitles != null)
                    foreach (var alt in a.AlternativeTitles)
                        if (alt == title)
                            return a;
            }
            return null;
        }

        /// <summary>按名字找针的 BoxID；name 为空表示"该方向的第一个"。</summary>
        private static int FindPort(NodeArchetype arch, string name, bool output)
        {
            var want = output ? NodeElementType.Output : NodeElementType.Input;
            if (arch.Elements != null)
            {
                foreach (var el in arch.Elements)
                {
                    if (el.Type != want)
                        continue;
                    if (string.IsNullOrEmpty(name) || el.Text == name)
                        return el.BoxID;
                }
            }
            throw new InvalidOperationException("原型 " + arch.Title + " 上找不到针: "
                + (output ? "输出/" : "输入/") + (name ?? "<第一个>"));
        }

        private static Box ResolveOut(Dictionary<string, Slot> byKey, string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("缺少来源 key");
            Slot slot;
            if (!byKey.TryGetValue(key, out slot) || slot.Out == null)
                throw new ArgumentException("引用了尚未定义（或顺序在前）的 key: " + key);
            return slot.Out;
        }

        private static void Connect(Box src, Box dst)
        {
            if (src == null || dst == null)
                throw new InvalidOperationException("连线的源或目标是 null");
            // 方向闸门：材质图**只允许 输出箱 → 输入箱**（引擎自己在 Box.cs:705 也是这么判的）。
            // 少了这条，"把某个输入箱当来源"会被静默接受，生成器随后在两个节点间互相递归，
            // 直接把编辑器**崩掉**（2026-10-16 实测：Get Parameter 的 UVs 输入被当成输出来用）。
            if (!src.IsOutput || dst.IsOutput)
                throw new InvalidOperationException(
                    "连线方向非法：必须 输出→输入。src.IsOutput=" + src.IsOutput + " dst.IsOutput=" + dst.IsOutput);
            for (int i = dst.Connections.Count - 1; i >= 0; i--)
            {
                var other = dst.Connections[i];
                other.Connections.Remove(dst);
                other.OnConnectionsChanged();
                other.ConnectionTick();
            }
            // 走引擎的 CreateConnection（双向登记 + 状态刷新），并显式 MarkAsEdited：
            // VisjectSurfaceWindow.Save() 第一句是 `if (!IsEdited) return;`，少了这一步
            // 保存会静默空转——节点因为 spawn 自己标脏能存进去，手工接线却存不进去（实测）。
            dst.CreateConnection(src);
            dst.Surface?.MarkAsEdited();
        }

        /// <summary>
        /// 解析 Get Parameter 节点的输出箱。**不能一律取箱 0**：贴图参数的箱 0 是 `UVs` 输入，
        /// "贴图参数自带的采样结果"是 `Color`（箱 1）—— 见 `Archetypes/Parameters.cs:270-280`。
        /// 取错方向 → 生成器 `ProcessGroupParameters ↔ ProcessGroupTextures` 互相递归 → 编辑器崩溃。
        /// </summary>
        private static int ResolveParamOutputBox(ScriptType type, string ask)
        {
            var t = type.Type;
            string want = string.IsNullOrWhiteSpace(ask) ? null : ask.Trim().ToUpperInvariant();
            if (t == typeof(Texture))
            {
                switch (want)
                {
                    case null: case "COLOR": case "VALUE": return 1;
                    case "R": case "X": return 2;
                    case "G": case "Y": return 3;
                    case "B": case "Z": return 4;
                    case "A": case "W": return 5;
                    case "OBJECT": case "TEXTURE": return 6;
                    default: throw new ArgumentException("贴图参数没有输出端口 " + ask);
                }
            }
            if (t == typeof(Color))
            {
                switch (want)
                {
                    case null: case "COLOR": case "VALUE": return 0;
                    case "R": case "X": return 1;
                    case "G": case "Y": return 2;
                    case "B": case "Z": return 3;
                    case "A": case "W": return 4;
                    default: throw new ArgumentException("颜色参数没有输出端口 " + ask);
                }
            }
            if (t == typeof(Float2) || t == typeof(Float3) || t == typeof(Float4))
            {
                switch (want)
                {
                    case null: case "VALUE": case "COLOR": return 0;
                    case "X": case "R": return 1;
                    case "Y": case "G": return 2;
                    case "Z": case "B": return 3;
                    case "W": case "A": return 4;
                    default: throw new ArgumentException("向量参数没有输出端口 " + ask);
                }
            }
            return 0;   // 标量 / 布尔 / 整数：箱 0
        }

        /// <summary>
        /// 取一个节点唯一的输出箱（Visject 里输出箱总在输入端之后，`IsOutput` 直接可判）。
        /// </summary>
        private static Box FindOutputBox(SurfaceNode node)
        {
            if (node == null || node.Archetype == null || node.Archetype.Elements == null)
                return null;
            for (int i = 0; i < node.Archetype.Elements.Length; i++)
            {
                var box = node.GetBox(i);
                if (box != null && box.IsOutput)
                    return box;
            }
            return null;
        }

        /// <summary>
        /// 找“标题匹配、且某个输入当前连着指定参数”的已存在节点。
        /// 用途：把混合结果插回 Color_MAIN 的 Pack Float3，同时保留它原有的下游消费链。
        /// </summary>
        private static SurfaceNode FindNodeByTitleLinkedParam(VisjectSurface surface, string title, string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName))
                throw new ArgumentException("接目标节点时必须给 matchParam");
            var sp = surface.GetParameter(paramName);
            if (sp == null)
                throw new ArgumentException("材质里没有参数 " + paramName);

            foreach (SurfaceNode n in surface.RootContext.Nodes)
            {
                if (n == null || n.Archetype == null || n.Archetype.Title != title || n.Archetype.Elements == null)
                    continue;
                foreach (var el in n.Archetype.Elements)
                {
                    if (el.Type != NodeElementType.Input)
                        continue;
                    Box box = n.GetBox(el.BoxID);
                    if (box == null || box.Connections == null)
                        continue;
                    foreach (Box src in box.Connections)
                    {
                        SurfaceNode srcNode = src == null ? null : src.ParentNode;
                        if (srcNode == null || srcNode.Archetype == null || srcNode.Archetype.Title != "Get Parameter")
                            continue;
                        if (srcNode.Values != null && srcNode.Values.Length > 0
                            && srcNode.Values[0] is Guid g && g == sp.ID)
                        {
                            return n;
                        }
                    }
                }
            }
            return null;
        }

        private static SurfaceNode FindParameterNode(VisjectSurface surface, NodeArchetype arch, Guid paramId)        {
            foreach (SurfaceNode n in surface.RootContext.Nodes)
            {
                if (n == null || n.Archetype != arch || n.Values == null || n.Values.Length == 0)
                    continue;
                if (n.Values[0] is Guid g && g == paramId)
                    return n;
            }
            return null;
        }

        private static SurfaceNode FindMaterialRoot(VisjectSurface surface)
        {
            var g = FindGroup("Material");
            var a = g == null ? null : FindArch(g, null, 1);
            if (a == null)
                return null;
            foreach (SurfaceNode n in surface.RootContext.Nodes)
                if (n != null && n.Archetype == a)
                    return n;
            return null;
        }

        /// <summary>材质输出节点的输入名 → pin id（照 Archetypes/Material.cs 的 Elements）。</summary>
        private static bool TryMapMaterialInput(string name, out int id)
        {
            id = -1;
            switch ((name ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty))
            {
                case "color": case "basecolor": case "albedo": id = 1; return true;
                case "mask": id = 2; return true;
                case "emissive": case "emission": id = 3; return true;
                case "metalness": case "metallic": id = 4; return true;
                case "specular": id = 5; return true;
                case "roughness": id = 6; return true;
                case "ao": case "ambientocclusion": case "cavity": id = 7; return true;
                case "normal": id = 8; return true;
                case "opacity": case "alpha": id = 9; return true;
                case "refraction": id = 10; return true;
                case "positionoffset": id = 11; return true;
                case "tessellation": case "tessellationmultiplier": id = 12; return true;
                case "worlddisplacement": case "displacement": id = 13; return true;
                case "subsurface": case "subsurfacecolor": id = 14; return true;
                default: return false;
            }
        }
    }
}
