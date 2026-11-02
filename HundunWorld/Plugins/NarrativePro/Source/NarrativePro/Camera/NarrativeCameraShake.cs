using FlaxEngine;

namespace NarrativePro.Camera
{
    /// <summary>
    /// 程序化相机震动。对应 UE5 的 <c>UCameraShakeBase</c> 变换噪声。
    /// <para>
    /// 为什么手写：已核对 <c>FlaxEngine.CSharp.xml</c>——Flax 1.12 托管 API 里不存在任何相机震动类型
    /// （全文对 "Shake" 的命中只有 Networking 的 "handshake"，<see cref="FlaxEngine.Camera"/> 成员仅
    /// FieldOfView/NearPlane/FarPlane/Viewport/ProjectPoint 等，无 Shake/CameraShake）。UE5 只在对话侧
    /// （Private/Tales/Dialogue.cpp:718,846）用相机震动，Flax 无对应资产与运行时，故这里用确定性的
    /// 衰减振荡（decaying oscillation）直接叠加到相机位置/旋转上，行为可复现、可被对话镜头调用。
    /// </para>
    /// </summary>
    public class NarrativeCameraShake
    {
        /// <summary>振幅（位置偏移，单位与场景一致；旋转用弧度=amplitude*RotationScale）。</summary>
        public float Amplitude { get; private set; } = 0f;

        /// <summary>总时长（秒）。到期后完全停止。</summary>
        public float Duration { get; private set; } = 0f;

        /// <summary>基础频率（Hz）。</summary>
        public float FrequencyHz { get; private set; } = 8f;

        /// <summary>阻尼比（0~1），越大衰减越快。</summary>
        public float DampingRatio { get; private set; } = 2.2f;

        /// <summary>位置振幅 → 旋转振幅的换算系数（弧度）。</summary>
        public float RotationScale { get; private set; } = 0.01f;

        /// <summary>确定性随机流（固定种子，保证同参数同结果）。</summary>
        private int _seed;

        private float _elapsed;
        private bool _active;

        /// <summary>是否仍在震动。</summary>
        public bool IsActive => _active && Duration > 0f && Amplitude > 0f && _elapsed < Duration;

        /// <summary>剩余时长（秒）。</summary>
        public float Remaining => IsActive ? Duration - _elapsed : 0f;

        public NarrativeCameraShake(int seed = 1337)
        {
            _seed = seed;
        }

        /// <summary>启动一次震动。amplitude/duration<=0 时视为停止。</summary>
        public void Play(float amplitude, float duration, float frequencyHz = 8f, float dampingRatio = 2.2f, int seed = 1337)
        {
            if (amplitude <= 0f || duration <= 0f)
            {
                Stop();
                return;
            }

            Amplitude = amplitude;
            Duration = duration;
            FrequencyHz = frequencyHz > 0.0001f ? frequencyHz : 8f;
            DampingRatio = dampingRatio > 0f ? dampingRatio : 1f;
            _seed = seed;
            _elapsed = 0f;
            _active = true;
        }

        /// <summary>立即停止并归零。</summary>
        public void Stop()
        {
            _active = false;
            _elapsed = 0f;
        }

        /// <summary>
        /// 推进震动并返回本帧叠加用的变换噪声（位置偏移 + 旋转欧拉角，单位分别为场景单位与弧度）。
        /// 三个轴用不同频率倍数与相位（由 <see cref="_seed"/> 确定性派生）做衰减正弦叠加，
        /// 幅度按 <c>Amp * exp(-DampingRatio * t)</c> 包络衰减——这就是"确定性的衰减振荡"。
        /// </summary>
        public Vector3 Advance(float deltaTime, out Vector3 outRotationRadians)
        {
            outRotationRadians = Vector3.Zero;
            if (!IsActive) return Vector3.Zero;

            _elapsed += deltaTime < 0f ? 0f : deltaTime;
            if (_elapsed >= Duration)
            {
                Stop();
                return Vector3.Zero;
            }

            float envelope = Amplitude * Mathf.Exp(-DampingRatio * _elapsed);
            float omega = FrequencyHz * Mathf.TwoPi * _elapsed;

            // 每个轴的频率倍率与相位从固定种子确定性派生（不依赖运行期随机数，保证可复现）
            float f0 = 1.0f, f1 = 1.7f, f2 = 2.3f;
            float p0 = Phase(0), p1 = Phase(1), p2 = Phase(2);

            float x = Mathf.Sin(omega * f0 + p0) + 0.5f * Mathf.Sin(omega * f1 * 2.1f + p1);
            float y = Mathf.Sin(omega * f1 + p1) + 0.5f * Mathf.Sin(omega * f2 * 1.7f + p2);
            float z = Mathf.Sin(omega * f2 + p2);

            Vector3 offset = new Vector3(x, y, z) * envelope;

            // 旋转噪声用更高频、更小的幅度，避免翻滚过大；弧度口径与 Quaternion.Euler 之外的加法一致
            float rx = Mathf.Sin(omega * f2 + p0) * envelope * RotationScale;
            float ry = Mathf.Sin(omega * f0 + p1) * envelope * RotationScale;
            float rz = Mathf.Sin(omega * f1 + p2) * envelope * RotationScale;
            outRotationRadians = new Vector3(rx, ry, rz);

            return offset;
        }

        /// <summary>轴 i 的确定性相位（弧度），由种子线性同余派生。</summary>
        private float Phase(int axis)
        {
            unchecked
            {
                int v = _seed * 1103515245 + axis * 12345 + 12345;
                float normalized = ((v & 0x7FFFFFFF) % 628318) / 100000f; // 0..~6.28
                return normalized;
            }
        }
    }
}
