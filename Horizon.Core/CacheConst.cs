using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    /// <summary>
    /// 缓存常量
    /// </summary>
    public class CacheConst
    {
        /// <summary>
        /// 前缀
        /// </summary>
        public const string Prefix = "iHuaxiaX";
        /// <summary>
        /// 连接字符
        /// </summary>
        public const string JoinChar = "-";
        /// <summary>
        /// 百度独立位置服务信息缓存键
        /// </summary>
        public const string BAIDULBS = nameof(BAIDULBS);
        /// <summary>
        /// 百度ak
        /// </summary>
        public const string BAIDUAK = "8ke07ezbbK4dpmVKgOjEtifqlVLoFKRf";
        /// <summary>
        /// 百度全球逆地理Get 接口，根据经纬度获取行政区信息
        /// </summary>
        public const string BAIDU_G_L_V2 = "http://api.map.baidu.com/geocoder/v2/?location={1},{2}&output=json&pois=0&ak={0} ";//GET请求
        /// <summary>
        /// 百度全球地理Get 接口,根据地址获取经纬度
        /// </summary>
        public const string BAIDU_G_A_V2 = "http://api.map.baidu.com/geocoder/v2/?address={1}&output=json&ak={0}";

        /// <summary>
        /// SocketEDASIP
        /// </summary>
        public const string SocketEDASIP = "192.168.1.34";
        /// <summary>
        /// Grain Stream  用户订阅流标识键
        /// </summary>
        /// <param name="fullName">IGrain 接口全名称</param>
        /// <param name="guid">用户标识Key</param>
        /// <returns></returns>
        public static string GrainStream(string fullName, Guid guid)
        {
            return $"{fullName}-{guid}";
        }

        /// <summary>
        /// SocketEDASPORT
        /// </summary>
        public const int SocketEDASPORT = 52679;
        /// <summary>
        /// Socket登录端口
        /// </summary>
        public const int LOGINPORT = 22111;
        /// <summary>
        /// Socket测站信息端口
        /// </summary>
        public const int STCDPORT = 22112;
        /// <summary>
        /// Socket登录IP
        /// </summary>
        public const string LOGINPORTIP = "";
        /// <summary>
        /// 成功移除缓存区标识
        /// </summary>
        public const string OUTCACHE = Prefix + JoinChar + nameof(OUTCACHE);
        /// <summary>
        /// 移除缓存区失败标识
        /// </summary>
        public const string FAILOUTCACHE = Prefix + JoinChar + nameof(FAILOUTCACHE);
        /// <summary>
        /// 应用配置信息缓存键
        /// </summary>
        public const string APPSETTINGS = Prefix + JoinChar + nameof(APPSETTINGS);
        /// <summary>
        /// 可用（未注册）通信证集合缓存键
        /// </summary>
        public const string PASSPORTIDPOOLS = Prefix + JoinChar + nameof(PASSPORTIDPOOLS);
        /// <summary>
        ///正常使用（已注册）通信证集合缓存键
        /// </summary>
        public const string PASSPORTSPOOLS = Prefix + JoinChar + nameof(PASSPORTSPOOLS);
        /// <summary>
        /// 注册锁
        /// </summary>
        public const string PASSPORTREGISTERLOCK = Prefix + JoinChar + nameof(PASSPORTREGISTERLOCK);
        /// <summary>
        /// 生成Passport锁
        /// </summary>
        public const string PASSPORTCREATINGLOCK = Prefix + JoinChar + nameof(PASSPORTCREATINGLOCK);
        /// <summary>
        /// 生成Passport 标识
        /// </summary>
        public const string PASSPORTFLAG = Prefix + JoinChar + nameof(PASSPORTFLAG);
        /// <summary>
        /// 已注册过的设备
        /// </summary>
        public const string DEVICES = Prefix + JoinChar + nameof(DEVICES);
        /// <summary>
        ///创建游戏角色
        /// </summary>
        public const string CREATEGAMEROLEE = nameof(CREATEGAMEROLEE);
        /// <summary>
        /// 游戏角色
        /// </summary>
        public const string GAMEROLEE = nameof(GAMEROLEE);

        /// <summary>
        /// 通行证最小位数
        /// </summary>
        public static int PassportLengthMin => 7;
        /// <summary>
        /// 通行证最大位数
        /// </summary>
        public static int PassportLengthMax => 11;
        /// <summary>
        /// APP Socket 通信端口
        /// </summary>
        public static int AppSocketPORT => 18299;
        /// <summary>
        /// WebSocket 通信端口
        /// </summary>
        public static int WebSocketPORT => 18200;

        /// <summary>
        /// 用户验证数据缓存键
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string VerificationUserData(string @type)
        {
            return $"{Prefix}{JoinChar}{nameof(VerificationUserData)}{JoinChar}{@type}";
        }

        /// <summary>
        /// 糖果三消游戏配置键
        /// </summary>
        public const string CandyGameConfiguration = nameof(CandyGameConfiguration);
        /// <summary>
        /// 聊天客户端Sesssion Id Key 标识前缀
        /// </summary>
        public const string IMSocketClientIdKey = nameof(IMSocketClientIdKey);
    }
}
