using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 物品定义加载器，从 JSON 加载物品定义并创建多态实例。
    /// 采用类型判别字段 "$type" 区分物品子类，对应 UE5 的 TSubclassOf 机制。
    /// </summary>
    public static class ItemFactory
    {
        private static readonly Dictionary<string, Type> _typeRegistry = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        static ItemFactory()
        {
            RegisterDefaults();
        }

        /// <summary>注册默认物品类型。</summary>
        private static void RegisterDefaults()
        {
            RegisterItemType("NarrativeItem", typeof(NarrativeItem));
            RegisterItemType("EquippableItem", typeof(EquippableItem));
            RegisterItemType("EquippableItem_Clothing", typeof(EquippableItem_Clothing));
            RegisterItemType("WeaponItem", typeof(WeaponItem));
            RegisterItemType("MeleeWeaponItem", typeof(MeleeWeaponItem));
            RegisterItemType("RangedWeaponItem", typeof(RangedWeaponItem));
            RegisterItemType("MagicWeaponItem", typeof(MagicWeaponItem));
            RegisterItemType("ThrowableWeaponItem", typeof(ThrowableWeaponItem));
            RegisterItemType("WeaponAttachmentItem", typeof(WeaponAttachmentItem));
            RegisterItemType("AmmoItem", typeof(AmmoItem));
            RegisterItemType("GameplayEffectItem", typeof(GameplayEffectItem));
        }

        /// <summary>注册物品类型到工厂。</summary>
        public static void RegisterItemType(string typeId, Type type)
        {
            if (!typeof(NarrativeItem).IsAssignableFrom(type))
            {
                NarrativeLog.LogError($"无法注册类型 '{typeId}': {type.Name} 不是 NarrativeItem 的子类");
                return;
            }
            _typeRegistry[typeId] = type;
        }

        /// <summary>根据物品类 ID 创建物品实例。</summary>
        /// <param name="itemClassId">物品类 ID（与定义文件中 $type 对应）</param>
        public static NarrativeItem CreateItem(string itemClassId)
        {
            if (string.IsNullOrEmpty(itemClassId)) return null;
            if (_typeRegistry.TryGetValue(itemClassId, out var type))
            {
                try
                {
                    var item = (NarrativeItem)Activator.CreateInstance(type);
                    item.ItemClassId = itemClassId;
                    return item;
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"创建物品实例失败 '{itemClassId}': {ex.Message}");
                    return null;
                }
            }
            // 未知类型，使用基类
            var fallback = new NarrativeItem { ItemClassId = itemClassId };
            NarrativeLog.LogWarning($"未注册的物品类型 '{itemClassId}'，回退到 NarrativeItem 基类");
            return fallback;
        }

        /// <summary>从 JSON 文件加载物品定义并创建实例。</summary>
        public static NarrativeItem LoadItem(string jsonFilePath)
        {
            try
            {
                if (!File.Exists(jsonFilePath))
                {
                    NarrativeLog.LogError($"物品定义文件不存在: {jsonFilePath}");
                    return null;
                }
                string json = File.ReadAllText(jsonFilePath);
                return LoadItemFromJson(json);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"加载物品定义失败 '{jsonFilePath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>从 JSON 字符串加载物品定义并创建实例。</summary>
        public static NarrativeItem LoadItemFromJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string typeId = root.TryGetProperty("$type", out var typeEl)
                    ? typeEl.GetString()
                    : "NarrativeItem";

                var item = CreateItem(typeId);
                if (item == null) return null;

                PopulateItemFromJson(item, json);
                return item;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"解析物品 JSON 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>将 JSON 数据填充到物品实例。</summary>
        private static void PopulateItemFromJson(NarrativeItem item, string json)
        {
            try
            {
                var type = item.GetType();
                var deserialized = JsonSerializer.Deserialize(json, type, _jsonOptions);
                if (deserialized is NarrativeItem other)
                {
                    CopyProperties(other, item);
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"填充物品数据失败: {ex.Message}");
            }
        }

        /// <summary>浅拷贝属性（用于反序列化结果复制到已创建实例）。</summary>
        private static void CopyProperties(NarrativeItem src, NarrativeItem dst)
        {
            var type = src.GetType();
            foreach (var prop in type.GetProperties())
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.GetIndexParameters().Length > 0) continue;
                try
                {
                    var value = prop.GetValue(src);
                    prop.SetValue(dst, value);
                }
                catch { /* 跳过无法设置的属性 */ }
            }
        }
    }
}
