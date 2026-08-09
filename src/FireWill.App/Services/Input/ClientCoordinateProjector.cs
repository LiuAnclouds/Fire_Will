namespace FireWill.App.Services.Input;

public readonly record struct ScreenProjectionContext(
    ScreenRectangle ClientBounds,
    double ProjectionAspectRatio)
{
    public bool IsValid =>
        ClientBounds.Width > 0 &&
        ClientBounds.Height > 0 &&
        double.IsFinite(ProjectionAspectRatio) &&
        ProjectionAspectRatio > 0d;
}

/// <summary>
/// Projects a point stored as client-area ratios into the current desktop coordinate space.
/// </summary>
public static class ClientCoordinateProjector
{
    internal static bool IsNormalizedRatio(double? value) =>
        value is >= 0d and <= 1d && double.IsFinite(value.Value);

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

    public static bool TryNormalize(
        ScreenPoint screenPoint,
        ScreenProjectionContext context,
        out double xRatio,
        out double yRatio,
        out double captureAspectRatio)
    {
        captureAspectRatio = 0d;
        if (!TryNormalize(screenPoint, context.ClientBounds, out xRatio, out yRatio) ||
            !context.IsValid)
        {
            xRatio = 0d;
            yRatio = 0d;
            return false;
        }

        captureAspectRatio = context.ProjectionAspectRatio;
        return true;
    }

    public static ScreenPoint ProjectOrFallback(
        ScreenPoint absoluteFallback,
        double? xRatio,
        double? yRatio,
        ScreenRectangle? clientBounds)
    {
        if (!IsNormalizedRatio(xRatio) || !IsNormalizedRatio(yRatio) ||
            clientBounds is not { Width: > 0, Height: > 0 } bounds)
        {
            return absoluteFallback;
        }

        var clientX = (int)Math.Round(
            xRatio!.Value * (bounds.Width - 1d),
            MidpointRounding.AwayFromZero);
        var clientY = (int)Math.Round(
            yRatio!.Value * (bounds.Height - 1d),
            MidpointRounding.AwayFromZero);

        return new ScreenPoint(
            AddSaturating(bounds.X, clientX),
            AddSaturating(bounds.Y, clientY));
    }

    /// <summary>
    /// Projects a point captured from a widescreen Warcraft III camera. The
    /// helper's projection scales the horizontal field of view with the outer
    /// window aspect ratio, while the vertical projection scales with the
    /// client height. Keeping the source aspect ratio lets a point survive a
    /// transition between 16:9 and 4:3 windows without converting old pixels.
    /// </summary>
    public static ScreenPoint ProjectWidescreenOrFallback(
        ScreenPoint absoluteFallback,
        double? xRatio,
        double? yRatio,
        double? captureAspectRatio,
        ScreenProjectionContext? currentContext)
    {
        if (!IsNormalizedRatio(xRatio) ||
            !IsNormalizedRatio(yRatio) ||
            captureAspectRatio is not > 0d ||
            !double.IsFinite(captureAspectRatio.Value) ||
            currentContext is not { } context ||
            !context.IsValid)
        {
            return absoluteFallback;
        }

        var bounds = context.ClientBounds;
        var currentAspect = context.ProjectionAspectRatio;

        // The camera is centered in the client area. X's distance from the
        // center is adjusted by the ratio of source/current projection
        // aspects; Y follows the client height directly.
        var centeredX = 0.5d +
            (xRatio!.Value - 0.5d) * captureAspectRatio.Value / currentAspect;
        var clientX = (int)Math.Round(
            centeredX * (bounds.Width - 1d),
            MidpointRounding.AwayFromZero);
        var clientY = (int)Math.Round(
            yRatio!.Value * (bounds.Height - 1d),
            MidpointRounding.AwayFromZero);

        return new ScreenPoint(
            AddSaturating(bounds.X, clientX),
            AddSaturating(bounds.Y, clientY));
    }

    private static int AddSaturating(int origin, int offset) =>
        (int)Math.Clamp((long)origin + offset, int.MinValue, int.MaxValue);
}
