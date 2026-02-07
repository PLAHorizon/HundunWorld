using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 应用程序配置模型
    /// </summary>
    [Serializable]
    public class AppSettingsModel
    {
        public Logging Logging { get; set; }
        public DataBase DataBase { get; set; }
        public Configs Configs { get; set; }

    }
    /// <summary>
    /// 数据库信息
    /// </summary>
    [Serializable]
    public class DataBase
    {
        /// <summary>
        ///  数据库连接字符串
        /// </summary>
        public string SystemConnectString { get; set; }
        /// <summary>
        /// Orleans Clustering 集群状态同步数据库连接字符串
        /// </summary>
        public string OrleansConnectString { get; set; }
        /// <summary>
        /// 服务区分库
        /// </summary>
        public DistrictDatabaseModel[] DistrictDatabases { get; set; }
        /// <summary>
        /// 历史服务区分库
        /// </summary>
        public DistrictDatabaseModel[] HDistrictDatabases { get; set; }
        /// <summary>
        /// Redis 主服务
        /// </summary>
        public RedisModel[] RedisMasters { get; set; }
        /// <summary>
        /// Redis 从服务
        /// </summary>
        public RedisModel[] RedisSlaves { get; set; }
        /// <summary>
        /// Redis 哨兵服务
        /// </summary>
        public RedisModel[] RedisSentinels { get; set; }
    }
    /// <summary>
    /// 服务区 分库
    /// </summary>
    [Serializable]
    public class DistrictDatabaseModel
    {
        /// <summary>
        /// 分库Id
        /// </summary>
        public Guid Id { get; set; }
        /// <summary>
        ///数据应用类型
        /// </summary>
        public AppType AppType { get; set; }
        /// <summary>
        /// 应用Id
        /// </summary>
        public long APPId { get; set; }
        /// <summary>
        /// 区域Id
        /// </summary>
        public long AreaId { get; set; }
        /// <summary>
        /// 服务Id
        /// </summary>
        public long ServerId { get; set; }
        /// <summary>
        /// 数据库连接字符串
        /// </summary>
        public string ConnectString { get; set; }

    }

    [Serializable]
    public class RedisModel
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Password { get; set; }
    }
    [Serializable]
    public class Configs
    {
        /// <summary>
        ///  
        /// </summary>
        public AliYun Ali { get; set; }
        public BuiDu BuiDu { get; set; }

        public HorizonSelf HorizonSelf { get; set; }
        /// <summary>
        /// 客户端数据任务配置
        /// </summary>
        public ClientDbTaskConfg ClientDbTaskConfg { get; set; }
    }
    /// <summary>
    /// 阿里云
    /// </summary>
    [Serializable]
    public class AliYun
    {


        public string AliOSSAccessKeyId { get; set; }
        public string AliOSSAccessKeySecret { get; set; }
        public string AliOSSPrivateEndpoint { get; set; }
        public string AliOSSFileServerDomain { get; set; }
        public string AliOSSBucketName { get; set; }
        public string AliOSSImageServerDomain { get; set; }
    }
    /// <summary>
    /// 百度
    /// </summary>
    [Serializable]
    public class BuiDu
    {

        public string BaiDuApiKey { get; set; }
        public string BaiDuSecretKey { get; set; }
    }
    /// <summary>
    /// 采集客户端数据配置
    /// </summary>
    [Serializable]
    public class ClientDbTaskConfg
    {
        /// <summary>
        /// 多长时间清理一次已上报成功的数据，单位小时
        /// </summary>
        public int ClearHours { get; set; }
        /// <summary>
        /// 多长时间上报一次数据，单位分钟
        /// </summary>
        public int UploadMinute { get; set; }
        /// <summary>
        /// 多长时间上报一次数据，单位秒
        /// </summary>
        public int UploadSecond { get; set; }
        /// <summary>
        /// 是否使用秒级别上报数据
        /// </summary>
        public bool IsSecond { get; set; }
    }
    /// <summary>
    /// 应用程序本身
    /// </summary>
    [Serializable]
    public class HorizonSelf
    {

        /// <summary>
        /// 通用日志仓库
        /// </summary>
        public string LogRepository { get; set; }
        /// <summary>
        /// Api 日志仓库
        /// </summary>
        public string ApiLogRepository { get; set; }
        /// <summary>
        /// 纬度
        /// </summary>
        public decimal Latitude { get; set; }
        /// <summary>
        /// 经度
        /// </summary>
        public decimal Longitude { get; set; }
        /// <summary>
        /// 容量服务启用标识
        /// </summary>
        public bool IsOpen { get; set; }
        /// <summary>
        /// 服务半径
        /// </summary>
        public int Radius { get; set; }
        /// <summary>
        /// 图片存储域名地址
        /// </summary>
        public string ImageServerDomain { get; internal set; }
        public long AppId { get; set; }
        public long AreaId { get; set; }
        public long ServerId { get; set; }
        public AppType AppType { get; set; }
        public object SocketIP { get; set; }
    }

    [Serializable]
    public class Logging
    {
        public bool IncludeScopes { get; set; }
        public LogLevel LogLevel { get; set; }
    }
    [Serializable]
    public class LogLevel
    {
        public string Default { get; set; }
    }
}
