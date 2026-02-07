using Horizon.Game.Message.Enums;
using System;

namespace HundunWorld.Game.UI.ErrorHandling
{
    /// <summary>
    /// 閿欒绫诲瀷鏋氫妇
    /// </summary>
    

    /// <summary>
    /// 閿欒涓ラ噸绾у埆
    /// </summary>
    

    /// <summary>
    /// 閿欒淇℃伅绫?
    /// </summary>
    public class ErrorInfo
    {
        public ErrorType Type { get; set; }
        public ErrorSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public DateTime Timestamp { get; set; }
        public string Source { get; set; }
        public Exception Exception { get; internal set; }

        public ErrorInfo() { }  
        public ErrorInfo(ErrorType type, ErrorSeverity severity, string message, string code = "", string details = "", string source = "")
        {
            Type = type;
            Severity = severity;
            Message = message;
            Code = code;
            Details = details;
            Source = source;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// 鍦烘櫙鐘舵€佸揩鐓?
    /// </summary>
    public class SceneStateSnapshot
    {
        public SceneType Scene { get; set; }
        public DateTime Timestamp { get; set; }
        public UserSession UserSession { get; set; }
        public object AdditionalData { get; set; }
    }
}
