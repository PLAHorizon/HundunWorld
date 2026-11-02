namespace NarrativePro.Core
{
    public enum EQuestCompletion
    {
        NotStarted,
        Started,
        Succeeded,
        Failed
    }

    public enum EStateNodeType
    {
        Regular,
        Success,
        Failure
    }

    public enum ELineDuration
    {
        Default,
        WhenAudioEnds,
        WhenSequenceEnds,
        AfterReadingTime,
        AfterDuration,
        Never
    }

    public enum EExitDialogueReason
    {
        NoLines,
        PlayerExited,
        TooFarAway,
        NewDialogueStarted,
        StoppedByCinematic
    }

    public enum EEventRuntime
    {
        Start,
        End,
        Both
    }

    public enum EEventFilter
    {
        Anyone,
        OnlyNPCs,
        OnlyPlayers
    }

    /// <summary>
    /// 叙事复制更新类型。对应 UE5 <c>EUpdateType</c>（NarrativeUpdate.h）。
    /// <para>
    /// <b>顺序敏感</b>：值会随网络包发送（<c>SendNarrativeMessageAsync(json, (int)UpdateType)</c>），
    /// 新类型只能追加在末尾，不能插入中间。
    /// </para>
    /// </summary>
    public enum EUpdateType
    {
        None,
        CompleteTask,
        BeginQuest,
        ForgetQuest,
        RestartQuest,
        QuestNewState,
        TaskProgressMade,

        /// <summary>客户端请求服务器退出当前对话。对应 UE5 <c>ServerTryExitDialogue</c> 的复制载荷。</summary>
        DialogueExit,

        /// <summary>客户端请求服务器跳过当前对话行。对应 UE5 <c>ServerTrySkipCurrentDialogueLine</c> 的复制载荷。</summary>
        DialogueSkip
    }

    /// <summary>
    /// 事件在队伍对话/任务中的执行策略。对应 UE5 <c>EPartyEventPolicy</c>（NarrativeEvent.h:12-24）。
    /// <para>
    /// <b>顺序敏感</b>：UE5 用 <c>PartyEventPolicy > EPartyEventPolicy::Party</c> 判定
    /// 「需要遍历队员」，因此 Party 必须为 0。
    /// </para>
    /// </summary>
    public enum EPartyEventPolicy
    {
        /// <summary>事件跑在队伍本身（队伍的 Narrative 组件）上。例如为全队开始一个任务。</summary>
        Party,

        /// <summary>事件跑在每个队员上。例如给奖励时全队都会拿到。</summary>
        AllPartyMembers,

        /// <summary>事件只跑在队长上。例如只有队长能拿到奖励。</summary>
        PartyLeader
    }

    /// <summary>
    /// 条件在队伍对话中的判定策略。对应 UE5 <c>EPartyConditionPolicy</c>（NarrativeCondition.h:11-28）。
    /// <para>
    /// <b>顺序敏感</b>：UE5 用 <c>PartyConditionPolicy > EPartyConditionPolicy::None</c> 判定是否走队伍分支，
    /// 因此 None 必须为 0。
    /// </para>
    /// </summary>
    public enum EPartyConditionPolicy
    {
        /// <summary>不在队伍上跑此条件。</summary>
        None,

        /// <summary>在所有队员上跑，任一人通过即通过。</summary>
        AnyPlayerPasses,

        /// <summary>在队伍自身的 Narrative 组件上跑。</summary>
        PartyPasses,

        /// <summary>在所有队员上跑，需全部通过才通过。</summary>
        AllPlayersPass,

        /// <summary>在队长上跑，队长通过则通过。</summary>
        PartyLeaderPasses
    }

    /// <summary>
    /// 条件允许跑在什么角色上。对应 UE5 <c>EConditionFilter</c>（NarrativeCondition.h:34-44）。</summary>
    public enum EConditionFilter
    {
        /// <summary>此条件不针对具体角色（例如判定时间）。</summary>
        DontTarget,

        /// <summary>可跑在任意角色上。</summary>
        AnyCharacter,

        /// <summary>只能跑在 NPC 上（判定 AI 相关状态）。</summary>
        OnlyNPCs,

        /// <summary>只能跑在玩家角色上。</summary>
        OnlyPlayers
    }
}
