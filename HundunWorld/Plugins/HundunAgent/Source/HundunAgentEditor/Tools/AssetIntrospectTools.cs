using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 引擎原生资产探查：通过 Flax 内容系统加载资产后读取真实几何数据。
    /// 与 <see cref="AssetForensicsTools"/> 的字节取证互补——字节直方图只能猜，
    /// 这里拿到的 LOD 顶点数、局部包围盒、骨骼层级是引擎解析后的确定值，
    /// 排查「模型导入后尺度/朝向不对」时应优先看这里。
    /// </summary>
    public static class AssetIntrospectTools
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "model_inspect",
                Description = "加载模型资产并输出引擎解析后的真实数据：资产 Guid/类型、LOD 数与逐级顶点数、局部包围盒（Model.GetBox，用于判断尺度与朝向）、" +
                              "骨骼/节点/BlendShape 数量与骨骼名、包围盒是否异常（过小/过大提示）。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"path\":{\"type\":\"string\",\"description\":\"资产路径，如 Content/Character/Models/skm_uefn_mannequin.flax；也可传 Guid\"}," +
                                  "\"lodIndex\":{\"type\":\"integer\",\"description\":\"取包围盒的 LOD 级别，默认 0（最高精度）\"}," +
                                  "\"includeBones\":{\"type\":\"boolean\",\"description\":\"是否列出骨骼/节点名，默认 true\"}," +
                                  "\"nameLimit\":{\"type\":\"integer\",\"description\":\"骨骼名条数上限，默认 30\"}," +
                                  "\"timeoutMs\":{\"type\":\"integer\",\"description\":\"资产加载超时(毫秒)，默认 10000\"}}," +
                                  "\"required\":[\"path\"]}",
                Execute = ModelInspectAsync
            });
        }

        private static Task<object> ModelInspectAsync(JsonElement args)
        {
            var pathArg = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(pathArg))
                throw new ArgumentException("缺少 path 参数");

            var lodIndex = EditorUtils.GetInt(args, "lodIndex", 0);
            var includeBones = EditorUtils.GetBool(args, "includeBones", true);
            var nameLimit = Math.Clamp(EditorUtils.GetInt(args, "nameLimit", 30), 0, 500);
            var timeoutMs = Math.Clamp(EditorUtils.GetInt(args, "timeoutMs", 10000), 1000, 120000);

            return MainThread.InvokeAsync<object>(() =>
            {
                var warnings = new List<string>();

                // 资产信息（不触发加载也能拿到 Guid 与类型名）
                var infoFound = false;
                AssetInfo assetInfo = default;
                if (Guid.TryParse(pathArg, out var guid))
                    infoFound = Content.GetAssetInfo(guid, out assetInfo);
                else
                    infoFound = Content.GetAssetInfo(pathArg, out assetInfo);

                if (!infoFound)
                    warnings.Add("内容数据库中未找到该资产（可能未导入或路径写法不同），尝试直接加载");

                var asset = Guid.TryParse(pathArg, out var loadGuid)
                    ? Content.Load(loadGuid, timeoutMs)
                    : Content.Load(pathArg, timeoutMs);

                if (asset == null)
                {
                    return new
                    {
                        path = pathArg,
                        foundInDatabase = infoFound,
                        assetId = infoFound ? assetInfo.ID.ToString() : null,
                        typeName = infoFound ? assetInfo.TypeName : null,
                        loaded = false,
                        warnings = warnings.Concat(new[] { "资产加载失败或不存在" }).ToList()
                    };
                }

                var result = new Dictionary<string, object>
                {
                    { "path", asset.Path ?? pathArg },
                    { "foundInDatabase", infoFound },
                    { "assetId", asset.ID.ToString() },
                    { "typeName", asset.GetType().Name },
                    { "loaded", asset.IsLoaded },
                    { "memoryUsageBytes", asset.MemoryUsage }
                };

                var model = asset as Model;
                if (model == null)
                {
                    warnings.Add("该资产不是 Model 类型（" + asset.GetType().Name + "），只返回通用信息");
                    result["warnings"] = warnings;
                    return result;
                }

                var lods = model.LODs;
                var lodSummaries = new List<object>();
                if (lods != null)
                {
                    for (var i = 0; i < lods.Length; i++)
                    {
                        var lod = lods[i];
                        if (lod == null)
                            continue;
                        lodSummaries.Add(new
                        {
                            index = i,
                            vertexCount = lod.VertexCount,
                            meshCount = lod.Meshes != null ? lod.Meshes.Length : 0
                        });
                    }
                }
                result["lodCount"] = lodSummaries.Count;
                result["lods"] = lodSummaries;

                // 局部包围盒：判断模型尺度与朝向的确定依据
                if (lods != null && lods.Length > 0)
                {
                    var index = Math.Clamp(lodIndex, 0, lods.Length - 1);
                    try
                    {
                        var box = model.GetBox(index);
                        var size = box.Size;
                        result["bounds"] = new
                        {
                            lodIndex = index,
                            minimum = new { x = box.Minimum.X, y = box.Minimum.Y, z = box.Minimum.Z },
                            maximum = new { x = box.Maximum.X, y = box.Maximum.Y, z = box.Maximum.Z },
                            center = new { x = box.Center.X, y = box.Center.Y, z = box.Center.Z },
                            size = new { x = size.X, y = size.Y, z = size.Z }
                        };

                        var maxAxis = Math.Max(size.X, Math.Max(size.Y, size.Z));
                        if (maxAxis <= 0.0001f)
                            warnings.Add("包围盒几乎为零（最长边 " + maxAxis + "），模型可能是空网格或导入失败");
                        else if (maxAxis > 1000f)
                            warnings.Add("包围盒过大（最长边 " + maxAxis + "），通常意味着导入 Scale 设错或单位不一致");
                        else if (maxAxis < 0.01f)
                            warnings.Add("包围盒过小（最长边 " + maxAxis + "），通常意味着导入 Scale 被写成极小值");
                    }
                    catch (Exception ex)
                    {
                        warnings.Add("读取包围盒失败: " + ex.Message);
                    }
                }

                var skinned = model as SkinnedModel;
                if (skinned != null)
                {
                    var bones = skinned.Bones;
                    var nodes = skinned.Nodes;
                    var blendShapes = skinned.BlendShapes;

                    result["boneCount"] = bones != null ? bones.Length : 0;
                    result["nodeCount"] = nodes != null ? nodes.Length : 0;
                    result["blendShapeCount"] = blendShapes != null ? blendShapes.Length : 0;

                    if (includeBones)
                    {
                        if (nodes != null && nodes.Length > 0)
                        {
                            result["nodeNames"] = nodes
                                .Take(nameLimit)
                                .Where(n => n.Name != null)
                                .Select(n => n.Name)
                                .ToList();
                        }
                        if (bones != null && bones.Length > 0)
                        {
                            var boneNames = new List<string>();
                            foreach (var bone in bones.Take(nameLimit))
                            {
                                if (nodes != null && bone.NodeIndex >= 0 && bone.NodeIndex < nodes.Length)
                                    boneNames.Add(nodes[bone.NodeIndex].Name);
                            }
                            result["boneNames"] = boneNames;
                        }
                    }
                }

                result["warnings"] = warnings;
                return result;
            });
        }
    }
}
