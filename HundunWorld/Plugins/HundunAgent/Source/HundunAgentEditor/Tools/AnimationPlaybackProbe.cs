using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEditor;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 只读探针：在编辑器里给指定 SkinnedModel 挂 AnimationGraph，隔若干帧采样骨骼矩阵，
    /// 回答"动画到底有没有驱动这具骨架"（含 SkeletonRetarget 是否真的生效）。
    /// 临时 Actor 用完即删，不写资产。
    /// </summary>
    public static class AnimationPlaybackProbe
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "animation_playback_probe",
                Description = "在编辑器里临时挂载 SkinnedModel + AnimationGraph，等待若干帧后比较若干骨骼的矩阵，" +
                              "报告动画是否真的驱动了骨架（用于验证 SkeletonRetarget）。只读、自动清理。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"model\":{\"type\":\"string\"}," +
                                  "\"graph\":{\"type\":\"string\"}," +
                                  "\"animation\":{\"type\":\"string\",\"description\":\"直接播的 Animation 资产（绕开状态机）\"}," +
                                  "\"bones\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"要采样的骨骼名\"}," +
                                  "\"frames\":{\"type\":\"integer\",\"description\":\"两次采样之间等多少帧，默认 30\"}}," +
                                  "\"required\":[\"model\"]}",
                Execute = ProbeAsync
            });

        }

        private static async Task<object> ProbeAsync(JsonElement args)
        {
            string modelPath = Normalize(EditorUtils.GetString(args, "model"));
            string graphPath = Normalize(EditorUtils.GetString(args, "graph"));
            string animPath = Normalize(EditorUtils.GetString(args, "animation"));
            int frames = Math.Clamp(EditorUtils.GetInt(args, "frames", 30), 1, 600);
            var bones = EditorUtils.GetStringArray(args, "bones");
            if (bones.Count == 0)
            {
                bones.Add("DEF-spine.006");
                bones.Add("DEF-spine.002");
                bones.Add("DEF-upper_arm.L");
                bones.Add("DEF-thigh.L");
            }

            SkinnedModel model = null;
            AnimationGraph graph = null;
            Animation animation = null;
            bool initFailed = false;
            await MainThread.InvokeAsync(() =>
            {
                model = Content.Load<SkinnedModel>(modelPath, 15000.0);
                if (model == null || !model.IsLoaded)
                    throw new ArgumentException("模型加载失败: " + modelPath);
                if (!string.IsNullOrEmpty(graphPath))
                {
                    graph = Content.Load<AnimationGraph>(graphPath, 15000.0);
                    if (graph == null || !graph.IsLoaded)
                        throw new ArgumentException("动画图加载失败: " + graphPath);
                }
                if (!string.IsNullOrEmpty(animPath))
                {
                    animation = Content.Load<Animation>(animPath, 15000.0);
                    if (animation == null || !animation.IsLoaded)
                        throw new ArgumentException("动画加载失败: " + animPath);
                    // 虚拟动画图：单剪辑直接播放（官方 InitAsAnimation 通路；返回 true = 失败）
                    graph = Content.CreateVirtualAsset<AnimationGraph>();
                    initFailed = graph.InitAsAnimation(model, animation, true, false);
                }
            });

            AnimatedModel actor = null;
            Scene scene = null;
            var before = new Dictionary<string, Matrix>();
            await MainThread.InvokeAsync(() =>
            {
                scene = FindScene();
                actor = new AnimatedModel { Name = "AnimPlaybackProbe" };
                Level.SpawnActor(actor, scene);
                actor.UpdateWhenOffscreen = true;
                actor.PerBoneMotionBlur = false;
                actor.IsActive = true;
                actor.SkinnedModel = model;
                actor.AnimationGraph = graph;
                actor.SetupSkinningData();
                actor.ResetAnimation();
                actor.UpdateAnimation();
            });

            // 等姿态稳定
            await MainThread.InvokeAsync(() => { });
            await MainThread.InvokeAsync(() => { });

            await MainThread.InvokeAsync(() =>
            {
                foreach (var b in bones)
                {
                    actor.GetNodeTransformation(b, out Matrix m, false);
                    before[b] = m;
                }
            });

            // 等帧必须是**真实时间**：原先只排主线程派发，几十个空回调可能在同一帧内跑完，
            // 动画时间根本没前进 ⇒ 任何剪辑都报 movingBones=0（2026-10-22 实测假阴性）。
            // 每帧再显式 UpdateAnimation，确保虚拟图/剪辑在编辑器态也会求值。
            double startTime = FlaxEngine.Time.GameTime;
            for (int i = 0; i < frames; i++)
            {
                await System.Threading.Tasks.Task.Delay(16);
                await MainThread.InvokeAsync(() =>
                {
                    if (actor != null) actor.UpdateAnimation();
                });
            }

            var rows = new List<object>();
            int moving = 0;
            await MainThread.InvokeAsync(() =>
            {
                foreach (var b in bones)
                {
                    Matrix m;
                    actor.GetNodeTransformation(b, out m, false);
                    Matrix bm = before[b];
                    float diff = Math.Max(
                        Math.Max(Math.Max(Math.Abs(m.M11 - bm.M11), Math.Abs(m.M12 - bm.M12)), Math.Abs(m.M13 - bm.M13)),
                        Math.Max(Math.Max(Math.Abs(m.M21 - bm.M21), Math.Abs(m.M22 - bm.M22)), Math.Abs(m.M23 - bm.M23)));
                    Float3 td = m.TranslationVector - bm.TranslationVector;
                    diff = Math.Max(diff, Math.Max(Math.Abs(td.X), Math.Max(Math.Abs(td.Y), Math.Abs(td.Z))));
                    bool moved = diff > 0.001f;
                    if (moved)
                    {
                        moving++;
                    }
                    rows.Add(new
                    {
                        bone = b,
                        found = bm.M11 != 0f || bm.M44 != 0f,
                        before = Vec(bm.TranslationVector),
                        after = Vec(m.TranslationVector),
                        maxMatrixDelta = diff,
                        moved
                    });
                }
            });

            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var node = editor.Scene.GetActorNode(actor);
                if (node != null)
                {
                    editor.SceneEditing.Select(node, false);
                    editor.SceneEditing.Delete();
                }
            });

            return new
            {
                model = modelPath,
                graph = graphPath,
                animation = animPath,
                initFailed,
                frames,
                // 自证：这段时间必须真的推进，否则 movingBones=0 只是"时间没走"。
                elapsedSeconds = Math.Round(FlaxEngine.Time.GameTime - startTime, 3),
                // 剪辑自身有多少内容：通道/关键帧为 0 才能说"剪辑是空的"，否则 0 位移是"轨道绑不上骨架"。
                animChannels = animation != null ? animation.Info.ChannelsCount : -1,
                animKeyframes = animation != null ? animation.Info.KeyframesCount : -1,
                animFrames = animation != null ? animation.Info.FramesCount : -1,
                animLength = animation != null ? animation.Info.Length : -1f,
                graphLoaded = graph != null,
                movingBones = moving,
                bones = rows
            };
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

        private static object Vec(Float3 v) => new { x = v.X, y = v.Y, z = v.Z };

        private static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            string path = raw.Replace('\\', '/');
            int idx = path.IndexOf("Content/", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? path.Substring(idx) : path;
        }
    }
}
