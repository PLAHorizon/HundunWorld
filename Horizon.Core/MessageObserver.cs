using Horizon.Core.Abstract;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Horizon.Core
{
    /// <summary>
    /// 消息流
    /// </summary>
    /// <typeparam name="T">消息实体类型类型参数</typeparam>
    //internal class MessageObserver<T> : IAsyncObserver<TransactionMessage<T>>
    internal class MessageObserver<T>
    {

        private readonly Func<TransactionMessage<T>, Task> action;

        public MessageObserver(Func<TransactionMessage<T>, Task> action)
        {

            this.action = action;
        }

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex)
        {
            return Task.CompletedTask;
        }

        // public Task OnNextAsync(TransactionMessage<T> item, StreamSequenceToken token = null) => action(item);
    }
}
