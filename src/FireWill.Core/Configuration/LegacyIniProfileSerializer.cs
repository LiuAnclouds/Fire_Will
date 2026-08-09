using System.Globalization;
using System.Text;

namespace FireWill.Core.Configuration;

public static class LegacyIniProfileSerializer
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static MacroConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);
        var offset = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        return Parse(StrictUtf8.GetString(bytes, offset, bytes.Length - offset));
    }

    public static MacroConfiguration Parse(string text)
    {
        var document = IniDocument.Parse(text);
        var configuration = ConfigurationDefaults.Create();
        LoadGeneral(document, configuration.General);
        LoadNpcs(document, configuration);
        LoadFarms(document, configuration);
        LoadFlows(document, configuration);
        LoadKeyMap(document, configuration.KeyMap);
        return configuration;
    }

    public static void Save(string path, MacroConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configuration);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = Path.Combine(directory ?? string.Empty, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, Serialize(configuration), Utf8WithoutBom);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string Serialize(MacroConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateShape(configuration);

        var document = new IniDocument();
        WriteGeneral(document, configuration.General);
        foreach (var name in LegacyCatalog.NpcNames)
        {
            WriteNpc(document, configuration.Npcs[name]);
        }

        foreach (var name in LegacyCatalog.FarmNames)
        {
            WriteFarm(document, configuration.Farms[name]);
        }

        foreach (var flow in configuration.Flows.OrderBy(flow => flow.Slot))
        {
            WriteFlow(document, flow);
        }

        WriteKeyMap(document, configuration.KeyMap);
        return document.Serialize();
    }

    private static void LoadGeneral(IniDocument document, GeneralSettings general)
    {
        const string section = "General";
        general.GameWindowMatcher = document.Get(section, "gameWindowMatcher", general.GameWindowMatcher);
        general.SkipGameCheck = LegacyNormalization.ToInt(document.Get(section, "skipGameCheck", general.SkipGameCheck ? "1" : "0")) == 1;
        general.StopHotkey = LegacyNormalization.Hotkey(document.Get(section, "stopHotkey", "Z"));
        if (general.StopHotkey.Length == 0)
        {
            general.StopHotkey = "Z";
        }

        var legacyKeyDelay = LegacyNormalization.ClampDelay(document.Get(section, "commandDelayMs", "40"), 40, 1000);
        general.KeyDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "keyDelayMs", Format(legacyKeyDelay)), legacyKeyDelay, 1000);
        general.SkillKeyDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "skillKeyDelayMs", "100"), 100, 1000);
        general.HeroSelectDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "heroSelectDelayMs", "80"), 80, 1000);
        var legacyClickDelay = LegacyNormalization.ClampDelay(document.Get(section, "clickDelayMs", "100"), 100, 1000);
        general.NpcClickDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "npcClickDelayMs", Format(legacyClickDelay)), legacyClickDelay, 1000);
        general.ChatDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "chatDelayMs", "500"), 500, 5000);
        general.TeleportKeyDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "teleportKeyDelayMs", "200"), 200, 5000);
        general.MouseMoveDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "mouseMoveDelayMs", "30"), 30, 1000);
        general.ReleaseMouseMoveDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "releaseMouseMoveDelayMs", "80"), 80, 1000);
        general.CurrentProfileName = document.Get(section, "currentProfileName", general.CurrentProfileName);
        general.CurrentProfilePath = IsDefaultProfileName(general.CurrentProfileName)
            ? string.Empty
            : document.Get(section, "currentProfilePath", general.CurrentProfilePath);
    }

    private static void LoadNpcs(IniDocument document, MacroConfiguration configuration)
    {
        foreach (var name in LegacyCatalog.NpcNames)
        {
            var section = $"NPC.{name}";
            var npc = configuration.Npcs[name];
            npc.Camera = name == "尾兽处追捕逃忍NPC"
                ? string.Empty
                : LegacyNormalization.Key(document.Get(section, "camera", npc.Camera));
            npc.X = LegacyNormalization.Coordinate(document.Get(section, "x", Format(npc.X)));
            npc.Y = LegacyNormalization.Coordinate(document.Get(section, "y", Format(npc.Y)));
            (npc.ClientXRatio, npc.ClientYRatio) = ParseClientRatioPair(
                document.Get(section, "clientXRatio"),
                document.Get(section, "clientYRatio"));
            npc.ClientCaptureAspectRatio = npc.ClientXRatio is not null
                ? ParseCaptureAspectRatio(document.Get(section, "clientCaptureAspectRatio"))
                : null;

            if (npc.X is not null && npc.Y is not null)
            {
                continue;
            }

            var x1 = LegacyNormalization.Coordinate(document.Get(section, "x1"));
            var y1 = LegacyNormalization.Coordinate(document.Get(section, "y1"));
            var x2 = LegacyNormalization.Coordinate(document.Get(section, "x2"));
            var y2 = LegacyNormalization.Coordinate(document.Get(section, "y2"));
            if (x1 is not null && y1 is not null && x2 is not null && y2 is not null)
            {
                npc.X = RoundLikeAhk(((double)x1.Value + x2.Value) / 2);
                npc.Y = RoundLikeAhk(((double)y1.Value + y2.Value) / 2);
            }
        }
    }

    private static void LoadFarms(IniDocument document, MacroConfiguration configuration)
    {
        foreach (var name in LegacyCatalog.FarmNames)
        {
            var section = $"Farm.{name}";
            var legacySection = name == "家里挑战自我x10" ? "Farm.家里挑战自我x20" : section;
            var farm = configuration.Farms[name];

            farm.ActionKey = LegacyNormalization.Key(GetWithLegacyFallback(document, section, legacySection, "actionKey", farm.ActionKey));
            farm.ReleaseType = LegacyNormalization.ReleaseType(GetWithLegacyFallback(document, section, legacySection, "releaseType", farm.ReleaseType));
            farm.ReleaseKey = LegacyNormalization.ReleaseKey(
                farm.ReleaseType,
                GetWithLegacyFallback(document, section, legacySection, "releaseKey", farm.ReleaseKey));
            farm.TargetX = ToNullableInt(
                GetWithLegacyFallback(document, section, legacySection, "targetX", Format(farm.TargetX)),
                farm.TargetX);
            farm.TargetY = ToNullableInt(
                GetWithLegacyFallback(document, section, legacySection, "targetY", Format(farm.TargetY)),
                farm.TargetY);
            (farm.TargetClientXRatio, farm.TargetClientYRatio) = ParseClientRatioPair(
                GetWithLegacyFallback(document, section, legacySection, "targetClientXRatio", string.Empty),
                GetWithLegacyFallback(document, section, legacySection, "targetClientYRatio", string.Empty));
            farm.TargetClientCaptureAspectRatio = farm.TargetClientXRatio is not null
                ? ParseCaptureAspectRatio(
                    GetWithLegacyFallback(document, section, legacySection, "targetClientCaptureAspectRatio", string.Empty))
                : null;
        }
    }

    private static void LoadFlows(IniDocument document, MacroConfiguration configuration)
    {
        var legacyLayout = document.Get("Flow.6", "name").Length == 0
            && (document.Get("Flow.3", "name").Contains("双鱼四镜像", StringComparison.Ordinal)
                || document.Get("Flow.3", "name").Contains("预留", StringComparison.Ordinal));

        foreach (var flow in configuration.Flows)
        {
            var sourceSlot = GetFlowSourceSlot(flow.Slot, legacyLayout);
            if (sourceSlot == 0)
            {
                continue;
            }

            var section = $"Flow.{sourceSlot}";
            flow.Name = LegacyNormalization.FlowName(document.Get(section, "name", flow.Name));
            flow.Enabled = LegacyNormalization.ToInt(document.Get(section, "enabled", flow.Enabled ? "1" : "0"), flow.Enabled ? 1 : 0) != 0;
            flow.Hotkey = LegacyNormalization.Hotkey(document.Get(section, "hotkey", flow.Hotkey));
            var legacyKeyDelay = LegacyNormalization.ClampDelay(document.Get(section, "commandDelay", "40"), 40, 1000);
            flow.KeyDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "keyDelay", Format(legacyKeyDelay)), legacyKeyDelay, 1000);
            flow.SkillKeyDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "skillKeyDelay", Format(configuration.General.SkillKeyDelayMs)), configuration.General.SkillKeyDelayMs, 1000);
            flow.HeroSelectDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "heroSelectDelay", Format(configuration.General.HeroSelectDelayMs)), configuration.General.HeroSelectDelayMs, 1000);
            var legacyClickDelay = LegacyNormalization.ClampDelay(document.Get(section, "clickDelay", "100"), 100, 1000);
            flow.NpcClickDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "npcClickDelay", Format(legacyClickDelay)), legacyClickDelay, 1000);
            flow.ChatDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "chatDelay", "500"), 500, 5000);
            flow.TeleportKeyDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "teleportKeyDelay", "200"), 200, 5000);
            flow.MouseMoveDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "mouseMoveDelay", "30"), 30, 1000);
            flow.ReleaseMouseMoveDelayMs = LegacyNormalization.ClampDelay(document.Get(section, "releaseMouseMoveDelay", "80"), 80, 1000);

            foreach (var group in flow.Groups)
            {
                var groupSection = $"Flow.{sourceSlot}.Group.{group.Slot}";
                group.Enabled = LegacyNormalization.ToInt(document.Get(groupSection, "enabled", group.Enabled ? "1" : "0"), group.Enabled ? 1 : 0) != 0;
                group.PreType = document.Get(groupSection, "preType", group.PreType);
                group.PreValue = LegacyNormalization.PreValue(group.PreType, document.Get(groupSection, "preValue", group.PreValue));
                group.FarmName = LegacyNormalization.FarmName(document.Get(groupSection, "farm", group.FarmName));
                if (group.FarmName == LegacyValues.None)
                {
                    var migratedFarm = FindFarmNameForNpcAction(
                        document.Get(groupSection, "npc"),
                        document.Get(groupSection, "npcAction"),
                        configuration);
                    if (migratedFarm.Length > 0)
                    {
                        group.FarmName = migratedFarm;
                    }
                }

                var legacyGroupDelay = Math.Clamp(LegacyNormalization.ToInt(document.Get(groupSection, "delay", "0")), 0, 30_000);
                group.DurationMs = Math.Clamp(LegacyNormalization.ToInt(document.Get(groupSection, "duration", Format(legacyGroupDelay)), legacyGroupDelay), 0, 30_000);
                group.WaitMs = document.TryGet(groupSection, "wait", out var rawWait)
                    ? Math.Clamp(LegacyNormalization.ToInt(rawWait), 0, 30_000)
                    : null;
            }
        }
    }

    private static void LoadKeyMap(IniDocument document, KeyMapSettings keyMap)
    {
        for (var index = 0; index < LegacyCatalog.SkillSlotCount; index++)
        {
            keyMap.Skills[index] = LegacyNormalization.Key(document.Get("KeyMap", $"skill{index + 1}", keyMap.Skills[index]));
        }

        for (var index = 0; index < LegacyCatalog.ItemSlotCount; index++)
        {
            keyMap.Items[index] = LegacyNormalization.Key(document.Get("KeyMap", $"item{index + 1}", keyMap.Items[index]));
        }
    }

    private static void WriteGeneral(IniDocument document, GeneralSettings general)
    {
        const string section = "General";
        document.Set(section, "gameWindowMatcher", general.GameWindowMatcher);
        document.Set(section, "skipGameCheck", general.SkipGameCheck ? "1" : "0");
        document.Set(section, "stopHotkey", LegacyNormalization.Hotkey(general.StopHotkey));
        document.Set(section, "keyDelayMs", Format(Math.Clamp(general.KeyDelayMs, 0, 1000)));
        document.Set(section, "skillKeyDelayMs", Format(Math.Clamp(general.SkillKeyDelayMs, 0, 1000)));
        document.Set(section, "heroSelectDelayMs", Format(Math.Clamp(general.HeroSelectDelayMs, 0, 1000)));
        document.Set(section, "npcClickDelayMs", Format(Math.Clamp(general.NpcClickDelayMs, 0, 1000)));
        document.Set(section, "chatDelayMs", Format(Math.Clamp(general.ChatDelayMs, 0, 5000)));
        document.Set(section, "teleportKeyDelayMs", Format(Math.Clamp(general.TeleportKeyDelayMs, 0, 5000)));
        document.Set(section, "mouseMoveDelayMs", Format(Math.Clamp(general.MouseMoveDelayMs, 0, 1000)));
        document.Set(section, "releaseMouseMoveDelayMs", Format(Math.Clamp(general.ReleaseMouseMoveDelayMs, 0, 1000)));
        document.Set(section, "currentProfileName", general.CurrentProfileName);
        document.Set(section, "currentProfilePath", general.CurrentProfilePath);
    }

    private static void WriteNpc(IniDocument document, NpcSettings npc)
    {
        var section = $"NPC.{npc.Name}";
        document.Set(section, "camera", npc.Name == "尾兽处追捕逃忍NPC" ? string.Empty : LegacyNormalization.Key(npc.Camera));
        document.Set(section, "x", Format(npc.X));
        document.Set(section, "y", Format(npc.Y));
        WriteClientRatioPair(
            document,
            section,
            "clientXRatio",
            "clientYRatio",
            npc.ClientXRatio,
            npc.ClientYRatio);
        WriteCaptureAspectRatio(
            document,
            section,
            "clientCaptureAspectRatio",
            npc.ClientCaptureAspectRatio,
            npc.ClientXRatio,
            npc.ClientYRatio);
    }

    private static void WriteFarm(IniDocument document, FarmSettings farm)
    {
        var section = $"Farm.{farm.Name}";
        var releaseType = LegacyNormalization.ReleaseType(farm.ReleaseType);
        document.Set(section, "actionKey", LegacyNormalization.Key(farm.ActionKey));
        document.Set(section, "releaseType", releaseType);
        document.Set(section, "releaseKey", LegacyNormalization.ReleaseKey(releaseType, farm.ReleaseKey));
        document.Set(section, "targetX", Format(farm.TargetX));
        document.Set(section, "targetY", Format(farm.TargetY));
        WriteClientRatioPair(
            document,
            section,
            "targetClientXRatio",
            "targetClientYRatio",
            farm.TargetClientXRatio,
            farm.TargetClientYRatio);
        WriteCaptureAspectRatio(
            document,
            section,
            "targetClientCaptureAspectRatio",
            farm.TargetClientCaptureAspectRatio,
            farm.TargetClientXRatio,
            farm.TargetClientYRatio);
    }

    private static void WriteFlow(IniDocument document, FlowSettings flow)
    {
        var section = $"Flow.{flow.Slot}";
        document.Set(section, "name", LegacyNormalization.FlowName(flow.Name));
        document.Set(section, "enabled", flow.Enabled ? "1" : "0");
        document.Set(section, "hotkey", LegacyNormalization.Hotkey(flow.Hotkey));
        document.Set(section, "keyDelay", Format(Math.Clamp(flow.KeyDelayMs, 0, 1000)));
        document.Set(section, "skillKeyDelay", Format(Math.Clamp(flow.SkillKeyDelayMs, 0, 1000)));
        document.Set(section, "heroSelectDelay", Format(Math.Clamp(flow.HeroSelectDelayMs, 0, 1000)));
        document.Set(section, "npcClickDelay", Format(Math.Clamp(flow.NpcClickDelayMs, 0, 1000)));
        document.Set(section, "chatDelay", Format(Math.Clamp(flow.ChatDelayMs, 0, 5000)));
        document.Set(section, "teleportKeyDelay", Format(Math.Clamp(flow.TeleportKeyDelayMs, 0, 5000)));
        document.Set(section, "mouseMoveDelay", Format(Math.Clamp(flow.MouseMoveDelayMs, 0, 1000)));
        document.Set(section, "releaseMouseMoveDelay", Format(Math.Clamp(flow.ReleaseMouseMoveDelayMs, 0, 1000)));

        foreach (var group in flow.Groups.OrderBy(group => group.Slot))
        {
            var groupSection = $"Flow.{flow.Slot}.Group.{group.Slot}";
            document.Set(groupSection, "enabled", group.Enabled ? "1" : "0");
            document.Set(groupSection, "preType", group.PreType);
            document.Set(groupSection, "preValue", LegacyNormalization.PreValue(group.PreType, group.PreValue));
            document.Set(groupSection, "farm", LegacyNormalization.FarmName(group.FarmName));
            if (group.WaitMs is not null)
            {
                document.Set(groupSection, "wait", Format(Math.Clamp(group.WaitMs.Value, 0, 30_000)));
            }

            document.Set(groupSection, "duration", Format(Math.Clamp(group.DurationMs, 0, 30_000)));
        }
    }

    private static void WriteKeyMap(IniDocument document, KeyMapSettings keyMap)
    {
        for (var index = 0; index < LegacyCatalog.SkillSlotCount; index++)
        {
            document.Set("KeyMap", $"skill{index + 1}", LegacyNormalization.Key(keyMap.Skills[index]));
        }

        for (var index = 0; index < LegacyCatalog.ItemSlotCount; index++)
        {
            document.Set("KeyMap", $"item{index + 1}", LegacyNormalization.Key(keyMap.Items[index]));
        }
    }

    private static string GetWithLegacyFallback(
        IniDocument document,
        string section,
        string legacySection,
        string key,
        string fallback)
    {
        if (document.TryGet(section, key, out var value))
        {
            return value;
        }

        return document.Get(legacySection, key, fallback);
    }

    private static int GetFlowSourceSlot(int slot, bool legacyLayout)
    {
        if (!legacyLayout)
        {
            return slot;
        }

        return slot switch
        {
            3 or 7 => 0,
            >= 4 and <= 6 => slot - 1,
            _ => slot,
        };
    }

    private static string FindFarmNameForNpcAction(string npcName, string action, MacroConfiguration configuration)
    {
        if (npcName.Length == 0 || action.Length == 0 || npcName == LegacyValues.None || action == LegacyValues.None)
        {
            return string.Empty;
        }

        if (npcName == "家里挑战自我NPC" && action == "x20")
        {
            return "家里挑战自我x10";
        }

        return configuration.Farms.Values.FirstOrDefault(
            farm => farm.NpcName == npcName && farm.NpcAction == action)?.Name ?? string.Empty;
    }

    private static int? ToNullableInt(string? value, int? fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;
    }

    private static (double? X, double? Y) ParseClientRatioPair(string? x, string? y)
    {
        return TryParseClientRatio(x, out var parsedX) &&
            TryParseClientRatio(y, out var parsedY)
                ? (parsedX, parsedY)
                : (null, null);
    }

    private static double? ParseCaptureAspectRatio(string? value)
    {
        return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) &&
            double.IsFinite(parsed) &&
            parsed > 0d
                ? parsed
                : null;
    }

    private static bool TryParseClientRatio(string? value, out double result)
    {
        return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result) &&
            double.IsFinite(result) &&
            result is >= 0d and <= 1d;
    }

    private static void WriteClientRatioPair(
        IniDocument document,
        string section,
        string xKey,
        string yKey,
        double? x,
        double? y)
    {
        if (x is not (>= 0d and <= 1d) ||
            y is not (>= 0d and <= 1d) ||
            !double.IsFinite(x.Value) ||
            !double.IsFinite(y.Value))
        {
            return;
        }

        document.Set(section, xKey, Format(x));
        document.Set(section, yKey, Format(y));
    }

    private static void WriteCaptureAspectRatio(
        IniDocument document,
        string section,
        string key,
        double? aspectRatio,
        double? x,
        double? y)
    {
        if (x is not (>= 0d and <= 1d) ||
            y is not (>= 0d and <= 1d) ||
            !double.IsFinite(x.Value) ||
            !double.IsFinite(y.Value) ||
            aspectRatio is not > 0d ||
            !double.IsFinite(aspectRatio.Value))
        {
            return;
        }

        document.Set(section, key, Format(aspectRatio));
    }

    private static int RoundLikeAhk(double value)
    {
        return checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static bool IsDefaultProfileName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        return normalized.Length == 0 || normalized == "默认/未读取";
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Format(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private static void ValidateShape(MacroConfiguration configuration)
    {
        if (configuration.Flows.Count != LegacyCatalog.FlowCount
            || configuration.Flows.Any(flow => flow.Groups.Count != LegacyCatalog.GroupCount))
        {
            throw new InvalidOperationException("Legacy profiles require 8 flows with 8 groups each.");
        }

        if (configuration.KeyMap.Skills.Count != LegacyCatalog.SkillSlotCount
            || configuration.KeyMap.Items.Count != LegacyCatalog.ItemSlotCount)
        {
            throw new InvalidOperationException("Legacy profiles require 12 skill slots and 6 item slots.");
        }

        foreach (var name in LegacyCatalog.NpcNames)
        {
            if (!configuration.Npcs.ContainsKey(name))
            {
                throw new InvalidOperationException($"Missing legacy NPC: {name}");
            }
        }

        foreach (var name in LegacyCatalog.FarmNames)
        {
            if (!configuration.Farms.ContainsKey(name))
            {
                throw new InvalidOperationException($"Missing legacy farm: {name}");
            }
        }
    }
}
