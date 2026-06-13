using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.UI
{
    public class NarrativeNotification : Script
    {
        public float DisplayDuration = 3f;
        public float FadeOutDuration = 1f;

        private List<NotificationEntry> _activeNotifications = new List<NotificationEntry>();

        public class NotificationEntry
        {
            public string Title;
            public string Message;
            public float TimeRemaining;
            public bool bFading;
        }

        public void ShowNotification(string title, string message)
        {
            var entry = new NotificationEntry
            {
                Title = title,
                Message = message,
                TimeRemaining = DisplayDuration,
                bFading = false
            };
            _activeNotifications.Add(entry);
        }

        public override void OnUpdate()
        {
            float deltaTime = Time.DeltaTime;

            for (int i = _activeNotifications.Count - 1; i >= 0; i--)
            {
                var entry = _activeNotifications[i];
                entry.TimeRemaining -= deltaTime;

                if (!entry.bFading && entry.TimeRemaining <= FadeOutDuration)
                {
                    entry.bFading = true;
                }

                if (entry.TimeRemaining <= 0f)
                {
                    _activeNotifications.RemoveAt(i);
                }
            }
        }

        public List<NotificationEntry> GetActiveNotifications()
        {
            return _activeNotifications;
        }

        public static void ShowQuestStarted(string questName)
        {
            var instance = FindInstance();
            if (instance != null)
            {
                instance.ShowNotification("Quest Started", questName);
            }
        }

        public static void ShowQuestCompleted(string questName)
        {
            var instance = FindInstance();
            if (instance != null)
            {
                instance.ShowNotification("Quest Completed", questName);
            }
        }

        public static void ShowQuestFailed(string questName)
        {
            var instance = FindInstance();
            if (instance != null)
            {
                instance.ShowNotification("Quest Failed", questName);
            }
        }

        private static NarrativeNotification FindInstance()
        {
            return null;
        }
    }
}
