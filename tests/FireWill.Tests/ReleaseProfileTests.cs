using FireWill.Core.Configuration;
using FireWill.Core.Execution;

namespace FireWill.Tests;

public sealed class ReleaseProfileTests
{
    [Fact]
    public void Defaults_CreateSevenSkillAndTwoItemProfilesInDisplayOrder()
    {
        var configuration = ConfigurationDefaults.Create();

        Assert.Equal(
            ["Q技能", "W技能", "E技能", "R技能", "D技能", "F技能", "B技能", "装备1", "装备2"],
            ReleaseProfileCatalog.Names);
        Assert.Equal(9, configuration.ReleaseProfiles.Count);
        Assert.Equal(7, configuration.ReleaseProfiles.Values.Count(profile => profile.Kind == ReleaseProfileKind.Skill));
        Assert.Equal(2, configuration.ReleaseProfiles.Values.Count(profile => profile.Kind == ReleaseProfileKind.Item));
        Assert.Equal("skill:1", configuration.ReleaseProfiles["Q技能"].KeyReference);
        Assert.Equal("skill:7", configuration.ReleaseProfiles["B技能"].KeyReference);
        Assert.Equal("item:1", configuration.ReleaseProfiles["装备1"].KeyReference);
        Assert.Equal("item:2", configuration.ReleaseProfiles["装备2"].KeyReference);
    }

    [Fact]
    public void KeyReferences_ResolveStableAndChineseDisplayNamesAtRuntime()
    {
        var configuration = ConfigurationDefaults.Create();
        configuration.KeyMap.Skills[0] = "W";
        configuration.KeyMap.Items[1] = "Numpad2";

        Assert.Equal("W", KeyMapReferences.Resolve(configuration.KeyMap, "skill:1"));
        Assert.Equal("W", KeyMapReferences.Resolve(configuration.KeyMap, "技能按键1"));
        Assert.Equal("Numpad2", KeyMapReferences.Resolve(configuration.KeyMap, "装备按键2"));
        Assert.Equal(string.Empty, KeyMapReferences.Resolve(configuration.KeyMap, "skill:13"));
        Assert.Equal(string.Empty, KeyMapReferences.Resolve(configuration.KeyMap, "item:7"));
    }

    [Fact]
    public void CompileGroup_TaskAndReleaseSelectionsAreIndependent()
    {
        var configuration = ConfigurationDefaults.Create();
        configuration.KeyMap.Skills[0] = "Q";
        configuration.KeyMap.Skills[1] = "W";
        var npc = configuration.Npcs["家里挑战自我NPC"];
        npc.X = 100;
        npc.Y = 200;
        var farm = configuration.Farms["家里挑战自我x5"];
        farm.ActionKey = "skill:1";
        var flow = configuration.GetFlow(1);
        flow.Enabled = true;
        var group = flow.Groups[0];
        group.Enabled = true;
        group.FarmName = farm.Name;
        group.ReleaseProfileName = "W技能";
        group.ReleaseSelectionIsExplicit = true;

        var compiled = new MacroActionCompiler().CompileGroup(configuration, flow, group);
        var keys = compiled.Actions.OfType<KeyPressAction>().Select(action => action.Key).ToArray();

        Assert.Equal(["Q", "F1", "W"], keys);
        Assert.Single(compiled.Actions.OfType<MoveMouseAction>());
    }

    [Fact]
    public void CompileGroup_ReleaseOnlyWorksWithoutFarmOrMouseTarget()
    {
        var configuration = ConfigurationDefaults.Create();
        configuration.KeyMap.Items[0] = "Numpad7";
        var flow = configuration.GetFlow(1);
        var group = flow.Groups[0];
        group.ReleaseProfileName = "装备1";
        group.ReleaseSelectionIsExplicit = true;

        var compiled = new MacroActionCompiler().CompileGroup(configuration, flow, group);

        Assert.Equal(["F1", "Numpad7"], compiled.Actions.OfType<KeyPressAction>().Select(action => action.Key));
        Assert.Empty(compiled.Actions.OfType<MoveMouseAction>());
        Assert.Empty(compiled.Actions.OfType<WarningAction>());
    }

