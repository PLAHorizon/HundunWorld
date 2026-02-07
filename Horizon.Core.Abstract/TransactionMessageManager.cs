using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 消息传输管理类
    /// </summary>
    public class TransactionMessageManager
    {
        public static T BuilderResponseMessage<T, M>(Header header, M boyMessage, int messageCode) where T : TransactionMessage<M>
        {
            T transactionMessage = FastActivator.Create<T>(false);
            StateMessage message = FastActivator.Create<StateMessage>(false);
            message.MessageCode = messageCode;
            transactionMessage.Body = boyMessage;
            transactionMessage.Header = header;
            transactionMessage.Message = message;
            return transactionMessage;
        }
    }
}
