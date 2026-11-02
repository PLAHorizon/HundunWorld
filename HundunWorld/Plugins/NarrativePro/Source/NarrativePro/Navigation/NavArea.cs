using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航区域（通行代价类别）。适配 UE5 UNavArea。
    /// <para>
    /// UE5 中 NavArea 由 Recast 烘焙时写入多边形，路径搜索按 AreaCost 加权。
    /// Flax 1.12 的托管导航 API（<c>FlaxEngine.Navigation.FindPath</c>）不暴露 area/代价过滤，
    /// 因此本端口把区域代价实现为 <see cref="NavAreaSystem"/> 的代价场，
    /// 由 <see cref="NavigatorStatics.TryFindPath"/> 在拿到引擎路径后做代价优化二次搜索来消费。
    /// </para>
    /// </summary>
    [Serializable]
    public class NavArea
    {
        /// <summary>区域显示名（对应 UE5 UNavArea::Name）。</summary>
        public string Name = "";

        /// <summary>默认通行代价（对应 UE5 DefaultCost，1.0 表示无加成）。</summary>
        public float DefaultCost = 1f;

        /// <summary>调试绘制颜色（对应 UE5 DrawColor）。</summary>
        public Color DrawColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);

        /// <summary>烘焙时是否把此区域作为默认区域（对应 UE5 bSupportsAssembly）。</summary>
        public bool bIsDefaultArea = false;

        public NavArea() { }

        public NavArea(string name, float defaultCost, Color drawColor)
        {
            Name = name;
            DefaultCost = defaultCost;
            DrawColor = drawColor;
        }

        /// <summary>地面网格的默认区域（代价 1.0）。对应 UE5 UNavArea_Default。</summary>
        public static readonly NavArea Default = new NavArea("Default", 1f, new Color(0.4f, 0.4f, 0.4f, 0.5f)) { bIsDefaultArea = true };

        /// <summary>不可通行区域（代价无穷）。对应 UE5 UNavArea_Null。</summary>
        public static readonly NavArea Null = new NavArea("Null", float.MaxValue, new Color(0.2f, 0.2f, 0.2f, 0.9f));
    }

    /// <summary>
    /// 道路导航区域。适配 UE5 UNavArea_Road（DefaultCost = 0.5，DrawColor = FColor(75,50,150)）。
    /// 走道路的代价是地面的 0.5 倍，因此代价优化的路径搜索会优先贴着道路走。
    /// </summary>
    [Serializable]
    public class NavAreaRoad : NavArea
    {
        /// <summary>道路默认代价（对应 UE5 DefaultCost 0.5）。</summary>
        public const float RoadCost = 0.5f;

        public NavAreaRoad()
        {
            Name = "Road";
            DefaultCost = RoadCost;
            // UE5 FColor(75, 50, 150) —— 归一化到 0~1 的线性颜色
            DrawColor = new Color(75f / 255f, 50f / 255f, 150f / 255f, 0.5f);
        }

        /// <summary>共享实例（对应 UE5 UNavArea_Road::StaticClass()）。</summary>
        public static readonly NavAreaRoad Instance = new NavAreaRoad();
    }
}
