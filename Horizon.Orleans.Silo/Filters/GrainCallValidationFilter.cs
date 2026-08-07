using System;
using System.Collections;
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

                        // 检查集合参数大小（防止恶意超大集合导致内存压力）
                        if (arg is ICollection collection && collection.Count > MaxCollectionSize)
                        {
                            _logger.LogWarning(
                                "Grain集合参数过大: {GrainType}.{MethodName} 参数 {ParamName} 元素数量 {Count} 超过限制 {MaxCount}",
                                grainType, methodName, param.Name, collection.Count, MaxCollectionSize);

                            throw new ArgumentException(
                                $"参数 {param.Name} 集合元素数量超过允许的最大值 {MaxCollectionSize}",
                                param.Name);
                        }

                        // 检查数值参数是否为负数（ID、数量等不应为负）
                        if (arg is int intArg && intArg < 0 && IsNonNegativeParameter(param.Name))
                        {
                            _logger.LogWarning(
                                "Grain数值参数为负: {GrainType}.{MethodName} 参数 {ParamName} 值 {Value}",
                                grainType, methodName, param.Name, intArg);

                            throw new ArgumentOutOfRangeException(
                                param.Name,
                                intArg,
                                $"参数 {param.Name} 不允许为负数");
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
        /// 集合参数最大允许元素数量。
        /// AOI 订阅（SubscribeSessionAsync）在 radius=28 时产生 57³=185193 个 chunk，
        /// 因此上限设为 200000 以容纳合法 AOI 订阅，同时仍能阻止恶意/错误的超大集合。
        /// </summary>
        private const int MaxCollectionSize = 200000;

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

        /// <summary>
        /// 判断参数名称是否暗示非负数值（ID、数量、页码等）
        /// </summary>
        private static bool IsNonNegativeParameter(string? paramName) =>
            paramName != null && (
                paramName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("count", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("quantity", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("amount", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("page", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("pageSize", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("level", StringComparison.OrdinalIgnoreCase));
    }
}
