using System;
using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>一个 UE 语义目标在 Flax 侧的处理状态。</summary>
    public enum MetaHumanMaterialTargetState
    {
        /// <summary>已落定：映射到一个规格表里存在的 Flax 参数名。</summary>
        Mapped,

        /// <summary>等你拍板：语义上确有对应物，但走哪条通路是**产品判定**，不该由实现替你选。</summary>
        PendingUserDecision,

        /// <summary>有记录的判定：明知有对应物但**刻意不做**（附理由），不是"缺数据所以先空着"。</summary>
        Declined,

        /// <summary>不在这张表的范围内：眼睛/毛发要各自另立材质规格（毛发本身卡在 #34）。</summary>
        OutOfSpecScope,
    }

    public sealed class MetaHumanMaterialTargetEntry
    {
        public string Target;
        public MetaHumanMaterialTargetState State;
        public string FlaxParameter;
        public string Note;
    }

    /// <summary>
    /// 脸模板声明的 9 个材质效果目标 → Flax 侧落点。
    ///
    /// 这 9 条不是抄来的：它们就是 `Template_Face_Archetype.json` 里
    /// `Kind ∈ {SkinTone, SkinDetail, Eye, Hair}` 的那 9 条参数所声明的 `Effects[].Target`
    /// （2+3+2+2 = 9，全为 `MaterialFloat`）。离线门会**从模板数据反读一遍**来核对这张表，
    /// 而不是信任这里手写的清单——两边对不上就红。
    ///
    /// 为什么要有 `PendingUserDecision` 这一态：把"泛红/皱纹/胡茬走哪条通路"直接写成某个映射
    /// 或直接写成 declined，都等于替产品做了判定；写成一个**会计数、会显示**的中间态，
    /// 才能既往前推进结构，又不把责任悄悄挪到实现这边。
    /// </summary>
    public static class MetaHumanMaterialMap
    {
        public static List<MetaHumanMaterialTargetEntry> Entries()
        {
            List<MetaHumanMaterialTargetEntry> list = new List<MetaHumanMaterialTargetEntry>();

            // 语义直通的两条：规格表里各只有唯一对应物，不需要产品判定。
            // Melanin 不能再直写 Color_MAIN：那是**贴图**参数（FaceColor_MAIN），写浮点会被类型不匹配静默丢掉；
            // 现在走材质图新加的 SkinTint 乘色通道（Color ← 原色 × SkinTint），由 applier 做 1.15→0.45 的亮度映射。
            Add(list, "Skin_Melanin", MetaHumanMaterialTargetState.Mapped, "SkinTint",
                "黑色素=整体明度：Color_MAIN 是贴图，不能写浮点；已加 SkinTint 乘色节点，applier 按 melanin 0..1 → tint 1.15..0.45");
            Add(list, "Skin_Roughness", MetaHumanMaterialTargetState.Mapped, "SkinRoughness",
                "Roughness_MAIN 是贴图参数，不能直写浮点；材质图已接 采样R × SkinRoughness → 根 Roughness，applier 映射为 0.4..1.6 倍率");

            // 三条已由用户拍板（2026-10-15）：通路选定后就不再是"待判定"，每条的出处写在 Note 里。
            Add(list, "Skin_Redness", MetaHumanMaterialTargetState.Mapped, "SkinRedness",
                "FoundationBlusherMask 是贴图不能直写；材质图按 掩码R × SkinRedness 作为 alpha，把 Color 向 SkinRedColor（暗红）插值");
            Add(list, "Skin_Wrinkles", MetaHumanMaterialTargetState.Declined, null,
                "用户判定记 declined：对应 Normal_WM1..3 那组法线加权，而 WM*→Normal 的混合链是未支持项；只换贴图视觉上不对，宁可不做");
            Add(list, "Skin_Stubble", MetaHumanMaterialTargetState.Mapped, "Skin_Stubble",
                "2026-10-23 已建通路：材质图新增 float Skin_Stubble，消费 Mask_Stubble（BakedGroomRootTipGradientRegionMasks 改指派生掩码）"
                + "× 常量胡茬色 → 乘到可见肤色；此前映射到贴图参数会被 applier 的类型闸门跳过（掩码也全零）");

            // 规格外：这两组要各自另立材质规格，不在这张表里。
            Add(list, "Eye_IrisColor", MetaHumanMaterialTargetState.OutOfSpecScope, null,
                "眼睛材质另立规格（材质目录里已有 eyeLeft/eyeRight）");
            Add(list, "Eye_PupilSize", MetaHumanMaterialTargetState.OutOfSpecScope, null,
                "同上");
            Add(list, "Hair_BaseColor", MetaHumanMaterialTargetState.OutOfSpecScope, null,
                "毛发随 #34（卡片不透明度那条路线已判死）");
            Add(list, "Hair_Roughness", MetaHumanMaterialTargetState.OutOfSpecScope, null,
                "同上");

            return list;
        }

        private static void Add(List<MetaHumanMaterialTargetEntry> list, string target,
            MetaHumanMaterialTargetState state, string flaxParameter, string note)
        {
            list.Add(new MetaHumanMaterialTargetEntry
            {
                Target = target,
                State = state,
                FlaxParameter = flaxParameter,
                Note = note,
            });
        }

        /// <summary>还等你拍板的目标名（要显示出来，不许静默留在中间态）。</summary>
        public static List<string> PendingUserDecisions()
        {
            List<string> pending = new List<string>();
            List<MetaHumanMaterialTargetEntry> all = Entries();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].State == MetaHumanMaterialTargetState.PendingUserDecision)
                {
                    pending.Add(all[i].Target);
                }
            }

            return pending;
        }

        /// <summary>
        /// Flax 侧为实现 UE 语义而**新增**的参数（不是 UE 材质里的原生参数，因此不进 Head_Material_Spec）。
        /// 目前只有 SkinTint：UE 的肤色是换贴图，而 Color_MAIN 是贴图参数不能写浮点；
        /// 做法是在材质图根 Color 上乘一个标量，由 applier 把 melanin 映射到 1.15..0.45。
        /// 白名单必须显式列在这里：映射门只允许"UE 原生参数"或"这里有记录的引擎增补"，不许自造。
        /// </summary>
        public static List<string> EngineAddedFlaxParameters()
        {
            return new List<string> { "SkinTint", "SkinRoughness", "SkinRedness", "Skin_Stubble" };
        }

        /// <summary>已经落定的 Flax 参数名（离线门会用它们核对是否 ⊆ 规格表）。</summary>
        public static List<string> MappedFlaxParameters()
        {
            List<string> mapped = new List<string>();
            List<MetaHumanMaterialTargetEntry> all = Entries();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].State == MetaHumanMaterialTargetState.Mapped && !string.IsNullOrEmpty(all[i].FlaxParameter))
                {
                    mapped.Add(all[i].FlaxParameter);
                }
            }

            return mapped;
        }
    }
}
