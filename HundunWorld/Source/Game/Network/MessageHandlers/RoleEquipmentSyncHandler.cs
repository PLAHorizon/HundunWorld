using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Character;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// 注意：`EquipmentSlot` 在 HundunWorld.Game.Equipment 与 Horizon.Game.Message.Enums 中**同名**，
// 同时 using 两个命名空间会触发 CS0104 歧义。这里刻意只 using 服务端消息命名空间，
// 客户端槽位枚举一律用下面的别名引用。
using ClientSlot = HundunWorld.Game.Equipment.EquipmentSlot;

namespace HundunWorld.Game.Network.Handlers
{
    /// <summary>
    /// 角色已装备清单同步（服务端权威 → 客户端 3D 换装）。
    /// <para>
    /// 改造前客户端的换装数据**只来自本地 SQLite**（<c>CharacterPersistenceService</c>），
    /// 服务端记录的装备变化客户端无从得知，跨设备登录外观也会不一致。
    /// 现在进场景时先发 <see cref="RoleEquipmentSyncMessage"/> 请求，回包到达后按槽位重算并重新应用。
    /// </para>
    /// <para>
    /// <b>本地优先、服务端纠正</b>：Actor 创建时仍立即用本地缓存渲染（避免进场瞬间光膀子），
    /// 服务端回包后再覆盖为权威结果；请求失败/超时则保留本地渲染，不做回滚清空。
    /// </para>
    /// </summary>
    public class RoleEquipmentSyncHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.RoleEquipmentSync };

        public override ServiceType ServiceType => ServiceType.Game;

        public RoleEquipmentSyncHandler() : base(MessageType.RoleEquipmentSync)
        {
        }

        /// <summary>
        /// 向服务端请求指定角色的已装备清单。发送失败只记日志、不抛异常——
        /// 调用方已经用本地缓存渲染过了，这里失败不该影响进场。
        /// </summary>
        public static async Task RequestAsync(ulong characterId)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
            {
                Debug.Log("[RoleEquipmentSync] 未连接，跳过装备清单请求（沿用本地外观缓存）。");
                return;
            }

            var sent = await networkManager.SendAsync(new RoleEquipmentSyncMessage
            {
                CharacterId = characterId
            });

            if (!sent)
                Debug.LogWarning("[RoleEquipmentSync] 装备清单请求发送失败（沿用本地外观缓存）。");
        }

        /// <summary>
        /// 把穿戴命令上报服务端权威背包。
        /// <para>
        /// 填 <see cref="EquipItemMessage.TemplateId"/>（客户端定义表编号）而不是 ItemId：
        /// 服务端背包实例 ID 是另一个编号空间，客户端不掌握，由服务端按模板解析。
        /// </para>
        /// </summary>
        /// <returns>请求是否发出（不代表服务端已接受）。</returns>
        public static async Task<bool> SendEquipAsync(ulong characterId, int templateId, ClientSlot slot)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
                return false;

            return await networkManager.SendAsync(new EquipItemMessage
            {
                CharacterId = characterId,
                TemplateId = templateId,
                Slot = (int)slot,
                Unequip = false,
            });
        }

        /// <summary>把卸下命令上报服务端权威背包。返回请求是否发出。</summary>
        public static async Task<bool> SendUnequipAsync(ulong characterId, ClientSlot slot)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
                return false;

            return await networkManager.SendAsync(new EquipItemMessage
            {
                CharacterId = characterId,
                Slot = (int)slot,
                Unequip = true,
            });
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is not RoleEquipmentSyncMessage response)
                return Task.CompletedTask;

            if (response.CharacterId == 0UL)
                return Task.CompletedTask;

            // 回包处理要在主线程做：下面会创建/销毁 Actor 与改骨骼挂载。
            Scripting.InvokeOnUpdate(() =>
            {
                try
                {
                    ApplyToLocalPlayer(response);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[RoleEquipmentSync] 应用服务端装备失败（保留本地渲染）: {ex.Message}");
                }
            });

            return Task.CompletedTask;
        }

        private static void ApplyToLocalPlayer(RoleEquipmentSyncMessage response)
        {
            if (!response.Success)
            {
                Debug.LogWarning($"[RoleEquipmentSync] 服务端返回失败，保留本地外观: {response.Message}");
                return;
            }

            // UI（Ink 角色面板装备槽）与 3D 换装消费同一份权威快照；
            // 即使 Actor 未就绪也要先喂给服务层，不然面板会停在旧数据。
            HundunWorld.Game.Services.PlayerInventoryService.Instance.ApplyEquipmentSnapshot(response.Entries);

            var local = HundunWorldGame.Instance?.LocalPlayerActor;
            if (local == null)
            {
                Debug.Log("[RoleEquipmentSync] 本地玩家 Actor 尚未就绪，忽略本次装备回包。");
                return;
            }

            var manager = local.GetScript<CharacterEquipmentManager>();
            if (manager == null)
                return;

            if (!TryBuildAppearance(response.Entries, out var bodyId, out var accessoryIds, out var weaponIds, out var appearanceId))
                return;

            // Initialize 可重复调用：内部会先清掉旧的配饰/武器再挂新的。
            manager.Initialize(bodyId, accessoryIds, weaponIds, appearanceId);

            Debug.Log($"[RoleEquipmentSync] 已应用服务端权威装备: Body={bodyId}, Appearance={appearanceId}, " +
                      $"Accessories=[{string.Join(",", accessoryIds)}], Weapons=[{string.Join(",", weaponIds)}]");
        }

        /// <summary>
        /// 把"槽位→物品"摊平成 <see cref="CharacterEquipmentManager.Initialize"/> 需要的三组 ID。
        /// <para>
        /// 用的是 <see cref="EquippedSlotEntry.TemplateId"/> 而非 ItemId：服务端 ItemId 是背包
        /// grain 的自增实例 ID（从 1 起），而 <c>EquipmentDatabase.GetEquipment(int)</c> 的入参是
        /// 客户端定义表 ID（10001..30001），两者不同编号空间，喂错会全部解析为 null。
        /// </para>
        /// </summary>
        internal static bool TryBuildAppearance(
            List<EquippedSlotEntry> entries,
            out int bodyEquipmentId,
            out List<int> accessoryIds,
            out List<int> weaponIds,
            out int appearanceEquipmentId)
        {
            bodyEquipmentId = 0;
            accessoryIds = new List<int>();
            weaponIds = new List<int>();
            appearanceEquipmentId = 0;

            if (entries == null || entries.Count == 0)
                return false;   // 空清单不动现有渲染：更可能是"还没穿上"而不是"服务端确认什么都没有"

            foreach (var entry in entries)
            {
                if (entry.TemplateId <= 0) continue;

                var slot = (ClientSlot)entry.Slot;
                if (slot == ClientSlot.Body)
                {
                    bodyEquipmentId = entry.TemplateId;
                }
                else if (slot == ClientSlot.Appearance)
                {
                    appearanceEquipmentId = entry.TemplateId;
                }
                else if (slot == ClientSlot.RightHand || slot == ClientSlot.LeftHand)
                {
                    weaponIds.Add(entry.TemplateId);
                }
                else
                {
                    accessoryIds.Add(entry.TemplateId);
                }
            }

            return true;
        }
    }
}
