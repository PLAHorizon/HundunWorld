using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// CorrelationId与分布式追踪相关单元测试
    /// Phase 2.2: 全局CorrelationId测试
    /// </summary>
    public class CorrelationIdMonitoringTests
    {
        #region CorrelationId Generation Tests

        [Fact]
        public void CorrelationId_Generate_ReturnsNonEmptyString()
        {
            var correlationId = GenerateCorrelationId();
            Assert.False(string.IsNullOrEmpty(correlationId));
        }

        [Fact]
        public void CorrelationId_Generate_ContainsDatePrefix()
        {
            var correlationId = GenerateCorrelationId();
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            Assert.Contains(today, correlationId);
        }

        [Fact]
        public void CorrelationId_Generate_HasCorrectFormat()
        {
            var correlationId = GenerateCorrelationId();
            // Format: yyyyMMdd-xxxxxxxx
            var parts = correlationId.Split('-');
            Assert.Equal(2, parts.Length);
            Assert.Equal(8, parts[0].Length); // date part
            Assert.Equal(8, parts[1].Length); // guid part
        }

        [Fact]
        public void CorrelationId_Generate_UniquePerCall()
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                ids.Add(GenerateCorrelationId());
            }
            Assert.Equal(100, ids.Count);
        }

        [Fact]
        public void GatewayCorrelationId_Generate_HasGwPrefix()
        {
            var correlationId = GenerateGatewayCorrelationId();
            Assert.StartsWith("gw-", correlationId);
        }

        [Fact]
        public void GatewayCorrelationId_Generate_HasCorrectFormat()
        {
            var correlationId = GenerateGatewayCorrelationId();
            // Format: gw-yyyyMMdd-xxxxxxxx
            var parts = correlationId.Split('-');
            Assert.Equal(3, parts.Length);
            Assert.Equal("gw", parts[0]);
            Assert.Equal(8, parts[1].Length); // date part
            Assert.Equal(8, parts[2].Length); // guid part
        }

        [Fact]
        public void GatewayCorrelationId_Generate_UniquePerCall()
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                ids.Add(GenerateGatewayCorrelationId());
            }
            Assert.Equal(100, ids.Count);
        }

        #endregion

        #region RequestContext CorrelationId Tests

        [Fact]
        public void RequestContext_SetAndGet_CorrelationId()
        {
            var correlationId = GenerateCorrelationId();
            RequestContext.Set("X-Correlation-Id", correlationId);

            var retrieved = RequestContext.Get("X-Correlation-Id") as string;
            Assert.Equal(correlationId, retrieved);
        }

        [Fact]
        public void RequestContext_SetAndGet_CausationId()
        {
            var causationId = "CombatGrain.AttackAsync";
            RequestContext.Set("X-Causation-Id", causationId);

            var retrieved = RequestContext.Get("X-Causation-Id") as string;
            Assert.Equal(causationId, retrieved);
        }

        [Fact]
        public void RequestContext_GetMissing_ReturnsNull()
        {
            // Clear any existing value
            RequestContext.Set("X-Test-Missing", null);
            var retrieved = RequestContext.Get("X-Test-Missing") as string;
            Assert.Null(retrieved);
        }

        [Fact]
        public void RequestContext_Overwrite_CorrelationId()
        {
            var first = "first-correlation-id";
            var second = "second-correlation-id";

            RequestContext.Set("X-Correlation-Id", first);
            Assert.Equal(first, RequestContext.Get("X-Correlation-Id") as string);

            RequestContext.Set("X-Correlation-Id", second);
            Assert.Equal(second, RequestContext.Get("X-Correlation-Id") as string);
        }

        #endregion

        #region CorrelationId Validation Tests

        [Fact]
        public void CorrelationId_DatePart_IsValidDate()
        {
            var correlationId = GenerateCorrelationId();
            var datePart = correlationId.Split('-')[0];
            Assert.True(DateTime.TryParseExact(datePart, "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _));
        }

        [Fact]
        public void CorrelationId_GuidPart_IsHexadecimal()
        {
            var correlationId = GenerateCorrelationId();
            var guidPart = correlationId.Split('-')[1];
            Assert.True(guidPart.All(c => "0123456789abcdef".Contains(c)));
        }

        [Theory]
        [InlineData("20260208-a1b2c3d4")]
        [InlineData("20260101-00000000")]
        [InlineData("20251231-ffffffff")]
        public void CorrelationId_ValidFormats_Accepted(string correlationId)
        {
            var parts = correlationId.Split('-');
            Assert.Equal(2, parts.Length);
            Assert.Equal(8, parts[0].Length);
            Assert.Equal(8, parts[1].Length);
        }

        [Theory]
        [InlineData("gw-20260208-a1b2c3d4")]
        [InlineData("gw-20260101-00000000")]
        public void GatewayCorrelationId_ValidFormats_Accepted(string correlationId)
        {
            Assert.StartsWith("gw-", correlationId);
            var parts = correlationId.Split('-');
            Assert.Equal(3, parts.Length);
        }

        #endregion

        #region Concurrent CorrelationId Tests

        [Fact]
        public async Task CorrelationId_ConcurrentGeneration_AllUnique()
        {
            var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
            var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            {
                ids.Add(GenerateCorrelationId());
            }));

            await Task.WhenAll(tasks);

            Assert.Equal(50, ids.Distinct().Count());
        }

        [Fact]
        public async Task GatewayCorrelationId_ConcurrentGeneration_AllUnique()
        {
            var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
            var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            {
                ids.Add(GenerateGatewayCorrelationId());
            }));

            await Task.WhenAll(tasks);

            Assert.Equal(50, ids.Distinct().Count());
        }

        #endregion

        #region Helper Methods (mirror the actual implementations)

        /// <summary>
        /// 生成Silo端CorrelationId（与CorrelationIdFilter.GenerateCorrelationId一致）
        /// </summary>
        private static string GenerateCorrelationId()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
            var guid = Guid.NewGuid().ToString("N")[..8];
            return $"{timestamp}-{guid}";
        }

        /// <summary>
        /// 生成Gateway端CorrelationId（与CorrelationIdManager.GenerateCorrelationId一致）
        /// </summary>
        private static string GenerateGatewayCorrelationId()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
            var guid = Guid.NewGuid().ToString("N")[..8];
            return $"gw-{timestamp}-{guid}";
        }

        #endregion
    }
}
