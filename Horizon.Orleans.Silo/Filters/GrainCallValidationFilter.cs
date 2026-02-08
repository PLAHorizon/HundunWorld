using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Orleans.Silo.Filters
{
    /// <summary>
    /// Grain调用参数验证过滤器
    /// 在Grain方法执行前进行通用参数验证，防止空引用和无效参数传入Grain
    /// </summary>
    public class GrainCallValidationFilter : IIncomingGrainCallFilter
    {
        private readonly ILogger<GrainCallValidationFilter> _logger;

        public GrainCallValidationFilter(ILogger<GrainCallValidationFilter> logger)
        {
            _logger = logger;
        }

        public async Task Invoke(IIncomingGrainCallContext context)
        {
            var grainType = context.Grain?.GetType().Name ?? "Unknown";
            var methodName = context.ImplementationMethod?.Name ?? "Unknown";

            // 验证方法参数
            if (context.Request != null)
            {
                var parameters = context.ImplementationMethod?.GetParameters();
                if (parameters != null)
                {
                    for (int i = 0; i < parameters.Length && i < context.Request.GetArgumentCount(); i++)
                    {
                        var param = parameters[i];
                        var arg = context.Request.GetArgument(i);

                        // 检查string参数是否超长（防止恶意超长输入）
                        if (arg is string strArg && strArg.Length > MaxStringArgumentLength)
                        {
                            _logger.LogWarning(
                                "Grain参数过长: {GrainType}.{MethodName} 参数 {ParamName} 长度 {Length} 超过限制 {MaxLength}",
                                grainType, methodName, param.Name, strArg.Length, MaxStringArgumentLength);

                            throw new ArgumentException(
                                $"参数 {param.Name} 长度超过允许的最大值 {MaxStringArgumentLength}",
                                param.Name);
                        }

                        // 检查Guid参数是否为Empty
                        if (arg is Guid guidArg && guidArg == Guid.Empty && !IsGuidEmptyAllowed(methodName, param.Name))
                        {
                            _logger.LogWarning(
                                "Grain Guid参数为空: {GrainType}.{MethodName} 参数 {ParamName}",
                                grainType, methodName, param.Name);

                            throw new ArgumentException(
                                $"参数 {param.Name} 不能为空GUID",
                                param.Name);
                        }

                        // 检查集合参数是否为null
                        if (arg == null && !param.HasDefaultValue && IsReferenceType(param.ParameterType))
                        {
                            // 只记录警告，不阻止调用（某些Grain方法允许null参数）
                            _logger.LogDebug(
                                "Grain参数为null: {GrainType}.{MethodName} 参数 {ParamName}",
                                grainType, methodName, param.Name);
                        }
                    }
                }
            }

            await context.Invoke();
        }

        /// <summary>
        /// 字符串参数最大允许长度
        /// </summary>
        private const int MaxStringArgumentLength = 10000;

        /// <summary>
        /// 判断参数类型是否为引用类型
        /// </summary>
        private static bool IsReferenceType(Type type) =>
            !type.IsValueType && type != typeof(string);

        /// <summary>
        /// 判断特定方法的Guid参数是否允许为Empty
        /// 某些初始化方法可能需要传入Guid.Empty
        /// </summary>
        private static bool IsGuidEmptyAllowed(string methodName, string? paramName) =>
            methodName.Contains("Initialize", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("Reset", StringComparison.OrdinalIgnoreCase);
    }
}
