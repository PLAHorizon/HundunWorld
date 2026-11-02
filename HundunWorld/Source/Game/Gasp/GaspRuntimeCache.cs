using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 进程内共享 GASP 数据：动画库与逐帧特征库按“资产路径 + 骨架”缓存。
    /// 多个本地/远端角色复用同一份只读数据，避免每个角色重复加载 3MB 与重建 962 帧特征。
    /// 注意：当前 GASP 只挂 Ada 身体；不同骨架会各自缓存（GaspClip 的 NodeIndex 映射与骨架相关）。
    /// </summary>
    public static class GaspRuntimeCache
    {
        private static readonly object Locker = new object();
        private static readonly Dictionary<string, GaspAnimationDatabase> Databases = new Dictionary<string, GaspAnimationDatabase>();
        private static readonly Dictionary<string, GaspMotionDatabase> MotionDatabases = new Dictionary<string, GaspMotionDatabase>();

        public static int DatabaseHits { get; private set; }
        public static int DatabaseLoads { get; private set; }
        public static int MotionHits { get; private set; }
        public static int MotionBuilds { get; private set; }

        public static GaspAnimationDatabase GetDatabase(string assetPath)
        {
            lock (Locker)
            {
                string key = "asset:" + assetPath;
                if (Databases.TryGetValue(key, out var cached) && cached.IsLoaded)
                {
                    DatabaseHits++;
                    return cached;
                }

                var db = GaspAnimationDatabase.LoadFromContent(assetPath);
                if (!db.IsLoaded)
                    db = GaspAnimationDatabase.Load(GaspAnimationDatabase.ResolveDefaultPath());
                Databases[key] = db;
                if (db.IsLoaded)
                    DatabaseLoads++;
                Debug.Log($"[GaspRuntimeCache] db {(db.IsLoaded ? "load" : "fail")} key={key} err={db.Error}");
                return db;
            }
        }

        public static GaspMotionDatabase GetMotionDatabase(GaspAnimationDatabase db, GaspSkeleton skeleton, string skeletonKey)
        {
            lock (Locker)
            {
                string key = (db != null ? db.SourcePath : "<null>") + "|" + skeletonKey;
                if (MotionDatabases.TryGetValue(key, out var cached) && cached.IsBuilt)
                {
                    MotionHits++;
                    return cached;
                }

                var motion = new GaspMotionDatabase();
                bool built = motion.Build(db, skeleton);
                MotionDatabases[key] = motion;
                if (built)
                    MotionBuilds++;
                Debug.Log($"[GaspRuntimeCache] motion {(built ? "build" : "fail")} key={key} ms={motion.BuildMilliseconds:F1} poses={motion.Poses.Count} err={motion.Error}");
                return motion;
            }
        }

        public static string Summary =>
            $"dbHit={DatabaseHits} dbLoad={DatabaseLoads} motionHit={MotionHits} motionBuild={MotionBuilds}";
    }
}
