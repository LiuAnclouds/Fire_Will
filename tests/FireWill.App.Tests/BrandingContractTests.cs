using System.Globalization;
using System.Xml.Linq;

namespace FireWill.App.Tests;

public sealed class BrandingContractTests
{
    [Fact]
    public void Motto_RemainsProminentAndAnimated()
    {
        var document = XDocument.Load(FindRepositoryFile("src", "FireWill.App", "MainWindow.xaml"));
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var storyboard = presentation + "Storyboard";
        var animation = presentation + "DoubleAnimation";
        var motto = document
            .Descendants(presentation + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "木叶飞舞之处，火亦生生不息");

        var fontSize = double.Parse(
            Assert.IsType<string>(motto.Attribute("FontSize")?.Value),
            CultureInfo.InvariantCulture);
        Assert.True(fontSize >= 20d, $"Expected motto font size >= 20, actual: {fontSize}.");

        Assert.Contains(
            motto.Descendants(storyboard),
            element => (string?)element.Attribute("RepeatBehavior") == "Forever");
        var flowAnimation = Assert.Single(
            motto.Descendants(animation),
            element => element.Attributes()
                .Any(attribute => attribute.Name.LocalName == "Storyboard.TargetName" &&
                                  attribute.Value == "MottoFlowTransform"));
        Assert.NotEqual(flowAnimation.Attribute("From")?.Value, flowAnimation.Attribute("To")?.Value);
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
