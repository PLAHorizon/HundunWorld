using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 视口截图与环境工具：截图（供 AI 视觉反馈）、编辑器相机控制、光照/天空/雾效调整。
    /// </summary>
    public static class ViewportEnvTools
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "viewport_screenshot",
                Description = "捕获编辑器主视口截图并保存为 PNG，返回文件路径。用于 AI 观察修改效果（视觉反馈循环）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"可选保存路径，缺省自动保存到 Logs/HundunAgent/screenshots\"}}}",
                Execute = ViewportScreenshotAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "viewport_camera_set",
                Description = "移动/旋转编辑器视口相机，便于观察指定区域。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"position\":{\"type\":\"object\",\"description\":\"{x,y,z}\"},\"rotationEuler\":{\"type\":\"object\",\"description\":\"欧拉角{pitch,yaw,roll}\"}}}",
                Execute = ViewportCameraSetAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "env_set",
                Description = "调整环境 Actor（DirectionalLight/Sky/Skybox/EnvironmentProbe/ExponentialHeightFog/PostFxVolume）的属性。按类型自动查找场景中的目标，找不到且 create=true 时自动创建。properties 为属性字典。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\",\"description\":\"DirectionalLight/Sky/Skybox/EnvironmentProbe/ExponentialHeightFog/PostFxVolume\"},\"actor\":{\"type\":\"string\",\"description\":\"可选，直接指定Actor名称或Guid\"},\"create\":{\"type\":\"boolean\",\"description\":\"找不到时是否创建，默认true\"},\"properties\":{\"type\":\"object\",\"description\":\"属性键值对\"}},\"required\":[\"type\",\"properties\"]}",
                Undoable = true,
                Execute = EnvSetAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "env_preset",
                Description = "一键应用氛围预设：sunny_day（晴日）/ golden_hour（黄昏）/ night_moon（月夜）/ ink_mist（水墨雾境）。自动调整 DirectionalLight 与 ExponentialHeightFog 等（不存在且 create=true 时自动创建）。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"preset\":{\"type\":\"string\",\"description\":\"sunny_day/golden_hour/night_moon/ink_mist\"},\"create\":{\"type\":\"boolean\",\"description\":\"缺失环境 Actor 时是否自动创建，默认true\"}},\"required\":[\"preset\"]}",
                Undoable = true,
                Execute = EnvPresetAsync
            });
        }

        // ==================== Handlers ====================

        private static async Task<object> ViewportScreenshotAsync(JsonElement args)
        {
            var customPath = EditorUtils.GetString(args, "path");

            var targetPath = await MainThread.InvokeAsync(() =>
            {
                string path;
                if (!string.IsNullOrEmpty(customPath))
                {
                    path = Path.IsPathRooted(customPath)
                        ? customPath
                        : Path.Combine(Globals.ProjectFolder, customPath);
                }
                else
                {
                    var dir = Path.Combine(Globals.ProjectFolder, "Logs", "HundunAgent", "screenshots");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                }

                var dirName = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
                    Directory.CreateDirectory(dirName);

                var editor = FlaxEditor.Editor.Instance;
                var viewport = editor.Windows?.EditWin?.Viewport;
                if (viewport != null)
                {
                    viewport.TakeScreenshot(path);
                }
                else
                {
                    Screenshot.Capture(path);
                }

                return path;
            });

            // 截图写入发生在下一渲染帧，等待文件出现
            var ok = await MainThread.WaitUntilAsync(() => File.Exists(targetPath), 8000);
            if (!ok)
                throw new TimeoutException("截图文件未生成: " + targetPath);

            return new
            {
                status = "captured",
                path = targetPath,
                size = new FileInfo(targetPath).Length
            };
        }

        private static Task<object> ViewportCameraSetAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var viewport = editor.Windows?.EditWin?.Viewport;
                if (viewport == null)
                    throw new InvalidOperationException("编辑器视口不可用");

                if (args.TryGetProperty("position", out var pos))
                    viewport.ViewPosition = EditorUtils.GetVector3(pos, viewport.ViewPosition);
                if (args.TryGetProperty("rotationEuler", out var rot))
                    viewport.ViewOrientation = Quaternion.Euler(EditorUtils.GetVector3(rot));

                return new
                {
                    status = "set",
                    position = new
                    {
                        x = viewport.ViewPosition.X,
                        y = viewport.ViewPosition.Y,
                        z = viewport.ViewPosition.Z
                    }
                };
            });
        }

        private static readonly HashSet<string> EnvTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DirectionalLight", "Sky", "Skybox", "EnvironmentProbe", "ExponentialHeightFog", "PostFxVolume"
        };

        /// <summary>氛围预设：类型 → 属性应用列表（JSON；宽松应用，属性不存在时跳过并报告）。</summary>
        private static readonly Dictionary<string, string[]> EnvPresets =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "sunny_day", new[]
                {
                    "{\"type\":\"DirectionalLight\",\"properties\":{\"Orientation\":{\"x\":-50,\"y\":20,\"z\":0},\"Color\":{\"r\":1.0,\"g\":0.97,\"b\":0.9,\"a\":1.0},\"Brightness\":1.3}}",
                    "{\"type\":\"ExponentialHeightFog\",\"properties\":{\"FogDensity\":0.008,\"FogInscatteringColor\":{\"r\":0.75,\"g\":0.8,\"b\":0.85,\"a\":1.0}}}"
                }
            },
            {
                "golden_hour", new[]
                {
                    "{\"type\":\"DirectionalLight\",\"properties\":{\"Orientation\":{\"x\":-15,\"y\":160,\"z\":0},\"Color\":{\"r\":1.0,\"g\":0.72,\"b\":0.42,\"a\":1.0},\"Brightness\":1.1}}",
                    "{\"type\":\"ExponentialHeightFog\",\"properties\":{\"FogDensity\":0.02,\"FogInscatteringColor\":{\"r\":0.85,\"g\":0.62,\"b\":0.42,\"a\":1.0}}}"
                }
            },
            {
                "night_moon", new[]
                {
                    "{\"type\":\"DirectionalLight\",\"properties\":{\"Orientation\":{\"x\":-42,\"y\":-125,\"z\":0},\"Color\":{\"r\":0.5,\"g\":0.62,\"b\":0.92,\"a\":1.0},\"Brightness\":0.35}}",
                    "{\"type\":\"ExponentialHeightFog\",\"properties\":{\"FogDensity\":0.012,\"FogInscatteringColor\":{\"r\":0.12,\"g\":0.17,\"b\":0.3,\"a\":1.0}}}"
                }
            },
            {
                "ink_mist", new[]
                {
                    "{\"type\":\"DirectionalLight\",\"properties\":{\"Orientation\":{\"x\":-65,\"y\":70,\"z\":0},\"Color\":{\"r\":0.88,\"g\":0.87,\"b\":0.83,\"a\":1.0},\"Brightness\":0.85}}",
                    "{\"type\":\"ExponentialHeightFog\",\"properties\":{\"FogDensity\":0.06,\"FogInscatteringColor\":{\"r\":0.78,\"g\":0.8,\"b\":0.82,\"a\":1.0}}}"
                }
            },
        };

        private static Task<object> EnvSetAsync(JsonElement args)
        {
            var typeStr = EditorUtils.GetString(args, "type");
            var actorRef = EditorUtils.GetString(args, "actor");
            var create = EditorUtils.GetBool(args, "create", true);

            if (string.IsNullOrEmpty(typeStr) || !EnvTypes.Contains(typeStr))
                throw new ArgumentException("type 必须是: " + string.Join("/", EnvTypes));
            if (!args.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("缺少 properties 对象");

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var target = ResolveEnvTarget(editor, typeStr, actorRef, create, out var wasCreated);

                var changed = new List<object>();
                var restorers = new List<Action>();
                ApplyEnvProps(target, typeStr, props, changed, restorers, null, false);

                if (!wasCreated)
                {
                    AgentUndo.Record("AI: 调整环境 " + typeStr + " (" + target.Name + ")", () =>
                    {
                        foreach (var restore in restorers)
                            restore();
                    }, null);
                }

                return new
                {
                    status = "set",
                    actor = target.Name,
                    type = typeStr,
                    created = wasCreated,
                    changed
                };
            });
        }

        private static Task<object> EnvPresetAsync(JsonElement args)
        {
            var presetName = EditorUtils.GetString(args, "preset");
            var create = EditorUtils.GetBool(args, "create", true);

            if (string.IsNullOrEmpty(presetName) || !EnvPresets.TryGetValue(presetName, out var entries))
                throw new ArgumentException("未知预设: " + presetName + "（可用: " + string.Join("/", EnvPresets.Keys) + "）");

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var allRestorers = new List<Action>();
                var results = new List<object>();

                foreach (var entryJson in entries)
                {
                    using (var doc = JsonDocument.Parse(entryJson))
                    {
                        var root = doc.RootElement;
                        var typeStr = root.GetProperty("type").GetString();
                        var props = root.GetProperty("properties");

                        var target = ResolveEnvTarget(editor, typeStr, null, create, out var wasCreated);

                        var changed = new List<object>();
                        var restorers = new List<Action>();
                        var skipped = new List<object>();
                        ApplyEnvProps(target, typeStr, props, changed, restorers, skipped, true);

                        if (!wasCreated)
                            allRestorers.AddRange(restorers);

                        results.Add(new
                        {
                            type = typeStr,
                            actor = target.Name,
                            created = wasCreated,
                            changed,
                            skipped
                        });
                    }
                }

                if (allRestorers.Count > 0)
                {
                    AgentUndo.Record("AI: 应用氛围预设 " + presetName, () =>
                    {
                        foreach (var restore in allRestorers)
                            restore();
                    }, null);
                }

                return new { status = "applied", preset = presetName, results };
            });
        }

        /// <summary>按类型/引用定位环境 Actor；找不到且 create=true 时创建并 Spawn（计入撤销计数）。</summary>
        private static Actor ResolveEnvTarget(FlaxEditor.Editor editor, string typeStr, string actorRef, bool create, out bool wasCreated)
        {
            wasCreated = false;

            Actor target = !string.IsNullOrEmpty(actorRef)
                ? EditorUtils.FindActor(actorRef)
                : FindActorByTypeName(typeStr);

            if (target != null)
                return target;

            if (!create)
                throw new ArgumentException("场景中不存在 " + typeStr + " Actor");

            target = SceneActorToolsHelper.CreateByTypeName(typeStr);
            if (target == null)
                throw new InvalidOperationException("无法创建 " + typeStr);
            target.Name = typeStr + "_AI";
            editor.SceneEditing.Spawn(target, EditorUtils.RequireScene(), -1, false);
            ToolRegistry.OnUndoableAction();
            wasCreated = true;
            return target;
        }

        /// <summary>把 JSON 属性字典写入环境 Actor；lenient=true 时跳过不存在/转换失败的属性并记入 skipped。</summary>
        private static void ApplyEnvProps(Actor target, string typeName, JsonElement props,
            List<object> changed, List<Action> restorers, List<object> skipped, bool lenient)
        {
            foreach (var prop in props.EnumerateObject())
            {
                var propInfo = target.GetType().GetProperty(prop.Name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (propInfo == null || !propInfo.CanWrite)
                {
                    if (lenient)
                    {
                        skipped?.Add(new { property = prop.Name, reason = "属性不存在" });
                        continue;
                    }
                    throw new ArgumentException(typeName + " 上不存在可写属性: " + prop.Name);
                }

                try
                {
                    var oldValue = propInfo.GetValue(target);
                    var newValue = EditorUtils.ConvertJsonValue(propInfo.PropertyType, prop.Value);
                    propInfo.SetValue(target, newValue);

                    var capturedTarget = target;
                    var capturedProp = propInfo;
                    var capturedOld = oldValue;
                    restorers?.Add(() =>
                    {
                        try { capturedProp.SetValue(capturedTarget, capturedOld); } catch { }
                    });
                    changed.Add(new { property = prop.Name, value = newValue?.ToString() });
                }
                catch (Exception ex) when (lenient)
                {
                    skipped?.Add(new { property = prop.Name, reason = ex.Message });
                }
            }
        }

        private static Actor FindActorByTypeName(string typeName)
        {
            foreach (var scene in Level.Scenes)
            {
                if (scene == null) continue;
                var found = FindByTypeInTree(scene, typeName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Actor FindByTypeInTree(Actor root, string typeName)
        {
            if (!(root is Scene) && root.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                return root;
            foreach (var child in root.Children)
            {
                var found = FindByTypeInTree(child, typeName);
                if (found != null)
                    return found;
            }
            return null;
        }
    }

    /// <summary>供环境工具复用的 Actor 创建辅助（避免暴露 SceneActorTools 内部 switch）。</summary>
    internal static class SceneActorToolsHelper
    {
        public static Actor CreateByTypeName(string typeStr)
        {
            switch (typeStr)
            {
                case "DirectionalLight": return Actor.New<DirectionalLight>();
                case "Sky": return Actor.New<Sky>();
                case "Skybox": return Actor.New<Skybox>();
                case "EnvironmentProbe": return Actor.New<EnvironmentProbe>();
                case "ExponentialHeightFog": return Actor.New<ExponentialHeightFog>();
                case "PostFxVolume": return Actor.New<PostFxVolume>();
                default: return null;
            }
        }
    }
}
