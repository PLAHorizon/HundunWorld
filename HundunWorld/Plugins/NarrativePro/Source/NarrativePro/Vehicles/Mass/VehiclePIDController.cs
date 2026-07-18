using System;
using FlaxEngine;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具 PID 控制器。对应 UE5 FVehiclePIDController（VehiclePIDController.h）。
    /// USTRUCT(BlueprintType)。Flax 中改为 [Serializable] struct。
    /// 用于载具油门/转向的 PID 控制。
    /// </summary>
    [Serializable]
    public struct VehiclePIDController
    {
        /// <summary>误差积分。</summary>
        [NonSerialized]
        public float ErrorIntegral;

        /// <summary>上次误差。</summary>
        [NonSerialized]
        public float LastError;

        /// <summary>
        /// 执行一次 PID 计算并返回控制量。对应 UE5 FVehiclePIDController::Tick。
        /// </summary>
        /// <param name="goal">目标值。</param>
        /// <param name="actual">实际值。</param>
        /// <param name="param">PID 参数。</param>
        /// <returns>PID 控制输出。</returns>
        public float Tick(float goal, float actual, PIDSettings param)
        {
            if (param == null) return 0f;

            float error = goal - actual;
            ErrorIntegral += error;
            float derivative = error - LastError;
            LastError = error;

            return param.ProportionalGain * error
                 + param.IntegralGain * ErrorIntegral
                 + param.DerivativeGain * derivative;
        }

        /// <summary>重置误差积分。对应 UE5 ResetErrorIntegral。</summary>
        public void ResetErrorIntegral()
        {
            ErrorIntegral = 0.0f;
        }
    }
}
