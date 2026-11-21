namespace GabrielAutoclick.Models
{
    public enum AutoClickStopReason
    {
        UserButton,
        GlobalHotkey,
        PanicF12,
        RepetitionCompleted,
        TimeLimitCompleted,
        SafeZone,
        ForegroundRestriction,
        ApplicationExit,
        InternalError
    }
}
