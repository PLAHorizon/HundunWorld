using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.SceneGraph;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 场景与 Actor 工具集：场景加载/保存、层级查询、Actor 增删改、选择、预制体。
    /// </summary>
    public static class SceneActorTools
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "scene_list",
                Description = "列出当前已加载的场景，以及项目 Content 目录下所有可用场景文件。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = SceneListAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "scene_load",
                Description = "在编辑器中打开场景。path 为项目内相对路径（如 Content/Scenes/Main.scene），也可传场景资产 Guid。additive=true 时叠加加载。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"场景资产路径或Guid\"},\"additive\":{\"type\":\"boolean\"}},\"required\":[\"path\"]}",
                Execute = SceneLoadAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "scene_save",
                Description = "保存场景。不传 path 则保存所有已打开场景。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"可选，指定场景路径\"}}}",
                Execute = SceneSaveAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "scene_new",
                Description = "创建新的空场景文件并打开。path 为项目内相对路径，如 Content/Scenes/NewScene.scene。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}",
                Undoable = false,
                Execute = SceneNewAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "scene_hierarchy",
                Description = "获取当前场景的 Actor 层级树（含 id、名称、类型、位置摘要），供定位对象使用。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"maxDepth\":{\"type\":\"integer\",\"description\":\"最大递归深度，默认4\"}}}",
                Execute = SceneHierarchyAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_get",
                Description = "获取单个 Actor 的详细信息：Transform、父子关系、脚本、可写属性列表。actor 传名称或 Guid。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\",\"description\":\"Actor 名称或 Guid\"}},\"required\":[\"actor\"]}",
                Execute = ActorGetAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_find",
                Description = "按名称模糊搜索场景中的 Actor，返回匹配列表（最多 50 个）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"type\":{\"type\":\"string\",\"description\":\"可选，按类型名过滤，如 StaticModel\"}},\"required\":[\"query\"]}",
                Execute = ActorFindAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_create",
                Description = "创建 Actor 并加入场景。type 可选：EmptyActor/StaticModel/AnimatedModel/Camera/DirectionalLight/PointLight/SpotLight/Sky/Skybox/EnvironmentProbe/ExponentialHeightFog/BoxCollider/SphereCollider/CapsuleCollider/RigidBody/CharacterController/UIControl/UICanvas/TextRender/AudioSource/AudioListener/PostFxVolume。创建后支持撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"},\"parent\":{\"type\":\"string\",\"description\":\"父级Actor名称或Guid，缺省为场景根\"},\"position\":{\"type\":\"object\",\"description\":\"{x,y,z}\"},\"rotationEuler\":{\"type\":\"object\",\"description\":\"欧拉角{pitch,yaw,roll}\"},\"scale\":{\"type\":\"object\",\"description\":\"{x,y,z}\"}},\"required\":[\"type\"]}",
                Undoable = true,
                Execute = ActorCreateAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_set_transform",
                Description = "设置 Actor 的位置/旋转/缩放。rotationEuler 为欧拉角度数。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\"},\"position\":{\"type\":\"object\"},\"rotationEuler\":{\"type\":\"object\"},\"scale\":{\"type\":\"object\"}},\"required\":[\"actor\"]}",
                Undoable = true,
                Execute = ActorSetTransformAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_set_property",
                Description = "设置 Actor 或其脚本组件的任意可写属性。value 支持数字/布尔/字符串/枚举/{x,y,z}向量/颜色/资产路径字符串（自动加载资产）。component 可选，指定组件类型名。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\"},\"component\":{\"type\":\"string\",\"description\":\"可选组件类型名，缺省在Actor自身查找\"},\"property\":{\"type\":\"string\"},\"value\":{}},\"required\":[\"actor\",\"property\",\"value\"]}",
                Undoable = true,
                Execute = ActorSetPropertyAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_delete",
                Description = "删除 Actor（走编辑器删除流程，可撤销）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\"}},\"required\":[\"actor\"]}",
                Dangerous = true,
                Undoable = true,
                Execute = ActorDeleteAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_duplicate",
                Description = "复制 Actor（可撤销），返回新 Actor 信息。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\"}},\"required\":[\"actor\"]}",
                Undoable = true,
                Execute = ActorDuplicateAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "actor_reparent",
                Description = "改变 Actor 的父级（可撤销）。newParent 传名称/Guid，传空字符串表示挂到场景根。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\"},\"newParent\":{\"type\":\"string\"}},\"required\":[\"actor\"]}",
                Undoable = true,
                Execute = ActorReparentAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "selection_get",
                Description = "获取编辑器当前选中的 Actor 列表。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = SelectionGetAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "selection_set",
                Description = "设置编辑器选中的 Actor（按名称或 Guid 数组）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actors\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"actors\"]}",
                Execute = SelectionSetAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "prefab_spawn",
                Description = "将预制体资产实例化到场景（装配预制体）。path 为预制体资产路径（可用 asset_search 查找）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"parent\":{\"type\":\"string\"},\"position\":{\"type\":\"object\"},\"rotationEuler\":{\"type\":\"object\"},\"scale\":{\"type\":\"object\"}},\"required\":[\"path\"]}",
                Undoable = true,
                Execute = PrefabSpawnAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "prefab_create",
                Description = "把指定 Actor（或当前选中 Actor）保存为预制体资产。path 为项目内路径，如 Content/Prefabs/MyPrefab.prefab。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"actor\":{\"type\":\"string\",\"description\":\"可选，缺省使用当前选中\"}},\"required\":[\"path\"]}",
                Dangerous = false,
                Execute = PrefabCreateAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "prefab_apply",
                Description = "把预制体实例的修改应用回预制体资产（Apply All）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\",\"description\":\"预制体实例Actor\"}},\"required\":[\"actor\"]}",
                Dangerous = true,
                Execute = PrefabApplyAsync
            });
        }

        // ==================== Handlers ====================

        private static Task<object> SceneListAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var loaded = new List<object>();
                foreach (var scene in Level.Scenes)
                {
                    if (scene == null) continue;
                    loaded.Add(new
                    {
                        name = Path.GetFileNameWithoutExtension(scene.Path),
                        path = scene.Path,
                        id = scene.ID.ToString()
                    });
                }

                var available = new List<object>();
                CollectSceneItems(editor.ContentDatabase.Game.Folder, available, 0);

                return new { loaded, available };
            });
        }

        private static void CollectSceneItems(ContentFolder folder, List<object> output, int depth)
        {
            if (folder == null || depth > 12)
                return;
            foreach (var child in folder.Children)
            {
                if (child is SceneItem sceneItem)
                {
                    output.Add(new
                    {
                        name = sceneItem.ShortName,
                        path = sceneItem.Path,
                        id = sceneItem.ID.ToString()
                    });
                }
                else if (child is ContentFolder sub)
                {
                    CollectSceneItems(sub, output, depth + 1);
                }
            }
        }

        private static async Task<object> SceneLoadAsync(JsonElement args)
        {
            var path = EditorUtils.GetString(args, "path");
            var additive = EditorUtils.GetBool(args, "additive");

            Guid sceneId = await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;

                if (Guid.TryParse(path, out var guid))
                    return guid;

                var item = editor.ContentDatabase.Find(path);
                if (item == null)
                {
                    // 尝试刷新后重查
                    editor.ContentDatabase.Rebuild(true);
                    item = editor.ContentDatabase.Find(path);
                }
                if (!(item is SceneItem sceneItem))
                    throw new ArgumentException("未找到场景资产: " + path);
                return sceneItem.ID;
            });

            await MainThread.InvokeAsync(() =>
                FlaxEditor.Editor.Instance.Scene.OpenScene(sceneId, additive));

            var ok = await MainThread.WaitUntilAsync(() =>
                Level.Scenes.Any(s => s != null && s.ID == sceneId), 30000);

            if (!ok)
                throw new TimeoutException("场景加载超时: " + path);

            return new { status = "loaded", sceneId = sceneId.ToString() };
        }

        private static Task<object> SceneSaveAsync(JsonElement args)
        {
            var path = EditorUtils.GetString(args, "path");

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;

                if (!string.IsNullOrEmpty(path))
                {
                    var scene = Level.Scenes.FirstOrDefault(s =>
                        s != null && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
                    if (scene == null)
                        throw new ArgumentException("场景未打开: " + path);
                    editor.Scene.SaveScene(scene);
                    return new { status = "saved", path = scene.Path };
                }

                editor.Scene.SaveScenes();
                return new { status = "saved", count = Level.Scenes.Length };
            });
        }

        private static async Task<object> SceneNewAsync(JsonElement args)
        {
            var relPath = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(relPath))
                throw new ArgumentException("缺少 path 参数");

            var fullPath = await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var full = Path.Combine(Globals.ProjectFolder, relPath);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                editor.Scene.CreateSceneFile(full);
                editor.ContentDatabase.Rebuild(true);
                return full;
            });

            // 打开新场景
            return await SceneLoadAsync(args);
        }

        private static Task<object> SceneHierarchyAsync(JsonElement args)
        {
            var maxDepth = EditorUtils.GetInt(args, "maxDepth", 4);

            return MainThread.InvokeAsync<object>(() =>
            {
                var scenes = new List<object>();
                foreach (var scene in Level.Scenes)
                {
                    if (scene == null) continue;
                    var children = new List<object>();
                    foreach (var child in scene.Children)
                        children.Add(SerializeNode(child, 0, maxDepth));
                    scenes.Add(new
                    {
                        scene = Path.GetFileNameWithoutExtension(scene.Path),
                        sceneId = scene.ID.ToString(),
                        actors = children
                    });
                }
                return new { scenes };
            });
        }

        private static object SerializeNode(Actor actor, int depth, int maxDepth)
        {
            var children = new List<object>();
            if (depth < maxDepth)
            {
                foreach (var child in actor.Children)
                    children.Add(SerializeNode(child, depth + 1, maxDepth));
            }

            return new
            {
                id = actor.ID.ToString(),
                name = actor.Name,
                type = actor.GetType().Name,
                position = new { x = actor.Position.X, y = actor.Position.Y, z = actor.Position.Z },
                active = actor.IsActive,
                children
            };
        }

        private static Task<object> ActorGetAsync(JsonElement args)
        {
            var nameOrId = EditorUtils.GetString(args, "actor");

            return MainThread.InvokeAsync<object>(() =>
            {
                var actor = EditorUtils.FindActor(nameOrId);
                if (actor == null)
                    throw new ArgumentException("Actor 不存在: " + nameOrId);

                var scripts = actor.Scripts.Select(s => new
                {
                    type = s.GetType().Name,
                    enabled = s.Enabled
                }).ToList();

                var children = actor.Children.Select(c => new
                {
                    id = c.ID.ToString(),
                    name = c.Name,
                    type = c.GetType().Name
                }).ToList();

                // 可写属性列表（帮助 Agent 了解能设置什么）
                var writable = actor.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanWrite && p.GetSetMethod() != null && p.DeclaringType != typeof(object))
                    .Take(60)
                    .Select(p => p.PropertyType.Name + " " + p.Name)
                    .ToList();

                var t = actor.Transform;
                return new
                {
                    id = actor.ID.ToString(),
                    name = actor.Name,
                    type = actor.GetType().Name,
                    position = new { x = t.Translation.X, y = t.Translation.Y, z = t.Translation.Z },
                    rotationEuler = new
                    {
                        pitch = t.Orientation.EulerAngles.X,
                        yaw = t.Orientation.EulerAngles.Y,
                        roll = t.Orientation.EulerAngles.Z
                    },
                    scale = new { x = t.Scale.X, y = t.Scale.Y, z = t.Scale.Z },
                    active = actor.IsActive,
                    parent = actor.Parent?.Name,
                    scripts,
                    children,
                    writableProperties = writable
                };
            });
        }

        private static Task<object> ActorFindAsync(JsonElement args)
        {
            var query = EditorUtils.GetString(args, "query");
            var typeFilter = EditorUtils.GetString(args, "type");

            return MainThread.InvokeAsync<object>(() =>
            {
                var results = new List<object>();
                foreach (var scene in Level.Scenes)
                {
                    if (scene == null) continue;
                    CollectMatches(scene, query, typeFilter, results, 0);
                }
                return new { count = results.Count, actors = results };
            });
        }

        private static void CollectMatches(Actor root, string query, string typeFilter, List<object> results, int depth)
        {
            if (results.Count >= 50 || depth > 24)
                return;

            if (!(root is Scene) &&
                root.Name != null &&
                root.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 &&
                (typeFilter == null || root.GetType().Name.Equals(typeFilter, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new
                {
                    id = root.ID.ToString(),
                    name = root.Name,
                    type = root.GetType().Name,
                    position = new { x = root.Position.X, y = root.Position.Y, z = root.Position.Z }
                });
            }

            foreach (var child in root.Children)
                CollectMatches(child, query, typeFilter, results, depth + 1);
        }

        private static async Task<object> ActorCreateAsync(JsonElement args)
        {
            var typeStr = EditorUtils.GetString(args, "type", "EmptyActor");
            var actorName = EditorUtils.GetString(args, "name");
            var parentRef = EditorUtils.GetString(args, "parent");

            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;

                Actor newActor = CreateActorByType(typeStr);
                if (newActor == null)
                    throw new ArgumentException("不支持的 Actor 类型: " + typeStr +
                        "（可用: EmptyActor/StaticModel/AnimatedModel/Camera/DirectionalLight/PointLight/SpotLight/Sky/Skybox/EnvironmentProbe/ExponentialHeightFog/BoxCollider/SphereCollider/CapsuleCollider/RigidBody/CharacterController/UIControl/UICanvas/TextRender/AudioSource/AudioListener/PostFxVolume）");

                if (!string.IsNullOrEmpty(actorName))
                    newActor.Name = actorName;

                if (args.TryGetProperty("position", out var pos))
                    newActor.Position = EditorUtils.GetVector3(pos);
                if (args.TryGetProperty("rotationEuler", out var rot))
                    newActor.Orientation = Quaternion.Euler(EditorUtils.GetVector3(rot));
                if (args.TryGetProperty("scale", out var scl))
                    newActor.Scale = EditorUtils.GetVector3(scl, Vector3.One);

                Actor parent = null;
                if (!string.IsNullOrEmpty(parentRef))
                {
                    parent = EditorUtils.FindActor(parentRef);
                    if (parent == null)
                        throw new ArgumentException("父级 Actor 不存在: " + parentRef);
                }
                else
                {
                    parent = EditorUtils.RequireScene();
                }

                // 走编辑器 Spawn 流程（含 Undo 记录与场景树刷新）
                editor.SceneEditing.Spawn(newActor, parent, -1, false);

                return new
                {
                    id = newActor.ID.ToString(),
                    name = newActor.Name,
                    type = newActor.GetType().Name,
                    position = new { x = newActor.Position.X, y = newActor.Position.Y, z = newActor.Position.Z }
                };
            });

            ToolRegistry.OnUndoableAction();
            return result;
        }

        private static Actor CreateActorByType(string typeStr)
        {
            switch (typeStr)
            {
                case "EmptyActor": return Actor.New<EmptyActor>();
                case "StaticModel": return Actor.New<StaticModel>();
                case "AnimatedModel": return Actor.New<AnimatedModel>();
                case "Camera": return Actor.New<Camera>();
                case "DirectionalLight": return Actor.New<DirectionalLight>();
                case "PointLight": return Actor.New<PointLight>();
                case "SpotLight": return Actor.New<SpotLight>();
                case "Sky": return Actor.New<Sky>();
                case "Skybox": return Actor.New<Skybox>();
                case "EnvironmentProbe": return Actor.New<EnvironmentProbe>();
                case "ExponentialHeightFog": return Actor.New<ExponentialHeightFog>();
                case "BoxCollider": return Actor.New<BoxCollider>();
                case "SphereCollider": return Actor.New<SphereCollider>();
                case "CapsuleCollider": return Actor.New<CapsuleCollider>();
                case "RigidBody": return Actor.New<RigidBody>();
                case "CharacterController": return Actor.New<CharacterController>();
                case "UIControl": return Actor.New<UIControl>();
                case "UICanvas": return Actor.New<UICanvas>();
                case "TextRender": return Actor.New<TextRender>();
                case "AudioSource": return Actor.New<AudioSource>();
                case "AudioListener": return Actor.New<AudioListener>();
                case "PostFxVolume": return Actor.New<PostFxVolume>();
                default: return null;
            }
        }

        private static Task<object> ActorSetTransformAsync(JsonElement args)
        {
            var nameOrId = EditorUtils.GetString(args, "actor");

            return MainThread.InvokeAsync<object>(() =>
            {
                var actor = EditorUtils.FindActor(nameOrId);
                if (actor == null)
                    throw new ArgumentException("Actor 不存在: " + nameOrId);

                var oldPos = actor.Position;
                var oldRot = actor.Orientation;
                var oldScale = actor.Scale;

                if (args.TryGetProperty("position", out var pos))
                    actor.Position = EditorUtils.GetVector3(pos, actor.Position);
                if (args.TryGetProperty("rotationEuler", out var rot))
                    actor.Orientation = Quaternion.Euler(EditorUtils.GetVector3(rot));
                if (args.TryGetProperty("scale", out var scl))
                    actor.Scale = EditorUtils.GetVector3(scl, actor.Scale);

                var capturedActor = actor;
                AgentUndo.Record("AI: 设置 Transform (" + actor.Name + ")", () =>
                {
                    capturedActor.Position = oldPos;
                    capturedActor.Orientation = oldRot;
                    capturedActor.Scale = oldScale;
                }, null);

                return new
                {
                    id = actor.ID.ToString(),
                    name = actor.Name,
                    position = new { x = actor.Position.X, y = actor.Position.Y, z = actor.Position.Z },
                    rotationEuler = new
                    {
                        pitch = actor.Orientation.EulerAngles.X,
                        yaw = actor.Orientation.EulerAngles.Y,
                        roll = actor.Orientation.EulerAngles.Z
                    },
                    scale = new { x = actor.Scale.X, y = actor.Scale.Y, z = actor.Scale.Z }
                };
            });
        }

        private static Task<object> ActorSetPropertyAsync(JsonElement args)
        {
            var nameOrId = EditorUtils.GetString(args, "actor");
            var componentType = EditorUtils.GetString(args, "component");
            var propertyName = EditorUtils.GetString(args, "property");

            if (string.IsNullOrEmpty(propertyName))
                throw new ArgumentException("缺少 property 参数");
            if (!args.TryGetProperty("value", out var value))
                throw new ArgumentException("缺少 value 参数");

            return MainThread.InvokeAsync<object>(() =>
            {
                var actor = EditorUtils.FindActor(nameOrId);
                if (actor == null)
                    throw new ArgumentException("Actor 不存在: " + nameOrId);

                // 定位目标对象
                object target = actor;
                if (!string.IsNullOrEmpty(componentType))
                {
                    object found = actor.Scripts.FirstOrDefault(s =>
                        s.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase));
                    if (found == null)
                        found = actor.Children.FirstOrDefault(c =>
                            c.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase));
                    if (found == null)
                        throw new ArgumentException("Actor 上未找到组件: " + componentType);
                    target = found;
                }
                else
                {
                    // Actor 自身没有该属性时，自动在脚本/子组件中查找
                    var prop = actor.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                    if (prop == null || !prop.CanWrite)
                    {
                        foreach (var script in actor.Scripts)
                        {
                            var sp = script.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                            if (sp != null && sp.CanWrite)
                            {
                                target = script;
                                break;
                            }
                        }
                    }
                }

                var propInfo = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (propInfo == null || !propInfo.CanWrite)
                    throw new ArgumentException(target.GetType().Name + " 上不存在可写属性: " + propertyName);

                var oldValue = propInfo.GetValue(target);
                var newValue = EditorUtils.ConvertJsonValue(propInfo.PropertyType, value);
                propInfo.SetValue(target, newValue);

                var capturedTarget = target;
                var capturedProp = propInfo;
                AgentUndo.Record("AI: 设置属性 " + propertyName + " (" + actor.Name + ")", () =>
                {
                    try { capturedProp.SetValue(capturedTarget, oldValue); } catch { }
                }, null);

                return new
                {
                    actor = actor.Name,
                    target = target.GetType().Name,
                    property = propertyName,
                    newValue = SafeToString(newValue)
                };
            });
        }

        private static string SafeToString(object value)
        {
            try { return value?.ToString(); } catch { return value?.GetType().Name; }
        }

        private static async Task<object> ActorDeleteAsync(JsonElement args)
        {
            var nameOrId = EditorUtils.GetString(args, "actor");

            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var actor = EditorUtils.FindActor(nameOrId);
                if (actor == null)
                    throw new ArgumentException("Actor 不存在: " + nameOrId);

                var node = editor.Scene.GetActorNode(actor);
                if (node == null)
                    throw new InvalidOperationException("无法定位场景树节点，请重试");

                var deleted = new { id = actor.ID.ToString(), name = actor.Name, type = actor.GetType().Name };
                editor.SceneEditing.Select(node, false);
                editor.SceneEditing.Delete();
                return deleted;
            });

            ToolRegistry.OnUndoableAction();
            return new { status = "deleted", actor = result };
        }

        private static async Task<object> ActorDuplicateAsync(JsonElement args)
        {
            var nameOrId = EditorUtils.GetString(args, "actor");

            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var actor = EditorUtils.FindActor(nameOrId);
                if (actor == null)
                    throw new ArgumentException("Actor 不存在: " + nameOrId);

                var node = editor.Scene.GetActorNode(actor);
                if (node == null)
                    throw new InvalidOperationException("无法定位场景树节点，请重试");

                editor.SceneEditing.Select(node, false);
                editor.SceneEditing.Duplicate();

                var dup = editor.SceneEditing.Selection
                    .Select(n => (n as ActorNode)?.Actor)
                    .Where(a => a != null && a != actor)
                    .Select(a => new { id = a.ID.ToString(), name = a.Name, type = a.GetType().Name })
                    .ToList();
                return dup;
            });

            ToolRegistry.OnUndoableAction();
            return new { status = "duplicated", actors = result };
        }

        private static Task<object> ActorReparentAsync(JsonElement args)
        {
            var nameOrId = EditorUtils.GetString(args, "actor");
            var newParentRef = EditorUtils.GetString(args, "newParent", "");

            return MainThread.InvokeAsync<object>(() =>
            {
                var actor = EditorUtils.FindActor(nameOrId);
                if (actor == null)
                    throw new ArgumentException("Actor 不存在: " + nameOrId);

                Actor newParent = string.IsNullOrEmpty(newParentRef)
                    ? EditorUtils.RequireScene()
                    : EditorUtils.FindActor(newParentRef);
                if (newParent == null)
                    throw new ArgumentException("目标父级不存在: " + newParentRef);

                var oldParent = actor.Parent;
                var oldTransform = actor.LocalTransform;

                actor.Parent = newParent;

                var capturedActor = actor;
                AgentUndo.Record("AI: 重新挂载 " + actor.Name, () =>
                {
                    capturedActor.Parent = oldParent;
                    capturedActor.LocalTransform = oldTransform;
                }, null);

                return new
                {
                    actor = actor.Name,
                    newParent = newParent.Name,
                    previousParent = oldParent?.Name
                };
            });
        }

        private static Task<object> SelectionGetAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var selection = editor.SceneEditing.Selection
                    .Select(n => (n as ActorNode)?.Actor)
                    .Where(a => a != null)
                    .Select(a => new { id = a.ID.ToString(), name = a.Name, type = a.GetType().Name })
                    .ToList();
                return new { count = selection.Count, actors = selection };
            });
        }

        private static Task<object> SelectionSetAsync(JsonElement args)
        {
            var refs = new List<string>();
            if (args.TryGetProperty("actors", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        refs.Add(item.GetString());
                }
            }

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var nodes = new List<SceneGraphNode>();
                var missing = new List<string>();

                foreach (var r in refs)
                {
                    var actor = EditorUtils.FindActor(r);
                    if (actor == null)
                    {
                        missing.Add(r);
                        continue;
                    }
                    var node = editor.Scene.GetActorNode(actor);
                    if (node != null)
                        nodes.Add(node);
                }

                if (nodes.Count == 0)
                {
                    editor.SceneEditing.Deselect();
                }
                else
                {
                    editor.SceneEditing.Select(nodes, false);
                }

                return new { selected = nodes.Count, missing };
            });
        }

        private static async Task<object> PrefabSpawnAsync(JsonElement args)
        {
            var path = EditorUtils.GetString(args, "path");
            var parentRef = EditorUtils.GetString(args, "parent");

            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;

                var prefab = Content.Load<Prefab>(path, 10000.0);
                if (prefab == null || !prefab.IsLoaded)
                    throw new ArgumentException("预制体加载失败: " + path);

                var transform = Transform.Identity;
                if (args.TryGetProperty("position", out var pos))
                    transform.Translation = EditorUtils.GetVector3(pos);
                if (args.TryGetProperty("rotationEuler", out var rot))
                    transform.Orientation = Quaternion.Euler(EditorUtils.GetVector3(rot));
                if (args.TryGetProperty("scale", out var scl))
                    transform.Scale = EditorUtils.GetVector3(scl, Vector3.One);

                var instance = PrefabManager.SpawnPrefab(prefab, transform);
                if (instance == null)
                    throw new InvalidOperationException("预制体实例化失败: " + path);

                Actor parent = null;
                if (!string.IsNullOrEmpty(parentRef))
                {
                    parent = EditorUtils.FindActor(parentRef);
                    if (parent == null)
                        throw new ArgumentException("父级 Actor 不存在: " + parentRef);
                }
                else
                {
                    parent = EditorUtils.RequireScene();
                }

                editor.SceneEditing.Spawn(instance, parent, -1, false);

                return new
                {
                    id = instance.ID.ToString(),
                    name = instance.Name,
                    type = instance.GetType().Name,
                    prefab = path
                };
            });

            ToolRegistry.OnUndoableAction();
            return result;
        }

        private static Task<object> PrefabCreateAsync(JsonElement args)
        {
            var relPath = EditorUtils.GetString(args, "path");
            var actorRef = EditorUtils.GetString(args, "actor");

            if (string.IsNullOrEmpty(relPath))
                throw new ArgumentException("缺少 path 参数");

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var fullPath = Path.Combine(Globals.ProjectFolder, relPath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                Actor actor = null;
                if (!string.IsNullOrEmpty(actorRef))
                {
                    actor = EditorUtils.FindActor(actorRef);
                    if (actor == null)
                        throw new ArgumentException("Actor 不存在: " + actorRef);
                }

                if (actor != null)
                {
                    // 依次尝试正斜杠绝对路径与项目相对路径；失败时用文件存在性二次验证
                    var ok = PrefabManager.CreatePrefab(actor, fullPath.Replace('\\', '/'), true)
                             || PrefabManager.CreatePrefab(actor, fullPath, true)
                             || PrefabManager.CreatePrefab(actor, relPath.Replace('\\', '/'), true);
                    if (!ok && !File.Exists(fullPath))
                        throw new InvalidOperationException("预制体创建失败: " + fullPath);
                }
                else
                {
                    if (!editor.SceneEditing.HasSthSelected)
                        throw new InvalidOperationException("未指定 actor 且当前无选中对象");
                    editor.Prefabs.CreatePrefab();
                }

                editor.ContentDatabase.Rebuild(true);
                return new { status = "created", path = relPath };
            });
        }

        private static async Task<object> PrefabApplyAsync(JsonElement args)
        {
            var actorRef = EditorUtils.GetString(args, "actor");

            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var actor = EditorUtils.FindActor(actorRef);
                if (actor == null)
                    throw new ArgumentException("Actor 不存在: " + actorRef);

                if (!PrefabManager.ApplyAll(actor))
                    throw new InvalidOperationException("Apply 失败（该 Actor 可能不是预制体实例）: " + actorRef);

                return new { status = "applied", actor = actor.Name };
            });

            ToolRegistry.OnUndoableAction();
            return result;
        }
    }
}
