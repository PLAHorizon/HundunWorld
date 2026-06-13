using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 登录相关消息

    /// <summary>
    /// 登录请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class LoginRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 账户名
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string AccountName { get; set; } = "";

        /// <summary>
        /// 密码（Base64编码）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Password { get; set; } = "";

        /// <summary>
        /// 平台ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string PlatformId { get; set; } = "";

        /// <summary>
        /// 设备ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string DeviceId { get; set; } = "";

        /// <summary>
        /// 客户端版本
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string ClientVersion { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.LoginRequest;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }

    /// <summary>
    /// 登录响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class LoginResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 通行证ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string PassportId { get; set; } = "";

        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong UserId { get; set; }
        [MemoryPackOrder(4)]
        [Id(4)] public string UserName { get; set; }
        /// <summary>
        /// 会话令牌
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string SessionToken { get; set; } = "";

        /// <summary>
        /// 角色列表
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public List<CharacterInfo> Characters { get; set; } = new();

        /// <summary>
        /// 服务器列表
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public List<ServerInfo> ServerList { get; set; } = new();

        /// <summary>
        /// 活跃点数
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int ActivityPoints { get; set; }

        /// <summary>
        /// 活跃等级
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int ActivityLevel { get; set; }

        /// <summary>
        /// 状态码
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public int Code { get; set; }

        [MemoryPackOrder(11)]
        [Id(11)]
        public MessageType Type { get; set; } = MessageType.LoginResponse;
        [MemoryPackOrder(12)]
        [Id(12)]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;

        /// <summary>
        /// 用户鉴权令牌
        /// 包含用户登录时间、IP与PassportId的加密数据，客户端需在后续请求头中携带此令牌
        /// </summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public string AuthToken { get; set; } = "";
    }

    #endregion

    #region Token登录相关消息

    [MemoryPackable]
    [GenerateSerializer]
    public partial class TokenLoginRequest : MessageUnion, INetworkMessage
    {
        [MemoryPackOrder(0)] [Id(0)] public string AuthToken { get; set; } = "";
        [MemoryPackOrder(1)] [Id(1)] public string PassportId { get; set; } = "";
        [MemoryPackOrder(2)] [Id(2)] public long UserId { get; set; }
        [MemoryPackOrder(3)] [Id(3)] public string MachineId { get; set; } = "";
        [MemoryPackOrder(4)] [Id(4)] public MessageType Type { get; set; } = MessageType.TokenLoginRequest;
        [MemoryPackOrder(5)] [Id(5)] public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }

    [MemoryPackable]
    [GenerateSerializer]
    public partial class TokenLoginResponse : MessageUnion, INetworkMessage
    {
        [MemoryPackOrder(0)] [Id(0)] public bool IsSuccess { get; set; }
        [MemoryPackOrder(1)] [Id(1)] public string Message { get; set; } = "";
        [MemoryPackOrder(2)] [Id(2)] public string PassportId { get; set; } = "";
        [MemoryPackOrder(3)] [Id(3)] public ulong UserId { get; set; }
        [MemoryPackOrder(4)] [Id(4)] public string SessionToken { get; set; } = "";
        [MemoryPackOrder(5)] [Id(5)] public string AuthToken { get; set; } = "";
        [MemoryPackOrder(6)] [Id(6)] public MessageType Type { get; set; } = MessageType.TokenLoginResponse;
        [MemoryPackOrder(7)] [Id(7)] public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }

    #endregion

    #region 注册相关消息

    /// <summary>
    /// 注册请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class RegisterRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 昵称
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string NickName { get; set; } = "";

        /// <summary>
        /// 密码（Base64编码）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Password { get; set; } = "";

        /// <summary>
        /// 手机号
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string PhoneNumber { get; set; } = "";

        /// <summary>
        /// 邮箱
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Email { get; set; } = "";

        /// <summary>
        /// 平台ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string PlatformId { get; set; } = "";

        /// <summary>
        /// 验证码
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string VerificationCode { get; set; } = "";
        /// <summary>
        /// 身份证姓名
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string RealName { get; set; } = "";
        /// <summary>
        /// 身份证号码
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string ID { get; set; } = "";

        /// <summary>
        /// 客户端版本
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public string ClientVersion { get; set; } = "";

        /// <summary>
        /// 平台类型
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public string Platform { get; set; } = "";

        /// <summary>
        /// 设备ID
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public string DeviceId { get; set; } = "";

        [MemoryPackOrder(11)]
        [Id(11)]
        public MessageType Type { get; set; } = MessageType.RegisterRequest;
        [MemoryPackOrder(12)]
        [Id(12)]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;
        [MemoryPackOrder(13)]
        [Id(13)]
        public string  Ip { get; set; } 
    }

    /// <summary>
    /// 注册响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class RegisterResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// 通行证ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string PassportId { get; set; } = "";

        /// <summary>
        /// 注册时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long RegisterTime { get; set; }

        /// <summary>
        /// 通行证昵称（注册成功后返回，供客户端后续逻辑使用）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string NickName { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.RegisterResponse;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }

    #endregion

    #region 角色管理消息

    /// <summary>
    /// 角色信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CharacterInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 角色名
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string CharacterName { get; set; } = "";

        /// <summary>
        /// 等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Level { get; set; }

        /// <summary>
        /// 职业
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public Profession Profession { get; set; }

        /// <summary>
        /// 性别
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Gender { get; set; }

        /// <summary>
        /// 位置信息
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public Position Position { get; set; } = new();

        /// <summary>
        /// 外观信息
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public AppearanceInfo Appearance { get; set; } = new();

        /// <summary>
        /// 当前血量
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public float CurrentHealth { get; set; }

        /// <summary>
        /// 最大血量
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public float MaxHealth { get; set; }

        /// <summary>
        /// 是否存活
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public bool IsAlive { get; set; } = true;

        /// <summary>
        /// 死亡次数
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public int DeathCount { get; set; }

        /// <summary>
        /// 复活次数
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public int ResurrectionCount { get; set; }

        /// <summary>
        /// 经验值
        /// </summary>
        [MemoryPackOrder(17)]
        [Id(17)]
        public long Experience { get; set; }

        /// <summary>
        /// 金币
        /// </summary>
        [MemoryPackOrder(18)]
        [Id(18)]
        public long Gold { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [MemoryPackOrder(19)]
        [Id(19)]
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 最后受伤时间
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public DateTime LastDamageTime { get; set; }

        /// <summary>
        /// 最后死亡时间
        /// </summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public DateTime LastDeathTime { get; set; }

        /// <summary>
        /// 最后登录时间
        /// </summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public DateTime LastLoginTime { get; set; }

        [MemoryPackOrder(15)]
        [Id(15)]
        public MessageType Type { get; set; } = MessageType.Unknown;
        [MemoryPackOrder(16)]
        [Id(16)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 位置信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class Position : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// X坐标
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public float X { get; set; }

        /// <summary>
        /// Y坐标
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public float Y { get; set; }

        /// <summary>
        /// Z坐标
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float Z { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.Movement;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 外观信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class AppearanceInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 头发模型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int HairModel { get; set; }

        /// <summary>
        /// 头发颜色
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int HairColor { get; set; }

        /// <summary>
        /// 脸型
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int FaceModel { get; set; }

        /// <summary>
        /// 服装
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Clothing { get; set; }
        [MemoryPackOrder(4)]
        [Id(4)] public int HairStyle { get; set; }
        [MemoryPackOrder(5)]
        [Id(5)]
        public int EyeColor { get; set; }
        [MemoryPackOrder(6)]
        [Id(6)]
        public int SkinColor { get; set; }
        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.Appearance;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 创建角色请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CreateCharacterRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 角色名
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string CharacterName { get; set; } = "";

        /// <summary>
        /// 职业
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Profession Profession { get; set; } 

        /// <summary>
        /// 性别
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Gender { get; set; }

        /// <summary>
        /// 外观信息
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public AppearanceInfo Appearance { get; set; } = new();

        /// <summary>
        /// 游戏ID
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int GameId { get; set; }

        /// <summary>
        /// 分区ID
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int ZoneId { get; set; }

        /// <summary>
        /// 服务器ID
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int ServerId { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.CreateCharacter;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 创建角色响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CreateCharacterResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 角色信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public CharacterInfo Character { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.CreateCharacter;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 进入游戏请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EnterGameRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 客户端版本
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ClientVersion { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.EnterGame;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 进入游戏响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class EnterGameResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        // 兼容性属性
        public bool IsSuccess 
        { 
            get => Success; 
            set => Success = value; 
        }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 角色信息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public CharacterInfo CharacterInfo { get; set; } = new();

        /// <summary>
        /// 角色ID（兼容性字段）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.EnterGame;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;

        /// <summary>
        /// 更新后的用户鉴权令牌（含游戏角色Id，客户端需用此令牌替换旧令牌）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string AuthToken { get; set; } = "";
    }

    /// <summary>
    /// 删除角色请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class DeleteCharacterRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong UserId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.CharacterDelete;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 删除角色响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class DeleteCharacterResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.CharacterDelete;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 移动请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MoveRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 目标X坐标
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public float TargetX { get; set; }

        /// <summary>
        /// 目标Y坐标
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float TargetY { get; set; }

        /// <summary>
        /// 目标Z坐标
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float TargetZ { get; set; }

        /// <summary>
        /// 移动速度
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float Speed { get; set; }
        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long Timestamp { get; set; }
        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.Movement;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;

        /// <summary>
        /// 客户端预测序列号，服务端需在响应中原样回传以支持客户端预测缓冲区清理。
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int SequenceNumber { get; set; }

    }

    /// <summary>
    /// 移动响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class MoveResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 当前X坐标
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float CurrentX { get; set; }

        /// <summary>
        /// 当前Y坐标
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float CurrentY { get; set; }

        /// <summary>
        /// 当前Z坐标
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float CurrentZ { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.Movement;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;

        /// <summary>
        /// 服务端已确认的客户端预测序列号（原样回传 <see cref="MoveRequest.SequenceNumber"/>），
        /// 客户端用此值清理已确认的预测缓冲帧。-1 表示服务端不支持此字段。
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int AcknowledgedSequence { get; set; } = -1;
    }

    #endregion

    #region 服务器信息

    /// <summary>
    /// 服务器信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ServerInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 服务器ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int ServerId { get; set; }

        /// <summary>
        /// 分区ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int ZoneId { get; set; }

        /// <summary>
        /// 游戏ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int GameId { get; set; }

        /// <summary>
        /// 服务器名称
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string ServerName { get; set; } = "";

        /// <summary>
        /// 服务器状态
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Status { get; set; }

        /// <summary>
        /// 在线人数
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int OnlineCount { get; set; }

        /// <summary>
        /// 最大在线人数
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int MaxOnlineCount { get; set; }

        /// <summary>
        /// 服务器IP
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string ServerIP { get; set; } = "";

        /// <summary>
        /// 服务器端口
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int ServerPort { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public MessageType Type { get; set; } = MessageType.ServerList;
        [MemoryPackOrder(10)]
        [Id(10)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 会话管理消息

    /// <summary>
    /// 会话信息消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SessionInfoMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 会话ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string SessionId { get; set; } = "";

        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 会话创建时间
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long CreateTime { get; set; }

        /// <summary>
        /// 最后活跃时间
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long LastActiveTime { get; set; }

        /// <summary>
        /// 客户端IP地址
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string ClientIP { get; set; } = "";

        /// <summary>
        /// 平台ID
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string PlatformId { get; set; } = "";

        /// <summary>
        /// 设备ID
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string DeviceId { get; set; } = "";
        /// <summary>
        /// 服务器ID
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int ServerId { get; set; }

        /// <summary>
        /// 分区ID
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int ZoneId { get; set; }

        /// <summary>
        /// 游戏ID
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int GameId { get; set; }
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 会话令牌
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public string Token { get; set; } = "";

        /// <summary>
        /// 过期时间
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public long ExpireTime { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public MessageType Type { get; set; } = MessageType.SessionInfo;
        [MemoryPackOrder(14)]
        [Id(14)]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }

    /// <summary>
    /// 角色列表请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CharacterListRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 服务器ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int ServerId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.CharacterList;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 角色列表响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CharacterListResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 角色列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<CharacterInfo> Characters { get; set; } = new();

        /// <summary>
        /// 错误消息
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// 最大角色数量
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int MaxCharacterCount { get; set; } = 5;

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.CharacterList;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 验证角色名请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ValidateCharacterNameRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色名
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string CharacterName { get; set; } = "";
        [MemoryPackOrder(1)]
        [Id(1)]
        public MessageType Type { get; set; } = MessageType.CharacterNameCheck;
        [MemoryPackOrder(2)]
        [Id(2)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 验证角色名响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ValidateCharacterNameResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否可用
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool IsAvailable { get; set; }

        // 兼容性属性
        public bool IsValid 
        { 
            get => IsAvailable; 
            set => IsAvailable = value; 
        }

        /// <summary>
        /// 验证消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 建议的角色名（如果原名不可用）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<string> SuggestedNames { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.CharacterNameCheck;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 错误和状态消息

    /// <summary>
    /// 认证错误响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class AuthenticationError : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 错误代码
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int ErrorCode { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// 错误详情
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string ErrorDetails { get; set; } = "";

        /// <summary>
        /// 重试间隔（秒）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int RetryAfterSeconds { get; set; }

        /// <summary>
        /// 是否需要重新连接
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool RequiresReconnect { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.Error;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }

    /// <summary>
    /// 区域和服务器信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ZoneAndServerInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 区域列表
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public List<ZoneInfo> Zones { get; set; } = new();

        /// <summary>
        /// 服务器列表
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public List<ServerInfo> Servers { get; set; } = new();

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.ZoneAndServerInfo;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 区域信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ZoneInfo : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 区域ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int ZoneId { get; set; }

        /// <summary>
        /// 区域名称
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string ZoneName { get; set; } = "";

        /// <summary>
        /// 区域描述
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 区域状态
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Status { get; set; }

        /// <summary>
        /// 推荐等级
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int RecommendedLevel { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.ZoneAndServerInfo;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion

    #region 构建游戏用户消息

    /// <summary>
    /// 构建游戏用户请求消息
    /// 当启动游戏时发现不存在游戏用户记录，通过网关主动请求创建
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class BuildGameUserRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 通行证ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string PassportId { get; set; } = "";

        /// <summary>
        /// 游戏ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int GameId { get; set; }

        /// <summary>
        /// 区域ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int AreaId { get; set; }

        /// <summary>
        /// 服务器ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int ServerId { get; set; }

        /// <summary>
        /// 平台ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string PlatformId { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.BuildGameUserRequest;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }

    /// <summary>
    /// 构建游戏用户响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class BuildGameUserResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 游戏用户ID（成功时返回）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long GameUserId { get; set; }

        /// <summary>
        /// 错误消息（失败时返回）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string ErrorMessage { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public MessageType Type { get; set; } = MessageType.BuildGameUserResponse;
        [MemoryPackOrder(4)]
        [Id(4)]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }

    #endregion
}
