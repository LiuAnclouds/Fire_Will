using System.Text;

namespace FireWill.Core.Configuration;

internal sealed class IniDocument
{
    private readonly List<IniSection> _sections = [];
    private readonly Dictionary<string, IniSection> _sectionLookup = new(StringComparer.OrdinalIgnoreCase);

    public static IniDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var document = new IniDocument();
        IniSection? currentSection = null;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            {
                var sectionName = trimmed[1..^1].Trim();
                currentSection = sectionName.Length == 0 ? null : document.GetOrAddSection(sectionName);
                continue;
            }

            if (currentSection is null)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (key.Length > 0)
            {
                currentSection.Set(key, line[(separator + 1)..]);
            }
        }

        return document;
    }

    public bool TryGet(string section, string key, out string value)
    {
        if (_sectionLookup.TryGetValue(section, out var foundSection) && foundSection.TryGet(key, out value))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }

    public string Get(string section, string key, string fallback = "")
    {
        return TryGet(section, key, out var value) ? value : fallback;
    }

    public void Set(string section, string key, string? value)
    {
        ValidateToken(section, nameof(section));
        ValidateToken(key, nameof(key));
        GetOrAddSection(section).Set(key, SanitizeValue(value));
    }

    public string Serialize()
    {
        var result = new StringBuilder();
        foreach (var section in _sections)
        {
            result.Append('[').Append(section.Name).Append("]\r\n");
            foreach (var entry in section.Entries)
            {
                result.Append(entry.Key).Append('=').Append(entry.Value).Append("\r\n");
            }
        }

        return result.ToString();
    }

    private IniSection GetOrAddSection(string name)
    {
        if (_sectionLookup.TryGetValue(name, out var section))
        {
            return section;
        }

        section = new IniSection(name);
        _sections.Add(section);
        _sectionLookup.Add(name, section);
        return section;
    }

    private static void ValidateToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.IndexOfAny(['\r', '\n', '[', ']', '=']) >= 0)
        {
            throw new ArgumentException("INI section and key names cannot contain control delimiters.", parameterName);
        }
    }

    private static string SanitizeValue(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    private sealed class IniSection(string name)
    {
        private readonly List<IniEntry> _entries = [];
        private readonly Dictionary<string, int> _entryLookup = new(StringComparer.OrdinalIgnoreCase);

        public string Name { get; } = name;

        public IReadOnlyList<IniEntry> Entries => _entries;

        public bool TryGet(string key, out string value)
        {
            if (_entryLookup.TryGetValue(key, out var index))
            {
                value = _entries[index].Value;
                return true;
            }

            value = string.Empty;
            return false;
        }

        public void Set(string key, string value)
        {
            if (_entryLookup.TryGetValue(key, out var index))
            {
                _entries[index] = _entries[index] with { Value = value };
                return;
            }

            _entryLookup.Add(key, _entries.Count);
            _entries.Add(new IniEntry(key, value));
        }
    }

    internal sealed record IniEntry(string Key, string Value);
}
