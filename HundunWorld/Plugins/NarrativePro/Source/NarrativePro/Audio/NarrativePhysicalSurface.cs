using System;
using FlaxEngine;

namespace NarrativePro.Audio
{
    /// <summary>
    /// 物理表面类型。对应 UE5 的 <c>EPhysicalSurface</c>
    /// （UFoleyAudioBank 的 FNarrativeFoleySound 就是用它作为 sound/particle 映射的 key）。
    /// <para>
    /// UE5 里 EPhysicalSurface 只有 SurfaceTypeDefault 与 SurfaceType1..9 这些"空槽"，
    /// 具体含义由项目在 <c>DefaultEngine.ini</c> 的 [Physics] 段里赋值。
    /// 本端口把常用赋值直接写成具名成员（数值与 UE5 的槽位顺序一致），
    /// 并通过 <see cref="NarrativePhysicalSurfaceUtility"/> 与 Flax 的
    /// <see cref="PhysicalMaterial.Tag"/>（引擎注释原文建议用它做脚步材质标识）互相映射。
    /// </para>
    /// </summary>
    public enum NarrativePhysicalSurface : byte
    {
        /// <summary>默认表面（UE5 SurfaceTypeDefault）。</summary>
        Default = 0,

        /// <summary>泥土（UE5 SurfaceType1 的常用赋值）。</summary>
        Dirt = 1,

        /// <summary>草地（UE5 SurfaceType2）。</summary>
        Grass = 2,

        /// <summary>碎石（UE5 SurfaceType3）。</summary>
        Gravel = 3,

        /// <summary>金属（UE5 SurfaceType4）。</summary>
        Metal = 4,

        /// <summary>木料（UE5 SurfaceType5）。</summary>
        Wood = 5,

        /// <summary>石材（UE5 SurfaceType6）。</summary>
        Stone = 6,

        /// <summary>雪地（UE5 SurfaceType7）。</summary>
        Snow = 7,

        /// <summary>沙地（UE5 SurfaceType8）。</summary>
        Sand = 8,

        /// <summary>水面（UE5 SurfaceType9）。</summary>
        Water = 9,

        /// <summary>玻璃（超出 UE5 枚举槽位，端口扩展）。</summary>
        Glass = 10,

        /// <summary>布料/地毯（端口扩展）。</summary>
        Fabric = 11
    }

    /// <summary>
    /// 表面类型与 Flax 物理材质标签之间的映射工具。
    /// </summary>
    public static class NarrativePhysicalSurfaceUtility
    {
        /// <summary>Flax 物理材质标签的统一前缀（Flax 引擎注释示例即 "Surface.Wood"）。</summary>
        public const string TagPrefix = "Surface.";

        /// <summary>表面名（与枚举成员同名，用于标签解析与日志）。</summary>
        public static string ToSurfaceName(NarrativePhysicalSurface surface)
        {
            return surface.ToString();
        }

        /// <summary>该表面对应的 Flax 物理材质标签（例如 Wood → "Surface.Wood"）。</summary>
        public static string ToTag(NarrativePhysicalSurface surface)
        {
            if (surface == NarrativePhysicalSurface.Default) return string.Empty;
            return TagPrefix + ToSurfaceName(surface);
        }

        /// <summary>
        /// 按名字解析表面（大小写不敏感，接受 "Wood"、"Surface.Wood"、"surface.wood"）。
        /// </summary>
        public static bool TryFromName(string name, out NarrativePhysicalSurface surface)
        {
            surface = NarrativePhysicalSurface.Default;
            if (string.IsNullOrEmpty(name)) return false;

            string key = name.Trim();
            if (key.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring(TagPrefix.Length);
            }

            // 兼容 UE5 的槽位写法（SurfaceType1 / EPhysicalSurface::SurfaceType2 …）
            if (key.StartsWith("SurfaceType", StringComparison.OrdinalIgnoreCase))
            {
                string digits = key.Substring("SurfaceType".Length);
                if (digits.Length == 0)
                {
                    surface = NarrativePhysicalSurface.Default;
                    return true;
                }
                if (byte.TryParse(digits, out var slot))
                {
                    // SurfaceTypeDefault 在 UE5 里是 0，1..9 与本枚举一致
                    if (slot == 0)
                    {
                        surface = NarrativePhysicalSurface.Default;
                        return true;
                    }
                    if (Enum.IsDefined(typeof(NarrativePhysicalSurface), slot))
                    {
                        surface = (NarrativePhysicalSurface)slot;
                        return true;
                    }
                }
                return false;
            }

            foreach (NarrativePhysicalSurface value in Enum.GetValues(typeof(NarrativePhysicalSurface)))
            {
                if (string.Equals(value.ToString(), key, StringComparison.OrdinalIgnoreCase))
                {
                    surface = value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 从 Flax 物理材质取其表面类型：优先读 <see cref="PhysicalMaterial.Tag"/>
        /// （"Surface.Wood" 之类），解析不出来时返回 <see cref="NarrativePhysicalSurface.Default"/>。
        /// </summary>
        public static NarrativePhysicalSurface FromPhysicalMaterial(PhysicalMaterial material)
        {
            if (material == null) return NarrativePhysicalSurface.Default;

            try
            {
                if (TryFromName(material.Tag.ToString(), out var surface)) return surface;
            }
            catch
            {
                // Tag 解析失败按默认表面处理（与 UE5 无物理材质时返回 SurfaceTypeDefault 一致）
            }

            return NarrativePhysicalSurface.Default;
        }
    }
}
