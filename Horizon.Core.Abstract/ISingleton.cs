using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 单列模式
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ISingleton<T>
    {
        /// <summary>
        /// 单列对象实例
        /// </summary>
        T Instance { get; }
    }
}
