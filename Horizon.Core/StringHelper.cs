using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    public static class StringHelper
    {
        /// <summary>
        /// 将字符串转换为 int类型数值
        /// </summary>
        /// <param name="str">字符值</param>
        /// <returns></returns>
        public static int ToInt(this string str)
        {
            int.TryParse(str, out int result);
            return result;
        }

        /// <summary>
        /// 将字符串转换为 long 类型数值
        /// </summary>
        /// <param name="str">字符值</param>
        /// <returns></returns>
        public static long ToLong(this string str)
        {
            long.TryParse(str, out long result);
            return result;
        }
    }
}
