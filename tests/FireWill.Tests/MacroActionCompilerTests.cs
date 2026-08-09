using FireWill.Core.Configuration;
using FireWill.Core.Execution;

namespace FireWill.Tests;

public sealed class MacroActionCompilerTests
{
    [Fact]
    public void CompileGroup_EmptyKeyPreValue_IsNoOpAndFarmStillCompiles()
    {
        var configuration = CreateReleaseConfiguration();
        var flow = configuration.GetFlow(1);
        var group = flow.Groups[0];
        group.Enabled = true;
        group.PreType = LegacyValues.KeyPreCommand;
        group.PreValue = string.Empty;
        group.FarmName = "家里挑战自我x5";
        group.WaitMs = 280;

        var compiled = new MacroActionCompiler().CompileGroup(configuration, flow, group);

        Assert.DoesNotContain(
            compiled.Actions.OfType<KeyPressAction>(),
            action => string.IsNullOrEmpty(action.Key));
        Assert.IsType<StopBoundaryAction>(compiled.Actions[0]);
        Assert.IsType<MoveMouseAction>(compiled.Actions[1]);
        Assert.Equal(280, compiled.WaitMilliseconds);
        Assert.Equal(230, compiled.CountedActionDurationMs);
    }

    [Fact]
    public void CompileGroup_MissingWait_UsesLegacyDurationBudget()
    {
        var configuration = CreateReleaseConfiguration();
        var flow = configuration.GetFlow(1);
        var group = flow.Groups[0];
        group.Enabled = true;
        group.PreType = LegacyValues.KeyPreCommand;
        group.PreValue = "F2";
        group.FarmName = "家里挑战自我x5";
        group.DurationMs = 500;
        group.WaitMs = null;

        var compiled = new MacroActionCompiler().CompileGroup(configuration, flow, group);

        Assert.Equal(430, compiled.CountedActionDurationMs);
        Assert.Equal(70, compiled.WaitMilliseconds);
    }

    [Fact]
    public void CompileGroup_ReleaseSlot_UsesTwelveSkillAndSixItemMaps()
    {
        var configuration = CreateReleaseConfiguration();
        var farm = configuration.Farms["家里挑战自我x5"];
        var compiler = new MacroActionCompiler();

        farm.ReleaseType = LegacyValues.SkillSlotRelease;
        farm.ReleaseKey = "12";
        configuration.KeyMap.Skills[11] = "R";
        Assert.Equal("R", compiler.ResolveReleaseKey(configuration, farm));

        farm.ReleaseType = LegacyValues.ItemSlotRelease;
        farm.ReleaseKey = "6";
        configuration.KeyMap.Items[5] = "Space";
        Assert.Equal("Space", compiler.ResolveReleaseKey(configuration, farm));

        farm.ReleaseKey = "7";
        Assert.Equal(string.Empty, compiler.ResolveReleaseKey(configuration, farm));
    }

    [Fact]
    public void CompileGroup_UnmarkedNpc_ProducesWarningWithoutMouseInput()
    {
        var configuration = ConfigurationDefaults.Create();
        var flow = configuration.GetFlow(1);
        var group = flow.Groups[0];
        group.Enabled = true;
        group.FarmName = "家里挑战自我x5";

        var compiled = new MacroActionCompiler().CompileGroup(configuration, flow, group);

        Assert.Collection(
            compiled.Actions,
            action => Assert.IsType<StopBoundaryAction>(action),
            action => Assert.IsType<WarningAction>(action));
        Assert.DoesNotContain(compiled.Actions, action => action is MoveMouseAction or LeftClickAction);
    }

    [Fact]
    public void CompileGroup_ClientRatios_AreCarriedWithNpcAndReleaseMouseActions()
    {
        var configuration = CreateReleaseConfiguration();
        var npc = configuration.Npcs["家里挑战自我NPC"];
        npc.ClientXRatio = 0.25d;
        npc.ClientYRatio = 0.75d;
        var farm = configuration.Farms["家里挑战自我x5"];
        farm.TargetClientXRatio = 0.4d;
        farm.TargetClientYRatio = 0.6d;
        var flow = configuration.GetFlow(1);
        var group = flow.Groups[0];
        group.Enabled = true;
        group.FarmName = farm.Name;

        var moves = new MacroActionCompiler()
            .CompileGroup(configuration, flow, group)
            .Actions
            .OfType<MoveMouseAction>()
            .ToArray();

        Assert.Collection(
            moves,
            move => Assert.Equal((1131, 679, 0.25d, 0.75d), (move.X, move.Y, move.ClientXRatio, move.ClientYRatio)),
            move => Assert.Equal((942, 705, 0.4d, 0.6d), (move.X, move.Y, move.ClientXRatio, move.ClientYRatio)));
    }

    [Fact]
    public void CompileGroup_InvalidOrPartialClientRatioPair_FallsBackToLegacyPixels()
    {
        var configuration = CreateReleaseConfiguration();
        var npc = configuration.Npcs["家里挑战自我NPC"];
        npc.ClientXRatio = 0.5d;
        npc.ClientYRatio = null;
        var farm = configuration.Farms["家里挑战自我x5"];
        farm.TargetClientXRatio = double.PositiveInfinity;
        farm.TargetClientYRatio = 0.5d;
        var flow = configuration.GetFlow(1);
        var group = flow.Groups[0];
        group.Enabled = true;
        group.FarmName = farm.Name;

        var moves = new MacroActionCompiler()
            .CompileGroup(configuration, flow, group)
            .Actions
            .OfType<MoveMouseAction>()
            .ToArray();

        Assert.All(moves, move =>
        {
            Assert.Null(move.ClientXRatio);
            Assert.Null(move.ClientYRatio);
        });
    }

    internal static MacroConfiguration CreateReleaseConfiguration()
    {
        var configuration = ConfigurationDefaults.Create();
        var flow = configuration.GetFlow(1);
        flow.Enabled = true;
        flow.Name = "黄金流程";
        flow.KeyDelayMs = 5;
        flow.SkillKeyDelayMs = 15;
        flow.HeroSelectDelayMs = 10;
        flow.NpcClickDelayMs = 20;
        flow.ChatDelayMs = 1;
        flow.TeleportKeyDelayMs = 200;
        flow.MouseMoveDelayMs = 30;
        flow.ReleaseMouseMoveDelayMs = 110;

        var npc = configuration.Npcs["家里挑战自我NPC"];
        npc.X = 1131;
        npc.Y = 679;

        var farm = configuration.Farms["家里挑战自我x5"];
        farm.ActionKey = "Q";
        farm.ReleaseType = LegacyValues.SkillKeyRelease;
        farm.ReleaseKey = "Q";
        farm.TargetX = 942;
        farm.TargetY = 705;
        return configuration;
    }
}
