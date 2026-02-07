using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 拦截接口
    /// </summary>
    public interface IIntercept
    {
        bool Apply();
    }
}
