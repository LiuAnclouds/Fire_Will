using FireWill.App.Services.Input;

namespace FireWill.App.Tests;

public sealed class ClientCoordinateProjectorTests
{
    [Fact]
    public void TryNormalize_ProjectionContextReturnsOuterWindowAspect()
    {
        var context = new ScreenProjectionContext(
            new ScreenRectangle(25, 66, 1065, 773),
            1073d / 804d);

        var normalized = ClientCoordinateProjector.TryNormalize(
            new ScreenPoint(719, 543),
            context,
            out var xRatio,
            out var yRatio,
            out var captureAspectRatio);

        Assert.True(normalized);
        Assert.Equal(694d / 1064d, xRatio);
        Assert.Equal(477d / 772d, yRatio);
        Assert.Equal(1073d / 804d, captureAspectRatio);
    }

    [Fact]
    public void ProjectWidescreen_ChangingFrom16By9To4By3ScalesFromClientCenter()
    {
        var projected = ClientCoordinateProjector.ProjectWidescreenOrFallback(
            default,
            xRatio: 0.75d,
            yRatio: 0.25d,
            captureAspectRatio: 16d / 9d,
            currentContext: new ScreenProjectionContext(
                new ScreenRectangle(0, 0, 1024, 768),
                4d / 3d));

        Assert.InRange(Math.Abs(projected.X - 853), 0, 1);
        Assert.Equal(192, projected.Y);
    }

    [Fact]
    public void ProjectWidescreen_UsesOuterAspectAndCurrentClientPixelSpan()
    {
        var sourceContext = new ScreenProjectionContext(
            new ScreenRectangle(0, 0, 1920, 1080),
            16d / 9d);
        Assert.True(ClientCoordinateProjector.TryNormalize(
            new ScreenPoint(1179, 667),
            sourceContext,
            out var xRatio,
            out var yRatio,
            out var captureAspectRatio));

        var projected = ClientCoordinateProjector.ProjectWidescreenOrFallback(
            default,
            xRatio,
            yRatio,
            captureAspectRatio,
            new ScreenProjectionContext(
                new ScreenRectangle(25, 66, 1065, 773),
                1073d / 804d));

        Assert.Equal(new ScreenPoint(719, 543), projected);
    }

    [Fact]
    public void ProjectWidescreen_SameAspectMatchesIndependentClientRatios()
    {
        var context = new ScreenProjectionContext(
            new ScreenRectangle(300, 200, 1280, 720),
            16d / 9d);

        var projected = ClientCoordinateProjector.ProjectWidescreenOrFallback(
            default,
            xRatio: 0.4d,
            yRatio: 0.6d,
            captureAspectRatio: 16d / 9d,
            currentContext: context);
        var simple = ClientCoordinateProjector.ProjectOrFallback(
            default,
            xRatio: 0.4d,
            yRatio: 0.6d,
            context.ClientBounds);

        Assert.Equal(simple, projected);
    }

    [Theory]
    [InlineData(1024, 768, 4d / 3d)]
    [InlineData(1065, 773, 1073d / 804d)]
    [InlineData(1377, 811, 1393d / 850d)]
    [InlineData(800, 1000, 0.8d)]
    [InlineData(2560, 720, 32d / 9d)]
    [InlineData(333, 777, 0.45d)]
    public void ProjectWidescreen_RoundTripsThroughIrregularWindowWithinOnePixel(
        int intermediateWidth,
        int intermediateHeight,
        double intermediateOuterAspect)
    {
        var sourceContext = new ScreenProjectionContext(
            new ScreenRectangle(0, 0, 1920, 1080),
            16d / 9d);
        var sourcePoint = new ScreenPoint(1087, 643);
        Assert.True(ClientCoordinateProjector.TryNormalize(
            sourcePoint,
            sourceContext,
            out var sourceXRatio,
            out var sourceYRatio,
            out var sourceAspect));

        var intermediateContext = new ScreenProjectionContext(
            new ScreenRectangle(37, 59, intermediateWidth, intermediateHeight),
            intermediateOuterAspect);
        var intermediatePoint = ClientCoordinateProjector.ProjectWidescreenOrFallback(
            default,
            sourceXRatio,
            sourceYRatio,
            sourceAspect,
            intermediateContext);
        Assert.True(ClientCoordinateProjector.TryNormalize(
            intermediatePoint,
            intermediateContext,
            out var intermediateXRatio,
            out var intermediateYRatio,
            out var capturedIntermediateAspect));

        var roundTripped = ClientCoordinateProjector.ProjectWidescreenOrFallback(
            default,
            intermediateXRatio,
            intermediateYRatio,
            capturedIntermediateAspect,
            sourceContext);

        Assert.InRange(Math.Abs(roundTripped.X - sourcePoint.X), 0, 1);
        Assert.InRange(Math.Abs(roundTripped.Y - sourcePoint.Y), 0, 1);
    }

