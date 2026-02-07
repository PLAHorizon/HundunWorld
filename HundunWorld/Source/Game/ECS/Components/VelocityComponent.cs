using Arch.Core.Utils;
using FlaxEngine;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 速度组件，用于存储实体的移动速度信息
    /// </summary>
    public struct VelocityComponent
    {
        /// <summary>
        /// 移动速度向量
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="x">X轴速度</param>
        /// <param name="y">Y轴速度</param>
        /// <param name="z">Z轴速度</param>
        public VelocityComponent(float x, float y, float z)
        {
            Velocity = new Vector3(x, y, z);
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="velocity">速度向量</param>
        public VelocityComponent(Vector3 velocity)
        {
            Velocity = velocity;
        }

        public override string ToString()
        {
            return $"Velocity({Velocity.X}, {Velocity.Y}, {Velocity.Z})";
        }
    }
}