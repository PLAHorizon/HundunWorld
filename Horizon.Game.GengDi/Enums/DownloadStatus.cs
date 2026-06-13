namespace Horizon.Game.GengDi.Enums
{
    public enum DownloadStatus
    {
        Pending,
        Downloading,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// 下载任务类别，用于区分游戏安装下载、游戏更新下载以及客户端自身更新下载。
    /// </summary>
    public enum DownloadTaskKind
    {
        GameInstall,
        GameUpdate,
        AppUpdate
    }

    /// <summary>
    /// 游戏生命周期状态，UI 上的按钮可用性由此唯一决定。
    /// </summary>
    public enum GameLifecycleState
    {
        NotInstalled,
        Downloading,
        DownloadPaused,
        Installing,
        Installed,
        Updating,
        Uninstalling,
        Running,
        Failed
    }
}