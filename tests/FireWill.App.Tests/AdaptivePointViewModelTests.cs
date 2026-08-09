using FireWill.App.ViewModels;
using FireWill.Core.Configuration;

namespace FireWill.App.Tests;

public sealed class AdaptivePointViewModelTests
{
    [Fact]
    public void NpcPoint_StoresRatios_AndManualCoordinateEditClearsThem()
    {
        var model = new NpcSettings { Name = "NPC" };
        var viewModel = new NpcRowViewModel(model);

        viewModel.SetPoint(843, 413, 0.4, 0.6, 16d / 9d);

        Assert.Equal((843, 413), (model.X, model.Y));
        Assert.Equal((0.4, 0.6), (model.ClientXRatio, model.ClientYRatio));
        Assert.Equal(16d / 9d, model.ClientCaptureAspectRatio);

        viewModel.X = "844";

        Assert.Null(model.ClientXRatio);
        Assert.Null(model.ClientYRatio);
        Assert.Null(model.ClientCaptureAspectRatio);
    }

    [Fact]
    public void FarmTarget_StoresRatios_AndClearRemovesThem()
    {
        var model = new FarmSettings
        {
            Name = "farm",
            NpcName = "NPC",
            NpcAction = "x5",
        };
        var viewModel = new FarmRowViewModel(model);

        viewModel.SetTarget(942, 705, 0.5, 0.75, 16d / 9d);

        Assert.Equal((942, 705), (model.TargetX, model.TargetY));
        Assert.Equal((0.5, 0.75), (model.TargetClientXRatio, model.TargetClientYRatio));
        Assert.Equal(16d / 9d, model.TargetClientCaptureAspectRatio);

        viewModel.Clear();

        Assert.Null(model.TargetX);
        Assert.Null(model.TargetY);
        Assert.Null(model.TargetClientXRatio);
        Assert.Null(model.TargetClientYRatio);
        Assert.Null(model.TargetClientCaptureAspectRatio);
    }

    [Fact]
    public void InvalidRatioPair_UsesAbsoluteFallbackOnly()
    {
        var model = new NpcSettings { Name = "NPC" };
        var viewModel = new NpcRowViewModel(model);

        viewModel.SetPoint(100, 200, 0.5, null);

        Assert.Equal((100, 200), (model.X, model.Y));
        Assert.Null(model.ClientXRatio);
        Assert.Null(model.ClientYRatio);
        Assert.Null(model.ClientCaptureAspectRatio);
    }
}
