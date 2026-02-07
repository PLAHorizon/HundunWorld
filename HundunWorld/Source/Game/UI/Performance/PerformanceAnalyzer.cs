using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxEngine;
using Game.UI.Controllers;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Controllers;

namespace Game.UI.Performance
{
    /// <summary>
    /// 性能分析器
    /// 提供基准测试、性能报告生成和优化建议功能
    /// </summary>
    public class PerformanceAnalyzer : Script
    {
        #region Singleton
        
        private static PerformanceAnalyzer _instance;
        public static PerformanceAnalyzer Instance => _instance;
        
        #endregion
        
        #region Performance Data Structures
        
        /// <summary>
        /// 基准测试结果
        /// </summary>
        public class BenchmarkResult
        {
            public string TestName { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public TimeSpan Duration { get; set; }
            public float AverageFrameRate { get; set; }
            public float MinFrameRate { get; set; }
            public float MaxFrameRate { get; set; }
            public long StartMemory { get; set; }
            public long EndMemory { get; set; }
            public long PeakMemory { get; set; }
            public int GcCollections { get; set; }
            public Dictionary<string, float> CustomMetrics { get; set; } = new Dictionary<string, float>();
        }
        
        /// <summary>
        /// 性能分析报告
        /// </summary>
        public class PerformanceAnalysisReport
        {
            public DateTime GeneratedAt { get; set; }
            public string SystemInfo { get; set; }
            public List<BenchmarkResult> BenchmarkResults { get; set; } = new List<BenchmarkResult>();
            public PerformanceComparison Comparison { get; set; }
            public List<OptimizationRecommendation> Recommendations { get; set; } = new List<OptimizationRecommendation>();
            public PerformanceSummary Summary { get; set; }
        }
        
        /// <summary>
        /// 性能对比数据
        /// </summary>
        public class PerformanceComparison
        {
            public BenchmarkResult BeforeOptimization { get; set; }
            public BenchmarkResult AfterOptimization { get; set; }
            public float FrameRateImprovement { get; set; }
            public long MemoryReduction { get; set; }
            public float SwitchTimeImprovement { get; set; }
        }
        
        /// <summary>
        /// 优化建议
        /// </summary>
        public class OptimizationRecommendation
        {
            public string Category { get; set; }
            public string Issue { get; set; }
            public string Recommendation { get; set; }
            public int Priority { get; set; } // 1-5, 5最高
            public float EstimatedImpact { get; set; }
        }
        
        /// <summary>
        /// 性能摘要
        /// </summary>
        public class PerformanceSummary
        {
            public float OverallScore { get; set; } // 0-100分
            public string PerformanceGrade { get; set; } // A, B, C, D, F
            public List<string> Strengths { get; set; } = new List<string>();
            public List<string> Weaknesses { get; set; } = new List<string>();
            public Dictionary<string, float> CategoryScores { get; set; } = new Dictionary<string, float>();
        }
        
        #endregion
        
        #region Private Fields
        
        private readonly List<BenchmarkResult> _benchmarkHistory = new List<BenchmarkResult>();
        private UIPerformanceMonitor _performanceMonitor;
        private MemoryOptimizationManager _memoryManager;
        private UISwitchController _switchController;
        
        // 基准值配置
        private readonly float _targetFrameRate = 60.0f;
        private readonly float _acceptableFrameRate = 30.0f;
        private readonly long _targetMemoryUsage = 200 * 1024 * 1024; // 200MB
        private readonly float _targetSwitchTime = 1.0f; // 1秒
        
        #endregion
        
        #region Unity Lifecycle
        
        public override void OnAwake()
        {
            if (_instance == null)
            {
                _instance = this;
                Destroy(this);
                
                InitializeAnalyzer();
                FlaxEngine.Debug.Log("[PerformanceAnalyzer] 性能分析器已初始化");
            }
            else if (_instance != this)
            {
                Destroy(this);
            }
        }
        
