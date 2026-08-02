using System.Text.Json;

namespace FireWill.App.Services;

public sealed class IniProjectReader
{
    private static readonly string[] FarmNames =
    {
        "妙木山挑战自我x20",
        "妙木山挑战自我x5",
        "家里挑战自我x10",
        "家里挑战自我x5",
        "家里追捕逃忍",
        "去尾兽处",
        "尾兽处追捕逃忍"
    };

    private static readonly string[] NpcNames =
    {
        "妙木山大蛤蟆",
        "妙木山挑战自我NPC",
        "家里挑战自我NPC",
        "家里追捕逃忍NPC",
        "尾兽处追捕逃忍NPC"
    };

    private readonly string _projectRoot;
    private readonly string _configPath;

    public IniProjectReader(string projectRoot)
    {
        _projectRoot = projectRoot;
        _configPath = Path.Combine(projectRoot, "war3_macro_gui.ini");
    }

    public object ReadProjectState(string? toast = null)
    {
        IniDocument ini = IniDocument.Load(_configPath);
        string profileDir = Path.Combine(_projectRoot, "profiles");

        return new
        {
            toast,
            profileName = ini.Get("General", "currentProfileName", "默认/未读取"),
            stopHotkey = ini.Get("General", "stopHotkey", "Z"),
            gameWindowMatcher = ini.Get("General", "gameWindowMatcher", ""),
            profiles = Directory.Exists(profileDir)
                ? Directory.GetFiles(profileDir, "*.ini").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToArray()
                : Array.Empty<string>(),
            farms = FarmNames.Select(name => new
            {
                name,
                actionKey = ini.Get("Farm." + name, "actionKey"),
                releaseType = ini.Get("Farm." + name, "releaseType", "无"),
                releaseKey = ini.Get("Farm." + name, "releaseKey"),
                targetX = ini.Get("Farm." + name, "targetX"),
                targetY = ini.Get("Farm." + name, "targetY")
            }),
            keyMap = new
            {
                skills = Enumerable.Range(1, 12).Select(slot => new
                {
                    slot,
                    key = ini.Get("KeyMap", "skill" + slot),
                    cooldown = ReadInt(ini.Get("SkillCooldown", "skill" + slot), 0)
                }),
                items = Enumerable.Range(1, 6).Select(slot => new
                {
                    slot,
                    key = ini.Get("KeyMap", "item" + slot)
                })
            },
            flows = Enumerable.Range(1, 8).Select(slot => new
            {
                slot,
                name = ini.Get("Flow." + slot, "name", "自定义流程" + slot),
                enabled = ini.Get("Flow." + slot, "enabled", "0") == "1",
                hotkey = ini.Get("Flow." + slot, "hotkey"),
                groups = Enumerable.Range(1, 8).Select(group => new
                {
                    group,
                    enabled = ini.Get($"Flow.{slot}.Group.{group}", "enabled", "0") == "1",
                    preType = ini.Get($"Flow.{slot}.Group.{group}", "preType", "无"),
                    preValue = ini.Get($"Flow.{slot}.Group.{group}", "preValue"),
                    farm = ini.Get($"Flow.{slot}.Group.{group}", "farm", "无"),
                    wait = ReadInt(ini.Get($"Flow.{slot}.Group.{group}", "wait"), 0),
                    duration = ReadInt(ini.Get($"Flow.{slot}.Group.{group}", "duration"), 0)
                })
            }),
            checks = new
            {
                missingNpc = NpcNames.Count(name =>
                    string.IsNullOrWhiteSpace(ini.Get("NPC." + name, "x")) ||
                    string.IsNullOrWhiteSpace(ini.Get("NPC." + name, "y"))),
                mappedSkills = Enumerable.Range(1, 12).Count(slot => !string.IsNullOrWhiteSpace(ini.Get("KeyMap", "skill" + slot))),
                mappedItems = Enumerable.Range(1, 6).Count(slot => !string.IsNullOrWhiteSpace(ini.Get("KeyMap", "item" + slot))),
                enabledFlows = Enumerable.Range(1, 8).Count(slot => ini.Get("Flow." + slot, "enabled", "0") == "1")
            }
        };
    }

    public void SaveUserBindings(JsonElement payload)
    {
        IniDocument ini = IniDocument.Load(_configPath);

        if (payload.TryGetProperty("skills", out JsonElement skills))
        {
            foreach (JsonElement skill in skills.EnumerateArray())
            {
                int slot = skill.GetProperty("slot").GetInt32();
                if (slot is < 1 or > 12)
                {
                    continue;
                }

                ini.Set("KeyMap", "skill" + slot, skill.GetProperty("key").GetString() ?? "");
                int cooldown = skill.TryGetProperty("cooldown", out JsonElement cd) ? cd.GetInt32() : 0;
                ini.Set("SkillCooldown", "skill" + slot, Math.Clamp(cooldown, 0, 600).ToString());
            }
        }

        if (payload.TryGetProperty("items", out JsonElement items))
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                int slot = item.GetProperty("slot").GetInt32();
                if (slot is < 1 or > 6)
                {
                    continue;
                }

                ini.Set("KeyMap", "item" + slot, item.GetProperty("key").GetString() ?? "");
            }
        }

        if (payload.TryGetProperty("farms", out JsonElement farms))
        {
            foreach (JsonElement farm in farms.EnumerateArray())
            {
                string name = farm.GetProperty("name").GetString() ?? "";
                if (!FarmNames.Contains(name))
                {
                    continue;
                }

                string section = "Farm." + name;
                ini.Set(section, "actionKey", farm.GetProperty("actionKey").GetString() ?? "");
                ini.Set(section, "releaseType", farm.GetProperty("releaseType").GetString() ?? "无");
                ini.Set(section, "releaseKey", farm.GetProperty("releaseKey").GetString() ?? "");
            }
        }

        ini.Save(_configPath);
    }

    private static int ReadInt(string value, int fallback)
    {
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }
}

