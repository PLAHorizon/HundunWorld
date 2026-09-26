using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.Inventory;
using HundunWorld.Game.Network.Handlers;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.GameMain;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network.MessageHandlers
{
    /// <summary>
    /// 角色背包换源：进场景时向服务端拉取权威物品清单，并在首次遇到"服务端背包为空"时
    /// 把玩家改动前的本地 SQLite 清单一次性迁移上去。
    /// <para>
    /// 三条安全约束（都对应真实故障，不是防御性摆设）：
    /// </para>
    /// <list type="number">
    /// <item><description>只在 <c>Success=true 且物品为空</c> 时迁移。<c>Success=false</c> 代表 grain
    /// 查询失败，此时上传会把"查不到"当成"没有"，每次断线重连就多播种一份物品。</description></item>
    /// <item><description>迁移前查本地标记 <c>InventoryMigrated</c>，迁移后置位——服务端
    /// <c>AddItemAsync</c> 是按 TemplateId+Quality 累加的，重复上传即重复累加。</description></item>
    /// <item><description>上传前按模板聚合数量：本地同一物品可能是多条记录，逐条发会造成
    /// 服务端多次新建/累加，也让"发放成功条目数"失去意义。</description></item>
    /// </list>
    /// </summary>
    public class RoleInventorySyncHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.RoleInventorySync, MessageType.InventoryGrant };

        public override ServiceType ServiceType => ServiceType.Game;

        /// <summary>本次会话是否已发起过迁移，防止 grant→sync→grant 递归。</summary>
        private static bool _migrationAttemptedThisSession;

        /// <summary>本次会话是否已做过新手消耗品补发检查（与持久标记双保险，防 grant→sync 递归）。</summary>
        private static bool _starterKitCheckedThisSession;

        public RoleInventorySyncHandler() : base(MessageType.RoleInventorySync)
        {
        }

        /// <summary>请求服务端背包清单。未连接时静默跳过（客户端仍用本地数据渲染）。</summary>
        public static async Task RequestAsync(ulong characterId)
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage()) return;

            await networkManager.SendAsync(new RoleInventorySyncMessage { CharacterId = characterId });
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is RoleInventorySyncMessage response)
            {
                // 应用会改 UI 控件，必须回主线程。
                Scripting.InvokeOnUpdate(async () =>
                {
                    try
                    {
                        await ApplyAsync(response);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[RoleInventorySync] 应用服务端背包失败（保留本地数据）: {ex.Message}");
                    }
                });
            }

            return Task.CompletedTask;
        }

        private static async Task ApplyAsync(RoleInventorySyncMessage response)
        {
            if (!response.Success)
            {
                Debug.LogWarning($"[RoleInventorySync] 服务端背包不可用，保留本地数据: {response.Message}");
                return;
            }

            // Ink 角色面板等真实 UI 消费这份权威快照（GameMainUI 是旧 UI 的兼容路径）。
            PlayerInventoryService.Instance.ApplyInventorySnapshot(response.Items, response.Capacity);
            GameMainUI.Instance?.ApplyServerInventory(response.Items, response.Capacity);

            // 逐件打印 templateId×count，验证"使用物品"是否真的扣减（数量问题排查用）。
            if (response.Items.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var it in response.Items)
                    sb.Append($"[{it.ItemId}:{it.TemplateId}x{it.Count}] ");
                Debug.Log($"[RoleInventorySync] 明细 {response.Items.Count} 种: {sb}");
            }

            if (response.Items.Count > 0)
            {
                Debug.Log($"[RoleInventorySync] 已应用服务端背包: {response.Items.Count} 种, 容量 {response.Capacity}");
                await TryGrantStarterKitAsync(response.CharacterId, response.Items);
                await TryGrantStarterEquipmentAsync(response.CharacterId);
                await TryGrantStarterGemsAsync(response.CharacterId);
                return;
            }

            // 服务端确认背包为空 —— 只有一次机会把本地历史清单迁上去。
            if (!_migrationAttemptedThisSession)
                await TryMigrateLocalInventoryAsync(response.CharacterId);

            // 迁移标记已有/未能上传的空背包（老角色）：补发一次新手消耗品，保证"使用物品"可用。
            await TryGrantStarterKitAsync(response.CharacterId, response.Items);
            await TryGrantStarterEquipmentAsync(response.CharacterId);
            await TryGrantStarterGemsAsync(response.CharacterId);
        }

        /// <summary>本次会话是否已做过新手装备补发检查。</summary>
        private static bool _starterEquipCheckedThisSession;

        /// <summary>
        /// 新手装备一次性补发（长剑/衣服/头巾各 1），让"穿戴/卸下"有可测内容。
        /// 与会话标记 + 持久标记双保险，防 grant→sync→grant 递归；与药水补发相互独立计时，
        /// 使此前只领过药水的老角色也能补到装备。
        /// </summary>
        private static async Task TryGrantStarterEquipmentAsync(ulong characterId)
        {
            if (_starterEquipCheckedThisSession || characterId == 0UL) return;

            var persistence = Services.CharacterPersistenceService.Instance;
            if (persistence == null) return;

            if (await persistence.IsStarterEquipGrantedAsync(characterId))
            {
                _starterEquipCheckedThisSession = true;
                return;
            }

            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage()) return;

            var sent = await networkManager.SendAsync(new InventoryGrantMessage
            {
                CharacterId = characterId,
                Entries = new List<InventoryGrantEntry>
                {
                    new InventoryGrantEntry { TemplateId = EquipmentDatabase.DefaultLongswordId, Count = 1 },
                    new InventoryGrantEntry { TemplateId = EquipmentDatabase.DefaultBodyId, Count = 1 },
                    new InventoryGrantEntry { TemplateId = EquipmentDatabase.DefaultHeadScarfId, Count = 1 },
                }
            });
            if (!sent) return;

            _starterEquipCheckedThisSession = true;
            await persistence.MarkStarterEquipGrantedAsync(characterId);
            Debug.Log("[RoleInventorySync] 已补发新手装备（长剑/衣服/头巾各 1）。");
            await RequestAsync(characterId);
        }

        /// <summary>本次会话是否已做过新手宝石补发检查。</summary>
        private static bool _starterGemsCheckedThisSession;

        /// <summary>
        /// 新手宝石一次性补发（金锐珠×2、木灵珠×1、水华珠×1），保证"镶嵌/交换/卸下"有可测内容。
        /// 会话标记 + 持久标记双保险，防 grant→sync→grant 递归。
        /// </summary>
        private static async Task TryGrantStarterGemsAsync(ulong characterId)
        {
            if (_starterGemsCheckedThisSession || characterId == 0UL) return;

            var persistence = Services.CharacterPersistenceService.Instance;
            if (persistence == null) return;

            if (await persistence.IsStarterGemsGrantedAsync(characterId))
            {
                _starterGemsCheckedThisSession = true;
                return;
            }

            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage()) return;

            var sent = await networkManager.SendAsync(new InventoryGrantMessage
            {
                CharacterId = characterId,
                Entries = new List<InventoryGrantEntry>
                {
                    new InventoryGrantEntry { TemplateId = ItemCatalog.GemMetalId, Count = 2 },
                    new InventoryGrantEntry { TemplateId = ItemCatalog.GemWoodId, Count = 1 },
                    new InventoryGrantEntry { TemplateId = ItemCatalog.GemWaterId, Count = 1 },
                }
            });
            if (!sent) return;

            _starterGemsCheckedThisSession = true;
            await persistence.MarkStarterGemsGrantedAsync(characterId);
            Debug.Log("[RoleInventorySync] 已补发新手宝石（金锐珠×2、木灵珠×1、水华珠×1）。");
            await RequestAsync(characterId);
        }

        /// <summary>
        /// 新手消耗品一次性补发（金创药×2、回气丹×1）。
        /// <para>
        /// 三重防重放：会话标记 + 持久标记 + "服务端清单里已有药水则只置位不发放"。
        /// 迁移种子本身含药水，故迁移成功路径直接置位、不重复发放。
        /// </para>
        /// </summary>
        private static async Task TryGrantStarterKitAsync(ulong characterId, List<ItemInfo> serverItems)
        {
            if (_starterKitCheckedThisSession || characterId == 0UL) return;

            var persistence = Services.CharacterPersistenceService.Instance;
            if (persistence == null) return;

            if (await persistence.IsStarterKitGrantedAsync(characterId))
            {
                _starterKitCheckedThisSession = true;
                return;
            }

            foreach (var item in serverItems)
            {
                if (item.TemplateId == ItemCatalog.HealingSalveTemplateId ||
                    item.TemplateId == ItemCatalog.EnergyPillTemplateId)
                {
                    await persistence.MarkStarterKitGrantedAsync(characterId);
                    _starterKitCheckedThisSession = true;
                    return;
                }
            }

            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage()) return;

            var sent = await networkManager.SendAsync(new InventoryGrantMessage
            {
                CharacterId = characterId,
                Entries = new List<InventoryGrantEntry>
                {
                    new InventoryGrantEntry { TemplateId = ItemCatalog.HealingSalveTemplateId, Count = 2 },
                    new InventoryGrantEntry { TemplateId = ItemCatalog.EnergyPillTemplateId, Count = 1 },
                }
            });
            if (!sent) return;

            _starterKitCheckedThisSession = true;
            await persistence.MarkStarterKitGrantedAsync(characterId);
            Debug.Log("[RoleInventorySync] 已补发新手消耗品（金创药×2、回气丹×1）。");

            // 发放后重拉一次，让界面立刻带上新物品。
            await RequestAsync(characterId);
        }

        private static async Task TryMigrateLocalInventoryAsync(ulong characterId)
        {
            _migrationAttemptedThisSession = true;

            if (characterId == 0) return;

            var persistence = Services.CharacterPersistenceService.Instance;
            if (persistence == null) return;

            if (await persistence.IsInventoryMigratedAsync(characterId))
                return;   // 之前迁过：服务端为空是真·空（玩家把东西用完了），不能再播一次

            var local = await persistence.LoadInventoryAsync(characterId);
            if (local == null || local.Count == 0)
                return;

            // 按模板聚合：InventoryItemData.ItemId 就是客户端定义表 ID = 服务端 TemplateId。
            var aggregated = new Dictionary<int, int>();
            foreach (var item in local)
            {
                if (item.ItemId <= 0 || item.Count <= 0) continue;
                aggregated.TryGetValue(item.ItemId, out var current);
                aggregated[item.ItemId] = current + item.Count;
            }

            if (aggregated.Count == 0) return;

            var entries = new List<InventoryGrantEntry>(aggregated.Count);
            foreach (var kv in aggregated)
                entries.Add(new InventoryGrantEntry { TemplateId = kv.Key, Count = kv.Value });

            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage()) return;

            var sent = await networkManager.SendAsync(new InventoryGrantMessage
            {
                CharacterId = characterId,
                Entries = entries
            });

            if (!sent) return;

            await persistence.MarkInventoryMigratedAsync(characterId);
            // 迁移种子自带新手药水（见 CharacterPersistenceService.GetDefaultAppearance），
            // 直接置位补发标记，避免空背包窗口期的 grant 竞态重复播种。
            await persistence.MarkStarterKitGrantedAsync(characterId);
            Debug.Log($"[RoleInventorySync] 已上传本地背包迁移请求: {entries.Count} 种模板（等待服务端回推清单）。");

            // 迁移后重新拉一次，让界面立刻显示服务端权威结果。
            await RequestAsync(characterId);
        }
    }
}