        public override void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// 运行基准测试
        /// </summary>
        public async Task<BenchmarkResult> RunBenchmarkAsync(string testName, Func<Task> testAction)
        {
            FlaxEngine.Debug.Log($"[PerformanceAnalyzer] 开始基准测试: {testName}");
            
            var result = new BenchmarkResult
            {
                TestName = testName,
                StartTime = DateTime.Now
            };
            
            // 记录开始状态
            result.StartMemory = GC.GetTotalMemory(true);
            var startGcCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            
            var frameRates = new List<float>();
            var stopwatch = Stopwatch.StartNew();
            
            // 启动性能监控
            var monitoringTask = Task.Run(async () =>
            {
                while (stopwatch.IsRunning)
                {
                    if (_performanceMonitor != null)
                    {
                        frameRates.Add(_performanceMonitor.CurrentStats.CurrentFrameRate);
                    }
                    await Task.Delay(100);
                }
            });
            
            try
            {
                // 执行测试
                await testAction();
            }
            finally
            {
                stopwatch.Stop();
                result.EndTime = DateTime.Now;
                result.Duration = stopwatch.Elapsed;
                
                // 记录结束状态
                result.EndMemory = GC.GetTotalMemory(false);
                result.PeakMemory = Math.Max(result.StartMemory, result.EndMemory);
                result.GcCollections = (GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2)) - startGcCount;
                
                // 计算帧率统计
                if (frameRates.Count > 0)
                {
                    result.AverageFrameRate = frameRates.Average();
                    result.MinFrameRate = frameRates.Min();
                    result.MaxFrameRate = frameRates.Max();
                }
                
                await monitoringTask;
            }
            
            _benchmarkHistory.Add(result);
            
            FlaxEngine.Debug.Log($"[PerformanceAnalyzer] 基准测试完成: {testName}, 耗时: {result.Duration.TotalSeconds:F2}s, 平均帧率: {result.AverageFrameRate:F1}fps");
            
            return result;
        }
        
        /// <summary>
        /// 生成性能分析报告
        /// </summary>
        public PerformanceAnalysisReport GenerateReport()
        {
            FlaxEngine.Debug.Log("[PerformanceAnalyzer] 开始生成性能分析报告...");
            
            var report = new PerformanceAnalysisReport
            {
                GeneratedAt = DateTime.Now,
                SystemInfo = GetSystemInfo(),
                BenchmarkResults = new List<BenchmarkResult>(_benchmarkHistory)
            };
            
            // 生成性能对比
            if (_benchmarkHistory.Count >= 2)
            {
                report.Comparison = GenerateComparison();
            }
            
            // 生成优化建议
            report.Recommendations = GenerateRecommendations();
            
            // 生成性能摘要
            report.Summary = GenerateSummary();
            
            FlaxEngine.Debug.Log("[PerformanceAnalyzer] 性能分析报告生成完成");
            
            return report;
        }
        
        /// <summary>
        /// 保存报告到文件
        /// </summary>
        public async Task SaveReportAsync(PerformanceAnalysisReport report, string filePath)
        {
            var content = GenerateReportContent(report);
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
            
            FlaxEngine.Debug.Log($"[PerformanceAnalyzer] 性能报告已保存到: {filePath}");
        }
        
