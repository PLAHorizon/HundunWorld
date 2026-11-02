using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEditor.Content;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 用编辑器自己的**资产预览窗口**（SkinnedModelWindow 的 AssetPreview）做离屏像素取证。
    ///
    /// 为什么不用 game_screenshot / ui_preview_screenshot：前者两条引擎侧通路都不含 GUI，
    /// 后者依赖游戏内 CharacterPreview3D（需要登录与 UI 渲染）。资产预览窗口有**自己的 SceneRenderTask**
    /// （骨骼缓冲也是独立的），可以在不进入 PIE 的情况下渲染一个 SkinnedModel，并用
    /// Screenshot.Capture(task, path) 落盘。大骨架在编辑器主视口会撞 AnimatedModel.cpp:82，
    /// 这条通路把渲染放进预览任务里，避开共享缓冲。
    ///
    /// 只读：不改资产；morph 只改预览 Actor 的运行态权重。
    /// </summary>
    public static class ModelPreviewTools
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "model_asset_preview",
                Description = "打开/复用资产的编辑器预览窗口，可选设置一个 blend shape 权重，然后把预览的渲染结果截图落盘。"
                              + "用于不依赖 PIE/登录的像素取证（表情发暗、槽位颜色、头型对比）。"
                              + "**上限 ≈341 骨**：预览任务同样用共享骨骼缓冲且 Allocate 只翻一倍（#76），"
                              + "495 骨的头一开窗口就断言崩溃、编辑器直接退出；超过 341 骨会先被拒绝并说明原因。"
                              + "path=资产路径；morph/morphValue 可选；out 可选（相对路径按工程根解析）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                                  + "\"path\":{\"type\":\"string\"},"
                                  + "\"morph\":{\"type\":\"string\"},"
                                  + "\"morphValue\":{\"type\":\"number\"},"
                                  + "\"out\":{\"type\":\"string\"},"
                                  + "\"waitMs\":{\"type\":\"integer\"}},"
                                  + "\"required\":[\"path\"]}",
                Execute = CaptureAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "model_offscreen_render",
                Description = "用自建 SceneRenderTask 离屏渲染一个 SkinnedModel（可选 morph / 模型朝向），落盘 PNG。"
                              + "与 model_asset_preview 的区别：不打开资产窗口，镜头/灯光/CustomActors 全部自建，"
                              + "并在赋 SkinnedModel **之前**关掉 PerBoneMotionBlur ⇒ 495 骨的大骨架也能渲染"
                              + "（大骨架用两具 138 骨小骨架先撑主视口与自建任务两条骨骼缓冲到 16KB，再赋 495 骨头，避开 #76 首分配断言）。只读资产。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                                  + "\"path\":{\"type\":\"string\"},"
                                  + "\"out\":{\"type\":\"string\"},"
                                  + "\"morph\":{\"type\":\"string\"},"
                                  + "\"morphValue\":{\"type\":\"number\"},"
                                  + "\"modelYaw\":{\"type\":\"number\",\"description\":\"模型绕 Y 轴朝向（度），默认 180（面向 +Z 的镜头）\"},"
                                  + "\"width\":{\"type\":\"integer\"},\"height\":{\"type\":\"integer\"},"
                                  + "\"waitMs\":{\"type\":\"integer\"}},"
                                  + "\"required\":[\"path\"]}",
                Execute = OffscreenRenderAsync
            });
        }

        private static async Task<object> CaptureAsync(JsonElement args)
        {
            string pathArg = EditorUtils.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(pathArg))
                throw new ArgumentException("缺少 path");
            string morph = EditorUtils.GetString(args, "morph");
            float morphValue = EditorUtils.GetNum(args, "morphValue", 1f);
            int waitMs = Math.Clamp(EditorUtils.GetInt(args, "waitMs", 8000), 1000, 60000);

            string outPath = EditorUtils.GetString(args, "out");
            if (string.IsNullOrEmpty(outPath))
            {
                outPath = Path.Combine(Globals.ProjectFolder, "Logs", "HundunAgent", "screenshots",
                    "preview_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            }
            else if (!Path.IsPathRooted(outPath))
            {
                outPath = Path.Combine(Globals.ProjectFolder, outPath);
            }
            string outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);
            if (File.Exists(outPath))
                File.Delete(outPath);

            Guid assetId = await MainThread.InvokeAsync(() =>
            {
                var item = EditorUtils.FindAssetItem(EditorUtils.ResolveAssetPath(pathArg));
                if (item == null)
                    throw new ArgumentException("内容库找不到资产: " + pathArg);

                // 护栏（#76）：资产预览窗口也走"共享骨骼缓冲、Allocate 只翻一倍"那条断言。
                // 冷缓冲 16KB→32KB，逐骨运动模糊默认开（×2）：骨数×48×2 > 32768 就会弹断言并随后崩溃。
                // 实测 495 骨的头一开窗口就崩（编辑器直接没了），因此这里先按骨数拒绝，别把编辑器带走。
                var model = EditorUtils.LoadAsset(pathArg, 60000.0) as SkinnedModel;
                int bones = model?.Bones?.Length ?? 0;
                if (bones > 341)
                {
                    throw new InvalidOperationException(
                        "拒绝打开预览：#76 骨骼缓冲断言。骨骼数 " + bones + " ×48×2 = " + (bones * 96)
                        + " 字节 > 32KB 冷缓冲（上限约 341 骨）。大骨架请走 PIE 或等引擎修 Allocate 的翻倍逻辑。");
                }

                FlaxEditor.Editor.Instance.ContentEditing.Open(item);
                // 关键：同一次主线程调用里就把预览 Actor 的逐骨运动模糊关掉。
                // 预览任务也是"共享骨骼缓冲、Allocate 只翻一倍"，495 骨 ×48×2≈47KB 会直接断言崩溃；
                // 关掉后是 ×1≈23.7KB，从 0 起步时 16KB→32KB 可容纳。
                var early = FindPreview(item.ID);
                if (early != null)
                {
                    var earlyActor = early.GetType().GetProperty("PreviewActor")?.GetValue(early) as AnimatedModel;
                    if (earlyActor != null)
                        earlyActor.PerBoneMotionBlur = false;
                }
                return item.ID;
            });

            bool ready = await MainThread.WaitUntilAsync(
                () => FindPreview(assetId) != null, waitMs);
            if (!ready)
                throw new TimeoutException("资产预览窗口没在 " + waitMs + "ms 内就绪（assetId=" + assetId + "）");

            // 窗口出现 ≠ 模型已加载：12MB/821 形状的头要几秒。等到预览 Actor 真的挂上并加载完再截，
            // 否则会拿到一张纯白（Character_1 之所以首截就成，是因为它早就在缓存里）。
            bool modelReady = await MainThread.WaitUntilAsync(() =>
            {
                var p = FindPreview(assetId);
                if (p == null) return false;
                var a = p.GetType().GetProperty("PreviewActor")?.GetValue(p) as AnimatedModel;
                return a != null && a.SkinnedModel != null && a.SkinnedModel.IsLoaded;
            }, waitMs);
            if (!modelReady)
                throw new TimeoutException("预览模型没在 " + waitMs + "ms 内加载完成（assetId=" + assetId + "）");

            var report = await MainThread.InvokeAsync(() =>
            {
                object preview = FindPreview(assetId);
                if (preview == null)
                    throw new InvalidOperationException("预览对象消失");

                bool morphApplied = false;
                if (!string.IsNullOrEmpty(morph))
                {
                    var setBlend = preview.GetType().GetMethod("SetBlendShapeWeight",
                        new[] { typeof(string), typeof(float) });
                    if (setBlend == null)
                        throw new InvalidOperationException("预览不支持 SetBlendShapeWeight");
                    setBlend.Invoke(preview, new object[] { morph, morphValue });
                    morphApplied = true;
                }

                var actor = preview.GetType().GetProperty("PreviewActor")?.GetValue(preview) as AnimatedModel;
                if (actor != null)
                {
                    // 再兜一次：早前那次与窗口创建的竞态可能没设上
                    actor.PerBoneMotionBlur = false;
                }
                if (!string.IsNullOrEmpty(morph))
                {
                    var setBlend = preview.GetType().GetMethod("SetBlendShapeWeight",
                        new[] { typeof(string), typeof(float) });
                    if (setBlend == null)
                        throw new InvalidOperationException("预览不支持 SetBlendShapeWeight");
                    setBlend.Invoke(preview, new object[] { morph, morphValue });
                    morphApplied = true;
                }

                // 记下应用后的回读，避免"调用成功但形状不存在/权重没变"
                float weightAfter = -1f;
                if (actor != null && !string.IsNullOrEmpty(morph))
                {
                    var getW = actor.GetType().GetMethod("GetBlendShapeWeight", new[] { typeof(string) });
                    if (getW != null)
                    {
                        var v = getW.Invoke(actor, new object[] { morph });
                        if (v is float f) weightAfter = f;
                    }
                }

                var task = preview.GetType().GetProperty("Task")?.GetValue(preview) as SceneRenderTask;
                if (task == null)
                    throw new InvalidOperationException("预览没有 SceneRenderTask（类型 " + preview.GetType().Name + "）");

                Screenshot.Capture(task, outPath);

                return new
                {
                    previewType = preview.GetType().Name,
                    skinnedModel = actor?.SkinnedModel?.Path,
                    boneCount = actor?.SkinnedModel != null ? actor.SkinnedModel.Bones?.Length ?? 0 : 0,
                    morph,
                    morphApplied,
                    weightAfter,
                    outPath,
                };
            });

            bool ok = await MainThread.WaitUntilAsync(() => File.Exists(outPath), 15000);
            if (!ok)
                throw new TimeoutException("截图文件未生成: " + outPath);

            return new
            {
                status = "captured",
                path = outPath,
                size = new FileInfo(outPath).Length,
                detail = report,
            };
        }

        private static async Task<object> OffscreenRenderAsync(JsonElement args)
        {
            string pathArg = EditorUtils.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(pathArg))
                throw new ArgumentException("缺少 path");
            string morph = EditorUtils.GetString(args, "morph");
            float morphValue = EditorUtils.GetNum(args, "morphValue", 1f);
            float modelYaw = EditorUtils.GetNum(args, "modelYaw", 180f);
            int width = Math.Clamp(EditorUtils.GetInt(args, "width", 512), 64, 2048);
            int height = Math.Clamp(EditorUtils.GetInt(args, "height", 512), 64, 2048);
            int waitMs = Math.Clamp(EditorUtils.GetInt(args, "waitMs", 8000), 1000, 60000);

            string outPath = EditorUtils.GetString(args, "out");
            if (string.IsNullOrEmpty(outPath))
            {
                outPath = Path.Combine(Globals.ProjectFolder, "Logs", "HundunAgent", "screenshots",
                    "offscreen_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            }
            else if (!Path.IsPathRooted(outPath))
            {
                outPath = Path.Combine(Globals.ProjectFolder, outPath);
            }
            string outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);
            if (File.Exists(outPath))
                File.Delete(outPath);

            // ── 建场景对象（主线程一口气完成，避免中途被渲染帧插进来） ──
            var built = await MainThread.InvokeAsync(() =>
            {
                var model = EditorUtils.LoadAsset(pathArg, 60000.0) as SkinnedModel;
                if (model == null || !model.IsLoaded)
                    throw new InvalidOperationException("SkinnedModel 载不进来: " + pathArg);

                // #76：给 AnimatedModel 赋 SkinnedModel 会立刻 PreInitSkinningData → Allocate，
                // 冷缓冲 16KB、Allocate 只翻一倍 ⇒ 骨数×48 > 16KB 就断言。495 骨（23.7KB）必须先把
                // 两条会参与分配的扩展各撑一次（主视口那条 + 本任务那条）到 16KB，大骨架再分配时才能 16→32KB。
                int boneCount = model.Bones?.Length ?? 0;
                bool needsWarm = boneCount > 300;

                var scene = EditorUtils.RequireScene();
                var root = new EmptyActor { Name = "AgentOffscreen_Model" };
                Level.SpawnActor(root, scene);
                // 放到很远：编辑器主视口的自由相机在原点附近，避免主视口也去渲染它
                root.Position = new Float3(0, 100000, 0);
                root.Orientation = Quaternion.Euler(0, modelYaw, 0);

                var anim = root.AddChild<AnimatedModel>();
                anim.Name = "AgentOffscreen_AnimatedModel";
                anim.UpdateWhenOffscreen = true;
                anim.PerBoneMotionBlur = false;
                if (!needsWarm)
                {
                    // 小骨架：直接赋（6~10KB 能放进 16KB 首分配）
                    anim.SkinnedModel = model;
                    if (!string.IsNullOrEmpty(morph))
                        anim.SetBlendShapeWeight(morph, morphValue);
                }

                // 包围盒：取 LOD0 各网格 Box 的并集
                BoundingBox box = new BoundingBox(new Float3(-50, -50, -50), new Float3(50, 50, 50));
                model.GetMeshes(out MeshBase[] meshes, 0);
                if (meshes != null && meshes.Length > 0)
                {
                    Float3 min = new Float3(float.MaxValue);
                    Float3 max = new Float3(float.MinValue);
                    for (int i = 0; i < meshes.Length; i++)
                    {
                        var b = meshes[i].Box;
                        min = new Float3(Mathf.Min(min.X, b.Minimum.X), Mathf.Min(min.Y, b.Minimum.Y), Mathf.Min(min.Z, b.Minimum.Z));
                        max = new Float3(Mathf.Max(max.X, b.Maximum.X), Mathf.Max(max.Y, b.Maximum.Y), Mathf.Max(max.Z, b.Maximum.Z));
                    }
                    box = new BoundingBox(min, max);
                }
                Float3 center = root.Position + box.Center;
                float radius = Mathf.Max(10f, box.Size.Length * 0.5f);
                float distance = radius * 3.0f;

                var camRoot = new EmptyActor { Name = "AgentOffscreen_Camera" };
                Level.SpawnActor(camRoot, scene);
                camRoot.Position = center + new Float3(0, radius * 0.15f, distance);
                camRoot.Orientation = Quaternion.LookRotation(center - camRoot.Position, Float3.Up);
                var camera = camRoot.AddChild<Camera>();
                camera.UsePerspective = true;
                camera.FieldOfView = 35f;
                camera.NearPlane = 0.1f;
                camera.FarPlane = distance + radius * 6f + 1000f;

                // 三点光（CustomActors 孤立渲染不会带场景灯光，必须自建）
                var lightRoot = new EmptyActor { Name = "AgentOffscreen_Lights" };
                Level.SpawnActor(lightRoot, scene);
                lightRoot.Position = center;
                var key = lightRoot.AddChild<DirectionalLight>();
                key.Orientation = Quaternion.Euler(-45f, 160f, 0f);
                key.Brightness = 8f;
                key.Color = new Color(1f, 0.96f, 0.90f);
                var fill = lightRoot.AddChild<DirectionalLight>();
                fill.Orientation = Quaternion.Euler(-15f, 200f, 0f);
                fill.Brightness = 0.7f;
                fill.Color = new Color(0.85f, 0.90f, 1f);
                var rim = lightRoot.AddChild<DirectionalLight>();
                rim.Orientation = Quaternion.Euler(-30f, 0f, 0f);
                rim.Brightness = 0.45f;
                rim.Color = new Color(0.9f, 0.92f, 1f);

                var desc = GPUTextureDescription.New2D(width, height, PixelFormat.R8G8B8A8_UNorm);
                var texture = new GPUTexture();
                if (texture.Init(ref desc))
                    throw new InvalidOperationException("GPUTexture.Init 失败（" + width + "x" + height + "）");

                // 预撑两条扩展（#76）：主视口那条 + 本任务那条，各先渲染一具 138 骨小骨架（≈6.6KB）
                EmptyActor warmEditor = null;   // 放在原点，被主视口渲染 → 撑主视口扩展
                EmptyActor warmTask = null;     // 放在镜头里，被本任务渲染 → 撑本任务扩展
                SkinnedModel warmModel = null;
                if (needsWarm)
                {
                    warmModel = EditorUtils.LoadAsset("Content/Character/Models/Character_1.flax", 30000.0) as SkinnedModel;
                    if (warmModel != null && warmModel.IsLoaded)
                    {
                        warmEditor = new EmptyActor { Name = "AgentOffscreen_WarmEditor" };
                        Level.SpawnActor(warmEditor, scene);
                        warmEditor.Position = Float3.Zero;
                        var we = warmEditor.AddChild<AnimatedModel>();
                        we.UpdateWhenOffscreen = true;
                        we.PerBoneMotionBlur = false;
                        we.SkinnedModel = warmModel;
                        warmEditor.IsActive = true;

                        warmTask = new EmptyActor { Name = "AgentOffscreen_WarmTask" };
                        Level.SpawnActor(warmTask, scene);
                        warmTask.Position = root.Position; // 必须在自建镜头里，否则被剔除不分配
                        var wt = warmTask.AddChild<AnimatedModel>();
                        wt.UpdateWhenOffscreen = true;
                        wt.PerBoneMotionBlur = false;
                        wt.SkinnedModel = warmModel;
                    }
                }

                Actor[] actors = warmTask != null
                    ? new Actor[] { warmTask, camRoot, lightRoot }
                    : new Actor[] { root, camRoot, lightRoot };

                var task = new SceneRenderTask
                {
                    Order = -100,
                    Output = texture,
                    Camera = camera,
                    ActorsSource = ActorsSources.CustomActors,
                    CustomActors = actors,
                    Enabled = false,
                };
                task.Enabled = true;

                return new
                {
                    root,
                    anim,
                    model,
                    camRoot,
                    lightRoot,
                    warmEditor,
                    warmTask,
                    task,
                    texture,
                    outputPath = outPath,
                    center,
                    distance,
                    bones = boneCount,
                    needsWarm,
                };
            });

            // 阶段 1 → 2：先让小骨架把两条扩展撑到 16KB，再给大骨架赋模型（16→32KB）
            if (built.needsWarm)
            {
                await Task.Delay(700);
                await MainThread.InvokeAsync(() =>
                {
                    var anim = (AnimatedModel)built.anim;
                    anim.SkinnedModel = (SkinnedModel)built.model;
                    if (!string.IsNullOrEmpty(morph))
                        anim.SetBlendShapeWeight(morph, morphValue);

                    var task = (SceneRenderTask)built.task;
                    task.CustomActors = new Actor[] { (Actor)built.root, (Actor)built.camRoot, (Actor)built.lightRoot };
                    if (built.warmTask != null)
                        Actor.Destroy((Actor)built.warmTask);
                    if (built.warmEditor != null)
                        Actor.Destroy((Actor)built.warmEditor);
                    return true;
                });
            }

            // 阶段 3：给大骨架/正常骨架几帧完成蒙皮与渲染
            await Task.Delay(700);

            await MainThread.InvokeAsync(() =>
            {
                Screenshot.Capture((SceneRenderTask)built.task, outPath);
                return true;
            });

            bool ok = await MainThread.WaitUntilAsync(() => File.Exists(outPath), waitMs);
            if (!ok)
                throw new TimeoutException("离屏渲染截图没生成: " + outPath);

            // 清理：先停任务再销毁 Actor（悬空原生指针会让下一帧 DrawAll 崩）
            await MainThread.InvokeAsync(() =>
            {
                var task = (SceneRenderTask)built.task;
                task.Enabled = false;
                task.CustomActors = null;
                task.Camera = null;
                Actor[] actors = { (Actor)built.root, (Actor)built.camRoot, (Actor)built.lightRoot,
                                   built.warmTask as Actor, built.warmEditor as Actor };
                for (int i = 0; i < actors.Length; i++)
                {
                    try
                    {
                        if (actors[i] != null)
                            Actor.Destroy(actors[i]);
                    }
                    catch
                    {
                        // 预热 Actor 已在阶段 2 销毁过，第二次 Destroy 忽略
                    }
                }
                return true;
            });

            return new
            {
                status = "captured",
                path = outPath,
                size = new FileInfo(outPath).Length,
                boneCount = built.bones,
                morph,
                distance = built.distance,
                // Float3 有 Normalized 等自引用属性，直接序列化会触发 cycle 检测（实测 HTTP 500）
                centerX = built.center.X,
                centerY = built.center.Y,
                centerZ = built.center.Z,
            };
        }

        /// <summary>按资产 ID 找开着的资产窗口里的预览对象（SkinnedModelWindow 的私有 _preview 字段）。</summary>
        private static object FindPreview(Guid assetId)
        {
            var editor = FlaxEditor.Editor.Instance;
            if (editor?.Windows == null)
                return null;
            foreach (var win in editor.Windows.Windows)
            {
                if (win == null)
                    continue;
                var asset = win.GetType().GetProperty("Asset")?.GetValue(win) as Asset;
                if (asset == null || asset.ID != assetId)
                    continue;
                var field = win.GetType().GetField("_preview", BindingFlags.NonPublic | BindingFlags.Instance);
                var preview = field?.GetValue(win);
                if (preview != null)
                    return preview;
            }
            return null;
        }
    }
}
