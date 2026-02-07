using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// 安全帮助类
    /// </summary>
    public class SecureHelper
    {
        private readonly static byte[] _aeskeys = new byte[] { 18, 52, 86, 120, 144, 171, 205, 239, 18, 52, 86, 120, 144, 171, 205, 239 };
        private static Regex _base64regex = new Regex("[A-Za-z0-9\\=\\/\\+]");
        private static Regex _sqlkeywordregex = new Regex("(select|insert|delete|from|count\\(|drop|table|update|truncate|asc\\(|mid\\(|char\\(|xp_cmdshell|exec|master|net|local|group|administrators|user|or|and|-|;|,|\\(|\\)|\\[|\\]|\\{|\\}|%|@|\\*|!|\\')", RegexOptions.IgnoreCase);
        /// <summary>
        /// AES解密
        /// </summary>
        /// <param name="decryptStr">解密字符串</param>
        /// <param name="decryptKey">密钥</param>
        /// <returns></returns>
        public static string AESDecrypt(string decryptStr, string decryptKey)
        {
            if (string.IsNullOrWhiteSpace(decryptStr))
            {
                return string.Empty;
            }
            decryptKey = StringHelper.SubString(decryptKey, 0x20);
            decryptKey = decryptKey.PadRight(0x20, ' ');
            byte[] buffer = Convert.FromBase64String(decryptStr);
            SymmetricAlgorithm algorithm = Rijndael.Create();
            algorithm.Key = Encoding.UTF8.GetBytes(decryptKey);
            algorithm.IV = _aeskeys;
            byte[] buffer2 = new byte[buffer.Length];
            using (MemoryStream stream = new MemoryStream(buffer))
            {
                using (CryptoStream stream2 = new CryptoStream(stream, algorithm.CreateDecryptor(), CryptoStreamMode.Read))
                {
                    stream2.ReadExactly(buffer2);
                    stream2.Close();
                    stream.Close();
                }
            }
            return Encoding.UTF8.GetString(buffer2).Replace("\0", "");
        }
        /// <summary>
        /// AES加密
        /// </summary>
        /// <param name="encryptStr">加密字符串</param>
        /// <param name="encryptKey">密钥</param>
        /// <returns></returns>
        public static string AESEncrypt(string encryptStr, string encryptKey)
        {
            if (string.IsNullOrWhiteSpace(encryptStr))
            {
                return string.Empty;
            }
            encryptKey = StringHelper.SubString(encryptKey, 0x20);
            encryptKey = encryptKey.PadRight(0x20, ' ');
            SymmetricAlgorithm algorithm = Rijndael.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(encryptStr);
            algorithm.Key = Encoding.UTF8.GetBytes(encryptKey);
            algorithm.IV = _aeskeys;
            byte[] inArray = null;
            using (MemoryStream stream = new MemoryStream())
            {
                using (CryptoStream stream2 = new CryptoStream(stream, algorithm.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    stream2.Write(bytes, 0, bytes.Length);
                    stream2.FlushFinalBlock();
                    inArray = stream.ToArray();
                    stream2.Close();
                    stream.Close();
                }
            }
            return Convert.ToBase64String(inArray);
        }

        /// <summary> 
        /// Base64解密 
        /// </summary> 
        /// <param name="codeName">解密采用的编码方式，注意和加密时采用的方式一致</param> 
        /// <param name="result">待解密的密文</param> 
        /// <returns>解密后的字符串</returns> 
        public static string DecodeBase64(Encoding codeName, string result)
        {
            byte[] bytes = Convert.FromBase64String(result);
            try
            {
                return codeName.GetString(bytes);
            }
            catch
            {
                return result;
            }
        }

        /// <summary> 
        /// Base64解密，采用utf8编码方式解密 
        /// </summary> 
        /// <param name="result">待解密的密文</param> 
        /// <returns>解密后的字符串</returns> 
        public static string DecodeBase64(string result)
        {
            return DecodeBase64(Encoding.UTF8, result);
        }

        /// <summary> 
        /// Base64加密 
        /// </summary> 
        /// <param name="codeName">加密采用的编码方式</param> 
        /// <param name="source">待加密的明文</param> 
        /// <returns></returns> 
        public static string EncodeBase64(Encoding codeName, string source)
        {
            return Convert.ToBase64String(codeName.GetBytes(source));
        }

        /// <summary> 
        /// Base64加密，采用utf8编码方式加密 
        /// </summary> 
        /// <param name="source">待加密的明文</param> 
        /// <returns>加密后的字符串</returns> 
        public static string EncodeBase64(string source)
        {
            return EncodeBase64(Encoding.UTF8, source);
        }

        /// <summary>
        /// 判断是否是Base64字符串
        /// </summary>
        /// <returns></returns>
        public static bool IsBase64String(string str)
        {
            if (str != null)
            {
                return _base64regex.IsMatch(str);
            }
            return true;
        }

        /// <summary>
        /// 判断当前字符串是否存在SQL注入
        /// </summary>
        /// <returns></returns>
        public static bool IsSafeSqlString(string s)
        {
            if ((s != null) && _sqlkeywordregex.IsMatch(s))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// MD5散列
        /// </summary>
        public static string MD5(string inputStr)
        {
            byte[] buffer = new MD5CryptoServiceProvider().ComputeHash(Encoding.UTF8.GetBytes(inputStr));
            StringBuilder builder = new StringBuilder();
            foreach (byte num in buffer)
            {
                builder.Append(num.ToString("x").PadLeft(2, '0'));
            }
            return builder.ToString();
        }
    }
}