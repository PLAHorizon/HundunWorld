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

    public enum EUpdateType
    {
        None,
        CompleteTask,
        BeginQuest,
        ForgetQuest,
        RestartQuest,
        QuestNewState,
        TaskProgressMade
    }

    public enum EPartyEventPolicy
    {
        OnlyTriggerForOwningPlayer,
        TriggerForAllPlayers,
        OnlyTriggerForNonOwningPlayers
    }
}
