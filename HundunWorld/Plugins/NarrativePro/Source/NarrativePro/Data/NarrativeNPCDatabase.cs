using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativePro.AI;
using NarrativePro.AI.Mass.Peds;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Data
{
    /// <summary>
    /// NPC 定义 JSON 的单条默认物品（磁盘形态）。
    /// 与 <see cref="LootTableRoll"/> 的运行时形态相互独立：磁盘只写「物品 ID + 数量」，
    /// 装载时构造成 UE5 侧等价物（必定授予的 <see cref="ItemWithQuantity"/> 列表）。
    /// </summary>
    [Serializable]
    public class NpcDefaultItemJson
    {
        /// <summary>物品定义 ID（对应 Content/NarrativePro/Items/&lt;id&gt;.json 的文件名）。</summary>
        public string ItemClassId = "";

        /// <summary>数量。</summary>
        public int Quantity = 1;

        /// <summary>掷取次数（对应 LootTableRoll.NumRolls）。</summary>
        public int NumRolls = 1;

        /// <summary>成功概率 0~1（对应 LootTableRoll.Chance）。</summary>
        public float Chance = 1f;
    }

    /// <summary>
    /// NPC 定义的磁盘 DTO。
    /// <para>
    /// 为什么不直接把 JSON 反序列化成 <see cref="NPCDefinition"/>：
    /// 该类的成员几乎全是字段（<c>IncludeFields</c> 才绑得上），
    /// 而 <see cref="CharacterDefinition.DefaultFactions"/> / <see cref="CharacterDefinition.DefaultOwnedTags"/>
    /// 是 <see cref="GameplayTagContainer"/>（内部私有集合、无公共写入口），
    /// <see cref="CharacterDefinition.DefaultItemLoadout"/> 是 <see cref="LootTableRoll"/>（混合属性与字段）。
    /// 直接绑定必然出现「部分字段静默为默认值」这一类已在本项目踩过两次的坑，
    /// 因此按 <see cref="NarrativePro.Audio.FoleyAudioBankLoader"/> 的同款做法：
    /// 磁盘用纯字符串/数值 DTO，装载时代码逐字段构造运行时对象。
    /// </para>
    /// </summary>
    [Serializable]
    public class NpcDefinitionJson
    {
        /// <summary>"Npc"（默认，交互型 NPC）或 "Ped"（群集行人，<see cref="PedNPCDefinition"/>）。</summary>
        public string Kind = "Npc";

        // ===== NPCDefinition =====
        public string NPCID = "";
        public string NPCName = "";
        public int MinLevel = 1;
        public int MaxLevel = 1;
        public bool bAllowMultipleInstances = true;
        public string UniqueNPCGUID = "";
        public string NPCClassPath = "";
        public string DialoguePath = "";
        public string TaggedDialogueSetPath = "";
        public bool bIsVendor = false;
        public int TradingCurrency = 0;
        public float BuyItemPercentage = 0.5f;
        public float SellItemPercentage = 1f;
        public string ShopFriendlyName = "";
        public List<string> ActivitySchedulePaths = new List<string>();
        public string ActivityConfigurationPath = "";

        // ===== CharacterDefinition =====
        public int DefaultCurrency = 0;
        public List<NpcDefaultItemJson> DefaultItemLoadout = new List<NpcDefaultItemJson>();
        public List<string> DefaultOwnedTags = new List<string>();
        public List<string> DefaultFactions = new List<string>();
        public List<string> TriggerSetPaths = new List<string>();
        public float AttackPriority = 1f;
        public string AbilityConfigurationPath = "";

        // ===== PedNPCDefinition =====
        public string BakedPedMeshPath = "";
        public string PedMeshPath = "";
        public string PedMeshAnimBPPath = "";
        public string PedTypeTag = "";
    }

    /// <summary>
    /// NPC / 行人定义的 JSON 数据库（补上审计缺口：此前 <see cref="NPCDefinition"/> 没有任何反序列化入口，
    /// 磁盘上也没有 NPC 内容，导致 SetNPCDefinition → 活动调度 → 目标评分整条 NPC 通路不可达）。
    /// <para>
    /// 数据目录：<c>Content/NarrativePro/NPCs/*.json</c>（经 <see cref="NarrativeDataPaths"/> 解析，
    /// 文件名即缺省 ID，与 <see cref="NarrativeItemDatabase"/> 口径一致）。
    /// 注册表按 <see cref="NPCDefinition.NPCID"/>（缺省回落文件名）索引，Ordinal 大小写敏感。
    /// </para>
    /// <para>
    /// 行人定义（<c>"Kind": "Ped"</c>）额外登记进 <see cref="NarrativePedAssetResolver"/>，
    /// 这样 <see cref="NarrativePedProperties.NarrativePedPaths"/> 的路径在
    /// <see cref="MassPedSpawnerSubsystem"/> 生成 Actor 时就能解析出定义并调用 SetNPCDefinition。
    /// </para>
    /// </summary>
    public static class NarrativeNPCDatabase
    {
        /// <summary>NPC 定义 JSON 目录（工程相对路径）。</summary>
        public static string DefaultNpcsDirectory = "Content/NarrativePro/NPCs";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static readonly Dictionary<string, NPCDefinition> _byNpcId =
            new Dictionary<string, NPCDefinition>(StringComparer.Ordinal);

        private static readonly Dictionary<string, NPCDefinition> _bySourceFile =
            new Dictionary<string, NPCDefinition>(StringComparer.Ordinal);

        private static readonly List<string> _pedPaths = new List<string>();
        private static bool _bInitialized;

        /// <summary>是否已完成一次加载。</summary>
        public static bool IsInitialized => _bInitialized;

        /// <summary>已加载的 NPC 定义数量。</summary>
        public static int Count => _byNpcId.Count;

        /// <summary>已加载的 ID 快照。</summary>
        public static IEnumerable<string> LoadedNpcIds => _byNpcId.Keys;

        /// <summary>已加载的行人定义路径（供群体模拟配置 PedProperties 用）。</summary>
        public static IReadOnlyList<string> PedDefinitionPaths => _pedPaths;

        /// <summary>幂等加载。</summary>
        public static void Initialize(bool force = false)
        {
            if (_bInitialized && !force) return;
            _bInitialized = true;

            if (force)
            {
                _byNpcId.Clear();
                _bySourceFile.Clear();
                _pedPaths.Clear();
            }

            string resolved = NarrativeDataPaths.ResolveDirectory(DefaultNpcsDirectory);
            if (!Directory.Exists(resolved))
            {
                NarrativeLog.LogError(
                    $"[Data] NPC 定义目录不存在: '{resolved}'（配置值: {DefaultNpcsDirectory}）。" +
                    "Has it been renamed or moved? 没有 NPC 定义时群体/活动通路将无内容可生成。");
                return;
            }

            int loaded = 0;
            int pedCount = 0;
            foreach (var file in Directory.GetFiles(resolved, "*.json"))
            {
                var definition = LoadFile(file);
                if (definition == null) continue;
                loaded++;
                if (definition is PedNPCDefinition) pedCount++;
            }

            NarrativeLog.Log($"[Data] NarrativeNPCDatabase: NPC 定义 {loaded} 条（其中行人 {pedCount} 条），目录 {resolved}。");
        }

        /// <summary>加载单个 JSON 文件并登记（解析失败返回 null，不抛）。</summary>
        public static NPCDefinition LoadFile(string jsonFilePath)
        {
            string resolved = NarrativeDataPaths.Resolve(jsonFilePath);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
            {
                NarrativeLog.LogError(
                    $"[Data] NPC 定义 JSON 缺失: '{resolved}'（原始路径: {jsonFilePath}）。Has it been renamed or moved?");
                return null;
            }

            NpcDefinitionJson dto;
            try
            {
                dto = JsonSerializer.Deserialize<NpcDefinitionJson>(File.ReadAllText(resolved), Options);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Data] NPC 定义 '{resolved}' 解析失败: {ex.Message}");
                return null;
            }

            if (dto == null)
            {
                NarrativeLog.LogError($"[Data] NPC 定义 '{resolved}' 内容为空，已跳过。");
                return null;
            }

            NPCDefinition definition = Build(dto, resolved);

            string key = definition.NPCID;
            if (string.IsNullOrEmpty(key)) key = Path.GetFileNameWithoutExtension(resolved);
            definition.NPCID = key;

            _byNpcId[key] = definition;
            _bySourceFile[resolved] = definition;

            // 行人定义：登记进资产解析器，路径三种写法都给（绝对 / 工程相对 / ID），
            // 因为 NarrativePedAssetResolver 既可能被传入配置里的相对路径也可能被传入解析后的绝对路径。
            if (definition is PedNPCDefinition ped)
            {
                NarrativePedAssetResolver.Register(resolved, ped);
                NarrativePedAssetResolver.Register(ToProjectRelative(resolved), ped);
                NarrativePedAssetResolver.Register(key, ped);
                if (!_pedPaths.Contains(resolved)) _pedPaths.Add(resolved);
            }

            return definition;
        }

        /// <summary>按 ID 取定义；未命中返回 null。</summary>
        public static NPCDefinition Get(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            return _byNpcId.TryGetValue(npcId, out var definition) ? definition : null;
        }

        /// <summary>按 ID 取定义。</summary>
        public static bool TryGet(string npcId, out NPCDefinition definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(npcId)) return false;
            return _byNpcId.TryGetValue(npcId, out definition) && definition != null;
        }

        /// <summary>该 ID 是否为行人定义（应走群集通路而非单独 Actor）。</summary>
        public static bool IsPed(string npcId) => Get(npcId) is PedNPCDefinition;

        /// <summary>取行人定义（非行人返回 null）。</summary>
        public static PedNPCDefinition GetPed(string npcId) => Get(npcId) as PedNPCDefinition;

        /// <summary>定义来源文件路径（诊断用）。</summary>
        public static bool TryGetSourceFile(string npcId, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(npcId)) return false;
            if (!_byNpcId.TryGetValue(npcId, out var definition)) return false;
            foreach (var kvp in _bySourceFile)
            {
                if (ReferenceEquals(kvp.Value, definition))
                {
                    path = kvp.Key;
                    return true;
                }
            }
            return false;
        }

        /// <summary>清缓存（切场景 / 热重载）。</summary>
        public static void ClearCache()
        {
            _byNpcId.Clear();
            _bySourceFile.Clear();
            _pedPaths.Clear();
            _bInitialized = false;
        }

        // =====================================================================
        // DTO → 运行时定义
        // =====================================================================

        private static NPCDefinition Build(NpcDefinitionJson dto, string sourceFile)
        {
            bool bPed = string.Equals(dto.Kind, "Ped", StringComparison.OrdinalIgnoreCase);
            NPCDefinition def = bPed ? new PedNPCDefinition() : new NPCDefinition();

            def.NPCID = !string.IsNullOrEmpty(dto.NPCID)
                ? dto.NPCID
                : Path.GetFileNameWithoutExtension(sourceFile);
            def.NPCName = string.IsNullOrEmpty(dto.NPCName) ? def.NPCID : dto.NPCName;
            def.MinLevel = Math.Max(1, dto.MinLevel);
            def.MaxLevel = Math.Max(def.MinLevel, dto.MaxLevel);
            def.bAllowMultipleInstances = dto.bAllowMultipleInstances;
            def.UniqueNPCGUID = dto.UniqueNPCGUID ?? "";
            def.NPCClassPath = dto.NPCClassPath ?? "";
            def.DialoguePath = dto.DialoguePath ?? "";
            def.TaggedDialogueSetPath = dto.TaggedDialogueSetPath ?? "";
            def.bIsVendor = dto.bIsVendor;
            def.TradingCurrency = dto.TradingCurrency;
            def.BuyItemPercentage = dto.BuyItemPercentage;
            def.SellItemPercentage = dto.SellItemPercentage;
            def.ShopFriendlyName = dto.ShopFriendlyName ?? "";
            def.ActivityConfigurationPath = string.IsNullOrEmpty(dto.ActivityConfigurationPath)
                ? NarrativePro.UnrealFramework.NarrativeNPCCharacter.DefaultActivityConfigurationPath
                : dto.ActivityConfigurationPath;
            def.ActivitySchedulePaths = dto.ActivitySchedulePaths != null
                ? new List<string>(dto.ActivitySchedulePaths)
                : new List<string>();

            // CharacterDefinition
            def.DefaultCurrency = dto.DefaultCurrency;
            def.DefaultFactions = ToContainer(dto.DefaultFactions);
            def.DefaultOwnedTags = ToContainer(dto.DefaultOwnedTags);
            def.TriggerSetPaths = dto.TriggerSetPaths != null
                ? new List<string>(dto.TriggerSetPaths)
                : new List<string>();
            def.AttackPriority = dto.AttackPriority;
            def.AbilityConfigurationPath = dto.AbilityConfigurationPath ?? "";
            def.DefaultItemLoadout = ToLoadout(dto.DefaultItemLoadout, def.NPCID);
            def.DefaultAppearance = new CharacterAppearance();

            if (bPed)
            {
                var ped = (PedNPCDefinition)def;
                ped.BakedPedMeshPath = dto.BakedPedMeshPath ?? "";
                ped.PedMeshPath = dto.PedMeshPath ?? "";
                ped.PedMeshAnimBPPath = dto.PedMeshAnimBPPath ?? "";
                ped.PedTypeTag = dto.PedTypeTag ?? "";
            }

            return def;
        }

        private static GameplayTagContainer ToContainer(List<string> names)
        {
            // GameplayTagContainer 内部是私有集合：只能走接受名字序列的构造函数。
            var container = new GameplayTagContainer();
            if (names == null || names.Count == 0) return container;

            var valid = new List<string>(names.Count);
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                valid.Add(name.Trim());
            }
            return new GameplayTagContainer(valid);
        }

        private static List<LootTableRoll> ToLoadout(List<NpcDefaultItemJson> items, string npcId)
        {
            var rolls = new List<LootTableRoll>();
            if (items == null) return rolls;

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.ItemClassId)) continue;

                if (!NarrativeItemDatabase.HasItem(item.ItemClassId))
                {
                    NarrativeLog.LogWarning(
                        $"[Data] NPC '{npcId}' 默认物品 '{item.ItemClassId}' 在物品库中不存在，" +
                        "仍会写入装载表（发放时按背包的空定义分支处理）。");
                }

                rolls.Add(new LootTableRoll
                {
                    ItemsToGrant = new List<ItemWithQuantity>
                    {
                        new ItemWithQuantity
                        {
                            ItemClassId = item.ItemClassId,
                            Quantity = Math.Max(1, item.Quantity),
                        }
                    },
                    NumRolls = Math.Max(1, item.NumRolls),
                    Chance = Math.Max(0f, Math.Min(1f, item.Chance)),
                });
            }
            return rolls;
        }

        private static string ToProjectRelative(string absolutePath)
        {
            try
            {
                string root = NarrativeDataPaths.ProjectDirectory;
                if (string.IsNullOrEmpty(root)) return absolutePath;
                if (!absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return absolutePath;

                string relative = absolutePath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return relative.Replace(Path.DirectorySeparatorChar, '/');
            }
            catch
            {
                return absolutePath;
            }
        }
    }
}
