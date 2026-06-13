namespace Horizon.Game.GengDi.Enums
{
    public enum PlayMode
    {
        Sequential,
        LoopOne,
        LoopAll,
        Shuffle
    }

    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused,
        Loading,
        Error
    }

    public enum SongSource
    {
        Local,
        Remote,
        Cloud
    }

    public enum MusicRankingType
    {
        Hot,
        New,
        Rising
    }
}
