using System;

namespace NarrativePro.Tales.Tasks
{
    public class WaitTask : NarrativeTask
    {
        public float WaitDuration { get; set; } = 5f;
        private float _elapsedTime = 0f;

        protected override void BeginTask()
        {
            _elapsedTime = 0f;
            TickInterval = 0.1f;
        }

        protected override void TickTask()
        {
            _elapsedTime += TickInterval;
            int newProgress = (int)Math.Min((_elapsedTime / WaitDuration) * RequiredQuantity, RequiredQuantity);
            if (newProgress != CurrentProgress)
            {
                SetProgress(newProgress);
            }
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return $"等待 {WaitDuration} 秒";
        }
    }
}
