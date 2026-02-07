using Horizon.Core.Abstract;
using Orleans;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 游戏服务器组
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GameServersDto
    {        /// <summary>
             /// 应用类型
             /// </summary>
        [Id(0)] public AppType AppType { get; set; } = AppType.Game;
        /// <summary>
        /// 应用Id
        /// </summary>
        [Id(1)] public int AppId { get; set; }
        /// <summary>
        /// 应用名称
        /// </summary>
        [Id(2)] public string AppName { get; set; }
        /// <summary>
        /// 应用简要说明
        /// </summary>
        [Id(3)] public string AppDescritpion { get; set; }
        /// <summary>
        /// 应用服务器组
        /// </summary>
        [Id(4)] public List<ServerAreaDto> Areas { get; set; }
    }
    [Serializable]
    [GenerateSerializer]
    public class ServerAreaDto
    {
        /// <summary>
        /// 分区Id
        /// </summary>
        [Id(0)] public int Id { get; set; }
        [Id(1)] public int GameId { get; set; }
        /// <summary>
        /// 分区名称
        /// </summary>
        [Id(2)] public string Name { get; set; }
        /// <summary>
        /// 分区简述
        /// </summary>
        [Id(3)] public string Description { get; set; }
        [Id(4)] public List<ServerDto> Servers { get; set; }
    }

    /// <summary>
    /// 服务器组信息
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class ServerDto
    {
        /// <summary>
        ///游戏分区Id
        /// </summary>
        [Id(0)] public int AreaId { get; set; }
        /// <summary>
        /// 游戏Id
        /// </summary>
        [Id(1)] public int GameId { get; set; }
        /// <summary>
        /// 服务其Id
        /// </summary>
        [Id(2)] public int Id { get; set; }
        /// <summary>
        /// 服务器名称
        /// </summary>
        [Id(3)] public string Name { get; set; }
        /// <summary>
        /// 服务器简述
        /// </summary>
        [Id(4)] public string Description { get; set; }
        /// <summary>
        /// 服务器Ip
        /// </summary>
        [Id(5)] public string Ip { get; set; }
        /// <summary>
        /// 服务器端口
        /// </summary>
        [Id(6)] public int Port { get; set; }
        /// <summary>
        /// 区服状态
        /// </summary>
        [Id(7)] public GameAreaServerStatus Status { get; set; }
    }


    /// <summary>
    /// 区服状态
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public enum GameAreaServerStatus
    {
        /// <summary>
        /// 新开服
        /// </summary>
        [EnumMember, Description("新开服")]
        [Id(0)]
        New = -1,

        /// <summary>
        /// 正常
        /// </summary>
        [EnumMember, Description("正常")]
        [Id(1)]
        Normal = 0,
        /// <summary>
        /// 繁忙
        /// </summary>
        [EnumMember, Description("繁忙")]
        [Id(2)]
        Busy = 1,
        /// <summary>
        /// 畅通
        /// </summary>
        [EnumMember, Description("畅通")]
        [Id(3)]
        Idle = 2,
        /// <summary>
        /// 爆满
        /// </summary>
        [EnumMember, Description("爆满")]
        [Id(4)]
        Full = 3,
        /// <summary>
        /// 未开通
        /// </summary>
        [EnumMember, Description("未开通")]
        [Id(5)]
        Unopen = 5,
        /// <summary>
        /// 不可用
        /// </summary>
        [EnumMember, Description("不可用")]
        [Id(6)]
        Disabled = 4
    }
}