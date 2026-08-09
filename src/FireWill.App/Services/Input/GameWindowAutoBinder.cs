namespace FireWill.App.Services.Input;

public enum GameWindowAutoBindState
{
    AlreadyBound,
    BoundNow,
    Waiting,
}

public readonly record struct GameWindowAutoBindResult(
    GameWindowAutoBindState State,
    War3WindowBinding? Binding);

public sealed class GameWindowAutoBinder(
    Func<bool> isBindingAlive,
    Func<War3WindowBinding?> findAndBind)
{
    public GameWindowAutoBindResult Poll()
    {
        if (isBindingAlive())
        {
            return new GameWindowAutoBindResult(GameWindowAutoBindState.AlreadyBound, null);
        }

        var binding = findAndBind();
        return binding is null
            ? new GameWindowAutoBindResult(GameWindowAutoBindState.Waiting, null)
            : new GameWindowAutoBindResult(GameWindowAutoBindState.BoundNow, binding);
    }
}
