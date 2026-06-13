using System;
using System.Collections.Generic;
using HundunWorld.Game.UI.Enums;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.States
{
    /// <summary>
    /// 状态快照类型枚举
    /// </summary>
    

    /// <summary>
    /// 状态快照
    /// 用于保存UI状态的完整快照，支持状态恢复和回退功能
    /// </summary>
    [Serializable]
    public class StateSnapshot
    {
        /// <summary>
        /// 快照唯一标识
        /// </summary>
        public string SnapshotId { get; set; } = "";

        /// <summary>
        /// 快照名称/描述
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 详细描述
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 快照类型
        /// </summary>
        public SnapshotType Type { get; set; } = SnapshotType.Automatic;

        /// <summary>
        /// UI状态快照
        /// </summary>
        public UIState UIState { get; set; } = new UIState();

        /// <summary>
        /// 各场景状态快照
        /// </summary>
        public Dictionary<SceneType, SceneState> SceneStates { get; set; } = 
            new Dictionary<SceneType, SceneState>();

        /// <summary>
        /// 当前切换状态快照（如果存在）
        /// </summary>
        public TransitionState TransitionState { get; set; } = null;

        /// <summary>
        /// 快照创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 快照版本号
        /// </summary>
        public long Version { get; set; } = 1;

        /// <summary>
        /// 快照优先级（用于清理时的保留策略）
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// 是否为关键快照（不会被自动清理）
        /// </summary>
        public bool IsCritical { get; set; } = false;

        /// <summary>
        /// 快照过期时间（可选）
        /// </summary>
        public DateTime? ExpirationTime { get; set; } = null;

        /// <summary>
        /// 快照标签（用于分类和检索）
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// 快照元数据
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 快照大小估算（字节）
        /// </summary>
        public long EstimatedSizeBytes { get; set; } = 0;

        /// <summary>
        /// 快照校验和（用于完整性验证）
        /// </summary>
        public string Checksum { get; set; } = "";

        /// <summary>
        /// 构造函数
        /// </summary>
        public StateSnapshot()
        {
            SnapshotId = GenerateSnapshotId();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">快照名称</param>
        /// <param name="type">快照类型</param>
        public StateSnapshot(string name, SnapshotType type = SnapshotType.Manual)
        {
            SnapshotId = GenerateSnapshotId();
            Name = name;
            Type = type;
        }

        /// <summary>
        /// 创建StateSnapshot的深拷贝
        /// </summary>
        /// <returns>StateSnapshot的副本</returns>
        public StateSnapshot Clone()
        {
            var clonedSceneStates = new Dictionary<SceneType, SceneState>();
            foreach (var kvp in SceneStates)
            {
                clonedSceneStates[kvp.Key] = kvp.Value?.Clone();
            }

            return new StateSnapshot
            {
                SnapshotId = this.SnapshotId,
                Name = this.Name,
                Description = this.Description,
                Type = this.Type,
                UIState = this.UIState?.Clone(),
                SceneStates = clonedSceneStates,
                TransitionState = this.TransitionState?.Clone(),
                CreatedTime = this.CreatedTime,
                Version = this.Version,
                Priority = this.Priority,
                IsCritical = this.IsCritical,
                ExpirationTime = this.ExpirationTime,
                Tags = new List<string>(this.Tags),
                Metadata = new Dictionary<string, object>(this.Metadata),
                EstimatedSizeBytes = this.EstimatedSizeBytes,
                Checksum = this.Checksum
            };
        }

        /// <summary>
        /// 生成快照ID
        /// </summary>
        /// <returns>唯一的快照ID</returns>
        private static string GenerateSnapshotId()
        {
            return $"snapshot_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
        }

        /// <summary>
        /// 添加标签
        /// </summary>
        /// <param name="tag">标签</param>
        public void AddTag(string tag)
        {
            if (!string.IsNullOrEmpty(tag) && !Tags.Contains(tag))
            {
                Tags.Add(tag);
            }
        }

        /// <summary>
        /// 移除标签
        /// </summary>
        /// <param name="tag">标签</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveTag(string tag)
        {
            return Tags.Remove(tag);
        }

        /// <summary>
        /// 检查是否包含标签
        /// </summary>
        /// <param name="tag">标签</param>
        /// <returns>是否包含该标签</returns>
        public bool HasTag(string tag)
        {
            return Tags.Contains(tag);
        }

        /// <summary>
        /// 设置元数据
        /// </summary>
        /// <param name="key">元数据键</param>
        /// <param name="value">元数据值</param>
        public void SetMetadata(string key, object value)
        {
            Metadata[key] = value;
        }

        /// <summary>
        /// 获取元数据
        /// </summary>
        /// <typeparam name="T">元数据类型</typeparam>
        /// <param name="key">元数据键</param>
        /// <returns>元数据值，如果不存在则返回默认值</returns>
        public T GetMetadata<T>(string key)
        {
            if (Metadata.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default(T);
        }

        /// <summary>
        /// 检查快照是否过期
        /// </summary>
        /// <returns>是否过期</returns>
        public bool IsExpired()
        {
            return ExpirationTime.HasValue && DateTime.UtcNow > ExpirationTime.Value;
        }

        /// <summary>
        /// 设置过期时间
        /// </summary>
        /// <param name="expirationTime">过期时间</param>
        public void SetExpiration(DateTime expirationTime)
        {
            ExpirationTime = expirationTime;
        }

        /// <summary>
        /// 设置过期时间（相对于当前时间）
        /// </summary>
        /// <param name="timeSpan">过期时间间隔</param>
        public void SetExpirationRelative(TimeSpan timeSpan)
        {
            ExpirationTime = DateTime.UtcNow.Add(timeSpan);
        }

        /// <summary>
        /// 计算快照大小估算
        /// </summary>
        /// <returns>估算的快照大小（字节）</returns>
        public long CalculateEstimatedSize()
        {
            long size = 0;

            // 基础字段大小估算
            size += 100; // SnapshotId, Name, Description等字符串
            size += 50;  // 其他基础字段

            // UIState大小估算
            if (UIState != null)
            {
                size += 200; // 基础UIState字段
                size += UIState.SceneData?.Count * 50 ?? 0; // SceneData估算
                size += UIState.Characters?.Count * 100 ?? 0; // Characters估算
            }

            // SceneStates大小估算
            size += SceneStates?.Count * 150 ?? 0;

            // TransitionState大小估算
            if (TransitionState != null)
            {
                size += 100; // 基础TransitionState字段
                size += TransitionState.Parameters?.Count * 30 ?? 0;
            }

            // Tags和Metadata大小估算
            size += Tags?.Count * 20 ?? 0;
            size += Metadata?.Count * 40 ?? 0;

            EstimatedSizeBytes = size;
            return size;
        }

        /// <summary>
        /// 生成快照校验和
        /// </summary>
        /// <returns>快照内容的校验和</returns>
        public string GenerateChecksum()
        {
            // 简单的内容哈希实现
            var content = $"{SnapshotId}_{Version}_{CreatedTime:O}";
            content += $"_{UIState?.Version}_{SceneStates?.Count}";
            content += $"_{TransitionState?.TransitionId}";

            var hash = content.GetHashCode();
            Checksum = hash.ToString("X8");
            return Checksum;
        }

        /// <summary>
        /// 验证快照完整性
        /// </summary>
        /// <returns>是否完整</returns>
        public bool ValidateIntegrity()
        {
            if (string.IsNullOrEmpty(Checksum))
            {
                return false;
            }

            var currentChecksum = GenerateChecksum();
            return string.Equals(Checksum, currentChecksum, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取快照摘要信息
        /// </summary>
        /// <returns>快照摘要</returns>
        public string GetSummary()
        {
            var summary = $"快照 [{SnapshotId}]\n";
            summary += $"名称: {Name}\n";
            summary += $"类型: {Type}\n";
            summary += $"创建时间: {CreatedTime:yyyy-MM-dd HH:mm:ss}\n";
            summary += $"当前场景: {UIState?.CurrentScene}\n";
            summary += $"场景数量: {SceneStates?.Count ?? 0}\n";
            summary += $"大小: {EstimatedSizeBytes} 字节\n";
            summary += $"关键快照: {(IsCritical ? "是" : "否")}\n";
            
            if (ExpirationTime.HasValue)
            {
                summary += $"过期时间: {ExpirationTime.Value:yyyy-MM-dd HH:mm:ss}\n";
            }

            if (Tags.Count > 0)
            {
                summary += $"标签: {string.Join(", ", Tags)}\n";
            }

            return summary;
        }
    }
}
