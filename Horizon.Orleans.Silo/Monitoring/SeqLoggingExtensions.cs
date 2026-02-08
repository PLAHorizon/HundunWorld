using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Silo.Monitoring
{
    /// <summary>
    /// Seq日志集成扩展方法
    /// 在开发环境中启用Seq社区版日志聚合
    /// </summary>
    public static class SeqLoggingExtensions
    {
        /// <summary>
        /// 添加Seq日志提供程序（如果配置启用）
        /// </summary>
        /// <param name="logging">日志构建器</param>
        /// <param name="configuration">应用配置</param>
        /// <param name="serviceName">服务名称</param>
        /// <returns>日志构建器</returns>
        public static ILoggingBuilder AddSeqIfEnabled(
            this ILoggingBuilder logging,
            IConfiguration configuration,
            string serviceName = "HundunWorld.Silo")
        {
            var options = new SeqLoggingOptions();
            configuration.GetSection("Seq").Bind(options);

            if (options.Enabled && !string.IsNullOrEmpty(options.ServerUrl))
            {
                logging.AddProvider(new SeqLoggerProvider(options, serviceName));
            }

            return logging;
        }

        /// <summary>
        /// 添加Seq日志提供程序（通过显式选项）
        /// </summary>
        /// <param name="logging">日志构建器</param>
        /// <param name="options">Seq配置选项</param>
        /// <param name="serviceName">服务名称</param>
        /// <returns>日志构建器</returns>
        public static ILoggingBuilder AddSeq(
            this ILoggingBuilder logging,
            SeqLoggingOptions options,
            string serviceName = "HundunWorld.Silo")
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            logging.AddProvider(new SeqLoggerProvider(options, serviceName));
            return logging;
        }
    }
}
