using Horizon.Game.Core.Configuration;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 默认兴趣分级策略实现：按距离映射近/中/远三档，携带滞回保护（spec 5.5.3.2）。
/// </summary>
/// <remarks>
/// <para>
/// 滞回规则（防边界抖动）：分级切换使用非对称阈值——
/// 升档（近→中/中→远）以 <c>档位边界 + HysteresisMeters</c> 为阈值，
/// 降档（远→中/中→近）以 <c>档位边界 - HysteresisMeters</c> 为阈值，
/// 实体在边界附近往复移动时分级结果不频繁抖动。
/// </para>
/// <para>
/// 字段与频率：近档下发全量字段（<see cref="InterestGradeOptions.NearSnapshotHz"/>）；
/// 中/远档裁剪低频字段（<see cref="ShouldSendFullFields"/> 返回 false），
/// 频率分别为 <see cref="InterestGradeOptions.MidSnapshotHz"/> / <see cref="InterestGradeOptions.FarSnapshotHz"/>。
/// </para>
/// <para>
/// 线程安全：本策略实例维护单一分级状态（上次档位），用于滞回判定；
/// 生产环境建议每个 ZoneShard 会话持有独立实例。
/// </para>
/// </remarks>
public sealed class DefaultSyncInterestGradeStrategy : ISyncInterestGradeStrategy
{
    private readonly InterestGradeOptions _options;
    private readonly float _nearUp;
    private readonly float _nearDown;
    private readonly float _midUp;
    private readonly float _midDown;

    // 上次分级档位（滞回判定状态）。
    private InterestGrade _lastGrade;

    /// <summary>
    /// 创建默认分级策略实例。
    /// </summary>
    /// <param name="options">分级参数（应为经 <see cref="InterestGradeValidator"/> 校验后的合法配置）。</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="options"/> 为 null。</exception>
    public DefaultSyncInterestGradeStrategy(InterestGradeOptions? options)
    {
        _options = options ?? new InterestGradeOptions();
        _nearUp = _options.NearDistanceMeters + _options.HysteresisMeters;
        _nearDown = _options.NearDistanceMeters - _options.HysteresisMeters;
        _midUp = _options.MidDistanceMeters + _options.HysteresisMeters;
        _midDown = _options.MidDistanceMeters - _options.HysteresisMeters;
        _lastGrade = ClassifyStateless(0f);
    }

    /// <inheritdoc />
    public InterestGrade Classify(float distanceMeters)
    {
        _lastGrade = ClassifyWithHysteresis(distanceMeters, _lastGrade);
        return _lastGrade;
    }

    /// <inheritdoc />
    public bool ShouldSendFullFields(InterestGrade grade) => grade == InterestGrade.Near;

    /// <inheritdoc />
    public int GetSnapshotHz(InterestGrade grade) => grade switch
    {
        InterestGrade.Near => _options.NearSnapshotHz,
        InterestGrade.Mid => _options.MidSnapshotHz,
        InterestGrade.Far => _options.FarSnapshotHz,
        _ => _options.FarSnapshotHz,
    };

    private InterestGrade ClassifyStateless(float distanceMeters)
    {
        if (distanceMeters <= _options.NearDistanceMeters) return InterestGrade.Near;
        if (distanceMeters <= _options.MidDistanceMeters) return InterestGrade.Mid;
        return InterestGrade.Far;
    }

    private InterestGrade ClassifyWithHysteresis(float distanceMeters, InterestGrade current)
    {
        switch (current)
        {
            case InterestGrade.Near:
                // 升档阈值 = 近档边界 + 滞回。
                if (distanceMeters > _nearUp)
                {
                    return distanceMeters > _midUp ? InterestGrade.Far : InterestGrade.Mid;
                }
                return InterestGrade.Near;

            case InterestGrade.Mid:
                // 降档阈值 = 近档边界 - 滞回；升档阈值 = 中档边界 + 滞回。
                if (distanceMeters < _nearDown) return InterestGrade.Near;
                if (distanceMeters > _midUp) return InterestGrade.Far;
                return InterestGrade.Mid;

            case InterestGrade.Far:
                // 降档阈值 = 中档边界 - 滞回。
                if (distanceMeters < _midDown)
                {
                    return distanceMeters <= _nearUp ? InterestGrade.Near : InterestGrade.Mid;
                }
                return InterestGrade.Far;

            default:
                return ClassifyStateless(distanceMeters);
        }
    }
}