        /// <summary>
        /// 运行完整性能分析
        /// </summary>
        public async Task<PerformanceAnalysisReport> RunCompleteAnalysisAsync()
        {
            FlaxEngine.Debug.Log("[PerformanceAnalyzer] 开始完整性能分析...");
            
            // UI切换性能基准测试
            await RunBenchmarkAsync("UI切换性能测试", async () =>
            {
                var scenes = new[] { SceneType.Login, SceneType.CharacterSelection, SceneType.CharacterSelection, SceneType.GameWorld };
                
                foreach (var scene in scenes)
                {
                    await _switchController.RequestSceneSwitchAsync(scene);
                    
                    await Task.Delay(2000); // 等待切换完成
                }
            });
            
            // 内存使用基准测试
            await RunBenchmarkAsync("内存使用基准测试", async () =>
            {
                var objects = new List<object>();
                
                // 分配内存
                for (int i = 0; i < 1000; i++)
                {
                    objects.Add(_memoryManager.RentObject<Dictionary<string, object>>());
                }
                
                await Task.Delay(1000);
                
                // 清理内存
                foreach (var obj in objects.OfType<Dictionary<string, object>>())
                {
                    _memoryManager.ReturnObject(obj);
                }
                
                _memoryManager.PerformCleanup();
                await Task.Delay(1000);
            });
            
            // 并发处理基准测试
            await RunBenchmarkAsync("并发处理基准测试", async () =>
            {
                var tasks = new List<Task>();
                
                for (int i = 0; i < 10; i++)
                {
                    tasks.Add(_switchController.RequestSceneSwitchAsync(SceneType.CharacterSelection,TransitionType.Instant));
                }
                
                await Task.WhenAll(tasks);
            });
            
            return GenerateReport();
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// 初始化分析器
        /// </summary>
        private void InitializeAnalyzer()
        {
            _performanceMonitor = Actor.GetScript<UIPerformanceMonitor>();
            _memoryManager = MemoryOptimizationManager.Instance;
            _switchController = Actor.GetScript<UISwitchController>();
        }
        
        /// <summary>
        /// 获取系统信息
        /// </summary>
        private string GetSystemInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"操作系统: {Environment.OSVersion}");
            sb.AppendLine($"处理器数量: {Environment.ProcessorCount}");
            sb.AppendLine($"工作集内存: {Environment.WorkingSet / (1024 * 1024)}MB");
            sb.AppendLine($"CLR版本: {Environment.Version}");
            sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// 生成性能对比
        /// </summary>
        private PerformanceComparison GenerateComparison()
        {
            var recent = _benchmarkHistory.TakeLast(2).ToArray();
            if (recent.Length < 2) return null;
            
            var before = recent[0];
            var after = recent[1];
            
            return new PerformanceComparison
            {
                BeforeOptimization = before,
                AfterOptimization = after,
                FrameRateImprovement = after.AverageFrameRate - before.AverageFrameRate,
                MemoryReduction = before.PeakMemory - after.PeakMemory,
                SwitchTimeImprovement = (float)(before.Duration.TotalSeconds - after.Duration.TotalSeconds)
            };
        }
        