    [Fact]
    public void CompileGroup_DoesNotReleaseWhenSelectedFarmTaskCannotRun()
    {
        var configuration = ConfigurationDefaults.Create();
        configuration.KeyMap.Skills[0] = "Q";
        var flow = configuration.GetFlow(1);
        var group = flow.Groups[0];
        group.FarmName = "家里挑战自我x5";
        group.ReleaseProfileName = "Q技能";
        group.ReleaseSelectionIsExplicit = true;

        var compiled = new MacroActionCompiler().CompileGroup(configuration, flow, group);

        Assert.DoesNotContain(compiled.Actions.OfType<KeyPressAction>(), action => action.Key == "F1");
        Assert.Contains(compiled.Actions.OfType<WarningAction>(), warning =>
            warning.Message.Contains("NPC未标定", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileGroup_DoesNotClickWhenFarmStartupMappingIsMissing()
    {
        var configuration = ConfigurationDefaults.Create();
        var npc = configuration.Npcs["家里挑战自我NPC"];
        npc.X = 100;
        npc.Y = 200;
        var farm = configuration.Farms["家里挑战自我x5"];
        farm.ActionKey = string.Empty;
        var flow = configuration.GetFlow(1);
        var group = flow.Groups[0];
        group.FarmName = farm.Name;

        var compiled = new MacroActionCompiler().CompileGroup(configuration, flow, group);

        Assert.DoesNotContain(compiled.Actions, action => action is MoveMouseAction or LeftClickAction);
        Assert.Contains(compiled.Actions.OfType<WarningAction>(), warning =>
            warning.Message.Contains("NPC动作缺少按键", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileGroup_ExplicitNoneDoesNotRunLegacyFarmRelease()
    {
        var configuration = MacroActionCompilerTests.CreateReleaseConfiguration();
        var flow = configuration.GetFlow(1);
        var group = flow.Groups[0];
        group.FarmName = "家里挑战自我x5";
        group.ReleaseProfileName = LegacyValues.None;
        group.ReleaseSelectionIsExplicit = true;

        var compiled = new MacroActionCompiler().CompileGroup(configuration, flow, group);

        Assert.DoesNotContain(compiled.Actions.OfType<KeyPressAction>(), action => action.Key == "F1");
        Assert.Single(compiled.Actions.OfType<MoveMouseAction>());
    }

    [Fact]
    public void ResolveReleaseKey_RejectsReferenceFromWrongMappingKind()
    {
        var configuration = ConfigurationDefaults.Create();
        configuration.KeyMap.Items[0] = "Numpad7";
        configuration.ReleaseProfiles["Q技能"].KeyReference = "item:1";

        var key = new MacroActionCompiler().ResolveReleaseKey(configuration, "Q技能");

        Assert.Equal(string.Empty, key);
    }

    [Fact]
    public void Serializer_RoundTripsProfilesAndIndependentFlowSelection()
    {
        var configuration = ConfigurationDefaults.Create();
        configuration.ReleaseProfiles["Q技能"].KeyReference = "skill:4";
        var group = configuration.GetFlow(1).Groups[0];
        group.FarmName = "家里挑战自我x5";
        group.ReleaseProfileName = "Q技能";
        group.ReleaseSelectionIsExplicit = true;

        var serialized = LegacyIniProfileSerializer.Serialize(configuration);
        var roundTripped = LegacyIniProfileSerializer.Parse(serialized);

        Assert.Contains("[Release.Q技能]", serialized, StringComparison.Ordinal);
        Assert.Contains("keyReference=skill:4", serialized, StringComparison.Ordinal);
        Assert.Contains("releaseProfile=Q技能", serialized, StringComparison.Ordinal);
        Assert.Equal("skill:4", roundTripped.ReleaseProfiles["Q技能"].KeyReference);
        Assert.Equal("Q技能", roundTripped.GetFlow(1).Groups[0].ReleaseProfileName);
        Assert.True(roundTripped.GetFlow(1).Groups[0].ReleaseSelectionIsExplicit);
    }

    [Fact]
    public void Serializer_MigratesLegacyDirectQToQProfile()
    {
        const string legacy = """
            [Farm.家里挑战自我x5]
            releaseType=技能按键
            releaseKey=Q
            [Flow.1.Group.1]
            enabled=1
            farm=家里挑战自我x5
            """;

        var configuration = LegacyIniProfileSerializer.Parse(legacy);
        var group = configuration.GetFlow(1).Groups[0];

        Assert.Equal("Q技能", group.ReleaseProfileName);
        Assert.True(group.ReleaseSelectionIsExplicit);
        Assert.Equal("direct:Q", configuration.ReleaseProfiles["Q技能"].KeyReference);
    }

    [Fact]
    public void Serializer_MigratesFarmStartupKeyToStableMappingReference()
    {
        const string legacy = """
            [KeyMap]
            skill1=Q
            [Farm.家里挑战自我x5]
            actionKey=Q
            """;

        var configuration = LegacyIniProfileSerializer.Parse(legacy);

        Assert.Equal("skill:1", configuration.Farms["家里挑战自我x5"].ActionKey);
    }

    [Fact]
    public void Serializer_ExplicitNoneSurvivesRoundTripAndBlocksLegacyFallback()
    {
        var configuration = MacroActionCompilerTests.CreateReleaseConfiguration();
        var group = configuration.GetFlow(1).Groups[0];
        group.FarmName = "家里挑战自我x5";
        group.ReleaseProfileName = LegacyValues.None;
        group.ReleaseSelectionIsExplicit = true;

        var roundTripped = LegacyIniProfileSerializer.Parse(
            LegacyIniProfileSerializer.Serialize(configuration));
        var reloadedGroup = roundTripped.GetFlow(1).Groups[0];
        var compiled = new MacroActionCompiler().CompileGroup(
            roundTripped,
            roundTripped.GetFlow(1),
            reloadedGroup);

        Assert.Equal(LegacyValues.None, reloadedGroup.ReleaseProfileName);
        Assert.True(reloadedGroup.ReleaseSelectionIsExplicit);
        Assert.DoesNotContain(compiled.Actions.OfType<KeyPressAction>(), action => action.Key == "F1");
    }

    [Fact]
    public void Serializer_MarksLegacyFarmWithoutReleaseAsExplicitNone()
    {
        const string legacy = """
            [Farm.家里挑战自我x5]
            releaseType=无
            releaseKey=Q
            [Flow.1.Group.1]
            enabled=1
            farm=家里挑战自我x5
            """;

        var configuration = LegacyIniProfileSerializer.Parse(legacy);
        var group = configuration.GetFlow(1).Groups[0];

        Assert.True(group.ReleaseSelectionIsExplicit);
        Assert.Equal(LegacyValues.None, group.ReleaseProfileName);
    }

    [Fact]
    public void Serializer_MigratesDifferentLegacySlotsToDifferentProfilesWithoutOverwrite()
    {
        const string legacy = """
            [Farm.家里挑战自我x5]
            releaseType=技能槽位
            releaseKey=8
            [Farm.家里挑战自我x10]
            releaseType=技能槽位
            releaseKey=9
            [Farm.家里追捕逃忍]
            releaseType=装备槽位
            releaseKey=3
            [Farm.尾兽处追捕逃忍]
            releaseType=装备槽位
            releaseKey=4
            [Flow.1.Group.1]
            enabled=1
            farm=家里挑战自我x5
            [Flow.1.Group.2]
            enabled=1
            farm=家里挑战自我x10
            [Flow.1.Group.3]
            enabled=1
            farm=家里追捕逃忍
            [Flow.1.Group.4]
            enabled=1
            farm=尾兽处追捕逃忍
            """;

        var configuration = LegacyIniProfileSerializer.Parse(legacy);
        var groups = configuration.GetFlow(1).Groups;
        var selectedNames = groups.Take(4).Select(group => group.ReleaseProfileName).ToArray();

        Assert.Equal(4, selectedNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("skill:8", configuration.ReleaseProfiles[selectedNames[0]].KeyReference);
        Assert.Equal("skill:9", configuration.ReleaseProfiles[selectedNames[1]].KeyReference);
        Assert.Equal("item:3", configuration.ReleaseProfiles[selectedNames[2]].KeyReference);
        Assert.Equal("item:4", configuration.ReleaseProfiles[selectedNames[3]].KeyReference);
    }
}
