using System;

namespace NarrativePro.Tales.Tasks
{
    public abstract class NarrativeTask
    {
        public int RequiredQuantity { get; set; } = 1;
        public int CurrentProgress { get; protected set; } = 0;
        public string DescriptionOverride { get; set; } = "";
        public bool bOptional { get; set; } = false;
        public bool bHidden { get; set; } = false;
        public float TickInterval { get; set; } = 0f;
        public bool bIsActive { get; protected set; } = false;
        public object OwningComp { get; set; }
        public object OwningPawn { get; set; }
        public object OwningController { get; set; }

        internal Action<NarrativeTask, int, int> ProgressChangedCallback { get; set; }

        public void BeginTaskInit()
        {
            bIsActive = true;
            BeginTask();
        }

        protected virtual void BeginTask() { }
        protected virtual void TickTask() { }
        public virtual void EndTask() { bIsActive = false; }

        public virtual void SetProgress(int newProgress)
        {
            int oldProgress = CurrentProgress;
            CurrentProgress = Math.Max(0, Math.Min(newProgress, RequiredQuantity));
            OnProgressChanged(oldProgress, CurrentProgress);
        }

        public virtual void AddProgress(int progressToAdd = 1)
        {
            SetProgress(CurrentProgress + progressToAdd);
        }

        public virtual void CompleteTask()
        {
            SetProgress(RequiredQuantity);
        }

        public virtual bool IsComplete()
        {
            return CurrentProgress >= RequiredQuantity;
        }

        public virtual string GetTaskDescription()
        {
            return !string.IsNullOrEmpty(DescriptionOverride) ? DescriptionOverride : GetType().Name;
        }

        public virtual string GetTaskProgressText()
        {
            if (RequiredQuantity <= 1) return "";
            return $"({CurrentProgress}/{RequiredQuantity})";
        }

        protected virtual void OnProgressChanged(int oldProgress, int newProgress)
        {
            ProgressChangedCallback?.Invoke(this, oldProgress, newProgress);
        }
    }
}
