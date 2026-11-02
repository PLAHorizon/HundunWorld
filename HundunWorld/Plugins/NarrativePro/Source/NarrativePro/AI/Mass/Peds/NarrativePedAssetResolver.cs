using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.AI.Mass.Peds
{
    /// <summary>
    /// 行人 NPC 定义解析器。对应 UE5 中 <c>TSoftObjectPtr&lt;UPedNPCDefinition&gt;::LoadSynchronous()</c>
    /// 与 <c>UAssetManager::GetPrimaryAssetObject</c>（<c>MassPedSpawnerSubsystem.cpp:44</c>、
    /// <c>NarrativeCharacterSubsystem.cpp:346 OnNPCClassLoaded</c>）。
    /// <para>
    /// Flax 没有 PrimaryAsset 体系，因此这里给出「路径 → 定义」的登记表 + JSON 文件加载：
    /// 已登记的定义直接命中；否则按 JSON 文件路径加载并缓存；都失败返回 null 并由调用方跳过。
    /// </para>
    /// </summary>
    public static class NarrativePedAssetResolver
    {
        private static readonly Dictionary<string, PedNPCDefinition> _registry =
            new Dictionary<string, PedNPCDefinition>(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            IncludeFields = true
        };

        /// <summary>登记一个定义（供代码/测试注入，等价于 UE5 已加载的资产）。</summary>
        public static void Register(string path, PedNPCDefinition definition)
        {
            if (string.IsNullOrEmpty(path) || definition == null) return;
            _registry[path] = definition;
        }

        /// <summary>按 UE5 <c>IsNull()</c> 语义判断路径是否为空引用。</summary>
        public static bool IsNull(string path) => string.IsNullOrEmpty(path);

        /// <summary>解析路径为定义实例。对应 UE5 <c>LoadSynchronous()</c>。</summary>
        public static PedNPCDefinition Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (_registry.TryGetValue(path, out var registered) && registered != null)
            {
                return registered;
            }

            try
            {
                string fullPath = ToAbsolute(path);
                if (!File.Exists(fullPath))
                {
                    NarrativeLog.LogWarning($"行人定义资产不存在：{path}");
                    return null;
                }

                string json = File.ReadAllText(fullPath);
                var definition = JsonSerializer.Deserialize<PedNPCDefinition>(json, _jsonOptions);
                if (definition == null)
                {
                    NarrativeLog.LogError($"行人定义反序列化得到空对象：{path}");
                    return null;
                }

                _registry[path] = definition;
                return definition;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"加载行人定义失败 {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>把工程相对路径转成磁盘绝对路径。</summary>
        private static string ToAbsolute(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            // Flax 托管 API 无 MasterProgram；进程工作目录即引擎/工程根（相对资产路径以此为基）。
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        /// <summary>清空缓存（切场景/热重载时用）。</summary>
        public static void Clear()
        {
            _registry.Clear();
        }
    }
}
