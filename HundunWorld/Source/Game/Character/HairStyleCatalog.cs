using System;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 发型目录：模板参数 hair.style 的离散下标 → 发卡模型资产。
    ///
    /// 资产来源是 UE 5.8 `MetaHumanCharacter` 插件的可选 Groom 库（见 Tools/metahuman_hair_export.py），
    /// 统一按 Scale=100 导入、槽 0 指认 MH_Hair。`Hair_S_Clean` 的极短卡会埋进头皮（实测近乎光头），
    /// 故不列入。下标必须与模板参数 hair.style 的 Max 对齐（0..5）。
    /// </summary>
    public static class HairStyleCatalog
    {
        public const int Count = 6;

        private static readonly string[] Paths =
        {
            "Content/Character/Models/mhhair/Hair_S_Coil_Cards.flax",
            "Content/Character/Models/mhhair/Hair_S_Pixie_CardsMesh_Group0_LOD0.flax",
            "Content/Character/Models/mhhair/Hair_M_BobStraight_CardsMesh_Group0_LOD0.flax",
            "Content/Character/Models/mhhair/Hair_S_BobLayered_CardsMesh_Group0_LOD0.flax",
            "Content/Character/Models/mhhair/Hair_S_LowPonytail_CardsMesh_Group0_LOD0.flax",
            "Content/Character/Models/mhhair/Hair_M_SideSweptFringe_CardsMesh_Group0_LOD0.flax",
        };

        private static readonly string[] Names =
        {
            "卷发", "精灵短发", "直波波头", "层次波波", "低马尾", "侧分刘海",
        };

        /// <summary>
        /// 每套发型的卡片覆盖率掩码（UE Groom 的 RootUVSeedCoverage 导出）。
        /// 材质 `MH_Hair_Cutout` 用它的 alpha 做 Mask（Opaque + MaskThreshold 即切边），
        /// 换发型时必须同时换这张图，否则会用上一套的剪切形状。
        /// </summary>
        private static readonly string[] MaskPaths =
        {
            "Content/Character/Skin/Hair_S_Coil_RootUVSeedCoverage.flax",
            "Content/Character/Skin/Hair_S_Pixie_RootUVSeedCoverage.flax",
            "Content/Character/Skin/Hair_M_BobStraight_RootUVSeedCoverage.flax",
            "Content/Character/Skin/Hair_S_BobLayered_RootUVSeedCoverage.flax",
            "Content/Character/Skin/Hair_S_LowPonytail_RootUVSeedCoverage.flax",
            "Content/Character/Skin/Hair_M_SideSweptFringe_RootUVSeedCoverage.flax",
        };

        public static string MaskPathFor(int index)
        {
            return MaskPaths[Clamp(index)];
        }

        public static int Clamp(int index)
        {
            if (index < 0)
            {
                return 0;
            }

            return index >= Count ? Count - 1 : index;
        }

        public static string PathFor(int index)
        {
            return Paths[Clamp(index)];
        }

        public static string NameFor(int index)
        {
            return Names[Clamp(index)];
        }
    }
}
