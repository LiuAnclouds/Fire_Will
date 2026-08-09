using FireWill.Core.Configuration;
using FireWill.Core.Execution;

namespace FireWill.Tests;

public sealed class FlowAdaptiveCoordinatePreflightTests
{
    [Fact]
    public void FindMissing_ListsNpcAndSkillTargetSeparately()
    {
        var configuration = CreateConfiguredFlow();

        var issues = FlowAdaptiveCoordinatePreflight.FindMissing(configuration, 1);

        Assert.Equal(
            [
                new AdaptiveCoordinateIssue(
                    AdaptiveCoordinatePointKind.Npc,
                    "家里挑战自我NPC"),
                new AdaptiveCoordinateIssue(
                    AdaptiveCoordinatePointKind.SkillTarget,
                    "家里挑战自我x5"),
            ],
            issues);
    }

    [Fact]
    public void FindMissing_DropsEachIssueAfterThatPointIsCaptured()
    {
        var configuration = CreateConfiguredFlow();
        var npc = configuration.Npcs["家里挑战自我NPC"];
        var farm = configuration.Farms["家里挑战自我x5"];
        npc.ClientXRatio = 0.4;
        npc.ClientYRatio = 0.6;
        npc.ClientCaptureAspectRatio = 16d / 9d;

        var afterNpcCapture = FlowAdaptiveCoordinatePreflight.FindMissing(configuration, 1);

        Assert.Equal(
            [new AdaptiveCoordinateIssue(
                AdaptiveCoordinatePointKind.SkillTarget,
                "家里挑战自我x5")],
            afterNpcCapture);

        farm.TargetClientXRatio = 0.5;
        farm.TargetClientYRatio = 0.75;
        farm.TargetClientCaptureAspectRatio = 16d / 9d;

        Assert.Empty(FlowAdaptiveCoordinatePreflight.FindMissing(configuration, 1));
    }

    [Fact]
    public void FindMissing_V013RatiosWithoutCaptureAspectListsBothPoints()
    {
        var configuration = CreateConfiguredFlow();
        var npc = configuration.Npcs["家里挑战自我NPC"];
        var farm = configuration.Farms["家里挑战自我x5"];
        npc.ClientXRatio = 0.4;
        npc.ClientYRatio = 0.6;
        farm.TargetClientXRatio = 0.5;
        farm.TargetClientYRatio = 0.75;

        var issues = FlowAdaptiveCoordinatePreflight.FindMissing(configuration, 1);

        Assert.Equal(
            [
                new AdaptiveCoordinateIssue(
                    AdaptiveCoordinatePointKind.Npc,
                    "家里挑战自我NPC"),
                new AdaptiveCoordinateIssue(
                    AdaptiveCoordinatePointKind.SkillTarget,
                    "家里挑战自我x5"),
            ],
            issues);
    }

    [Fact]
    public void FindMissing_IgnoresUnusedSkillTargetAndDeduplicatesNpc()
    {
        var configuration = CreateConfiguredFlow();
        var flow = configuration.GetFlow(1);
        var farm = configuration.Farms["家里挑战自我x5"];
        farm.ReleaseType = LegacyValues.None;
        flow.Groups[1].Enabled = true;
        flow.Groups[1].FarmName = farm.Name;

        var issues = FlowAdaptiveCoordinatePreflight.FindMissing(configuration, 1);

        Assert.Equal(
            [new AdaptiveCoordinateIssue(
                AdaptiveCoordinatePointKind.Npc,
                "家里挑战自我NPC")],
            issues);
    }

    [Fact]
    public void FindMissing_IgnoresDisabledFlowAndGroups()
    {
        var configuration = CreateConfiguredFlow();
        configuration.GetFlow(1).Enabled = false;

        Assert.Empty(FlowAdaptiveCoordinatePreflight.FindMissing(configuration, 1));

        configuration.GetFlow(1).Enabled = true;
        configuration.GetFlow(1).Groups[0].Enabled = false;

        Assert.Empty(FlowAdaptiveCoordinatePreflight.FindMissing(configuration, 1));
    }

    private static MacroConfiguration CreateConfiguredFlow()
    {
        var configuration = ConfigurationDefaults.Create();
        var flow = configuration.GetFlow(1);
        flow.Enabled = true;
        flow.Groups[0].Enabled = true;
        flow.Groups[0].FarmName = "家里挑战自我x5";

        var npc = configuration.Npcs["家里挑战自我NPC"];
        npc.X = 1130;
        npc.Y = 684;

        var farm = configuration.Farms["家里挑战自我x5"];
        farm.ActionKey = "Q";
        farm.ReleaseType = LegacyValues.SkillKeyRelease;
        farm.ReleaseKey = "Q";
        farm.TargetX = 942;
        farm.TargetY = 705;
        return configuration;
    }
}
