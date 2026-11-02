using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Combat.Skills;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.StyleSystem;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hud = global::Game.UI.HUD;

namespace HundunWorld.Game.Network.MessageHandlers
{
    /// <summary>
    /// 精研（技能升级）的下行与发送方（<see cref="MessageType.UpgradeSkill"/>）。
    /// <para>
    /// 演化式要求源式"圆满"（<c>Level == MaxLevel</c>，§6.2），而圆满只能靠精研推进；
    /// 这条链路此前在服务端早已存在却没有任何玩家入口，树页只能"习得"不能"练满"。
    /// </para>
    /// <para>
    /// 这里<b>不</b>自演算等级：回包只当"该重拉权威目录"的信号（同 <see cref="SkillLearnHandler"/>）。
    /// 服务端当前不扣任何成本（<c>ConsumedGold/ConsumedItems/ConsumedExperience</c> 恒为 0），
    /// 所以按钮点一下即进一重；将来定了数值口径，只需在 <see cref="SkillGrain"/> 侧加校验，
    /// 入口与回包都不用再动。
    /// </para>
    /// </summary>
    public class SkillUpgradeHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.UpgradeSkill };

        // 真实现在网关的 SkillHandler（ServiceType.Combat），与 LearnSkill 同一条通道。
        public override ServiceType ServiceType => ServiceType.Combat;

        /// <summary>本次会话是否已有一次精研在飞；回包到达前不再重复发。</summary>
        public static bool InFlight { get; private set; }

        public SkillUpgradeHandler() : base(MessageType.UpgradeSkill)
        {
        }

        /// <summary>请求把这一式精研一重。返回请求是否发出（不代表已进境）。</summary>
        public static async Task<bool> RequestAsync(ulong characterId, int skillId)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
                return false;

            InFlight = true;
            var sent = await networkManager.SendAsync(new UpgradeSkillRequest
            {
                CharacterId = characterId,
                SkillId = skillId,
                UpgradeMethod = 0,
                ServiceType = ServiceType.Combat,
            });
            // 没发出去就不会有回包，得自己清掉在飞标记，否则按钮永久锁死。
            if (!sent) InFlight = false;
            return sent;
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is not UpgradeSkillResponse response)
                return Task.CompletedTask;

            InFlight = false;

            var player = HundunWorldGame.Instance;
            if (player == null) return Task.CompletedTask;

            if (response.Success)
            {
                // 等级以服务端为准：重拉目录，让树页、技能栏、演化门槛同时看到新的一重。
                _ = SkillCatalogSyncHandler.RequestAsync(player.PlayerId);
                var cfg = SkillDatabase.GetSkill(response.UpgradedSkill?.SkillId ?? 0);
                int level = SkillCatalogService.Instance.LevelOf(cfg?.SkillId ?? 0);
                Hud.ShowNotification($"《{(cfg != null ? cfg.SkillName : "此式")}》精研至第 {level} 重",
                    2.0f, InkWashTheme.JadeBright);
            }
            else
            {
                // 网关只在"未习得/已满级"两种情况下回 false，且不带区分；
                // 满级在点按钮前已由本地权威快照拦下并另发文案，走到这里就是真的没成。
                Hud.ShowNotification(string.IsNullOrEmpty(response.Message) ? "精研未果" : response.Message,
                    2.4f, InkWashTheme.GoldBright);
            }
            return Task.CompletedTask;
        }
    }
}
