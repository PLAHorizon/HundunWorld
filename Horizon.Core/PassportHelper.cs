using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core
{
    /// <summary>
    /// 设置通行证
    /// </summary>
    public class PassportHelper
    {
        public const string BASEID = "0123567890987";

        /// <summary>
        /// 生成特定的ID号(6--11位10进制数字)
        /// </summary>
        /// <param name="repeat">校验值</param>
        /// <returns>通行证ID</returns>
        public static long GetPassportID(long repeat)
        {
            string tem = null;
            Random random = new Random();
            for (int i = 0; i < random.Next(6, 11); i++)
            {
                tem += BASEID[random.Next(0, 9)];
            }
            return (Convert.ToInt64(tem) == repeat) ? GetPassportID(repeat) : Convert.ToInt64(tem);
        }

        /// <summary>
        /// 生成特定的ID号(6--11位10进制数字)
        /// </summary>
        /// <param name="repeat">校验值</param>
        /// <returns>通行证ID</returns>
        public static string GetPassportID(string repeat)
        {
            string tem = null;
            Random random = new Random();
            for (int i = 0; i < random.Next(6, 11); i++)
            {
                tem += BASEID[random.Next(0, 9)];
            }
            while (tem.StartsWith("0"))
                tem = long.Parse(tem).ToString();
            return (tem == repeat) ? GetPassportID(repeat) : tem;
        }

        /// <summary>
        /// 生成特定的ID号
        /// </summary>
        /// <param name="repeat">校验值(已存在的ID)</param>
        /// <param name="minN">通行证的最小位数</param>
        /// <param name="maxN">通行证的最大位数</param>
        /// <returns>通行证ID</returns>
        public static long GetPassportID(long repeat, int minN, int maxN)
        {
            string tem = null;
            Random random = new Random();
            for (int i = 0; i < random.Next(minN, maxN); i++)
            {
                tem += BASEID[random.Next(0, 9)];
            }
            return (Convert.ToInt64(tem) == repeat) ? GetPassportID(repeat, minN, maxN) : Convert.ToInt64(tem);
        }

        /// <summary>
        /// 生成特定的ID号
        /// </summary>
        /// <param name="repeat">校验值(已存在的ID)</param>
        /// <param name="minN">通行证的最小位数</param>
        /// <param name="maxN">通行证的最大位数</param>
        /// <returns>通行证ID</returns>
        public static string GetPassportID(string repeat, int minN, int maxN)
        {
            string tem = null;
            Random random = new Random();
            for (int i = 0; i < random.Next(minN, maxN); i++)
            {
                tem += BASEID[random.Next(0, 9)];
            }
            if (tem.StartsWith("0"))
            {
                string h = string.Empty;
                for (int i = 0; i < tem.Length - long.Parse(tem).ToString().Length; i++)
                    h += "0";
                tem = long.Parse(tem).ToString() + h;//前位零移到后位零
            }
            return (tem == repeat) ? GetPassportID(repeat, minN, maxN) : tem;
        }

        /// <summary>
        /// 提供加密服务
        /// </summary>
        /// <param name="pasportID">通行证号</param>
        /// <param name="password">明文密码</param>
        /// <returns>返回经过最终加密的密码</returns>
        public static string SetPasportPassword(string pasportID, string password)
        {
            Long_Aes des = new Long_Aes();
            des.EncryptStr = password;
            des.LongAesKey = Long_Aes.AesKey;
            des.AesEncrypt();
            password = des.LongAesStr;

            return LongAes.SetPassword(pasportID, password);
        }
    }
}
