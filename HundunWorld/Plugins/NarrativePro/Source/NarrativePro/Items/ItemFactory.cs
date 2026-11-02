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
            AllowTrailingCommas = true,
            // 物品可配项本身全是属性，但它内嵌的 FPickupMeshData.PickupOffset 是 FlaxEngine.Transform，
            // 而 Transform 只有公共字段没有可写属性：不开这一项，作者写的 pickupOffset
            // 会静默变成全零 default（缩放 0 等于把拾取物缩到看不见）。
            IncludeFields = true
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

            // 物品片段类型：对应 UE5 在物品定义上挂载的 UAmmoFragment / UPoisonableFragment
            RegisterFragmentType("AmmoFragment", typeof(AmmoFragment));
            RegisterFragmentType("PoisonableFragment", typeof(PoisonableFragment));
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

        private static readonly System.Collections.Generic.Dictionary<string, Type> _fragmentRegistry =
            new System.Collections.Generic.Dictionary<string, Type>();

        /// <summary>注册物品片段类型（对应 UE5 物品上挂载的 Item Fragment 子类）。</summary>
        public static void RegisterFragmentType(string typeId, Type type)
        {
            if (string.IsNullOrEmpty(typeId) || type == null) return;
            if (!typeof(NarrativeItemFragment).IsAssignableFrom(type))
            {
                NarrativeLog.LogError($"无法注册片段类型 '{typeId}': {type.Name} 不是 NarrativeItemFragment 的子类");
                return;
            }
            _fragmentRegistry[typeId] = type;
        }

        /// <summary>按片段类型 ID 创建片段实例；未注册返回 null。</summary>
        public static NarrativeItemFragment CreateFragment(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return null;
            if (!_fragmentRegistry.TryGetValue(typeId, out var type))
            {
                NarrativeLog.LogWarning($"未注册的片段类型 '{typeId}'，已忽略");
                return null;
            }
            try
            {
                return (NarrativeItemFragment)Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"创建片段实例失败 '{typeId}': {ex.Message}");
                return null;
            }
        }

        /// <summary>按片段类型 ID 创建片段并挂到物品上（供物品数据/装备逻辑使用）。</summary>
        public static NarrativeItemFragment AttachFragment(NarrativeItem item, string fragmentTypeId)
        {
            if (item == null) return null;
            var fragment = CreateFragment(fragmentTypeId);
            if (fragment != null) item.Fragments.Add(fragment);
            return fragment;
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
                PopulateFragmentsFromJson(item, root);
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

        /// <summary>
        /// 解析物品 JSON 中的 "Fragments" 数组并把片段挂到物品上。
        /// 对应 UE5 在 UNarrativeItem 定义上通过 Item Fragment 数组挂载 UAmmoFragment / UPoisonableFragment。
        /// 支持两种写法：<c>["AmmoFragment"]</c> 与 <c>[{"$fragment":"AmmoFragment","AmmoDamageOverride":25}]</c>。
        /// </summary>
        private static void PopulateFragmentsFromJson(NarrativeItem item, JsonElement root)
        {
            if (item == null) return;
            if (!root.TryGetProperty("Fragments", out var frags) || frags.ValueKind != JsonValueKind.Array) return;

            foreach (var el in frags.EnumerateArray())
            {
                string typeId = null;
                if (el.ValueKind == JsonValueKind.String)
                {
                    typeId = el.GetString();
                }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    if (el.TryGetProperty("$fragment", out var f)) typeId = f.GetString();
                    else if (el.TryGetProperty("Type", out var t)) typeId = t.GetString();
                }
                if (string.IsNullOrEmpty(typeId)) continue;

                var fragment = CreateFragment(typeId);
                if (fragment == null) continue;

                if (el.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        var loaded = JsonSerializer.Deserialize(el.GetRawText(), fragment.GetType(), _jsonOptions);
                        if (loaded != null) CopyFragmentProperties(loaded, fragment);
                    }
                    catch (Exception ex)
                    {
                        NarrativeLog.LogWarning($"片段 '{typeId}' 数据解析失败: {ex.Message}");
                    }
                }
                item.Fragments.Add(fragment);
            }
        }

        /// <summary>浅拷贝片段属性（跳过只读/索引属性）。</summary>
        private static void CopyFragmentProperties(object src, object dst)
        {
            var type = dst.GetType();
            foreach (var prop in type.GetProperties())
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.GetIndexParameters().Length > 0) continue;
                try
                {
                    prop.SetValue(dst, prop.GetValue(src));
                }
                catch
                {
                    // 类型不兼容的属性忽略
                }
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
