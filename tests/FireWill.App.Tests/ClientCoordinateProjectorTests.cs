using FireWill.App.Services.Input;

namespace FireWill.App.Tests;

public sealed class ClientCoordinateProjectorTests
{
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
    public void ProjectOrFallback_ClampsRatiosToClientPixelRange()
    {
        var bounds = new ScreenRectangle(-1920, -200, 1280, 720);

        var projected = ClientCoordinateProjector.ProjectOrFallback(
            default,
            -0.25,
            1.25,
            bounds);

        Assert.Equal(new ScreenPoint(-1920, 519), projected);
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
