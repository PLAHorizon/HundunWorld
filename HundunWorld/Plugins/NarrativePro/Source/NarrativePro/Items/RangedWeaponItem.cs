using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 远程武器（枪械/弓）。适配 UE5 URangedWeaponItem。
    /// 包含远程武器专属的散布、后坐力、瞄准 FOV 等配置。攻击逻辑由授予的战斗能力处理。
    /// </summary>
    public class RangedWeaponItem : WeaponItem
    {
        /// <summary>投射物资源路径（如弓箭发射的箭），空表示使用射线追踪</summary>
        public string ProjectilePath { get; set; } = "";

        /// <summary>射速（发/分钟）</summary>
        public float FireRate { get; set; } = 600f;

        /// <summary>是否全自动（按住即可连续发射）</summary>
        public bool bAutomatic { get; set; } = false;

        /// <summary>
        /// 瞄准时相对基础 FOV 的百分比。1=无缩放，0.1=大幅缩放。
        /// 对应 UE5 AimFOVPct（ClampMin=0.1, ClampMax=1.0）。
        /// </summary>
        public float AimFOVPct { get; set; } = 1f;

        /// <summary>
        /// 瞄准时武器的渲染 FOV。固定值以保证所有武器瞄准构图一致，
        /// 避免设置武器 FOV 时瞄具/照门大小变化。
        /// </summary>
        public float AimWeaponRenderFOV { get; set; } = 0f;

        /// <summary>
        /// 瞄准时武器的渲染光圈值（FStop）。固定值以保证瞄准构图一致。
        /// </summary>
        public float AimWeaponFStop { get; set; } = 0f;

        /// <summary>瞄准时基础散布角度（度）</summary>
        public float BaseSpreadDegrees { get; set; } = 0f;

        /// <summary>满速移动时额外增加的散布角度；半速则加一半，以此类推</summary>
        public float MoveSpeedAddDegrees { get; set; } = 0f;

        /// <summary>蹲伏时当前散布乘以此系数</summary>
        public float CrouchSpreadMultiplier { get; set; } = 1f;

        /// <summary>瞄准时当前散布乘以此系数</summary>
        public float AimSpreadMultiplier { get; set; } = 1f;

        /// <summary>武器开火时附加到散布的角度</summary>
        public float SpreadFireBump { get; set; } = 0f;

        /// <summary>允许的最大散布角度</summary>
        public float MaxSpreadDegrees { get; set; } = 10f;

        /// <summary>当前散布恢复到基础散布的速度</summary>
        public float SpreadDecreaseSpeed { get; set; } = 1f;

        /// <summary>当前散布角度（运行时）</summary>
        public float CurrentSpread { get; set; } = 0f;

        /// <summary>后坐力平移冲量最小值</summary>
        public Vector3 RecoilImpulseTranslationMin { get; set; } = Vector3.Zero;

        /// <summary>后坐力平移冲量最大值</summary>
        public Vector3 RecoilImpulseTranslationMax { get; set; } = Vector3.Zero;

        /// <summary>腰射后坐力平移冲量最小值</summary>
        public Vector3 HipRecoilImpulseTranslationMin { get; set; } = Vector3.Zero;

        /// <summary>腰射后坐力平移冲量最大值</summary>
        public Vector3 HipRecoilImpulseTranslationMax { get; set; } = Vector3.Zero;

        /// <summary>开火时使用的追踪数据</summary>
        public CombatTraceData TraceData { get; set; } = new CombatTraceData();

        public RangedWeaponItem()
        {
            // 初始化散布为基础散布
            CurrentSpread = BaseSpreadDegrees;
        }

        public override void HandleWield()
        {
            base.HandleWield();
            // 装备时启用 Tick 以便处理散布恢复
            EnableItemTick(true);
        }

        public override void HandleUnWield()
        {
            base.HandleUnWield();
            // 收起时禁用 Tick
            EnableItemTick(false);
        }

        public override void TickItem(float deltaTime)
        {
            base.TickItem(deltaTime);
            if (IsWielded())
            {
                TickSpread(deltaTime);
            }
        }

        /// <summary>每帧更新散布，使其逐步恢复到基础散布。</summary>
        public virtual void TickSpread(float deltaTime)
        {
            if (SpreadDecreaseSpeed <= 0f) return;
            if (CurrentSpread > BaseSpreadDegrees)
            {
                CurrentSpread = Math.Max(BaseSpreadDegrees, CurrentSpread - SpreadDecreaseSpeed * deltaTime);
            }
            else if (CurrentSpread < BaseSpreadDegrees)
            {
                CurrentSpread = BaseSpreadDegrees;
            }
        }

        public override void OnAttack()
        {
            base.OnAttack();
            AddSpread();
        }

        /// <summary>开火时增加散布。</summary>
        public virtual void AddSpread()
        {
            CurrentSpread = Math.Min(MaxSpreadDegrees, CurrentSpread + SpreadFireBump);
        }

        public override float GetWeaponSpread() => CurrentSpread;

        /// <summary>获取此武器的追踪数据。</summary>
        public virtual CombatTraceData GetTraceData() => TraceData;

        public override float GetAttackRange() => TraceData?.Range ?? BotAttackRange;

        /// <summary>获取瞄准时相对默认 FOV 的百分比。</summary>
        public virtual float GetAimFOV() => AimFOVPct;

        /// <summary>获取瞄准时希望的武器渲染 FOV。</summary>
        public virtual float GetAimWeaponRenderFOV() => AimWeaponRenderFOV;

        /// <summary>获取瞄准时应用的 PP 设置 FStop。</summary>
        public virtual float GetAimFStop() => AimWeaponFStop;

        /// <summary>获取一个可应用的随机后坐力冲量变换。</summary>
        public Transform GetRecoilImpulse()
        {
            var min = RecoilImpulseTranslationMin;
            var max = RecoilImpulseTranslationMax;
            var rnd = new Random();
            float rx = (max.X - min.X) <= 0f ? min.X : min.X + (float)(rnd.NextDouble() * (max.X - min.X));
            float ry = (max.Y - min.Y) <= 0f ? min.Y : min.Y + (float)(rnd.NextDouble() * (max.Y - min.Y));
            float rz = (max.Z - min.Z) <= 0f ? min.Z : min.Z + (float)(rnd.NextDouble() * (max.Z - min.Z));
            return new Transform(new Vector3(rx, ry, rz));
        }

        public override List<string> GetComboAnims(bool bHeavyAttack) => new System.Collections.Generic.List<string>();
    }
}
