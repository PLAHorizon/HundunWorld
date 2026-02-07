using Horizon.Core;
using Horizon.Core.Abstract;
using Orleans;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    /// <summary>
    /// 客户端接收数据基础回调接口
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IBaseCallback<T> : IGrainObserver
    {
        /// <summary>
        /// 客户端接收数据基础回调接口
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        Task ReceiveMessageAsync(TransactionMessage<T> message);
    }
}
