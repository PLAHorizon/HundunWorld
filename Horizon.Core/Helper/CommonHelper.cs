using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// 普通帮助类
    /// </summary>
    public class CommonHelper
    {
        private static Regex _tbbrRegex = new Regex(@"\s*|\t|\r|\n", RegexOptions.IgnoreCase);
        private static string[] _weekdays = new string[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
        /// <summary>
        /// 去除字符串中的空格、回车、换行符、制表符
        /// </summary>
        public static string ClearTBBR(string str)
        {
            if (!string.IsNullOrEmpty(str))
            {
                return _tbbrRegex.Replace(str, "");
            }
            return string.Empty;
        }

        /// <summary>
        /// 将ip地址转换成Int64类型
        /// </summary>
        /// <param name="ip">ip</param>
        /// <returns></returns>
        public static long ConvertIPToInt64(string ip)
        {
            return Convert.ToInt64(ip.Replace(".", string.Empty));
        }

        /// <summary>
        /// 删除字符串中的空行
        /// </summary>
        /// <returns></returns>
        public static string DeleteNullOrSpaceRow(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            string[] strArray = StringHelper.SplitString("\r\n");
            StringBuilder builder = new StringBuilder();
            foreach (string str in strArray)
            {
                if (!string.IsNullOrWhiteSpace(str))
                {
                    builder.AppendFormat("{0}\r\n", str);
                }
            }
            if (builder.Length > 0)
            {
                builder.Remove(builder.Length - 2, 2);
            }
            return builder.ToString();
        }

        /// <summary>
        /// 转义正则表达式
        /// </summary>
        public static string EscapeRegex(string s)
        {
            string[] strArray1 = new string[] { @"\", ".", "+", "*", "?", "{", "}", "[", "^", "]", "$", "(", ")", "=", "!", "<", ">", "|", ":" };
            string[] strArray2 = new string[] { @"\\", @"\.", @"\+", @"\*", @"\?", @"\{", @"\}", @"\[", @"\^", @"\]", @"\$", @"\(", @"\)", @"\=", @"\!", @"\<", @"\>", @"\|", @"\:" };
            for (int i = 0; i < strArray1.Length; i++)
            {
                s = s.Replace(strArray1[i], strArray2[i]);
            }
            return s;
        }

        /// <summary>
        /// 获得中文当前日期
        /// </summary>
        public static string GetChineseDate()
        {
            return DateTime.Now.ToString("yyyy月MM日dd");
        }

        /// <summary>
        /// 获得当前日期
        /// </summary>
        public static string GetDate()
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// 获得当前时间的""yyyy-MM-dd HH:mm:ss""格式字符串
        /// </summary>
        public static string GetDateTime()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 获得当前时间的""yyyy-MM-dd HH:mm:ss:fffffff""格式字符串
        /// </summary>
        public static string GetDateTimeMS()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fffffff");
        }

        /// <summary>
        /// 获得当前时间的""yyyy年MM月dd日 HH:mm:ss""格式字符串
        /// </summary>
        public static string GetDateTimeU()
        {
            return string.Format("{0:U}", DateTime.Now);
        }

        /// <summary>
        /// 获得当前天
        /// </summary>
        public static string GetDay()
        {
            return DateTime.Now.Day.ToString("00");
        }

        /// <summary>
        /// 获得当前星期(数字)
        /// </summary>
        public static string GetDayOfWeek()
        {
            return DateTime.Now.DayOfWeek.ToString();
        }

        ///  <summary>
        /// 获得邮箱提供者
        ///  </summary>
        ///  <param name="email">邮箱</param>
        ///  <returns></returns>
        public static string GetEmailProvider(string email)
        {
            int num = email.LastIndexOf('@');
            if (num > 0)
            {
                return email.Substring(num + 1);
            }
            return string.Empty;
        }

        /// <summary>
        /// 获得当前小时
        /// </summary>
        public static string GetHour()
        {
            return DateTime.Now.Hour.ToString("00");
        }

        /// <summary>
        /// 获得指定数量的html空格
        /// </summary>
        /// <returns></returns>
        public static string GetHtmlBS(int count)
        {
            if (count == 1)
            {
                return "&nbsp;&nbsp;&nbsp;&nbsp;";
            }
            if (count == 2)
            {
                return "&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;";
            }
            if (count == 3)
            {
                return "&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;";
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                builder.Append("&nbsp;&nbsp;&nbsp;&nbsp;");
            }
            return builder.ToString();
        }

        /// <summary>
        /// 获得指定数量的htmlSpan元素
        /// </summary>
        /// <returns></returns>
        public static string GetHtmlSpan(int count)
        {
            if (count <= 0)
            {
                return "";
            }
            if (count == 1)
            {
                return "<span></span>";
            }
            if (count == 2)
            {
                return "<span></span><span></span>";
            }
            if (count == 3)
            {
                return "<span></span><span></span><span></span>";
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                builder.Append("<span></span>");
            }
            return builder.ToString();
        }

        /// <summary>
        /// 获得字符串在字符串数组中的位置
        /// </summary>
        public static int GetIndexInArray(string s, string[] array, bool ignoreCase)
        {
            if ((!string.IsNullOrEmpty(s) && array != null) && array.Length != 0)
            {
                int num = 0;
                string str = null;
                if (ignoreCase)
                {
                    s = s.ToLower();
                }
                foreach (string str2 in array)
                {
                    if (ignoreCase)
                    {
                        str = str2.ToLower();
                    }
                    else
                    {
                        str = str2;
                    }
                    if (s == str)
                    {
                        return num;
                    }
                    num++;
                }
            }
            return -1;
        }

        /// <summary>
        /// 获得字符串在字符串数组中的位置
        /// </summary>
        public static int GetIndexInArray(string s, string[] array)
        {
            return GetIndexInArray(s, array, false);
        }

        /// <summary>
        /// 获得当前月
        /// </summary>
        public static string GetMonth()
        {
            return DateTime.Now.Month.ToString("00");
        }

        /// <summary>
        /// 获得当前时间(不含日期部分)
        /// </summary>
        public static string GetTime()
        {
            return DateTime.Now.ToString("HH:mm:ss");
        }

        /// <summary>
        /// 去除字符串中的重复元素
        /// </summary>
        /// <returns></returns>
        public static string GetUniqueString(string s)
        {
            return GetUniqueString(s, ",");
        }

        /// <summary>
        /// 去除字符串中的重复元素
        /// </summary>
        /// <returns></returns>
        public static string GetUniqueString(string s, string splitStr)
        {
            return ObjectArrayToString(RemoveRepeaterItem(StringHelper.SplitString(s, splitStr)), splitStr);
        }

        /// <summary>
        /// 获得当前星期(汉字)
        /// </summary>
        public static string GetWeek()
        {
            return _weekdays[(int)DateTime.Now.DayOfWeek];
        }

        /// <summary>
        /// 获得当前年
        /// </summary>
        public static string GetYear()
        {
            return DateTime.Now.Year.ToString();
        }

        /// <summary>
        /// 隐藏邮箱
        /// </summary>
        public static string HideEmail(string email)
        {
            int startIndex = email.LastIndexOf('@');
            switch (startIndex)
            {
                case 1:
                    return ("*" + email.Substring(startIndex));

                case 2:
                    return (email[0] + "*" + email.Substring(startIndex));
            }
            StringBuilder builder = new StringBuilder();
            builder.Append(email.Substring(0, 2));
            for (int i = startIndex - 2; i > 0; i--)
            {
                builder.Append("*");
            }
            builder.Append(email.Substring(startIndex));
            return builder.ToString();
        }

        /// <summary>
        /// 隐藏手机
        /// </summary>
        public static string HideMobile(string mobile)
        {
            return string.Concat(mobile.Substring(0, 3), "*****", mobile.Substring(8));
        }

        /// <summary>
        /// 将整数数组拼接成字符串
        /// </summary>
        public static string IntArrayToString(int[] array, string splitStr)
        {
            if (array == null || array.Length == 0)
            {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < array.Length; i++)
            {
                builder.AppendFormat("{0}{1}", array[i], splitStr);
            }
            return builder.Remove(builder.Length - splitStr.Length, splitStr.Length).ToString();
        }

        /// <summary>
        /// 将整数数组拼接成字符串
        /// </summary>
        public static string IntArrayToString(int[] array)
        {
            return IntArrayToString(array, ",");
        }

        /// <summary>
        /// 判断字符串是否在字符串数组中
        /// </summary>
        public static bool IsInArray(string s, string[] array, bool ignoreCase)
        {
            return GetIndexInArray(s, array, ignoreCase) > -1;
        }

        /// <summary>
        /// 判断字符串是否在字符串数组中
        /// </summary>
        public static bool IsInArray(string s, string[] array)
        {
            return IsInArray(s, array, false);
        }

        /// <summary>
        /// 判断字符串是否在字符串中
        /// </summary>
        public static bool IsInArray(string s, string array, string splitStr, bool ignoreCase)
        {
            return IsInArray(s, StringHelper.SplitString(array, splitStr), ignoreCase);
        }

        /// <summary>
        /// 判断字符串是否在字符串中
        /// </summary>
        public static bool IsInArray(string s, string array, string splitStr)
        {
            return IsInArray(s, StringHelper.SplitString(array, splitStr), false);
        }

        /// <summary>
        /// 判断字符串是否在字符串中
        /// </summary>
        public static bool IsInArray(string s, string array)
        {
            return IsInArray(s, StringHelper.SplitString(array, ","), false);
        }

        /// <summary>
        /// 将对象数组拼接成字符串
        /// </summary>
        public static string ObjectArrayToString(object[] array, string splitStr)
        {
            if (array == null || array.Length == 0)
            {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < array.Length; i++)
            {
                builder.AppendFormat("{0}{1}", array[i], splitStr);
            }
            return builder.Remove(builder.Length - splitStr.Length, splitStr.Length).ToString();
        }

        /// <summary>
        /// 将对象数组拼接成字符串
        /// </summary>
        public static string ObjectArrayToString(object[] array)
        {
            return ObjectArrayToString(array, ",");
        }

        /// <summary>
        /// 移除数组中的指定项
        /// </summary>
        /// <param name="array">源数组</param>
        /// <param name="removeItem">要移除的项</param>
        /// <param name="removeBackspace">是否移除空格</param>
        /// <param name="ignoreCase">是否忽略大小写</param>
        /// <returns></returns>
        public static string[] RemoveArrayItem(string[] array, string removeItem, bool removeBackspace, bool ignoreCase)
        {

            if (array != null && array.Length > 0)
            {
                StringBuilder builder = new StringBuilder();
                if (ignoreCase)
                {
                    removeItem = removeItem.ToLower();
                }
                string str = "";
                foreach (string str2 in array)
                {
                    if (ignoreCase)
                    {
                        str = str2.ToLower();
                    }
                    else
                    {
                        str = str2;
                    }
                    if (str != removeItem)
                    {
                        builder.AppendFormat("{0}_", removeBackspace ? str2.Trim() : str2);
                    }
                }
                return StringHelper.SplitString(builder.Remove(builder.Length - 1, 1).ToString(), "_");
            }
            return array;
        }

        /// <summary>
        /// 移除数组中的指定项
        /// </summary>
        /// <param name="array">源数组</param>
        /// <returns></returns>
        public static string[] RemoveArrayItem(string[] array)
        {
            return RemoveArrayItem(array, "", true, false);
        }

        /// <summary>
        /// 移除数组中的重复项
        /// </summary>
        /// <returns></returns>
        public static int[] RemoveRepeaterItem(int[] array)
        {

            if (array == null || array.Length < 2)
            {
                return array;
            }
            Array.Sort<int>(array);
            int num1 = 1;
            for (int i = 1; i < array.Length; i++)
            {
                if (array[i] != array[i - 1])
                {
                    num1++;
                }
            }
            int[] numArray = new int[num1];
            numArray[0] = array[0];
            int num2 = 1;
            for (int i = 1; i < array.Length; i++)
            {
                if (array[i] != array[i - 1])
                {
                    numArray[num2++] = array[i];
                }
            }
            return numArray;
        }

        /// <summary>
        /// 移除数组中的重复项
        /// </summary>
        /// <returns></returns>
        public static string[] RemoveRepeaterItem(string[] array)
        {
            if (array == null || array.Length < 2)
            {
                return array;
            }
            Array.Sort<string>(array);
            int num = 1;
            for (int i = 1; i < array.Length; i++)
            {
                if (array[i] != array[i - 1])
                {
                    num++;
                }
            }
            string[] strArray = new string[num];
            strArray[0] = array[0];
            int num1 = 1;
            for (int i = 1; i < array.Length; i++)
            {
                if (array[i] != array[i - 1])
                {
                    strArray[num1++] = array[i];
                }
            }
            return strArray;
        }

        /// <summary>
        /// 移除字符串中的指定项
        /// </summary>
        /// <param name="s">源字符串</param>
        /// <param name="splitStr">分割字符串</param>
        /// <returns></returns>
        public static string[] RemoveStringItem(string s, string splitStr)
        {
            return RemoveArrayItem(StringHelper.SplitString(s, splitStr), "", true, false);
        }

        /// <summary>
        /// 移除字符串中的指定项
        /// </summary>
        /// <param name="s">源字符串</param>
        /// <returns></returns>
        public static string[] RemoveStringItem(string s)
        {
            return RemoveArrayItem(StringHelper.SplitString(s), "", true, false);
        }

        /// <summary>
        /// 将字符串数组拼接成字符串
        /// </summary>
        public static string StringArrayToString(string[] array, string splitStr)
        {
            return ObjectArrayToString(array, splitStr);
        }

        /// <summary>
        /// 将字符串数组拼接成字符串
        /// </summary>
        public static string StringArrayToString(string[] array)
        {
            return StringArrayToString(array, ",");
        }

        /// <summary>
        /// 截取小数
        /// </summary>
        /// <param name="dec">小数值</param>
        /// <param name="pointCount">保留小数点数</param>
        /// <returns></returns>
        public static decimal SubDecimal(decimal dec, int pointCount)
        {
            string str = dec.ToString();
            return TypeHelper.StringToDecimal(str.Substring(0, str.IndexOf('.') + pointCount + 1));
        }

        /// <summary>
        /// 去除字符串首尾处的空格、回车、换行符、制表符
        /// </summary>
        public static string TBBRTrim(string str)
        {
            if (!string.IsNullOrEmpty(str))
            {
                return str.Trim().Trim('\r').Trim('\n').Trim('\t');
            }
            return string.Empty;
        }
    }
}