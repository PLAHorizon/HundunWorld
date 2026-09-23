using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Horizon.Game.GengDi.Core.Services.Database;
using LiteDB;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// 农事日历任务记录（本地 LiteDB 持久化，game 库）。
    /// 让「种植建议 · 农事日历」脱离硬编码演示数据，成为可增删改、可保存的真实功能。
    /// </summary>
    public class FarmCalendarTaskRecord
    {
        public string Id { get; set; } = ObjectId.NewObjectId().ToString();

        /// <summary>任务日期（只精确到天）。</summary>
        public DateTime Date { get; set; }

        /// <summary>任务类型：播种/施肥/病虫害防治/采收/其他。</summary>
        public string TaskType { get; set; } = "其他";

        public string Title { get; set; } = "";

        public string Note { get; set; } = "";

        public bool IsDone { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? DoneAt { get; set; }
    }

    /// <summary>
    /// 农事日历本地存储。任务数据落在 LiteDataContext 的 game 库，
    /// 与客户端其他本地数据同库，随应用保留。只经由 LiteDataContext 的泛型 API 访问，
    /// 集合名由 LiteDB 按类型名生成，保持全链路一致。
    /// </summary>
    public static class FarmCalendarStore
    {
        public static List<FarmCalendarTaskRecord> LoadAll()
        {
            try
            {
                var items = LiteDataContext.Where<FarmCalendarTaskRecord>(LiteDatabaseKind.Game, _ => true);
                return items?.OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ToList() ?? new List<FarmCalendarTaskRecord>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FarmCalendarStore] {nameof(LoadAll)}: {ex.Message}");
                return new List<FarmCalendarTaskRecord>();
            }
        }

        /// <summary>按 Id 保存（存在则更新，不存在则插入）。</summary>
        public static bool Save(FarmCalendarTaskRecord record)
        {
            if (record == null) return false;
            try
            {
                if (LiteDataContext.Update(LiteDatabaseKind.Game, record))
                    return true;
                LiteDataContext.Add(LiteDatabaseKind.Game, record);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FarmCalendarStore] {nameof(Save)}: {ex.Message}");
                return false;
            }
        }

        public static bool Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            try
            {
                return LiteDataContext.DeletedMany<FarmCalendarTaskRecord>(LiteDatabaseKind.Game, x => x.Id == id) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FarmCalendarStore] {nameof(Delete)}: {ex.Message}");
                return false;
            }
        }

        /// <summary>首次使用时写入示例任务；已有数据则不播撒。返回实际写入数量。</summary>
        public static int SeedIfEmpty(IEnumerable<FarmCalendarTaskRecord> seed)
        {
            try
            {
                if (LiteDataContext.Where_<FarmCalendarTaskRecord>(LiteDatabaseKind.Game).Any()) return 0;
                var list = seed?.ToList() ?? new List<FarmCalendarTaskRecord>();
                if (list.Count == 0) return 0;
                return LiteDataContext.AddRange(LiteDatabaseKind.Game, list);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FarmCalendarStore] {nameof(SeedIfEmpty)}: {ex.Message}");
                return 0;
            }
        }
    }
}
