using System;
using System.Runtime.CompilerServices;

namespace Horizon.Game.Core.Sim;

/// <summary>
/// 确定性移动数学层（P1-b）。<br/>
/// 客户端 <c>LocalSimulationSystem</c>（预测）与服务器 <c>ZoneShardGrain</c>（权威回放）
/// 都必须调用本类，以保证在同样 (pos, input, dt) 下得到**按位一致**的位移结果。
/// </summary>
/// <remarks>
/// 实现约束：
/// <list type="bullet">
///   <item>纯静态方法，不依赖全局状态；所有入参 + 出参用 <c>float</c>。</item>
///   <item>不得调用 <c>System.Numerics.Vector</c> 等 SIMD 路径（不同 CPU 结果可能微差）。</item>
///   <item>运算顺序、类型转换严格固定，便于跨语言/跨运行时复现。</item>
/// </list>
/// 任何修改都必须更新 <see cref="FormulaVersion"/>，以便两端协商。
/// </remarks>
public static class MovementFormula
{
    /// <summary>
    /// 移动公式版本号；客户端/服务器不一致则视为协议不兼容，须升级包体。
    /// </summary>
    public const int FormulaVersion = 2;

    /// <summary>世界重力加速度（米/秒²），沿 -Z 方向。</summary>
    public const float Gravity = 9.81f;

    /// <summary>垂直速度终端值（达到后不再加速）。</summary>
    public const float TerminalVelocity = 50f;

    /// <summary>玩家移动的默认最大水平速度（米/秒）；可被外部 override。</summary>
    public const float DefaultMaxSpeed = 6f;

    /// <summary>
    /// 水平移动一步：在 XY 平面按 (moveX, moveY) 输入量以 <paramref name="maxSpeed"/> 前进 <paramref name="dt"/> 秒。
    /// </summary>
    /// <param name="x">当前 X。</param>
    /// <param name="y">当前 Y。</param>
    /// <param name="moveX">输入 X 分量，[-1, 1]，超出将被夹紧。</param>
    /// <param name="moveY">输入 Y 分量，[-1, 1]，超出将被夹紧。</param>
    /// <param name="dt">时间步长（秒），要求严格正数。</param>
    /// <param name="maxSpeed">最大速度；非正值时使用 <see cref="DefaultMaxSpeed"/>。</param>
    /// <returns>新的 (X, Y)。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (float X, float Y) StepHorizontal(float x, float y, float moveX, float moveY, float dt, float maxSpeed)
    {
        if (dt <= 0f) return (x, y);
        if (maxSpeed <= 0f) maxSpeed = DefaultMaxSpeed;

        // 1) 夹紧输入
        moveX = Clamp(moveX, -1f, 1f);
        moveY = Clamp(moveY, -1f, 1f);

        // 2) 归一化输入向量（长度 > 1 时），避免斜向走得比正向快
        var lenSq = moveX * moveX + moveY * moveY;
        if (lenSq > 1f)
        {
            var inv = 1f / MathF.Sqrt(lenSq);
            moveX *= inv;
            moveY *= inv;
        }

        // 3) 线性步进；顺序严格固定
        var vx = moveX * maxSpeed;
        var vy = moveY * maxSpeed;
        return (x + vx * dt, y + vy * dt);
    }

    /// <summary>
    /// 垂直移动一步：在 Z 轴按 (vz, jump) 做跳跃/重力模拟。
    /// 返回新的 (Z, Vz)；外部应在地面检测后将 Vz 置 0。
    /// </summary>
    /// <param name="z">当前 Z。</param>
    /// <param name="vz">当前 Z 方向速度。</param>
    /// <param name="jumpImpulse">本帧跳跃冲量（0 表示不跳；推荐 5.5f ≈ 1.5m 跳高）。</param>
    /// <param name="dt">时间步长（秒）。</param>
    /// <returns>(新 Z, 新 Vz)。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (float Z, float Vz) StepVertical(float z, float vz, float jumpImpulse, float dt)
    {
        if (dt <= 0f) return (z, vz);

        // 1) 跳跃冲量叠加（仅在 jumpImpulse > 0 时生效；地面检测由调用方保证）
        vz += jumpImpulse;

        // 2) 重力积分
        vz -= Gravity * dt;

        // 3) 终端速度截断（双向都截，避免浮点积分飞出）
        if (vz < -TerminalVelocity) vz = -TerminalVelocity;
        else if (vz > TerminalVelocity) vz = TerminalVelocity;

        return (z + vz * dt, vz);
    }

    /// <summary>
    /// 组合一步：水平 + 垂直按 (input, dt) 推进。便于测试/简单场景。
    /// </summary>
    public static (float X, float Y, float Z, float Vz) Step(
        float x, float y, float z, float vz,
        float moveX, float moveY, float jumpImpulse,
        float dt, float maxSpeed)
    {
        var (nx, ny) = StepHorizontal(x, y, moveX, moveY, dt, maxSpeed);
        var (nz, nvz) = StepVertical(z, vz, jumpImpulse, dt);
        return (nx, ny, nz, nvz);
    }

    /// <summary>
    /// 计算 (a, b) 两点之间的 3D 欧氏距离。用于 <see cref="MovementValidator"/> 位置偏差阈值比较。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance3D(float ax, float ay, float az, float bx, float by, float bz)
    {
        var dx = ax - bx;
        var dy = ay - by;
        var dz = az - bz;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// <see cref="Math.Clamp(float, float, float)"/> 的内联等价物，避免 netstandard 场景下的封装开销。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Clamp(float v, float lo, float hi)
    {
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }
}
