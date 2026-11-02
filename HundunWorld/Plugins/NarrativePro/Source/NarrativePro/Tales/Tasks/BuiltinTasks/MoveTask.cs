using System;
using NarrativePro.Core;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「走/移动够若干距离」的任务条目。对应 UE5 蓝图任务 <c>BPT_Move</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_Move.uasset）。
    /// <para>
    /// 二进制证据：图在 <c>K2_BeginTask</c> 里缓存 <c>CallFunc_K2_GetActorLocation_ReturnValue</c>
    /// 到变量 <c>CachedLoc</c>，<c>TickTask</c> 里用 <c>CallFunc_Vector_Distance_ReturnValue</c>
    /// / <c>CallFunc_Vector_Distance2D_ReturnValue</c>（+ <c>SelectFloat</c> 二选一）累加
    /// <c>DistanceMoved</c>，再经 <c>Divide_DoubleDouble</c> → <c>FClamp</c> →
    /// <c>FFloor</c> → <c>CallFunc_SetProgress</c>（变量 <c>NewProgress</c> / <c>Meters</c>），
    /// 并覆写 <c>GetTaskProgressText</c>（<c>Conv_IntToDouble</c> +
    /// <c>Conv_DoubleToText</c> 的 <c>MaximumFractionalDigits</c> 小数位设置）。
    /// 图里的 <c>CallFunc_IsStandalone_ReturnValue</c> 与注释
    /// （"auth/server/replicated … doesn't move"）说明专用服务器上不结算。
    /// </para>
    /// <para>
    /// 端口差异：UE5 世界单位是厘米，日志里的 <c>Meters</c> 换算在端口不必再做
    /// （Flax 用米），故 <see cref="MetersPerProgress"/> 直接以世界单位计。
    /// </para>
    /// </summary>
    [Serializable]
    public class MoveTask : NarrativeTask
    {
        /// <summary>每推进 1 点进度需要移动的距离（世界单位）。对应 UE5 <c>Meters</c>。</summary>
        public float MetersPerProgress { get; set; } = 1f;

        /// <summary>true 只算水平位移（UE5 的 Distance2D 分支），false 算三维直线距离。</summary>
        public bool bUse2DDistance { get; set; } = true;

        /// <summary>进度文本保留的小数位。对应 <c>Conv_DoubleToText</c> 的 MaximumFractionalDigits。</summary>
        public int ProgressTextFractionalDigits { get; set; } = 1;

        private bool _bHasCache;
        private FlaxEngine.Vector3 _cachedLocation;
        private float _distanceMoved;

        protected override void OnBeginTask()
        {
            _distanceMoved = 0f;
            _bHasCache = false;
            MetersPerProgress = BuiltinTaskSupport.PositiveOr(MetersPerProgress, 1f);
            TickInterval = 0.25f;
            CacheLocation();
        }

        private void CacheLocation()
        {
            var pawn = BuiltinTaskSupport.ResolvePawnActor(this);
            if (pawn == null) return;
            _cachedLocation = pawn.Position;
            _bHasCache = true;
        }

        protected override void TickTask()
        {
            var pawn = BuiltinTaskSupport.ResolvePawnActor(this);
            if (pawn == null) return;

            if (!_bHasCache)
            {
                CacheLocation();
                return;
            }

            var current = pawn.Position;
            float step = bUse2DDistance
                ? NarrativePro.Navigation.NavigatorStatics.Distance2D(_cachedLocation, current)
                : FlaxEngine.Vector3.Distance(_cachedLocation, current);

            _cachedLocation = current;
            _distanceMoved += step;

            // UE5：progress = clamp(floor(已走距离 / 每点所需距离), 0, RequiredQuantity)
            int newProgress = (int)Math.Floor(_distanceMoved / MetersPerProgress);
            if (newProgress < 0) newProgress = 0;
            if (newProgress > RequiredQuantity) newProgress = RequiredQuantity;

            if (newProgress != CurrentProgress)
                SetProgress(newProgress);
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return $"移动 {RequiredQuantity * MetersPerProgress:0.#} 单位距离";
        }

        public override string GetTaskProgressText()
        {
            int digits = ProgressTextFractionalDigits < 0 ? 0 : (ProgressTextFractionalDigits > 4 ? 4 : ProgressTextFractionalDigits);
            string format = "0" + new string('#', Math.Max(0, digits));
            return $"({_distanceMoved.ToString(format)} / {(RequiredQuantity * MetersPerProgress).ToString(format)})";
        }
    }
}
