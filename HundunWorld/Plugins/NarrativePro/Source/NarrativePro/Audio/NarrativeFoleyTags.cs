using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.Items;

namespace NarrativePro.Audio
{
    /// <summary>
    /// Foley 事件标签。对应 UE5 GASPs 风格的 <c>Foley.Event.*</c> 标签体系
    /// （UFoleyAudioBank 的 TMap key 就是这类标签，BP 侧由 I_FoleyAudioBankInterface 提供库）。
    /// </summary>
    public static class NarrativeFoleyTags
    {
        /// <summary>常规行走脚步。</summary>
        public static readonly GameplayTag Feet_Walk = new GameplayTag("Foley.Event.feet.Walk");

        /// <summary>慢走脚步。</summary>
        public static readonly GameplayTag Feet_SlowWalk = new GameplayTag("Foley.Event.feet.SlowWalk");

        /// <summary>冲刺/奔跑脚步。</summary>
        public static readonly GameplayTag Feet_Run = new GameplayTag("Foley.Event.feet.Run");

        /// <summary>起跳。</summary>
        public static readonly GameplayTag Feet_Jump = new GameplayTag("Foley.Event.feet.Jump");

        /// <summary>落地。</summary>
        public static readonly GameplayTag Feet_Land = new GameplayTag("Foley.Event.feet.Land");

        /// <summary>
        /// 按角色的移动状态选出该用哪个脚步事件标签。
        /// 对应 UE5 里脚步 Notify 依据移动模式/速度挑选 foley 事件的做法。
        /// </summary>
        public static GameplayTag ForMovementState(NarrativeCharacterMovement movement)
        {
            if (movement == null) return Feet_Walk;

            if (movement.MovementMode == NarrativeMovementMode.Walking)
            {
                if (movement.IsSprinting()) return Feet_Run;
                if (movement.IsSlowWalking()) return Feet_SlowWalk;
                return Feet_Walk;
            }

            if (movement.IsCustomMovementMode(NarrativeCustomMovementMode.Climb)) return Feet_Walk;

            return Feet_Walk;
        }
    }
}
