using System;

namespace MetaHuman.Core
{
    /// <summary>
    /// 本机与服务端各有一份“已发布”外观时的仲裁结果。
    /// </summary>
    public enum AppearanceArbitration
    {
        /// <summary>两份 blob 相同，无需覆盖，也不算冲突。</summary>
        Identical = 0,

        /// <summary>保留本机那份（本机 revision 不小于服务端，或 revision 不可比）。</summary>
        UseLocal = 1,

        /// <summary>用服务端那份覆盖本机（服务端 revision 更大）。</summary>
        UseServer = 2,
    }

    /// <summary>
    /// 外观仲裁纯核：口径来自用户拍板（2026-10-20）——“比修订号”，revision 大者生效。
    /// 同 revision 但校验和不同属于契约上不该出现的情况；此时保守保留本机，
    /// 由调用方把两个校验和写进 LastErrorCode，避免静默用远端覆盖玩家刚发布的版本。
    /// </summary>
    public static class MetaHumanAppearanceArbitration
    {
        public static AppearanceArbitration Choose(int localRevision, int serverRevision,
                                                   uint localChecksum, uint serverChecksum)
        {
            return Choose((long)localRevision, (long)serverRevision, localChecksum, serverChecksum);
        }

        /// <summary>
        /// 长整型修订号（当前用 blob 自带的 SavedAtUtcTicks 作为单调序；契约加上独立 revision 后直接换源即可）。
        /// </summary>
        public static AppearanceArbitration Choose(long localRevision, long serverRevision,
                                                   uint localChecksum, uint serverChecksum)
        {
            if (localChecksum != 0u && localChecksum == serverChecksum)
                return AppearanceArbitration.Identical;

            if (serverRevision > localRevision)
                return AppearanceArbitration.UseServer;

            return AppearanceArbitration.UseLocal;
        }

        /// <summary>
        /// 生产口径：两份都带独立修订号（&gt;0）时按修订号比；任一份是旧档（0）就整体回退到
        /// blob 自带的保存时刻。**不能逐字段混用**——旧档的 ticks 与新的小整数计数不在同一条时间轴上。
        /// </summary>
        public static AppearanceArbitration ChooseLooks(
            long localRevision, long localTicks, uint localChecksum,
            long serverRevision, long serverTicks, uint serverChecksum,
            out string basis)
        {
            if (localRevision > 0 && serverRevision > 0)
            {
                basis = "修订号";
                return Choose(localRevision, serverRevision, localChecksum, serverChecksum);
            }

            basis = "保存时刻";
            return Choose(localTicks, serverTicks, localChecksum, serverChecksum);
        }

        /// <summary>给日志/界面用的一句话结论（不含校验和细节）。</summary>
        public static string Describe(AppearanceArbitration result)
        {
            switch (result)
            {
                case AppearanceArbitration.Identical:
                    return "本机与服务端已发布存档一致（无冲突）";
                case AppearanceArbitration.UseServer:
                    return "服务端修订号更大，采用服务端外观";
                default:
                    return "本机修订号不小于服务端，保留本机外观";
            }
        }
    }
}
