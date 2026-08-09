using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FireWill.Core.Configuration;

namespace FireWill.Tests;

public sealed class LegacyIniProfileSerializerTests
{
    [Fact]
    public void Parse_CurrentProfileFields_PreservesEmptyKeyPreCommand()
    {
        var configuration = LegacyIniProfileSerializer.Parse(CurrentProfileFixture);

        Assert.Equal("白悟空", configuration.General.CurrentProfileName);
        Assert.Equal(5, configuration.General.KeyDelayMs);
        Assert.Equal(15, configuration.General.SkillKeyDelayMs);
        Assert.Equal(10, configuration.General.HeroSelectDelayMs);
        Assert.Equal(20, configuration.General.NpcClickDelayMs);
        Assert.Equal("XButton2", configuration.GetFlow(1).Hotkey);

        var emptyPreCommand = configuration.GetFlow(1).Groups[1];
        Assert.True(emptyPreCommand.Enabled);
        Assert.Equal(LegacyValues.KeyPreCommand, emptyPreCommand.PreType);
        Assert.Equal(string.Empty, emptyPreCommand.PreValue);
        Assert.Equal("家里挑战自我x5", emptyPreCommand.FarmName);
        Assert.Equal(280, emptyPreCommand.WaitMs);
    }

    [Fact]
    public void Parse_LegacyFields_MigratesWithoutInventingWait()
    {
        const string legacy = """
            [General]
            commandDelayMs=7
            clickDelayMs=9
            stopHotkey=
            [NPC.家里挑战自我NPC]
            x1=10
            y1=20
            x2=15
            y2=25
            [Farm.家里挑战自我x20]
            actionKey=W
            releaseType=物品按键
            releaseKey=Tab
            targetX=300
            targetY=400
            [Flow.3]
            name=双鱼四镜像（预留）
            enabled=1
            hotkey=Ctrl + X
            commandDelay=11
            clickDelay=12
            [Flow.3.Group.1]
            enabled=1
            preType=无
            preValue=
            farm=无
            npc=家里挑战自我NPC
            npcAction=x20
            duration=321
            """;

        var configuration = LegacyIniProfileSerializer.Parse(legacy);

        Assert.Equal("Z", configuration.General.StopHotkey);
        Assert.Equal(7, configuration.General.KeyDelayMs);
        Assert.Equal(9, configuration.General.NpcClickDelayMs);
        Assert.Equal((13, 23), (configuration.Npcs["家里挑战自我NPC"].X, configuration.Npcs["家里挑战自我NPC"].Y));
        Assert.Null(configuration.Npcs["家里挑战自我NPC"].ClientXRatio);
        Assert.Null(configuration.Npcs["家里挑战自我NPC"].ClientYRatio);

        var migratedFarm = configuration.Farms["家里挑战自我x10"];
        Assert.Equal("W", migratedFarm.ActionKey);
        Assert.Equal(LegacyValues.ItemKeyRelease, migratedFarm.ReleaseType);
        Assert.Equal("Tab", migratedFarm.ReleaseKey);
        Assert.Equal(300, migratedFarm.TargetX);
        Assert.Null(migratedFarm.TargetClientXRatio);
        Assert.Null(migratedFarm.TargetClientYRatio);

        // In the five-flow layout, old Flow.3 becomes new Flow.4.
        var flow = configuration.GetFlow(4);
        Assert.Equal("双鱼四镜像", flow.Name);
        Assert.Equal("^X", flow.Hotkey);
        Assert.Equal(11, flow.KeyDelayMs);
        Assert.Equal(12, flow.NpcClickDelayMs);
        Assert.Equal("家里挑战自我x10", flow.Groups[0].FarmName);
        Assert.Null(flow.Groups[0].WaitMs);
        Assert.Equal(321, flow.Groups[0].DurationMs);
        Assert.Equal("自定义流程3", configuration.GetFlow(3).Name);
        Assert.Equal("自定义流程7", configuration.GetFlow(7).Name);
    }

