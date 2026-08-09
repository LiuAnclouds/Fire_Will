namespace FireWill.App.Services.Input;

/// <summary>
/// Projects a point stored as client-area ratios into the current desktop coordinate space.
/// </summary>
public static class ClientCoordinateProjector
{
    public static bool TryNormalize(
        ScreenPoint screenPoint,
        ScreenRectangle clientBounds,
        out double xRatio,
        out double yRatio)
    {
        xRatio = 0d;
        yRatio = 0d;
        if (clientBounds.Width <= 0 || clientBounds.Height <= 0)
        {
            return false;
        }

        var clientX = (long)screenPoint.X - clientBounds.X;
        var clientY = (long)screenPoint.Y - clientBounds.Y;
        if (clientX < 0 || clientX >= clientBounds.Width ||
            clientY < 0 || clientY >= clientBounds.Height)
        {
            return false;
        }

        xRatio = clientBounds.Width == 1
            ? 0d
            : clientX / (clientBounds.Width - 1d);
        yRatio = clientBounds.Height == 1
            ? 0d
            : clientY / (clientBounds.Height - 1d);
        return true;
    }

    public static ScreenPoint ProjectOrFallback(
        ScreenPoint absoluteFallback,
        double? xRatio,
        double? yRatio,
        ScreenRectangle? clientBounds)
    {
        if (xRatio is null || yRatio is null ||
            !double.IsFinite(xRatio.Value) || !double.IsFinite(yRatio.Value) ||
            clientBounds is not { Width: > 0, Height: > 0 } bounds)
        {
            return absoluteFallback;
        }

        var normalizedX = Math.Clamp(xRatio.Value, 0d, 1d);
        var normalizedY = Math.Clamp(yRatio.Value, 0d, 1d);
        var clientX = (int)Math.Round(
            normalizedX * (bounds.Width - 1d),
            MidpointRounding.AwayFromZero);
        var clientY = (int)Math.Round(
            normalizedY * (bounds.Height - 1d),
            MidpointRounding.AwayFromZero);

        return new ScreenPoint(
            AddSaturating(bounds.X, clientX),
            AddSaturating(bounds.Y, clientY));
    }

    private static int AddSaturating(int origin, int offset) =>
        (int)Math.Clamp((long)origin + offset, int.MinValue, int.MaxValue);
}