    [Fact]
    public void ProjectWidescreen_ValidRatioOutsideNarrowerViewIsNotClampedToEdge()
    {
        var bounds = new ScreenRectangle(100, 200, 640, 480);

        var projected = ClientCoordinateProjector.ProjectWidescreenOrFallback(
            default,
            xRatio: 1d,
            yRatio: 0.5d,
            captureAspectRatio: 4d,
            currentContext: new ScreenProjectionContext(bounds, 4d / 3d));

        Assert.False(bounds.Contains(projected));
        Assert.True(projected.X >= bounds.Right);
        Assert.NotEqual(bounds.Right - 1, projected.X);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TryNormalize_InvalidProjectionAspect_ReturnsFalseAndClearsOutputs(
        double projectionAspectRatio)
    {
        var normalized = ClientCoordinateProjector.TryNormalize(
            new ScreenPoint(50, 60),
            new ScreenProjectionContext(
                new ScreenRectangle(0, 0, 100, 100),
                projectionAspectRatio),
            out var xRatio,
            out var yRatio,
            out var captureAspectRatio);

        Assert.False(normalized);
        Assert.Equal(0d, xRatio);
        Assert.Equal(0d, yRatio);
        Assert.Equal(0d, captureAspectRatio);
    }

    [Fact]
    public void TryNormalize_ThenProject_AdaptsFrom1920By1080To1280By720()
    {
        var originalBounds = new ScreenRectangle(0, 0, 1920, 1080);
        var originalPoint = new ScreenPoint(1716, 836);

        var normalized = ClientCoordinateProjector.TryNormalize(
            originalPoint,
            originalBounds,
            out var xRatio,
            out var yRatio);
        var projected = ClientCoordinateProjector.ProjectOrFallback(
            originalPoint,
            xRatio,
            yRatio,
            new ScreenRectangle(300, 200, 1280, 720));

        Assert.True(normalized);
        Assert.Equal(new ScreenPoint(1444, 757), projected);
    }

    [Fact]
    public void TryNormalize_ThenProject_FollowsWindowMovementWithoutChangingOffset()
    {
        var originalBounds = new ScreenRectangle(0, 0, 1920, 1080);
        var originalPoint = new ScreenPoint(843, 413);
        Assert.True(ClientCoordinateProjector.TryNormalize(
            originalPoint,
            originalBounds,
            out var xRatio,
            out var yRatio));

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            originalPoint,
            xRatio,
            yRatio,
            new ScreenRectangle(-1920, 0, 1920, 1080));

        Assert.Equal(new ScreenPoint(-1077, 413), projected);
    }

    [Fact]
    public void TryNormalize_ProjectAndNormalize_RoundTripsWithinOneTargetPixel()
    {
        var sourceBounds = new ScreenRectangle(-100, 50, 1920, 1080);
        Assert.True(ClientCoordinateProjector.TryNormalize(
            new ScreenPoint(1616, 886),
            sourceBounds,
            out var sourceXRatio,
            out var sourceYRatio));
        var targetBounds = new ScreenRectangle(300, 200, 1280, 720);

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            default,
            sourceXRatio,
            sourceYRatio,
            targetBounds);
        Assert.True(ClientCoordinateProjector.TryNormalize(
            projected,
            targetBounds,
            out var targetXRatio,
            out var targetYRatio));

