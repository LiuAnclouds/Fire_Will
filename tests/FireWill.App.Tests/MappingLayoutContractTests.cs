using System.Xml.Linq;
using FireWill.App.ViewModels;
using FireWill.Core.Configuration;

namespace FireWill.App.Tests;

public sealed class MappingLayoutContractTests
{
    [Fact]
    public void MappingPanels_MirrorWarcraftGridStructure()
    {
        var document = XDocument.Load(FindRepositoryFile("src", "FireWill.App", "MainWindow.xaml"));
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");

        var itemPanel = AssertGridColumns(document, presentation, "{Binding ItemMappings}", "2");
        var skillPanel = AssertGridColumns(document, presentation, "{Binding SkillMappingsForDisplay}", "4");
        Assert.Null(itemPanel.Attribute("Grid.Column"));
        Assert.Equal("2", skillPanel.Attribute("Grid.Column")?.Value);
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") == "技能按键");
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") == "装备按键");
        Assert.DoesNotContain(
            document.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value.Contains("映射", StringComparison.Ordinal) == true);

        var template = document
            .Descendants(presentation + "DataTemplate")
            .Single(element => element.Attributes().Any(
                attribute => attribute.Name.LocalName == "Key" && attribute.Value == "KeyMapTemplate"));
        var slot = Assert.Single(template.Elements(presentation + "Border"));
        Assert.Equal(slot.Attribute("Width")?.Value, slot.Attribute("Height")?.Value);
    }

    [Fact]
    public void SkillDisplayOrder_StartsAtTheBottomWithoutChangingConfigurationIndexes()
    {
        var state = new MainWindowState(ConfigurationDefaults.Create());

        Assert.Equal(
            ["技能 9", "技能 10", "技能 11", "技能 12",
             "技能 5", "技能 6", "技能 7", "技能 8",
             "技能 1", "技能 2", "技能 3", "技能 4"],
            state.SkillMappingsForDisplay.Select(mapping => mapping.Label));
        Assert.Same(state.SkillMappings[0], state.SkillMappingsForDisplay[8]);
    }

    [Fact]
    public void CaptureHints_UseSwitchWording()
    {
        var document = XDocument.Load(FindRepositoryFile("src", "FireWill.App", "MainWindow.xaml"));
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var visibleText = document
            .Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .ToArray();

        Assert.Contains("↑ 切换技能点 · F5 记录", visibleText);
        Assert.Contains("↓ 切换 NPC · F6 记录", visibleText);
        Assert.DoesNotContain(visibleText, value => value!.Contains("循环", StringComparison.Ordinal));
    }

    private static XElement AssertGridColumns(
        XDocument document,
        XNamespace presentation,
        string itemsSource,
        string expectedColumns)
    {
        var itemsControl = document
            .Descendants(presentation + "ItemsControl")
            .Single(element => (string?)element.Attribute("ItemsSource") == itemsSource);
        var grid = Assert.Single(itemsControl.Descendants(presentation + "UniformGrid"));
        Assert.Equal(expectedColumns, grid.Attribute("Columns")?.Value);
        return Assert.IsType<XElement>(itemsControl.Parent);
    }

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
