using System;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 姿态扭曲（Animation Warping）的纯几何层：只做矩阵运算，不依赖状态机，便于离线验证。
    /// 组件空间为引擎的 Y-up（骨骼组件矩阵已转正）。
    /// </summary>
    public static class GaspPoseWarp
    {
        /// <summary>
        /// 把整副姿态绕“枢轴骨骼位置”的竖直轴旋转 <paramref name="radians"/>。
        /// 枢轴本身位置保持不变（只转朝向）；距离/缩放不受影响。
        /// </summary>
        /// <returns>实际应用的弧度。</returns>
        public static float RotateYAbout(Matrix[] comp, int pivotNode, float radians)
        {
            if (comp == null || pivotNode < 0 || pivotNode >= comp.Length || Mathf.Abs(radians) < 1e-6f)
                return 0f;

            var pivot = comp[pivotNode].TranslationVector;
            var toPivot = Matrix.Translation(-pivot);
            var fromPivot = Matrix.Translation(pivot);
            var rot = Matrix.RotationY(radians);
            var about = toPivot * rot * fromPivot;
            // 行向量约定：v' = v * comp * about（先做骨骼变换，再绕枢轴做世界旋转）。
            for (int i = 0; i < comp.Length; i++)
                comp[i] = comp[i] * about;
            return radians;
        }

        /// <summary>把角度折到 (-π, π]。</summary>
        public static float WrapAngle(float radians)
        {
            while (radians > Mathf.Pi)
                radians -= Mathf.TwoPi;
            while (radians < -Mathf.Pi)
                radians += Mathf.TwoPi;
            return radians;
        }

        /// <summary>
        /// 计算转向扭曲角：把剪辑“本来要去的方向”扭到“实际要去方向”，
        /// 限制在 ±<paramref name="maxDegrees"/> 内，并按速度占比缩放（静止不扭）。
        /// </summary>
        /// <param name="desiredHeading">实际移动朝向（弧度，世界/模型同一平面）。</param>
        /// <param name="clipHeading">剪辑标称移动朝向（弧度）。</param>
        /// <param name="speed">当前速度（cm/s）。</param>
        /// <param name="walkSpeed">参考速度（cm/s），速度达到它时按满额扭曲。</param>
        public static float ComputeSteer(float desiredHeading, float clipHeading, float speed,
            float walkSpeed, float maxDegrees)
        {
            if (speed <= 1f || maxDegrees <= 0f)
                return 0f;
            float delta = WrapAngle(desiredHeading - clipHeading);
            float maxRad = maxDegrees * Mathf.DegreesToRadians;
            delta = Mathf.Clamp(delta, -maxRad, maxRad);
            float scale = Mathf.Saturate(speed / Mathf.Max(walkSpeed, 1f));
            return delta * scale;
        }

        /// <summary>按最大角速度逼近目标角（度/秒），返回新角。</summary>
        public static float Approach(float current, float target, float maxDegreesPerSecond, float dt)
        {
            float maxStep = Mathf.Max(0f, maxDegreesPerSecond) * Mathf.DegreesToRadians * Mathf.Max(0f, dt);
            float delta = target - current;
            if (delta > maxStep) delta = maxStep;
            else if (delta < -maxStep) delta = -maxStep;
            return current + delta;
        }
    }
}
