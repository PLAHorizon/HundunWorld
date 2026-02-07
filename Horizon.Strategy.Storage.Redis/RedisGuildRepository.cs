using System;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;
using Horizon.Game.Message;

namespace Horizon.Strategy.Storage.Redis
{
    /// <summary>
    /// Redis公会数据存储库
    /// </summary>
    public class RedisGuildRepository
    {
        private readonly IDatabase _database;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="database">Redis数据库实例</param>
        public RedisGuildRepository(IDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        

       

        /// <summary>
        /// 删除公会信息
        /// </summary>
        /// <param name="guildId">公会ID</param>
        /// <returns>删除任务</returns>
        public async Task<bool> DeleteGuildAsync(int guildId)
        {
            try
            {
                var key = $"guild:{guildId}";
                return await _database.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to delete guild {guildId} from Redis", ex);
            }
        }

        /// <summary>
        /// 检查公会是否存在
        /// </summary>
        /// <param name="guildId">公会ID</param>
        /// <returns>是否存在</returns>
        public async Task<bool> ExistsAsync(int guildId)
        {
            try
            {
                var key = $"guild:{guildId}";
                return await _database.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to check if guild {guildId} exists in Redis", ex);
            }
        }
    }
}
