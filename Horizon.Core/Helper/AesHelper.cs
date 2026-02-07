using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core;
using Horizon.Core.Abstract;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// Aes加/解密 初级加密
    /// </summary>   
    public class AesHelper
    {
        private const string IV = "K834KSONV61LSAJV";
        public const string AesKey = "zLKrO8fjzLKrO8fjzLKrO8fj";
        #region 字段
        /// <summary>
        /// MD5加密的字符串
        /// </summary>
        private string md5Str = null;
        /// <summary>
        /// DES加密的字符串
        /// </summary>
        private string encryptStr = null;
        /// <summary>
        /// DES解密的字符串
        /// </summary>
        private string aecryptStr = null;
        /// <summary>
        /// DES密匙
        /// </summary>
        private string longaesKey = null;
        /// <summary>
        /// 返回的字符串
        /// </summary>
        private string longaesStr = null;
        /// <summary>
        /// 错误信息
        /// </summary>
        private string messAge = null;
        #endregion
        #region 公共属性
        /// <summary>
        /// MD5加密字符串
        /// </summary>
        public string MD5Str
        {
            get { return md5Str; }
            set { md5Str = value; }
        }
        /// <summary>
        /// Aes加密的字符串
        /// </summary>
        public string EncryptStr
        {
            get { return encryptStr; }
            set { encryptStr = value; }
        }
        /// <summary>
        ///Aes 解密的字符串
        /// </summary>
        public string DecryptStr
        {
            get { return aecryptStr; }
            set { aecryptStr = value; }
        }
        /// <summary>
        /// Aes对称密匙
        /// </summary>
        public string LongAesKey
        {
            get { return longaesKey; }
            set { longaesKey = value; }
        }
        /// <summary>
        /// 返回的字符串
        /// </summary>
        public string LongAesStr
        {
            get { return longaesStr; }
            set { longaesStr = value; }
        }
        /// <summary>
        /// 错误信息
        /// </summary>
        public string Message
        {
            get { return messAge; }
            set { messAge = value; }
        }
        #endregion

        /// <summary>
        /// 执行Aes加密
        /// </summary>
        public string AesEncrypt(string password)
        {
            try
            {
                byte[] MyStr_E = Encoding.UTF8.GetBytes(password);
                byte[] MyKey_E = Encoding.UTF8.GetBytes(this.longaesKey);
                byte[] Mykey_IV = Encoding.UTF8.GetBytes(IV);
                AesCryptoServiceProvider MyDes_E = new AesCryptoServiceProvider();
                MyDes_E.Key = MyKey_E;
                MyDes_E.IV = Mykey_IV;
                MemoryStream MyMem_E = new MemoryStream();
                CryptoStream MyCry_E = new CryptoStream(MyMem_E, MyDes_E.CreateEncryptor(), CryptoStreamMode.Write);
                MyCry_E.Write(MyStr_E, 0, MyStr_E.Length);
                MyCry_E.FlushFinalBlock();
                MyCry_E.Close();
                return Convert.ToBase64String(MyMem_E.ToArray());
            }
            catch (Exception Error)
            {
                Log.Error(Log.CommRepository, Error.Message);
                return string.Empty;
            }
        }
        /// <summary>
        /// 执行Aes加密
        /// </summary>
        public void AesEncrypt()
        {
            try
            {
                byte[] MyStr_E = Encoding.UTF8.GetBytes(this.encryptStr);
                byte[] MyKey_E = Encoding.UTF8.GetBytes(this.longaesKey);
                byte[] Mykey_IV = Encoding.UTF8.GetBytes(IV);
                AesCryptoServiceProvider MyDes_E = new AesCryptoServiceProvider();
                MyDes_E.Key = MyKey_E;
                MyDes_E.IV = Mykey_IV;
                MemoryStream MyMem_E = new MemoryStream();
                CryptoStream MyCry_E = new CryptoStream(MyMem_E, MyDes_E.CreateEncryptor(), CryptoStreamMode.Write);
                MyCry_E.Write(MyStr_E, 0, MyStr_E.Length);
                MyCry_E.FlushFinalBlock();
                MyCry_E.Close();
                this.longaesStr = Convert.ToBase64String(MyMem_E.ToArray());
            }
            catch (Exception Error)
            {
                Log.Error(Log.CommRepository, Error.Message);
            }
        }
        /// <summary>
        /// 执行Aes解密
        /// </summary>
        public void AesDecrypt()
        {
            try
            {
                byte[] MyStr_D = Convert.FromBase64String(this.aecryptStr);
                byte[] MyKey_D = Encoding.UTF8.GetBytes(this.longaesKey);
                byte[] Mykey_IV = Encoding.UTF8.GetBytes(IV);
                AesCryptoServiceProvider MyDes_D = new AesCryptoServiceProvider();
                MyDes_D.Key = MyKey_D;
                MyDes_D.IV = Mykey_IV;
                MemoryStream MyMem_D = new MemoryStream();
                CryptoStream MyCry_D = new CryptoStream(MyMem_D, MyDes_D.CreateDecryptor(), CryptoStreamMode.Write);
                MyCry_D.Write(MyStr_D, 0, MyStr_D.Length);
                MyCry_D.FlushFinalBlock();
                MyCry_D.Close();
                this.longaesStr = Encoding.UTF8.GetString(MyMem_D.ToArray());
            }
            catch (Exception Error)
            {
                Log.Error(Log.CommRepository, Error.Message);
            }
        }

    }
}
