using System.Xml.Linq;

namespace FireWill.App.Tests;

public sealed class FarmLayoutContractTests
{
    [Fact]
    public void FarmEditor_IsSplitIntoTaskStartAndSkillReleaseColumns()
    {
        var document = XDocument.Load(FindRepositoryFile("src", "FireWill.App", "MainWindow.xaml"));
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var template = document
            .Descendants(presentation + "DataTemplate")
            .Single(element => element.Attributes().Any(
                attribute => attribute.Name.LocalName == "Key" && attribute.Value == "FarmRowTemplate"));
        var row = Assert.Single(template.Elements(presentation + "Grid"));
        var rowColumns = Assert.IsType<XElement>(row.Element(presentation + "Grid.ColumnDefinitions"))
            .Elements(presentation + "ColumnDefinition")
            .ToArray();
        Assert.Equal(["644", "32", "644"], rowColumns.Select(column => column.Attribute("Width")?.Value));

        var taskFields = row
            .Descendants(presentation + "Grid")
            .Single(element => (string?)element.Attribute(xaml + "Name") == "TaskStartFields");
        var skillFields = row
            .Descendants(presentation + "Grid")
            .Single(element => (string?)element.Attribute(xaml + "Name") == "SkillReleaseFields");
        Assert.Null(taskFields.Attribute("Grid.Column"));
        Assert.Equal("2", skillFields.Attribute("Grid.Column")?.Value);

        Assert.Contains(taskFields.Descendants(), element => HasBinding(element, "Name"));
        Assert.Contains(taskFields.Descendants(), element => HasBinding(element, "NpcAction"));
        Assert.Contains(taskFields.Descendants(), element => HasBinding(element, "ActionKey"));
        Assert.Contains(skillFields.Descendants(), element => HasBinding(element, "ReleaseType"));
        Assert.Contains(skillFields.Descendants(), element => HasBinding(element, "ReleaseKey"));
        Assert.Contains(
            skillFields.Descendants(presentation + "Button"),
            element => (string?)element.Attribute("Content") == "记录技能点");

        var headings = document
            .Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .ToArray();
        Assert.Contains("任务启动", headings);
        Assert.Contains("技能释放", headings);
        Assert.DoesNotContain(headings, value => value!.Contains("鼠标点", StringComparison.Ordinal));
    }

    private static bool HasBinding(XElement element, string propertyName) =>
        element.Attributes().Any(
            attribute => attribute.Name.LocalName is "Text" or "SelectedItem" &&
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
