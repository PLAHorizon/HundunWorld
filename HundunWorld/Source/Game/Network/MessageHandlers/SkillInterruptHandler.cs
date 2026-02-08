using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 技能打断消息处理器
    /// 处理服务端发来的技能打断通知
    /// </summary>
    public class SkillInterruptHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.SkillInterrupt };

        public override ServiceType ServiceType => ServiceType.Combat;

        public event Action<SkillInterruptMessage> SkillInterrupted;

        public SkillInterruptHandler() : base(MessageType.SkillInterrupt)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is SkillInterruptMessage interruptMessage)
                {
                    FlaxEngine.Debug.Log($"收到技能打断消息: 角色 {interruptMessage.CharacterId} 的技能 {interruptMessage.SkillId} 被打断, 原因: {interruptMessage.Reason}");

                    SkillInterrupted?.Invoke(interruptMessage);

                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        ProcessSkillInterrupt(interruptMessage);
                    });
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理技能打断消息时出错: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 处理技能打断逻辑
        /// </summary>
        private void ProcessSkillInterrupt(SkillInterruptMessage message)
        {
            FlaxEngine.Debug.Log($"[SkillInterrupt] 技能 {message.SkillId} 被打断 (原因: {message.Reason}, 冷却重置: {message.ResetCooldown})");
        }
    }
}
