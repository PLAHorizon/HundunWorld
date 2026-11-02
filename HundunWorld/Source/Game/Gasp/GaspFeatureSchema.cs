using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// Motion Matching 特征定义（唯一真源）。
    /// 所有位置在“模型空间”里、以当前 pelvis 的水平位置为原点；单位厘米。
    /// 特征在写库/查询时都先乘 sqrt(weight)，因此搜索代价就是加权平方欧氏距离。
    /// </summary>
    public static class GaspFeatureSchema
    {
        // 轨迹采样时间（秒）；0 点隐式为中心。
        public static readonly float[] TrajectoryTimes =
        {
            -0.3f, -0.2f, -0.1f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f
        };

        public static readonly float[] FacingTimes = { 0.1f, 0.3f, 0.5f, 0.8f };

        public const int TrajectoryDim = 22;      // 11 × (x,z)
        public const int FacingDim = 8;           // 4 × (sin,cos)
        public const int PoseDim = 21;            // rootVel + 双脚 pos/vel + head + hands
        public const int Dimension = TrajectoryDim + FacingDim + PoseDim;

        public const int PoseOffset = TrajectoryDim + FacingDim;

        // 原始权重（平方代价前的系数）；数组在静态构造里展开到 Dimension。
        private const float WeightTrajectory = 0.02f;
        private const float WeightFacing = 100f;
        private const float WeightFootPosition = 0.02f;
        private const float WeightFootVelocity = 0.0015f;
        private const float WeightHeadPosition = 0.01f;
        private const float WeightHandPosition = 0.004f;
        private const float WeightRootVelocity = 0.0015f;

        public static readonly float[] SqrtWeights = BuildWeights();

        private static float[] BuildWeights()
        {
            var w = new float[Dimension];
            for (int i = 0; i < TrajectoryTimes.Length; i++)
            {
                w[i * 2] = WeightTrajectory;
                w[i * 2 + 1] = WeightTrajectory;
            }
            for (int i = 0; i < FacingTimes.Length; i++)
            {
                w[TrajectoryDim + i * 2] = WeightFacing;
                w[TrajectoryDim + i * 2 + 1] = WeightFacing;
            }
            int o = PoseOffset;
            w[o] = WeightRootVelocity;
            w[o + 1] = WeightRootVelocity;
            o += 2;
            w[o] = WeightFootPosition; w[o + 1] = WeightFootPosition; w[o + 2] = WeightFootPosition;
            w[o + 3] = WeightFootVelocity; w[o + 4] = WeightFootVelocity;
            o += 5;
            w[o] = WeightFootPosition; w[o + 1] = WeightFootPosition; w[o + 2] = WeightFootPosition;
            w[o + 3] = WeightFootVelocity; w[o + 4] = WeightFootVelocity;
            o += 5;
            w[o] = WeightHeadPosition; w[o + 1] = WeightHeadPosition; w[o + 2] = WeightHeadPosition;
            o += 3;
            w[o] = WeightHandPosition; w[o + 1] = WeightHandPosition; w[o + 2] = WeightHandPosition;
            o += 3;
            w[o] = WeightHandPosition; w[o + 1] = WeightHandPosition; w[o + 2] = WeightHandPosition;
            for (int i = 0; i < Dimension; i++)
                w[i] = Mathf.Sqrt(w[i]);
            return w;
        }

        public static void ApplyWeights(float[] values)
        {
            for (int i = 0; i < Dimension; i++)
                values[i] *= SqrtWeights[i];
        }

        public static float Distance(float[] a, float[] b)
        {
            float sum = 0f;
            for (int i = 0; i < Dimension; i++)
            {
                float d = a[i] - b[i];
                sum += d * d;
            }
            return sum;
        }
    }
}