        /// <summary>
        /// 生成优化建议
        /// </summary>
        private List<OptimizationRecommendation> GenerateRecommendations()
        {
            var recommendations = new List<OptimizationRecommendation>();
            
            if (_benchmarkHistory.Count == 0) return recommendations;
            
            var latest = _benchmarkHistory.Last();
            
            // 帧率相关建议
            if (latest.AverageFrameRate < _acceptableFrameRate)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Category = "帧率性能",
                    Issue = $"平均帧率({latest.AverageFrameRate:F1}fps)低于可接受水平({_acceptableFrameRate}fps)",
                    Recommendation = "考虑减少UI切换动画复杂度，优化渲染流程，使用对象池减少GC压力",
                    Priority = 5,
                    EstimatedImpact = 0.8f
                });
            }
            
            // 内存相关建议
            if (latest.PeakMemory > _targetMemoryUsage)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Category = "内存使用",
                    Issue = $"峰值内存使用({latest.PeakMemory / (1024 * 1024)}MB)超过目标值({_targetMemoryUsage / (1024 * 1024)}MB)",
                    Recommendation = "启用内存优化管理器，增加对象池使用，定期执行内存清理",
                    Priority = 4,
                    EstimatedImpact = 0.6f
                });
            }
            
            // 切换时间相关建议
            if (latest.Duration.TotalSeconds > _targetSwitchTime)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Category = "切换性能",
                    Issue = $"UI切换时间({latest.Duration.TotalSeconds:F2}s)超过目标值({_targetSwitchTime}s)",
                    Recommendation = "使用异步加载，预加载常用场景，优化状态管理流程",
                    Priority = 3,
                    EstimatedImpact = 0.7f
                });
            }
            
            // GC相关建议
            if (latest.GcCollections > 5)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Category = "垃圾回收",
                    Issue = $"GC次数过多({latest.GcCollections}次)，可能影响流畅度",
                    Recommendation = "减少临时对象创建，使用结构体或对象池，避免频繁字符串操作",
                    Priority = 3,
                    EstimatedImpact = 0.5f
                });
            }
            
            return recommendations.OrderByDescending(r => r.Priority).ToList();
        }
        
        /// <summary>
        /// 生成性能摘要
        /// </summary>
        private PerformanceSummary GenerateSummary()
        {
            var summary = new PerformanceSummary();
            
            if (_benchmarkHistory.Count == 0)
            {
                summary.OverallScore = 0;
                summary.PerformanceGrade = "未测试";
                return summary;
            }
            
            var latest = _benchmarkHistory.Last();
            
            // 计算各项分数
            var frameRateScore = CalculateFrameRateScore(latest.AverageFrameRate);
            var memoryScore = CalculateMemoryScore(latest.PeakMemory);
            var switchTimeScore = CalculateSwitchTimeScore((float)latest.Duration.TotalSeconds);
            var gcScore = CalculateGCScore(latest.GcCollections);
            
            summary.CategoryScores["帧率性能"] = frameRateScore;
            summary.CategoryScores["内存使用"] = memoryScore;
            summary.CategoryScores["切换性能"] = switchTimeScore;
            summary.CategoryScores["垃圾回收"] = gcScore;
            
            // 计算总分
            summary.OverallScore = (frameRateScore + memoryScore + switchTimeScore + gcScore) / 4.0f;
            
            // 确定等级
            summary.PerformanceGrade = summary.OverallScore switch
            {
                >= 90 => "A",
                >= 80 => "B",
                >= 70 => "C",
                >= 60 => "D",
                _ => "F"
            };
            
            // 生成优势和劣势
            GenerateStrengthsAndWeaknesses(summary);
            
            return summary;
        }
        
        /// <summary>
        /// 计算帧率分数
        /// </summary>
        private float CalculateFrameRateScore(float frameRate)
        {
            if (frameRate >= _targetFrameRate) return 100f;
            if (frameRate >= _acceptableFrameRate) return 70f + (frameRate - _acceptableFrameRate) / (_targetFrameRate - _acceptableFrameRate) * 30f;
            return Math.Max(0, frameRate / _acceptableFrameRate * 70f);
        }
        
        /// <summary>
        /// 计算内存分数
        /// </summary>
        private float CalculateMemoryScore(long memoryUsage)
        {
            if (memoryUsage <= _targetMemoryUsage) return 100f;
            
            var overUsage = memoryUsage - _targetMemoryUsage;
            var penaltyRatio = Math.Min(1.0f, (float)overUsage / _targetMemoryUsage);
            
            return Math.Max(0, 100f - penaltyRatio * 50f);
        }
        
        /// <summary>
        /// 计算切换时间分数
        /// </summary>
        private float CalculateSwitchTimeScore(float switchTime)
        {
            if (switchTime <= _targetSwitchTime) return 100f;
            
            var penalty = Math.Min(50f, (switchTime - _targetSwitchTime) * 25f);
            return Math.Max(0, 100f - penalty);
        }
        
        /// <summary>
        /// 计算GC分数
        /// </summary>
        private float CalculateGCScore(int gcCount)
        {
            if (gcCount <= 2) return 100f;
            
            var penalty = Math.Min(50f, (gcCount - 2) * 10f);
            return Math.Max(0, 100f - penalty);
        }
        
        /// <summary>
        /// 生成优势和劣势
        /// </summary>
        private void GenerateStrengthsAndWeaknesses(PerformanceSummary summary)
        {
            foreach (var category in summary.CategoryScores)
            {
                if (category.Value >= 80f)
                {
                    summary.Strengths.Add($"{category.Key}表现良好({category.Value:F1}分)");
                }
                else if (category.Value < 60f)
                {
                    summary.Weaknesses.Add($"{category.Key}需要改进({category.Value:F1}分)");
                }
            }
            
            if (summary.Strengths.Count == 0)
            {
                summary.Strengths.Add("系统运行基本稳定");
            }
            
            if (summary.Weaknesses.Count == 0)
            {
                summary.Weaknesses.Add("暂无明显性能问题");
            }
        }
        
        /// <summary>
        /// 生成报告内容
        /// </summary>
        private string GenerateReportContent(PerformanceAnalysisReport report)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("=== UI切换逻辑性能分析报告 ===");
            sb.AppendLine();
            sb.AppendLine($"生成时间: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            
            // 系统信息
            sb.AppendLine("=== 系统信息 ===");
            sb.AppendLine(report.SystemInfo);
            sb.AppendLine();
            
            // 性能摘要
            sb.AppendLine("=== 性能摘要 ===");
            sb.AppendLine($"总体得分: {report.Summary.OverallScore:F1}/100");
            sb.AppendLine($"性能等级: {report.Summary.PerformanceGrade}");
            sb.AppendLine();
            
            sb.AppendLine("分项得分:");
            foreach (var score in report.Summary.CategoryScores)
            {
                sb.AppendLine($"  {score.Key}: {score.Value:F1}/100");
            }
            sb.AppendLine();
            
            sb.AppendLine("主要优势:");
            foreach (var strength in report.Summary.Strengths)
            {
                sb.AppendLine($"  + {strength}");
            }
            sb.AppendLine();
            
            sb.AppendLine("需要改进:");
            foreach (var weakness in report.Summary.Weaknesses)
            {
                sb.AppendLine($"  - {weakness}");
            }
            sb.AppendLine();
            
            // 基准测试结果
            sb.AppendLine("=== 基准测试结果 ===");
            foreach (var result in report.BenchmarkResults)
            {
                sb.AppendLine($"测试名称: {result.TestName}");
                sb.AppendLine($"  测试时间: {result.StartTime:HH:mm:ss} - {result.EndTime:HH:mm:ss}");
                sb.AppendLine($"  持续时间: {result.Duration.TotalSeconds:F2}秒");
                sb.AppendLine($"  平均帧率: {result.AverageFrameRate:F1}fps");
                sb.AppendLine($"  帧率范围: {result.MinFrameRate:F1} - {result.MaxFrameRate:F1}fps");
                sb.AppendLine($"  内存使用: {result.StartMemory / (1024 * 1024):F1}MB -> {result.EndMemory / (1024 * 1024):F1}MB");
                sb.AppendLine($"  峰值内存: {result.PeakMemory / (1024 * 1024):F1}MB");
                sb.AppendLine($"  GC次数: {result.GcCollections}");
                sb.AppendLine();
            }
            
            // 优化建议
            sb.AppendLine("=== 优化建议 ===");
            foreach (var recommendation in report.Recommendations)
            {
                sb.AppendLine($"[{recommendation.Category}] 优先级: {recommendation.Priority}/5");
                sb.AppendLine($"  问题: {recommendation.Issue}");
                sb.AppendLine($"  建议: {recommendation.Recommendation}");
                sb.AppendLine($"  预计影响: {recommendation.EstimatedImpact * 100:F0}%");
                sb.AppendLine();
            }
            
            // 性能对比
            if (report.Comparison != null)
            {
                sb.AppendLine("=== 性能对比 ===");
                sb.AppendLine($"帧率改进: {report.Comparison.FrameRateImprovement:F1}fps");
                sb.AppendLine($"内存减少: {report.Comparison.MemoryReduction / (1024 * 1024):F1}MB");
                sb.AppendLine($"切换时间改进: {report.Comparison.SwitchTimeImprovement:F2}秒");
                sb.AppendLine();
            }
            
            sb.AppendLine("=== 报告结束 ===");
            
            return sb.ToString();
        }
        
        #endregion
    }
}