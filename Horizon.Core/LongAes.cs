using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Horizon.Core
{
    /// <summary>
    /// Aes加/解密
    /// </summary>   
    public unsafe class LongAes
    {

        #region 私有属性
        /// <summary>
        /// 盐
        /// </summary>
        private string iv = "LCF52020LCF52020";
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
        /// 盐
        /// </summary>
        public string IV
        { get { return iv; } set { iv = value; } }
        /// <summary>
        /// MD5加密字符串
        /// </summary>
        public string MD5Str
        {
            get { return md5Str; }
            set { md5Str = value; }
        }
        /// <summary>
        /// Aes待加密的字符串
        /// </summary>
        public string EncryptStr
        {
            get { return encryptStr; }
            set { encryptStr = value; }
        }
        /// <summary>
        ///Aes 待解密的字符串
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
        #region 公共方法
        /// <summary>
        /// 执行Aes加密
        /// </summary>
        public void AesEncrypt()
        {
            try
            {

                byte[] MyStr_E = Encoding.UTF8.GetBytes(this.encryptStr);
                byte[] MyKey_E = Encoding.UTF8.GetBytes(this.longaesKey);
                byte[] Mykey_IV = Encoding.UTF8.GetBytes(this.iv);
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
                //this.messAge = "DES加密出错：" + Error.Message;
                this.messAge = "密码使用了不可用的字符，请检查！" + Error.Message;
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
                byte[] Mykey_IV = Encoding.UTF8.GetBytes(this.iv);
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
                //this.messAge = "DES解密出错：" + Error.Message;
                this.messAge = "密码使用了不可用的字符，请检查！" + Error.Message;
            }
        }

        /// <summary>
        /// 获取或设置Aes 加密的Key 和 Iv 
        /// </summary>
        /// <param name="source">动态产生Key和Iv的源字符串</param>
        /// <param name="key">加密的对称钥匙</param>
        /// <param name="iv">加密的而外信息</param>
        public static void KeyIv(string source, out string key, out string iv)
        {
            long s = 0;
            if (source == null)
                source = PassportHelper.GetPassportID(s, 6, 16).ToString();
            key = source;
            if (key.Length < 16)
                key = key.PadRight(16, 'F');
            else
                key = GetSHA1MachineCode(key).Length < 16 ? GetSHA1MachineCode(key).PadRight(16, 'F') : GetSHA1MachineCode(key).Substring(0, 16);
            iv = GetSHA1MachineCode(key).Length < 16 ? GetSHA1MachineCode(key).PadRight(16, 'A') : GetSHA1MachineCode(key).Substring(0, 16);
            //if (iv.Length < 16)
            //    iv = iv.PadRight(16, 'A');
            //else
            //    iv = iv.Substring(0, 16);
        }
        /// <summary>
        /// 为网站用户提供加密服务
        /// </summary>
        /// <param name="source">通行证ID</param>
        /// <param name="pas">网站的明文密码</param>
        /// <returns></returns>
        public static string SetPasswordOfWeb(string source, string pas)
        {
            Long_Aes des = new Long_Aes();
            des.EncryptStr = pas;
            des.LongAesKey = Long_Aes.AesKey;
            des.AesEncrypt();
            pas = des.LongAesStr;//初次加密
            return SetPassword(source, pas);
        }
        /// <summary>
        /// 获取加密后的密码
        /// </summary>
        /// <param name="source">动态产生Key和Iv的源字符串</param>
        /// <param name="key">加密的对称钥匙</param>
        /// <param name="iv">加密的而外信息</param>
        /// <param name="pas">明文的密码</param>
        /// <returns>返回经过加密处理得到的密码密文</returns>
        public static string SetPassword(string source, string pas)
        {
            long s = 0;
            LongAes longaes = new LongAes();
            string key, iv;
            int length = source.Length;
            int chineseLength = 0;
            //中文字符做为密匙时处理为 字符编码值之和
            int chinese = 0;
            while (Regex.IsMatch(source, "[\u4e00-\u9fa5]", RegexOptions.IgnoreCase))
            {
                Match match = Regex.Match(source, "[\u4e00-\u9fa5]", RegexOptions.IgnoreCase);
                source = source.Replace(match.Value, "aa");//替换，可以偏移
                chinese += (int)match.Value.ToArray().FirstOrDefault();
                chineseLength++;
            }
            if (chinese > 0 && length == chineseLength)
                source = chinese.ToString();
            if (source == null)
                source = PassportHelper.GetPassportID(s, 6, 16).ToString();
            key = source;
            if (key.Length < 16)
                key = key.PadRight(16, 'F');
            else
                key = GetSHA1MachineCode(key).Length < 16 ? GetSHA1MachineCode(key).PadRight(16, 'F') : GetSHA1MachineCode(key).Substring(0, 16);
            iv = GetSHA1MachineCode(key);
            if (iv.Length < 16)
                iv = iv.PadRight(16, 'A');
            else
                iv = iv.Substring(0, 16);

            longaes.IV = iv;
            longaes.LongAesKey = key;
            longaes.EncryptStr = pas;
            longaes.AesEncrypt();

            return longaes.LongAesStr;
        }
        /// <summary>
        /// 获取加密后的密码
        /// </summary>
        /// <param name="source">需要生成的源字符串，为空则传入，输出特定的字符串(用户ParssportID)</param>
        /// <param name="key">加密的对称钥匙</param>
        /// <param name="iv">加密的而外信息</param>
        /// <param name="pas">明文的密码</param>
        /// <returns>返回经过加密处理得到的密码密文</returns>
        public static string SetPassword(out string source, string pas)
        {
            long s = 0;
            LongAes longaes = new LongAes();
            string key, iv;
            source = PassportHelper.GetPassportID(s, 9, 11).ToString();
            key = source;
            if (key.Length < 16)
                key = key.PadRight(16, 'F');
            else
                key = GetSHA1MachineCode(key).Length < 16 ? GetSHA1MachineCode(key).PadRight(16, 'F') : GetSHA1MachineCode(key).Substring(0, 16);
            iv = GetSHA1MachineCode(key);
            if (iv.Length < 16)
                iv = iv.PadRight(16, 'A');
            else
                iv = iv.Substring(0, 16);

            longaes.IV = iv;
            longaes.LongAesKey = key;
            longaes.EncryptStr = pas;
            longaes.AesEncrypt();

            return longaes.LongAesStr;
        }
        /// <summary>
        /// 执行MD5加密
        /// </summary>
        public static string MD5JiaMi(string md5Source)
        {
            MD5CryptoServiceProvider MyMD5 = new MD5CryptoServiceProvider();
            try
            {
                Byte[] MyMD5_Str = MyMD5.ComputeHash(Encoding.UTF8.GetBytes(md5Source));
                return Encoding.UTF8.GetString(MyMD5_Str);
            }
            catch
            {
                return "MD5EntCryptoError";
            }
        }

        /// <summary>
        /// 执行MD5加密
        /// </summary>
        public void MD5JiaMi()
        {
            MD5CryptoServiceProvider MyMD5 = new MD5CryptoServiceProvider();
            try
            {
                Byte[] MyMD5_Str = MyMD5.ComputeHash(Encoding.UTF8.GetBytes(this.md5Str));
                this.LongAesStr = Encoding.UTF8.GetString(MyMD5_Str);
            }
            catch (Exception Error)
            {
                this.messAge = "MD5加密出错：" + Error.Message;
            }
        }
        /// <summary>
        /// 获得文件的MD5 值
        /// </summary>
        /// <param name="fullFileName"> 要MD5的文件路径</param>
        /// <returns>返回一个MD5值</returns>
        public static string GetMD5HashFromFile(string fullFileName)
        {
            StringBuilder sb = new StringBuilder();
            using (FileStream file = new FileStream(fullFileName, FileMode.Open))
            {
                System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
                byte[] retVal = md5.ComputeHash(file);
                file.Close();
                for (int i = 0; i < retVal.Length; i++)
                {
                    sb.Append(retVal[i].ToString("x2"));
                }
            }
            return sb.ToString();

        }
        #endregion
        ///获取磁盘序列号 
        /// <summary>
        /// 获取磁盘序列号
        /// </summary>
        public static string GetHardDiskID()
        {
            string hardDiskId = string.Empty;
            try
            {
                ManagementObjectSearcher cmicWmi = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                UInt32 tmpUint32 = 0;
                foreach (ManagementObject cmicWmiObj in cmicWmi.Get())
                {
                    tmpUint32 = Convert.ToUInt32(cmicWmiObj["signature"].ToString());
                }
                hardDiskId = tmpUint32.ToString();
            }
            catch { }
            return hardDiskId;
        }

        ///获取cpu序列号
        /// <summary>
        /// 获取cpu序列号
        /// </summary>
        public unsafe static string GetCPUID()
        {
            string cpuId = string.Empty;
            try
            {
                ManagementObjectSearcher Wmi = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (ManagementObject WmiObj in Wmi.Get())
                {
                    cpuId = WmiObj["ProcessorId"].ToString();
                }
            }
            catch { }
            return cpuId;
        }


        /// <summary>
        /// 获取经过SHA1哈希之后的字符串
        /// </summary>
        public unsafe static string GetSHA1MachineCode(string code)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(code);//特定编码格式
            SHA1 sha1 = SHA1.Create();
            return BitConverter.ToString(sha1.ComputeHash(buffer)).Replace("-", "");
        }
    }


    /// <summary>
    /// Aes加/解密 初级加密，密码可用于保存在客户端使用
    /// </summary>   
    public class Long_Aes
    {
        private const string IV = "LCF52020LCF52020";
        public const string AesKey = "zLKrO8fjzLKrO8fjzLKrO8fj";
        #region 私有属性
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
                //this.messAge = "DES加密出错：" + Error.Message;
                this.messAge = "密码使用了不可用的字符，请检查！" + Error.Message;
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
                //this.messAge = "DES加密出错：" + Error.Message;
                this.messAge = "密码使用了不可用的字符，请检查！" + Error.Message;
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
                //this.messAge = "DES解密出错：" + Error.Message;
                this.messAge = "登录出错，请重试！" + Error.Message;
            }
        }
        /// <summary>
        /// 执行MD5加密
        /// </summary>
        public void MD5JiaMi()
        {
            MD5CryptoServiceProvider MyMD5 = new MD5CryptoServiceProvider();
            try
            {
                Byte[] MyMD5_Str = MyMD5.ComputeHash(Encoding.UTF8.GetBytes(this.md5Str));
                this.LongAesStr = Encoding.UTF8.GetString(MyMD5_Str);
            }
            catch (Exception Error)
            {
                this.messAge = "MD5加密出错：" + Error.Message;
            }
        }

    }
}
