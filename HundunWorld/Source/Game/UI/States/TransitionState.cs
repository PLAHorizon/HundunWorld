using System;

using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.States
{
    /// <summary>
    /// 场景切换阶段枚举
    /// </summary>
    

    /// <summary>
    /// 场景切换状态
    /// 管理场景切换过程中的详细状态信息
    /// </summary>
    [Serializable]
    public class TransitionState
    {
        /// <summary>
        /// 切换操作的唯一标识
        /// </summary>
        public string TransitionId { get; set; } = "";

        /// <summary>
        /// 源场景类型
        /// </summary>
        public SceneType FromScene { get; set; }

        /// <summary>
        /// 目标场景类型
        /// </summary>
        public SceneType ToScene { get; set; }

        /// <summary>
        /// 当前切换阶段
        /// </summary>
        public TransitionPhase CurrentPhase { get; set; } = TransitionPhase.Preparing;

        /// <summary>
        /// 切换进度 (0.0 - 1.0)
        /// </summary>
        public float Progress { get; set; } = 0.0f;

        /// <summary>
        /// 切换开始时间
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 当前阶段开始时间
        /// </summary>
        public DateTime PhaseStartTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 预计总耗时（毫秒）
        /// </summary>
        public int EstimatedDurationMs { get; set; } = 500;

        /// <summary>
        /// 当前阶段预计耗时（毫秒）
        /// </summary>
        public int PhaseEstimatedDurationMs { get; set; } = 100;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// 切换参数
        /// 存储切换过程中需要传递的数据
        /// </summary>
        public System.Collections.Generic.Dictionary<string, object> Parameters { get; set; } = 
            new System.Collections.Generic.Dictionary<string, object>();

        /// <summary>
        /// 是否可以取消
        /// </summary>
        public bool CanCancel { get; set; } = true;

        /// <summary>
        /// 是否已取消
        /// </summary>
        public bool IsCancelled { get; set; } = false;

        /// <summary>
        /// 是否为强制切换
        /// </summary>
        public bool IsForced { get; set; } = false;

        /// <summary>
        /// 切换优先级
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// 切换类型标识
        /// </summary>
        public string TransitionType { get; set; } = "Normal";

        /// <summary>
        /// 创建TransitionState的深拷贝
        /// </summary>
        /// <returns>TransitionState的副本</returns>
        public TransitionState Clone()
        {
            return new TransitionState
            {
                TransitionId = this.TransitionId,
                FromScene = this.FromScene,
                ToScene = this.ToScene,
                CurrentPhase = this.CurrentPhase,
                Progress = this.Progress,
                StartTime = this.StartTime,
                PhaseStartTime = this.PhaseStartTime,
                EstimatedDurationMs = this.EstimatedDurationMs,
                PhaseEstimatedDurationMs = this.PhaseEstimatedDurationMs,
                ErrorMessage = this.ErrorMessage,
                Parameters = new System.Collections.Generic.Dictionary<string, object>(this.Parameters),
                CanCancel = this.CanCancel,
                IsCancelled = this.IsCancelled,
                IsForced = this.IsForced,
                Priority = this.Priority,
                TransitionType = this.TransitionType
            };
        }

        /// <summary>
        /// 设置切换阶段
        /// </summary>
        /// <param name="phase">新的切换阶段</param>
        /// <param name="estimatedDurationMs">该阶段预计耗时</param>
        public void SetPhase(TransitionPhase phase, int estimatedDurationMs = 100)
        {
            CurrentPhase = phase;
            PhaseStartTime = DateTime.UtcNow;
            PhaseEstimatedDurationMs = estimatedDurationMs;
            
            // 自动更新总进度
            UpdateProgress();
        }

        /// <summary>
        /// 更新切换进度
        /// </summary>
        /// <param name="phaseProgress">当前阶段进度 (0.0 - 1.0)</param>
        public void UpdateProgress(float phaseProgress = 0.0f)
        {
            // 根据当前阶段计算总体进度
            float baseProgress = GetPhaseBaseProgress();
            float phaseWeight = GetPhaseWeight();
            
            Progress = Math.Min(1.0f, baseProgress + phaseProgress * phaseWeight);
        }

        /// <summary>
        /// 获取阶段基础进度
        /// </summary>
        /// <returns>阶段基础进度值</returns>
        private float GetPhaseBaseProgress()
        {
            switch (CurrentPhase)
            {
                case TransitionPhase.Preparing: return 0.0f;
                case TransitionPhase.Validating: return 0.1f;
                case TransitionPhase.ExitAnimation: return 0.2f;
                case TransitionPhase.SceneSwitch: return 0.4f;
                case TransitionPhase.DataLoading: return 0.6f;
                case TransitionPhase.EnterAnimation: return 0.8f;
                case TransitionPhase.Completed: return 1.0f;
                case TransitionPhase.Failed: return Progress; // 保持当前进度
                default: return 0.0f;
            }
        }

        /// <summary>
        /// 获取阶段权重
        /// </summary>
        /// <returns>阶段权重值</returns>
        private float GetPhaseWeight()
        {
            switch (CurrentPhase)
            {
                case TransitionPhase.Preparing: return 0.1f;
                case TransitionPhase.Validating: return 0.1f;
                case TransitionPhase.ExitAnimation: return 0.2f;
                case TransitionPhase.SceneSwitch: return 0.2f;
                case TransitionPhase.DataLoading: return 0.2f;
                case TransitionPhase.EnterAnimation: return 0.2f;
                case TransitionPhase.Completed: return 0.0f;
                case TransitionPhase.Failed: return 0.0f;
                default: return 0.1f;
            }
        }

        /// <summary>
        /// 设置错误状态
        /// </summary>
        /// <param name="errorMessage">错误信息</param>
        public void SetError(string errorMessage)
        {
            ErrorMessage = errorMessage;
            CurrentPhase = TransitionPhase.Failed;
        }

        /// <summary>
        /// 取消切换
        /// </summary>
        /// <param name="reason">取消原因</param>
        public void Cancel(string reason = "")
        {
            if (CanCancel && !IsCancelled)
            {
                IsCancelled = true;
                ErrorMessage = string.IsNullOrEmpty(reason) ? "切换已取消" : $"切换已取消: {reason}";
                CurrentPhase = TransitionPhase.Failed;
            }
        }

        /// <summary>
        /// 完成切换
        /// </summary>
        public void Complete()
        {
            CurrentPhase = TransitionPhase.Completed;
            Progress = 1.0f;
        }

        /// <summary>
        /// 获取已耗时（毫秒）
        /// </summary>
        /// <returns>已耗时毫秒数</returns>
        public int GetElapsedMs()
        {
            return (int)(DateTime.UtcNow - StartTime).TotalMilliseconds;
        }

        /// <summary>
        /// 获取当前阶段已耗时（毫秒）
        /// </summary>
        /// <returns>当前阶段已耗时毫秒数</returns>
        public int GetPhaseElapsedMs()
        {
            return (int)(DateTime.UtcNow - PhaseStartTime).TotalMilliseconds;
        }

        /// <summary>
        /// 获取预计剩余时间（毫秒）
        /// </summary>
        /// <returns>预计剩余时间毫秒数</returns>
        public int GetEstimatedRemainingMs()
        {
            int elapsed = GetElapsedMs();
            int remaining = EstimatedDurationMs - elapsed;
            return Math.Max(0, remaining);
        }

        /// <summary>
        /// 检查是否超时
        /// </summary>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>是否超时</returns>
        public bool IsTimeout(int timeoutMs = 10000) // 默认10秒超时
        {
            return GetElapsedMs() > timeoutMs;
        }

        /// <summary>
        /// 设置切换参数
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值</param>
        public void SetParameter(string key, object value)
        {
            Parameters[key] = value;
        }

        /// <summary>
        /// 获取切换参数
        /// </summary>
        /// <typeparam name="T">参数类型</typeparam>
        /// <param name="key">参数键</param>
        /// <returns>参数值，如果不存在则返回默认值</returns>
        public T GetParameter<T>(string key)
        {
            if (Parameters.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default(T);
        }

        /// <summary>
        /// 生成切换报告
        /// </summary>
        /// <returns>切换过程的详细报告</returns>
        public string GenerateReport()
        {
            var report = $"切换报告 [ID: {TransitionId}]\n";
            report += $"源场景: {FromScene} -> 目标场景: {ToScene}\n";
            report += $"切换类型: {TransitionType}\n";
            report += $"当前阶段: {CurrentPhase}\n";
            report += $"进度: {Progress:P2}\n";
            report += $"已耗时: {GetElapsedMs()}ms / 预计: {EstimatedDurationMs}ms\n";
            report += $"状态: {(IsCancelled ? "已取消" : (CurrentPhase == TransitionPhase.Failed ? "失败" : "正常"))}\n";
            
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                report += $"错误信息: {ErrorMessage}\n";
            }

            return report;
        }
    }
}
