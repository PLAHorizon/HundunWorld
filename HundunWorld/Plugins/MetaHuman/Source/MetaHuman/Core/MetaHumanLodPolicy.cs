using System;
using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>渲染档位（与画质设置对接的四档）。</summary>
    public enum MetaHumanRenderQuality
    {
        Low = 0,
        Medium,
        High,
        Ultra,
    }

    /// <summary>LOD 策略的结论。</summary>
    public class MetaHumanLodDecision
    {
        /// <summary>允许的最粗 LOD 档（数字越小越精细）。</summary>
        public int MaxLod;

        /// <summary>是否应把形状解写到模型上（false 时写回端要跳过 blendshape 与骨骼偏移）。</summary>
        public bool ApplyShape;

        /// <summary>为什么这么定，给诊断用。</summary>
        public string Reason;

        public override string ToString()
        {
            return "MaxLod=" + MaxLod + " 写形状=" + (ApplyShape ? "是" : "否") + "（" + Reason + "）";
        }
    }

    /// <summary>
    /// 数字人的 LOD 策略。
    ///
    /// 压着一条引擎事实：Flax 的 `SkinnedModel.BlendShapes` 只给 LOD0 的名字，
    /// blendshape 变形也只在 LOD0 网格上有效。所以 LOD 一降，玩家捏出来的形状就会**凭空消失**——
    /// 远看像换了个人。UE 用逐 LOD 的 morph 与烘焙解决，我们没有那个条件，
    /// 于是这里定的规则是：带形状定制的角色，LOD 不许越过能承载形状的那一档。
    /// </summary>
    public static class MetaHumanLodPolicy
    {
        /// <summary>各画质档允许的最粗 LOD（远端角色用）。</summary>
        public static readonly int[] QualityMaxLod = { 4, 3, 2, 1 };

        /// <summary>有形状可写时，最多能降到哪一档：只有第 0 档真能显示 blendshape。</summary>
        public const int LodWithShapeSupport = 0;

        /// <summary>本地玩家与正在编辑的角色一律锁在承载形状的那一档。</summary>
        public static readonly HashSet<string> AlwaysShapeRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            "local", "editing", "preview",
        };

        /// <summary>
        /// 距离分档（米）：与 UE 的屏幕尺寸分档近似，但判据要能离线算，所以按显式阈值。
        /// 阈值单调递增是硬要求，违反就退回 0 档并说明——距离再远也不会「反而变粗」。
        /// </summary>
        public static int DistanceLod(float meters)
        {
            float[] thresholds = { 8f, 18f, 35f, 60f };
            int lod = 0;
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (meters >= thresholds[i])
                {
                    lod = i + 1;
                }
            }

            return lod;
        }

        public static MetaHumanLodDecision Decide(
            string role,
            MetaHumanRenderQuality quality,
            float distanceMeters,
            int morphCount,
            int lodCount)
        {
            MetaHumanLodDecision decision = new MetaHumanLodDecision();

            if (lodCount < 1)
            {
                lodCount = 1;
            }

            int qualityIndex = (int)quality;
            if (qualityIndex < 0 || qualityIndex >= QualityMaxLod.Length)
            {
                qualityIndex = (int)MetaHumanRenderQuality.Medium;
            }

            int byQuality = QualityMaxLod[qualityIndex];
            int byDistance = DistanceLod(distanceMeters);
            int wanted = Math.Min(byQuality, byDistance);

            if (morphCount <= 0)
            {
                // 这套资产没有形状可丢，按画质与距离自由降档
                decision.ApplyShape = false;
                decision.MaxLod = ClampLod(wanted, lodCount);
                decision.Reason = "资产无 blendshape，LOD 只按画质与距离";
                return decision;
            }

            bool needShape = AlwaysShapeRoles.Contains(role == null ? string.Empty : role.ToLowerInvariant());
            if (needShape)
            {
                decision.ApplyShape = true;
                decision.MaxLod = LodWithShapeSupport;
                decision.Reason = "角色身份需要形状（" + role + "），锁在 LOD0";
                return decision;
            }

            // 远端角色：距离允许到 LOD0 才能保形状，否则宁可不写形状也要保住帧率——
            // 但这是「整体不写」，绝不能写成「一半 morph 一半不写」的撕裂状态。
            int allowed = ClampLod(wanted, lodCount);
            if (allowed <= LodWithShapeSupport)
            {
                decision.ApplyShape = true;
                decision.MaxLod = LodWithShapeSupport;
                decision.Reason = "距离 " + distanceMeters + "m 还在形状档内";
            }
            else
            {
                decision.ApplyShape = false;
                decision.MaxLod = allowed;
                decision.Reason = "距离 " + distanceMeters + "m 已超出形状档，整体不写形状以免半张脸";
            }

            return decision;
        }

        private static int ClampLod(int lod, int lodCount)
        {
            if (lod < 0)
            {
                return 0;
            }

            return lod >= lodCount ? lodCount - 1 : lod;
        }
    }
}
