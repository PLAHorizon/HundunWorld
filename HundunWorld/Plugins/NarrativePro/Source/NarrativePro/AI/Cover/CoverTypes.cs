using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.AI.Cover
{
    /// <summary>
    /// 瓦片边缘方向枚举。适配 UE5 ETileEdge。
    /// </summary>
    public enum ETileEdge
    {
        None = -1,
        Bottom = 0,
        BottomLeft = 1,
        Left = 2,
        TopLeft = 3,
        Top = 4,
        TopRight = 5,
        Right = 6,
        BottomRight = 7,
    }

    /// <summary>
    /// 掩护链单元。由起点、终点和朝向掩护面的法线组成。
    /// 适配 UE5 FChainLink。
    /// </summary>
    [Serializable]
    public struct ChainLink
    {
        /// <summary>生成此边缘的瓦片索引</summary>
        public ulong ParentTileIndex;

        /// <summary>链单元起点</summary>
        public Vector3 Start;

        /// <summary>链单元终点</summary>
        public Vector3 End;

        /// <summary>朝向掩护面的法线</summary>
        public Vector3 CoverNormal;

        public ChainLink(Vector3 start, Vector3 end, Vector3 normal)
        {
            ParentTileIndex = 0;
            Start = start;
            End = end;
            CoverNormal = normal;
            // 计算法线：上向量与前向向量叉乘
            Vector3 forward = end - Start;
            if (forward.LengthSquared > Mathf.Epsilon)
            {
                forward = Vector3.Normalize(forward);
                CoverNormal = Vector3.Cross(Vector3.Up, forward);
            }
            else
            {
                CoverNormal = normal;
            }
        }

        /// <summary>从终点到起点的法线（左方向）</summary>
        public Vector3 GetStartNormal()
        {
            Vector3 d = End - Start;
            return d.LengthSquared > Mathf.Epsilon ? Vector3.Normalize(d) : Vector3.Zero;
        }

        /// <summary>从起点到终点的法线（右方向）</summary>
        public Vector3 GetEndNormal()
        {
            Vector3 d = Start - End;
            return d.LengthSquared > Mathf.Epsilon ? Vector3.Normalize(d) : Vector3.Zero;
        }

        /// <summary>链单元长度</summary>
        public float Length()
        {
            return (End - Start).Length;
        }
    }

    /// <summary>
    /// 掩护链容器。适配 UE5 FCoverChainContainer。
    /// </summary>
    [Serializable]
    public class CoverChainContainer
    {
        public List<ChainLink> Chain = new List<ChainLink>(2);

        public ChainLink Start() => Chain.Count > 0 ? Chain[0] : default;
        public ChainLink End() => Chain.Count > 0 ? Chain[Chain.Count - 1] : default;
    }

    /// <summary>
    /// 链单元索引容器。适配 UE5 FChainLinkIndexContainer。
    /// </summary>
    [Serializable]
    public class ChainLinkIndexContainer
    {
        public List<sbyte> ChainIndexes = new List<sbyte>();

        public bool AnyChains() => ChainIndexes.Count > 0;

        public ChainLinkIndexContainer() { }

        public ChainLinkIndexContainer(sbyte chainIndex)
        {
            ChainIndexes = new List<sbyte> { chainIndex };
        }

        public static readonly ChainLinkIndexContainer Invalid = new ChainLinkIndexContainer();
    }

    /// <summary>
    /// 边界哈希信息。适配 UE5 FBoundaryHashInfo。
    /// 标记一个瓦片边界点连接到哪个相邻瓦片的哪条掩护链。
    /// </summary>
    [Serializable]
    public struct BoundaryHashInfo
    {
        public long NextTileIndex;
        public sbyte CoverChainIndex;

        public BoundaryHashInfo(sbyte coverChainIndex, long nextTileIndex)
        {
            NextTileIndex = nextTileIndex;
            CoverChainIndex = coverChainIndex;
        }

        public bool IsValid() => NextTileIndex != -1 && CoverChainIndex != -1;
    }

    /// <summary>
    /// 单个瓦片的掩护表示。适配 UE5 FTileCover。
    /// </summary>
    [Serializable]
    public class TileCover
    {
        /// <summary>此瓦片决定的掩护链</summary>
        public List<CoverChainContainer> OwnedCovers = new List<CoverChainContainer>(4);

        /// <summary>边界哈希：起点/终点位置 → 相邻瓦片掩护链索引</summary>
        public Dictionary<Vector3, BoundaryHashInfo> BoundaryHash = new Dictionary<Vector3, BoundaryHashInfo>();

        public static readonly TileCover Invalid = new TileCover();

        public bool AnyCovers()
        {
            foreach (var cover in OwnedCovers)
            {
                if (cover.Chain.Count > 0) return true;
            }
            return false;
        }

        public bool HashContains(Vector3 point)
        {
            return BoundaryHash.ContainsKey(point);
        }

        public BoundaryHashInfo GetCoverFromHash(Vector3 point)
        {
            BoundaryHashInfo info;
            BoundaryHash.TryGetValue(point, out info);
            return info;
        }

        public int EndLinkIndex(int coverChainIndex)
        {
            if (coverChainIndex < 0 || coverChainIndex >= OwnedCovers.Count) return -1;
            return OwnedCovers[coverChainIndex].Chain.Count - 1;
        }
    }

    /// <summary>
    /// 掩护容器。适配 UE5 FCoverContainer。
    /// 表示一条完整掩护链及其所属瓦片信息。
    /// </summary>
    [Serializable]
    public class CoverContainer
    {
        public long ParentTileIndex = -1;
        public int CoverChainIndex = -1;
        public List<ChainLink> CoverChain = new List<ChainLink>();

        public CoverContainer() { }

        public CoverContainer(List<ChainLink> chain, long tileIndex, sbyte coverIndex)
        {
            ParentTileIndex = tileIndex;
            CoverChainIndex = coverIndex;
            if (chain != null) CoverChain = new List<ChainLink>(chain);
        }

        public bool IsValid()
        {
            return ParentTileIndex != -1 && CoverChainIndex != -1 && CoverChain.Count > 0;
        }

        public ChainLink Start() => CoverChain.Count > 0 ? CoverChain[0] : default;
        public ChainLink End() => CoverChain.Count > 0 ? CoverChain[CoverChain.Count - 1] : default;

        public void Reset()
        {
            ParentTileIndex = -1;
            CoverChainIndex = -1;
            CoverChain.Clear();
        }
    }

    /// <summary>
    /// 掩护射线检测配置。适配 UE5 FCoverTraceConfig。
    /// 定义低掩/高掩/侧探等射线检测参数。
    /// </summary>
    [Serializable]
    public class CoverTraceConfig
    {
        /// <summary>沿掩护链采样点间距</summary>
        public float CoverSpacing = 150.0f;

        /// <summary>低掩的半高</summary>
        public float HalfHeight = 85.0f;

        /// <summary>高掩（可越过）的高度</summary>
        public float PeekOverHeight = 150.0f;

        /// <summary>侧探射线高度</summary>
        public float PeekSideHeight = 115.0f;

        /// <summary>侧探横向偏移</summary>
        public float PeekSideWidth = 65.0f;

        /// <summary>朝掩护方向射线深度</summary>
        public float TraceDepth = 100.0f;

        /// <summary>4 条检测射线的起点（本地坐标）。顺序：低→高→左探→右探</summary>
        public Vector3[] TraceStart = new Vector3[4];

        public CoverTraceConfig()
        {
            UpdateTraceStarts();
        }

        public void UpdateTraceStarts()
        {
            TraceStart[0] = new Vector3(0.0f, 0.0f, HalfHeight);       // 低掩
            TraceStart[1] = new Vector3(0.0f, 0.0f, PeekOverHeight);    // 高掩
            TraceStart[2] = new Vector3(0.0f, -PeekSideWidth, PeekSideHeight); // 左探
            TraceStart[3] = new Vector3(0.0f, PeekSideWidth, PeekSideHeight);   // 右探
        }

        public Vector3 Low() => TraceStart[0];
        public Vector3 High() => TraceStart[1];
        public Vector3 LeanLeft() => TraceStart[2];
        public Vector3 LeanRight() => TraceStart[3];
    }
}
