namespace Horizon.Game.ECS.Arch.Components;

/// <summary>
/// 客户端预测 Transform 组件：本地模拟的实体位姿。
/// </summary>
public struct PredictedTransformComponent
{
    /// <summary>世界坐标 X（米）。</summary>
    public float X;

    /// <summary>世界坐标 Y（米）。</summary>
    public float Y;

    /// <summary>世界坐标 Z（米）。</summary>
    public float Z;

    /// <summary>垂直速度（米/秒）。</summary>
    public float Vz;

    /// <summary>偏航角（弧度）。</summary>
    public float Yaw;

    /// <summary>俯仰角（弧度）。</summary>
    public float Pitch;

    /// <summary>产生此预测的客户端 tick 序号。</summary>
    public long ClientTick;

    /// <summary>是否需要服务器校正（收到 correction 后标记为 true）。</summary>
    public bool NeedsReconciliation;

    /// <summary>
    /// 阻尼平滑追平期间的 X 方向速度状态（米/秒）。
    /// 仅在修正追平期间使用，追平完成后清零，不污染正常预测路径。
    /// </summary>
    public float ReconcileVelX;

    /// <summary>
    /// 阻尼平滑追平期间的 Y 方向速度状态（米/秒）。
    /// 仅在修正追平期间使用，追平完成后清零，不污染正常预测路径。
    /// </summary>
    public float ReconcileVelY;

    /// <summary>
    /// 阻尼平滑追平期间的 Z 方向速度状态（米/秒）。
    /// 仅在修正追平期间使用，追平完成后清零，不污染正常预测路径。
    /// </summary>
    public float ReconcileVelZ;
}