    [Fact]
    public void Parse_ClientRatios_LoadsOnlyCompleteFinitePairsWithinUnitInterval()
    {
        const string profile = """
            [NPC.妙木山大蛤蟆]
            x=845
            y=390
            clientXRatio=0.25
            clientYRatio=0.75
            clientCaptureAspectRatio=1.7777777777777777
            [NPC.妙木山挑战自我NPC]
            clientXRatio=0.5
            [NPC.家里挑战自我NPC]
            clientXRatio=-0.01
            clientYRatio=0.5
            [NPC.家里追捕逃忍NPC]
            clientXRatio=0.5
            clientYRatio=1.01
            [NPC.尾兽处追捕逃忍NPC]
            clientXRatio=NaN
            clientYRatio=0.5
            [Farm.家里挑战自我x5]
            targetX=942
            targetY=705
            targetClientXRatio=0
            targetClientYRatio=1
            targetClientCaptureAspectRatio=1.3333333333333333
            [Farm.家里追捕逃忍]
            targetClientXRatio=0.4
            targetClientYRatio=Infinity
            """;

        var configuration = LegacyIniProfileSerializer.Parse(profile);

        var validNpc = configuration.Npcs["妙木山大蛤蟆"];
        Assert.Equal((0.25, 0.75), (validNpc.ClientXRatio, validNpc.ClientYRatio));
        Assert.Equal(1.7777777777777777d, validNpc.ClientCaptureAspectRatio);
        Assert.Equal((845, 390), (validNpc.X, validNpc.Y));
        Assert.Null(configuration.Npcs["妙木山挑战自我NPC"].ClientXRatio);
        Assert.Null(configuration.Npcs["妙木山挑战自我NPC"].ClientYRatio);
        Assert.Null(configuration.Npcs["家里挑战自我NPC"].ClientXRatio);
        Assert.Null(configuration.Npcs["家里挑战自我NPC"].ClientYRatio);
        Assert.Null(configuration.Npcs["家里追捕逃忍NPC"].ClientXRatio);
        Assert.Null(configuration.Npcs["家里追捕逃忍NPC"].ClientYRatio);
        Assert.Null(configuration.Npcs["尾兽处追捕逃忍NPC"].ClientXRatio);
        Assert.Null(configuration.Npcs["尾兽处追捕逃忍NPC"].ClientYRatio);

        var validFarm = configuration.Farms["家里挑战自我x5"];
        Assert.Equal((0d, 1d), (validFarm.TargetClientXRatio, validFarm.TargetClientYRatio));
        Assert.Equal(1.3333333333333333d, validFarm.TargetClientCaptureAspectRatio);
        Assert.Null(configuration.Farms["家里追捕逃忍"].TargetClientXRatio);
        Assert.Null(configuration.Farms["家里追捕逃忍"].TargetClientYRatio);
    }

    [Fact]
    public void Serialize_ClientRatios_UsesInvariantRoundTripFormatAndPreservesLegacyPixels()
    {
        var configuration = ConfigurationDefaults.Create();
        var npc = configuration.Npcs["妙木山大蛤蟆"];
        npc.ClientXRatio = 0.12345678901234568d;
        npc.ClientYRatio = 0.875d;
        npc.ClientCaptureAspectRatio = 1.7777777777777777d;
        var farm = configuration.Farms["家里挑战自我x5"];
        farm.TargetX = 942;
        farm.TargetY = 705;
        farm.TargetClientXRatio = 0d;
        farm.TargetClientYRatio = 1d;
        farm.TargetClientCaptureAspectRatio = 1.3333333333333333d;

        var originalCulture = CultureInfo.CurrentCulture;
        string serialized;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            serialized = LegacyIniProfileSerializer.Serialize(configuration);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        var roundTripped = LegacyIniProfileSerializer.Parse(serialized);

        Assert.Contains("clientXRatio=0.12345678901234568", serialized, StringComparison.Ordinal);
        Assert.Contains("clientYRatio=0.875", serialized, StringComparison.Ordinal);
        Assert.Contains("clientCaptureAspectRatio=1.7777777777777777", serialized, StringComparison.Ordinal);
        Assert.Contains("targetClientXRatio=0", serialized, StringComparison.Ordinal);
        Assert.Contains("targetClientYRatio=1", serialized, StringComparison.Ordinal);
        Assert.Contains("targetClientCaptureAspectRatio=1.3333333333333333", serialized, StringComparison.Ordinal);
        Assert.Equal((845, 390), (roundTripped.Npcs["妙木山大蛤蟆"].X, roundTripped.Npcs["妙木山大蛤蟆"].Y));
        Assert.Equal(
            (npc.ClientXRatio, npc.ClientYRatio),
            (roundTripped.Npcs["妙木山大蛤蟆"].ClientXRatio, roundTripped.Npcs["妙木山大蛤蟆"].ClientYRatio));
        Assert.Equal(
            npc.ClientCaptureAspectRatio,
            roundTripped.Npcs["妙木山大蛤蟆"].ClientCaptureAspectRatio);
        Assert.Equal((942, 705), (roundTripped.Farms["家里挑战自我x5"].TargetX, roundTripped.Farms["家里挑战自我x5"].TargetY));
        Assert.Equal(
            (farm.TargetClientXRatio, farm.TargetClientYRatio),
            (roundTripped.Farms["家里挑战自我x5"].TargetClientXRatio, roundTripped.Farms["家里挑战自我x5"].TargetClientYRatio));
        Assert.Equal(
            farm.TargetClientCaptureAspectRatio,
            roundTripped.Farms["家里挑战自我x5"].TargetClientCaptureAspectRatio);
    }

