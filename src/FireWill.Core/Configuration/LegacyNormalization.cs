using System.Globalization;
using System.Text.RegularExpressions;

namespace FireWill.Core.Configuration;

public static partial class LegacyNormalization
{
    public static int ToInt(string? value, int fallback = 0)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    public static int ClampDelay(string? value, int fallback, int maximum)
    {
        return Math.Clamp(ToInt(value, fallback), 0, maximum);
    }

    public static int? Coordinate(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length == 0
            ? null
            : int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : null;
    }

    public static string ReleaseType(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized == "物品按键" ? LegacyValues.ItemKeyRelease : normalized;
    }

    public static string ReleaseKey(string? releaseType, string? value)
    {
        var type = ReleaseType(releaseType);
        return type is LegacyValues.SkillSlotRelease or LegacyValues.ItemSlotRelease
            ? value?.Trim() ?? string.Empty
            : Key(value);
    }

    public static string PreValue(string? preType, string? value)
    {
        return preType == LegacyValues.KeyPreCommand ? Key(value) : value?.Trim() ?? string.Empty;
    }

    public static string FarmName(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized == "家里挑战自我x20" ? "家里挑战自我x10" : normalized;
    }

    public static string FlowName(string? value)
    {
        return (value?.Trim() ?? string.Empty)
            .Replace("（预留）", string.Empty, StringComparison.Ordinal)
            .Replace("(预留)", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    public static string Hotkey(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = ModifierSeparatorRegex().Replace(normalized.Replace('＋', '+'), "+");
        var parts = normalized.Split('+');
        if (parts.Length == 1)
        {
            return Key(normalized);
        }

        var prefix = string.Empty;
        var key = string.Empty;
        foreach (var part in parts)
        {
            var candidate = part.Trim();
            switch (candidate.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                case "控":
                case "控制":
                    AppendModifier(ref prefix, '^');
                    break;
                case "ALT":
                    AppendModifier(ref prefix, '!');
                    break;
                case "SHIFT":
                    AppendModifier(ref prefix, '+');
                    break;
                case "WIN":
                case "WINDOWS":
                    AppendModifier(ref prefix, '#');
                    break;
                default:
                    key = Key(candidate);
                    break;
            }
        }

        return key.Length > 0 ? prefix + key : Key(normalized);
    }

    public static string Key(string? value)
    {
        var raw = (value ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var normalized = raw.Trim(' ', '\t');
        if (normalized.Length == 0)
        {
            return raw.Contains('\t', StringComparison.Ordinal) ? "Tab" : "Space";
        }

        var braceMatch = BracedKeyRegex().Match(normalized);
        if (braceMatch.Success)
        {
            normalized = braceMatch.Groups[1].Value;
        }

        return normalized.ToUpperInvariant() switch
        {
            "TAB" or "制表" or "制表键" => "Tab",
            "SPACE" or "SPACEBAR" or "空格" or "空格键" => "Space",
            "ESC" or "ESCAPE" => "Esc",
            _ => normalized,
        };
    }

    public static bool IsTeleportKey(string? key)
    {
        var normalized = Key(key).ToUpperInvariant();
        return normalized is "F2" or "F3";
    }

    private static void AppendModifier(ref string prefix, char modifier)
    {
        if (!prefix.Contains(modifier, StringComparison.Ordinal))
        {
            prefix += modifier;
        }
    }

    [GeneratedRegex(@"\s*\+\s*", RegexOptions.CultureInvariant)]
    private static partial Regex ModifierSeparatorRegex();

    [GeneratedRegex(@"^\{([^{}]+)\}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BracedKeyRegex();
}
