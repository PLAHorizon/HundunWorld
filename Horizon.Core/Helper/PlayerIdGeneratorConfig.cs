using System;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// 玩家ID生成器配置
    /// </summary>
    public class PlayerIdGeneratorConfig
    {
        /// <summary>
        /// 机器ID（0-31）
        /// </summary>
        public long WorkerId { get; set; }
        
        /// <summary>
        /// 数据中心ID（0-31）
        /// </summary>
        public long DataCenterId { get; set; }
        
        /// <summary>
        /// 是否启用缓存
        /// </summary>
        public bool EnableCache { get; set; } = true;
        
        /// <summary>
        /// 缓存过期时间（小时）
        /// </summary>
        public int CacheExpireHours { get; set; } = 24;
        
        /// <summary>
        /// 缓存清理间隔（小时）
        /// </summary>
        public int CacheCleanupIntervalHours { get; set; } = 6;
        
        /// <summary>
        /// 自定义开始时间戳（毫秒）
        /// </summary>
        public long? CustomStartTimestamp { get; set; }
        
        /// <summary>
        /// 环境名称
        /// </summary>
        public string Environment { get; set; } = "Production";
        
        /// <summary>
        /// 是否启用ID验证
        /// </summary>
        public bool EnableValidation { get; set; } = true;
        
        /// <summary>
        /// 验证配置
        /// </summary>
        public ValidationConfig Validation { get; set; } = new ValidationConfig();
    }
    
    /// <summary>
    /// 验证配置
    /// </summary>
    public class ValidationConfig
    {
        /// <summary>
        /// 最小有效时间（距离开始时间戳的最小间隔，防止时间倒退）
        /// </summary>
        public TimeSpan MinValidTimespan { get; set; } = TimeSpan.Zero;
        
        /// <summary>
        /// 最大有效时间（距离开始时间戳的最大间隔，防止未来时间）
        /// </summary>
        public TimeSpan MaxValidTimespan { get; set; } = TimeSpan.FromDays(365 * 10); // 10年
        
        /// <summary>
        /// 系统保留ID上限
        /// </summary>
        public long SystemReservedIdLimit { get; set; } = 10000L;
        
        /// <summary>
        /// 是否允许重复检查
        /// </summary>
        public bool EnableDuplicateCheck { get; set; } = true;
    }
    
    /// <summary>
    /// 配置ID生成器的工厂类
    /// </summary>
    public static class PlayerIdGeneratorFactory
    {
        /// <summary>
        /// 根据配置创建ID生成器
        /// </summary>
        /// <param name="config">配置</param>
        /// <returns>ID生成器</returns>
        public static PlayerIdGenerator CreateGenerator(PlayerIdGeneratorConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
                
            return new PlayerIdGenerator(config.WorkerId, config.DataCenterId);
        }
        
        /// <summary>
        /// 创建开发环境配置
        /// </summary>
        /// <returns>开发环境配置</returns>
        public static PlayerIdGeneratorConfig CreateDevelopmentConfig()
        {
            return new PlayerIdGeneratorConfig
            {
                WorkerId = 1,
                DataCenterId = 1,
                Environment = "Development",
                EnableCache = false, // 开发环境不启用缓存
                EnableValidation = false, // 开发环境关闭严格验证
                Validation = new ValidationConfig
                {
                    EnableDuplicateCheck = false,
                    SystemReservedIdLimit = 1000L
                }
            };
        }
        
        /// <summary>
        /// 创建测试环境配置
        /// </summary>
        /// <returns>测试环境配置</returns>
        public static PlayerIdGeneratorConfig CreateTestingConfig()
        {
            return new PlayerIdGeneratorConfig
            {
                WorkerId = 2,
                DataCenterId = 2,
                Environment = "Testing",
                EnableCache = true,
                CacheExpireHours = 1, // 测试环境快速过期
                CacheCleanupIntervalHours = 1,
                EnableValidation = true,
                Validation = new ValidationConfig
                {
                    EnableDuplicateCheck = true,
                    SystemReservedIdLimit = 5000L
                }
            };
        }
        
        /// <summary>
        /// 创建生产环境配置
        /// </summary>
        /// <param name="workerId">机器ID</param>
        /// <param name="dataCenterId">数据中心ID</param>
        /// <returns>生产环境配置</returns>
        public static PlayerIdGeneratorConfig CreateProductionConfig(long workerId, long dataCenterId)
        {
            return new PlayerIdGeneratorConfig
            {
                WorkerId = workerId,
                DataCenterId = dataCenterId,
                Environment = "Production",
                EnableCache = true,
                CacheExpireHours = 24,
                CacheCleanupIntervalHours = 6,
                EnableValidation = true,
                Validation = new ValidationConfig
                {
                    EnableDuplicateCheck = true,
                    SystemReservedIdLimit = 10000L
                }
            };
        }
        
        /// <summary>
        /// 根据环境名称创建配置
        /// </summary>
        /// <param name="environment">环境名称</param>
        /// <param name="workerId">机器ID（生产环境必需）</param>
        /// <param name="dataCenterId">数据中心ID（生产环境必需）</param>
        /// <returns>配置</returns>
        public static PlayerIdGeneratorConfig CreateConfigByEnvironment(
            string environment, 
            long? workerId = null, 
            long? dataCenterId = null)
        {
            return environment?.ToLowerInvariant() switch
            {
                "development" or "dev" => CreateDevelopmentConfig(),
                "testing" or "test" => CreateTestingConfig(),
                "production" or "prod" => CreateProductionConfig(
                    workerId ?? throw new ArgumentException("生产环境需要指定WorkerId"), 
                    dataCenterId ?? throw new ArgumentException("生产环境需要指定DataCenterId")),
                _ => throw new ArgumentException($"不支持的环境名称: {environment}")
            };
        }
    }
    
    /// <summary>
    /// 性能统计信息
    /// </summary>
    public class PerformanceStats
    {
        /// <summary>
        /// 总生成次数
        /// </summary>
        public long TotalGenerated { get; set; }
        
        /// <summary>
        /// 每秒生成数
        /// </summary>
        public double GenerationsPerSecond { get; set; }
        
        /// <summary>
        /// 平均生成时间（毫秒）
        /// </summary>
        public double AverageGenerationTime { get; set; }
        
        /// <summary>
        /// 最大生成时间（毫秒）
        /// </summary>
        public double MaxGenerationTime { get; set; }
        
        /// <summary>
        /// 最小生成时间（毫秒）
        /// </summary>
        public double MinGenerationTime { get; set; }
        
        /// <summary>
        /// 统计开始时间
        /// </summary>
        public DateTime StatsStartTime { get; set; }
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime { get; set; }
        
        public override string ToString()
        {
            return $"总计: {TotalGenerated}, 每秒: {GenerationsPerSecond:F2}, " +
                   $"平均: {AverageGenerationTime:F2}ms, 最大: {MaxGenerationTime:F2}ms, " +
                   $"最小: {MinGenerationTime:F2}ms";
        }
    }
}
