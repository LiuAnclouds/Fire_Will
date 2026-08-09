using System.Xml.Linq;
using FireWill.App.ViewModels;
using FireWill.Core.Configuration;

namespace FireWill.App.Tests;

public sealed class FarmLayoutContractTests
{
    [Fact]
    public void FarmState_ExposesSevenTasksAndNineTypedReleaseProfiles()
    {
        var state = new MainWindowState(ConfigurationDefaults.Create());

        Assert.Equal(7, state.Farms.Count);
        Assert.Equal(
            ["Q技能", "W技能", "E技能", "R技能", "D技能", "F技能", "B技能"],
            state.SkillReleaseProfiles.Select(profile => profile.Name));
        Assert.Equal(["装备1", "装备2"], state.ItemReleaseProfiles.Select(profile => profile.Name));
        Assert.All(state.Farms, farm => Assert.Equal(13, farm.ActionKeyOptions.Count));
        Assert.Equal(
            ["无", .. Enumerable.Range(1, 12).Select(index => $"技能按键{index}")],
            state.Farms[0].ActionKeyOptions.Select(option => option.DisplayName));
        Assert.All(
            state.Farms.SelectMany(farm => farm.ActionKeyOptions.Skip(1)),
            option => Assert.StartsWith("skill:", option.Reference, StringComparison.Ordinal));
        Assert.All(state.SkillReleaseProfiles, profile => Assert.Equal(13, profile.KeyOptions.Count));
        Assert.All(state.ItemReleaseProfiles, profile => Assert.Equal(7, profile.KeyOptions.Count));
        Assert.Equal(
            ["无", "Q技能", "W技能", "E技能", "R技能", "D技能", "F技能", "B技能", "装备1", "装备2"],
            state.ReleaseProfileOptions);
    }

    [Fact]
    public void FarmEditor_UsesIndependentTaskAndNineReleaseSections()
    {
        var document = XDocument.Load(FindRepositoryFile("src", "FireWill.App", "MainWindow.xaml"));
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");

        var taskTemplate = FindTemplate(document, presentation, "FarmTaskRowTemplate");
        var taskCombo = Assert.Single(taskTemplate.Descendants(presentation + "ComboBox"));
        Assert.True(HasBinding(taskCombo, "ActionReference"));
        Assert.Equal("DisplayName", taskCombo.Attribute("DisplayMemberPath")?.Value);
        Assert.Equal("Reference", taskCombo.Attribute("SelectedValuePath")?.Value);
        Assert.DoesNotContain("装备", (string?)taskCombo.Attribute("ToolTip"), StringComparison.Ordinal);

        var releaseTemplate = FindTemplate(document, presentation, "ReleaseProfileRowTemplate");
        var releaseCombo = Assert.Single(releaseTemplate.Descendants(presentation + "ComboBox"));
        Assert.True(HasBinding(releaseCombo, "KeyReference"));
        Assert.Equal("DisplayName", releaseCombo.Attribute("DisplayMemberPath")?.Value);
        Assert.Equal("Reference", releaseCombo.Attribute("SelectedValuePath")?.Value);

        var skillRows = document.Descendants(presentation + "ItemsControl")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding SkillReleaseProfiles}");
        var itemRows = document.Descendants(presentation + "ItemsControl")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding ItemReleaseProfiles}");
        Assert.Equal("{StaticResource ReleaseProfileRowTemplate}", skillRows.Attribute("ItemTemplate")?.Value);
        Assert.Equal("{StaticResource ReleaseProfileRowTemplate}", itemRows.Attribute("ItemTemplate")?.Value);

        var headings = document
            .Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .ToArray();
        Assert.Contains("任务启动", headings);
        Assert.Contains("技能释放", headings);
        Assert.Contains("刷本任务", headings);
        Assert.Contains("启动技能按键", headings);
        Assert.Contains("组合名称", headings);
        Assert.Contains("映射按键", headings);
        Assert.DoesNotContain(headings, value => value!.Contains("槽位", StringComparison.Ordinal));
        Assert.DoesNotContain(headings, value => value!.Contains("释放方式", StringComparison.Ordinal));
        Assert.DoesNotContain(headings, value => value!.Contains("按键类型", StringComparison.Ordinal));
        Assert.DoesNotContain(headings, value => value!.Contains("技能点", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Descendants(presentation + "Button"), element =>
            (string?)element.Attribute("Content") == "记录技能点");
        Assert.DoesNotContain(document.ToString(), "FarmCaptureTarget", StringComparison.Ordinal);
        Assert.DoesNotContain(document.ToString(), "F5", StringComparison.Ordinal);

        var flowTemplate = FindTemplate(document, presentation, "FlowGroupTemplate");
        var flowCombos = flowTemplate.Descendants(presentation + "ComboBox").ToArray();
        Assert.Contains(flowCombos, combo => HasBinding(combo, "FarmName"));
        Assert.Contains(flowCombos, combo => HasBinding(combo, "ReleaseProfileName"));

        var state = new MainWindowState(ConfigurationDefaults.Create());
        Assert.DoesNotContain(
            state.Farms[0].ActionKeyOptions,
            option => option.Reference.StartsWith("item:", StringComparison.OrdinalIgnoreCase));
        var group = state.Flows[0].Groups[0];
        group.FarmName = state.FarmOptions[1];
        group.ReleaseProfileName = "Q技能";
        Assert.Equal(state.FarmOptions[1], group.FarmName);
        Assert.Equal("Q技能", group.ReleaseProfileName);
        group.FarmName = LegacyValues.None;
        Assert.Equal("Q技能", group.ReleaseProfileName);
        group.ReleaseProfileName = LegacyValues.None;
        Assert.Equal(LegacyValues.None, group.ReleaseProfileName);
    }

    [Fact]
    public void FarmStartupDropdown_RejectsItemReferencesFromLegacyConfiguration()
    {
        var model = new FarmSettings
        {
            Name = "任务",
            NpcName = "NPC",
            NpcAction = "x5",
            ActionKey = "item:1",
        };
        var viewModel = new FarmRowViewModel(model);

        Assert.DoesNotContain(
            viewModel.ActionKeyOptions,
            option => option.Reference.StartsWith("item:", StringComparison.OrdinalIgnoreCase));

        viewModel.ActionReference = "item:2";

        Assert.Equal(string.Empty, model.ActionKey);
    }

    private static XElement FindTemplate(XDocument document, XNamespace presentation, string key) =>
        document.Descendants(presentation + "DataTemplate")
            .Single(element => element.Attributes().Any(
                attribute => attribute.Name.LocalName == "Key" && attribute.Value == key));

    private static bool HasBinding(XElement element, string propertyName) =>
        element.Attributes().Any(attribute =>
            attribute.Name.LocalName is ("Text" or "SelectedItem" or "SelectedValue") &&
            attribute.Value.StartsWith("{Binding " + propertyName, StringComparison.Ordinal));

    private static string FindRepositoryFile(params string[] path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FireWill.slnx")))
            {
                return Path.Combine([directory.FullName, .. path]);
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Fire Will repository root.");
    }
}
