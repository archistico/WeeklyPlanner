using System.Xml.Linq;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class CardInformationWindowLayoutTests
{
    [Fact]
    public void Card_information_window_keeps_bottom_scroll_space_after_the_last_element()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "WeeklyPlanner.App",
            "Views",
            "CardInformationWindow.axaml"));
        var document = XDocument.Load(sourcePath);
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollViewer = document
            .Descendants(avalonia + "ScrollViewer")
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute(x + "Name"),
                    "CardInformationScrollViewer",
                    StringComparison.Ordinal));

        Assert.Equal("Auto", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Top", (string?)scrollViewer.Attribute("VerticalContentAlignment"));
        Assert.Equal("Stretch", (string?)scrollViewer.Attribute("HorizontalContentAlignment"));

        var bottomSpacer = scrollViewer
            .Descendants(avalonia + "Border")
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute(x + "Name"),
                    "BottomScrollSpacer",
                    StringComparison.Ordinal));

        Assert.Equal("96", (string?)bottomSpacer.Attribute("Height"));
        Assert.Equal("False", (string?)bottomSpacer.Attribute("IsHitTestVisible"));
    }
}