        Assert.InRange(Math.Abs(sourceXRatio - targetXRatio), 0d, 0.5d / (targetBounds.Width - 1));
        Assert.InRange(Math.Abs(sourceYRatio - targetYRatio), 0d, 0.5d / (targetBounds.Height - 1));
    }

    [Fact]
    public void TryNormalize_SinglePixelAxesUseZeroRatio()
    {
        var normalized = ClientCoordinateProjector.TryNormalize(
            new ScreenPoint(-7, 13),
            new ScreenRectangle(-7, 13, 1, 1),
            out var xRatio,
            out var yRatio);

        Assert.True(normalized);
        Assert.Equal(0d, xRatio);
        Assert.Equal(0d, yRatio);
    }

    [Theory]
    [InlineData(99, 200, 1280, 720)]
    [InlineData(100, 199, 1280, 720)]
    [InlineData(1380, 200, 1280, 720)]
    [InlineData(100, 920, 1280, 720)]
    [InlineData(100, 200, 0, 720)]
    [InlineData(100, 200, 1280, 0)]
    public void TryNormalize_PointOutsideOrInvalidClientArea_ReturnsFalse(
        int pointX,
        int pointY,
        int width,
        int height)
    {
        var normalized = ClientCoordinateProjector.TryNormalize(
            new ScreenPoint(pointX, pointY),
            new ScreenRectangle(100, 200, width, height),
            out var xRatio,
            out var yRatio);

        Assert.False(normalized);
        Assert.Equal(0d, xRatio);
        Assert.Equal(0d, yRatio);
    }

    [Fact]
    public void ProjectOrFallback_ProjectsRatiosIntoCurrentClientBounds()
    {
        var bounds = new ScreenRectangle(100, 200, 1280, 720);

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            new ScreenPoint(900, 700),
            0.5,
            0.5,
            bounds);

        Assert.Equal(new ScreenPoint(740, 560), projected);
    }

    [Fact]
    public void ProjectOrFallback_UsesAwayFromZeroRounding()
    {
        var bounds = new ScreenRectangle(10, 20, 4, 6);

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            default,
            0.5,
            0.5,
            bounds);

        Assert.Equal(new ScreenPoint(12, 23), projected);
    }

    [Fact]
    public void ProjectOrFallback_OutOfRangeRatioReturnsAbsolutePoint()
    {
        var fallback = new ScreenPoint(843, 413);

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            fallback,
            -0.25,
            1.25,
            new ScreenRectangle(-1920, -200, 1280, 720));

        Assert.Equal(fallback, projected);
    }

    [Fact]
    public void ProjectOrFallback_SupportsSinglePixelClientArea()
    {
        var bounds = new ScreenRectangle(-7, 13, 1, 1);

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            default,
            1,
            1,
            bounds);

        Assert.Equal(new ScreenPoint(-7, 13), projected);
    }

    [Theory]
    [InlineData(null, 0.5)]
    [InlineData(0.5, null)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(0.5, double.PositiveInfinity)]
    public void ProjectOrFallback_MissingOrNonFiniteRatio_ReturnsAbsolutePoint(
        double? xRatio,
        double? yRatio)
    {
        var fallback = new ScreenPoint(843, 413);

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            fallback,
            xRatio,
            yRatio,
            new ScreenRectangle(100, 200, 1280, 720));

        Assert.Equal(fallback, projected);
    }

    [Theory]
    [InlineData(-0.0001d, 0.5d)]
    [InlineData(1.0001d, 0.5d)]
    [InlineData(0.5d, -0.0001d)]
    [InlineData(0.5d, 1.0001d)]
    [InlineData(double.NaN, 0.5d)]
    [InlineData(0.5d, double.PositiveInfinity)]
    public void ProjectWidescreen_InvalidRatioReturnsAbsolutePoint(double xRatio, double yRatio)
    {
        var fallback = new ScreenPoint(843, 413);

        var projected = ClientCoordinateProjector.ProjectWidescreenOrFallback(
            fallback,
            xRatio,
            yRatio,
            16d / 9d,
            new ScreenProjectionContext(
                new ScreenRectangle(0, 0, 1920, 1080),
                16d / 9d));

        Assert.Equal(fallback, projected);
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1280, 0)]
    [InlineData(-1, 720)]
    [InlineData(1280, -1)]
    public void ProjectOrFallback_InvalidClientArea_ReturnsAbsolutePoint(int width, int height)
    {
        var fallback = new ScreenPoint(1170, 687);

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            fallback,
            0.75,
            0.25,
            new ScreenRectangle(10, 20, width, height));

        Assert.Equal(fallback, projected);
    }

    [Fact]
    public void ProjectOrFallback_MissingClientArea_ReturnsAbsolutePoint()
    {
        var fallback = new ScreenPoint(1716, 836);

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            fallback,
            0.9,
            0.8,
            clientBounds: null);

        Assert.Equal(fallback, projected);
    }

    [Fact]
    public void ProjectOrFallback_SaturatesImpossibleDesktopCoordinates()
    {
        var projected = ClientCoordinateProjector.ProjectOrFallback(
            default,
            1,
            1,
            new ScreenRectangle(int.MaxValue, int.MaxValue, 2, 2));

        Assert.Equal(new ScreenPoint(int.MaxValue, int.MaxValue), projected);
    }
}
