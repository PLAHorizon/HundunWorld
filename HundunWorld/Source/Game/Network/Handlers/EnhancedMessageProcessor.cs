using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network.Handlers
{
    /// <summary>
    /// 增强型消息处理器管理器
    /// 集成安全验证、负载均衡和智能路由功能
    /// </summary>
    public class EnhancedMessageProcessor
    {
        private static EnhancedMessageProcessor _instance;
        public static EnhancedMessageProcessor Instance => _instance ??= new EnhancedMessageProcessor();

        private readonly Dictionary<MessageType, List<IMessageHandler>> _handlers;
        private readonly MessageSecurityManager _securityManager;
        private readonly LoadBalancer _loadBalancer;
        private readonly MessageStatistics _statistics;
        private readonly List<MessageInterceptor> _interceptors;

        public EnhancedMessageProcessor()
        {
            _handlers = new Dictionary<MessageType, List<IMessageHandler>>();
            _securityManager = MessageSecurityManager.Instance;
            _loadBalancer = new LoadBalancer();
            _statistics = new MessageStatistics();
            _interceptors = new List<MessageInterceptor>();

            InitializeDefaultInterceptors();
        }

        /// <summary>
        /// 初始化默认拦截器
        /// </summary>
        private void InitializeDefaultInterceptors()
        {
            // 日志拦截器
            _interceptors.Add(new LoggingInterceptor());

            // 性能监控拦截器
            _interceptors.Add(new PerformanceMonitoringInterceptor());

            // 异常处理拦截器
            _interceptors.Add(new ExceptionHandlingInterceptor());
        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        public void RegisterHandler(IMessageHandler handler)
        {
            if (handler?.MessageTypes == null)
                return;

            foreach (var messageType in handler.MessageTypes)
            {
                if (!_handlers.ContainsKey(messageType))
                {
                    _handlers[messageType] = new List<IMessageHandler>();
                }

                _handlers[messageType].Add(handler);
                Debug.Log($"[EnhancedMessageProcessor] 注册处理器: {handler.GetType().Name} 处理 {messageType}");
            }
        }

        /// <summary>
        /// 注册消息拦截器
        /// </summary>
        public void RegisterInterceptor(MessageInterceptor interceptor)
        {
            _interceptors.Add(interceptor);
            Debug.Log($"[EnhancedMessageProcessor] 注册拦截器: {interceptor.GetType().Name}");
        }

        /// <summary>
        /// 处理消息（增强版）
        /// </summary>
        public async Task<ProcessingResult> ProcessMessageAsync(
            HorizonMessagePacket message, 
            string sessionId = null, 
            string clientIp = null)
        {
            var result = new ProcessingResult();

            try
            {
                // 1. 预处理阶段 - 拦截器执行
                foreach (var interceptor in _interceptors)
                {
                    await interceptor.OnPreProcess(message, sessionId, clientIp);
                }

                // 2. 安全验证
                var validationResult = _securityManager.ValidateMessage(message, sessionId, clientIp);
                if (!validationResult.IsValid)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = validationResult.RejectionReason;
                    result.ThreatLevel = validationResult.ThreatLevel;
                    
                    Debug.LogWarning($"[EnhancedMessageProcessor] 消息安全验证失败: {validationResult.RejectionReason}");
                    return result;
                }

                // 3. 负载均衡决策
                var targetHandler = _loadBalancer.SelectHandler(message.Header.MessageType, _handlers);
                if (targetHandler == null)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "未找到可用的消息处理器";
                    Debug.LogWarning($"[EnhancedMessageProcessor] 未找到处理器: {message.Header.MessageType}");
                    return result;
                }

                // 4. 统计信息收集
                _statistics.RecordMessageReceived(message.Header.MessageType);

                // 5. 执行处理器
                var processingTasks = new List<Task>();
                foreach (var handler in targetHandler)
                {
                    if (handler.CanHandle(message.Header.MessageType))
                    {
                        processingTasks.Add(ProcessWithHandler(handler, message, result));
                    }
                }

                // 等待所有处理器完成
                await Task.WhenAll(processingTasks);

                // 6. 后处理阶段 - 拦截器执行
                foreach (var interceptor in _interceptors)
                {
                    await interceptor.OnPostProcess(message, result, sessionId, clientIp);
                }

                result.IsSuccess = true;
                _statistics.RecordMessageProcessed(message.Header.MessageType);
                
                Debug.Log($"[EnhancedMessageProcessor] 消息处理成功: {message.Header.MessageType}");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"消息处理异常: {ex.Message}";
                result.Exception = ex;
                
                Debug.LogError($"[EnhancedMessageProcessor] 消息处理异常: {ex.Message}");
                
                // 异常拦截器处理
                foreach (var interceptor in _interceptors)
                {
                    await interceptor.OnException(message, ex, sessionId, clientIp);
                }
            }

            return result;
        }

        /// <summary>
        /// 使用特定处理器处理消息
        /// </summary>
        private async Task ProcessWithHandler(IMessageHandler handler, HorizonMessagePacket message, ProcessingResult result)
        {
            try
            {
                // 验证消息格式
                if (!handler.ValidateMessage(message))
                {
                    result.ValidationErrors.Add($"处理器 {handler.GetType().Name} 验证失败");
                    return;
                }

                // 执行处理
                await handler.HandleAsync(message);
                result.ProcessedHandlers.Add(handler.GetType().Name);
            }
            catch (Exception ex)
            {
                result.HandlerExceptions.Add(handler.GetType().Name, ex);
                Debug.LogError($"[EnhancedMessageProcessor] 处理器 {handler.GetType().Name} 执行异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取消息统计信息
        /// </summary>
        public MessageStatistics GetStatistics()
        {
            return _statistics.Clone();
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Cleanup()
        {
            _securityManager.CleanupExpiredTrackers();
            _statistics.Reset();
            Debug.Log("[EnhancedMessageProcessor] 清理完成");
        }

        /// <summary>
        /// 获取当前注册的处理器信息
        /// </summary>
        public Dictionary<string, int> GetHandlerInfo()
        {
            var info = new Dictionary<string, int>();
            foreach (var kvp in _handlers)
            {
                info[kvp.Key.ToString()] = kvp.Value.Count;
            }
            return info;
        }
    }

    /// <summary>
    /// 处理结果
    /// </summary>
    public class ProcessingResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public SecurityThreatLevel ThreatLevel { get; set; } = SecurityThreatLevel.Low;
        public List<string> ProcessedHandlers { get; set; } = new List<string>();
        public List<string> ValidationErrors { get; set; } = new List<string>();
        public Dictionary<string, Exception> HandlerExceptions { get; set; } = new Dictionary<string, Exception>();
        public object ResponseData { get; set; }
    }

    /// <summary>
    /// 负载均衡器
    /// </summary>
    public class LoadBalancer
    {
        private readonly Random _random = new Random();

        /// <summary>
        /// 选择合适的处理器
        /// </summary>
        public List<IMessageHandler> SelectHandler(MessageType messageType, Dictionary<MessageType, List<IMessageHandler>> handlers)
        {
            if (!handlers.ContainsKey(messageType))
                return null;

            var availableHandlers = handlers[messageType];
            if (availableHandlers.Count == 0)
                return null;

            // 简单的随机负载均衡
            // 实际应用中可以实现更复杂的策略（轮询、权重、性能感知等）
            return availableHandlers;
        }
    }

    /// <summary>
    /// 消息统计信息
    /// </summary>
    public class MessageStatistics
    {
        private readonly Dictionary<MessageType, int> _receivedCount = new Dictionary<MessageType, int>();
        private readonly Dictionary<MessageType, int> _processedCount = new Dictionary<MessageType, int>();
        private readonly object _lock = new object();

        public void RecordMessageReceived(MessageType type)
        {
            lock (_lock)
            {
                if (!_receivedCount.ContainsKey(type))
                    _receivedCount[type] = 0;
                _receivedCount[type]++;
            }
        }

        public void RecordMessageProcessed(MessageType type)
        {
            lock (_lock)
            {
                if (!_processedCount.ContainsKey(type))
                    _processedCount[type] = 0;
                _processedCount[type]++;
            }
        }

        public Dictionary<MessageType, int> GetReceivedCount()
        {
            lock (_lock)
            {
                return new Dictionary<MessageType, int>(_receivedCount);
            }
        }

        public Dictionary<MessageType, int> GetProcessedCount()
        {
            lock (_lock)
            {
                return new Dictionary<MessageType, int>(_processedCount);
            }
        }

        public MessageStatistics Clone()
        {
            var clone = new MessageStatistics();
            lock (_lock)
            {
                foreach (var kvp in _receivedCount)
                    clone._receivedCount[kvp.Key] = kvp.Value;
                foreach (var kvp in _processedCount)
                    clone._processedCount[kvp.Key] = kvp.Value;
            }
            return clone;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _receivedCount.Clear();
                _processedCount.Clear();
            }
        }
    }

    /// <summary>
    /// 消息拦截器基类
    /// </summary>
    public abstract class MessageInterceptor
    {
        public virtual async Task OnPreProcess(HorizonMessagePacket message, string sessionId, string clientIp)
        {
            await Task.CompletedTask;
        }

        public virtual async Task OnPostProcess(HorizonMessagePacket message, ProcessingResult result, string sessionId, string clientIp)
        {
            await Task.CompletedTask;
        }

        public virtual async Task OnException(HorizonMessagePacket message, Exception exception, string sessionId, string clientIp)
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// 日志拦截器
    /// </summary>
    public class LoggingInterceptor : MessageInterceptor
    {
        public override async Task OnPreProcess(HorizonMessagePacket message, string sessionId, string clientIp)
        {
            Debug.Log($"[LoggingInterceptor] 开始处理消息: {message.Header.MessageType} (会话: {sessionId}, IP: {clientIp})");
            await base.OnPreProcess(message, sessionId, clientIp);
        }

        public override async Task OnPostProcess(HorizonMessagePacket message, ProcessingResult result, string sessionId, string clientIp)
        {
            Debug.Log($"[LoggingInterceptor] 消息处理完成: {message.Header.MessageType} 结果: {result.IsSuccess}");
            await base.OnPostProcess(message, result, sessionId, clientIp);
        }
    }

    /// <summary>
    /// 性能监控拦截器
    /// </summary>
    public class PerformanceMonitoringInterceptor : MessageInterceptor
    {
        private readonly Dictionary<MessageType, List<long>> _processingTimes = new Dictionary<MessageType, List<long>>();
        private readonly object _lock = new object();

        public override async Task OnPreProcess(HorizonMessagePacket message, string sessionId, string clientIp)
        {
            message.Header.ExtensionData["StartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await base.OnPreProcess(message, sessionId, clientIp);
        }

        public override async Task OnPostProcess(HorizonMessagePacket message, ProcessingResult result, string sessionId, string clientIp)
        {
            if (message.Header.ExtensionData.ContainsKey("StartTime"))
            {
                var startTime = (long)message.Header.ExtensionData["StartTime"];
                var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var processingTime = endTime - startTime;

                lock (_lock)
                {
                    if (!_processingTimes.ContainsKey(message.Header.MessageType))
                        _processingTimes[message.Header.MessageType] = new List<long>();
                    
                    _processingTimes[message.Header.MessageType].Add(processingTime);
                    
                    // 保持最近100条记录
                    if (_processingTimes[message.Header.MessageType].Count > 100)
                        _processingTimes[message.Header.MessageType].RemoveAt(0);
                }

                Debug.Log($"[PerformanceMonitoring] {message.Header.MessageType} 处理耗时: {processingTime}ms");
            }
            await base.OnPostProcess(message, result, sessionId, clientIp);
        }

        public Dictionary<MessageType, double> GetAverageProcessingTimes()
        {
            var averages = new Dictionary<MessageType, double>();
            lock (_lock)
            {
                foreach (var kvp in _processingTimes)
                {
                    if (kvp.Value.Count > 0)
                    {
                        averages[kvp.Key] = kvp.Value.Average();
                    }
                }
            }
            return averages;
        }
    }

    /// <summary>
    /// 异常处理拦截器
    /// </summary>
    public class ExceptionHandlingInterceptor : MessageInterceptor
    {
        public override async Task OnException(HorizonMessagePacket message, Exception exception, string sessionId, string clientIp)
        {
            Debug.LogError($"[ExceptionHandling] 消息处理异常: {message.Header.MessageType}");
            Debug.LogError($"异常详情: {exception.Message}");
            Debug.LogError($"堆栈跟踪: {exception.StackTrace}");
            
            // 可以在这里添加异常上报、告警等逻辑
            
            await base.OnException(message, exception, sessionId, clientIp);
        }
    }
}