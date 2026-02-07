using System;
using System.Text.RegularExpressions;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// 验证帮助类
    /// </summary>
    public class ValidateHelper
    {
        private static Regex _dateregex = new Regex(@"(\d{4})-(\d{1,2})-(\d{1,2})");
        private static Regex _emailregex = new Regex(@"^(\w)+(\.\w+)*@(\w)+((\.\w+)+)$", RegexOptions.IgnoreCase);
        private static Regex _ipregex = new Regex(@"^(\d{1,2}|1\d\d|2[0-4]\d|25[0-5])\.(\d{1,2}|1\d\d|2[0-4]\d|25[0-5])\.(\d{1,2}|1\d\d|2[0-4]\d|25[0-5])\.(\d{1,2}|1\d\d|2[0-4]\d|25[0-5])$");
        private static Regex _mobileregex = new Regex("^(13|14|15|16|17|18|19)[0-9]{9}$");
        private static Regex _numericregex = new Regex(@"^[-]?[0-9]+(\.[0-9]+)?$");
        private static Regex _phoneregex = new Regex(@"^(\d{3,4}-?)?\d{7,8}$");
        private static Regex _zipcoderegex = new Regex(@"^\d{6}$");

        /// <summary>
        /// 判断当前时间是否在指定的时间段内
        /// </summary>
        /// <param name="periodList">指定时间段</param>
        /// <param name="liePeriod">所处时间段</param>
        /// <returns></returns>
        public static bool BetweenPeriod(string[] periodList, out string liePeriod)
        {
            if (periodList != null && periodList.Length > 0)
            {
                DateTime now = DateTime.Now;
                DateTime date = now.Date;
                foreach (string str in periodList)
                {
                    int index = str.IndexOf("-");
                    DateTime time = TypeHelper.StringToDateTime(str.Substring(0, index));
                    DateTime time2 = TypeHelper.StringToDateTime(str.Substring(index + 1));
                    if (time < time2)
                    {
                        if (now > time && now < time2)
                        {
                            liePeriod = str;
                            return true;
                        }
                    }
                    else if ((now > time && now < date.AddDays(1.0)) || now < time2)
                    {
                        liePeriod = str;
                        return true;
                    }
                }
            }
            liePeriod = string.Empty;
            return false;
        }

        /// <summary>
        /// 判断当前时间是否在指定的时间段内
        /// </summary>
        /// <param name="periodStr">指定时间段</param>
        /// <param name="liePeriod">所处时间段</param>
        /// <returns></returns>
        public static bool BetweenPeriod(string periodStr, out string liePeriod)
        {
            string[] strArrays = StringHelper.SplitString(periodStr, "\n");
            return BetweenPeriod(strArrays, out liePeriod);
        }

        /// <summary>
        /// 判断当前时间是否在指定的时间段内
        /// </summary>
        /// <param name="periodList">指定时间段</param>
        /// <returns></returns>
        public static bool BetweenPeriod(string periodList)
        {
            string liePeriod = string.Empty;
            return BetweenPeriod(periodList, out liePeriod);
        }

        /// <summary>
        /// 是否为15位身份证号
        /// </summary>
        private static bool CheckIDCard15(string Id)
        {
            long result = 0;
            if (!(long.TryParse(Id, out result) && (result >= Math.Pow(10, 14))))
            {
                return false;
            }
            string str = "11x22x35x44x53x12x23x36x45x54x13x31x37x46x61x14x32x41x50x62x15x33x42x51x63x21x34x43x52x64x65x71x81x82x91";
            if (str.IndexOf(Id.Remove(2)) == -1)
            {
                return false;
            }
            string s = Id.Substring(6, 6).Insert(4, "-").Insert(2, "-");
            DateTime time = new DateTime();
            if (!DateTime.TryParse(s, out time))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 是否为18位身份证号
        /// </summary>
        private static bool CheckIDCard18(string Id)
        {
            long result = 0;
            if (!((long.TryParse(Id.Remove(0x11), out result) && (result >= Math.Pow(10, 16))) && long.TryParse(Id.Replace('x', '0').Replace('X', '0'), out result)))
            {
                return false;
            }
            string str = "11x22x35x44x53x12x23x36x45x54x13x31x37x46x61x14x32x41x50x62x15x33x42x51x63x21x34x43x52x64x65x71x81x82x91";
            if (str.IndexOf(Id.Remove(2)) == -1)
            {
                return false;
            }
            string s = Id.Substring(6, 8).Insert(6, "-").Insert(4, "-");
            DateTime time = new DateTime();
            if (!DateTime.TryParse(s, out time))
            {
                return false;
            }
            string[] strArray = "1,0,x,9,8,7,6,5,4,3,2".Split(',');
            string[] strArray2 = "7,9,10,5,8,4,2,1,6,3,7,9,10,5,8,4,2".Split(',');
            char[] chArray = Id.Remove(0x11).ToCharArray();
            int a = 0;
            for (int i = 0; i < 0x11; i++)
            {
                a += int.Parse(strArray2[i]) * int.Parse(chArray[i].ToString());
            }
            int num = -1;
            Math.DivRem(a, 11, out num);
            if (strArray[num] != Id.Substring(0x11, 1).ToLower())
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 判断一个ip是否在另一个ip内
        /// </summary>
        /// <param name="sourceIP">检测ip</param>
        /// <param name="targetIP">匹配ip</param>
        /// <returns></returns>
        public static bool InIP(string sourceIP, string targetIP)
        {
            if (!string.IsNullOrEmpty(sourceIP) && !string.IsNullOrEmpty(targetIP))
            {
                string[] strArray = StringHelper.SplitString(sourceIP, ".");
                string[] strArray2 = StringHelper.SplitString(targetIP, ".");
                int length = strArray.Length;
                for (int i = 0; i < length; i++)
                {
                    if (strArray2[i] == "*")
                    {
                        return true;
                    }
                    if (strArray[i] != strArray2[i])
                    {
                        return false;
                    }
                    if (i == 3)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 判断一个ip是否在另一个ip内
        /// </summary>
        /// <param name="sourceIP">检测ip</param>
        /// <param name="targetIPList">匹配ip列表</param>
        /// <returns></returns>
        public static bool InIPList(string sourceIP, string[] targetIPList)
        {
            if ((targetIPList != null) && (targetIPList.Length > 0))
            {
                foreach (string str in targetIPList)
                {
                    if (InIP(sourceIP, str))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 判断一个ip是否在另一个ip内
        /// </summary>
        /// <param name="sourceIP">检测ip</param>
        /// <param name="targetIPStr">匹配ip</param>
        /// <returns></returns>
        public static bool InIPList(string sourceIP, string targetIPStr)
        {
            string[] strArrays = StringHelper.SplitString(targetIPStr, "\n");
            return InIPList(sourceIP, strArrays);
        }

        /// <summary>
        /// 是否为日期
        /// </summary>
        public static bool IsDate(string s)
        {
            return _dateregex.IsMatch(s);
        }
        /// <summary>
        /// 是否为邮箱名
        /// </summary>
        public static bool IsEmail(string s)
        {
            return (!string.IsNullOrEmpty(s) && _emailregex.IsMatch(s));
        }

        /// <summary>
        /// 是否是身份证号
        /// </summary>
        // 优化正则表达式预编译
        private static readonly Regex EmailRegex = new Regex(@"^[\w-]+(\.[\w-]+)*@([\w-]+\.)+[a-zA-Z]{2,7}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 合并15/18位身份证校验逻辑
        public static bool IsIdCard(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            return id.Length switch
            {
                15 => CheckIDCard15(id),
                18 => CheckIDCard18(id),
                _ => false
            };
        }

        /// <summary>
        /// 是否是图片文件名
        /// </summary>
        /// <returns> </returns>
        public static bool IsImgFileName(string fileName)
        {
            if (fileName.IndexOf(".") == -1)
            {
                return false;
            }
            string lower = fileName.Trim().ToLower();
            string str = lower.Substring(lower.LastIndexOf("."));
            return ((((str == ".png") || (str == ".bmp")) || ((str == ".jpg") || (str == ".jpeg"))) || (str == ".gif"));
        }

        /// <summary>
        /// 是否为IP
        /// </summary>
        public static bool IsIP(string s)
        {
            return _ipregex.IsMatch(s);
        }

        /// <summary>
        /// 是否为手机号
        /// </summary>
        public static bool IsMobile(string s)
        {
            return (!string.IsNullOrEmpty(s) && _mobileregex.IsMatch(s));
        }

        /// <summary>
        /// 是否是数值(包括整数和小数)
        /// </summary>
        public static bool IsNumeric(string numericStr)
        {
            return _numericregex.IsMatch(numericStr);
        }

        /// <summary>
        /// 是否是数值(包括整数和小数)
        /// </summary>
        public static bool IsNumericArray(string[] numericStrList)
        {
            if ((numericStrList != null) && (numericStrList.Length > 0))
            {
                foreach (string str in numericStrList)
                {
                    if (!IsNumeric(str))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 是否是数值(包括整数和小数)
        /// </summary>
        public static bool IsNumericRule(string numericRuleStr, string splitChar)
        {
            return IsNumericArray(StringHelper.SplitString(numericRuleStr, splitChar));
        }

        /// <summary>
        /// 是否是数值(包括整数和小数)
        /// </summary>
        public static bool IsNumericRule(string numericRuleStr)
        {
            return IsNumericRule(numericRuleStr, ",");
        }

        /// <summary>
        /// 是否为固话号
        /// </summary>
        public static bool IsPhone(string s)
        {
            return (!string.IsNullOrEmpty(s) && _phoneregex.IsMatch(s));
        }

        /// <summary>
        /// 是否为邮政编码
        /// </summary>
        public static bool IsZipCode(string s)
        {
            return (!string.IsNullOrEmpty(s) && _zipcoderegex.IsMatch(s));
        }
    }
}