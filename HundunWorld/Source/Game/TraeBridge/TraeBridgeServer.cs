using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;

namespace HundunWorld.TraeBridge
{
    public class TraeBridgeServer
    {
        private static readonly Lazy<TraeBridgeServer> _instance = new Lazy<TraeBridgeServer>(() => new TraeBridgeServer());
        public static TraeBridgeServer Instance => _instance.Value;

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _serverTask;

        private const string BaseUrl = "http://localhost:21888/";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private TraeBridgeServer()
        {
        }

        public void Start()
        {
            if (_listener != null)
                return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(BaseUrl);
                _listener.Start();

                _cts = new CancellationTokenSource();
                _serverTask = Task.Run(() => ListenAsync(_cts.Token), _cts.Token);

                Debug.Log("[TraeBridge] HTTP 服务器已启动: " + BaseUrl);
            }
            catch (Exception ex)
            {
                Debug.LogError("[TraeBridge] 启动 HTTP 服务器失败: " + ex.Message);
                _listener?.Close();
                _listener = null;
            }
        }

        public void Stop()
        {
            if (_listener == null)
                return;

            try
            {
                _cts?.Cancel();
                _listener.Stop();
                _listener.Close();
                _serverTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                Debug.LogError("[TraeBridge] 停止 HTTP 服务器异常: " + ex.Message);
            }
            finally
            {
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                _serverTask = null;
            }
        }

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();

                    if (context == null)
                        break;

                    _ = Task.Run(() => HandleRequestAsync(context), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[TraeBridge] 请求处理异常: " + ex.Message);
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            SetCorsHeaders(response);

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            try
            {
                var path = request.Url.AbsolutePath.TrimEnd('/');
                var method = request.HttpMethod.ToUpperInvariant();

                switch (method)
                {
                    case "GET":
                        await HandleGetAsync(path, request, response);
                        break;
                    case "POST":
                        await HandlePostAsync(path, request, response);
                        break;
                    case "PUT":
                        await HandlePutAsync(path, request, response);
                        break;
                    case "DELETE":
                        await HandleDeleteAsync(path, request, response);
                        break;
                    default:
                        SendError(response, 405, "Method Not Allowed");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[TraeBridge] 处理请求异常: " + ex.Message + "\n" + ex.StackTrace);
                SendError(response, 500, ex.Message);
            }
        }

        private void SetCorsHeaders(HttpListenerResponse response)
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
            response.AddHeader("Access-Control-Max-Age", "86400");
        }

        private async Task HandleGetAsync(string path, HttpListenerRequest request, HttpListenerResponse response)
        {
            if (path == "/health" || path == "")
            {
                await SendJsonAsync(response, new { status = "ok", editor = Engine.IsEditor });
            }
            else if (path == "/api/scene/info")
            {
                await HandleSceneInfoAsync(response);
            }
            else if (path == "/api/scene/hierarchy")
            {
                await HandleSceneHierarchyAsync(response);
            }
            else if (path.StartsWith("/api/scene/actor/") && path.Count(c => c == '/') == 4)
            {
                var actorName = Uri.UnescapeDataString(path.Substring("/api/scene/actor/".Length));
                await HandleGetActorAsync(response, actorName);
            }
            else if (path == "/api/assets/list")
            {
                await HandleAssetsListAsync(response, request.Url.Query);
            }
            else if (path == "/api/scene/save")
            {
                await HandleSaveSceneAsync(response);
            }
            else if (path == "/api/viewport/screenshot")
            {
                await HandleScreenshotAsync(response);
            }
            else
            {
                SendError(response, 404, "Not Found: " + path);
            }
        }

