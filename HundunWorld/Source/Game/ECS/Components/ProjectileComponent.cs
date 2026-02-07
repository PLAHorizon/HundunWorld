using Arch.Core;
using FlaxEngine;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 投射物组件
    /// 标识一个实体是飞行的投射物（如箭矢、法术弹道等）
    /// </summary>
    public struct ProjectileComponent 
    {
        public ulong ProjectileId;
        public Vector3 Direction;
        public float Speed;
        public float Lifetime;
        public ulong OwnerId; // 发射者的ID
        
        public ProjectileComponent(ulong projectileId, Vector3 direction, float speed, float lifetime, ulong ownerId)
        {
            ProjectileId = projectileId;
            Direction = direction;
            Speed = speed;
            Lifetime = lifetime;
            OwnerId = ownerId;
        }
    }
}
