using System;
using System.Collections.Generic;
using System.Text;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;

namespace Horizon.Core
{
    /// <summary>
    /// 事务处理异常
    /// </summary>
    public class TransactionException : ApplicationException
    {
        /// <summary>
        /// 事务处理异常
        /// </summary>
        /// <param name="transcation"></param>
        public TransactionException(TranscationType transcation)
        {
            HResult = (int)transcation;
            Log.Info(Log.CommRepository, this.Message, this);
        }
        /// <summary>
        /// 事务处理异常
        /// </summary>
        /// <param name="transcation"></param>
        /// <param name="message"></param>
        public TransactionException(TranscationType transcation, string message) : base(message)
        {
            HResult = (int)transcation;
            Log.Info(Log.CommRepository, message, this);
        }
        /// <summary>
        /// 事务处理异常
        /// </summary>
        /// <param name="transcation"></param>
        /// <param name="message"></param>
        /// <param name="inner"></param>
        public TransactionException(TranscationType transcation, string message, Exception inner) : base(message, inner)
        {
            HResult = (int)transcation;
            Log.Info(Log.CommRepository, message, inner);
        }
    }
}
