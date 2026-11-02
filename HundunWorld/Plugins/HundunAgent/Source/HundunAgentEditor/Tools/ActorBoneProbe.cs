using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 只读探针：对运行中的真实 Actor 递归找 AnimatedModel，隔一段真实时间采同一批骨骼的本地矩阵，
    /// 报告是否变化。等待用 Task.Delay 走真实时间，Actor 就是场景里正在跑的那一个。
    /// 2026-10-22 对 LocalPlayer_44 实测 1.201 s 内 DEF-thigh.L / DEF-spine.002 / DEF-upper_arm.L 全部 0 位移
    /// ⇒ 世界角色确实停在静态姿态（T-pose），不是截图偶然。
    /// 注意：曾试过把虚拟剪辑图临时挂到真实 Actor 上，结果卡死主线程（MCP 全员超时），已移除；
    /// 不要再在实时角色上热换 AnimationGraph。
    /// </summary>
    public static class ActorBoneProbe
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_bone_probe",
                Description = "对运行中的真实 Actor 递归找 AnimatedModel，隔 waitMs 真实时间采同一批骨骼矩阵并报告变化。只读，不 spawn、不改资产。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\"},\"bones\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"waitMs\":{\"type\":\"integer\"}},\"required\":[\"actor\"]}",
                Execute = ExecuteAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_skeleton_unit_probe",
                Description = "只读：按节点索引读整副骨架，逐节点比较「当前局部平移 vs 绑定姿态平移」，报告哪些节点的平移被动画改写及比值（判定米/厘米口径），并隔 waitMs 采两次报告真正在动的节点。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\"},\"waitMs\":{\"type\":\"integer\"},\"samples\":{\"type\":\"integer\"}},\"required\":[\"actor\"]}",
                Execute = ExecuteUnitProbeAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "anim_graph_param_get",
                Description = "只读：读运行中 AnimatedModel 的动画图参数当前值（按参数名）。用于核对驱动端是否真的把参数写进实例。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\"},\"params\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"actor\"]}",
                Execute = ExecuteParamGetAsync
            });
        }

        private static async Task<object> ExecuteParamGetAsync(JsonElement args)
        {
            string name = EditorUtils.GetString(args, "actor");
            var names = EditorUtils.GetStringArray(args, "params");
            if (names.Count == 0)
            {
                names.Add("LocomotionWalk");
                names.Add("LocomotionRun");
                names.Add("LocomotionSprint");
                names.Add("TurnLeft");
                names.Add("TurnRight");
            }

            Actor actor = null;
            var models = new List<AnimatedModel>();
            await MainThread.InvokeAsync(() =>
            {
                actor = FindActorInScenes(name);
                if (actor != null)
                {
                    CollectModels(actor, models);
                }
            });
            if (actor == null)
            {
                throw new ArgumentException("Actor 不存在: " + name);
            }

            var rows = new List<object>();
            await MainThread.InvokeAsync(() =>
            {
                for (int m = 0; m < models.Count; m++)
                {
                    AnimatedModel model = models[m];
                    if (model == null || model.AnimationGraph == null)
                    {
                        continue;
                    }
                    var values = new List<object>();
                    for (int i = 0; i < names.Count; i++)
                    {
                        object value = null;
                        try
                        {
                            AnimGraphParameter p = model.GetParameter(names[i]);
                            value = p != null ? p.Value : null;
                        }
                        catch (Exception ex)
                        {
                            value = "<err:" + ex.GetType().Name + ">";
                        }
                        values.Add(new { name = names[i], value });
                    }
                    rows.Add(new { model = model.Name, graph = model.AnimationGraph.Path, values });
                }
            });

            return new { actor = actor.Name, modelCount = models.Count, rows };
        }

        private static async Task<object> ExecuteAsync(JsonElement args)
        {
            string name = EditorUtils.GetString(args, "actor");
            int waitMs = Math.Clamp(EditorUtils.GetInt(args, "waitMs", 800), 50, 10000);
            var bones = EditorUtils.GetStringArray(args, "bones");
            if (bones.Count == 0)
            {
                bones.Add("pelvis");
                bones.Add("spine_01");
                bones.Add("thigh_l");
            }

            Actor actor = null;
            var models = new List<AnimatedModel>();
            await MainThread.InvokeAsync(() =>
            {
                actor = FindActorInScenes(name);
                if (actor != null)
                {
                    CollectModels(actor, models);
                }
            });

            if (actor == null)
            {
                throw new ArgumentException("Actor 不存在: " + name);
            }
            if (models.Count == 0)
            {
                throw new ArgumentException("Actor 下没有 AnimatedModel: " + name);
            }

            var before = new Dictionary<string, Matrix>();
            await MainThread.InvokeAsync(() =>
            {
                for (int m = 0; m < models.Count; m++)
                {
                    if (!models[m])
                    {
                        continue;
                    }
                    for (int b = 0; b < bones.Count; b++)
                    {
                        int nodeIndex = FindNodeIndex(models[m], bones[b]);
                        Matrix mat = Matrix.Identity;
                        if (nodeIndex >= 0)
                        {
                            models[m].GetNodeTransformation(nodeIndex, out mat, false);
                        }
                        before[m + "|" + bones[b]] = mat;
                    }
                }
            });

            DateTime t0 = DateTime.UtcNow;
            await Task.Delay(waitMs);
            DateTime t1 = DateTime.UtcNow;

            var rows = new List<object>();
            var modelRows = new List<object>();
            var missing = new List<string>();
            int movedCount = 0;
            await MainThread.InvokeAsync(() =>
            {
                for (int m = 0; m < models.Count; m++)
                {
                    if (!models[m])
                    {
                        continue;
                    }

                    // 只读体检：把"这具模型到底是谁、有没有图、是不是激活"一并带回来。
                    // 排障时靠它把"看到的是占位壳"与"写回端在改另一具模型"分开。
                    modelRows.Add(new
                    {
                        name = models[m].Name,
                        parent = models[m].Parent != null ? models[m].Parent.Name : null,
                        active = models[m].IsActive,
                        updateMode = models[m].UpdateMode.ToString(),
                        updateWhenOffscreen = models[m].UpdateWhenOffscreen,
                        skinnedModel = models[m].SkinnedModel != null ? models[m].SkinnedModel.Path : null,
                        animationGraph = models[m].AnimationGraph != null ? models[m].AnimationGraph.Path : null,
                    });

                    for (int b = 0; b < bones.Count; b++)
                    {
                        int nodeIndex = FindNodeIndex(models[m], bones[b]);
                        bool found = nodeIndex >= 0;
                        Matrix mat = Matrix.Identity;
                        if (found)
                        {
                            models[m].GetNodeTransformation(nodeIndex, out mat, false);
                        }
                        else
                        {
                            missing.Add(models[m].Name + ":" + bones[b]);
                        }
                        if (!before.TryGetValue(m + "|" + bones[b], out Matrix bm))
                        {
                            continue;
                        }
                        float diff = Math.Max(
                            Math.Max(Math.Max(Math.Abs(mat.M11 - bm.M11), Math.Abs(mat.M12 - bm.M12)), Math.Abs(mat.M13 - bm.M13)),
                            Math.Max(Math.Max(Math.Abs(mat.M21 - bm.M21), Math.Abs(mat.M22 - bm.M22)), Math.Abs(mat.M23 - bm.M23)));
                        Float3 td = mat.TranslationVector - bm.TranslationVector;
                        diff = Math.Max(diff, Math.Max(Math.Abs(td.X), Math.Max(Math.Abs(td.Y), Math.Abs(td.Z))));
                        bool moved = found && diff > 0.001f;
                        if (moved)
                        {
                            movedCount++;
                        }

                        rows.Add(new
                        {
                            model = models[m].Name,
                            bone = bones[b],
                            found,
                            nodeIndex,
                            maxMatrixDelta = diff,
                            moved,
                            before = new { x = bm.TranslationVector.X, y = bm.TranslationVector.Y, z = bm.TranslationVector.Z },
                            after = new { x = mat.TranslationVector.X, y = mat.TranslationVector.Y, z = mat.TranslationVector.Z }
                        });
                    }
                }
            });

            return new
            {
                actor = actor.Name,
                elapsedSeconds = Math.Round((t1 - t0).TotalSeconds, 3),
                modelCount = models.Count,
                models = modelRows,
                missingBones = missing,
                movedCount,
                rows
            };
        }

        /// <summary>
        /// 按节点索引读整副骨架：把姿态矩阵换算成「相对父节点的局部平移」，与绑定姿态逐节点比对。
        /// 这是判定「米制剪辑打在厘米骨架上」的尺子——名字查不到会静默返回单位阵，所以这里只用索引。
        /// </summary>
        private static async Task<object> ExecuteUnitProbeAsync(JsonElement args)
        {
            string name = EditorUtils.GetString(args, "actor");
            int waitMs = Math.Clamp(EditorUtils.GetInt(args, "waitMs", 900), 50, 10000);
            int sampleLimit = Math.Clamp(EditorUtils.GetInt(args, "samples", 12), 1, 60);

            Actor actor = null;
            var models = new List<AnimatedModel>();
            await MainThread.InvokeAsync(() =>
            {
                actor = FindActorInScenes(name);
                if (actor != null)
                {
                    CollectModels(actor, models);
                }
            });
            if (actor == null)
            {
                throw new ArgumentException("Actor 不存在: " + name);
            }
            if (models.Count == 0)
            {
                throw new ArgumentException("Actor 下没有 AnimatedModel: " + name);
            }

            var snapshots = new List<Matrix[]>();
            var modelRows = new List<object>();
            await MainThread.InvokeAsync(() =>
            {
                for (int m = 0; m < models.Count; m++)
                {
                    AnimatedModel model = models[m];
                    var nodes = model.SkinnedModel != null ? model.SkinnedModel.Nodes : null;
                    int nodeCount = nodes != null ? nodes.Length : 0;
                    var pose = new Matrix[nodeCount];
                    for (int i = 0; i < nodeCount; i++)
                    {
                        model.GetNodeTransformation(i, out pose[i], false);
                    }
                    snapshots.Add(pose);

                    int animatedCount = 0;
                    var samples = new List<object>();
                    for (int i = 0; i < nodeCount; i++)
                    {
                        Vector3 rest = nodes[i].LocalTransform.Translation;
                        Vector3 local = LocalTranslation(pose, nodes, i);
                        if ((local - rest).Length <= 0.01f)
                        {
                            continue;
                        }
                        animatedCount++;
                        if (samples.Count < sampleLimit)
                        {
                            float restLen = rest.Length;
                            float curLen = local.Length;
                            samples.Add(new
                            {
                                index = i,
                                name = nodes[i].Name,
                                rest = new { x = rest.X, y = rest.Y, z = rest.Z },
                                current = new { x = local.X, y = local.Y, z = local.Z },
                                ratio = (curLen > 1e-4f && restLen > 1e-4f) ? (double?)(restLen / curLen) : null
                            });
                        }
                    }

                    modelRows.Add(new
                    {
                        name = model.Name,
                        parent = model.Parent != null ? model.Parent.Name : null,
                        skinnedModel = model.SkinnedModel != null ? model.SkinnedModel.Path : null,
                        animationGraph = model.AnimationGraph != null ? model.AnimationGraph.Path : null,
                        nodeCount,
                        animatedTranslationCount = animatedCount,
                        samples
                    });
                }
            });

            DateTime t0 = DateTime.UtcNow;
            await Task.Delay(waitMs);
            DateTime t1 = DateTime.UtcNow;

            var movingRows = new List<object>();
            int totalMoving = 0;
            await MainThread.InvokeAsync(() =>
            {
                for (int m = 0; m < models.Count; m++)
                {
                    AnimatedModel model = models[m];
                    var nodes = model.SkinnedModel != null ? model.SkinnedModel.Nodes : null;
                    if (nodes == null || m >= snapshots.Count)
                    {
                        continue;
                    }
                    int nodeCount = nodes.Length;
                    int moving = 0;
                    var top = new List<object>();
                    for (int i = 0; i < nodeCount; i++)
                    {
                        Matrix now;
                        model.GetNodeTransformation(i, out now, false);
                        Float3 d = now.TranslationVector - snapshots[m][i].TranslationVector;
                        float delta = Math.Max(Math.Abs(d.X), Math.Max(Math.Abs(d.Y), Math.Abs(d.Z)));
                        if (delta <= 0.01f)
                        {
                            continue;
                        }
                        moving++;
                        if (top.Count < sampleLimit)
                        {
                            top.Add(new { index = i, name = nodes[i].Name, delta });
                        }
                    }
                    totalMoving += moving;
                    movingRows.Add(new { name = model.Name, movingCount = moving, top });
                }
            });

            return new
            {
                actor = actor.Name,
                elapsedSeconds = Math.Round((t1 - t0).TotalSeconds, 3),
                modelCount = models.Count,
                models = modelRows,
                movingTotal = totalMoving,
                moving = movingRows
            };
        }

        private static Vector3 LocalTranslation(Matrix[] pose, SkeletonNode[] nodes, int index)
        {
            int parent = nodes[index].ParentIndex;
            if (parent < 0 || parent >= pose.Length)
            {
                return pose[index].TranslationVector;
            }
            Matrix local = Matrix.Invert(pose[parent]) * pose[index];
            return local.TranslationVector;
        }

        private static int FindNodeIndex(AnimatedModel model, string bone)
        {
            if (model.SkinnedModel == null)
            {
                return -1;
            }
            var nodes = model.SkinnedModel.Nodes;
            if (nodes == null)
            {
                return -1;
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                if (string.Equals(nodes[i].Name, bone, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private static Actor FindActorInScenes(string name)
        {
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                Scene scene = Level.GetScene(i);
                if (scene == null)
                {
                    continue;
                }
                Actor hit = FindActor(scene, name);
                if (hit != null)
                {
                    return hit;
                }
            }
            return null;
        }

        private static Actor FindActor(Actor root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            var children = root.Children;
            if (children == null)
            {
                return null;
            }
            for (int i = 0; i < children.Length; i++)
            {
                Actor hit = FindActor(children[i], name);
                if (hit != null)
                {
                    return hit;
                }
            }
            return null;
        }

        private static void CollectModels(Actor root, List<AnimatedModel> models)
        {
            if (root == null)
            {
                return;
            }
            if (root is AnimatedModel model && model)
            {
                models.Add(model);
            }

            var children = root.Children;
            if (children == null)
            {
                return;
            }
            for (int i = 0; i < children.Length; i++)
            {
                CollectModels(children[i], models);
            }
        }
    }
}
