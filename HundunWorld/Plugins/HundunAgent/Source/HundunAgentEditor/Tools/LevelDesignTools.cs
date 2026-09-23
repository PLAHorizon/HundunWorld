using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEditor.SceneGraph;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 关卡设计工具：批量布置（网格/环绕/直线 + 随机抖动/旋转/缩放/贴地）、
    /// 批量对齐与均匀分布、地面吸附。所有变更走编辑器 Undo 流程，
    /// 批量操作按实际生成数量计入撤销计数（undo_rollback 可整体回滚）。
    /// </summary>
    public static class LevelDesignTools
    {
        /// <summary>单次批量布置上限（防滥用）。</summary>
        public const int MaxScatterCount = 200;

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "level_scatter",
                Description = "批量布置 Actor（prefab 资产路径或 Actor 类型名作为 source）。pattern=grid（网格，spacingX/spacingZ）/circle（环绕，radius）/line（直线，from/to）。支持 center 中心、jitterX/jitterZ 抖动、rotationYawRandom 随机朝向、scaleMin/scaleMax 随机缩放、snapGround 贴地、offsetY、parent、namePrefix、seed（复现随机序列）。count 上限 200。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"source\":{\"type\":\"string\",\"description\":\"prefab 资产路径（如 Content/Prefabs/Tree.prefab）或 Actor 类型名（如 StaticModel）\"},\"pattern\":{\"type\":\"string\",\"description\":\"grid|circle|line，默认grid\"},\"count\":{\"type\":\"integer\",\"description\":\"数量，默认10，上限200\"},\"center\":{\"type\":\"object\",\"description\":\"中心点{x,y,z}\"},\"spacingX\":{\"type\":\"number\"},\"spacingZ\":{\"type\":\"number\"},\"radius\":{\"type\":\"number\"},\"from\":{\"type\":\"object\"},\"to\":{\"type\":\"object\"},\"jitterX\":{\"type\":\"number\"},\"jitterZ\":{\"type\":\"number\"},\"rotationYawRandom\":{\"type\":\"number\",\"description\":\"随机偏航角范围（±度）\"},\"scaleMin\":{\"type\":\"number\"},\"scaleMax\":{\"type\":\"number\"},\"snapGround\":{\"type\":\"boolean\"},\"offsetY\":{\"type\":\"number\"},\"parent\":{\"type\":\"string\"},\"namePrefix\":{\"type\":\"string\"},\"seed\":{\"type\":\"integer\"}},\"required\":[\"source\"]}",
                Undoable = true,
                Execute = LevelScatterAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "level_align",
                Description = "批量对齐/均匀分布 Actor 的位置。axis=x/y/z，mode=min/max/center/average（对齐到的目标值）。distribute=true 时按当前轴值排序均匀分布（spacing 指定则等间距，否则在 min~max 间均分）。targets 缺省使用当前选中。操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"targets\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Actor 名称/Guid 数组，缺省用当前选中\"},\"axis\":{\"type\":\"string\",\"description\":\"x|y|z，默认x\"},\"mode\":{\"type\":\"string\",\"description\":\"min|max|center|average，默认center\"},\"distribute\":{\"type\":\"boolean\"},\"spacing\":{\"type\":\"number\",\"description\":\"分布间距，缺省在min~max间均分\"}}}",
                Undoable = true,
                Execute = LevelAlignAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "level_snap_ground",
                Description = "把 Actor 吸附到下方地面（Physics.RayCast 向下探测）。rayHeight 起始高度（默认200）、maxDistance 探测距离（默认500）、offsetY 抬升、alignToNormal 对齐地面法线、offsetAlongNormal 沿法线偏移。targets 缺省使用当前选中。编辑态物理可能未模拟导致未命中（会逐个报告）；操作可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"targets\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"rayHeight\":{\"type\":\"number\"},\"maxDistance\":{\"type\":\"number\"},\"offsetY\":{\"type\":\"number\"},\"alignToNormal\":{\"type\":\"boolean\"},\"offsetAlongNormal\":{\"type\":\"number\"}}}",
                Undoable = true,
                Execute = LevelSnapGroundAsync
            });
        }

        // ==================== Handlers ====================

        private static Task<object> LevelScatterAsync(JsonElement args)
        {
            var source = EditorUtils.GetString(args, "source");
            if (string.IsNullOrEmpty(source))
                throw new ArgumentException("缺少 source（prefab 资产路径或 Actor 类型名）");

            var pattern = (EditorUtils.GetString(args, "pattern", "grid") ?? "grid").ToLowerInvariant();
            if (pattern != "grid" && pattern != "circle" && pattern != "line")
                throw new ArgumentException("pattern 必须是 grid/circle/line");

            var count = Math.Clamp(EditorUtils.GetInt(args, "count", 10), 1, MaxScatterCount);
            var spacingX = EditorUtils.GetNum(args, "spacingX", 2f);
            var spacingZ = EditorUtils.GetNum(args, "spacingZ", 2f);
            var radius = EditorUtils.GetNum(args, "radius", 5f);
            var offsetY = EditorUtils.GetNum(args, "offsetY", 0f);
            var jitterX = EditorUtils.GetNum(args, "jitterX", 0f);
            var jitterZ = EditorUtils.GetNum(args, "jitterZ", 0f);
            var rotationYawRandom = EditorUtils.GetNum(args, "rotationYawRandom", 0f);
            var scaleMin = EditorUtils.GetNum(args, "scaleMin", 1f);
            var scaleMax = EditorUtils.GetNum(args, "scaleMax", 1f);
            var snapGround = EditorUtils.GetBool(args, "snapGround", false);
            var namePrefix = EditorUtils.GetString(args, "namePrefix");
            var parentRef = EditorUtils.GetString(args, "parent");
            var seed = EditorUtils.GetInt(args, "seed", Environment.TickCount);

            var center = args.TryGetProperty("center", out var centerEl) ? EditorUtils.GetVector3(centerEl) : Vector3.Zero;
            var lineFrom = args.TryGetProperty("from", out var fromEl) ? EditorUtils.GetVector3(fromEl) : Vector3.Zero;
            var lineTo = args.TryGetProperty("to", out var toEl) ? EditorUtils.GetVector3(toEl) : Vector3.Zero;

            var isPrefabPath = source.IndexOf('/') >= 0 || source.IndexOf('\\') >= 0 ||
                               source.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;

                Actor parent;
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

                Prefab prefab = null;
                Type actorType = null;
                if (isPrefabPath)
                {
                    prefab = Content.Load<Prefab>(source, 10000.0);
                    if (prefab == null || !prefab.IsLoaded)
                        throw new ArgumentException("预制体加载失败: " + source);
                }
                else
                {
                    actorType = ResolveActorType(source);
                }

                var random = new Random(seed);
                var created = new List<object>();

                for (var i = 0; i < count; i++)
                {
                    var pos = ComputePatternPosition(pattern, i, count, center, spacingX, spacingZ, radius, lineFrom, lineTo);

                    if (jitterX > 0f || jitterZ > 0f)
                    {
                        pos += new Vector3(
                            (float)(random.NextDouble() * 2.0 - 1.0) * jitterX,
                            0f,
                            (float)(random.NextDouble() * 2.0 - 1.0) * jitterZ);
                    }

                    if (snapGround && TrySnapToGround(pos, 200f, 500f, out var hitPoint))
                        pos.Y = hitPoint.Y + offsetY;
                    else
                        pos.Y += offsetY;

                    var yaw = rotationYawRandom > 0f
                        ? (float)(random.NextDouble() * 2.0 - 1.0) * rotationYawRandom
                        : 0f;
                    var uniformScale = (scaleMin != 1f || scaleMax != 1f)
                        ? (float)(scaleMin + random.NextDouble() * (Math.Max(scaleMin, scaleMax) - Math.Min(scaleMin, scaleMax)))
                        : 1f;

                    Actor instance;
                    if (prefab != null)
                    {
                        var transform = Transform.Identity;
                        transform.Translation = pos;
                        if (yaw != 0f)
                            transform.Orientation = Quaternion.Euler(0f, yaw, 0f);
                        if (uniformScale != 1f)
                            transform.Scale = new Vector3(uniformScale, uniformScale, uniformScale);

                        instance = PrefabManager.SpawnPrefab(prefab, transform);
                        if (instance == null)
                            throw new InvalidOperationException("预制体实例化失败: " + source);
                        editor.SceneEditing.Spawn(instance, parent, -1, false);
                    }
                    else
                    {
                        instance = CreateActorByType(actorType);
                        if (yaw != 0f)
                            instance.Orientation = Quaternion.Euler(0f, yaw, 0f);
                        if (uniformScale != 1f)
                            instance.Scale = new Vector3(uniformScale, uniformScale, uniformScale);
                        instance.Position = pos;
                        editor.SceneEditing.Spawn(instance, parent, -1, false);
                    }

                    if (!string.IsNullOrEmpty(namePrefix))
                        instance.Name = namePrefix + "_" + (i + 1);

                    // 每个实例计入一次撤销计数（与 SceneEditing.Spawn 的 Undo 记录一一对应）
                    ToolRegistry.OnUndoableAction();
                    created.Add(new { name = instance.Name, id = instance.ID.ToString(), position = FmtV3(pos) });
                }

                return new
                {
                    status = "scattered",
                    pattern,
                    source,
                    seed,
                    count = created.Count,
                    created
                };
            });
        }

        private static Task<object> LevelAlignAsync(JsonElement args)
        {
            var targets = ParseStringArray(args, "targets");
            var axis = (EditorUtils.GetString(args, "axis", "x") ?? "x").ToLowerInvariant();
            var mode = (EditorUtils.GetString(args, "mode", "center") ?? "center").ToLowerInvariant();
            var distribute = EditorUtils.GetBool(args, "distribute", false);
            var spacing = EditorUtils.GetNum(args, "spacing", 0f);

            if (axis != "x" && axis != "y" && axis != "z")
                throw new ArgumentException("axis 必须是 x/y/z");
            if (mode != "min" && mode != "max" && mode != "center" && mode != "average")
                throw new ArgumentException("mode 必须是 min/max/center/average");

            return MainThread.InvokeAsync<object>(() =>
            {
                var actors = ResolveTargets(targets);
                if (actors.Count == 0)
                    throw new ArgumentException("没有可对齐的目标（targets 为空且当前无选中）");

                var restorers = new List<Action>();
                foreach (var actor in actors)
                {
                    var oldPos = actor.Position;
                    var captured = actor;
                    restorers.Add(() => { try { captured.Position = oldPos; } catch { } });
                }

                var values = actors.Select(a => GetAxisValue(a.Position, axis)).ToList();
                var minValue = values.Min();
                var maxValue = values.Max();
                var anchor = mode == "min" ? minValue
                    : mode == "max" ? maxValue
                    : mode == "average" ? (float)values.Average()
                    : (minValue + maxValue) * 0.5f;

                var moves = new List<object>();

                if (distribute && actors.Count > 1)
                {
                    var ordered = actors.OrderBy(a => GetAxisValue(a.Position, axis)).ToList();
                    for (var i = 0; i < ordered.Count; i++)
                    {
                        var value = spacing > 0f
                            ? minValue + i * spacing
                            : minValue + (maxValue - minValue) * i / (ordered.Count - 1);

                        var actor = ordered[i];
                        var from = actor.Position;
                        actor.Position = SetAxisValue(from, axis, value);
                        moves.Add(new { actor = actor.Name, from = FmtV3(from), to = FmtV3(actor.Position) });
                    }
                }
                else
                {
                    foreach (var actor in actors)
                    {
                        var from = actor.Position;
                        actor.Position = SetAxisValue(from, axis, anchor);
                        moves.Add(new { actor = actor.Name, from = FmtV3(from), to = FmtV3(actor.Position) });
                    }
                }

                AgentUndo.Record("AI: 批量对齐 (" + actors.Count + " 个)", () =>
                {
                    foreach (var restore in restorers)
                        restore();
                }, null);

                return new
                {
                    status = "aligned",
                    axis,
                    mode = distribute ? "distribute" : mode,
                    count = actors.Count,
                    moves
                };
            });
        }

        private static Task<object> LevelSnapGroundAsync(JsonElement args)
        {
            var targets = ParseStringArray(args, "targets");
            var rayHeight = EditorUtils.GetNum(args, "rayHeight", 200f);
            var maxDistance = EditorUtils.GetNum(args, "maxDistance", 500f);
            var offsetY = EditorUtils.GetNum(args, "offsetY", 0f);
            var alignToNormal = EditorUtils.GetBool(args, "alignToNormal", false);
            var offsetAlongNormal = EditorUtils.GetNum(args, "offsetAlongNormal", 0f);

            return MainThread.InvokeAsync<object>(() =>
            {
                var actors = ResolveTargets(targets);
                if (actors.Count == 0)
                    throw new ArgumentException("没有可吸附的目标（targets 为空且当前无选中）");

                var restorers = new List<Action>();
                var results = new List<object>();

                foreach (var actor in actors)
                {
                    var oldPos = actor.Position;
                    var oldRot = actor.Orientation;
                    var captured = actor;
                    restorers.Add(() =>
                    {
                        try
                        {
                            captured.Position = oldPos;
                            captured.Orientation = oldRot;
                        }
                        catch
                        {
                        }
                    });

                    var origin = actor.Position + Vector3.Up * rayHeight;
                    if (!Physics.RayCast(origin, -Vector3.Up, out RayCastHit hit, maxDistance, uint.MaxValue, false))
                    {
                        results.Add(new
                        {
                            actor = actor.Name,
                            hit = false,
                            note = "射线未命中碰撞体（编辑态物理可能未模拟，可在 sim_play 后重试）"
                        });
                        continue;
                    }

                    var newPos = hit.Point + Vector3.Up * offsetY + hit.Normal * offsetAlongNormal;
                    actor.Position = newPos;

                    if (alignToNormal)
                    {
                        try
                        {
                            var forward = actor.Transform.Forward;
                            var projected = forward - hit.Normal * Vector3.Dot(forward, hit.Normal);
                            if (projected.Length > 0.001f)
                                actor.Orientation = Quaternion.LookRotation(Vector3.Normalize(projected), hit.Normal);
                        }
                        catch
                        {
                            // 法线对齐失败时保留原朝向
                        }
                    }

                    results.Add(new
                    {
                        actor = actor.Name,
                        hit = true,
                        position = FmtV3(actor.Position),
                        normal = FmtV3(hit.Normal)
                    });
                }

                AgentUndo.Record("AI: 批量贴地 (" + actors.Count + " 个)", () =>
                {
                    foreach (var restore in restorers)
                        restore();
                }, null);

                return new { status = "snapped", count = actors.Count, results };
            });
        }

        // ==================== 内部辅助 ====================

        private static Vector3 ComputePatternPosition(string pattern, int index, int count, Vector3 center,
            float spacingX, float spacingZ, float radius, Vector3 lineFrom, Vector3 lineTo)
        {
            switch (pattern)
            {
                case "grid":
                {
                    var cols = (int)Math.Ceiling(Math.Sqrt(count));
                    var rows = (int)Math.Ceiling((double)count / cols);
                    var col = index % cols;
                    var row = index / cols;
                    var x = (col - (cols - 1) * 0.5f) * spacingX;
                    var z = (row - (rows - 1) * 0.5f) * spacingZ;
                    return center + new Vector3(x, 0f, z);
                }
                case "circle":
                {
                    var angle = (float)(2.0 * Math.PI * index / count);
                    return center + new Vector3((float)Math.Cos(angle) * radius, 0f, (float)Math.Sin(angle) * radius);
                }
                default: // line
                {
                    if (count <= 1)
                        return center;
                    var t = index / (float)(count - 1);
                    return Vector3.Lerp(lineFrom, lineTo, t);
                }
            }
        }

        private static bool TrySnapToGround(Vector3 position, float rayHeight, float maxDistance, out Vector3 hitPoint)
        {
            hitPoint = position;
            var origin = position + Vector3.Up * rayHeight;
            if (Physics.RayCast(origin, -Vector3.Up, out RayCastHit hit, maxDistance, uint.MaxValue, false))
            {
                hitPoint = hit.Point;
                return true;
            }
            return false;
        }

        private static Type ResolveActorType(string typeName)
        {
            var type = EditorUtils.FindType(typeName);
            if (type == null || !typeof(Actor).IsAssignableFrom(type) || type.IsAbstract)
                throw new ArgumentException("未知 Actor 类型: " + typeName + "（source 也可传 prefab 资产路径）");
            return type;
        }

        private static Actor CreateActorByType(Type type)
        {
            // Actor.New<T>() 实际定义在基类 FlaxEngine.Object 上（Scripting/Object.cs），
            // 反射查找必须带 FlattenHierarchy 才能命中
            var newMethod = typeof(Actor).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .FirstOrDefault(m => m.Name == "New" && m.IsGenericMethodDefinition &&
                                     m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0);
            if (newMethod == null)
                throw new InvalidOperationException("Actor.New 反射定位失败");
            return (Actor)newMethod.MakeGenericMethod(type).Invoke(null, null);
        }

        private static List<Actor> ResolveTargets(List<string> refs)
        {
            var actors = new List<Actor>();
            if (refs != null && refs.Count > 0)
            {
                foreach (var reference in refs)
                {
                    var actor = EditorUtils.FindActor(reference);
                    if (actor == null)
                        throw new ArgumentException("Actor 不存在: " + reference);
                    actors.Add(actor);
                }
            }
            else
            {
                var editor = FlaxEditor.Editor.Instance;
                foreach (var node in editor.SceneEditing.Selection)
                {
                    var actor = (node as ActorNode)?.Actor;
                    if (actor != null)
                        actors.Add(actor);
                }
            }
            return actors;
        }

        private static List<string> ParseStringArray(JsonElement args, string name)
        {
            var list = new List<string>();
            if (args.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        list.Add(item.GetString());
                }
            }
            return list;
        }

        private static float GetAxisValue(Vector3 v, string axis)
        {
            switch (axis)
            {
                case "x": return v.X;
                case "y": return v.Y;
                default: return v.Z;
            }
        }

        private static Vector3 SetAxisValue(Vector3 v, string axis, float value)
        {
            switch (axis)
            {
                case "x":
                    v.X = value;
                    break;
                case "y":
                    v.Y = value;
                    break;
                default:
                    v.Z = value;
                    break;
            }
            return v;
        }

        private static object FmtV3(Vector3 v)
        {
            return new { x = v.X, y = v.Y, z = v.Z };
        }
    }
}
