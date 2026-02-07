using System;
using System.Threading.Tasks;
using FlaxEngine;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 待处理动作类
    /// 用于管理战斗中的延迟动作和队列系统
    /// </summary>
    public class PendingAction
    {
        public enum ActionType
        {
            Attack,
            SkillCast,
            Movement,
            ItemUse,
            Interaction
        }

        public enum ActionStatus
        {
            Queued,
            Executing,
            Completed,
            Cancelled,
            Failed
        }

        public ulong CharacterId { get; set; }
        public ActionType Type { get; set; }
        public ActionStatus Status { get; set; }
        public float ExecutionTime { get; set; }
        public float Delay { get; set; }
        public object Payload { get; set; }
        public Action<PendingAction> OnCompleted { get; set; }
        public Action<PendingAction> OnFailed { get; set; }

        private bool _isCancelled = false;

        public PendingAction(ulong characterId, ActionType type, float delay = 0f)
        {
            CharacterId = characterId;
            Type = type;
            Delay = delay;
            Status = ActionStatus.Queued;
            ExecutionTime = Time.TimeSinceStartup + delay;
        }

        /// <summary>
        /// 执行动作
        /// </summary>
        public async Task<bool> ExecuteAsync()
        {
            if (_isCancelled || Status != ActionStatus.Queued)
                return false;

            Status = ActionStatus.Executing;
            Debug.Log($"执行待处理动作: {Type} for character {CharacterId}");

            try
            {
                // 等待延迟时间
                if (Delay > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Delay));
                }

                if (_isCancelled)
                {
                    Status = ActionStatus.Cancelled;
                    OnFailed?.Invoke(this);
                    return false;
                }

                // 执行具体的动作逻辑
                var result = await ExecuteActionLogic();
                
                Status = result ? ActionStatus.Completed : ActionStatus.Failed;
                
                if (result)
                {
                    OnCompleted?.Invoke(this);
                }
                else
                {
                    OnFailed?.Invoke(this);
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"执行待处理动作时发生异常: {ex.Message}");
                Status = ActionStatus.Failed;
                OnFailed?.Invoke(this);
                return false;
            }
        }

        /// <summary>
        /// 取消动作
        /// </summary>
        public void Cancel()
        {
            _isCancelled = true;
            Status = ActionStatus.Cancelled;
            Debug.Log($"待处理动作已取消: {Type} for character {CharacterId}");
        }

        /// <summary>
        /// 执行具体的动作逻辑
        /// </summary>
        private async Task<bool> ExecuteActionLogic()
        {
            // 根据动作类型执行不同的逻辑
            switch (Type)
            {
                case ActionType.Attack:
                    return await ExecuteAttack();
                case ActionType.SkillCast:
                    return await ExecuteSkillCast();
                case ActionType.Movement:
                    return await ExecuteMovement();
                case ActionType.ItemUse:
                    return await ExecuteItemUse();
                case ActionType.Interaction:
                    return await ExecuteInteraction();
                default:
                    return false;
            }
        }

        private async Task<bool> ExecuteAttack()
        {
            // 实现攻击逻辑
            await Task.Delay(100); // 模拟执行时间
            Debug.Log($"执行攻击动作 for character {CharacterId}");
            return true;
        }

        private async Task<bool> ExecuteSkillCast()
        {
            // 实现技能施放逻辑
            await Task.Delay(200); // 模拟执行时间
            Debug.Log($"执行技能施放动作 for character {CharacterId}");
            return true;
        }

        private async Task<bool> ExecuteMovement()
        {
            // 实现移动逻辑
            await Task.Delay(50); // 模拟执行时间
            Debug.Log($"执行移动动作 for character {CharacterId}");
            return true;
        }

        private async Task<bool> ExecuteItemUse()
        {
            // 实现物品使用逻辑
            await Task.Delay(150); // 模拟执行时间
            Debug.Log($"执行物品使用动作 for character {CharacterId}");
            return true;
        }

        private async Task<bool> ExecuteInteraction()
        {
            // 实现交互逻辑
            await Task.Delay(100); // 模拟执行时间
            Debug.Log($"执行交互动作 for character {CharacterId}");
            return true;
        }

        /// <summary>
        /// 检查动作是否准备好执行
        /// </summary>
        public bool IsReadyToExecute()
        {
            return Status == ActionStatus.Queued && Time.TimeSinceStartup >= ExecutionTime && !_isCancelled;
        }

        /// <summary>
        /// 获取剩余等待时间
        /// </summary>
        public float GetRemainingTime()
        {
            if (Status != ActionStatus.Queued)
                return 0f;

            return Math.Max(0, ExecutionTime - Time.TimeSinceStartup);
        }
    }
}