namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 跨页密聊请求：好友页点「私聊」时先把对象记在这里，再跳回战斗 HUD；
    /// HUD 构造完成时消费一次，进入对应密聊会话。
    /// <para>
    /// 用一次性静态交接而不是让好友页直接抓 HUD 实例：HUD 可能被路由重建
    /// （切换传统/V2、场景切换），页面之间不应持有彼此的控件引用。
    /// </para>
    /// </summary>
    public static class WhisperRequest
    {
        private static ulong _targetId;
        private static string _targetName;

        public static void Set(ulong targetId, string targetName)
        {
            _targetId = targetId;
            _targetName = targetName;
        }

        /// <summary>取走请求（取后清零，避免下次打开 HUD 又进同一个密聊）。</summary>
        public static bool TryConsume(out ulong targetId, out string targetName)
        {
            targetId = _targetId;
            targetName = _targetName;

            bool hasRequest = _targetId != 0UL;
            _targetId = 0UL;
            _targetName = null;
            return hasRequest;
        }
    }
}
