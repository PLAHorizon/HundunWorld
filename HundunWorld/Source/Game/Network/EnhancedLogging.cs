using FlaxEngine;
using System;
using System.IO;
using System.Text;

namespace Game.Game.Network
{
    /// <summary>
    /// 增强日志系统，确保日志完整输出
    /// </summary>
    public static class EnhancedLogging
    {
        private static readonly object _lock = new object();
        private static StreamWriter _fileWriter;
        private static bool _initialized = false;
        
        /// <summary>
        /// 初始化增强日志系统
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            
            try
            {
                // 创建日志目录
                string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HundunWorld", "Logs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                
                // 创建日志文件
                string logFilePath = Path.Combine(logDirectory, $"network_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                _fileWriter = new StreamWriter(logFilePath, true, Encoding.UTF8);
                _fileWriter.AutoFlush = true;
                
                _initialized = true;
                LogInfo("增强日志系统初始化完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ENHANCED_LOG] 初始化增强日志系统时发生错误: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 记录信息日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogInfo(string message)
        {
            string logMessage = $"[INFO] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            WriteLog(logMessage);
        }
        
        /// <summary>
        /// 记录警告日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogWarning(string message)
        {
            string logMessage = $"[WARN] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            WriteLog(logMessage);
        }
        
        /// <summary>
        /// 记录错误日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void LogError(string message)
        {
            string logMessage = $"[ERROR] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            WriteLog(logMessage);
        }
        
        /// <summary>
        /// 写入日志到文件和控制台
        /// </summary>
        /// <param name="message">日志消息</param>
        private static void WriteLog(string message)
        {
            lock (_lock)
            {
                try
                {
                    // 输出到控制台
                    if (message.Contains("[ERROR]"))
                    {
                        Debug.LogError($"[ENHANCED_LOG] {message}");
                    }
                    else if (message.Contains("[WARN]"))
                    {
                        Debug.LogWarning($"[ENHANCED_LOG] {message}");
                    }
                    else
                    {
                        Debug.Log($"[ENHANCED_LOG] {message}");
                    }
                    
                    // 输出到文件
                    if (_initialized && _fileWriter != null)
                    {
                        _fileWriter.WriteLine(message);
                    }
                }
                catch (Exception ex)
                {
                    // 如果日志系统出错，至少在控制台输出错误信息
                    Debug.LogError($"[ENHANCED_LOG] 写入日志时发生错误: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 关闭日志系统
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized) return;
            
            try
            {
                LogInfo("关闭增强日志系统");
                _fileWriter?.Close();
                _fileWriter?.Dispose();
                _fileWriter = null;
                _initialized = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ENHANCED_LOG] 关闭增强日志系统时发生错误: {ex.Message}");
            }
        }
    }
}