    [Fact]
    public void Serialize_ClientProjectionFp64Values_RoundTripsBitExactly()
    {
        var configuration = ConfigurationDefaults.Create();
        var npc = configuration.Npcs["妙木山大蛤蟆"];
        npc.ClientXRatio = Math.BitIncrement(0.5d);
        npc.ClientYRatio = Math.BitDecrement(1d);
        npc.ClientCaptureAspectRatio = Math.BitIncrement(16d / 9d);
        var farm = configuration.Farms["家里挑战自我x5"];
        farm.TargetClientXRatio = Math.BitDecrement(0.5d);
        farm.TargetClientYRatio = double.Epsilon;
        farm.TargetClientCaptureAspectRatio = Math.BitDecrement(4d / 3d);

        var roundTripped = LegacyIniProfileSerializer.Parse(
            LegacyIniProfileSerializer.Serialize(configuration));
        var roundTrippedNpc = roundTripped.Npcs[npc.Name];
        var roundTrippedFarm = roundTripped.Farms[farm.Name];

        AssertSameBits(npc.ClientXRatio, roundTrippedNpc.ClientXRatio);
        AssertSameBits(npc.ClientYRatio, roundTrippedNpc.ClientYRatio);
        AssertSameBits(npc.ClientCaptureAspectRatio, roundTrippedNpc.ClientCaptureAspectRatio);
        AssertSameBits(farm.TargetClientXRatio, roundTrippedFarm.TargetClientXRatio);
        AssertSameBits(farm.TargetClientYRatio, roundTrippedFarm.TargetClientYRatio);
        AssertSameBits(
            farm.TargetClientCaptureAspectRatio,
            roundTrippedFarm.TargetClientCaptureAspectRatio);
    }

