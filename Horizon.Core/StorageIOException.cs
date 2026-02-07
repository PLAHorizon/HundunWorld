using System;
using System.Collections.Generic;
using System.Text;
using Horizon.Core.Abstract;

namespace Horizon.Core
{
    /// <summary>
    /// 存储IO异常
    /// </summary>
    public class StorageIOException : ApplicationException
    {
        public StorageIOException()
        {
            Log.Info(Log.CommRepository, this.Message, this);
        }

        public StorageIOException(string message) : base(message)
        {
            Log.Info(Log.CommRepository, message, this);
        }

        public StorageIOException(string message, Exception inner) : base(message, inner)
        {
            Log.Info(Log.CommRepository, message, inner);
        }
    }
}
