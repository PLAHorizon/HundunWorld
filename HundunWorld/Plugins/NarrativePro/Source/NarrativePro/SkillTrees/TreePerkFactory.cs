using System;
using System.Collections.Generic;
using System.Text.Json;
using NarrativePro.Core;

namespace NarrativePro.SkillTrees
{
    /// <summary>
    /// Perk 类型注册表/工厂。对应 UE5 中 NewObject&lt;UTreePerk&gt;(this, PerkClass) 的类反射实例化。
    /// Flax 无 UE 反射，按本仓库既有工厂惯例（参见 Items/ItemFactory、GAS/NarrativeExecCalc）
    /// 以“类路径字符串 → 已注册 Type”的登记表 + Activator 实例化。
    /// perkClassPath 与 <see cref="TreePerk.PerkClassPath"/>、<see cref="FSavedPerk.PerkClass"/> 一致。
    /// </summary>
    public static class TreePerkFactory
    {
        private static readonly Dictionary<string, Type> _perkTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>
        /// Perk 作者数据表：类路径 → 内容 JSON 里该 Perk 的原始节点。
        /// 对应 UE5 的 <c>Perk_*.uasset</c>（BlueprintGeneratedClass 的 CDO 默认值）。
        /// </summary>
        private static readonly Dictionary<string, JsonElement> _perkDefinitions =
            new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions _definitionOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            // TreePerk / Perk_AttributeMod 的作者字段都是公共字段（与 Flax 序列化同口径），
            // 不开 IncludeFields 会得到一个字段全空的对象，且不会报任何错。
            IncludeFields = true
        };

        static TreePerkFactory()
        {
            RegisterDefaults();
        }

        /// <summary>注册内置 Perk 类型。与 UE5 Content/Pro/Core/Data/SkillTrees 中的类型对应。</summary>
        private static void RegisterDefaults()
        {
            RegisterPerkType("TreePerk", typeof(TreePerk));
            RegisterPerkType("Perk_AttributeMod", typeof(Perk_AttributeMod));
            RegisterPerkType("/NarrativePro/Pro/Core/Data/SkillTrees/Base/TreePerk_AttributeModifier", typeof(Perk_AttributeMod));
        }

        /// <summary>
        /// 按名解析 Perk 类型（内容 JSON 里的 <c>perkType</c> 就是这个键）。未知名返回 null。
        /// </summary>
        public static Type ResolvePerkType(string perkTypeName)
        {
            if (string.IsNullOrEmpty(perkTypeName)) return null;
            Type type;
            return _perkTypes.TryGetValue(perkTypeName, out type) ? type : null;
        }

        /// <summary>注册 Perk 类型到工厂。type 必须是 <see cref="TreePerk"/> 子类且具备无参公共构造函数。</summary>
        public static void RegisterPerkType(string perkClassPath, Type type)
        {
            if (string.IsNullOrEmpty(perkClassPath) || type == null) return;
            if (!typeof(TreePerk).IsAssignableFrom(type))
            {
                NarrativeLog.LogError($"无法注册 Perk 类型 '{perkClassPath}': {type.Name} 不是 TreePerk 的子类");
                return;
            }
            _perkTypes[perkClassPath] = type;
        }

        /// <summary>注销 Perk 类型（测试/热重载用）。</summary>
        public static void UnregisterPerkType(string perkClassPath)
        {
            if (!string.IsNullOrEmpty(perkClassPath)) _perkTypes.Remove(perkClassPath);
        }

        /// <summary>
        /// 登记一个 Perk 的作者数据（内容 JSON 里该 Perk 的节点）。
        /// 传入的 <see cref="JsonElement"/> 会被 <c>Clone()</c>：元素原本借用 JsonDocument 的缓冲区，
        /// 文档一失效就变成读不回数据的空壳，这里必须断开依赖。
        /// </summary>
        public static void RegisterPerkDefinition(string perkClassPath, JsonElement node)
        {
            if (string.IsNullOrEmpty(perkClassPath) || node.ValueKind != JsonValueKind.Object) return;
            _perkDefinitions[perkClassPath] = node.Clone();
        }

        /// <summary>清空 Perk 作者数据（热重载/测试用；类型表不动）。</summary>
        public static void ClearPerkDefinitions()
        {
            _perkDefinitions.Clear();
        }

        /// <summary>已登记的 Perk 作者数据条数。</summary>
        public static int RegisteredPathCount => _perkDefinitions.Count;

        /// <summary>该 Perk 类路径是否带作者数据。</summary>
        public static bool HasDefinition(string perkClassPath)
        {
            return !string.IsNullOrEmpty(perkClassPath) && _perkDefinitions.ContainsKey(perkClassPath);
        }

        /// <summary>
        /// 是否已注册指定 Perk 类路径（内容里的 <c>skills[].perks[].perk</c> 与
        /// <c>linkedTo[]</c> 引用都靠它校验，未注册会静默回退成基类）。
        /// </summary>
        public static bool IsRegistered(string perkClassPath)
        {
            return !string.IsNullOrEmpty(perkClassPath)
                   && (_perkTypes.ContainsKey(perkClassPath) || _perkDefinitions.ContainsKey(perkClassPath));
        }

        /// <summary>
        /// 按类路径创建 Perk 实例。对应 UE5 NewObject&lt;UTreePerk&gt;(this, PerkClass)。
        /// 未注册的路径回退到 <see cref="TreePerk"/> 基类并告警（与 ItemFactory 回退策略一致）。
        /// </summary>
        /// <param name="perkClassPath">Perk 类路径标识</param>
        /// <param name="owner">持有该 Perk 的技能树组件（对应 UE5 Outer）</param>
        public static TreePerk Create(string perkClassPath, SkillTreeComponent owner)
        {
            if (string.IsNullOrEmpty(perkClassPath)) return null;

            Type type;
            if (!_perkTypes.TryGetValue(perkClassPath, out type)) type = null;

            JsonElement definition;
            if (_perkDefinitions.TryGetValue(perkClassPath, out definition))
            {
                // 作者数据（显示名/描述/MaxLevels/属性名与倍率）在 UE5 里是 Perk 蓝图类的 CDO 默认值；
                // Flax 侧由内容 JSON 承载，创建时反序列化进具体类型，等价于“按类默认值实例化”。
                try
                {
                    TreePerk perk = JsonSerializer.Deserialize(
                        definition.GetRawText(), type ?? typeof(TreePerk), _definitionOptions) as TreePerk;
                    if (perk != null)
                    {
                        perk.PerkClassPath = perkClassPath;
                        perk.OwningComponent = owner;
                        return perk;
                    }
                    NarrativeLog.LogWarning($"Perk 定义 '{perkClassPath}' 反序列化后为空，回退到类型默认实例");
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"Perk 定义 '{perkClassPath}' 反序列化失败: {ex.Message}");
                }
            }

            if (type != null)
            {
                try
                {
                    TreePerk perk = (TreePerk)Activator.CreateInstance(type);
                    perk.PerkClassPath = perkClassPath;
                    perk.OwningComponent = owner;
                    return perk;
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"创建 Perk 实例失败 '{perkClassPath}': {ex.Message}");
                    return null;
                }
            }

            NarrativeLog.LogWarning($"未注册的 Perk 类型 '{perkClassPath}'，回退到 TreePerk 基类");
            return new TreePerk
            {
                PerkClassPath = perkClassPath,
                OwningComponent = owner
            };
        }
    }
}