    [Fact]
    public void Parse_V013RatiosWithoutCaptureAspect_PreservesMigrationSignal()
    {
        const string profile = """
            [NPC.家里挑战自我NPC]
            clientXRatio=0.4
            clientYRatio=0.6
            [Farm.家里挑战自我x5]
            targetClientXRatio=0.5
            targetClientYRatio=0.75
            """;

        var configuration = LegacyIniProfileSerializer.Parse(profile);
        var serialized = LegacyIniProfileSerializer.Serialize(configuration);

        Assert.Equal(0.4d, configuration.Npcs["家里挑战自我NPC"].ClientXRatio);
        Assert.Equal(0.6d, configuration.Npcs["家里挑战自我NPC"].ClientYRatio);
        Assert.Null(configuration.Npcs["家里挑战自我NPC"].ClientCaptureAspectRatio);
        Assert.Equal(0.5d, configuration.Farms["家里挑战自我x5"].TargetClientXRatio);
        Assert.Equal(0.75d, configuration.Farms["家里挑战自我x5"].TargetClientYRatio);
        Assert.Null(configuration.Farms["家里挑战自我x5"].TargetClientCaptureAspectRatio);
        Assert.Contains("clientXRatio=0.4", serialized, StringComparison.Ordinal);
        Assert.Contains("targetClientXRatio=0.5", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureAspectRatio=", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_InvalidOrPartialClientRatioPair_OmitsPair()
    {
        var configuration = ConfigurationDefaults.Create();
        var npc = configuration.Npcs["妙木山大蛤蟆"];
        npc.ClientXRatio = 0.5d;
        npc.ClientYRatio = null;
        npc.ClientCaptureAspectRatio = 16d / 9d;
        var farm = configuration.Farms["家里挑战自我x5"];
        farm.TargetClientXRatio = 2d;
        farm.TargetClientYRatio = 0.5d;
        farm.TargetClientCaptureAspectRatio = 4d / 3d;

        var serialized = LegacyIniProfileSerializer.Serialize(configuration);
        var roundTripped = LegacyIniProfileSerializer.Parse(serialized);

        Assert.DoesNotContain("clientXRatio=", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("clientYRatio=", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("clientCaptureAspectRatio=", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("targetClientXRatio=", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("targetClientYRatio=", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("targetClientCaptureAspectRatio=", serialized, StringComparison.Ordinal);
        Assert.Null(roundTripped.Npcs["妙木山大蛤蟆"].ClientXRatio);
        Assert.Null(roundTripped.Npcs["妙木山大蛤蟆"].ClientYRatio);
        Assert.Null(roundTripped.Npcs["妙木山大蛤蟆"].ClientCaptureAspectRatio);
        Assert.Null(roundTripped.Farms["家里挑战自我x5"].TargetClientXRatio);
        Assert.Null(roundTripped.Farms["家里挑战自我x5"].TargetClientYRatio);
        Assert.Null(roundTripped.Farms["家里挑战自我x5"].TargetClientCaptureAspectRatio);
    }

    [Fact]
    public void Save_WritesUtf8WithoutBom_AndRoundTripsCanonicalFields()
    {
        var configuration = LegacyIniProfileSerializer.Parse(CurrentProfileFixture);
        configuration.GetFlow(8).Name = "中文配置须佐斑";
        configuration.KeyMap.Skills[0] = "Q";
        var directory = Path.Combine(Path.GetTempPath(), $"FireWill.Tests.{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "测试.ini");

        try
        {
            LegacyIniProfileSerializer.Save(path, configuration);
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.Contains("中文配置须佐斑", new UTF8Encoding(false, true).GetString(bytes), StringComparison.Ordinal);

            var roundTripped = LegacyIniProfileSerializer.Load(path);
            Assert.Equal("中文配置须佐斑", roundTripped.GetFlow(8).Name);
            Assert.Equal("Q", roundTripped.KeyMap.Skills[0]);
            Assert.Equal("家里挑战自我x5", roundTripped.GetFlow(1).Groups[1].FarmName);
            Assert.Equal(string.Empty, roundTripped.GetFlow(1).Groups[1].PreValue);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void LegacyOracle_WhenPresent_LoadsEveryProfileWithoutMutation()
    {
        var legacyRoot = FindLegacyRoot();
        if (legacyRoot is null)
        {
            return;
        }

        var paths = new[] { Path.Combine(legacyRoot, "war3_macro_gui.ini") }
            .Concat(Directory.EnumerateFiles(Path.Combine(legacyRoot, "profiles"), "*.ini"))
            .ToArray();
        var hashesBefore = paths.ToDictionary(path => path, Sha256, StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            var configuration = LegacyIniProfileSerializer.Load(path);
            Assert.Equal(5, configuration.Npcs.Count);
            Assert.Equal(7, configuration.Farms.Count);
            Assert.Equal(8, configuration.Flows.Count);
            Assert.All(configuration.Flows, flow => Assert.Equal(8, flow.Groups.Count));
            Assert.Equal(12, configuration.KeyMap.Skills.Count);
            Assert.Equal(6, configuration.KeyMap.Items.Count);

            var compiler = new FireWill.Core.Execution.MacroActionCompiler();
            for (var slot = 1; slot <= LegacyCatalog.FlowCount; slot++)
            {
                var compiled = compiler.CompileFlow(configuration, slot);
                Assert.InRange(compiled.Groups.Count, 0, LegacyCatalog.GroupCount);
            }
        }

        Assert.Equal(hashesBefore, paths.ToDictionary(path => path, Sha256, StringComparer.OrdinalIgnoreCase));
    }

    private static string Sha256(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static void AssertSameBits(double? expected, double? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(expected.Value),
            BitConverter.DoubleToInt64Bits(actual.Value));
    }

    private static string? FindLegacyRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "war3_macro_gui.ini"))
                && Directory.Exists(Path.Combine(directory.FullName, "profiles")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private const string CurrentProfileFixture = """
        [General]
        stopHotkey=Z
        currentProfileName=白悟空
        currentProfilePath=profiles\白悟空.ini
        keyDelayMs=5
        skillKeyDelayMs=15
        heroSelectDelayMs=10
        npcClickDelayMs=20
        chatDelayMs=1
        teleportKeyDelayMs=200
        mouseMoveDelayMs=30
        releaseMouseMoveDelayMs=110
        [NPC.家里挑战自我NPC]
        x=1131
        y=679
        [Farm.家里挑战自我x5]
        actionKey=Q
        releaseKey=Q
        releaseType=技能按键
        targetX=942
        targetY=705
        [Flow.1]
        name=家里开鱼双镜像
        enabled=1
        hotkey=XButton2
        keyDelay=5
        skillKeyDelay=15
        heroSelectDelay=10
        npcClickDelay=20
        chatDelay=1
        teleportKeyDelay=200
        mouseMoveDelay=30
        releaseMouseMoveDelay=110
        [Flow.1.Group.1]
        enabled=0
        preType=无
        preValue=
        farm=无
        duration=0
        wait=0
        [Flow.1.Group.2]
        enabled=1
        preType=按键
        preValue=
        farm=家里挑战自我x5
        duration=510
        wait=280
        """;
}