        private async Task HandlePostAsync(string path, HttpListenerRequest request, HttpListenerResponse response)
        {
            if (path == "/api/scene/actor")
            {
                var body = await ReadRequestBodyAsync(request);
                await HandleCreateActorAsync(response, body);
            }
            else if (path.StartsWith("/api/scene/actor/") && path.EndsWith("/component"))
            {
                var prefix = "/api/scene/actor/";
                var suffix = "/component";
                var actorName = Uri.UnescapeDataString(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length));
                var body = await ReadRequestBodyAsync(request);
                await HandleAddComponentAsync(response, actorName, body);
            }
            else if (path == "/api/assets/import")
            {
                var body = await ReadRequestBodyAsync(request);
                await HandleImportAssetAsync(response, body);
            }
            else if (path.StartsWith("/api/scene/actor/") && path.EndsWith("/property"))
            {
                var prefix = "/api/scene/actor/";
                var suffix = "/property";
                var actorName = Uri.UnescapeDataString(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length));
                var body = await ReadRequestBodyAsync(request);
                await HandleSetPropertyAsync(response, actorName, body);
            }
            else if (path == "/api/execute")
            {
                var body = await ReadRequestBodyAsync(request);
                await HandleExecuteCodeAsync(response, body);
            }
            else
            {
                SendError(response, 404, "Not Found: " + path);
            }
        }

        private async Task HandlePutAsync(string path, HttpListenerRequest request, HttpListenerResponse response)
        {
            if (path.StartsWith("/api/scene/actor/") && path.EndsWith("/transform"))
            {
                var actorName = Uri.UnescapeDataString(
                    path.Substring("/api/scene/actor/".Length, path.Length - "/api/scene/actor/".Length - "/transform".Length));
                var body = await ReadRequestBodyAsync(request);
                await HandleUpdateTransformAsync(response, actorName, body);
            }
            else
            {
                SendError(response, 404, "Not Found: " + path);
            }
        }

        private async Task HandleDeleteAsync(string path, HttpListenerRequest request, HttpListenerResponse response)
        {
            if (path.StartsWith("/api/scene/actor/") && path.Contains("/component/"))
            {
                var parts = path.Substring("/api/scene/actor/".Length).Split(new[] { "/component/" }, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    var actorName = Uri.UnescapeDataString(parts[0]);
                    var componentType = Uri.UnescapeDataString(parts[1]);
                    await HandleRemoveComponentAsync(response, actorName, componentType);
                    return;
                }
            }
            else if (path.StartsWith("/api/scene/actor/"))
            {
                var actorName = Uri.UnescapeDataString(path.Substring("/api/scene/actor/".Length));
                await HandleDeleteActorAsync(response, actorName);
                return;
            }

            SendError(response, 404, "Not Found: " + path);
        }

        private async Task<T> InvokeOnMainThreadAsync<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>();

            Scripting.InvokeOnUpdate(() =>
            {
                try
                {
                    var result = action();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return await tcs.Task;
        }

        private async Task InvokeOnMainThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            Scripting.InvokeOnUpdate(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            await tcs.Task;
        }

        private async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private async Task SendJsonAsync(HttpListenerResponse response, object data, int statusCode = 200)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";

            var json = System.Text.Json.JsonSerializer.Serialize(data, JsonOptions);
            var buffer = Encoding.UTF8.GetBytes(json);

            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }

        private void SendError(HttpListenerResponse response, int statusCode, string message)
        {
            try
            {
                response.StatusCode = statusCode;
                response.ContentType = "application/json; charset=utf-8";

                var json = System.Text.Json.JsonSerializer.Serialize(new { error = message }, JsonOptions);
                var buffer = Encoding.UTF8.GetBytes(json);

                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch { }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        // ==================== API Handlers ====================

        private async Task HandleSceneInfoAsync(HttpListenerResponse response)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                var scenes = Level.Scenes;
                var sceneInfos = new List<object>();

                foreach (var scene in scenes)
                {
                    if (scene == null) continue;

                    int actorCount = 0;
                    var actors = scene.Children;
                    if (actors != null)
                    {
                        foreach (var _ in actors)
                            actorCount++;
                    }

                    sceneInfos.Add(new
                    {
                        name = Path.GetFileNameWithoutExtension(scene.Path) ?? scene.GetType().Name,
                        path = scene.Path,
                        actorCount = actorCount
                    });
                }

                await SendJsonAsync(response, new { scenes = sceneInfos });
            });
        }

        private async Task HandleSceneHierarchyAsync(HttpListenerResponse response)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                var scenes = Level.Scenes;
                var hierarchyList = new List<object>();

                foreach (var scene in scenes)
                {
                    if (scene == null) continue;

                    var actors = scene.Children;
                    if (actors == null) continue;

                    var children = new List<object>();
                    foreach (var actor in actors)
                    {
                        if (actor == null) continue;
                        if (actor.Parent == null || actor.Parent == scene)
                        {
                            children.Add(SerializeActorHierarchy(actor));
                        }
                    }

                    hierarchyList.Add(new
                    {
                        scene = Path.GetFileNameWithoutExtension(scene.Path) ?? scene.GetType().Name,
                        actors = children
                    });
                }

                await SendJsonAsync(response, new { scenes = hierarchyList });
            });
        }

        private object SerializeActorHierarchy(Actor actor)
        {
            var children = new List<object>();
            foreach (var child in actor.Children)
            {
                children.Add(SerializeActorHierarchy(child));
            }

            return new
            {
                name = actor.Name,
                type = actor.GetType().Name,
                id = actor.ID.ToString(),
                children = children
            };
        }

        private async Task HandleGetActorAsync(HttpListenerResponse response, string actorName)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                var actor = FindActorByName(actorName);
                if (actor == null)
                {
                    await SendJsonAsync(response, new { error = "Actor not found: " + actorName }, 404);
                    return;
                }

                var scripts = new List<object>();
                foreach (var script in actor.Scripts)
                {
                    if (script != null)
                    {
                        scripts.Add(new
                        {
                            type = script.GetType().Name,
                            active = script.Enabled
                        });
                    }
                }

                var children = new List<object>();
                foreach (var child in actor.Children)
                {
                    if (child != null)
                    {
                        children.Add(new
                        {
                            name = child.Name,
                            type = child.GetType().Name,
                            active = child.IsActive
                        });
                    }
                }

                var transform = actor.Transform;
                var result = new
                {
                    name = actor.Name,
                    type = actor.GetType().Name,
                    id = actor.ID.ToString(),
                    position = new { x = transform.Translation.X, y = transform.Translation.Y, z = transform.Translation.Z },
                    rotation = new { x = transform.Orientation.EulerAngles.X, y = transform.Orientation.EulerAngles.Y, z = transform.Orientation.EulerAngles.Z },
                    scale = new { x = transform.Scale.X, y = transform.Scale.Y, z = transform.Scale.Z },
                    isActive = actor.IsActive,
                    parent = actor.Parent?.Name ?? "Scene",
                    scripts = scripts,
                    children = children
                };

                await SendJsonAsync(response, result);
            });
        }

        private async Task HandleCreateActorAsync(HttpListenerResponse response, string requestBody)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    var json = JsonDocument.Parse(requestBody).RootElement;

                    var typeStr = json.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "EmptyActor";
                    var actorName = json.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var parentName = json.TryGetProperty("parent", out var parentProp) ? parentProp.GetString() : null;

                    Vector3? position = null;
                    if (json.TryGetProperty("position", out var posProp))
                    {
                        var x = posProp.TryGetProperty("X", out var xProp) ? xProp.GetSingle() : 0f;
                        var y = posProp.TryGetProperty("Y", out var yProp) ? yProp.GetSingle() : 0f;
                        var z = posProp.TryGetProperty("Z", out var zProp) ? zProp.GetSingle() : 0f;
                        position = new Vector3(x, y, z);
                    }

                    Actor newActor = null;

                    switch (typeStr)
                    {
                        case "EmptyActor":
                            newActor = Actor.New<EmptyActor>();
                            break;
                        case "StaticModel":
                            newActor = Actor.New<StaticModel>();
                            break;
                        case "Camera":
                            newActor = Actor.New<Camera>();
                            break;
                        case "DirectionalLight":
                            newActor = Actor.New<DirectionalLight>();
                            break;
                        case "PointLight":
                            newActor = Actor.New<PointLight>();
                            break;
                        case "SpotLight":
                            newActor = Actor.New<SpotLight>();
                            break;
                        case "AnimatedModel":
                            newActor = Actor.New<AnimatedModel>();
                            break;
                        case "BoxCollider":
                            newActor = Actor.New<BoxCollider>();
                            break;
                        case "SphereCollider":
                            newActor = Actor.New<SphereCollider>();
                            break;
                        case "CapsuleCollider":
                            newActor = Actor.New<CapsuleCollider>();
                            break;
                        case "CharacterController":
                            newActor = Actor.New<CharacterController>();
                            break;
                        case "RigidBody":
                            newActor = Actor.New<RigidBody>();
                            break;
                        case "UIControl":
                            newActor = Actor.New<UIControl>();
                            break;
                        case "UICanvas":
                            newActor = Actor.New<UICanvas>();
                            break;
                        case "TextRender":
                            newActor = Actor.New<TextRender>();
                            break;
                        case "AudioSource":
                            newActor = Actor.New<AudioSource>();
                            break;
                        case "AudioListener":
                            newActor = Actor.New<AudioListener>();
                            break;
                        default:
                            await SendJsonAsync(response, new { error = "Unknown actor type: " + typeStr }, 400);
                            return;
                    }

                    if (!string.IsNullOrEmpty(actorName))
                    {
                        newActor.Name = actorName;
                    }

                    if (position.HasValue)
                    {
                        newActor.Position = position.Value;
                    }

                    if (!string.IsNullOrEmpty(parentName))
                    {
                        var parentActor = FindActorByName(parentName);
                        if (parentActor != null)
                        {
                            newActor.Parent = parentActor;
                        }
                        else
                        {
                            Debug.LogWarning("[TraeBridge] 父级 Actor 未找到: " + parentName + "，将添加到场景根节点");
                            var scenes = Level.Scenes;
                            if (scenes != null && scenes.Length > 0)
                            {
                                newActor.Parent = scenes[0];
                            }
                        }
                    }
                    else
                    {
                        var scenes = Level.Scenes;
                        if (scenes != null && scenes.Length > 0)
                        {
                            newActor.Parent = scenes[0];
                            Debug.Log("[TraeBridge] 已将 Actor 添加到场景: " + newActor.Name);
                        }
                        else
                        {
                            Debug.LogWarning("[TraeBridge] 没有活动场景，无法添加 Actor");
                        }
                    }

                    var result = new
                    {
                        name = newActor.Name,
                        type = newActor.GetType().Name,
                        id = newActor.ID.ToString(),
                        position = new { x = newActor.Transform.Translation.X, y = newActor.Transform.Translation.Y, z = newActor.Transform.Translation.Z }
                    };

                    await SendJsonAsync(response, result, 201);
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(response, new { error = "创建 Actor 失败: " + ex.Message }, 500);
                }
            });
        }

        private async Task HandleUpdateTransformAsync(HttpListenerResponse response, string actorName, string requestBody)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                var actor = FindActorByName(actorName);
                if (actor == null)
                {
                    await SendJsonAsync(response, new { error = "Actor not found: " + actorName }, 404);
                    return;
                }

                try
                {
                    var json = JsonDocument.Parse(requestBody).RootElement;

                    if (json.TryGetProperty("position", out var posProp))
                    {
                        var x = posProp.TryGetProperty("x", out var px) ? px.GetSingle() : posProp.TryGetProperty("X", out var pX) ? pX.GetSingle() : 0f;
                        var y = posProp.TryGetProperty("y", out var py) ? py.GetSingle() : posProp.TryGetProperty("Y", out var pY) ? pY.GetSingle() : 0f;
                        var z = posProp.TryGetProperty("z", out var pz) ? pz.GetSingle() : posProp.TryGetProperty("Z", out var pZ) ? pZ.GetSingle() : 0f;
                        actor.Position = new Vector3(x, y, z);
                    }

                    if (json.TryGetProperty("rotation", out var rotProp))
                    {
                        var x = rotProp.TryGetProperty("x", out var rx) ? rx.GetSingle() : rotProp.TryGetProperty("X", out var rX) ? rX.GetSingle() : 0f;
                        var y = rotProp.TryGetProperty("y", out var ry) ? ry.GetSingle() : rotProp.TryGetProperty("Y", out var rY) ? rY.GetSingle() : 0f;
                        var z = rotProp.TryGetProperty("z", out var rz) ? rz.GetSingle() : rotProp.TryGetProperty("Z", out var rZ) ? rZ.GetSingle() : 0f;
                        actor.Orientation = Quaternion.Euler(x, y, z);
                    }

                    if (json.TryGetProperty("scale", out var scaleProp))
                    {
                        var x = scaleProp.TryGetProperty("x", out var sx) ? sx.GetSingle() : scaleProp.TryGetProperty("X", out var sX) ? sX.GetSingle() : 1f;
                        var y = scaleProp.TryGetProperty("y", out var sy) ? sy.GetSingle() : scaleProp.TryGetProperty("Y", out var sY) ? sY.GetSingle() : 1f;
                        var z = scaleProp.TryGetProperty("z", out var sz) ? sz.GetSingle() : scaleProp.TryGetProperty("Z", out var sZ) ? sZ.GetSingle() : 1f;
                        actor.Scale = new Vector3(x, y, z);
                    }

                    var result = new
                    {
                        name = actor.Name,
                        position = new { x = actor.Position.X, y = actor.Position.Y, z = actor.Position.Z },
                        rotation = new { x = actor.Orientation.EulerAngles.X, y = actor.Orientation.EulerAngles.Y, z = actor.Orientation.EulerAngles.Z },
                        scale = new { x = actor.Scale.X, y = actor.Scale.Y, z = actor.Scale.Z }
                    };

                    await SendJsonAsync(response, result);
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(response, new { error = "更新 Transform 失败: " + ex.Message }, 500);
                }
            });
        }

        private async Task HandleDeleteActorAsync(HttpListenerResponse response, string actorName)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                var actor = FindActorByName(actorName);
                if (actor == null)
                {
                    await SendJsonAsync(response, new { error = "Actor not found: " + actorName }, 404);
                    return;
                }

                Actor.Destroy(actor);
                await SendJsonAsync(response, new { status = "deleted", actor = actorName });
            });
        }

        private async Task HandleSetPropertyAsync(HttpListenerResponse response, string actorName, string requestBody)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                var actor = FindActorByName(actorName);
                if (actor == null)
                {
                    await SendJsonAsync(response, new { error = "Actor not found: " + actorName }, 404);
                    return;
                }

                try
                {
                    var json = JsonDocument.Parse(requestBody).RootElement;
                    var propertyName = json.TryGetProperty("property", out var propProp) ? propProp.GetString() : null;
                    var valueKind = json.TryGetProperty("value", out var valProp) ? valProp.ValueKind : JsonValueKind.Undefined;

                    if (string.IsNullOrEmpty(propertyName) || valueKind == JsonValueKind.Undefined)
                    {
                        await SendJsonAsync(response, new { error = "Missing property name or value" }, 400);
                        return;
                    }

                    var propInfo = actor.GetType().GetProperty(propertyName);
                    if (propInfo == null || !propInfo.CanWrite)
                    {
                        foreach (var comp in actor.Scripts)
                        {
                            propInfo = comp.GetType().GetProperty(propertyName);
                            if (propInfo != null && propInfo.CanWrite)
                            {
                                object SetVal(Type targetType)
                                {
                                    if (targetType == typeof(int)) return valProp.GetInt32();
                                    if (targetType == typeof(float)) return valProp.GetSingle();
                                    if (targetType == typeof(double)) return valProp.GetDouble();
                                    if (targetType == typeof(bool)) return valProp.GetBoolean();
                                    if (targetType == typeof(string)) return valProp.GetString();
                                    if (targetType.IsEnum) return Enum.ToObject(targetType, valProp.GetInt32());
                                    // Support Asset references by path
                                    if (typeof(Asset).IsAssignableFrom(targetType) && valueKind == JsonValueKind.String)
                                    {
                                        var assetPath = valProp.GetString();
                                        var loadMethod = typeof(Content).GetMethods()
                                            .FirstOrDefault(m => m.Name == "LoadAsync" && m.IsGenericMethod && m.GetParameters().Length == 1);
                                        if (loadMethod != null)
                                        {
                                            var genericLoad = loadMethod.MakeGenericMethod(targetType);
                                            return genericLoad.Invoke(null, new object[] { assetPath });
                                        }
                                    }
                                    return null;
                                }
                                var val = SetVal(propInfo.PropertyType);
                                if (val != null)
                                {
                                    propInfo.SetValue(comp, val);
                                    await SendJsonAsync(response, new { status = "set", actor = actorName, property = propertyName, value = val, on = comp.GetType().Name });
                                    return;
                                }
                            }
                        }
                        await SendJsonAsync(response, new { error = "Property not found or not writable: " + propertyName }, 400);
                        return;
                    }

                    object SetValue(Type targetType)
                    {
                        if (targetType == typeof(int)) return valProp.GetInt32();
                        if (targetType == typeof(float)) return valProp.GetSingle();
                        if (targetType == typeof(double)) return valProp.GetDouble();
                        if (targetType == typeof(bool)) return valProp.GetBoolean();
                        if (targetType == typeof(string)) return valProp.GetString();
                        if (targetType.IsEnum) return Enum.ToObject(targetType, valProp.GetInt32());
                        // Support Asset references by path (e.g., Model, Material, Texture)
                        if (typeof(Asset).IsAssignableFrom(targetType) && valueKind == JsonValueKind.String)
                        {
                            var assetPath = valProp.GetString();
                            var asset = Content.LoadAsync(assetPath);
                            if (asset != null && targetType.IsInstanceOfType(asset))
                                return asset;
                            // Try loading with specific type
                            var loadMethod = typeof(Content).GetMethods()
                                .FirstOrDefault(m => m.Name == "LoadAsync" && m.IsGenericMethod && m.GetParameters().Length == 1);
                            if (loadMethod != null)
                            {
                                var genericLoad = loadMethod.MakeGenericMethod(targetType);
                                var loaded = genericLoad.Invoke(null, new object[] { assetPath });
                                if (loaded != null) return loaded;
                            }
                            return null;
                        }
                        return null;
                    }

                    var value = SetValue(propInfo.PropertyType);
                    if (value == null)
                    {
                        await SendJsonAsync(response, new { error = "Unsupported property type: " + propInfo.PropertyType.Name }, 400);
                        return;
                    }

                    propInfo.SetValue(actor, value);
                    await SendJsonAsync(response, new { status = "set", actor = actorName, property = propertyName, value = value });
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(response, new { error = "设置属性失败: " + ex.Message }, 500);
                }
            });
        }

        private async Task HandleAddComponentAsync(HttpListenerResponse response, string actorName, string requestBody)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                var actor = FindActorByName(actorName);
                if (actor == null)
                {
                    await SendJsonAsync(response, new { error = "Actor not found: " + actorName }, 404);
                    return;
                }

                try
                {
                    var json = JsonDocument.Parse(requestBody).RootElement;
                    var typeStr = json.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

                    if (string.IsNullOrEmpty(typeStr))
                    {
                        await SendJsonAsync(response, new { error = "Missing component type" }, 400);
                        return;
                    }

                    Type componentType = FindFlaxType(typeStr);
                    if (componentType == null)
                    {
                        await SendJsonAsync(response, new { error = "Unknown type: " + typeStr }, 400);
                        return;
                    }

                    if (typeof(Script).IsAssignableFrom(componentType))
                    {
                        var addScriptMethod = typeof(Actor).GetMethods()
                            .FirstOrDefault(m => m.Name == "AddScript" && m.IsGenericMethod);
                        var genericMethod = addScriptMethod?.MakeGenericMethod(componentType);
                        var newScript = genericMethod?.Invoke(actor, null) as Script;

                        if (newScript == null)
                        {
                            await SendJsonAsync(response, new { error = "Failed to add script: " + typeStr }, 500);
                            return;
                        }

                        await SendJsonAsync(response, new
                        {
                            status = "added",
                            actor = actorName,
                            component = new { type = typeStr, name = newScript.GetType().Name }
                        }, 201);
                        return;
                    }

                    if (!typeof(Actor).IsAssignableFrom(componentType))
                    {
                        await SendJsonAsync(response, new { error = "Type is not an Actor or Script: " + typeStr }, 400);
                        return;
                    }

                    var addChildMethod = typeof(Actor).GetMethod("AddChild", Type.EmptyTypes);
                    var genericAddChildMethod = addChildMethod?.MakeGenericMethod(componentType);
                    var newComponent = genericAddChildMethod?.Invoke(actor, null) as Actor;

                    if (newComponent == null)
                    {
                        await SendJsonAsync(response, new { error = "Failed to create component: " + typeStr }, 500);
                        return;
                    }

                    await SendJsonAsync(response, new
                    {
                        status = "added",
                        actor = actorName,
                        component = new { type = typeStr, name = newComponent.GetType().Name }
                    }, 201);
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(response, new { error = "添加组件失败: " + ex.Message }, 500);
                }
            });
        }

        private async Task HandleRemoveComponentAsync(HttpListenerResponse response, string actorName, string componentType)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                var actor = FindActorByName(actorName);
                if (actor == null)
                {
                    await SendJsonAsync(response, new { error = "Actor not found: " + actorName }, 404);
                    return;
                }

                // Search in Scripts first
                Script targetScript = null;
                foreach (var script in actor.Scripts)
                {
                    if (script.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase))
                    {
                        targetScript = script;
                        break;
                    }
                }

                if (targetScript != null)
                {
                    FlaxEngine.Object.Destroy(targetScript);
                    await SendJsonAsync(response, new { status = "removed", actor = actorName, component = componentType, location = "scripts" });
                    return;
                }

                // Then search in Children (Actor subtypes)
                Actor targetChild = null;
                foreach (var child in actor.Children)
                {
                    if (child.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase))
                    {
                        targetChild = child;
                        break;
                    }
                }

                if (targetChild == null)
                {
                    await SendJsonAsync(response, new { error = "Component not found: " + componentType }, 404);
                    return;
                }

                FlaxEngine.Object.Destroy(targetChild);
                await SendJsonAsync(response, new { status = "removed", actor = actorName, component = componentType, location = "children" });
            });
        }

        private async Task HandleAssetsListAsync(HttpListenerResponse response, string queryString)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    // Parse type filter from query string
                    string typeFilter = null;
                    if (!string.IsNullOrEmpty(queryString) && queryString.StartsWith("?"))
                    {
                        var pairs = queryString.Substring(1).Split('&');
                        foreach (var pair in pairs)
                        {
                            var kv = pair.Split(new[] { '=' }, 2);
                            if (kv.Length == 2 && kv[0].Equals("type", StringComparison.OrdinalIgnoreCase))
                            {
                                typeFilter = Uri.UnescapeDataString(kv[1]);
                                break;
                            }
                        }
                    }

                    var assets = new List<object>();
                    var contentPath = Path.Combine(Globals.ProjectFolder, "Content");

                    if (Directory.Exists(contentPath))
                    {
                        var files = Directory.GetFiles(contentPath, "*.*", SearchOption.AllDirectories)
                            .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(f => f)
                            .Take(500);

                        foreach (var file in files)
                        {
                            var relativePath = file.Substring(contentPath.Length + 1).Replace('\\', '/');
                            var extension = Path.GetExtension(file);
                            var name = Path.GetFileNameWithoutExtension(file);

                            // Map asset type based on extension and path
                            string assetType = null;
                            if (extension.Equals(".flax", StringComparison.OrdinalIgnoreCase))
                            {
                                if (relativePath.Contains("/Models/", StringComparison.OrdinalIgnoreCase))
                                    assetType = "Model";
                                else if (relativePath.Contains("/Textures/", StringComparison.OrdinalIgnoreCase))
                                    assetType = "Texture";
                                else if (relativePath.Contains("/Materials/", StringComparison.OrdinalIgnoreCase))
                                    assetType = "Material";
                                else if (relativePath.Contains("/Prefabs/", StringComparison.OrdinalIgnoreCase))
                                    assetType = "Prefab";
                                else if (relativePath.Contains("/Animations/", StringComparison.OrdinalIgnoreCase))
                                    assetType = "Animation";
                                else if (relativePath.Contains("/Audio/", StringComparison.OrdinalIgnoreCase))
                                    assetType = "Audio";
                            }
                            else if (extension.Equals(".scene", StringComparison.OrdinalIgnoreCase))
                            {
                                assetType = "Scene";
                            }

                            // Apply type filter
                            if (typeFilter != null && !string.Equals(assetType, typeFilter, StringComparison.OrdinalIgnoreCase))
                                continue;

                            assets.Add(new
                            {
                                path = relativePath,
                                name = name,
                                type = assetType ?? "Unknown",
                                extension = extension,
                                size = new FileInfo(file).Length
                            });
                        }
                    }

                    await SendJsonAsync(response, new
                    {
                        contentPath = contentPath,
                        count = assets.Count,
                        assets = assets
                    });
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(response, new { error = "获取资源列表失败: " + ex.Message }, 500);
                }
            });
        }

        private async Task HandleImportAssetAsync(HttpListenerResponse response, string requestBody)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    var json = JsonDocument.Parse(requestBody).RootElement;

                    var sourcePath = json.TryGetProperty("sourcePath", out var srcProp) ? srcProp.GetString() : null;
                    var targetPath = json.TryGetProperty("targetPath", out var tgtProp) ? tgtProp.GetString() : null;

                    if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath))
                    {
                        await SendJsonAsync(response, new { error = "Missing sourcePath or targetPath" }, 400);
                        return;
                    }

                    if (!File.Exists(sourcePath))
                    {
                        await SendJsonAsync(response, new { error = "Source file not found: " + sourcePath }, 404);
                        return;
                    }

                    var fullTargetPath = Path.Combine(Globals.ProjectFolder, targetPath);
                    var targetDir = Path.GetDirectoryName(fullTargetPath);

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(sourcePath, fullTargetPath, overwrite: true);

                    await SendJsonAsync(response, new
                    {
                        status = "imported",
                        source = sourcePath,
                        target = targetPath
                    });
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(response, new { error = "导入资源失败: " + ex.Message }, 500);
                }
            });
        }

        private async Task HandleSaveSceneAsync(HttpListenerResponse response)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                await SendJsonAsync(response, new
                {
                    status = "unavailable",
                    message = "请在 Flax Editor 中按 Ctrl+S 保存场景"
                });
            });
        }

        private async Task HandleScreenshotAsync(HttpListenerResponse response)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    // Use Screenshot.Capture to save to temp file, then read and return as base64
                    var tempDir = Path.Combine(Path.GetTempPath(), "TraeBridge");
                    if (!Directory.Exists(tempDir))
                        Directory.CreateDirectory(tempDir);
                    var tempFile = Path.Combine(tempDir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

                    Screenshot.Capture(tempFile);

                    // Wait briefly for the screenshot to be saved
                    await System.Threading.Tasks.Task.Delay(500);

                    if (File.Exists(tempFile))
                    {
                        var bytes = File.ReadAllBytes(tempFile);
                        var base64 = Convert.ToBase64String(bytes);
                        try { File.Delete(tempFile); } catch { }

                        await SendJsonAsync(response, new
                        {
                            format = "png",
                            data = base64,
                            size = bytes.Length
                        });
                    }
                    else
                    {
                        var camera = Camera.MainCamera;
                        await SendJsonAsync(response, new
                        {
                            error = "Screenshot file not created",
                            suggestion = "Screenshot.Capture may not work in this context. Try running in Editor mode.",
                            mainCamera = camera != null ? new
                            {
                                name = camera.Name,
                                posX = camera.Position.X,
                                posY = camera.Position.Y,
                                posZ = camera.Position.Z
                            } : (object)null
                        });
                    }
                }
                catch (Exception ex)
                {
                    var camera = Camera.MainCamera;
                    await SendJsonAsync(response, new
                    {
                        error = "Screenshot failed: " + ex.Message,
                        suggestion = "Game assembly has limited screenshot support. Use editor mode for full screenshot capability.",
                        mainCamera = camera != null ? new
                        {
                            name = camera.Name,
                            posX = camera.Position.X,
                            posY = camera.Position.Y,
                            posZ = camera.Position.Z
                        } : (object)null
                    });
                }
            });
        }

        private async Task HandleExecuteCodeAsync(HttpListenerResponse response, string requestBody)
        {
            await InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    var json = JsonDocument.Parse(requestBody).RootElement;
                    var code = json.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;

                    if (string.IsNullOrEmpty(code))
                    {
                        await SendJsonAsync(response, new { error = "Missing code to execute" }, 400);
                        return;
                    }

                    object lastResult = null;

                    // Support "raw" mode for full C# expression execution via reflection
                    var mode = json.TryGetProperty("mode", out var modeProp) ? modeProp.GetString() : "simple";

                    if (mode == "raw")
                    {
                        // Execute raw C# code using reflection-based evaluation
                        lastResult = ExecuteRawCode(code);
                    }
                    else
                    {
                        var statements = code.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var stmt in statements)
                        {
                            var trimmed = stmt.Trim();
                            if (string.IsNullOrEmpty(trimmed)) continue;

                            if (trimmed.StartsWith("Debug.Log"))
                            {
                                var msgStart = trimmed.IndexOf('"') + 1;
                                var msgEnd = trimmed.LastIndexOf('"');
                                if (msgStart > 0 && msgEnd > msgStart)
                                {
                                    var msg = trimmed.Substring(msgStart, msgEnd - msgStart);
                                    FlaxEngine.Debug.Log(msg);
                                    lastResult = msg;
                                }
                            }
                            else if (trimmed == "Editor.SaveScene" || trimmed == "Editor.SaveScene()")
                            {
                                lastResult = "Save not available from Game assembly, use Ctrl+S in Editor";
                            }
                            else if (trimmed == "Editor.SaveScenes" || trimmed == "Editor.SaveScenes()")
                            {
                                lastResult = "Save not available from Game assembly, use Ctrl+S in Editor";
                            }
                            else if (trimmed.StartsWith("Level.LoadScene"))
                            {
                                var argStart = trimmed.IndexOf('"') + 1;
                                var argEnd = trimmed.LastIndexOf('"');
                                if (argStart > 0 && argEnd > argStart)
                                {
                                    var scenePath = trimmed.Substring(argStart, argEnd - argStart);
                                    var sceneAsset = Content.LoadAsync<SceneAsset>(scenePath);
                                    if (sceneAsset != null)
                                    {
                                        Level.LoadScene(sceneAsset.ID);
                                        lastResult = "Loading scene: " + scenePath;
                                    }
                                    else
                                    {
                                        lastResult = "Scene asset not found: " + scenePath;
                                    }
                                }
                                else
                                {
                                    lastResult = "Invalid Level.LoadScene syntax, use: Level.LoadScene(\"path\")";
                                }
                            }
                            else
                            {
                                lastResult = ExecuteRawCode(trimmed);
                            }
                        }
                    }

                    await SendJsonAsync(response, new
                    {
                        status = "executed",
                        result = lastResult ?? "(no output)"
                    });
                }
                catch (Exception ex)
                {
                    await SendJsonAsync(response, new { error = "代码执行失败: " + ex.Message }, 500);
                }
            });
        }

        /// <summary>
        /// Execute raw C# code using reflection. Supports:
        /// - Level.FindActor("name")
        /// - actorName.Method()
        /// - Level.FindActor("name").Property = value
        /// - actor.Property = value
        /// - Content.LoadAsync{Type}("path")
        /// - Level.SpawnActor(actor)
        /// - actor.AddScript{Type}()
        /// </summary>
        private object ExecuteRawCode(string code)
        {
            var trimmed = code.Trim();

            // Level.FindActor("name")
            if (trimmed.StartsWith("Level.FindActor("))
            {
                var argStart = trimmed.IndexOf('"') + 1;
                var argEnd = trimmed.LastIndexOf('"');
                if (argStart > 0 && argEnd > argStart)
                {
                    var actorName = trimmed.Substring(argStart, argEnd - argStart);
                    var actor = Level.FindActor(actorName);
                    return actor != null ? $"Found actor: {actor.Name} ({actor.GetType().Name})" : $"Actor not found: {actorName}";
                }
            }

            // actorName.Method() - call method on actor via reflection
            if (trimmed.Contains(".") && trimmed.EndsWith("()") && !trimmed.StartsWith("Level.") && !trimmed.StartsWith("Content."))
            {
                var dotIdx = trimmed.IndexOf('.');
                if (dotIdx > 0)
                {
                    var actorName = trimmed.Substring(0, dotIdx);
                    var rest = trimmed.Substring(dotIdx + 1);
                    var parenIdx = rest.IndexOf('(');
                    if (parenIdx > 0)
                    {
                        var methodName = rest.Substring(0, parenIdx);
                        var actor = Level.FindActor(actorName);
                        if (actor != null)
                        {
                            var method = actor.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (method != null)
                            {
                                var result = method.Invoke(actor, null);
                                return result != null ? $"Called {methodName}() on {actorName}: {result}" : $"Called {methodName}() on {actorName}";
                            }
                            // Also try on scripts
                            foreach (var script in actor.Scripts)
                            {
                                var scriptMethod = script.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                if (scriptMethod != null)
                                {
                                    var result = scriptMethod.Invoke(script, null);
                                    return result != null ? $"Called {methodName}() on {actorName}.{script.GetType().Name}: {result}" : $"Called {methodName}() on {actorName}.{script.GetType().Name}";
                                }
                            }
                            return $"Method not found: {methodName} on {actorName}";
                        }
                        return $"Actor not found: {actorName}";
                    }
                }
            }

            // Level.FindScript<Type>()
            if (trimmed.StartsWith("Level.FindScript<"))
            {
                var typeStart = trimmed.IndexOf('<') + 1;
                var typeEnd = trimmed.IndexOf('>');
                if (typeStart > 0 && typeEnd > typeStart)
                {
                    var typeName = trimmed.Substring(typeStart, typeEnd - typeStart);
                    var type = FindFlaxType(typeName);
                    if (type != null)
                    {
                        var findMethod = typeof(Level).GetMethods()
                            .FirstOrDefault(m => m.Name == "FindScript" && m.IsGenericMethod);
                        if (findMethod != null)
                        {
                            var genericFind = findMethod.MakeGenericMethod(type);
                            var result = genericFind.Invoke(null, null);
                            return result != null ? $"Found script: {result.GetType().Name}" : $"Script not found: {typeName}";
                        }
                    }
                    return $"Unknown type: {typeName}";
                }
            }

            // Level.SpawnActor - handled via POST /api/scene/actor
            if (trimmed.StartsWith("Level.SpawnActor"))
            {
                return "Use POST /api/scene/actor to create actors";
            }

            // Content.LoadAsync<Type>("path")
            if (trimmed.StartsWith("Content.LoadAsync<"))
            {
                var typeStart = trimmed.IndexOf('<') + 1;
                var typeEnd = trimmed.IndexOf('>');
                if (typeStart > 0 && typeEnd > typeStart)
                {
                    var typeName = trimmed.Substring(typeStart, typeEnd - typeStart);
                    var type = FindFlaxType(typeName) ?? Type.GetType(typeName);
                    if (type != null)
                    {
                        var argStart = trimmed.IndexOf('"', typeEnd) + 1;
                        var argEnd = trimmed.LastIndexOf('"');
                        if (argStart > 0 && argEnd > argStart)
                        {
                            var path = trimmed.Substring(argStart, argEnd - argStart);
                            var loadMethod = typeof(Content).GetMethods()
                                .FirstOrDefault(m => m.Name == "LoadAsync" && m.IsGenericMethod && m.GetParameters().Length == 1);
                            if (loadMethod != null)
                            {
                                var genericLoad = loadMethod.MakeGenericMethod(type);
                                var asset = genericLoad.Invoke(null, new object[] { path });
                                return asset != null ? $"Loaded: {path} as {type.Name}" : $"Failed to load: {path}";
                            }
                        }
                    }
                    return $"Unknown type: {typeName}";
                }
            }

            // Level.FindActor("name").Property = value
            if (trimmed.StartsWith("Level.FindActor(") && trimmed.Contains(")."))
            {
                var closeParen = trimmed.IndexOf(')');
                if (closeParen > 0 && trimmed.Length > closeParen + 2)
                {
                    var afterClose = trimmed.Substring(closeParen + 1);
                    if (afterClose.StartsWith("."))
                    {
                        var actorArgStart = trimmed.IndexOf('"') + 1;
                        var actorArgEnd = trimmed.IndexOf('"', actorArgStart);
                        if (actorArgStart > 0 && actorArgEnd > actorArgStart)
                        {
                            var aName = trimmed.Substring(actorArgStart, actorArgEnd - actorArgStart);
                            var actor = Level.FindActor(aName);
                            if (actor != null)
                            {
                                var rest = afterClose.Substring(1); // skip the dot
                                var eqIdx = rest.IndexOf('=');
                                if (eqIdx > 0)
                                {
                                    var propName = rest.Substring(0, eqIdx).Trim();
                                    var valueStr = rest.Substring(eqIdx + 1).Trim();
                                    return SetActorProperty(actor, propName, valueStr);
                                }
                            }
                            return $"Actor not found: {aName}";
                        }
                    }
                }
            }

            // actorName.Property = value  (e.g., CharacterPlatform.Model = "Engine/BasicModels/Box")
            if (trimmed.Contains("."))
            {
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    var leftSide = trimmed.Substring(0, eqIdx).Trim();
                    var rightSide = trimmed.Substring(eqIdx + 1).Trim();
                    var dotIdx = leftSide.IndexOf('.');
                    if (dotIdx > 0)
                    {
                        var actorName = leftSide.Substring(0, dotIdx);
                        var propName = leftSide.Substring(dotIdx + 1);
                        var actor = Level.FindActor(actorName);
                        if (actor != null)
                        {
                            return SetActorProperty(actor, propName, rightSide);
                        }
                        return $"Actor not found: {actorName}";
                    }
                }
            }

            return "Unsupported: " + trimmed;
        }

        private object SetActorProperty(Actor actor, string propertyName, string valueStr)
        {
            // Try on actor first, then on its scripts/components
            var targets = new List<object> { actor };
            foreach (var script in actor.Scripts)
                targets.Add(script);

            foreach (var target in targets)
            {
                var propInfo = target.GetType().GetProperty(propertyName);
                if (propInfo != null && propInfo.CanWrite)
                {
                    object value = null;
                    var propType = propInfo.PropertyType;

                    // String value (possibly quoted)
                    var unquoted = valueStr.Trim('"').Trim('\'');

                    if (propType == typeof(string))
                        value = unquoted;
                    else if (propType == typeof(int) && int.TryParse(unquoted, out var intVal))
                        value = intVal;
                    else if (propType == typeof(float) && float.TryParse(unquoted, out var floatVal))
                        value = floatVal;
                    else if (propType == typeof(double) && double.TryParse(unquoted, out var doubleVal))
                        value = doubleVal;
                    else if (propType == typeof(bool) && bool.TryParse(unquoted, out var boolVal))
                        value = boolVal;
                    else if (propType.IsEnum && int.TryParse(unquoted, out var enumVal))
                        value = Enum.ToObject(propType, enumVal);
                    else if (typeof(Asset).IsAssignableFrom(propType))
                    {
                        // Load asset by path
                        var loadMethod = typeof(Content).GetMethods()
                            .FirstOrDefault(m => m.Name == "LoadAsync" && m.IsGenericMethod && m.GetParameters().Length == 1);
                        if (loadMethod != null)
                        {
                            var genericLoad = loadMethod.MakeGenericMethod(propType);
                            var asset = genericLoad.Invoke(null, new object[] { unquoted }) as Asset;
                            if (asset != null)
                            {
                                asset.WaitForLoaded();
                                if (asset.IsLoaded)
                                {
                                    value = asset;
                                }
                            }
                        }
                    }
                    else if (propType == typeof(Guid))
                    {
                        // Asset reference stored as Guid - load asset then use its ID
                        var assetType = typeof(Asset);
                        // Try to infer asset type from property name
                        if (propertyName.Contains("Model") || propertyName.Contains("Mesh"))
                            assetType = typeof(Model);
                        else if (propertyName.Contains("Material"))
                            assetType = typeof(Material);
                        else if (propertyName.Contains("Texture"))
                            assetType = typeof(Texture);

                        var loadMethod = typeof(Content).GetMethods()
                            .FirstOrDefault(m => m.Name == "LoadAsync" && m.IsGenericMethod && m.GetParameters().Length == 1);
                        if (loadMethod != null)
                        {
                            var genericLoad = loadMethod.MakeGenericMethod(assetType);
                            var asset = genericLoad.Invoke(null, new object[] { unquoted }) as Asset;
                            if (asset != null)
                            {
                                asset.WaitForLoaded();
                                if (asset.IsLoaded)
                                {
                                    value = asset.ID;
                                }
                            }
                        }
                    }

                    if (value != null)
                    {
                        propInfo.SetValue(target, value);
                        return $"Set {propertyName} = {valueStr} on {target.GetType().Name}";
                    }
                    return $"Unsupported property type: {propType.Name}";
                }
            }
            return $"Property not found: {propertyName}";
        }

        // ==================== Utility Methods ====================

        private Actor FindActorByName(string name)
        {
            var scenes = Level.Scenes;
            foreach (var scene in scenes)
            {
                if (scene == null) continue;

                var actors = scene.Children;
                if (actors == null) continue;

                foreach (var actor in actors)
                {
                    var found = FindActorInTree(actor, name);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private Actor FindActorInTree(Actor root, string name)
        {
            if (root == null)
                return null;

            if (string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase))
                return root;

            foreach (var child in root.Children)
            {
                var found = FindActorInTree(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private Type FindFlaxType(string typeName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                var type = assembly.GetType("FlaxEngine." + typeName, false);
                if (type != null)
                    return type;

                type = assembly.GetType(typeName, false);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
