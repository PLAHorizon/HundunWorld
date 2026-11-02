using FlaxEngine;
using Game.Character.Attributes;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network.MessageHandlers
{
    /// <summary>
    /// 服务端权威战斗画像下行（<see cref="MessageType.CharacterAttributeRefresh"/>）。
    /// <para>
    /// 五行三行的权威出处在服务端：属性表三组列、物品模板表里的装备数值、以及五行属性值兜底，
    /// 客户端全都读不到。本处理器把 <c>CharacterGrain</c> 那份画像灌进
    /// <see cref="CharacterAttributesComponent"/>，之后面板与本地预览读的就是结算真正生效的数。
    /// </para>
    /// <para>
    /// 失败（<c>Success=false</c>）时**保留本地镜像**，不清零：查询失败和"服务端确认你五行全 0"
    /// 是两件事，把前者当后者会让面板在每次断网重连时闪一下空值。
    /// </para>
    /// </summary>
    public class CharacterAttributeRefreshHandler : BaseMessageHandler
    {
        /// <summary>刷新原因（与协议注释一致：0=登录, 1=升级, 2=装备变更, 3=Buff变更, 4=手动）。</summary>
        public const int ReasonLogin = 0, ReasonLevelUp = 1, ReasonEquipment = 2, ReasonBuff = 3, ReasonManual = 4;

        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.CharacterAttributeRefresh };

        public override ServiceType ServiceType => ServiceType.Game;

        public CharacterAttributeRefreshHandler() : base(MessageType.CharacterAttributeRefresh)
        {
        }

        /// <summary>向服务端请求指定角色的权威画像。返回请求是否发出。</summary>
        public static async Task<bool> RequestAsync(ulong characterId, int reason = ReasonManual)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
                return false;

            return await networkManager.SendAsync(new CharacterAttributeRefreshRequest
            {
                CharacterId = characterId,
                RefreshReason = reason,
            });
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is not CharacterAttributeRefreshResponse response)
                return Task.CompletedTask;

            if (!response.Success)
            {
                Debug.LogWarning($"[CombatProfileSync] 服务端未返回权威画像，沿用本地镜像：{response.Message}");
                return Task.CompletedTask;
            }

            // 写组件与刷 UI 都要在主线程。
            Scripting.InvokeOnUpdate(() =>
            {
                try
                {
                    ApplyToLocalPlayer(response);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            });

            return Task.CompletedTask;
        }

        private static void ApplyToLocalPlayer(CharacterAttributeRefreshResponse response)
        {
            var actor = HundunWorldGame.Instance?.LocalPlayerActor;
            var component = actor != null ? actor.GetScript<CharacterAttributesComponent>() : null;
            if (component == null)
            {
                Debug.Log("[CombatProfileSync] 本地玩家属性组件未就绪，本次权威画像丢弃（下次请求会再来）。");
                return;
            }

            component.ApplyAuthoritativeProfile(
                ToRows(response.ElementAttack), ToRows(response.ElementDefense), ToRows(response.ElementPenetration),
                response.Attack, response.Defense);

            // 宠物栏是同行下行的另一份账：它不是"合算后的攻防"，而是"攻防里宠物占多少"。
            // 逐字段赋值（不是按位置传八个整数），并把 Issues 原样带上——
            // "有名册但没出战宠"与"名册没读到"必须能在界面上分开。
            component.ApplyPetBonus(new CharacterAttributesComponent.PetBonusInfo
            {
                Attack = response.FromPetsAttack,
                Health = response.FromPetsHealth,
                Defense = response.FromPetsDefense,
                Hit = response.FromPetsHit,
                CritTankChanceBps = response.PetCritTankChanceBps,
                Contributors = response.PetBonusContributors,
                RosterCount = response.PetRosterCount,
                Issues = response.PetBonusIssues ?? string.Empty,
            });

            Debug.Log($"[CombatProfileSync] 权威画像已生效: 攻={response.Attack:0.#} 防={response.Defense:0.#} " +
                      $"攻行={Rows(response.ElementAttack)} 防行={Rows(response.ElementDefense)} " +
                      $"破防行={Rows(response.ElementPenetration)} 亲和={Rows(response.Affinity)} " +
                      $"宠物贡献 攻={response.FromPetsAttack} 血={response.FromPetsHealth} " +
                      $"防={response.FromPetsDefense} 命中={response.FromPetsHit} " +
                      $"参与={response.PetBonusContributors}/{response.PetRosterCount} " +
                      $"问题={(string.IsNullOrEmpty(response.PetBonusIssues) ? "无" : response.PetBonusIssues)}");
        }

        /// <summary>浮点三行落成整数行（与面板/本地口径同为整数，四舍五入而非截断）。</summary>
        private static CharacterAttributesComponent.WuxingRows ToRows(in ElementStatVector vector) =>
            new CharacterAttributesComponent.WuxingRows
            {
                Metal = (int)Math.Round(vector.Metal),
                Wood = (int)Math.Round(vector.Wood),
                Water = (int)Math.Round(vector.Water),
                Fire = (int)Math.Round(vector.Fire),
                Earth = (int)Math.Round(vector.Earth),
            };

        private static string Rows(in ElementStatVector vector)
            => $"{(int)Math.Round(vector.Metal)}/{(int)Math.Round(vector.Wood)}/" +
               $"{(int)Math.Round(vector.Water)}/{(int)Math.Round(vector.Fire)}/{(int)Math.Round(vector.Earth)}";
    }
}
