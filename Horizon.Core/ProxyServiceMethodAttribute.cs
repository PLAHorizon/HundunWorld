using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core
{
    /// <summary>
    /// WCF  服务代理方法标记
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class ProxyServiceMethodAttribute : Attribute
    {
    }
}
