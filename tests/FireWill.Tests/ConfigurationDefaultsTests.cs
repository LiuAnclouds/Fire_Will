using FireWill.Core.Configuration;

namespace FireWill.Tests;

public sealed class ConfigurationDefaultsTests
{
    [Fact]
    public void Create_ReproducesLegacyShapeAndDefaults()
    {
        var configuration = ConfigurationDefaults.Create();

        Assert.Equal(5, configuration.Npcs.Count);
        Assert.Equal(7, configuration.Farms.Count);
        Assert.Equal(8, configuration.Flows.Count);
        Assert.All(configuration.Flows, flow => Assert.Equal(8, flow.Groups.Count));
        Assert.Equal(12, configuration.KeyMap.Skills.Count);
        Assert.Equal(6, configuration.KeyMap.Items.Count);

        Assert.Equal((845, 390), Coordinates(configuration.Npcs["妙木山大蛤蟆"]));
        Assert.Equal((1172, 689), Coordinates(configuration.Npcs["妙木山挑战自我NPC"]));
        Assert.Equal((977, 509), Coordinates(configuration.Npcs["尾兽处追捕逃忍NPC"]));
        Assert.Equal((null, null), Coordinates(configuration.Npcs["家里挑战自我NPC"]));

        Assert.Equal("妙木山挑战自我NPC", configuration.Farms["妙木山挑战自我x20"].NpcName);
        Assert.Equal("x20", configuration.Farms["妙木山挑战自我x20"].NpcAction);
        Assert.Equal("家里挑战自我NPC", configuration.Farms["家里挑战自我x10"].NpcName);
        Assert.Equal("x10", configuration.Farms["家里挑战自我x10"].NpcAction);

        Assert.Equal("Z", configuration.General.StopHotkey);
        Assert.Equal(40, configuration.General.KeyDelayMs);
        Assert.Equal(100, configuration.General.SkillKeyDelayMs);
        Assert.Equal(80, configuration.General.HeroSelectDelayMs);
        Assert.Equal(100, configuration.General.NpcClickDelayMs);
        Assert.Equal(500, configuration.General.ChatDelayMs);
        Assert.Equal(200, configuration.General.TeleportKeyDelayMs);
        Assert.Equal(30, configuration.General.MouseMoveDelayMs);
        Assert.Equal(80, configuration.General.ReleaseMouseMoveDelayMs);
    }

    [Theory]
    [InlineData("Tab", "Tab")]
    [InlineData("{Space}", "Space")]
    [InlineData("空格键", "Space")]
    [InlineData("Escape", "Esc")]
    [InlineData("Ctrl + Alt + X", "^!X")]
    [InlineData("Alt+Ctrl+X", "!^X")]
    [InlineData("!2", "!2")]
    public void Normalization_MatchesLegacyAliases(string input, string expected)
    {
        var actual = input.Contains('+', StringComparison.Ordinal)
            ? LegacyNormalization.Hotkey(input)
            : LegacyNormalization.Key(input);

        Assert.Equal(expected, actual);
    }

    private static (int? X, int? Y) Coordinates(NpcSettings npc) => (npc.X, npc.Y);
}
