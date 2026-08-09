using System.Globalization;

namespace FireWill.Core.Configuration;

public enum KeyMapReferenceKind
{
    Skill,
    Item,
}

/// <summary>
/// Stores a stable reference to a skill or item slot while allowing legacy
/// profiles that stored a physical key to continue working.
/// </summary>
public static class KeyMapReferences
{
    public static string Skill(int slot) => $"skill:{slot}";

    public static string Item(int slot) => $"item:{slot}";

    public static string Direct(string key) => $"direct:{LegacyNormalization.Key(key)}";

    public static IReadOnlyList<string> All()
    {
        return
        [
            .. Enumerable.Range(1, LegacyCatalog.SkillSlotCount).Select(Skill),
            .. Enumerable.Range(1, LegacyCatalog.ItemSlotCount).Select(Item),
        ];
    }

    public static IReadOnlyList<string> All(KeyMapReferenceKind kind)
    {
        return kind == KeyMapReferenceKind.Skill
            ? Enumerable.Range(1, LegacyCatalog.SkillSlotCount).Select(Skill).ToArray()
            : Enumerable.Range(1, LegacyCatalog.ItemSlotCount).Select(Item).ToArray();
    }

    public static string Canonicalize(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized == LegacyValues.None)
        {
            return string.Empty;
        }

        if (TryParse(normalized, out var kind, out var slot))
        {
            return kind == KeyMapReferenceKind.Skill ? Skill(slot) : Item(slot);
        }

        if (TryParseDisplayName(normalized, out kind, out slot))
        {
            return kind == KeyMapReferenceKind.Skill ? Skill(slot) : Item(slot);
        }

        if (normalized.StartsWith("direct:", StringComparison.OrdinalIgnoreCase))
        {
            return Direct(normalized["direct:".Length..]);
        }

        return Direct(normalized);
    }

    public static bool TryParse(
        string? value,
        out KeyMapReferenceKind kind,
        out int slot)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (TryParseSlot(normalized, "skill:", out slot))
        {
            kind = KeyMapReferenceKind.Skill;
            return true;
        }

        if (TryParseSlot(normalized, "item:", out slot))
        {
            kind = KeyMapReferenceKind.Item;
            return true;
        }

        kind = default;
        slot = 0;
        return false;
    }

    public static bool TryParseDisplayName(
        string? value,
        out KeyMapReferenceKind kind,
        out int slot)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (TryParseSlot(normalized, "技能按键", out slot) ||
            TryParseSlot(normalized, "技能槽位", out slot))
        {
            kind = KeyMapReferenceKind.Skill;
            return true;
        }

        if (TryParseSlot(normalized, "装备按键", out slot) ||
            TryParseSlot(normalized, "装备槽位", out slot))
        {
            kind = KeyMapReferenceKind.Item;
            return true;
        }

        kind = default;
        slot = 0;
        return false;
    }

    public static bool TryGetDirect(string? value, out string key)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.StartsWith("direct:", StringComparison.OrdinalIgnoreCase))
        {
            key = LegacyNormalization.Key(normalized["direct:".Length..]);
            return key.Length > 0;
        }

        key = string.Empty;
        return false;
    }

    public static string Resolve(KeyMapSettings keyMap, string? reference)
    {
        ArgumentNullException.ThrowIfNull(keyMap);
        var normalized = reference?.Trim() ?? string.Empty;
        if (TryParse(normalized, out var kind, out var slot) ||
            TryParseDisplayName(normalized, out kind, out slot))
        {
            var keys = kind == KeyMapReferenceKind.Skill ? keyMap.Skills : keyMap.Items;
            var maximum = kind == KeyMapReferenceKind.Skill
                ? LegacyCatalog.SkillSlotCount
                : LegacyCatalog.ItemSlotCount;
            return slot is >= 1
                   && slot <= maximum
                   && slot <= keys.Count
                ? LegacyNormalization.Key(keys[slot - 1])
                : string.Empty;
        }

        if (TryGetDirect(normalized, out var directKey))
        {
            return directKey;
        }

        // Profiles written before mapped references stored the physical key.
        return LegacyNormalization.Key(normalized);
    }

    public static string Find(
        KeyMapSettings keyMap,
        string? rawValue,
        KeyMapReferenceKind? preferredKind = null)
    {
        ArgumentNullException.ThrowIfNull(keyMap);
        var normalized = rawValue?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized == LegacyValues.None)
        {
            return string.Empty;
        }

        if (TryParse(normalized, out var parsedKind, out var parsedSlot))
        {
            return parsedKind == KeyMapReferenceKind.Skill
                ? Skill(parsedSlot)
                : Item(parsedSlot);
        }

        if (TryParseDisplayName(normalized, out parsedKind, out parsedSlot))
        {
            return parsedKind == KeyMapReferenceKind.Skill
                ? Skill(parsedSlot)
                : Item(parsedSlot);
        }

        var key = Resolve(keyMap, normalized);
        if (key.Length == 0)
        {
            return string.Empty;
        }

        if (preferredKind is null or KeyMapReferenceKind.Skill)
        {
            var skillSlot = keyMap.Skills.FindIndex(value =>
                string.Equals(LegacyNormalization.Key(value), key, StringComparison.OrdinalIgnoreCase));
            if (skillSlot >= 0)
            {
                return Skill(skillSlot + 1);
            }
        }

        if (preferredKind is null or KeyMapReferenceKind.Item)
        {
            var itemSlot = keyMap.Items.FindIndex(value =>
                string.Equals(LegacyNormalization.Key(value), key, StringComparison.OrdinalIgnoreCase));
            if (itemSlot >= 0)
            {
                return Item(itemSlot + 1);
            }
        }

        return Direct(key);
    }

    public static string DisplayName(string? reference)
    {
        var normalized = reference?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized == LegacyValues.None)
        {
            return LegacyValues.None;
        }

        if (TryParse(normalized, out var kind, out var slot) ||
            TryParseDisplayName(normalized, out kind, out slot))
        {
            return kind == KeyMapReferenceKind.Skill
                ? $"技能按键{slot}"
                : $"装备按键{slot}";
        }

        if (TryGetDirect(normalized, out var directKey))
        {
            return $"待映射：{directKey}";
        }

        return normalized;
    }

    private static bool TryParseSlot(string value, string prefix, out int slot)
    {
        slot = 0;
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
            value[prefix.Length..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out slot) && slot > 0;
    }
}
