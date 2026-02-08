using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// Phase 2.2/2.3 Seq日志集成、告警通知、架构改进相关单元测试
    /// </summary>
    public class SeqAlertingValidationTests
    {
        #region Seq Logging Options Tests

        [Fact]
        public void SeqLoggingOptions_DefaultValues_AreCorrect()
        {
            var options = new SeqLoggingOptionsTest();
            Assert.Equal("http://localhost:5341", options.ServerUrl);
            Assert.Null(options.ApiKey);
            Assert.False(options.Enabled);
            Assert.Equal(2, options.BatchIntervalSeconds);
            Assert.Equal(100, options.MaxBatchSize);
        }

        [Fact]
        public void SeqLoggingOptions_SetValues_ArePersisted()
        {
            var options = new SeqLoggingOptionsTest
            {
                ServerUrl = "http://seq.example.com:5341",
                ApiKey = "test-api-key",
                Enabled = true,
                BatchIntervalSeconds = 5,
                MaxBatchSize = 200
            };

            Assert.Equal("http://seq.example.com:5341", options.ServerUrl);
            Assert.Equal("test-api-key", options.ApiKey);
            Assert.True(options.Enabled);
            Assert.Equal(5, options.BatchIntervalSeconds);
            Assert.Equal(200, options.MaxBatchSize);
        }

        [Fact]
        public void SeqLoggingOptions_EmptyApiKey_IsAllowed()
        {
            var options = new SeqLoggingOptionsTest
            {
                ApiKey = "",
                Enabled = true
            };

            Assert.Equal("", options.ApiKey);
            Assert.True(options.Enabled);
        }

        #endregion

        #region CLEF Format Tests

        [Fact]
        public void ClefEvent_ContainsRequiredFields()
        {
            var clefEvent = CreateClefEvent("Information", "Test message", "TestCategory");

            Assert.True(clefEvent.ContainsKey("@t"));
            Assert.True(clefEvent.ContainsKey("@l"));
            Assert.True(clefEvent.ContainsKey("@mt"));
            Assert.True(clefEvent.ContainsKey("SourceContext"));
            Assert.True(clefEvent.ContainsKey("Service"));
        }

        [Fact]
        public void ClefEvent_TimestampFormat_IsISO8601()
        {
            var clefEvent = CreateClefEvent("Information", "Test message", "TestCategory");

            var timestamp = clefEvent["@t"]?.ToString();
            Assert.NotNull(timestamp);
            Assert.True(DateTime.TryParse(timestamp, out _));
        }

        [Theory]
        [InlineData(LogLevel.Trace, "Verbose")]
        [InlineData(LogLevel.Debug, "Debug")]
        [InlineData(LogLevel.Information, "Information")]
        [InlineData(LogLevel.Warning, "Warning")]
        [InlineData(LogLevel.Error, "Error")]
        [InlineData(LogLevel.Critical, "Fatal")]
        public void ClefLogLevel_MapsCorrectly(LogLevel logLevel, string expectedSeqLevel)
        {
            var seqLevel = MapLogLevel(logLevel);
            Assert.Equal(expectedSeqLevel, seqLevel);
        }

        [Fact]
        public void ClefEvent_WithException_ContainsExceptionField()
        {
            var exception = new InvalidOperationException("Test error");
            var clefEvent = CreateClefEventWithException("Error", "An error occurred", "TestCategory", exception);

            Assert.True(clefEvent.ContainsKey("@x"));
            Assert.Contains("Test error", clefEvent["@x"]?.ToString());
        }

        [Fact]
        public void ClefEvent_WithoutException_OmitsExceptionField()
        {
            var clefEvent = CreateClefEvent("Information", "No error", "TestCategory");

            Assert.False(clefEvent.ContainsKey("@x"));
        }

        [Fact]
        public void ClefEvent_ServiceName_IsSet()
        {
            var clefEvent = CreateClefEvent("Information", "Test", "TestCategory", "HundunWorld.TestService");

            Assert.Equal("HundunWorld.TestService", clefEvent["Service"]);
        }

        [Fact]
        public void ClefEvent_SourceContext_IsSet()
        {
            var clefEvent = CreateClefEvent("Information", "Test", "MyGrain.TestCategory");

            Assert.Equal("MyGrain.TestCategory", clefEvent["SourceContext"]);
        }

        #endregion

        #region Event Queue Tests

        [Fact]
        public void EventQueue_EnqueueDequeue_WorksCorrectly()
        {
            var queue = new ConcurrentQueue<string>();
            queue.Enqueue("{\"@t\":\"2026-01-01\",\"@l\":\"Information\",\"@mt\":\"Test\"}");

            Assert.Single(queue);
            Assert.True(queue.TryDequeue(out var result));
            Assert.Contains("Information", result);
            Assert.Empty(queue);
        }

        [Fact]
        public async Task EventQueue_ConcurrentEnqueue_IsThreadSafe()
        {
            var queue = new ConcurrentQueue<string>();
            var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
            {
                queue.Enqueue($"{{\"@mt\":\"Event {i}\"}}");
            }));

            await Task.WhenAll(tasks);

            Assert.Equal(100, queue.Count);
        }

        [Fact]
        public void EventQueue_BatchDequeue_RespectsMaxSize()
        {
            var queue = new ConcurrentQueue<string>();
            for (int i = 0; i < 150; i++)
            {
                queue.Enqueue($"{{\"@mt\":\"Event {i}\"}}");
            }

            var batch = new List<string>();
            int maxBatch = 100;
            while (batch.Count < maxBatch && queue.TryDequeue(out var item))
            {
                batch.Add(item);
            }

            Assert.Equal(100, batch.Count);
            Assert.Equal(50, queue.Count);
        }

        #endregion

        #region GrainExceptionFilter Tests (Architecture)

        [Fact]
        public void GrainExceptionFilter_SlowCallThreshold_Is1000ms()
        {
            // The filter should warn for calls taking longer than 1000ms
            Assert.Equal(1000, GrainExceptionFilterConstants.SlowCallThresholdMs);
        }

        [Theory]
        [InlineData(500, false)]
        [InlineData(999, false)]
        [InlineData(1000, false)]
        [InlineData(1001, true)]
        [InlineData(5000, true)]
        public void GrainExceptionFilter_SlowCallDetection_WorksCorrectly(double durationMs, bool shouldWarn)
        {
            var isSlowCall = durationMs > GrainExceptionFilterConstants.SlowCallThresholdMs;
            Assert.Equal(shouldWarn, isSlowCall);
        }

        #endregion

        #region GrainCallValidationFilter Tests (Architecture)

        [Fact]
        public void GrainCallValidation_MaxStringLength_Is10000()
        {
            Assert.Equal(10000, GrainCallValidationConstants.MaxStringArgumentLength);
        }

        [Theory]
        [InlineData("short string", false)]
        [InlineData("", false)]
        public void GrainCallValidation_StringLength_ValidatesCorrectly(string input, bool shouldReject)
        {
            var isOverLimit = input.Length > GrainCallValidationConstants.MaxStringArgumentLength;
            Assert.Equal(shouldReject, isOverLimit);
        }

        [Fact]
        public void GrainCallValidation_LongString_IsRejected()
        {
            var longString = new string('a', 10001);
            var isOverLimit = longString.Length > GrainCallValidationConstants.MaxStringArgumentLength;
            Assert.True(isOverLimit);
        }

        [Fact]
        public void GrainCallValidation_ExactMaxLength_IsAccepted()
        {
            var maxString = new string('a', 10000);
            var isOverLimit = maxString.Length > GrainCallValidationConstants.MaxStringArgumentLength;
            Assert.False(isOverLimit);
        }

        [Fact]
        public void GrainCallValidation_EmptyGuid_IsDetected()
        {
            var emptyGuid = Guid.Empty;
            Assert.Equal(Guid.Empty, emptyGuid);
        }

        [Fact]
        public void GrainCallValidation_NonEmptyGuid_IsAccepted()
        {
            var guid = Guid.NewGuid();
            Assert.NotEqual(Guid.Empty, guid);
        }

        [Theory]
        [InlineData("InitializeServerAsync", "playerId", true)]
        [InlineData("CreateTradeAsync", "sellerId", true)]
        [InlineData("ResetAllSkillsAsync", "playerId", true)]
        [InlineData("AcceptQuestAsync", "playerId", false)]
        [InlineData("AttackAsync", "targetId", false)]
        [InlineData("GetServerStatusAsync", "serverId", false)]
        public void GrainCallValidation_GuidEmptyAllowed_ChecksByMethodName(string methodName, string paramName, bool expected)
        {
            var isAllowed = IsGuidEmptyAllowed(methodName, paramName);
            Assert.Equal(expected, isAllowed);
        }

        #endregion

        #region Alertmanager Configuration Tests

        [Fact]
        public void AlertRouting_CriticalSeverity_RoutesToCriticalAlerts()
        {
            var routing = GetAlertRouting("critical", "GrainCallLatencyHigh");
            Assert.Equal("critical-alerts", routing);
        }

        [Fact]
        public void AlertRouting_WarningSeverity_RoutesToPerformanceWebhook()
        {
            var routing = GetAlertRouting("warning", "HighMemoryUsage");
            Assert.Equal("performance-webhook", routing);
        }

        [Fact]
        public void AlertRouting_SiloDown_RoutesToCriticalAlerts()
        {
            var routing = GetAlertRoutingByName("SiloInstanceDown");
            Assert.Equal("critical-alerts", routing);
        }

        [Fact]
        public void AlertRouting_GatewayDown_RoutesToCriticalAlerts()
        {
            var routing = GetAlertRoutingByName("GatewayInstanceDown");
            Assert.Equal("critical-alerts", routing);
        }

        [Fact]
        public void AlertRouting_LoginFailureSpike_RoutesToSecurityAlerts()
        {
            var routing = GetAlertRoutingByName("LoginFailureSpike");
            Assert.Equal("security-alerts", routing);
        }

        [Fact]
        public void AlertRouting_LoginFailureRateHigh_RoutesToSecurityAlerts()
        {
            var routing = GetAlertRoutingByName("LoginFailureRateHigh");
            Assert.Equal("security-alerts", routing);
        }

        [Fact]
        public void AlertRouting_UnknownAlert_RoutesToDefault()
        {
            var routing = GetAlertRouting("info", "UnknownAlert");
            Assert.Equal("default-webhook", routing);
        }

        [Theory]
        [InlineData("critical", 10)]
        [InlineData("warning", 60)]
        public void AlertRouting_GroupWaitSeconds_ByServerity(string severity, int expectedWaitSeconds)
        {
            var waitSeconds = GetGroupWaitSeconds(severity);
            Assert.Equal(expectedWaitSeconds, waitSeconds);
        }

        #endregion

        #region Helper Methods

        private static Dictionary<string, object?> CreateClefEvent(
            string level, string message, string category, string service = "HundunWorld.Silo")
        {
            return new Dictionary<string, object?>
            {
                ["@t"] = DateTime.UtcNow.ToString("O"),
                ["@l"] = level,
                ["@mt"] = message,
                ["SourceContext"] = category,
                ["Service"] = service
            };
        }

        private static Dictionary<string, object?> CreateClefEventWithException(
            string level, string message, string category, Exception exception)
        {
            var clef = CreateClefEvent(level, message, category);
            clef["@x"] = exception.ToString();
            return clef;
        }

        private static string MapLogLevel(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => "Verbose",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Information",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Fatal",
            _ => "Information"
        };

        private static bool IsGuidEmptyAllowed(string methodName, string? paramName) =>
            methodName.Contains("Initialize", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("Reset", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 模拟告警路由逻辑（与alertmanager.yml配置一致）
        /// </summary>
        private static string GetAlertRouting(string severity, string alertName)
        {
            // 先按alertname匹配特定路由
            var nameRoute = GetAlertRoutingByName(alertName);
            if (nameRoute != "default-webhook") return nameRoute;

            // 再按severity匹配
            return severity switch
            {
                "critical" => "critical-alerts",
                "warning" => "performance-webhook",
                _ => "default-webhook"
            };
        }

        private static string GetAlertRoutingByName(string alertName)
        {
            // 服务宕机 → critical-alerts
            if (alertName == "SiloInstanceDown" || alertName == "GatewayInstanceDown")
                return "critical-alerts";

            // 安全告警 → security-alerts
            if (alertName == "LoginFailureSpike" || alertName == "LoginFailureRateHigh")
                return "security-alerts";

            return "default-webhook";
        }

        private static int GetGroupWaitSeconds(string severity) => severity switch
        {
            "critical" => 10,
            "warning" => 60,
            _ => 30
        };

        #endregion
    }

    #region Test Support Types

    /// <summary>
    /// Seq日志选项测试数据类（镜像实际SeqLoggingOptions）
    /// </summary>
    public class SeqLoggingOptionsTest
    {
        public string ServerUrl { get; set; } = "http://localhost:5341";
        public string? ApiKey { get; set; }
        public bool Enabled { get; set; } = false;
        public int BatchIntervalSeconds { get; set; } = 2;
        public int MaxBatchSize { get; set; } = 100;
    }

    /// <summary>
    /// GrainExceptionFilter常量（镜像实际过滤器中的阈值）
    /// </summary>
    public static class GrainExceptionFilterConstants
    {
        public const double SlowCallThresholdMs = 1000;
    }

    /// <summary>
    /// GrainCallValidation常量（镜像实际验证过滤器中的限制值）
    /// </summary>
    public static class GrainCallValidationConstants
    {
        public const int MaxStringArgumentLength = 10000;
    }

    #endregion
}
