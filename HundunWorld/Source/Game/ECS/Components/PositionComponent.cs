using Arch.Core.Utils;
using FlaxEngine;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 位置组件，用于存储实体在3D空间中的位置信息
    /// </summary>
    public struct PositionComponent 
    {
        /// <summary>
        /// 世界坐标
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="x">X坐标</param>
        /// <param name="y">Y坐标</param>
        /// <param name="z">Z坐标</param>
        public PositionComponent(float x, float y, float z)
        {
            Position = new Vector3(x, y, z);
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="position">位置向量</param>
        public PositionComponent(Vector3 position)
        {
            Position = position;
        }

        public override string ToString()
        {
            return $"Position({Position.X}, {Position.Y}, {Position.Z})";
        }
    }
}