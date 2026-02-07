using log4net;
using log4net.Config;
using log4net.Repository;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// Log4Net 日志实现
    /// </summary>
    public static class Log
    {

        public static ILoggerRepository CommRepository { get; private set; }
        public static ILoggerRepository ApiRepository { get; private set; }
        public static string CommRepName { get; private set; }
        public static string ApiRepName { get; private set; }
        public static string LogPath { get; set; }
        public static void LogConfig(string repositoryName = "comm", string apirepositoryName = "api")
        {
            if (!string.IsNullOrWhiteSpace(CommRepName)) return;
            CommRepName = repositoryName;
            ApiRepName = apirepositoryName;
            //配置日志，设置日志仓库及日志格式
            ApiRepository = LogManager.CreateRepository(ApiRepName);
            XmlConfigurator.Configure(ApiRepository, new FileInfo($"{LogPath}Configs/ApiLog.config"));

            CommRepository = LogManager.CreateRepository(CommRepName);
            XmlConfigurator.Configure(CommRepository, new FileInfo($"{LogPath}Configs/CommonLog.config"));
        }
        /// <summary>
        /// 调试
        /// </summary>
        /// <param name="message">消息</param>
        public static void Debug(ILoggerRepository repository, object message)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Debug(message);
        }
        /// <summary>
        /// 调试
        /// </summary>
        /// <param name="message">消息</param>
        public static void Debug(string repository, object message)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Debug(message);
        }
        /// <summary>
        /// 调试
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="ex">异常</param>
        public static void Debug(ILoggerRepository repository, object message, Exception ex)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Debug(message, ex);
        }
        /// <summary>
        /// 调试
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="ex">异常</param>
        public static void Debug(string repository, object message, Exception ex)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Debug(message, ex);
        }
        /// <summary>
        /// 一般错误
        /// </summary>
        /// <param name="message">消息</param>
        public static void Error(ILoggerRepository repository, object message)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Error(message);
        }
        /// <summary>
        /// 一般错误
        /// </summary>
        /// <param name="message">消息</param>
        public static void Error(string repository, object message)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Error(message);
        }
        public static void Error(object message)
        {
            Error(CommRepository, message);
        }
        /// <summary>
        /// 一般错误
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="exception">异常</param>
        public static void Error(ILoggerRepository repository, object message, Exception exception)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Error(message, exception);
        }



        /// <summary>
        /// 一般错误
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="exception">异常</param>
        public static void Error(string repository, object message, Exception exception)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Error(message, exception);
        }

        /// <summary>
        /// 致命错误
        /// </summary>
        /// <param name="message">消息</param>
        public static void Fatal(ILoggerRepository repository, object message)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Fatal(message);
        }
        /// <summary>
        /// 致命错误
        /// </summary>
        /// <param name="message">消息</param>
        public static void Fatal(string repository, object message)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Fatal(message);
        }
        /// <summary>
        /// 致命错误
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="exception">异常</param>
        public static void Fatal(ILoggerRepository repository, object message, Exception exception)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Fatal(message, exception);
        }



        /// <summary>
        /// 致命错误
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="exception">异常</param>
        public static void Fatal(string repository, object message, Exception exception)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Fatal(message, exception);
        }


        /// <summary>
        /// 信息
        /// </summary>
        /// <param name="message">消息</param>
        public static void Info(ILoggerRepository repository, object message)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Info(message);
        }
        /// <summary>
        /// 信息
        /// </summary>
        /// <param name="message">消息</param>
        public static void Info(string repository, object message)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Info(message);
        }
        /// <summary>
        /// 信息
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="ex">异常</param>
        public static void Info(ILoggerRepository repository, object message, Exception ex)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Info(message, ex);
        }
        /// <summary>
        /// 信息
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="ex">异常</param>
        public static void Info(string repository, object message, Exception ex)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Info(message, ex);
        }
        /// <summary>
        /// 警告
        /// </summary>
        /// <param name="message">消息</param>
        public static void Warn(ILoggerRepository repository, object message)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Warn(message);
        }
        /// <summary>
        /// 警告
        /// </summary>
        /// <param name="message">消息</param>
        public static void Warn(object message)
        {
            Warn(CommRepository, message);
        }
        /// <summary>
        /// 警告
        /// </summary>
        /// <param name="message">消息</param>
        public static void Warn(string repository, object message)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Warn(message);
        }
        /// <summary>
        /// 警告
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="ex">异常</param>
        public static void Warn(ILoggerRepository repository, object message, Exception ex)
        {
            LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Warn(message, ex);
        }

        /// <summary>
        /// 警告
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="ex">异常</param>
        public static void Warn(string repository, object message, Exception ex)
        {
            LogManager.GetLogger(repository, GetCurrentMethodFullName()).Warn(message, ex);
        }

        /// <summary>
        /// 添加日志上下文支持
        /// </summary>
        public static IDisposable BeginScope(this ILoggerRepository repository, Dictionary<string, object> context)
        {
            return LogManager.GetLogger(repository.Name, GetCurrentMethodFullName()).Logger.Repository.BeginScope(context);
        }

        private static string GetCurrentMethodFullName()
        {
            try
            {
                StackFrame frame;
                string str;
                int num = 2;
                StackTrace trace = new StackTrace();
                int length = trace.GetFrames().Length;
                do
                {
                    frame = trace.GetFrame(num++);
                    str = frame.GetMethod().DeclaringType.ToString();
                }
                while (str.EndsWith("Exception") && (num < length));
                string name = frame.GetMethod().Name;
                return (str + "." + name);
            }
            catch
            {
                return null;
            }
        }
    }
}
