namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 消息速率限制器
    /// </summary>
    public class MessageRateLimiter
    {
        private readonly int _maxMessagesPerWindow;
        private readonly TimeSpan _windowDuration;
        private readonly Dictionary<long, Queue<DateTime>> _playerMessageTimes = new();

        public MessageRateLimiter(int maxMessagesPerWindow = 10, int windowSeconds = 60)
        {
            _maxMessagesPerWindow = maxMessagesPerWindow;
            _windowDuration = TimeSpan.FromSeconds(windowSeconds);
        }

        public bool IsRateLimited(long playerId)
        {
            CleanupExpired(playerId);
            if (!_playerMessageTimes.TryGetValue(playerId, out var queue))
                return false;
            return queue.Count >= _maxMessagesPerWindow;
        }

        public void RecordMessage(long playerId)
        {
            CleanupExpired(playerId);
            if (!_playerMessageTimes.TryGetValue(playerId, out var queue))
            {
                queue = new Queue<DateTime>();
                _playerMessageTimes[playerId] = queue;
            }
            queue.Enqueue(DateTime.Now);
        }

        public int GetRemainingMessages(long playerId)
        {
            CleanupExpired(playerId);
            if (!_playerMessageTimes.TryGetValue(playerId, out var queue))
                return _maxMessagesPerWindow;
            return Math.Max(0, _maxMessagesPerWindow - queue.Count);
        }

        public void Reset(long playerId)
        {
            _playerMessageTimes.Remove(playerId);
        }

        public void ResetAll()
        {
            _playerMessageTimes.Clear();
        }

        private void CleanupExpired(long playerId)
        {
            if (!_playerMessageTimes.TryGetValue(playerId, out var queue))
                return;
            var cutoff = DateTime.Now - _windowDuration;
            while (queue.Count > 0 && queue.Peek() < cutoff)
                queue.Dequeue();
        }
    }
}
