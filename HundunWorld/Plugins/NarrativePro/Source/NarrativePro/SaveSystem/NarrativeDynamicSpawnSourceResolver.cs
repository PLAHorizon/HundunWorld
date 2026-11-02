// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
//
// UE5 中动态 Actor 记录用 TSoftClassPtr<AActor> 记住类别（NarrativeSaveSubsystem.cpp:705
// ActorRecord.ActorSoftClass = Actor->GetClass()），LoadDynamicRecord 再 LoadSynchronous 重生它。
// Flax 没有“Actor 类”可用于生成（Actor 是密封类，Prefab 才是可生成资产），
// 因此 NarrativeActorRecord.ActorSoftClassPath 必须存 Prefab 资产路径，而不是类型全名。
// 本文件负责解析出这条路径：显式接口 > 影子成员/方法 > 运行期登记 > 生成器反查 > Tag/资产名匹配，
// 并对结果做资产存在性校验，杜绝把类型名当路径写进存档。

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 动态生成来源接口。实现它的 Actor 或脚本可自定义“重生用 Prefab 路径”。
    /// 对应 UE5 的 Actor->GetClass()，Flax 语义为“该 Actor 由哪个 Prefab 生成”。
    /// </summary>
    public interface INarrativeDynamicSpawnSource
    {
        /// <summary>返回用于重生成的 Prefab 资产路径（如 "Content/Items/Pickup_Health.flax"）。</summary>
        string GetSavePrefabPath();
    }

    /// <summary>
    /// 动态 Actor 的 Prefab 路径解析器。
    /// </summary>
    public static class NarrativeDynamicSpawnSourceResolver
    {
        /// <summary>影子实现识别的成员/方法名（按优先级）。</summary>
        private static readonly string[] MemberNameCandidates =
        {
            "SavePrefabPath", "DynamicSpawnPrefabPath", "PrefabPath", "ActorClass", "NPCClassPath", "ClassPath",
        };

        private static readonly string[] MethodNameCandidates =
        {
            "GetSavePrefabPath", "GetPrefabPath", "GetDynamicSpawnPrefabPath",
        };

        // 运行期登记表：Actor 运行时 ID（Flax Actor.ID）与存档稳定 GUID 两个维度
        private static readonly Dictionary<Guid, string> _registeredByActorId = new Dictionary<Guid, string>();
        private static readonly Dictionary<Guid, string> _registeredByGuid = new Dictionary<Guid, string>();

        // 校验缓存：路径 -> 是否为可加载 Prefab
        private static readonly Dictionary<string, bool> _pathValidationCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // 生成器反查索引：被生成 Actor 的运行时 ID -> Prefab 路径
        private static Dictionary<Guid, string> _spawnedByIndex;
        private static DateTime _indexBuiltAt = DateTime.MinValue;

        // Prefab 资产名索引：资产文件名（不含扩展名，小写） -> 路径
        private static Dictionary<string, string> _prefabNameIndex;

        /// <summary>登记（或覆盖）某个 Actor 的动态生成来源路径。外部子系统用一行代码即可接入。</summary>
        public static void RegisterSpawnSource(Actor actor, string prefabPath)
        {
            if (actor == null || string.IsNullOrEmpty(prefabPath)) return;
            if (!IsPlausibleAssetPath(prefabPath))
            {
                NarrativeLog.LogWarning($"[SaveSystem] RegisterSpawnSource 路径不合法（疑似类型名）：{prefabPath}");
                return;
            }
            _registeredByActorId[actor.ID] = prefabPath;
            var binding = NarrativeSavableActorResolver.Resolve(actor);
            if (binding != null)
            {
                Guid guid = binding.GetActorGUID();
                if (guid != Guid.Empty) _registeredByGuid[guid] = prefabPath;
            }
        }

        /// <summary>按稳定 GUID 登记动态生成来源（Actor 尚未存在时使用，例如生成器预登记）。</summary>
        public static void RegisterSpawnSource(Guid actorGuid, string prefabPath)
        {
            if (actorGuid == Guid.Empty || string.IsNullOrEmpty(prefabPath)) return;
            if (!IsPlausibleAssetPath(prefabPath)) return;
            _registeredByGuid[actorGuid] = prefabPath;
        }

        /// <summary>清除某 Actor 的登记（Actor 销毁时调用，避免 ID 复用导致的错绑）。</summary>
        public static void UnregisterSpawnSource(Actor actor)
        {
            if (actor == null) return;
            _registeredByActorId.Remove(actor.ID);
        }

        /// <summary>清空全部登记与缓存（切换场景时调用）。</summary>
        public static void Reset()
        {
            _registeredByActorId.Clear();
            _registeredByGuid.Clear();
            _pathValidationCache.Clear();
            _spawnedByIndex = null;
            _prefabNameIndex = null;
        }

        /// <summary>
        /// 解析 Actor 的重生 Prefab 路径。找不到时返回 null 并记录告警，绝不返回类型名。
        /// </summary>
        public static string Resolve(Actor actor)
        {
            if (actor == null) return null;

            string candidate = ResolveRaw(actor);
            if (string.IsNullOrEmpty(candidate)) return null;

            if (!IsPlausibleAssetPath(candidate))
            {
                NarrativeLog.LogWarning($"[SaveSystem] Actor {actor.Name} 的动态生成来源路径不合法：{candidate}");
                return null;
            }

            string normalized = NormalizePath(candidate);
            if (!ValidatePrefabPath(normalized))
            {
                NarrativeLog.LogWarning($"[SaveSystem] Actor {actor.Name} 的动态生成来源 Prefab 无法解析：{normalized}");
                return null;
            }

            return normalized;
        }

        private static string ResolveRaw(Actor actor)
        {
            // 1) 显式接口：Actor 自身或其脚本
            if (actor is INarrativeDynamicSpawnSource actorSource)
            {
                var path = SafeCall(actorSource.GetSavePrefabPath);
                if (!string.IsNullOrEmpty(path)) return path;
            }

            var scripts = NarrativeSavableActorResolver.GetScripts(actor);
            foreach (var script in scripts)
            {
                if (script is INarrativeDynamicSpawnSource source)
                {
                    var path = SafeCall(source.GetSavePrefabPath);
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }

            // 2) 运行期登记（存档系统自重生成的 Actor、以及外部子系统主动登记的）
            if (_registeredByActorId.TryGetValue(actor.ID, out var registered)) return registered;

            var binding = NarrativeSavableActorResolver.Resolve(actor);
            if (binding != null)
            {
                Guid guid = binding.GetActorGUID();
                if (guid != Guid.Empty && _registeredByGuid.TryGetValue(guid, out var byGuid)) return byGuid;
            }

            // 3) 影子实现：同名字段/属性/方法
            foreach (var script in scripts)
            {
                var path = ReadShadowMember(script);
                if (!string.IsNullOrEmpty(path)) return path;
            }
            var actorShadow = ReadShadowMember(actor);
            if (!string.IsNullOrEmpty(actorShadow)) return actorShadow;

            // 4) 生成器反查：场景中 SpawnComponent 的 SpawnedActor 指向本 Actor 时，取其 Prefab 路径
            EnsureSpawnIndex();
            if (_spawnedByIndex != null && _spawnedByIndex.TryGetValue(actor.ID, out var indexPath)) return indexPath;

            // 5) Actor Tag 承载路径（美术/关卡侧零代码接入方式）
            var tagPath = ReadTagPath(actor);
            if (!string.IsNullOrEmpty(tagPath)) return tagPath;

            // 6) Prefab 资产名与 Actor 名匹配
            var namePath = MatchPrefabByName(actor.Name);
            if (!string.IsNullOrEmpty(namePath)) return namePath;

            return null;
        }

        /// <summary>路径是否像资产路径而不像类型全名。</summary>
        public static bool IsPlausibleAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.IndexOf(',') >= 0) return false;                    // 程序集限定类型名
            if (path.StartsWith("NarrativePro.", StringComparison.Ordinal)) return false;
            if (path.Contains("System.Collections")) return false;

            bool hasSeparator = path.IndexOf('/') >= 0 || path.IndexOf('\\') >= 0;
            bool hasExtension = path.EndsWith(".flax", StringComparison.OrdinalIgnoreCase)
                                || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
            return hasSeparator || hasExtension;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/').Trim();
        }

        /// <summary>校验路径确实能解析为 Prefab 资产（结果缓存）。</summary>
        public static bool ValidatePrefabPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (_pathValidationCache.TryGetValue(path, out var cached)) return cached;

            bool valid = false;

            try
            {
                AssetInfo info;
                if (Content.GetAssetInfo(path, out info))
                {
                    valid = true;
                }
                else if (Content.GetAsset(path) is Prefab)
                {
                    valid = true;
                }
                else
                {
                    var loaded = Content.LoadAsync<Prefab>(path);
                    valid = loaded != null && !loaded.LastLoadFailed;
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] Prefab 路径校验异常 {path}: {ex.Message}");
                valid = false;
            }

            _pathValidationCache[path] = valid;
            return valid;
        }

        private static string SafeCall(Func<string> func)
        {
            try
            {
                return func();
            }
            catch
            {
                return null;
            }
        }

        private static string ReadShadowMember(System.Object target)
        {
            if (target == null) return null;
            var type = target.GetType();

            foreach (var name in MemberNameCandidates)
            {
                try
                {
                    var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        var value = field.GetValue(target) as string;
                        if (!string.IsNullOrEmpty(value)) return value;
                    }

                    var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (prop != null && prop.PropertyType == typeof(string) && prop.CanRead && prop.GetGetMethod(false) != null)
                    {
                        var value = prop.GetValue(target, null) as string;
                        if (!string.IsNullOrEmpty(value)) return value;
                    }
                }
                catch
                {
                    // 忽略单个反射失败
                }
            }

            foreach (var name in MethodNameCandidates)
            {
                try
                {
                    var method = type.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (method != null && method.ReturnType == typeof(string))
                    {
                        var value = method.Invoke(target, null) as string;
                        if (!string.IsNullOrEmpty(value)) return value;
                    }
                }
                catch
                {
                    // 忽略单个反射失败
                }
            }

            return null;
        }

        private static string ReadTagPath(Actor actor)
        {
            try
            {
                if (!string.IsNullOrEmpty(actor.Tag) && IsPlausibleAssetPath(actor.Tag)) return actor.Tag;
                var tags = actor.Tags;
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        // FlaxEngine.Tag 不是字符串：需显式取其名称再判定
                        string tagName = tag.ToString();
                        if (!string.IsNullOrEmpty(tagName) && IsPlausibleAssetPath(tagName)) return tagName;
                    }
                }
            }
            catch
            {
                // Tags 在部分版本可能不可用，忽略
            }
            return null;
        }

        private static void EnsureSpawnIndex()
        {
            // 索引 5 秒内复用，避免每次保存都全场景扫描
            if (_spawnedByIndex != null && (DateTime.UtcNow - _indexBuiltAt).TotalSeconds < 5.0) return;

            _spawnedByIndex = new Dictionary<Guid, string>();
            _indexBuiltAt = DateTime.UtcNow;

            try
            {
                foreach (var actor in Level.GetActors<Actor>())
                {
                    if (actor == null) continue;
                    foreach (var script in NarrativeSavableActorResolver.GetScripts(actor))
                    {
                        var spawned = ReadActorMember(script, "SpawnedActor");
                        if (spawned == null) continue;

                        string path = ReadShadowMember(script);
                        if (string.IsNullOrEmpty(path)) continue;
                        if (!IsPlausibleAssetPath(path)) continue;

                        _spawnedByIndex[spawned.ID] = NormalizePath(path);
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 生成器反查索引构建失败: {ex.Message}");
            }
        }

        private static Actor ReadActorMember(System.Object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            try
            {
                var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (field != null && typeof(Actor).IsAssignableFrom(field.FieldType))
                {
                    return field.GetValue(target) as Actor;
                }

                var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null && typeof(Actor).IsAssignableFrom(prop.PropertyType) && prop.CanRead)
                {
                    return prop.GetValue(target, null) as Actor;
                }
            }
            catch
            {
                // 忽略
            }
            return null;
        }

        private static string MatchPrefabByName(string actorName)
        {
            if (string.IsNullOrEmpty(actorName)) return null;
            EnsurePrefabNameIndex();
            if (_prefabNameIndex == null || _prefabNameIndex.Count == 0) return null;

            // 去掉 Flax 实例化后缀（如 "Pickup_Health" 之后的空格/数字实例名）
            string baseName = actorName;
            int spacePos = baseName.IndexOf(' ');
            if (spacePos > 0) baseName = baseName.Substring(0, spacePos);

            if (_prefabNameIndex.TryGetValue(baseName.ToLowerInvariant(), out var path)) return path;
            return null;
        }

        private static void EnsurePrefabNameIndex()
        {
            if (_prefabNameIndex != null) return;

            _prefabNameIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var ids = Content.GetAllAssetsByType(typeof(Prefab));
                if (ids != null)
                {
                    foreach (var id in ids)
                    {
                        AssetInfo info;
                        if (!Content.GetAssetInfo(id, out info)) continue;
                        if (string.IsNullOrEmpty(info.Path)) continue;

                        string fileName = System.IO.Path.GetFileNameWithoutExtension(info.Path);
                        if (string.IsNullOrEmpty(fileName)) continue;
                        if (!_prefabNameIndex.ContainsKey(fileName.ToLowerInvariant()))
                        {
                            _prefabNameIndex[fileName.ToLowerInvariant()] = NormalizePath(info.Path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] Prefab 资产名索引构建失败: {ex.Message}");
            }
        }
    }
}
