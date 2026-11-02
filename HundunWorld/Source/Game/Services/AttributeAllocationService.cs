using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Network;
using Game.Character.Attributes;

namespace HundunWorld.Game.Services
{
    /// <summary>
    /// 角色属性加点的客户端侧服务：持有服务端权威快照，把 UI 的"期望终态"提交给服务端，
    /// 由回执驱动组件属性更新。
    /// <para>
    /// 职责边界：加点的服务端权威在 <c>CharacterGrain</c>（校验点池 → Redis 缓存 → 属性表落库），
    /// 本服务**不做本地持久化**，也不自行增减点数——它只发请求、收回执、把权威分配灌进
    /// <see cref="CharacterAttributesComponent"/>。UI 的"预览"（未提交增量）留在页面内，
    /// 提交失败时直接按权威快照回滚。
    /// </para>
    /// </summary>
    public class AttributeAllocationService
    {
        private static AttributeAllocationService _instance;
        public static AttributeAllocationService Instance => _instance ??= new AttributeAllocationService();

        /// <summary>服务端权威分配（未收到任何下发时为全零）。</summary>
        public AttributeAllocationData Allocation { get; private set; } = new AttributeAllocationData();

        /// <summary>点池上限（服务端下发；未收到时按 等级 × <see cref="FallbackPointsPerLevel"/> 估算）。</summary>
        public int TotalPoints { get; private set; }

        /// <summary>剩余可分配点数（服务端下发）。</summary>
        public int AvailablePoints { get; private set; }

        /// <summary>是否已拿到服务端数据（决定 UI 能否提交）。</summary>
        public bool HasServerData { get; private set; }

        /// <summary>服务端未回点池时的兜底每级点数，需与服务端 CharacterGrain 保持一致。</summary>
        public const int FallbackPointsPerLevel = 5;

        /// <summary>权威快照变化（登录下发或回执成功后触发）。</summary>
        public event Action Changed;

        /// <summary>提交结果（成功/失败 + 说明），供 UI 弹提示与回滚预览。</summary>
        public event Action<bool, string> OperationFinished;

        private AttributeAllocationService() { }

        private static ulong CurrentCharacterId =>
            HundunWorldGame.Instance != null ? HundunWorldGame.Instance.PlayerId : 0UL;

        /// <summary>
        /// 登录下发入口：把 <c>CharacterInfo.Allocation</c> 灌进权威快照并应用到本地组件。
        /// 由 <c>EnterGameHandler</c> 在收到 EnterGameResponse 时调用。
        /// </summary>
        public void ApplyFromCharacterInfo(AttributeAllocationData allocation, int level)
        {
            Allocation = allocation ?? new AttributeAllocationData();
            TotalPoints = Math.Max(0, level) * FallbackPointsPerLevel;
            AvailablePoints = TotalPoints - Spent(Allocation);
            HasServerData = true;
            ApplyToComponent();
            Changed?.Invoke();
            Debug.Log($"[AttributeAllocation] 登录下发: 已用 {Spent(Allocation)} / {TotalPoints}");
        }

