namespace MetaHuman
{
    /// <summary>
    /// 远端外观副本的新旧判定。服务端会主动推、客户端进场也会拉，两条路共用
    /// <c>OnRemoteAppearanceResponse</c>，所以乱序/迟到副本必须靠这条规则挡住，
    /// 否则远端角色会退回旧脸。单独成函数是为了让纯核工装能直接断言（规则反了不会只是"看着怪"）。
    /// </summary>
    public static class MetaHumanLookRevisionPolicy
    {
        /// <summary>收到的副本是否比缓存旧（只有"严格更旧"才丢；同修订号允许重放，幂等）。</summary>
        public static bool IsStale(long cachedRevision, long incomingRevision)
        {
            return cachedRevision > incomingRevision;
        }
    }
}
