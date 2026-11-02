using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network.MessageHandlers
{
    /// <summary>
    /// 技能目录下行（<see cref="MessageType.SkillCatalogSync"/>）。
    /// <para>
    /// 客户端此前自带一份硬编码技能表，与服务端的 <c>Game_HunduShijie_SkillTemplate</c> 只能靠人工同步；
    /// 本处理器把服务端那份灌进 <see cref="SkillCatalogService"/>，之后技能栏/详情读的都是权威值。
    /// </para>
    /// </summary>
    public class SkillCatalogSyncHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.SkillCatalogSync };

        // 与网关侧的 SkillHandler 一致（它声明的是 ServiceType.Combat）。
        public override ServiceType ServiceType => ServiceType.Combat;

        public SkillCatalogSyncHandler() : base(MessageType.SkillCatalogSync)
        {
        }

        /// <summary>请求指定角色的技能目录。返回请求是否发出。</summary>
        public static async Task<bool> RequestAsync(ulong characterId)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
                return false;

            return await networkManager.SendAsync(new SkillCatalogSyncMessage
            {
                CharacterId = characterId,
                ServiceType = ServiceType.Combat,
            });
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is not SkillCatalogSyncMessage response)
                return Task.CompletedTask;

            Scripting.InvokeOnUpdate(() => SkillCatalogService.Instance.Apply(response));
            return Task.CompletedTask;
        }
    }
}
