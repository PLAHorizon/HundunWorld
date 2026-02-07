using Arch.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace HundunWorld.Game.Worlds
{
    /// <summary>
    /// 世界事件类型枚举
    /// </summary>
    public enum WorldEventType
    {
        EntityAdded,
        EntityRemoved,
        EntityUpdated
    }

    /// <summary>
    /// 世界状态类，表示游戏世界的整体状态
    /// </summary>
    public class WorldState
    {
        /// <summary>
        /// 世界时间
        /// </summary>
        public DateTime WorldTime { get; set; }

        /// <summary>
        /// 世界中的所有实体
        /// </summary>
        public Dictionary<ulong, Entity> Entities { get; set; }

        /// <summary>
        /// 世界事件队列
        /// </summary>
        public Queue<WorldEvent> EventQueue { get; set; }

        /// <summary>
        /// 世界边界
        /// </summary>
        public BoundingBox WorldBounds { get; set; }

        public WorldState()
        {
            WorldTime = DateTime.UtcNow;
            Entities = new Dictionary<ulong, Entity>();
            EventQueue = new Queue<WorldEvent>();
            WorldBounds = new BoundingBox(Vector3.Zero, new Vector3(1000, 1000, 1000));
        }

        /// <summary>
        /// 更新世界状态
        /// </summary>
        /// <param name="deltaTime">时间增量</param>
        public void Update(float deltaTime)
        {
            WorldTime = DateTime.UtcNow;
            
            // 处理事件队列
            while (EventQueue.Count > 0)
            {
                WorldEvent worldEvent = EventQueue.Dequeue();
                ProcessEvent(worldEvent);
            }
        }

        /// <summary>
        /// 处理世界事件
        /// </summary>
        /// <param name="worldEvent">世界事件</param>
        private void ProcessEvent(WorldEvent worldEvent)
        {
            // 根据事件类型处理不同的世界事件
            switch (worldEvent.EventType)
            {
                case WorldEventType.EntityAdded:
                    HandleEntityAdded(worldEvent);
                    break;
                case WorldEventType.EntityRemoved:
                    HandleEntityRemoved(worldEvent);
                    break;
                case WorldEventType.EntityUpdated:
                    HandleEntityUpdated(worldEvent);
                    break;
            }
        }

        /// <summary>
        /// 处理实体添加事件
        /// </summary>
        private void HandleEntityAdded(WorldEvent worldEvent)
        {
            // 添加实体到世界
        }

        /// <summary>
        /// 处理实体移除事件
        /// </summary>
        private void HandleEntityRemoved(WorldEvent worldEvent)
        {
            // 从世界移除实体
        }

        /// <summary>
        /// 处理实体更新事件
        /// </summary>
        private void HandleEntityUpdated(WorldEvent worldEvent)
        {
            // 更新实体状态
        }
    }

    /// <summary>
    /// 世界事件类
    /// </summary>
    public class WorldEvent
    {
        /// <summary>
        /// 事件类型
        /// </summary>
        public WorldEventType EventType { get; set; }

        /// <summary>
        /// 事件时间
        /// </summary>
        public DateTime EventTime { get; set; }

        /// <summary>
        /// 相关实体ID
        /// </summary>
        public ulong EntityId { get; set; }

        /// <summary>
        /// 事件数据
        /// </summary>
        public object Data { get; set; }

        public WorldEvent(WorldEventType eventType, ulong entityId, object data = null)
        {
            EventType = eventType;
            EventTime = DateTime.UtcNow;
            EntityId = entityId;
            Data = data;
        }
    }

    /// <summary>
    /// 边界框类
    /// </summary>
    public class BoundingBox
    {
        /// <summary>
        /// 最小点
        /// </summary>
        public Vector3 Min { get; set; }

        /// <summary>
        /// 最大点
        /// </summary>
        public Vector3 Max { get; set; }

        public BoundingBox(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// 检查点是否在边界框内
        /// </summary>
        /// <param name="point">检查点</param>
        /// <returns>是否在边界框内</returns>
        public bool Contains(Vector3 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                   point.Y >= Min.Y && point.Y <= Max.Y &&
                   point.Z >= Min.Z && point.Z <= Max.Z;
        }
    }
}
