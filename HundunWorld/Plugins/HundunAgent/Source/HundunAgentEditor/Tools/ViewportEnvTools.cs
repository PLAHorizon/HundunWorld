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

                Actor target = null;
                if (!string.IsNullOrEmpty(actorRef))
                {
                    target = EditorUtils.FindActor(actorRef);
                }
                else
                {
                    target = FindActorByTypeName(typeStr);
                }

                bool wasCreated = false;
                if (target == null)
                {
                    if (!create)
                        throw new ArgumentException("场景中不存在 " + typeStr + " Actor");

                    target = SceneActorToolsHelper.CreateByTypeName(typeStr);
                    if (target == null)
                        throw new InvalidOperationException("无法创建 " + typeStr);
                    target.Name = typeStr + "_AI";
                    editor.SceneEditing.Spawn(target, EditorUtils.RequireScene(), -1, false);
                    ToolRegistry.OnUndoableAction();
                    wasCreated = true;
                }

                var changed = new List<object>();
                var restorers = new List<Action>();

                foreach (var prop in props.EnumerateObject())
                {
                    var propInfo = target.GetType().GetProperty(prop.Name,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (propInfo == null || !propInfo.CanWrite)
                        throw new ArgumentException(typeStr + " 上不存在可写属性: " + prop.Name);

                    var oldValue = propInfo.GetValue(target);
                    var newValue = EditorUtils.ConvertJsonValue(propInfo.PropertyType, prop.Value);
                    propInfo.SetValue(target, newValue);

                    var capturedTarget = target;
                    var capturedProp = propInfo;
                    var capturedOld = oldValue;
                    restorers.Add(() =>
                    {
                        try { capturedProp.SetValue(capturedTarget, capturedOld); } catch { }
                    });
                    changed.Add(new { property = prop.Name, value = newValue?.ToString() });
                }

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
