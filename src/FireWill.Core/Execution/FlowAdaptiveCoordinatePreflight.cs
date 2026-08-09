using FireWill.Core.Configuration;

namespace FireWill.Core.Execution;

public enum AdaptiveCoordinatePointKind
{
    Npc,
    SkillTarget,
}

public sealed record AdaptiveCoordinateIssue(
    AdaptiveCoordinatePointKind Kind,
    string PointName);

public static class FlowAdaptiveCoordinatePreflight
{
    public static IReadOnlyList<AdaptiveCoordinateIssue> FindMissing(
        MacroConfiguration configuration,
        int flowSlot)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var flow = configuration.GetFlow(flowSlot);
        if (!flow.Enabled)
        {
            return [];
        }

        var compiler = new MacroActionCompiler();
        var result = new List<AdaptiveCoordinateIssue>();
        var seen = new HashSet<(AdaptiveCoordinatePointKind Kind, string PointName)>();

        foreach (var group in flow.Groups.Where(item => item.Enabled).OrderBy(item => item.Slot))
        {
            var farmName = LegacyNormalization.FarmName(group.FarmName);
            if (farmName == LegacyValues.None ||
                !configuration.Farms.TryGetValue(farmName, out var farm) ||
                !configuration.Npcs.TryGetValue(farm.NpcName, out var npc) ||
                npc.X is null ||
                npc.Y is null)
            {
                continue;
            }

            AddIfLegacy(
                result,
                seen,
                AdaptiveCoordinatePointKind.Npc,
                farm.NpcName,
                npc.ClientXRatio,
                npc.ClientYRatio,
                npc.ClientCaptureAspectRatio);

            if (farm.NpcAction != "只点击NPC" &&
                compiler.ResolveActionKey(configuration, farm).Length == 0)
            {
                continue;
            }

            // New release profiles are key-only and no longer depend on a
            // per-farm mouse target. The remaining branch migrates old profiles.
            if (group.ReleaseSelectionIsExplicit ||
                ReleaseProfileCatalog.NormalizeName(group.ReleaseProfileName) != LegacyValues.None)
            {
                continue;
            }

            var releaseType = LegacyNormalization.ReleaseType(farm.ReleaseType);
            if (releaseType == LegacyValues.None ||
                compiler.ResolveReleaseKey(configuration, farm).Length == 0 ||
                farm.TargetX is null ||
                farm.TargetY is null)
            {
                continue;
            }

            AddIfLegacy(
                result,
                seen,
                AdaptiveCoordinatePointKind.SkillTarget,
                farm.Name,
                farm.TargetClientXRatio,
                farm.TargetClientYRatio,
                farm.TargetClientCaptureAspectRatio);
        }

        return result;
    }

    private static void AddIfLegacy(
        ICollection<AdaptiveCoordinateIssue> result,
        ISet<(AdaptiveCoordinatePointKind Kind, string PointName)> seen,
        AdaptiveCoordinatePointKind kind,
        string pointName,
        double? xRatio,
        double? yRatio,
        double? captureAspectRatio)
    {
        if (IsAdaptive(xRatio, yRatio, captureAspectRatio) || !seen.Add((kind, pointName)))
        {
            return;
        }

        result.Add(new AdaptiveCoordinateIssue(kind, pointName));
    }

    private static bool IsAdaptive(
        double? xRatio,
        double? yRatio,
        double? captureAspectRatio) =>
        xRatio is >= 0d and <= 1d &&
        yRatio is >= 0d and <= 1d &&
        double.IsFinite(xRatio.Value) &&
        double.IsFinite(yRatio.Value) &&
        captureAspectRatio is > 0d &&
        double.IsFinite(captureAspectRatio.Value);
}
