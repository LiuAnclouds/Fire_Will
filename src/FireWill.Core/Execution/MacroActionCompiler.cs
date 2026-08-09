using FireWill.Core.Configuration;

namespace FireWill.Core.Execution;

public sealed class MacroActionCompiler
{
    private const int OrdinaryKeyHoldMs = 15;
    private const int HeroSelectMinimumHoldMs = 50;
    private const int HeroSelectMaximumHoldMs = 80;

    public CompiledFlow CompileFlow(MacroConfiguration configuration, int slot)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var flow = configuration.GetFlow(slot);
        var groups = flow.Groups
            .Where(group => group.Enabled)
            .OrderBy(group => group.Slot)
            .Select(group => CompileGroup(configuration, flow, group))
            .ToArray();

        return new CompiledFlow(flow.Slot, flow.Name, flow.Enabled, groups);
    }

    public CompiledGroup CompileGroup(
        MacroConfiguration configuration,
        FlowSettings flow,
        FlowGroupSettings group)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(group);

        var builder = new GroupActionBuilder();
        CompilePreCommand(flow, group, builder);
        builder.Add(new StopBoundaryAction());
        var farmName = LegacyNormalization.FarmName(group.FarmName);
        FarmSettings? farm = null;
        var taskSucceeded = true;
        if (farmName != LegacyValues.None && configuration.Farms.TryGetValue(farmName, out farm))
        {
            taskSucceeded = CompileFarmTask(configuration, flow, farm, builder);
        }
        else if (farmName != LegacyValues.None)
        {
            builder.Warn($"未知刷本项：{farmName}。");
            taskSucceeded = false;
        }

        var releaseProfileName = ReleaseProfileCatalog.NormalizeName(group.ReleaseProfileName);
        var hasLegacyRelease = !group.ReleaseSelectionIsExplicit &&
            releaseProfileName == LegacyValues.None &&
            farm is not null &&
            LegacyNormalization.ReleaseType(farm.ReleaseType) != LegacyValues.None;
        if (taskSucceeded && (releaseProfileName != LegacyValues.None || hasLegacyRelease))
        {
            CompileRelease(configuration, flow, group, farm, builder);
        }

        var wait = group.WaitMs is null
            ? Math.Max(0, Math.Clamp(group.DurationMs, 0, 30_000) - builder.CountedDurationMs)
            : Math.Clamp(group.WaitMs.Value, 0, 30_000);
        return new CompiledGroup(group.Slot, builder.Actions.ToArray(), builder.CountedDurationMs, wait);
    }

    public string ResolveReleaseKey(MacroConfiguration configuration, FarmSettings farm)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(farm);

        var releaseType = LegacyNormalization.ReleaseType(farm.ReleaseType);
        var raw = farm.ReleaseKey.Trim();
        if (releaseType == LegacyValues.SkillSlotRelease)
        {
            var index = LegacyNormalization.ToInt(raw);
            return index is >= 1 and <= LegacyCatalog.SkillSlotCount
                ? LegacyNormalization.Key(configuration.KeyMap.Skills[index - 1])
                : string.Empty;
        }

        if (releaseType == LegacyValues.ItemSlotRelease)
        {
            var index = LegacyNormalization.ToInt(raw);
            return index is >= 1 and <= LegacyCatalog.ItemSlotCount
                ? LegacyNormalization.Key(configuration.KeyMap.Items[index - 1])
                : string.Empty;
        }

        return KeyMapReferences.Resolve(configuration.KeyMap, raw);
    }

    public string ResolveReleaseKey(MacroConfiguration configuration, string? releaseProfileName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var name = ReleaseProfileCatalog.NormalizeName(releaseProfileName);
        if (name == LegacyValues.None ||
            !configuration.ReleaseProfiles.TryGetValue(name, out var profile))
        {
            return string.Empty;
        }

        return ResolveReleaseKey(configuration, profile);
    }

    public string ResolveReleaseKey(
        MacroConfiguration configuration,
        ReleaseProfileSettings profile)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(profile);

        var expectedKind = profile.Kind == ReleaseProfileKind.Skill
            ? KeyMapReferenceKind.Skill
            : KeyMapReferenceKind.Item;
        if (KeyMapReferences.TryParse(profile.KeyReference, out var kind, out _) &&
            kind != expectedKind)
        {
            return string.Empty;
        }

        return KeyMapReferences.Resolve(configuration.KeyMap, profile.KeyReference);
    }

    public string ResolveActionKey(MacroConfiguration configuration, FarmSettings farm)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(farm);

        return KeyMapReferences.Resolve(configuration.KeyMap, farm.ActionKey);
    }

    private static void CompilePreCommand(
        FlowSettings flow,
        FlowGroupSettings group,
        GroupActionBuilder builder)
    {
        switch (group.PreType)
        {
            case LegacyValues.KeyPreCommand:
                // The old AHK SendKey returns immediately for an empty value. It is a no-op,
                // even when preType remains "按键" in an existing profile.
                AddOrdinaryKey(builder, LegacyNormalization.Key(group.PreValue), flow);
                break;
            case LegacyValues.ChatPreCommand:
                var text = group.PreValue.Trim();
                if (text.Length > 0)
                {
                    builder.Add(new SendChatAction(text));
                    builder.AddCountedDelay(flow.ChatDelayMs, "chat");
                }

                break;
        }
    }

    private bool CompileFarmTask(
        MacroConfiguration configuration,
        FlowSettings flow,
        FarmSettings farm,
        GroupActionBuilder builder)
    {
        if (!configuration.Npcs.TryGetValue(farm.NpcName, out var npc)
            || npc.X is null
            || npc.Y is null)
        {
            builder.Warn($"NPC未标定点击坐标：{farm.NpcName}。");
            return false;
        }

        var actionKey = string.Empty;
        if (farm.NpcAction != "只点击NPC")
        {
            actionKey = ResolveActionKey(configuration, farm);
            if (actionKey.Length == 0)
            {
                builder.Warn($"NPC动作缺少按键：{farm.NpcName} / {farm.NpcAction}。");
                return false;
            }
        }

        var npcProjection = NormalizeClientProjection(
            npc.ClientXRatio,
            npc.ClientYRatio,
            npc.ClientCaptureAspectRatio);
        builder.Add(new MoveMouseAction(
            npc.X.Value,
            npc.Y.Value,
            npcProjection.X,
            npcProjection.Y,
            npcProjection.CaptureAspectRatio));
        builder.AddCountedDelay(flow.MouseMoveDelayMs, "npc-mouse-move");
        builder.Add(new LeftClickAction());
        builder.AddCountedDelay(flow.NpcClickDelayMs, "npc-click");

        if (farm.NpcAction != "只点击NPC")
        {
            AddOrdinaryKey(builder, actionKey, flow);
        }

        return true;
    }

    private void CompileRelease(
        MacroConfiguration configuration,
        FlowSettings flow,
        FlowGroupSettings group,
        FarmSettings? farm,
        GroupActionBuilder builder)
    {
        var releaseProfileName = ReleaseProfileCatalog.NormalizeName(group.ReleaseProfileName);
        var isProfileRelease = releaseProfileName != LegacyValues.None;
        var releaseKey = isProfileRelease
            ? ResolveReleaseKey(configuration, releaseProfileName)
            : farm is null ? string.Empty : ResolveReleaseKey(configuration, farm);
        if (releaseKey.Length == 0)
        {
            builder.Warn(isProfileRelease
                ? $"技能释放方案缺少平台映射键：{releaseProfileName}。"
                : farm is null
                    ? "技能释放方案未选择有效按键。"
                    : GetReleaseKeyError(
                        configuration,
                        farm,
                        LegacyNormalization.ReleaseType(farm.ReleaseType)));
            return;
        }

        AddTimedKey(
            builder,
            "F1",
            flow.HeroSelectDelayMs,
            HeroSelectMinimumHoldMs,
            HeroSelectMaximumHoldMs);
        if (!isProfileRelease && farm?.TargetX is not null && farm.TargetY is not null)
        {
            var targetProjection = NormalizeClientProjection(
                farm.TargetClientXRatio,
                farm.TargetClientYRatio,
                farm.TargetClientCaptureAspectRatio);
            builder.Add(new MoveMouseAction(
                farm.TargetX.Value,
                farm.TargetY.Value,
                targetProjection.X,
                targetProjection.Y,
                targetProjection.CaptureAspectRatio));
            builder.AddCountedDelay(flow.ReleaseMouseMoveDelayMs, "release-mouse-move");
        }

        if (LegacyNormalization.IsTeleportKey(releaseKey))
        {
            AddTimedKey(builder, releaseKey, flow.TeleportKeyDelayMs, minimumHoldMs: 50, maximumHoldMs: 100);
        }
        else
        {
            AddTimedKey(builder, releaseKey, flow.SkillKeyDelayMs, minimumHoldMs: 0, maximumHoldMs: flow.SkillKeyDelayMs);
        }
    }

    private static void AddOrdinaryKey(GroupActionBuilder builder, string key, FlowSettings flow)
    {
        if (key.Length == 0)
        {
            return;
        }

        builder.Add(new KeyPressAction(key, OrdinaryKeyHoldMs, HoldIsInterruptible: false));
        builder.AddCountedDelay(
            LegacyNormalization.IsTeleportKey(key) ? flow.TeleportKeyDelayMs : flow.KeyDelayMs,
            LegacyNormalization.IsTeleportKey(key) ? "teleport-key" : "ordinary-key");
    }

    private static void AddTimedKey(
        GroupActionBuilder builder,
        string key,
        int durationMs,
        int minimumHoldMs,
        int maximumHoldMs)
    {
        var effectiveDuration = Math.Max(Math.Max(0, durationMs), minimumHoldMs);
        var hold = Math.Clamp(effectiveDuration, minimumHoldMs, Math.Max(minimumHoldMs, maximumHoldMs));
        var rest = Math.Max(0, effectiveDuration - hold);
        builder.Add(new KeyPressAction(key, hold, HoldIsInterruptible: true, rest, RestIsInterruptible: true));
        builder.Count(effectiveDuration);
    }

    private static string GetReleaseKeyError(
        MacroConfiguration configuration,
        FarmSettings farm,
        string releaseType)
    {
        if (releaseType is LegacyValues.SkillKeyRelease or LegacyValues.ItemKeyRelease)
        {
            return $"刷本项缺少释放按键：{farm.Name}。";
        }

        if (releaseType is LegacyValues.SkillSlotRelease or LegacyValues.ItemSlotRelease)
        {
            var index = LegacyNormalization.ToInt(farm.ReleaseKey);
            var maximum = releaseType == LegacyValues.SkillSlotRelease
                ? LegacyCatalog.SkillSlotCount
                : LegacyCatalog.ItemSlotCount;
            var slotName = releaseType == LegacyValues.SkillSlotRelease ? "技能" : "装备";
            if (index < 1 || index > maximum)
            {
                return $"刷本项{slotName}槽位编号必须是 1-{maximum}：{farm.Name}。";
            }

            var mappedKey = releaseType == LegacyValues.SkillSlotRelease
                ? configuration.KeyMap.Skills[index - 1]
                : configuration.KeyMap.Items[index - 1];
            if (LegacyNormalization.Key(mappedKey).Length == 0)
            {
                return $"刷本项槽位缺少平台映射键：{farm.Name} / 第{index}格。";
            }
        }

        return $"刷本项缺少释放按键或槽位映射：{farm.Name}。";
    }

    private static (double? X, double? Y, double? CaptureAspectRatio) NormalizeClientProjection(
        double? x,
        double? y,
        double? captureAspectRatio)
    {
        if (x is not (>= 0d and <= 1d) ||
            y is not (>= 0d and <= 1d) ||
            !double.IsFinite(x.Value) ||
            !double.IsFinite(y.Value))
        {
            return (null, null, null);
        }

        return captureAspectRatio is > 0d &&
            double.IsFinite(captureAspectRatio.Value)
                ? (x, y, captureAspectRatio)
                : (x, y, null);
    }

    private sealed class GroupActionBuilder
    {
        public List<MacroAction> Actions { get; } = [];

        public int CountedDurationMs { get; private set; }

        public void Add(MacroAction action) => Actions.Add(action);

        public void AddCountedDelay(int milliseconds, string reason)
        {
            var normalized = Math.Max(0, milliseconds);
            if (normalized > 0)
            {
                Actions.Add(new DelayAction(normalized, IsInterruptible: true, reason));
                Count(normalized);
            }
        }

        public void Count(int milliseconds)
        {
            CountedDurationMs = checked(CountedDurationMs + Math.Max(0, milliseconds));
        }

        public void Warn(string message) => Actions.Add(new WarningAction(message));
    }
}
