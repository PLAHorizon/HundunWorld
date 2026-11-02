using System;
using System.Collections.Generic;
using System.IO;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Data
{
    /// <summary>
    /// 物品定义与战利品表的数据库入口。
    /// <para>
    /// 背景：<see cref="ItemFactory"/> 只有"给一个 JSON 文件路径就地加载"的 <c>LoadItem</c>，
    /// 而磁盘上此前没有任何物品定义文件；<see cref="LootTableData"/> 同样只有解析函数、没有加载入口。
    /// 本类补上这两条数据通路：启动时（<see cref="NarrativeDataLoader.InitializeAll"/>）把
    /// Content/NarrativePro/Items 与 Content/NarrativePro/LootTables 目录里的 JSON 全部读入缓存，
    /// 之后按 ItemClassId / 表 ID 供给运行时（文件名即键，Ordinal 大小写敏感，与
    /// <see cref="QuestFactory.LoadAllQuests"/> 的口径一致）。
    /// </para>
    /// </summary>
    public static class NarrativeItemDatabase
    {
        /// <summary>物品定义 JSON 目录（工程相对路径，经 <see cref="NarrativeDataPaths"/> 解析）。</summary>
        public static string DefaultItemsDirectory = "Content/NarrativePro/Items";

        /// <summary>战利品表 JSON 目录。</summary>
        public static string DefaultLootTablesDirectory = "Content/NarrativePro/LootTables";

        private static readonly Dictionary<string, string> _itemJsonById =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, LootTableData> _lootTablesById =
            new Dictionary<string, LootTableData>(StringComparer.Ordinal);

        private static readonly Dictionary<string, NarrativeItem> _templatesById =
            new Dictionary<string, NarrativeItem>(StringComparer.Ordinal);

        private static bool _bInitialized;

        /// <summary>是否已完成一次加载。</summary>
        public static bool IsInitialized => _bInitialized;

        /// <summary>已加载的物品定义 ID 快照。</summary>
        public static IEnumerable<string> LoadedItemClassIds => _itemJsonById.Keys;

        /// <summary>已加载的战利品表 ID 快照。</summary>
        public static IEnumerable<string> LoadedLootTableIds => _lootTablesById.Keys;

        /// <summary>幂等加载（重复调用直接返回）。</summary>
        public static void Initialize(bool force = false)
        {
            if (_bInitialized && !force) return;
            _bInitialized = true;

            if (force)
            {
                _itemJsonById.Clear();
                _lootTablesById.Clear();
                _templatesById.Clear();
            }

            LoadItemsDirectory(ResolveDirOrReport(DefaultItemsDirectory, "物品定义"));
            LoadLootTablesDirectory(ResolveDirOrReport(DefaultLootTablesDirectory, "战利品表"));

            NarrativeLog.Log($"[Data] NarrativeItemDatabase: 物品定义 {_itemJsonById.Count} 条、战利品表 {_lootTablesById.Count} 张。");
        }

        private static string ResolveDirOrReport(string directory, string label)
        {
            string resolved = NarrativeDataPaths.ResolveDirectory(directory);
            if (!Directory.Exists(resolved))
            {
                NarrativeLog.LogError(
                    $"[Data] {label}目录不存在: '{resolved}'。Has it been renamed or moved? (配置值: {directory})");
                return null;
            }
            return resolved;
        }

        private static void LoadItemsDirectory(string resolvedDirectory)
        {
            if (resolvedDirectory == null) return;

            foreach (var file in Directory.GetFiles(resolvedDirectory, "*.json"))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                try
                {
                    string json = File.ReadAllText(file);
                    // 先用 ItemFactory 现成的多态入口校验一遍（$type → 注册表类型 + 片段挂载）。
                    var template = ItemFactory.LoadItemFromJson(json);
                    if (template == null)
                    {
                        NarrativeLog.LogError($"[Data] 物品定义 '{file}' 解析失败（ItemFactory.LoadItemFromJson 返回 null），已跳过。");
                        continue;
                    }

                    _itemJsonById[key] = json;
                    _templatesById[key] = template;
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"[Data] 加载物品定义 '{file}' 失败: {ex.Message}");
                }
            }
        }

        private static void LoadLootTablesDirectory(string resolvedDirectory)
        {
            if (resolvedDirectory == null) return;

            foreach (var file in Directory.GetFiles(resolvedDirectory, "*.json"))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                var table = LootTableData.LoadFromJson(file);
                if (table == null)
                {
                    NarrativeLog.LogError($"[Data] 战利品表 '{file}' 加载失败。Has it been renamed?");
                    continue;
                }

                if (string.IsNullOrEmpty(table.TableId)) table.TableId = key;
                _lootTablesById[key] = table;
            }
        }

        /// <summary>物品定义是否已加载。</summary>
        public static bool HasItem(string itemClassId) =>
            !string.IsNullOrEmpty(itemClassId) && _itemJsonById.ContainsKey(itemClassId);

        /// <summary>取物品定义模板（只读用途：请勿直接放进背包，运行时请用 <see cref="CreateInstance"/>）。</summary>
        public static NarrativeItem GetTemplate(string itemClassId)
        {
            if (string.IsNullOrEmpty(itemClassId)) return null;
            return _templatesById.TryGetValue(itemClassId, out var template) ? template : null;
        }

        /// <summary>
        /// 按定义创建一个全新物品实例（每次重新解析 JSON，得到独立 GUID 与独立字段状态）。
        /// 没有该 ID 的定义时回退 <see cref="ItemFactory.CreateItem"/>（保持旧调用行为）。
        /// </summary>
        public static NarrativeItem CreateInstance(string itemClassId)
        {
            if (string.IsNullOrEmpty(itemClassId)) return null;

            if (_itemJsonById.TryGetValue(itemClassId, out string json))
            {
                var item = ItemFactory.LoadItemFromJson(json);
                if (item != null)
                {
                    if (string.IsNullOrEmpty(item.ItemClassId)) item.ItemClassId = itemClassId;
                    item.ItemGUID = Guid.NewGuid();
                }
                return item;
            }

            return ItemFactory.CreateItem(itemClassId);
        }

        /// <summary>按表 ID 取战利品表。</summary>
        public static bool TryGetLootTable(string tableId, out LootTableData table)
        {
            table = null;
            return !string.IsNullOrEmpty(tableId) && _lootTablesById.TryGetValue(tableId, out table);
        }

        /// <summary>取路径解析后的默认目录（诊断/编辑器工具用）。</summary>
        public static string ResolvedItemsDirectory => NarrativeDataPaths.ResolveDirectory(DefaultItemsDirectory);

        /// <summary>取路径解析后的默认战利品表目录。</summary>
        public static string ResolvedLootTablesDirectory => NarrativeDataPaths.ResolveDirectory(DefaultLootTablesDirectory);
    }
}
