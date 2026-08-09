namespace FireWill.Core.Configuration;

public static class LegacyValues
{
    public const string None = "无";
    public const string KeyPreCommand = "按键";
    public const string ChatPreCommand = "公屏";
    public const string SkillKeyRelease = "技能按键";
    public const string ItemKeyRelease = "装备按键";
    public const string SkillSlotRelease = "技能槽位";
    public const string ItemSlotRelease = "装备槽位";
}

public static class LegacyCatalog
{
    public const int FlowCount = 8;
    public const int GroupCount = 8;
    public const int SkillSlotCount = 12;
    public const int ItemSlotCount = 6;

    public static IReadOnlyList<string> NpcNames { get; } = Array.AsReadOnly(
    [
        "妙木山大蛤蟆",
        "妙木山挑战自我NPC",
        "家里挑战自我NPC",
        "家里追捕逃忍NPC",
        "尾兽处追捕逃忍NPC",
    ]);

    public static IReadOnlyList<string> FarmNames { get; } = Array.AsReadOnly(
    [
        "妙木山挑战自我x20",
        "妙木山挑战自我x5",
        "家里挑战自我x10",
        "家里挑战自我x5",
        "家里追捕逃忍",
        "去尾兽处",
        "尾兽处追捕逃忍",
    ]);
}

public sealed class MacroConfiguration
{
    public GeneralSettings General { get; } = new();

    public Dictionary<string, NpcSettings> Npcs { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, FarmSettings> Farms { get; } = new(StringComparer.Ordinal);

    public List<FlowSettings> Flows { get; } = [];

    public KeyMapSettings KeyMap { get; } = new();

    public FlowSettings GetFlow(int slot)
    {
        if (slot < 1 || slot > Flows.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Flow slots are one-based.");
        }

        return Flows[slot - 1];
    }
}

public sealed class GeneralSettings
{
    public string GameWindowMatcher { get; set; } = string.Empty;

    public bool SkipGameCheck { get; set; }

    public string StopHotkey { get; set; } = "Z";

    public int KeyDelayMs { get; set; } = 40;

    public int SkillKeyDelayMs { get; set; } = 100;

    public int HeroSelectDelayMs { get; set; } = 80;

    public int NpcClickDelayMs { get; set; } = 100;

    public int ChatDelayMs { get; set; } = 500;

    public int TeleportKeyDelayMs { get; set; } = 200;

    public int MouseMoveDelayMs { get; set; } = 30;

    public int ReleaseMouseMoveDelayMs { get; set; } = 80;

    public string CurrentProfileName { get; set; } = "默认/未读取";

    public string CurrentProfilePath { get; set; } = string.Empty;
}

public sealed class NpcSettings
{
    public required string Name { get; init; }

    public string Camera { get; set; } = string.Empty;

    public int? X { get; set; }

    public int? Y { get; set; }
}

public sealed class FarmSettings
{
    public required string Name { get; init; }

    public required string NpcName { get; init; }

    public required string NpcAction { get; init; }

    public string ActionKey { get; set; } = string.Empty;

    public string ReleaseType { get; set; } = LegacyValues.None;

    public string ReleaseKey { get; set; } = string.Empty;

    public int? TargetX { get; set; }

    public int? TargetY { get; set; }
}

public sealed class FlowSettings
{
    public int Slot { get; init; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string Hotkey { get; set; } = string.Empty;

    public int KeyDelayMs { get; set; } = 40;

    public int SkillKeyDelayMs { get; set; } = 100;

    public int HeroSelectDelayMs { get; set; } = 80;

    public int NpcClickDelayMs { get; set; } = 100;

    public int ChatDelayMs { get; set; } = 500;

    public int TeleportKeyDelayMs { get; set; } = 200;

    public int MouseMoveDelayMs { get; set; } = 30;

    public int ReleaseMouseMoveDelayMs { get; set; } = 80;

    public List<FlowGroupSettings> Groups { get; } = [];
}

public sealed class FlowGroupSettings
{
    public int Slot { get; init; }

    public bool Enabled { get; set; }

    public string PreType { get; set; } = LegacyValues.None;

    public string PreValue { get; set; } = string.Empty;

    public string FarmName { get; set; } = LegacyValues.None;

    // Null preserves old profiles where duration represented the whole group budget.
    public int? WaitMs { get; set; }

    public int DurationMs { get; set; }
}

public sealed class KeyMapSettings
{
    public List<string> Skills { get; } = Enumerable.Repeat(string.Empty, LegacyCatalog.SkillSlotCount).ToList();

    public List<string> Items { get; } = Enumerable.Repeat(string.Empty, LegacyCatalog.ItemSlotCount).ToList();
}
