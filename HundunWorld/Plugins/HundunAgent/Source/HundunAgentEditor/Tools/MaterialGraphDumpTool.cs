using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.Surface;
using FlaxEditor.Surface.Elements;
using FlaxEditor.Windows.Assets;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 只读：把一张材质图的节点与连线摊开，回答"命名参数到底接到哪了"。
    /// 离线 VISJECT 解析器还有一格没对齐（节点体），这里走编辑器自己的 Surface 对象，
    /// 连接关系是**运行时真值**，不靠猜二进制。
    /// </summary>
    public static class MaterialGraphDumpTool
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "material_graph_dump",
                Description = "只读：打开材质窗口，逐节点列出 Archetype/参数名/箱，以及输入箱的连线来源。" +
                              "用来验证命名参数是否真的接到材质输出。不保存、不标脏。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"path\":{\"type\":\"string\"}," +
                                  "\"maxBoxes\":{\"type\":\"integer\",\"description\":\"每节点最多扫多少箱，默认 64\"}}}",
                Execute = DumpAsync
            });
        }

        private static async Task<object> DumpAsync(JsonElement args)
        {
            string raw = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(raw))
            {
                throw new ArgumentException("缺少 path");
            }

            string path = raw.Replace('\\', '/');
            int idx = path.IndexOf("Content/", StringComparison.OrdinalIgnoreCase);
            string normalized = idx >= 0 ? path.Substring(idx) : path;
            int maxBoxes = Math.Clamp(EditorUtils.GetInt(args, "maxBoxes", 64), 1, 256);

            MaterialWindow window = null;
            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var assetItem = editor.ContentDatabase.Find(normalized) as AssetItem
                                ?? editor.ContentDatabase.Find(path) as AssetItem;
                if (assetItem == null)
                {
                    var loaded = Content.Load<MaterialBase>(normalized, 10000.0);
                    if (loaded == null || !loaded.IsLoaded)
                        throw new ArgumentException("材质加载失败: " + raw);
                    assetItem = editor.ContentDatabase.Find(loaded.ID) as AssetItem;
                }
                if (assetItem == null)
                    throw new ArgumentException("找不到 AssetItem: " + raw);

                Content.Load<MaterialBase>(normalized, 10000.0);
                window = new MaterialWindow(editor, assetItem);
                editor.Windows.Open(window);
            });

            for (int i = 0; i < 240; i++)
            {
                if (await MainThread.InvokeAsync(() => window != null && window.Surface != null && window.Surface.Enabled))
                    break;
            }
            if (!await MainThread.InvokeAsync(() => window != null && window.Surface != null && window.Surface.Enabled))
                throw new InvalidOperationException("材质窗口 surface 未就绪");

            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var surface = window.Surface;
                var nodes = surface.RootContext.Nodes.Where(n => n != null).ToList();
                var indexOf = new Dictionary<SurfaceNode, int>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    indexOf[nodes[i]] = i;
                }

                // 参数 id -> 名字
                var paramNames = new Dictionary<Guid, string>();
                foreach (var p in surface.Parameters)
                {
                    paramNames[p.ID] = p.Name;
                }

                // 箱 -> 所属节点（SurfaceNode 没有公开 ParentNode 时也能反查）
                var boxOwner = new Dictionary<Box, SurfaceNode>();
                var nodeBoxes = new List<Box[]>();
                foreach (var n in nodes)
                {
                    var list = new List<Box>();
                    for (int b = 0; b < maxBoxes; b++)
                    {
                        Box box = n.GetBox(b);
                        if (box == null)
                        {
                            continue;
                        }
                        list.Add(box);
                        boxOwner[box] = n;
                    }
                    nodeBoxes.Add(list.ToArray());
                }

                string TitleOf(SurfaceNode n)
                {
                    try
                    {
                        return n.Archetype != null ? n.Archetype.Title : "?";
                    }
                    catch
                    {
                        return "?";
                    }
                }

                string ParamOf(SurfaceNode n)
                {
                    if (n.Values == null || n.Values.Length == 0)
                    {
                        return null;
                    }
                    if (n.Values[0] is Guid g && paramNames.TryGetValue(g, out var name))
                    {
                        return name;
                    }
                    return null;
                }

                var rows = new List<object>();
                int wired = 0;
                for (int ni = 0; ni < nodes.Count; ni++)
                {
                    var n = nodes[ni];
                    string title = TitleOf(n);
                    string param = ParamOf(n);
                    for (int bi = 0; bi < nodeBoxes[ni].Length; bi++)
                    {
                        Box box = nodeBoxes[ni][bi];
                        if (box == null || box.IsOutput || box.Connections == null || box.Connections.Count == 0)
                        {
                            continue;
                        }
                        foreach (Box src in box.Connections)
                        {
                            if (src == null)
                            {
                                continue;
                            }
                            SurfaceNode srcNode = null;
                            boxOwner.TryGetValue(src, out srcNode);
                            string srcTitle = srcNode != null ? TitleOf(srcNode) : "?";
                            string srcParam = srcNode != null ? ParamOf(srcNode) : null;
                            int srcIndex = srcNode != null && indexOf.TryGetValue(srcNode, out int si) ? si : -1;
                            int srcBox = srcIndex >= 0 ? Array.IndexOf(nodeBoxes[srcIndex], src) : -1;
                            string srcPin = null;
                            if (srcNode != null && srcNode.Archetype != null && srcNode.Archetype.Elements != null)
                            {
                                foreach (var el in srcNode.Archetype.Elements)
                                {
                                    if (el.Type == NodeElementType.Output && el.BoxID == srcBox)
                                    {
                                        srcPin = el.Text;
                                        break;
                                    }
                                }
                            }
                            rows.Add(new
                            {
                                toNode = ni,
                                toTitle = title,
                                toParam = param,
                                toBox = bi,
                                fromNode = srcIndex,
                                fromTitle = srcTitle,
                                fromParam = srcParam,
                                fromBox = srcBox,
                                fromPin = srcPin,
                                fromIsOutput = src.IsOutput
                            });
                            wired++;
                        }
                    }
                }

                // 参数是否至少有一条出线
                var paramUsage = new List<object>();
                foreach (var p in surface.Parameters)
                {
                    int nodeIndex = -1;
                    int conns = 0;
                    for (int ni = 0; ni < nodes.Count; ni++)
                    {
                        var n = nodes[ni];
                        if (n.Values == null || n.Values.Length == 0 || !(n.Values[0] is Guid g) || g != p.ID)
                        {
                            continue;
                        }
                        nodeIndex = ni;
                        foreach (var b in nodeBoxes[ni])
                        {
                            if (b != null && b.IsOutput && b.Connections != null)
                            {
                                conns += b.Connections.Count;
                            }
                        }
                    }
                    paramUsage.Add(new { name = p.Name, nodeIndex, outputConnections = conns });
                }

                // 材质根节点的 Values（BlendMode/ShadingModel 等）：皮肤不显影时必须先看这一组值。
                var rootValues = new List<string>();
                foreach (var n in nodes)
                {
                    if (n == null || n.Archetype == null || n.Archetype.Title != "Material")
                        continue;
                    if (n.Values != null)
                    {
                        foreach (var v in n.Values)
                            rootValues.Add(v == null ? "null" : v.ToString());
                    }
                    break;
                }

                // 常量节点的 Values：根输入全接对但画面不对时，要能直接看到 0.85/0.2/0 这些值到底写没写进去。
                var nodeValues = new List<object>();
                foreach (var n in nodes)
                {
                    if (n == null || n.Archetype == null)
                        continue;
                    var vals = n.Values == null
                        ? new List<string>()
                        : n.Values.Select(v => v == null ? "null" : v.ToString()).ToList();
                    nodeValues.Add(new
                    {
                        index = indexOf.TryGetValue(n, out int ni) ? ni : -1,
                        title = n.Archetype.Title,
                        values = vals
                    });
                }

                return new
                {
                    path = normalized,
                    nodeCount = nodes.Count,
                    parameterCount = surface.Parameters.Count,
                    connectionCount = wired,
                    rootValues,
                    nodeValues,
                    parameters = paramUsage,
                    connections = rows.Take(1200).ToList()
                };
            });

            // 关窗：只读工具也不该把窗口留着——同一资产多个 MaterialWindow 会在失焦/关闭时
            // 用各自的旧图互相覆盖存盘（实测过一次接线被旧窗口写没了）。
            await MainThread.InvokeAsync(() =>
            {
                try
                {
                    if (window != null)
                        window.Close();
                }
                catch
                {
                }
            });

            return result;
        }
    }
}
