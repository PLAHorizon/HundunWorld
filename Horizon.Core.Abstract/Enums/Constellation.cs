using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 星座
    /// </summary>
    public enum Constellation
    {
        /*白羊座（3.12-4.19） Aries 金牛座（4.20-5.20）Taurus 双子座（5.21-6.21） Gemini 巨蟹座（6.22-7.22） Cancer 狮子座（7.23-8.22） Leo
         * 处女座（8.23-9.22） Virgo 天枰座（9.23-10.23） Libra 天蝎座（10.24-11.22）Scorpio 射手座（11.23-12.21） Sagittarius
         摩羯座（12.22-1.19） Capricornus 水瓶座（1.20-2.18） Aquarius 双鱼座（2.19-3.20） Pisces*/
        /// <summary>
        /// 白羊座
        /// </summary>
        [Description("白羊座")]
        Aries = 0,
        /// <summary>
        /// 金牛座
        /// </summary>
        [Description("金牛座")]
        Taurus = 1,
        /// <summary>
        /// 双子座
        /// </summary>
        [Description("双子座")]
        Gemini = 2,
        /// <summary>
        /// 巨蟹座
        /// </summary>
        [Description("巨蟹座")]
        Cancer = 3,
        /// <summary>
        /// 狮子座
        /// </summary>
        [Description("狮子座")]
        Leo = 4,
        /// <summary>
        /// 处女座
        /// </summary>
        [Description("处女座")]
        Virgo = 5,
        /// <summary>
        /// 天枰座
        /// </summary>
        [Description("天枰座")]
        Libra = 6,
        /// <summary>
        /// 天蝎座
        /// </summary>
        [Description("天蝎座")]
        Scorpio = 7,
        /// <summary>
        /// 射手座
        /// </summary>
        [Description("射手座")]
        Sagittarius = 8,
        /// <summary>
        /// 摩羯座
        /// </summary>
        [Description("摩羯座")]
        Capricornus = 9,
        /// <summary>
        /// 水瓶座
        /// </summary>
        [Description("水瓶座")]
        Aquarius = 10,
        /// <summary>
        /// 双鱼座
        /// </summary>
        [Description("双鱼座")]
        Pisces = 11
    }
}
