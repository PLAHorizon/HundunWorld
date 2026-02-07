using System;
using System.Reflection;

namespace Horizon.Entities
{
    public static class DesignTimeContextChecker
    {
        public static bool IsDesignTime()
        {
            // 检查是否在设计时环境（EF Core 迁移工具或 Visual Studio 设计器）
            return Assembly.GetEntryAssembly() == null || 
                   AppDomain.CurrentDomain.FriendlyName.Contains("ef.dll") ||
                   AppDomain.CurrentDomain.FriendlyName.Contains("DesignTime");
        }
    }
}