using System;
using System.Collections.Generic;

using System.Text.Json;
using System.Threading.Tasks;
using FlaxEditor;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 蒙皮顶点属性探针：回答"这具 SkinnedModel 的网格到底有没有把 BlendIndices/BlendWeights 导进来"，
    /// 以及"在这具资产上摆骨骼姿势到底会不会改变几何/包围盒"。
    /// <para>
    /// 存在的理由（2026-10-19）：捏脸骨骼姿势写进 GraphInstance.NodesPose 且读得回、引擎自己也从同一数组
    /// 算蒙皮矩阵（AnimatedModel::OnAnimationUpdated_Async），但像素在 ×2000 增益下也纹丝不动；
    /// 同时 blendshape 通道能改像素。要先把"属性没导入"与"摆姿势这条路不生效"分开。
    /// </para>
    /// </summary>
    public static class ModelSkinProbeTool
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "model_skin_probe",
                Description = "只读（可选驱动）：读 SkinnedModel 的 LOD0 网格顶点布局，报告是否存在 BlendIndices / BlendWeights 元素及其偏移；" +
                              "带 drive=true 时临时建一个 AnimatedModel、把指定骨骼平移一段，回报骨骼矩阵读回与包围盒变化——" +
                              "用来判定『骨骼姿势写了但蒙皮不跟随』是属性没导入，还是摆姿势这条路本身不生效。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"path\":{\"type\":\"string\",\"description\":\"资产路径或 Guid\"}," +
                                  "\"lod\":{\"type\":\"integer\",\"description\":\"LOD 序号，默认 0\"}," +
                                  "\"drive\":{\"type\":\"boolean\",\"description\":\"true 才做驱动试验（默认 false 只读布局）\"}," +
                                  "\"bones\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"要试的节点名；缺省 head + 第一根 FACIAL_*\"}," +
                                  "\"offset\":{\"type\":\"number\",\"description\":\"沿 Y 平移多少厘米，默认 50\"}}}",
                Execute = ProbeAsync
            });
        }

        private static async Task<object> ProbeAsync(JsonElement args)
        {
            string raw = EditorUtils.GetString(args, "path");
            int lodIndex = Math.Max(0, EditorUtils.GetInt(args, "lod", 0));
            bool drive = EditorUtils.GetBool(args, "drive", false);
            float offset = (float)EditorUtils.GetDouble(args, "offset", 50.0);
            List<string> wanted = EditorUtils.GetStringArray(args, "bones");

            string path = Normalize(raw);

            SkinnedModel model = null;
            var layout = new Dictionary<string, object>();
            await MainThread.InvokeAsync(() =>
            {
                model = Content.Load<SkinnedModel>(path, 10000.0f);
                if (model == null || !model.IsLoaded)
                {
                    Content.GetAssetInfo(path, out AssetInfo info);
                    if (info.ID != Guid.Empty)
                    {
                        model = Content.Load<SkinnedModel>(info.ID, 10000.0f);
                    }
                }

                if (model == null || !model.IsLoaded)
                {
                    throw new InvalidOperationException("载入不了 SkinnedModel: " + path);
                }

                var lods = model.LODs;
                if (lods == null || lods.Length == 0)
                {
                    throw new InvalidOperationException("该资产没有 LOD: " + path);
                }

                int idx = Math.Clamp(lodIndex, 0, lods.Length - 1);
                // 用 model.GetMeshes 而不是 lods[idx].Meshes：实测后者在资产重载后会返回 0 顶点
                // 的空壳（同一资产 model_inspect 走 GetMeshes 读到 24408），会把"有几何"误报成"没几何"。
                model.GetMeshes(out MeshBase[] meshes, idx);
                if (meshes == null)
                {
                    meshes = Array.Empty<MeshBase>();
                }
                var rows = new List<object>();
                int withSkin = 0;

                for (int i = 0; i < meshes.Length; i++)
                {
                    var mesh = meshes[i];
                    GPUVertexLayout vertexLayout = mesh.VertexLayout;
                    bool hasIndices = false;
                    bool hasWeights = false;
                    int indicesOffset = -1;
                    int weightsOffset = -1;
                    string indicesFormat = null;
                    string weightsFormat = null;

                    if (vertexLayout != null)
                    {
                        VertexElement bi = vertexLayout.FindElement(VertexElement.Types.BlendIndices);
                        VertexElement bw = vertexLayout.FindElement(VertexElement.Types.BlendWeights);
                        hasIndices = bi.Type == VertexElement.Types.BlendIndices;
                        hasWeights = bw.Type == VertexElement.Types.BlendWeights;
                        if (hasIndices)
                        {
                            indicesOffset = bi.Offset;
                            indicesFormat = bi.Format.ToString();
                        }
                        if (hasWeights)
                        {
                            weightsOffset = bw.Offset;
                            weightsFormat = bw.Format.ToString();
                        }
                    }

                    if (hasIndices && hasWeights)
                    {
                        withSkin++;
                    }

                    rows.Add(new
                    {
                        mesh = i,
                        vertices = mesh.VertexCount,
                        blendIndices = hasIndices,
                        blendWeights = hasWeights,
                        indicesOffset,
                        weightsOffset,
                        indicesFormat,
                        weightsFormat
                    });
                }

                layout["path"] = path;
                layout["type"] = model.GetType().Name;
                layout["lodCount"] = lods.Length;
                layout["lod"] = idx;
                layout["meshCount"] = meshes.Length;
                layout["meshesWithSkinning"] = withSkin;
                layout["nodeCount"] = model.Nodes != null ? model.Nodes.Length : 0;
                layout["meshes"] = rows;
            });

            if (!drive)
                return layout;

            if (model.Nodes == null || model.Nodes.Length == 0)
            {
                layout["drive"] = new { ok = false, reason = "资产没有骨架节点" };
                return layout;
            }

            // 选要驱动的节点：显式传入优先；否则 head + 第一根 FACIAL_*（脸颊类骨骼直接带着脸皮顶点）
            var targets = new List<KeyValuePair<int, string>>();
            void AddTarget(string name)
            {
                if (string.IsNullOrEmpty(name)) return;
                for (int i = 0; i < model.Nodes.Length; i++)
                {
                    if (string.Equals(model.Nodes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        targets.Add(new KeyValuePair<int, string>(i, model.Nodes[i].Name));
                        return;
                    }
                }
                targets.Add(new KeyValuePair<int, string>(-1, name));
            }

            if (wanted != null && wanted.Count > 0)
            {
                foreach (var name in wanted) AddTarget(name);
            }
            else
            {
                AddTarget("head");
                string facial = null;
                for (int i = 0; i < model.Nodes.Length && facial == null; i++)
                {
                    string n = model.Nodes[i].Name;
                    if (!string.IsNullOrEmpty(n) && n.StartsWith("FACIAL_", StringComparison.OrdinalIgnoreCase))
                        facial = n;
                }
                AddTarget(facial);
            }

            AnimatedModel actor = null;
            Scene scene = null;
            var baseline = new Dictionary<int, Matrix>();
            var boxBefore = BoundingBox.Zero;

            await MainThread.InvokeAsync(() =>
            {
                scene = FindScene();
                actor = new AnimatedModel { Name = "SkinDriveProbe" };
                Level.SpawnActor(actor, scene);
                actor.SkinnedModel = model;
                actor.UpdateWhenOffscreen = true;
                actor.IsActive = true;
                actor.Position = new Float3(0, 0, 0);
                actor.SetupSkinningData();
                actor.ResetAnimation();
                actor.UpdateAnimation();
            });

            // 等一帧：包围盒与蒙皮数据在更新后的一帧才稳定
            await MainThread.InvokeAsync(() => { });

            await MainThread.InvokeAsync(() =>
            {
                foreach (var t in targets)
                {
                    if (t.Key < 0) continue;
                    actor.GetNodeTransformation(t.Key, out Matrix m, false);
                    baseline[t.Key] = m;
                }
                boxBefore = actor.Box;
            });

            var results = new List<object>();
            foreach (var t in targets)
            {
                if (t.Key < 0)
                {
                    results.Add(new { bone = t.Value, found = false });
                    continue;
                }

                Matrix before = baseline.TryGetValue(t.Key, out Matrix b) ? b : Matrix.Identity;
                // 与生产写回端同一条乘法顺序：右乘平移（current * T）
                Matrix moved = before * Matrix.Translation(0, offset, 0);

                // 单骨写：只改这一根
                await MainThread.InvokeAsync(() =>
                {
                    actor.SetNodeTransformation(t.Key, moved, false);
                    actor.UpdateAnimation();
                });
                await MainThread.InvokeAsync(() => { });

                Matrix readback = Matrix.Identity;
                BoundingBox boxSingle = BoundingBox.Zero;
                await MainThread.InvokeAsync(() =>
                {
                    actor.GetNodeTransformation(t.Key, out readback, false);
                    boxSingle = actor.Box;
                });

                // 整姿批量写：与生产写回端同一条路（GetCurrentPose → 改 → SetCurrentPose）
                Matrix readbackBatch = Matrix.Identity;
                BoundingBox boxBatch = BoundingBox.Zero;
                await MainThread.InvokeAsync(() =>
                {
                    actor.GetCurrentPose(out Matrix[] pose, false);
                    if (pose != null && t.Key < pose.Length)
                    {
                        pose[t.Key] = before * Matrix.Translation(0, offset * 2, 0);
                        actor.SetCurrentPose(pose, false);
                        actor.UpdateAnimation();
                    }
                });
                await MainThread.InvokeAsync(() => { });

                await MainThread.InvokeAsync(() =>
                {
                    actor.GetNodeTransformation(t.Key, out readbackBatch, false);
                    boxBatch = actor.Box;
                });

                results.Add(new
                {
                    bone = t.Value,
                    index = t.Key,
                    found = true,
                    single = new
                    {
                        nodeMoved = Delta(before.TranslationVector, readback.TranslationVector),
                        boxDelta = Delta(boxBefore.Center, boxSingle.Center),
                        boxSizeBefore = Size(boxBefore.Size),
                        boxSizeAfter = Size(boxSingle.Size)
                    },
                    batch = new
                    {
                        nodeMoved = Delta(before.TranslationVector, readbackBatch.TranslationVector),
                        boxDelta = Delta(boxBefore.Center, boxBatch.Center),
                        boxSizeBefore = Size(boxBefore.Size),
                        boxSizeAfter = Size(boxBatch.Size)
                    }
                });

                // 复原这一次的写入，免得下一次测试从偏移状态开始
                await MainThread.InvokeAsync(() =>
                {
                    actor.SetNodeTransformation(t.Key, before, false);
                    actor.UpdateAnimation();
                });
                await MainThread.InvokeAsync(() => { });
            }

            await Cleanup(actor);

            layout["drive"] = new
            {
                ok = true,
                offsetY = offset,
                boxBefore = new { center = Vec(boxBefore.Center), size = Size(boxBefore.Size) },
                targets = results
            };
            return layout;
        }

        private static Scene FindScene()
        {
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var s = Level.GetScene(i);
                if (s != null && s.Name != "TransitionScene")
                    return s;
            }
            throw new InvalidOperationException("没有可用场景");
        }

        private static async Task Cleanup(AnimatedModel actor)
        {
            if (actor == null) return;
            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var node = editor.Scene.GetActorNode(actor);
                if (node == null) return;
                editor.SceneEditing.Select(node, false);
                editor.SceneEditing.Delete();
            });
        }

        private static object Vec(Float3 v) => new { x = v.X, y = v.Y, z = v.Z };
        private static object Size(Float3 v) => new { x = v.X, y = v.Y, z = v.Z };
        private static object Delta(Float3 a, Float3 b) => new { x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z };

        private static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                throw new ArgumentException("缺少 path");
            }

            string path = raw.Replace('\\', '/');
            int idx = path.IndexOf("Content/", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? path.Substring(idx) : path;
        }
    }
}
