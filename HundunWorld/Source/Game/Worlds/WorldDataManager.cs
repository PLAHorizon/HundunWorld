using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HundunWorld.Game.Worlds
{
    /// <summary>
    /// 世界数据管理器，负责管理游戏世界的数据存储和加载
    /// </summary>
    public class WorldDataManager
    {
        private readonly string _dataDirectory;
        private readonly Dictionary<string, object> _cachedData;

        public WorldDataManager(string dataDirectory)
        {
            _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
            _cachedData = new Dictionary<string, object>();
            
            // 确保数据目录存在
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
            }
        }

        /// <summary>
        /// 保存世界数据
        /// </summary>
        /// <param name="dataKey">数据键</param>
        /// <param name="data">数据对象</param>
        public async Task SaveWorldDataAsync<T>(string dataKey, T data)
        {
            try
            {
                string filePath = Path.Combine(_dataDirectory, $"{dataKey}.dat");
                
                // 这里应该使用适当的序列化方法
                // 由于项目中使用MemoryPack，我们可以使用它来序列化数据
                // byte[] serializedData = MemoryPackSerializer.Serialize(data);
                // await File.WriteAllBytesAsync(filePath, serializedData);
                
                // 缓存数据
                _cachedData[dataKey] = data;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"保存世界数据失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 加载世界数据
        /// </summary>
        /// <param name="dataKey">数据键</param>
        /// <returns>数据对象</returns>
        public async Task<T> LoadWorldDataAsync<T>(string dataKey)
        {
            try
            {
                // 首先检查缓存
                if (_cachedData.ContainsKey(dataKey))
                {
                    return (T)_cachedData[dataKey];
                }
                
                string filePath = Path.Combine(_dataDirectory, $"{dataKey}.dat");
                
                if (!File.Exists(filePath))
                {
                    return default(T);
                }
                
                // 加载并反序列化数据
                // byte[] serializedData = await File.ReadAllBytesAsync(filePath);
                // T data = MemoryPackSerializer.Deserialize<T>(serializedData);
                
                // T data = default(T); // 占位符，实际应使用反序列化
                
                // 缓存数据
                // _cachedData[dataKey] = data;
                
                // return data;
                return default(T); // 占位符返回
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"加载世界数据失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 删除世界数据
        /// </summary>
        /// <param name="dataKey">数据键</param>
        public void DeleteWorldDataAsync(string dataKey)
        {
            try
            {
                string filePath = Path.Combine(_dataDirectory, $"{dataKey}.dat");
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                
                // 从缓存中移除
                if (_cachedData.ContainsKey(dataKey))
                {
                    _cachedData.Remove(dataKey);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"删除世界数据失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 检查数据是否存在
        /// </summary>
        /// <param name="dataKey">数据键</param>
        /// <returns>是否存在</returns>
        public bool DataExists(string dataKey)
        {
            string filePath = Path.Combine(_dataDirectory, $"{dataKey}.dat");
            return File.Exists(filePath) || _cachedData.ContainsKey(dataKey);
        }

        /// <summary>
        /// 获取所有数据键
        /// </summary>
        /// <returns>数据键列表</returns>
        public IEnumerable<string> GetAllDataKeys()
        {
            var dataKeys = new List<string>();
            
            // 从文件系统获取数据键
            if (Directory.Exists(_dataDirectory))
            {
                string[] files = Directory.GetFiles(_dataDirectory, "*.dat");
                foreach (string file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    dataKeys.Add(fileName);
                }
            }
            
            // 添加缓存中的数据键
            foreach (string key in _cachedData.Keys)
            {
                if (!dataKeys.Contains(key))
                {
                    dataKeys.Add(key);
                }
            }
            
            return dataKeys;
        }

        /// <summary>
        /// 清除所有缓存数据
        /// </summary>
        public void ClearCache()
        {
            _cachedData.Clear();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // 清除缓存
            ClearCache();
        }
    }
}