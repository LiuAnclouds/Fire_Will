namespace FireWill.Core.Execution;

public abstract record MacroAction;

public sealed record KeyPressAction(
    string Key,
    int HoldMilliseconds,
    bool HoldIsInterruptible,
    int RestMilliseconds = 0,
    bool RestIsInterruptible = true) : MacroAction;

public sealed record MoveMouseAction(int X, int Y) : MacroAction;

public sealed record LeftClickAction(int HoldMilliseconds = 10) : MacroAction;

public sealed record SendChatAction(string Text) : MacroAction;

public sealed record DelayAction(
    int Milliseconds,
    bool IsInterruptible = true,
    string Reason = "") : MacroAction;

public sealed record WarningAction(string Message) : MacroAction;

// ExecuteFlow checks stopRequested after the pre-command, but ExecuteFarmStep
// itself is atomic in the legacy AHK implementation.
public sealed record StopBoundaryAction : MacroAction;

public sealed record CompiledGroup(
    int Slot,
    IReadOnlyList<MacroAction> Actions,
    int CountedActionDurationMs,
    int WaitMilliseconds);

public sealed record CompiledFlow(
    int Slot,
    string Name,
    bool Enabled,
    IReadOnlyList<CompiledGroup> Groups);
