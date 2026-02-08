using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Horizon.Orleans.Silo.Monitoring
{
    /// <summary>
    /// 混沌世界Orleans Silo自定义指标定义
    /// 提供Grain调用、认证、会话等核心业务指标
    /// </summary>
    public static class HorizonMetrics
    {
        /// <summary>
        /// 指标源名称
        /// </summary>
        public const string MeterName = "HundunWorld.Silo";

        /// <summary>
        /// 活动源名称（用于分布式追踪）
        /// </summary>
        public const string ActivitySourceName = "HundunWorld.Silo";

        private static readonly Meter Meter = new(MeterName, "1.0.0");
        private static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

        // ========== Grain调用指标 ==========

        /// <summary>Grain方法调用总数</summary>
        public static readonly Counter<long> GrainCallsTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.grain.calls.total",
            description: "Grain方法调用总数");

        /// <summary>Grain方法调用失败总数</summary>
        public static readonly Counter<long> GrainCallErrorsTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.grain.call_errors.total",
            description: "Grain方法调用失败总数");

        /// <summary>Grain方法执行时长（毫秒）</summary>
        public static readonly Histogram<double> GrainCallDuration = Meter.CreateHistogram<double>(
            "hundunworld.silo.grain.call_duration.ms",
            unit: "ms",
            description: "Grain方法执行时长");

        // ========== 认证指标 ==========

        /// <summary>登录尝试总数</summary>
        public static readonly Counter<long> LoginAttemptsTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.auth.login_attempts.total",
            description: "登录尝试总数");

        /// <summary>登录成功总数</summary>
        public static readonly Counter<long> LoginSuccessTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.auth.login_success.total",
            description: "登录成功总数");

        /// <summary>登录失败总数</summary>
        public static readonly Counter<long> LoginFailuresTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.auth.login_failures.total",
            description: "登录失败总数");

        /// <summary>注册总数</summary>
        public static readonly Counter<long> RegistrationsTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.auth.registrations.total",
            description: "用户注册总数");

        /// <summary>密码变更总数</summary>
        public static readonly Counter<long> PasswordChangesTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.auth.password_changes.total",
            description: "密码变更总数");

        // ========== 会话指标 ==========

        /// <summary>活跃会话数</summary>
        public static readonly UpDownCounter<long> ActiveSessions = Meter.CreateUpDownCounter<long>(
            "hundunworld.silo.sessions.active",
            description: "当前活跃会话数");

        /// <summary>会话创建总数</summary>
        public static readonly Counter<long> SessionsCreatedTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.sessions.created.total",
            description: "会话创建总数");

        /// <summary>会话销毁总数</summary>
        public static readonly Counter<long> SessionsTerminatedTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.sessions.terminated.total",
            description: "会话销毁总数");

        // ========== 任务监控指标 ==========

        /// <summary>运行中的任务数</summary>
        public static readonly UpDownCounter<long> RunningTasks = Meter.CreateUpDownCounter<long>(
            "hundunworld.silo.tasks.running",
            description: "运行中的任务数");

        /// <summary>失败的任务总数</summary>
        public static readonly Counter<long> FailedTasksTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.tasks.failed.total",
            description: "失败的任务总数");

        // ========== 数据库指标 ==========

        /// <summary>数据库查询总数</summary>
        public static readonly Counter<long> DbQueriesTotal = Meter.CreateCounter<long>(
            "hundunworld.silo.db.queries.total",
            description: "数据库查询总数");

        /// <summary>数据库查询耗时（毫秒）</summary>
        public static readonly Histogram<double> DbQueryDuration = Meter.CreateHistogram<double>(
            "hundunworld.silo.db.query_duration.ms",
            unit: "ms",
            description: "数据库查询耗时");

        // ========== 分布式追踪辅助方法 ==========

        /// <summary>
        /// 创建一个Grain调用的活动追踪
        /// </summary>
        public static Activity? StartGrainActivity(string grainType, string methodName)
        {
            return ActivitySource.StartActivity(
                $"Grain/{grainType}/{methodName}",
                ActivityKind.Server);
        }
    }
}
