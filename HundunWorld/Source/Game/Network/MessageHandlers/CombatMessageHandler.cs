using Arch.Core;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 战斗消息处理器
    /// </summary>
    public class CombatMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes=>new List<MessageType> {  MessageType.ComboAttack, MessageType.SkillCooldown};

        public override ServiceType ServiceType => ServiceType.Game;

       

        public event Action<DamageMessage> DamageReceived;
        public event Action<DeathMessage> DeathReceived;
        public event Action<ResurrectMessage> ResurrectReceived;

        public CombatMessageHandler() : base(MessageType.Damage)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            switch (message.Header.MessageType)
            {
                case MessageType.Damage:
                    if (message.Body is DamageMessage damageMessage)
                    {
                        DamageReceived?.Invoke(damageMessage);
                    }
                    break;

                case MessageType.Death:
                    if (message.Body is DeathMessage deathMessage)
                    {
                        DeathReceived?.Invoke(deathMessage);
                        // Create a world and an entity with position and velocity.添加用户数据组件（ECS 组件）
                        using var world = World.Create();
                        var adventurer = world.Create(new PositionComponent(0, 0,0), new VelocityComponent(1, 1,1));
                       
                        // Enumerate all entities with Position & Velocity to move them
                        var query = new QueryDescription().WithAll<PositionComponent, VelocityComponent>();
                        world.Query(in query, (Entity entity, ref Position pos, ref VelocityComponent vel) => {
                            
                            pos.X += vel.Velocity.X;
                            pos.Y += vel.Velocity.Y;
                            Console.WriteLine($"Moved adventurer: {entity.Id}");
                        });
                    }
                    break;

                case MessageType.Resurrect:
                    if (message.Body is ResurrectMessage resurrectMessage)
                    {
                        ResurrectReceived?.Invoke(resurrectMessage);
                    }
                    break;

                default:
                    Debug.Log($"未处理的战斗消息类型: {message.Header.MessageType}");
                    break;
            }

            await Task.CompletedTask;
        }
    }
}