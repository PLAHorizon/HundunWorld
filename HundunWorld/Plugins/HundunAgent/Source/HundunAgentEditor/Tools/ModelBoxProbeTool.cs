using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEditor;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 量模型资产的**世界包围盒**（center/size），用来回答"两具模型的尺度/位置差多少"这类问题。
    ///
    /// 为什么不用现成的 `model_inspect`：它走**编辑器内容数据库**查资产，本会话实测对
    /// `Character_1.flax` / `Ada_FaceMesh_slotpacked.flax` 都回"不在编辑器内容数据库中"（已知问题），
    /// 于是连 LOD 包围盒都拿不到。本工具改走 `Content.Load` + 临时 AnimatedModel + `Actor.Box`，
    /// 与游戏里预览那条路同源（PIE 里这两具模型都加载成功过）。
    ///
    /// 用法：`model_box_probe { "paths": ["a.flax","b.flax"] }`。
    /// 回执逐条给 center/size/体积占比，并**删除临时 Actor**（不留场景残留）。
    /// </summary>
    public static class ModelBoxProbeTool
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "model_box_probe",
                Description = "量模型资产的世界包围盒（center/size）。参数 paths 为资产路径数组。"
                    + "走 Content.Load + 临时 AnimatedModel + Actor.Box（绕开内容数据库查找失效）。只读、自动清理。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"paths\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"paths\"]}",
                Execute = ProbeAsync
            });
        }

        private static readonly string[] HeadBones =
            { "DEF-spine.006", "head", "bip001 head", "mixamorig:head" };

        private static readonly string[] NeckBones =
            { "DEF-spine.005", "neck", "neck_01", "bip001 neck", "mixamorig:neck" };

        /// <summary>在模型的节点名里按候选顺序找第一个存在的（大小写不敏感；与预览组件同口径）。</summary>
        private static string FindBoneName(SkinnedModel model, string[] candidates)
        {
            if (model == null || model.Nodes == null)
                return null;
            for (int c = 0; c < candidates.Length; c++)
            {
                for (int i = 0; i < model.Nodes.Length; i++)
                {
                    var n = model.Nodes[i].Name;
                    if (!string.IsNullOrEmpty(n) && string.Equals(n, candidates[c], StringComparison.OrdinalIgnoreCase))
                        return n;
                }
            }
            return null;
        }

        private static async Task<object> ProbeAsync(JsonElement args)
        {
            JsonElement pathsEl;
            if (!args.TryGetProperty("paths", out pathsEl) || pathsEl.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("缺少 paths 数组");

            var paths = new List<string>();
            foreach (JsonElement p in pathsEl.EnumerateArray())
            {
                if (p.ValueKind == JsonValueKind.String)
                {
                    var s = p.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        paths.Add(s.Replace('\\', '/'));
                }
            }
            if (paths.Count == 0)
                throw new ArgumentException("paths 里没有有效条目");

            // 可选：先让内容库按磁盘重建。为什么需要：实测 `Ada_BodyMesh.flax` / `Ada_FaceMesh.flax`
            // 报 `Cannot find asset '<旧ID>' within Asset '<路径>'` + `Cannot load asset header.`
            // —— 那是**注册表里的 ID 与文件头 ID 不一致**（资产被重新导入过、内容库没刷新）。
            // 重建会把 ID 按磁盘重新读一遍；成功与否看回执里的 loadErrors 有没有变空。
            if (args.TryGetProperty("rebuildDatabase", out var rb) && rb.ValueKind == JsonValueKind.True)
            {
                await MainThread.InvokeAsync(() => FlaxEditor.Editor.Instance.ContentDatabase.Rebuild(true));
            }

            var actors = new List<AnimatedModel>();
            var loaded = new List<string>();

            // ① 逐条加载并建临时 Actor
            var loadErrors = new List<string>();
            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                Scene scene = null;
                for (int i = 0; i < Level.ScenesCount; i++)
                {
                    var s = Level.GetScene(i);
                    if (s != null && s.Name != "TransitionScene")
                    {
                        scene = s;
                        break;
                    }
                }
                if (scene == null)
                    throw new InvalidOperationException("没有可用场景");

                for (int i = 0; i < paths.Count; i++)
                {
                    var model = Content.Load<SkinnedModel>(paths[i], 10000.0);
                    if (model == null || !model.IsLoaded)
                    {
                        loadErrors.Add(paths[i] + " 加载失败或未就绪");
                        continue;
                    }

                    var actor = new AnimatedModel { Name = "BoxProbe_" + i };
                    Level.SpawnActor(actor, scene);
                    actor.SkinnedModel = model;
                    actor.UpdateWhenOffscreen = true;
                    actor.IsActive = true;
                    try
                    {
                        actor.SetupSkinningData();
                        actor.ResetAnimation();
                        actor.UpdateAnimation();
                    }
                    catch (Exception ex)
                    {
                        loadErrors.Add(paths[i] + " 刷新失败: " + ex.Message);
                    }

                    actors.Add(actor);
                    loaded.Add(paths[i]);
                }
            });

            // ② 下一帧再读 Box（刚指定模型那一帧包围盒还没更新）
            await MainThread.InvokeAsync(() => { });

            var rows = new List<object>();
            await MainThread.InvokeAsync(() =>
            {
                for (int i = 0; i < actors.Count; i++)
                {
                    var box = actors[i].Box;
                    // 顺带把头骨/颈骨也量出来：创角预览那条"按骨骼对齐头"的修法靠这两个数判对错，
                    // 而预览要登录才进得去 —— 这里量出来就不用等业务流程。
                    string headBone = FindBoneName(actors[i].SkinnedModel, HeadBones);
                    string neckBone = FindBoneName(actors[i].SkinnedModel, NeckBones);
                    Float3 headPos = Float3.Zero, neckPos = Float3.Zero;
                    bool headOk = false, neckOk = false;
                    if (headBone != null)
                    {
                        Matrix m;
                        actors[i].GetNodeTransformation(headBone, out m, false);
                        headPos = m.TranslationVector;
                        headOk = true;
                    }
                    if (neckBone != null)
                    {
                        Matrix m;
                        actors[i].GetNodeTransformation(neckBone, out m, false);
                        neckPos = m.TranslationVector;
                        neckOk = true;
                    }

                    rows.Add(new
                    {
                        path = loaded[i],
                        center = new { x = box.Center.X, y = box.Center.Y, z = box.Center.Z },
                        size = new { x = box.Size.X, y = box.Size.Y, z = box.Size.Z },
                        scale = new { x = actors[i].Scale.X, y = actors[i].Scale.Y, z = actors[i].Scale.Z },
                        slots = actors[i].MaterialSlots != null ? actors[i].MaterialSlots.Length : 0,
                        slot0 = actors[i].GetMaterial(0) != null ? actors[i].GetMaterial(0).Path : null,
                        headBone,
                        headPos = headOk ? new { x = headPos.X, y = headPos.Y, z = headPos.Z } : null,
                        neckBone,
                        neckPos = neckOk ? new { x = neckPos.X, y = neckPos.Y, z = neckPos.Z } : null,
                    });
                }
            });

            // ③ 清理临时 Actor（走编辑器自己的删除路径：场景树节点 + SceneEditing.Delete，
            //    与 HudunAgent 的 actor_delete 同一套 API；Actor 上没有 DeleteObject）
            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                for (int i = 0; i < actors.Count; i++)
                {
                    var actor = actors[i];
                    if (actor == null)
                        continue;
                    var node = editor.Scene.GetActorNode(actor);
                    if (node == null)
                        continue;
                    editor.SceneEditing.Select(node, false);
                    editor.SceneEditing.Delete();
                }
            });

            return new { count = rows.Count, loadErrors, boxes = rows };
        }
    }
}
