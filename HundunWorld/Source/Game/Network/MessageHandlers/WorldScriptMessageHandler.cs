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
    /// 脚本化世界生成结果处理器：只负责把 <see cref="GmScriptResultMessage"/> 转成事件，
    /// 文案与配对逻辑交给 <see cref="HundunWorld.Game.Chat.WorldScriptClient"/>。
    /// </summary>
    /// <remarks>
    /// 与其余客户端处理器一致：继承 <see cref="BaseMessageHandler"/> 即会被 NetworkManager 的
    /// 程序集反射扫描自动注册，无需在任何装配表里追加。
    /// </remarks>
    public class WorldScriptMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.ScriptResult
        };

        // 服务端 GmScriptResultMessage 标注为 ServiceType.System，ValidateMessage 会按此校验。
        public override ServiceType ServiceType => ServiceType.System;

        /// <summary>
        /// 收到脚本指令结果事件（网络线程触发，订阅者自行切线程）
        /// </summary>
        public event Action<GmScriptResultMessage> ScriptResultReceived;

        public WorldScriptMessageHandler() : base(MessageType.ScriptResult)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[WorldScriptMessageHandler] 收到空消息体");
                return;
            }

            if (message.Body is GmScriptResultMessage result)
            {
                Debug.Log($"[WorldScriptMessageHandler] 收到脚本指令结果: RequestId={result.RequestId}, 成功={result.Success}, 原因={result.Reason}");
                ScriptResultReceived?.Invoke(result);
            }
            else
            {
                Debug.LogWarning($"[WorldScriptMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
            }

            await Task.CompletedTask;
        }
    }
}
