using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    public class MetaInfo
    {
        public long ContentLength
        {
            get;
            set;
        }

        public string ContentType
        {
            get;
            set;
        }

        public DateTime? LastModifiedTime
        {
            get;
            set;
        }

        public string ObjectType
        {
            get;
            set;
        }
    }
}
