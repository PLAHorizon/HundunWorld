using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// Orleans扩展方法，用于在Grain中生成玩家ID
    /// </summary>
    public static class OrleansPlayerIdExtensions
    {
        /// <summary>
        /// 在Grain中生成玩家ID
        /// </summary>
        /// <param name="grain">Grain实例</param>
        /// <returns>唯一玩家ID</returns>
        public static long GeneratePlayerId(this Grain grain)
        {
            return PlayerIdManager.Instance.GeneratePlayerId();
        }
        
        /// <summary>
        /// 在Grain中生成带前缀的玩家ID
        /// </summary>
        /// <param name="grain">Grain实例</param>
        /// <param name="prefix">前缀</param>
        /// <returns>带前缀的玩家ID字符串</returns>
        public static string GeneratePlayerIdString(this Grain grain, string prefix = "PLY")
        {
            return PlayerIdManager.Instance.GeneratePlayerIdString(prefix);
        }
        
        /// <summary>
        /// 在Grain中批量生成玩家ID
        /// </summary>
        /// <param name="grain">Grain实例</param>
        /// <param name="count">生成数量</param>
        /// <returns>ID数组</returns>
        public static long[] GenerateBatchPlayerIds(this Grain grain, int count)
        {
            return PlayerIdManager.Instance.GenerateBatchPlayerIds(count);
        }
        
        /// <summary>
        /// 在Grain中验证玩家ID
        /// </summary>
        /// <param name="grain">Grain实例</param>
        /// <param name="playerId">要验证的玩家ID</param>
        /// <returns>是否有效</returns>
        public static bool ValidatePlayerId(this Grain grain, long playerId)
        {
            return PlayerIdManager.Instance.IsValidPlayerId(playerId);
        }
        
        /// <summary>
        /// 在Grain中获取玩家ID的详细信息
        /// </summary>
        /// <param name="grain">Grain实例</param>
        /// <param name="playerId">玩家ID</param>
        /// <returns>ID信息</returns>
        public static IdInfo GetPlayerIdInfo(this Grain grain, long playerId)
        {
            return PlayerIdManager.Instance.GetIdInfo(playerId);
        }
    }
    
    /// <summary>
    /// 玩家ID相关的常量定义
    /// </summary>
    public static class PlayerIdConstants
    {
        /// <summary>
        /// 默认玩家ID前缀
        /// </summary>
        public const string DefaultPlayerPrefix = "PLY";
        
        /// <summary>
        /// NPC ID前缀
        /// </summary>
        public const string NpcPrefix = "NPC";
        
        /// <summary>
        /// 宠物ID前缀
        /// </summary>
        public const string PetPrefix = "PET";
        
        /// <summary>
        /// 公会ID前缀
        /// </summary>
        public const string GuildPrefix = "GLD";
        
        /// <summary>
        /// 队伍ID前缀
        /// </summary>
        public const string TeamPrefix = "TEAM";
        
        /// <summary>
        /// 房间ID前缀
        /// </summary>
        public const string RoomPrefix = "ROOM";
        
        /// <summary>
        /// 最小有效ID值
        /// </summary>
        public const long MinValidId = 1L;
        
        /// <summary>
        /// 系统保留ID范围上限
        /// </summary>
        public const long SystemReservedIdLimit = 10000L;
    }
    
    /// <summary>
    /// 玩家ID类型枚举
    /// </summary>
    public enum PlayerIdType
    {
        /// <summary>
        /// 普通玩家
        /// </summary>
        Player = 1,
        
        /// <summary>
        /// NPC
        /// </summary>
        Npc = 2,
        
        /// <summary>
        /// 宠物
        /// </summary>
        Pet = 3,
        
        /// <summary>
        /// 系统角色
        /// </summary>
        System = 4
    }
    
    /// <summary>
    /// 玩家ID验证器
    /// </summary>
    public static class PlayerIdValidator
    {
        /// <summary>
        /// 验证玩家ID格式
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        /// <returns>验证结果</returns>
        public static ValidationResult ValidateFormat(long playerId)
        {
            if (playerId <= 0)
            {
                return new ValidationResult(false, "玩家ID必须大于0");
            }
            
            if (playerId <= PlayerIdConstants.SystemReservedIdLimit)
            {
                return new ValidationResult(false, "玩家ID不能使用系统保留范围");
            }
            
            if (!PlayerIdManager.Instance.IsValidPlayerId(playerId))
            {
                return new ValidationResult(false, "玩家ID格式无效");
            }
            
            return new ValidationResult(true, "玩家ID格式有效");
        }
        
        /// <summary>
        /// 验证玩家ID字符串格式
        /// </summary>
        /// <param name="playerIdString">玩家ID字符串</param>
        /// <param name="expectedPrefix">期望的前缀</param>
        /// <returns>验证结果</returns>
        public static ValidationResult ValidateStringFormat(string playerIdString, string expectedPrefix = null)
        {
            if (string.IsNullOrWhiteSpace(playerIdString))
            {
                return new ValidationResult(false, "玩家ID字符串不能为空");
            }
            
            if (expectedPrefix != null && !playerIdString.StartsWith(expectedPrefix))
            {
                return new ValidationResult(false, $"玩家ID字符串必须以'{expectedPrefix}'开头");
            }
            
            // 提取数字部分
            var numericPart = expectedPrefix != null 
                ? playerIdString.Substring(expectedPrefix.Length)
                : playerIdString;
            
            if (!long.TryParse(numericPart, out var playerId))
            {
                return new ValidationResult(false, "玩家ID字符串包含无效的数字格式");
            }
            
            return ValidateFormat(playerId);
        }
    }
    
    /// <summary>
    /// 验证结果
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; }
        
        /// <summary>
        /// 错误消息
        /// </summary>
        public string Message { get; }
        
        /// <summary>
        /// 初始化验证结果
        /// </summary>
        /// <param name="isValid">是否有效</param>
        /// <param name="message">消息</param>
        public ValidationResult(bool isValid, string message)
        {
            IsValid = isValid;
            Message = message;
        }
        
        public override string ToString()
        {
            return $"Valid: {IsValid}, Message: {Message}";
        }
    }
}
