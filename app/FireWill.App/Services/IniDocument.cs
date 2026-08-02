using System.Text;

namespace FireWill.App.Services;

public sealed class IniDocument
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public static IniDocument Load(string path)
    {
        IniDocument document = new();
        if (!File.Exists(path))
        {
            return document;
        }

        string section = "";
        foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                document.EnsureSection(section);
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();
            document.Set(section, key, value);
        }

        return document;
    }

    public string Get(string section, string key, string fallback = "")
    {
        return _sections.TryGetValue(section, out Dictionary<string, string>? values) &&
            values.TryGetValue(key, out string? value)
            ? value
            : fallback;
    }

    public void Set(string section, string key, string value)
    {
        EnsureSection(section)[key] = value;
    }

    public void Save(string path)
    {
        StringBuilder builder = new();
        foreach ((string section, Dictionary<string, string> values) in _sections)
        {
            if (section.Length > 0)
            {
                builder.Append('[').Append(section).AppendLine("]");
            }

            foreach ((string key, string value) in values)
            {
                builder.Append(key).Append('=').AppendLine(value);
            }

            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private Dictionary<string, string> EnsureSection(string section)
    {
        if (!_sections.TryGetValue(section, out Dictionary<string, string>? values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections[section] = values;
        }

        return values;
    }
}

