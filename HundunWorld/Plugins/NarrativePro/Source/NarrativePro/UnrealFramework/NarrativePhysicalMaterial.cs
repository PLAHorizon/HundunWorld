using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Audio;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// Narrative 物理材质。对应 UE5 <c>UNarrativePhysicalMaterial</c>。
    /// <para>
    /// UE5 里它直接继承 <c>UPhysicalMaterial</c>，携带两项数据：
    /// <list type="bullet">
    /// <item><description><c>SurfaceType</c>（EPhysicalSurface）—— foley 库按它取脚步/音效与粒子；</description></item>
    /// <item><description><c>DamageMultiplier</c> —— 命中该材质时伤害计算的额外倍率
    /// （见 UE5 <c>NarrativeDamageExecCalc.cpp:100-106</c>）。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Flax 1.12 的 <see cref="FlaxEngine.PhysicalMaterial"/> 是 <c>sealed</c> 且只派生自
    /// <see cref="FlaxEngine.Object"/>（已核对 FlaxEngine.CSharp.dll：非 Asset、不可继承、
    /// 只能经 <c>Collider.Material</c> 的 JsonAssetReference 绑定），
    /// 所以本端口保留 [Serializable] 包装类的形态，并把它做成真正可用的数据载体：
    /// 表面类型 + 伤害倍率 + 与引擎物理材质的双向绑定（按 Flax 官方的 <c>Tag</c>（"Surface.Wood"）识别）。
    /// </para>
    /// </summary>
    [Serializable]
    public class NarrativePhysicalMaterial
    {
        /// <summary>材质条目名（日志与调试用）。</summary>
        public string Name = string.Empty;

        /// <summary>
        /// 物理表面类型。对应 UE5 <c>UPhysicalMaterial::SurfaceType</c>（EPhysicalSurface），
        /// 是 <see cref="FoleyAudioBank"/> 做 (事件, 表面) 查询的 key。
        /// </summary>
        public NarrativePhysicalSurface Surface = NarrativePhysicalSurface.Default;

        /// <summary>
        /// 命中此材质时应用的特殊伤害倍率。
        /// 对应 UE5 UPROPERTY(EditDefaultsOnly, BlueprintReadOnly) float DamageMultiplier，
        /// 由 NarrativeDamageExecCalc 在结算末尾乘入最终伤害（UE5 源码 100-106 行）。
        /// </summary>
        public float DamageMultiplier = 1f;

        /// <summary>
        /// 关联的 FlaxEngine 物理材质实例（Inspector 直接引用，优先于 <see cref="PhysicalMaterialPath"/>）。
        /// </summary>
        [NonSerialized]
        public PhysicalMaterial PhysicalMaterialAsset;

        /// <summary>
        /// Flax 物理材质所在 JsonAsset 的资产路径。
        /// Flax 的 <c>Collider.Material</c> 是 <c>JsonAssetReference&lt;PhysicalMaterial&gt;</c>，
        /// 因此运行时按路径加载 JsonAsset 后取其 Instance 即可拿到引擎材质。
        /// </summary>
        public string PhysicalMaterialPath = string.Empty;

        /// <summary>
        /// 用于识别引擎材质的 Flax 标签（例如 "Surface.Wood"）。
        /// 引擎材质与条目的匹配顺序：直接引用 → Tag 相同 → 路径。
        /// </summary>
        public string PhysicalMaterialTag = string.Empty;

        public NarrativePhysicalMaterial()
        {
            DamageMultiplier = 1f;
        }

        public NarrativePhysicalMaterial(NarrativePhysicalSurface surface, float damageMultiplier = 1f, string tag = null)
        {
            Surface = surface;
            DamageMultiplier = damageMultiplier;
            PhysicalMaterialTag = tag ?? NarrativePhysicalSurfaceUtility.ToTag(surface);
        }

        // ===== 引擎材质解析 =====

        /// <summary>
        /// 获取关联的 FlaxEngine 物理材质：
        /// 已有直接引用则直接返回；否则按 <see cref="PhysicalMaterialPath"/> 从 JsonAsset 取实例并缓存。
        /// </summary>
        public virtual PhysicalMaterial GetPhysicalMaterial()
        {
            if (PhysicalMaterialAsset != null) return PhysicalMaterialAsset;

            if (string.IsNullOrEmpty(PhysicalMaterialPath)) return null;

            try
            {
                var json = Content.Load<JsonAsset>(PhysicalMaterialPath);
                if (json == null)
                {
                    NarrativeLog.LogWarning($"NarrativePhysicalMaterial: 无法加载物理材质资产 '{PhysicalMaterialPath}'");
                    return null;
                }

                PhysicalMaterialAsset = json.GetInstance<PhysicalMaterial>();
                if (PhysicalMaterialAsset == null)
                {
                    NarrativeLog.LogWarning($"NarrativePhysicalMaterial: 资产 '{PhysicalMaterialPath}' 里没有 PhysicalMaterial 实例");
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"NarrativePhysicalMaterial: 解析物理材质 '{PhysicalMaterialPath}' 失败：{ex.Message}");
            }

            return PhysicalMaterialAsset;
        }

        /// <summary>该条目在 Flax 侧识别用的标签（未显式配置时按表面类型生成 "Surface.Xxx"）。</summary>
        public string GetResolvedTag()
        {
            if (!string.IsNullOrEmpty(PhysicalMaterialTag)) return PhysicalMaterialTag;
            return NarrativePhysicalSurfaceUtility.ToTag(Surface);
        }

        /// <summary>
        /// 把本条目登记到全局注册表（<see cref="Register"/>），
        /// 之后射线命中返回的引擎物理材质即可反查出表面与伤害倍率。
        /// </summary>
        public NarrativePhysicalMaterial RegisterSelf()
        {
            Register(this);
            return this;
        }

        // ===== 全局注册表（连接 Flax 引擎材质 ↔ Narrative 材质数据） =====

        private static readonly List<NarrativePhysicalMaterial> _registered = new List<NarrativePhysicalMaterial>();

        /// <summary>已登记的材质条目（只读）。</summary>
        public static IReadOnlyList<NarrativePhysicalMaterial> Registered => _registered;

        /// <summary>登记一个条目（重复登记按标签覆盖，保证配置刷新生效）。</summary>
        public static void Register(NarrativePhysicalMaterial material)
        {
            if (material == null) return;

            string tag = material.GetResolvedTag();
            if (!string.IsNullOrEmpty(tag))
            {
                for (int i = _registered.Count - 1; i >= 0; i--)
                {
                    var existing = _registered[i];
                    if (existing != null && string.Equals(existing.GetResolvedTag(), tag, StringComparison.OrdinalIgnoreCase))
                    {
                        _registered.RemoveAt(i);
                    }
                }
            }

            _registered.Add(material);
        }

        /// <summary>批量登记。</summary>
        public static void Register(IEnumerable<NarrativePhysicalMaterial> materials)
        {
            if (materials == null) return;
            foreach (var material in materials)
            {
                Register(material);
            }
        }

        /// <summary>清空注册表（切场景/热重载时使用）。</summary>
        public static void ClearRegistered()
        {
            _registered.Clear();
        }

        /// <summary>
        /// 按引擎物理材质查找条目：先比对象引用，再比 Flax 标签。找不到返回 null。
        /// </summary>
        public static NarrativePhysicalMaterial Find(PhysicalMaterial material)
        {
            if (material == null) return null;

            for (int i = 0; i < _registered.Count; i++)
            {
                var entry = _registered[i];
                if (entry == null) continue;
                if (ReferenceEquals(entry.PhysicalMaterialAsset, material)) return entry;
            }

            string tag = SafeTag(material);
            if (string.IsNullOrEmpty(tag)) return null;

            for (int i = 0; i < _registered.Count; i++)
            {
                var entry = _registered[i];
                if (entry == null) continue;
                if (string.Equals(entry.GetResolvedTag(), tag, StringComparison.OrdinalIgnoreCase)) return entry;
            }
            return null;
        }

        /// <summary>按标签（"Wood" / "Surface.Wood" 均可）查找条目。</summary>
        public static NarrativePhysicalMaterial FindByTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;

            for (int i = 0; i < _registered.Count; i++)
            {
                var entry = _registered[i];
                if (entry == null) continue;

                if (string.Equals(entry.GetResolvedTag(), tag, StringComparison.OrdinalIgnoreCase)) return entry;
                if (NarrativePhysicalSurfaceUtility.TryFromName(tag, out var surface) && entry.Surface == surface) return entry;
            }
            return null;
        }

        /// <summary>
        /// 命中结果的表面类型。对应 UE5 的 <c>HitResult.PhysMaterial-&gt;SurfaceType</c>。
        /// </summary>
        public static NarrativePhysicalSurface GetSurface(RayCastHit hit)
        {
            return GetSurface(hit.Material);
        }

        /// <summary>引擎物理材质 → 表面类型（注册表优先，其次 Flax Tag）。</summary>
        public static NarrativePhysicalSurface GetSurface(PhysicalMaterial material)
        {
            if (material == null) return NarrativePhysicalSurface.Default;

            var entry = Find(material);
            if (entry != null) return entry.Surface;

            // 未在注册表里的引擎材质：走 Flax 自带的 Tag 约定
            return NarrativePhysicalSurfaceUtility.FromPhysicalMaterial(material);
        }

        /// <summary>
        /// 命中结果的特殊伤害倍率。对应 UE5 <c>NarrativeDamageExecCalc</c> 里的
        /// <c>MaterialMultiplier = NPM-&gt;DamageMultiplier</c>；没有 Narrative 材质数据时返回 1（不改动伤害）。
        /// </summary>
        public static float GetDamageMultiplier(RayCastHit hit, float fallback = 1f)
        {
            return GetDamageMultiplier(hit.Material, fallback);
        }

        /// <summary>引擎物理材质的特殊伤害倍率。</summary>
        public static float GetDamageMultiplier(PhysicalMaterial material, float fallback = 1f)
        {
            var entry = Find(material);
            return entry != null ? entry.DamageMultiplier : fallback;
        }

        /// <summary>命中结果对应的 foley 表面（供脚步/挥击音效直接播放）。</summary>
        public static NarrativePhysicalSurface GetFoleySurface(RayCastHit hit)
        {
            return GetSurface(hit);
        }

        /// <summary>
        /// 播放"发生在该材质表面上"的一条 foley。
        /// 组合了本类的表面数据与 <see cref="FoleyAudioBank"/> 的 (事件, 表面) 查询。
        /// </summary>
        public static bool PlayFoleyOnSurface(
            PhysicalMaterial material,
            GameplayTag foleyTag,
            FoleyAudioBank bank,
            Vector3 location,
            float volume = 1f,
            bool bSpawnEffect = true)
        {
            if (!foleyTag.IsValid()) return false;

            NarrativePhysicalSurface surface = GetSurface(material);
            return NarrativeFoleyPlayback.PlayFoleyAtLocation(bank, foleyTag, surface, location, volume, 0.08f, bSpawnEffect);
        }

        private static string SafeTag(PhysicalMaterial material)
        {
            try
            {
                return material.Tag.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