        /// <summary>提交期望终态（九项全量；全零即重置）。返回是否成功发出，结果走 <see cref="OperationFinished"/>。</summary>
        public async Task<bool> RequestAllocateAsync(AttributeAllocationData desired)
        {
            var characterId = CurrentCharacterId;
            if (characterId == 0UL || desired == null)
            {
                OperationFinished?.Invoke(false, "角色未就绪");
                return false;
            }
            if (HundunWorldGame.Instance?.NetworkManager == null)
            {
                OperationFinished?.Invoke(false, "网络未连接");
                return false;
            }

            bool sent;
            try
            {
                sent = await HundunWorldGame.Instance.NetworkManager.SendAsync(new AttributeAllocateRequest
                {
                    CharacterId = characterId,
                    Allocation = desired,
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttributeAllocation] 发送加点请求异常: {ex.Message}");
                sent = false;
            }

            if (!sent) OperationFinished?.Invoke(false, "发送失败，请检查网络");
            return sent;
        }

        /// <summary>
        /// 消费服务端回执：无论成败都以权威快照收敛本地组件（成功=新分配，失败=原状回滚）。
        /// 由 <c>AttributeAllocationResponseHandler</c> 经主线程调用。
        /// </summary>
        public void OnAllocateResponse(AttributeAllocateResponse response)
        {
            if (response == null) return;

            Allocation = response.Allocation ?? new AttributeAllocationData();
            TotalPoints = response.TotalPoints;
            AvailablePoints = response.AvailablePoints;
            HasServerData = true;

            ApplyToComponent();
            Changed?.Invoke();
            OperationFinished?.Invoke(response.Success, response.Message ?? "");

            // 加点会同时改变五行三行与通用攻防基值（都写在服务端那份画像里）：
            // 紧接着重取一次权威画像，面板才不会停在加点前的行值上。
            if (response.Success)
            {
                var characterId = CurrentCharacterId;
                if (characterId != 0UL)
                    _ = HundunWorld.Game.Network.MessageHandlers.CharacterAttributeRefreshHandler.RequestAsync(characterId);
            }

            Debug.Log($"[AttributeAllocation] 回执: Success={response.Success}, Msg={response.Message}, " +
                      $"已用 {Spent(Allocation)} / {TotalPoints}");
        }

        /// <summary>
        /// 把权威分配写进本地玩家属性组件（组件按"基值+装备+加点"三层合成，写入即触发全 UI 刷新）。
        /// <para>
        /// 公开是因为时序：登录下发到达时本地玩家 Actor 往往还没随场景创建出来，
        /// 此时本方法静默跳过，由 MainUIManager 在"本地玩家就绪"时再调一次把缓存灌进去。
        /// </para>
        /// </summary>
        public void ApplyToComponent()
        {
            try
            {
                var actor = HundunWorldGame.Instance?.LocalPlayerActor;
                var component = actor != null ? actor.GetScript<CharacterAttributesComponent>() : null;
                if (component == null) return;

                // 协议里的 Intelligence 就是历史上第 4 项"内力"（MemoryPackOrder 仍是 3，字节流兼容）。
                // 但语义变了：服务端必须同版本重启，否则它仍按"内力每点 +10 内力上限"结算，
                // 与客户端这边"智力每点 +100 内力上限 + 精神/智慧/神识/幸运"对不上。
                component.ApplyAllocation(new CharacterAttributesComponent.AttributeAllocation
                {
                    Strength = Allocation.Strength,
                    Agility = Allocation.Agility,
                    Health = Allocation.Health,
                    Intelligence = Allocation.Intelligence,
                    Metal = Allocation.Metal,
                    Wood = Allocation.Wood,
                    Water = Allocation.Water,
                    Fire = Allocation.Fire,
                    Earth = Allocation.Earth,
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttributeAllocation] 应用到属性组件失败: {ex.Message}");
            }
        }

        /// <summary>当前权威分配已用点数。</summary>
        public int SpentPoints => Spent(Allocation);

        private static int Spent(AttributeAllocationData a)
            => a == null ? 0
                : a.Strength + a.Agility + a.Health + a.Intelligence
                  + a.Metal + a.Wood + a.Water + a.Fire + a.Earth;

        /// <summary>
        /// 按行序（力量/敏捷/体质/智力/金/木/水/火/土，与协议字段序一致）构造期望终态 DTO。
        /// 数组不足 9 项时缺位按 0，避免 UI 与协议两边各写一遍映射。
        /// </summary>
        public static AttributeAllocationData FromRowValues(int[] rowValues)
        {
            var dto = new AttributeAllocationData();
            if (rowValues == null || rowValues.Length < 9) return dto;
            dto.Strength = rowValues[0];
            dto.Agility = rowValues[1];
            dto.Health = rowValues[2];
            dto.Intelligence = rowValues[3];
            dto.Metal = rowValues[4];
            dto.Wood = rowValues[5];
            dto.Water = rowValues[6];
            dto.Fire = rowValues[7];
            dto.Earth = rowValues[8];
            return dto;
        }
    }
}
