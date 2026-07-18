using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.CharacterCreator
{
    /// <summary>
    /// 颜色色板。对应 UE5 UCharacterCreatorColorSwatch。
    /// 角色创建器使用的颜色集合。
    /// </summary>
    [Serializable]
    public class CreatorColorSwatch
    {
        /// <summary>色板颜色列表</summary>
        public List<Color> Colors = new List<Color>();

        /// <summary>获取指定索引处的颜色</summary>
        public Color GetColor(uint index)
        {
            if (Colors == null || Colors.Count == 0) return Color.Black;
            int idx = (int)(index % (uint)Colors.Count);
            return Colors[idx];
        }

        /// <summary>随机获取一个颜色</summary>
        public Color GetColorRandom()
        {
            if (Colors == null || Colors.Count == 0) return Color.Black;
            int randIndex = _random.Next(Colors.Count);
            return GetColor((uint)randIndex);
        }

        private static readonly System.Random _random = new System.Random();
    }
}
