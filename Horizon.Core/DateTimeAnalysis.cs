using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    /// <summary>
    /// 时间解析
    /// </summary>
    public static class DateTimeAnalysis
    {

        public static DateTime GetStartDayOfWeeks(int year, int month, int index)
        {
            DateTime minValue;
            if (year < 1600 || year > 9999)
            {
                minValue = DateTime.MinValue;
            }
            else if (month < 0 || month > 12)
            {
                minValue = DateTime.MinValue;
            }
            else if (index >= 1)
            {
                DateTime dateTime = new DateTime(year, month, 1);
                int num = 7;
                if (Convert.ToInt32(dateTime.DayOfWeek.ToString("d")) > 0)
                {
                    num = Convert.ToInt32(dateTime.DayOfWeek.ToString("d"));
                }
                DateTime dateTime1 = dateTime.AddDays(1 - num);
                DateTime dateTime2 = dateTime1.AddDays(index * 7);
                minValue = ((dateTime2 - dateTime.AddMonths(1)).Days <= 0 ? dateTime2 : DateTime.MinValue);
            }
            else
            {
                minValue = DateTime.MinValue;
            }
            return minValue;
        }

        public static string GetWeekSpanOfMonth(int year, int month)
        {

            if (year < 1600 || year > 9999)
            {
                return "";
            }
            if (month < 0 || month > 12)
            {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 1; i < 5; i++)
            {
                DateTime time = new DateTime(year, month, 1);
                int num = 7;
                if (Convert.ToInt32(time.DayOfWeek.ToString("d")) > 0)
                {
                    num = Convert.ToInt32(time.DayOfWeek.ToString("d"));
                }
                DateTime time2 = time.AddDays(1 - num).AddDays(i * 7);
                TimeSpan span = time2 - time.AddMonths(1);
                if (span.Days > 0)
                {
                    return "";
                }
                builder.Append(time2.ToString("yyyy-MM-dd"));
                builder.Append(" ~ ");
                builder.Append(time2.AddDays(6.0).ToString("yyyy-MM-dd"));
                builder.Append(Environment.NewLine);
            }
            return builder.ToString();

        }


        /// <summary>
        /// 获得中文当前日期 2000年01月01日 字符形式
        /// </summary>
        public static string GetChineseDate(this DateTime time)
        {
            return time.ToString("yyyy年MM月dd日");
        }

        /// <summary>
        /// 获得当前日期 2000-01-01
        /// </summary>
        public static string GetDate(this DateTime time)
        {
            return time.ToString("yyyy-MM-dd");
        }
        /// <summary>
        /// 获得当前时间(不含日期部分) 12：00：00
        /// </summary>
        public static string GetTime()
        {
            return DateTime.Now.ToString("HH:mm:ss");
        }
        /// <summary>
        /// 获得当前时间的""yyyy-MM-dd HH:mm:ss""格式字符串
        /// </summary>
        public static string GetDateTime(this DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 获得当前时间的""yyyy-MM-dd HH:mm:ss:fffffff""格式字符串
        /// </summary>
        public static string GetDateTimeMS(this DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm:ss:fffffff");
        }

        /// <summary>
        /// 获得当前时间的""yyyy年MM月dd日 HH:mm:ss""格式字符串
        /// </summary>
        public static string GetDateTimeU(this DateTime time)
        {
            return string.Format("{0:U}", time);
        }

    }
}
