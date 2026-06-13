using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Horizon.Game.GengDi.Core.Services
{
    public class SourceHealthRecord
    {
        public string SourceName { get; set; }
        public int TotalRequests { get; set; }
        public int SuccessRequests { get; set; }
        public int FailedRequests { get; set; }
        public double AverageLatencyMs { get; set; }
        public DateTime LastSuccessTime { get; set; }
        public DateTime LastFailureTime { get; set; }
        public DateTime LastCheckTime { get; set; }
        public bool IsAvailable { get; set; } = true;

        public double SuccessRate => TotalRequests == 0 ? 1.0 : (double)SuccessRequests / TotalRequests;
    }

    public class SourceHealthTracker
    {
        private static SourceHealthTracker _instance;
        private static readonly object _lock = new object();

        public static SourceHealthTracker Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new SourceHealthTracker();
                        }
                    }
                }
                return _instance;
            }
        }

        private readonly Dictionary<string, SourceHealthRecord> _records = new Dictionary<string, SourceHealthRecord>();
        private readonly object _recordsLock = new object();

        public SourceHealthRecord GetRecord(string sourceName)
        {
            lock (_recordsLock)
            {
                if (!_records.ContainsKey(sourceName))
                {
                    _records[sourceName] = new SourceHealthRecord { SourceName = sourceName };
                }
                return _records[sourceName];
            }
        }

        public void RecordSuccess(string sourceName, double latencyMs)
        {
            lock (_recordsLock)
            {
                if (!_records.ContainsKey(sourceName))
                {
                    _records[sourceName] = new SourceHealthRecord { SourceName = sourceName };
                }
                var record = _records[sourceName];
                record.TotalRequests++;
                record.SuccessRequests++;
                record.LastSuccessTime = DateTime.UtcNow;
                record.IsAvailable = true;
                record.LastCheckTime = DateTime.UtcNow;
                record.AverageLatencyMs = (record.AverageLatencyMs * (record.TotalRequests - 1) + latencyMs) / record.TotalRequests;
            }
        }

        public void RecordFailure(string sourceName, double latencyMs)
        {
            lock (_recordsLock)
            {
                if (!_records.ContainsKey(sourceName))
                {
                    _records[sourceName] = new SourceHealthRecord { SourceName = sourceName };
                }
                var record = _records[sourceName];
                record.TotalRequests++;
                record.FailedRequests++;
                record.LastFailureTime = DateTime.UtcNow;
                record.LastCheckTime = DateTime.UtcNow;
                if (record.FailedRequests >= 5)
                {
                    record.IsAvailable = false;
                }
            }
        }

        public List<KeyValuePair<string, SourceHealthRecord>> GetRankedSources()
        {
            lock (_recordsLock)
            {
                return _records
                    .Where(r => r.Value.IsAvailable)
                    .OrderByDescending(r => r.Value.SuccessRate)
                    .ThenByDescending(r => r.Value.LastSuccessTime)
                    .ThenBy(r => r.Value.AverageLatencyMs)
                    .ToList();
            }
        }

        public void ResetSource(string sourceName)
        {
            lock (_recordsLock)
            {
                if (_records.ContainsKey(sourceName))
                {
                    var record = _records[sourceName];
                    record.TotalRequests = 0;
                    record.SuccessRequests = 0;
                    record.FailedRequests = 0;
                    record.IsAvailable = true;
                }
            }
        }

        public void DecayOldRecords(TimeSpan decayThreshold)
        {
            var now = DateTime.UtcNow;
            lock (_recordsLock)
            {
                foreach (var record in _records.Values)
                {
                    if (now - record.LastCheckTime > decayThreshold)
                    {
                        record.TotalRequests = (int)(record.TotalRequests * 0.5);
                        record.SuccessRequests = (int)(record.SuccessRequests * 0.5);
                        record.FailedRequests = (int)(record.FailedRequests * 0.5);
                        if (record.FailedRequests < 5)
                        {
                            record.IsAvailable = true;
                        }
                        record.LastCheckTime = now;
                    }
                }
            }
        }
    }
}
