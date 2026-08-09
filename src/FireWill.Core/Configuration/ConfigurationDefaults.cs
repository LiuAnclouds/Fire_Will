namespace FireWill.Core.Configuration;

public static class ConfigurationDefaults
{
    public static MacroConfiguration Create()
    {
        var configuration = new MacroConfiguration();

        AddNpc(configuration, "妙木山大蛤蟆", 845, 390);
        AddNpc(configuration, "妙木山挑战自我NPC", 1172, 689);
        AddNpc(configuration, "家里挑战自我NPC", null, null);
        AddNpc(configuration, "家里追捕逃忍NPC", null, null);
        AddNpc(configuration, "尾兽处追捕逃忍NPC", 977, 509);

        AddFarm(configuration, "妙木山挑战自我x20", "妙木山挑战自我NPC", "x20");
        AddFarm(configuration, "妙木山挑战自我x5", "妙木山挑战自我NPC", "x5");
        AddFarm(configuration, "家里挑战自我x10", "家里挑战自我NPC", "x10");
        AddFarm(configuration, "家里挑战自我x5", "家里挑战自我NPC", "x5");
        AddFarm(configuration, "家里追捕逃忍", "家里追捕逃忍NPC", "追捕");
        AddFarm(configuration, "去尾兽处", "妙木山大蛤蟆", "去尾兽处");
        AddFarm(configuration, "尾兽处追捕逃忍", "尾兽处追捕逃忍NPC", "追捕");

        foreach (var definition in ReleaseProfileCatalog.Definitions)
        {
            configuration.ReleaseProfiles.Add(
                definition.Name,
                new ReleaseProfileSettings
                {
                    Name = definition.Name,
                    Kind = definition.Kind,
                    KeyReference = definition.Kind == ReleaseProfileKind.Skill
                        ? KeyMapReferences.Skill(definition.DefaultSlot)
                        : KeyMapReferences.Item(definition.DefaultSlot),
                });
        }

        for (var flowSlot = 1; flowSlot <= LegacyCatalog.FlowCount; flowSlot++)
        {
            var flow = new FlowSettings
            {
                Slot = flowSlot,
                Name = $"自定义流程{flowSlot}",
            };

            for (var groupSlot = 1; groupSlot <= LegacyCatalog.GroupCount; groupSlot++)
            {
                flow.Groups.Add(new FlowGroupSettings { Slot = groupSlot, WaitMs = 0 });
            }

            configuration.Flows.Add(flow);
        }

        return configuration;
    }

    private static void AddNpc(MacroConfiguration configuration, string name, int? x, int? y)
    {
        configuration.Npcs.Add(name, new NpcSettings { Name = name, X = x, Y = y });
    }

    private static void AddFarm(MacroConfiguration configuration, string name, string npcName, string action)
    {
        configuration.Farms.Add(name, new FarmSettings
        {
            Name = name,
            NpcName = npcName,
            NpcAction = action,
        });
    }
}
