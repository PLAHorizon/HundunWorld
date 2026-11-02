using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Combat.Skills;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.UI.StyleSystem;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hud = global::Game.UI.HUD;

namespace HundunWorld.Game.Network.MessageHandlers
{
    /// <summary>
    /// 习得技能的下行与发送方（<see cref="MessageType.LearnSkill"/>）。
    /// <para>
    /// 这里只开<b>门派传授</b>通道（<c>LearnMethod=0</c>），服务端只把它放给白阶；
    /// 黄阶以上请从背包里"使用"对应秘籍，那条链路会掷签并按结果扣书。
    /// 为什么客户端能选通道却拦不住：判定在 <c>SkillGrain.LearnSkillAsync</c> 里，
    /// 上报一个 <c>LearnMethod=0</c> 顶多换来一句拒绝，不能绕过技能书经济。
    /// </para>
    /// </summary>
    public class SkillLearnHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.LearnSkill };

        // 真实现在网关的 SkillHandler（它声明的是 ServiceType.Combat）。
        // CharacterHandler 以前也声明过同一消息但只回一句"成功"，那几支声明已经撤掉了。
        public override ServiceType ServiceType => ServiceType.Combat;

        public SkillLearnHandler() : base(MessageType.LearnSkill)
        {
        }

        /// <summary>
        /// 请求以门派传授习得这一式。返回请求是否发出（不代表习得成功）。
        /// </summary>
        /// <param name="trainerEntityId">
        /// 正在交互的训练师实体 id；0 = 不在训练师面前（走免费的老路）。
        /// <b>它只决定"要不要按训练师结算"</b>：学费、教不教、距离都由服务端裁决。
        /// </param>
        public static async Task<bool> LearnByTransmissionAsync(ulong characterId, int skillId, ulong trainerEntityId = 0)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
                return false;

            return await networkManager.SendAsync(new LearnSkillRequest
            {
                CharacterId = characterId,
                SkillId = skillId,
                LearnMethod = 0,
                ServiceType = ServiceType.Combat,
                EntityId = trainerEntityId,
            });
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is not LearnSkillResponse response)
                return Task.CompletedTask;

            int skillId = response.LearnedSkill != null ? response.LearnedSkill.SkillId : 0;
            Scripting.InvokeOnUpdate(() =>
            {
                if (!response.Success)
                {
                    // 失败多半意味着"人不在训练师跟前了"（服务端按距离/身份判过）。
                    // 解绑，避免下一次点击继续带着一个过期实体去结算。
                    TrainerClientService.Instance.Clear();
                }

                if (response.Success)
                {
                    // 不在本地臆造"已学"集合，改成重拉权威目录：
                    // 就地改状态会带来"栏上亮着、服务端没这条"的分裂，重拉最坏也只是暂时没刷出来。
                    var player = HundunWorldGame.Instance;
                    if (player != null)
                        _ = SkillCatalogSyncHandler.RequestAsync(player.PlayerId);

                    var cfg = skillId > 0 ? SkillDatabase.GetSkill(skillId) : null;
                    Hud.ShowNotification($"习得《{(cfg != null ? cfg.SkillName : skillId.ToString())}》",
                        2.2f, InkWashTheme.JadeBright);
                }
                else
                {
                    // 服务端给的原文（缺哪一卷、周天还差几次）直接上屏，别换成"学习失败"四个字
                    Hud.ShowNotification(string.IsNullOrEmpty(response.Message) ? "未能习得" : response.Message,
                        2.6f, InkWashTheme.GoldBright);
                }
            });
            return Task.CompletedTask;
        }
    }
}
