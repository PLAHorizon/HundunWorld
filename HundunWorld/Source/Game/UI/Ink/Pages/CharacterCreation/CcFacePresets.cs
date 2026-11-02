using FlaxEngine;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{
    /// <summary>
    /// 面容预设表。
    /// <para>
    /// 名称沿用旧创建流程 <c>UI/Character/FacePresetCard.cs</c> 的 8 项表，
    /// 索引即 <c>AppearanceInfo.HairModel / FaceModel</c> 的取值，保持与后端一致。
    /// 前 4 项归「风雅」、后 4 项归「写实」，对应参考图 图片/2-5.png 的两个分类页签。
    /// </para>
    /// </summary>
    internal static class CcFacePresets
    {
        /// <summary>风格分类名（下标即 <see cref="CcSession.StyleCategory"/>）。</summary>
        public static readonly string[] Categories = { "风雅", "写实" };

        /// <summary>衣装 / 体型预设名，下标写入 <c>AppearanceInfo.Clothing</c>（参考图 图片/5.png 最右一列）。</summary>
        public static readonly string[] Outfits = { "布衣", "劲装", "道袍", "儒衫", "甲胄", "蓑衣" };

        // 下面三组色板分别对应 AppearanceInfo 的 HairColor / EyeColor / SkinColor（离散下标）。
        // 旧创建流程这三个字段全传 0，所以这是第一次真正把它们带上值。

        /// <summary>发色。</summary>
        public static readonly Color[] HairColors =
        {
            new Color(0.10f, 0.10f, 0.12f), new Color(0.28f, 0.18f, 0.12f), new Color(0.45f, 0.28f, 0.16f),
            new Color(0.78f, 0.78f, 0.80f), new Color(0.92f, 0.90f, 0.86f), new Color(0.14f, 0.18f, 0.30f),
        };

        /// <summary>瞳色。</summary>
        public static readonly Color[] EyeColors =
        {
            new Color(0.32f, 0.20f, 0.12f), new Color(0.12f, 0.12f, 0.14f), new Color(0.62f, 0.42f, 0.16f),
            new Color(0.24f, 0.48f, 0.44f), new Color(0.32f, 0.40f, 0.54f),
        };

        /// <summary>肤色。</summary>
        public static readonly Color[] SkinColors =
        {
            new Color(0.93f, 0.85f, 0.76f), new Color(0.86f, 0.72f, 0.58f), new Color(0.78f, 0.62f, 0.46f),
            new Color(0.97f, 0.91f, 0.84f), new Color(0.48f, 0.34f, 0.26f), new Color(0.90f, 0.86f, 0.82f),
        };

        /// <summary>
        /// 每个肤色下标对应的 `skin.melanin`（0..1）。色板顺序**不是按明度排的**，
        /// 直接拿 `i/(n-1)` 会让浅色块把皮肤调黑（下标 3/5 就是最浅的两块）。
        /// 这里按色板自身的亮度反推：越暗 → melanin 越高，保证"点哪个色，皮肤接近哪个色"。
        /// </summary>
        public static readonly float[] SkinMelaninByIndex = BuildSkinMelanin();

        private static float[] BuildSkinMelanin()
        {
            int count = SkinColors.Length;
            var luma = new float[count];
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                Color c = SkinColors[i];
                float l = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
                luma[i] = l;
                if (l < min)
                {
                    min = l;
                }

                if (l > max)
                {
                    max = l;
                }
            }

            float span = System.Math.Max(1e-4f, max - min);
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = Mathf.Clamp((max - luma[i]) / span, 0f, 1f);
            }

            return result;
        }

        /// <summary>色板下标 → skin.melanin。</summary>
        public static float MelaninOf(int index)
        {
            return SkinMelaninByIndex[Mathf.Clamp(index, 0, SkinMelaninByIndex.Length - 1)];
        }

        /// <summary>skin.melanin → 最接近的色板下标（撤销/预设回填用）。</summary>
        public static int ClosestSkinIndex(float melanin)
        {
            int best = 0;
            float bestDelta = float.MaxValue;
            for (int i = 0; i < SkinMelaninByIndex.Length; i++)
            {
                float delta = System.Math.Abs(SkinMelaninByIndex[i] - melanin);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = i;
                }
            }

            return best;
        }

        private static readonly string[] MaleNames = { "清秀", "儒雅", "俊朗", "阳光", "英气", "刚毅", "豪迈", "冷峻" };
        private static readonly string[] FemaleNames = { "温婉", "端庄", "清丽", "甜美", "灵动", "妩媚", "冷艳", "温柔" };

        /// <summary>
        /// 预设的**稳定 id**（下标与上面两张名字表一一对应）。
        /// <para>
        /// 为什么不拿中文名当 id：捏脸会话按 id 调预设，而名字是要改文案、要出英文界面的展示文本
        /// ——展示文本不能当身份用。id 里带性别是因为男女两套名字的下标会撞（0..7 各一份）。
        /// </para>
        /// </summary>
        private static readonly string[] MalePresetIds =
        {
            "face.preset.male.01", "face.preset.male.02", "face.preset.male.03", "face.preset.male.04",
            "face.preset.male.05", "face.preset.male.06", "face.preset.male.07", "face.preset.male.08",
        };

        private static readonly string[] FemalePresetIds =
        {
            "face.preset.female.01", "face.preset.female.02", "face.preset.female.03", "face.preset.female.04",
            "face.preset.female.05", "face.preset.female.06", "face.preset.female.07", "face.preset.female.08",
        };

        /// <summary>预设下标（<c>CcSession.FacePresetIndex</c>）→ 模板预设 id；越界返回 null。</summary>
        public static string PresetIdOf(int index)
        {
            string[] table = CcSession.Current.Gender == 1 ? FemalePresetIds : MalePresetIds;
            if (index < 0 || index >= table.Length)
            {
                return null;
            }

            return table[index];
        }

        /// <summary>预设下标对应的展示名（状态栏/卡片用，不参与任何判定）。</summary>
        public static string PresetNameOf(int index)
        {
            string[] table = CcSession.Current.Gender == 1 ? FemaleNames : MaleNames;
            if (index < 0 || index >= table.Length)
            {
                return null;
            }

            return table[index];
        }

        /// <summary>取某性别、某分类下的预设（名 + 全局索引）。</summary>
        public static List<(string Name, int Index)> GetByCategory(int gender, int category)
        {
            string[] table = gender == 1 ? FemaleNames : MaleNames;
            int baseIndex = category == 1 ? 4 : 0;

            var list = new List<(string, int)>(4);
            for (int i = baseIndex; i < baseIndex + 4; i++)
                list.Add((table[i], i));
            return list;
        }

        /// <summary>全部 8 项预设（「更多面容」展开时用）。</summary>
        public static List<(string Name, int Index)> GetAll(int gender)
        {
            string[] table = gender == 1 ? FemaleNames : MaleNames;

            var list = new List<(string, int)>(table.Length);
            for (int i = 0; i < table.Length; i++)
                list.Add((table[i], i));
            return list;
        }
    }
}
