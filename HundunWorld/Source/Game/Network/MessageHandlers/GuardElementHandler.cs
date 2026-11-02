using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Combat.Skills;
using HundunWorld.Game.Network.Handlers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network.MessageHandlers
{
    /// <summary>
    /// 镇岳（<see cref="MessageType.SetGuardElement"/>，方案 §5.3）：把护体五行钉在某一行上。
    /// <para>
    /// 选择权在玩家，判定权在服务端：这里只发请求并回写服务端给的<b>生效结果</b>，
    /// 本地绝不预判"我点了就算"。被拒（冷却中、该行没练过）时服务端回的还是原值，
    /// UI 因此会自己弹回真正生效的那一行，而不是留一个亮着却无效的选中态。
    /// </para>
    /// </summary>
    public class GuardElementHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.SetGuardElement };

        // 与网关侧 SkillHandler 同一服务域（它声明的是 ServiceType.Combat）。
        public override ServiceType ServiceType => ServiceType.Combat;

        public GuardElementHandler() : base(MessageType.SetGuardElement)
        {
        }

        /// <summary>请求设定镇岳行（element：1金 2木 3水 4火 5土，0=放开回到自动）。</summary>
        public static async Task<bool> RequestAsync(ulong characterId, int element)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
                return false;

            return await networkManager.SendAsync(new SetGuardElementMessage
            {
                CharacterId = characterId,
                Element = element,
                ServiceType = ServiceType.Combat,
            });
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is not SetGuardElementMessage response)
                return Task.CompletedTask;

            Scripting.InvokeOnUpdate(() => WuxingCycleTracker.Instance.ApplyGuardResult(
                response.Success, response.GuardElement, response.NextChangeInSeconds, response.Message));
            return Task.CompletedTask;
        }
    }
}
