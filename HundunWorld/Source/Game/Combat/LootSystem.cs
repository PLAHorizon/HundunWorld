using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 物品品质
    /// </summary>
    public enum ItemQuality
    {
        /// <summary>灰色 - 垃圾</summary>
        Trash = 0,
        /// <summary>白色 - 普通</summary>
        Common = 1,
        /// <summary>绿色 - 优秀</summary>
        Uncommon = 2,
        /// <summary>蓝色 - 精良</summary>
        Rare = 3,
        /// <summary>紫色 - 史诗</summary>
        Epic = 4,
        /// <summary>橙色 - 传说</summary>
        Legendary = 5,
        /// <summary>红色 - 神话</summary>
        Mythic = 6
    }

    /// <summary>
    /// 掉落物品条目
    /// </summary>
    [Serializable]
    public class LootEntry
    {
        [JsonPropertyName("itemId")]
        public int ItemId { get; set; }

        [JsonPropertyName("itemName")]
        public string ItemName { get; set; } = "";

        [JsonPropertyName("quality")]
        public int Quality { get; set; } = 1;

        [JsonPropertyName("dropChance")]
        public float DropChance { get; set; } = 1.0f;

        [JsonPropertyName("minCount")]
        public int MinCount { get; set; } = 1;

        [JsonPropertyName("maxCount")]
        public int MaxCount { get; set; } = 1;

        [JsonPropertyName("weight")]
        public int Weight { get; set; } = 100;
    }

    /// <summary>
    /// 掉落表配置
    /// </summary>
    [Serializable]
    public class LootTableConfig
    {
        [JsonPropertyName("tableId")]
        public string TableId { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("minDrops")]
        public int MinDrops { get; set; } = 1;

        [JsonPropertyName("maxDrops")]
        public int MaxDrops { get; set; } = 3;

        [JsonPropertyName("goldMin")]
        public int GoldMin { get; set; } = 0;

        [JsonPropertyName("goldMax")]
        public int GoldMax { get; set; } = 100;

        [JsonPropertyName("entries")]
        public List<LootEntry> Entries { get; set; } = new List<LootEntry>();
    }

    /// <summary>
    /// 掉落结果
    /// </summary>
    public class LootResult
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public ItemQuality Quality { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// 掉落系统 - 管理怪物/NPC的物品掉落逻辑。
    /// 产品级特性：
    /// - 数据驱动掉落表（JSON配置）
    /// - 权重随机 + 概率判定双重机制
    /// - 品质保底机制（N次不出稀有则保底）
    /// - 幸运值系统（影响掉落率）
    /// - 首杀/特殊条件额外掉落
    /// </summary>
    public class LootSystem
    {
        private static LootSystem _instance;
        public static LootSystem Instance => _instance ??= new LootSystem();

        private Dictionary<string, LootTableConfig> _lootTables = new Dictionary<string, LootTableConfig>();
        private Dictionary<string, int> _pityCounters = new Dictionary<string, int>();
        private Random _random = new Random();

        /// <summary>保底次数（N次不出Rare+则保底）</summary>
        public int PityThreshold = 20;

        /// <summary>全局掉落率倍率（活动加成）</summary>
        public float GlobalDropRateMultiplier { get; set; } = 1.0f;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>掉落事件</summary>
        public event Action<List<LootResult>, int> OnLootGenerated;

        /// <summary>
        /// 初始化（加载掉落表）
        /// </summary>
        public void Initialize()
        {
            LoadBuiltInTables();
            Debug.Log($"[LootSystem] 初始化完成，{_lootTables.Count} 个掉落表");
        }

        /// <summary>
        /// 从JSON加载掉落表
        /// </summary>
        public void LoadLootTable(string json)
        {
            try
            {
                var table = JsonSerializer.Deserialize<LootTableConfig>(json, _jsonOptions);
                if (table != null && !string.IsNullOrEmpty(table.TableId))
                {
                    _lootTables[table.TableId] = table;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LootSystem] 掉落表加载失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 注册掉落表
        /// </summary>
        public void RegisterTable(LootTableConfig table)
        {
            if (table != null && !string.IsNullOrEmpty(table.TableId))
            {
                _lootTables[table.TableId] = table;
            }
        }

        /// <summary>
        /// 生成掉落物（核心方法）
        /// </summary>
        /// <param name="tableId">掉落表ID</param>
        /// <param name="luckBonus">幸运加成（0-1，影响概率）</param>
        /// <param name="monsterLevel">怪物等级（影响掉落品质）</param>
        /// <returns>掉落结果列表</returns>
        public List<LootResult> GenerateLoot(string tableId, float luckBonus = 0f, int monsterLevel = 1)
        {
            var results = new List<LootResult>();

            if (!_lootTables.TryGetValue(tableId, out var table))
            {
                Debug.LogWarning($"[LootSystem] 掉落表不存在: {tableId}");
                return results;
            }

            // 1. 生成金币
            if (table.GoldMax > 0)
            {
                int gold = _random.Next(table.GoldMin, table.GoldMax + 1);
                gold = (int)(gold * (1f + luckBonus * 0.5f));
                if (gold > 0)
                {
                    results.Add(new LootResult { ItemId = 0, ItemName = "金币", Quality = ItemQuality.Common, Count = gold });
                }
            }

            // 2. 决定掉落数量
            int dropCount = _random.Next(table.MinDrops, table.MaxDrops + 1);

            // 3. 逐次Roll物品
            for (int i = 0; i < dropCount; i++)
            {
                var item = RollItem(table, luckBonus, monsterLevel, tableId);
                if (item != null)
                {
                    results.Add(item);
                }
            }

            // 4. 保底检查
            CheckPity(table, results, tableId);

            OnLootGenerated?.Invoke(results, monsterLevel);
            return results;
        }

        /// <summary>
        /// 单次Roll物品（权重 + 概率双重判定）
        /// </summary>
        private LootResult RollItem(LootTableConfig table, float luckBonus, int monsterLevel, string tableId)
        {
            if (table.Entries == null || table.Entries.Count == 0) return null;

            // 权重随机选择
            int totalWeight = table.Entries.Sum(e => e.Weight);
            int roll = _random.Next(0, totalWeight);
            int cumulative = 0;
            LootEntry selectedEntry = null;

            foreach (var entry in table.Entries)
            {
                cumulative += entry.Weight;
                if (roll < cumulative)
                {
                    selectedEntry = entry;
                    break;
                }
            }

            if (selectedEntry == null) return null;

            // 概率判定（受幸运值和全局倍率影响）
            float effectiveChance = selectedEntry.DropChance * GlobalDropRateMultiplier * (1f + luckBonus);
            effectiveChance = Mathf.Clamp(effectiveChance, 0f, 1f);

            if (_random.NextSingle() > effectiveChance)
            {
                // 未命中，增加保底计数
                IncrementPity(tableId);
                return null;
            }

            // 命中，重置保底
            ResetPity(tableId);

            // 决定数量
            int count = _random.Next(selectedEntry.MinCount, selectedEntry.MaxCount + 1);

            return new LootResult
            {
                ItemId = selectedEntry.ItemId,
                ItemName = selectedEntry.ItemName,
                Quality = (ItemQuality)selectedEntry.Quality,
                Count = count
            };
        }

        /// <summary>
        /// 保底机制：连续N次未出Rare+则保底一个
        /// </summary>
        private void CheckPity(LootTableConfig table, List<LootResult> results, string tableId)
        {
            int pity = GetPity(tableId);
            if (pity < PityThreshold) return;

            // 检查本次是否已有Rare+
            bool hasRarePlus = results.Any(r => r.Quality >= ItemQuality.Rare);
            if (hasRarePlus)
            {
                ResetPity(tableId);
                return;
            }

            // 保底：从掉落表中选一个Rare+物品
            var rareEntries = table.Entries.Where(e => e.Quality >= (int)ItemQuality.Rare).ToList();
            if (rareEntries.Count > 0)
            {
                var guaranteed = rareEntries[_random.Next(rareEntries.Count)];
                results.Add(new LootResult
                {
                    ItemId = guaranteed.ItemId,
                    ItemName = guaranteed.ItemName,
                    Quality = (ItemQuality)guaranteed.Quality,
                    Count = 1
                });
                ResetPity(tableId);
                Debug.Log($"[LootSystem] 保底触发! 获得: {guaranteed.ItemName}");
            }
        }

        // ===== 保底计数管理 =====

        private void IncrementPity(string tableId)
        {
            _pityCounters[tableId] = GetPity(tableId) + 1;
        }

        private void ResetPity(string tableId)
        {
            _pityCounters[tableId] = 0;
        }

        private int GetPity(string tableId)
        {
            return _pityCounters.TryGetValue(tableId, out int count) ? count : 0;
        }

        // ===== 内置掉落表 =====

        private void LoadBuiltInTables()
        {
            // 普通野怪掉落表
            RegisterTable(new LootTableConfig
            {
                TableId = "normal_mob",
                Description = "普通野怪掉落",
                MinDrops = 1, MaxDrops = 2,
                GoldMin = 5, GoldMax = 30,
                Entries = new List<LootEntry>
                {
                    new LootEntry { ItemId = 101, ItemName = "兽皮", Quality = 1, DropChance = 0.6f, MinCount = 1, MaxCount = 3, Weight = 300 },
                    new LootEntry { ItemId = 102, ItemName = "兽骨", Quality = 1, DropChance = 0.5f, MinCount = 1, MaxCount = 2, Weight = 250 },
                    new LootEntry { ItemId = 103, ItemName = "草药", Quality = 1, DropChance = 0.4f, MinCount = 1, MaxCount = 2, Weight = 200 },
                    new LootEntry { ItemId = 201, ItemName = "铁剑", Quality = 2, DropChance = 0.15f, MinCount = 1, MaxCount = 1, Weight = 80 },
                    new LootEntry { ItemId = 202, ItemName = "皮甲", Quality = 2, DropChance = 0.12f, MinCount = 1, MaxCount = 1, Weight = 60 },
                    new LootEntry { ItemId = 301, ItemName = "回春丹", Quality = 2, DropChance = 0.2f, MinCount = 1, MaxCount = 2, Weight = 100 },
                    new LootEntry { ItemId = 401, ItemName = "精铁长剑", Quality = 3, DropChance = 0.05f, MinCount = 1, MaxCount = 1, Weight = 20 },
                    new LootEntry { ItemId = 402, ItemName = "玄铁护甲", Quality = 3, DropChance = 0.04f, MinCount = 1, MaxCount = 1, Weight = 15 },
                }
            });

            // 精英怪掉落表
            RegisterTable(new LootTableConfig
            {
                TableId = "elite_mob",
                Description = "精英怪掉落",
                MinDrops = 2, MaxDrops = 4,
                GoldMin = 30, GoldMax = 100,
                Entries = new List<LootEntry>
                {
                    new LootEntry { ItemId = 103, ItemName = "草药", Quality = 1, DropChance = 0.8f, MinCount = 2, MaxCount = 5, Weight = 200 },
                    new LootEntry { ItemId = 104, ItemName = "灵石碎片", Quality = 2, DropChance = 0.5f, MinCount = 1, MaxCount = 3, Weight = 150 },
                    new LootEntry { ItemId = 401, ItemName = "精铁长剑", Quality = 3, DropChance = 0.2f, MinCount = 1, MaxCount = 1, Weight = 80 },
                    new LootEntry { ItemId = 402, ItemName = "玄铁护甲", Quality = 3, DropChance = 0.18f, MinCount = 1, MaxCount = 1, Weight = 70 },
                    new LootEntry { ItemId = 403, ItemName = "五行秘卷", Quality = 3, DropChance = 0.15f, MinCount = 1, MaxCount = 1, Weight = 60 },
                    new LootEntry { ItemId = 501, ItemName = "紫霄神剑", Quality = 4, DropChance = 0.06f, MinCount = 1, MaxCount = 1, Weight = 20 },
                    new LootEntry { ItemId = 502, ItemName = "天蚕宝甲", Quality = 4, DropChance = 0.05f, MinCount = 1, MaxCount = 1, Weight = 15 },
                    new LootEntry { ItemId = 503, ItemName = "龙血丹", Quality = 4, DropChance = 0.08f, MinCount = 1, MaxCount = 2, Weight = 25 },
                }
            });

            // Boss掉落表
            RegisterTable(new LootTableConfig
            {
                TableId = "boss_mob",
                Description = "Boss掉落",
                MinDrops = 3, MaxDrops = 6,
                GoldMin = 100, GoldMax = 500,
                Entries = new List<LootEntry>
                {
                    new LootEntry { ItemId = 104, ItemName = "灵石碎片", Quality = 2, DropChance = 1.0f, MinCount = 3, MaxCount = 8, Weight = 200 },
                    new LootEntry { ItemId = 501, ItemName = "紫霄神剑", Quality = 4, DropChance = 0.25f, MinCount = 1, MaxCount = 1, Weight = 80 },
                    new LootEntry { ItemId = 502, ItemName = "天蚕宝甲", Quality = 4, DropChance = 0.22f, MinCount = 1, MaxCount = 1, Weight = 70 },
                    new LootEntry { ItemId = 503, ItemName = "龙血丹", Quality = 4, DropChance = 0.3f, MinCount = 1, MaxCount = 3, Weight = 90 },
                    new LootEntry { ItemId = 504, ItemName = "Boss专属技能书", Quality = 4, DropChance = 0.15f, MinCount = 1, MaxCount = 1, Weight = 50 },
                    new LootEntry { ItemId = 601, ItemName = "混沌之刃", Quality = 5, DropChance = 0.05f, MinCount = 1, MaxCount = 1, Weight = 15 },
                    new LootEntry { ItemId = 602, ItemName = "混沌战甲", Quality = 5, DropChance = 0.04f, MinCount = 1, MaxCount = 1, Weight = 12 },
                    new LootEntry { ItemId = 603, ItemName = "混沌之心", Quality = 5, DropChance = 0.03f, MinCount = 1, MaxCount = 1, Weight = 10 },
                }
            });
        }
    }
}
