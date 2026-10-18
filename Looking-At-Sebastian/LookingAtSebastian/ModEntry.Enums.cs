namespace LookingAtSebastian;

public sealed partial class ModEntry
{
    private enum CloseStareScenario
    {
        None,
        Side,
        Back,
        Front
    }
    private enum CloseStareStage
    {
        None,

        PcSideWaitingForNoticeEmoteToEnd,
        PcSideWaitingForDialogueOpen,
        PcSideWaitingForDialogueClose,
        PcSideWaitingForLeave,

        FrontLowHeartWaitingForApproach,
        FrontLowHeartWatchingAfterApproach,
        FrontLowHeartWaitingAfterCloseShake,
        FrontLowHeartWaitingForDialogueOpen,
        FrontLowHeartWaitingForDialogueClose,
        FrontLowHeartWaitingForLeave,
        FrontLowHeartWaitingForFinalLeaveReaction,
        
        

        SideWaitingForSecondStare,
        SideWaitingAfterShake,
        SideWaitingForDialogueOpen,
        SideWaitingForDialogueClose,
        SideWaitingForFinalLeaveReaction,
        SideWaitingForLeave,

        BackWaitingForNoticeEmoteToEnd,
        BackWaitingForDialogueOpen,
        BackWaitingForDialogueClose,
        BackWaitingForFinalLeaveReaction,
        BackWaitingForLeave
    }

    private enum BackCloseSequenceKind
    {
        None,
        LowHeart,
        HighHeart,
        Dating8
    }
    private enum SideCloseSequenceKind
    {
        None,
        LowToSix,
        PreDating8,
        Dating8
    }
    private enum FrontCloseSequenceKind
    {
        None,
        LowHeart,
        SixHeart,
        PreDating8,
        Dating8
    }
    private enum PcSideSequenceKind
    {
        None,
        TwoHeart,
        FourHeart,
        SixHeart,
        PreDating8,
        Dating8
    }

}