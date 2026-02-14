using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// DPS计量器
    /// 统计伤害输出、治疗量、DPS、HPS等数据
    /// </summary>
    public class DamageMeter
    {
        private static DamageMeter _instance;
        public static DamageMeter Instance => _instance ??= new DamageMeter();

        // 统计时间窗口（秒）
        private const float StatWindow = 10f;

        // 伤害记录列表
        private List<DamageRecord> _damageRecords = new List<DamageRecord>();

        // 治疗记录列表
        private List<HealRecord> _healRecords = new List<HealRecord>();

        // 是否启用调试日志
        private bool _enableDebugLog = false;

        private DamageMeter()
        {
            Debug.Log("[DamageMeter] 初始化完成");
        }

        /// <summary>
        /// 记录伤害
        /// </summary>
        public void RecordDamage(ulong dealerId, ulong targetId, float damage, bool isCritical, string skillName = "普通攻击")
        {
            _damageRecords.Add(new DamageRecord
            {
                DealerId = dealerId,
                TargetId = targetId,
                Damage = damage,
                IsCritical = isCritical,
                SkillName = skillName,
                Timestamp = Time.GameTime
            });

            if (_enableDebugLog)
                Debug.Log($"[DamageMeter] 记录伤害: {dealerId} -> {targetId}, {damage:F1} ({skillName})");

            // 清理过期记录
            CleanupOldRecords();
        }

        /// <summary>
        /// 记录治疗
        /// </summary>
        public void RecordHeal(ulong healerId, ulong targetId, float healAmount, string skillName = "治疗")
        {
            _healRecords.Add(new HealRecord
            {
                HealerId = healerId,
                TargetId = targetId,
                HealAmount = healAmount,
                SkillName = skillName,
                Timestamp = Time.GameTime
            });

            if (_enableDebugLog)
                Debug.Log($"[DamageMeter] 记录治疗: {healerId} -> {targetId}, {healAmount:F1} ({skillName})");

            CleanupOldRecords();
        }

        /// <summary>
        /// 获取实体的DPS（最近10秒）
        /// </summary>
        public float GetDPS(ulong entityId)
        {
            var recentDamage = _damageRecords
                .Where(r => r.DealerId == entityId && Time.GameTime - r.Timestamp <= StatWindow)
                .Sum(r => r.Damage);

            return recentDamage / StatWindow;
        }

        /// <summary>
        /// 获取实体的瞬时DPS（最近1秒）
        /// </summary>
        public float GetInstantDPS(ulong entityId)
        {
            var recentDamage = _damageRecords
                .Where(r => r.DealerId == entityId && Time.GameTime - r.Timestamp <= 1f)
                .Sum(r => r.Damage);

            return recentDamage;
        }

        /// <summary>
        /// 获取实体的HPS（最近10秒）
        /// </summary>
        public float GetHPS(ulong entityId)
        {
            var recentHeal = _healRecords
                .Where(r => r.HealerId == entityId && Time.GameTime - r.Timestamp <= StatWindow)
                .Sum(r => r.HealAmount);

            return recentHeal / StatWindow;
        }

        /// <summary>
        /// 获取总伤害输出
        /// </summary>
        public float GetTotalDamage(ulong entityId)
        {
            return _damageRecords
                .Where(r => r.DealerId == entityId)
                .Sum(r => r.Damage);
        }

        /// <summary>
        /// 获取总治疗量
        /// </summary>
        public float GetTotalHealing(ulong entityId)
        {
            return _healRecords
                .Where(r => r.HealerId == entityId)
                .Sum(r => r.HealAmount);
        }

        /// <summary>
        /// 获取暴击率
        /// </summary>
        public float GetCriticalRate(ulong entityId)
        {
            var records = _damageRecords.Where(r => r.DealerId == entityId).ToList();
            if (records.Count == 0) return 0;

            var criticalHits = records.Count(r => r.IsCritical);
            return (float)criticalHits / records.Count * 100f;
        }

        /// <summary>
        /// 获取最近的暴击率（最近20次攻击）
        /// </summary>
        public float GetRecentCriticalRate(ulong entityId, int sampleCount = 20)
        {
            var recentRecords = _damageRecords
                .Where(r => r.DealerId == entityId)
                .OrderByDescending(r => r.Timestamp)
                .Take(sampleCount)
                .ToList();

            if (recentRecords.Count == 0) return 0;

            var criticalHits = recentRecords.Count(r => r.IsCritical);
            return (float)criticalHits / recentRecords.Count * 100f;
        }

        /// <summary>
        /// 获取技能伤害分布
        /// </summary>
        public Dictionary<string, float> GetSkillDamageBreakdown(ulong entityId)
        {
            return _damageRecords
                .Where(r => r.DealerId == entityId)
                .GroupBy(r => r.SkillName)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Damage));
        }

        /// <summary>
        /// 获取技能使用次数统计
        /// </summary>
        public Dictionary<string, int> GetSkillUsageCount(ulong entityId)
        {
            return _damageRecords
                .Where(r => r.DealerId == entityId)
                .GroupBy(r => r.SkillName)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// 获取单次最高伤害
        /// </summary>
        public float GetMaxHit(ulong entityId)
        {
            var records = _damageRecords.Where(r => r.DealerId == entityId).ToList();
            return records.Count > 0 ? records.Max(r => r.Damage) : 0;
        }

        /// <summary>
        /// 获取平均伤害
        /// </summary>
        public float GetAverageDamage(ulong entityId)
        {
            var records = _damageRecords.Where(r => r.DealerId == entityId).ToList();
            return records.Count > 0 ? records.Average(r => r.Damage) : 0;
        }

        /// <summary>
        /// 获取攻击次数
        /// </summary>
        public int GetHitCount(ulong entityId)
        {
            return _damageRecords.Count(r => r.DealerId == entityId);
        }

        /// <summary>
        /// 获取最近攻击次数（最近10秒）
        /// </summary>
        public int GetRecentHitCount(ulong entityId)
        {
            return _damageRecords
                .Count(r => r.DealerId == entityId && Time.GameTime - r.Timestamp <= StatWindow);
        }

        /// <summary>
        /// 获取完整统计数据
        /// </summary>
        public DamageStatistics GetStatistics(ulong entityId)
        {
            return new DamageStatistics
            {
                EntityId = entityId,
                TotalDamage = GetTotalDamage(entityId),
                TotalHealing = GetTotalHealing(entityId),
                DPS = GetDPS(entityId),
                InstantDPS = GetInstantDPS(entityId),
                HPS = GetHPS(entityId),
                CriticalRate = GetCriticalRate(entityId),
                MaxHit = GetMaxHit(entityId),
                AverageDamage = GetAverageDamage(entityId),
                HitCount = GetHitCount(entityId),
                RecentHitCount = GetRecentHitCount(entityId),
                SkillBreakdown = GetSkillDamageBreakdown(entityId),
                SkillUsageCount = GetSkillUsageCount(entityId)
            };
        }

        /// <summary>
        /// 清理过期记录
        /// </summary>
        private void CleanupOldRecords()
        {
            // 只保留最近60秒的记录
            const float retentionTime = 60f;
            float cutoffTime = Time.GameTime - retentionTime;
            
            _damageRecords.RemoveAll(r => r.Timestamp < cutoffTime);
            _healRecords.RemoveAll(r => r.Timestamp < cutoffTime);
        }

        /// <summary>
        /// 重置所有统计
        /// </summary>
        public void Reset()
        {
            _damageRecords.Clear();
            _healRecords.Clear();
            Debug.Log("[DamageMeter] 重置所有统计数据");
        }

        /// <summary>
        /// 重置指定实体的统计
        /// </summary>
        public void ResetEntity(ulong entityId)
        {
            _damageRecords.RemoveAll(r => r.DealerId == entityId || r.TargetId == entityId);
            _healRecords.RemoveAll(r => r.HealerId == entityId || r.TargetId == entityId);
            Debug.Log($"[DamageMeter] 重置实体 {entityId} 的统计数据");
        }

        /// <summary>
        /// 设置调试日志
        /// </summary>
        public void SetDebugLog(bool enable)
        {
            _enableDebugLog = enable;
        }

        /// <summary>
        /// 伤害记录
        /// </summary>
        private class DamageRecord
        {
            public ulong DealerId { get; set; }
            public ulong TargetId { get; set; }
            public float Damage { get; set; }
            public bool IsCritical { get; set; }
            public string SkillName { get; set; }
            public float Timestamp { get; set; }
        }

        /// <summary>
        /// 治疗记录
        /// </summary>
        private class HealRecord
        {
            public ulong HealerId { get; set; }
            public ulong TargetId { get; set; }
            public float HealAmount { get; set; }
            public string SkillName { get; set; }
            public float Timestamp { get; set; }
        }
    }

    /// <summary>
    /// 伤害统计数据
    /// </summary>
    public class DamageStatistics
    {
        public ulong EntityId { get; set; }
        public float TotalDamage { get; set; }
        public float TotalHealing { get; set; }
        public float DPS { get; set; }
        public float InstantDPS { get; set; }
        public float HPS { get; set; }
        public float CriticalRate { get; set; }
        public float MaxHit { get; set; }
        public float AverageDamage { get; set; }
        public int HitCount { get; set; }
        public int RecentHitCount { get; set; }
        public Dictionary<string, float> SkillBreakdown { get; set; }
        public Dictionary<string, int> SkillUsageCount { get; set; }

        public string Description()
        {
            return $"总伤害: {TotalDamage:F0}, DPS: {DPS:F1}, 暴击率: {CriticalRate:F1}%, 最高: {MaxHit:F0}, 命中: {HitCount}";
        }
    }
}
