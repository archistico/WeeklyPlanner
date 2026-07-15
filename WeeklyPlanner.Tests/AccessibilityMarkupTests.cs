using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class AccessibilityMarkupTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void Icon_only_buttons_have_tooltips_and_automation_names()
    {
        foreach (var path in GetViewPaths())
        {
            var document = XDocument.Load(path);
            var iconButtons = document
                .Descendants(Avalonia + "Button")
                .Where(button => button.Attribute("Content") is null)
                .ToList();

            Assert.All(iconButtons, button =>
            {
                Assert.False(string.IsNullOrWhiteSpace(
                    (string?)button.Attribute("ToolTip.Tip")));
                Assert.False(string.IsNullOrWhiteSpace(
                    (string?)button.Attribute("AutomationProperties.Name")));
            });
        }
    }

    [Fact]
    public void Focusable_custom_controls_have_an_accessible_name()
    {
        var mainWindow = XDocument.Load(GetViewPath("MainWindow.axaml"));
        var focusableCustomControls = mainWindow
            .Descendants()
            .Where(element => string.Equals(
                (string?)element.Attribute("Focusable"),
                "True",
                StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(focusableCustomControls);
        Assert.All(focusableCustomControls, element =>
            Assert.False(string.IsNullOrWhiteSpace(
                (string?)element.Attribute("AutomationProperties.Name"))));

        var prioritySummary = focusableCustomControls.Single(element =>
            string.Equals(
                (string?)element.Attribute("Classes"),
                "prioritySummary",
                StringComparison.Ordinal));
        Assert.Equal("OnPrioritySummaryKeyDown", (string?)prioritySummary.Attribute("KeyDown"));
    }

    [Fact]
    public void Compact_icon_targets_keep_a_minimum_keyboard_and_pointer_size()
    {
        var controls = XDocument.Load(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "WeeklyPlanner.App",
            "Themes",
            "Controls.axaml")));

        AssertStyleSize(controls, "Button.icon", minimumWidth: 30, minimumHeight: 30);
        AssertStyleSize(controls, "Button.columnAdd.headerAdd", minimumWidth: 32, minimumHeight: 28);
    }

    [Fact]
    public void Active_ui_contains_no_weekday_column_labels()
    {
        string[] legacyLabels =
        [
            "Lunedì",
            "Martedì",
            "Mercoledì",
            "Giovedì",
            "Venerdì",
            "Sabato",
            "Domenica",
        ];

        foreach (var path in GetViewPaths())
        {
            var source = File.ReadAllText(path);
            Assert.All(legacyLabels, label =>
                Assert.DoesNotContain(label, source, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void AssertStyleSize(
        XDocument document,
        string selector,
        double minimumWidth,
        double minimumHeight)
    {
        var style = document
            .Descendants(Avalonia + "Style")
            .Single(element => string.Equals(
                (string?)element.Attribute("Selector"),
                selector,
                StringComparison.Ordinal));
        var setters = style
            .Elements(Avalonia + "Setter")
            .ToDictionary(
                element => (string?)element.Attribute("Property") ?? string.Empty,
                element => (string?)element.Attribute("Value") ?? string.Empty,
                StringComparer.Ordinal);

        Assert.True(ParseSize(setters["Width"]) >= minimumWidth);
        Assert.True(ParseSize(setters["Height"]) >= minimumHeight);
    }

    private static double ParseSize(string value) =>
        double.Parse(value, CultureInfo.InvariantCulture);

    private static IEnumerable<string> GetViewPaths() =>
        Directory.EnumerateFiles(
            Path.GetDirectoryName(GetViewPath("MainWindow.axaml"))!,
            "*.axaml",
            SearchOption.TopDirectoryOnly);

    private static string GetViewPath(string fileName) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "WeeklyPlanner.App",
        "Views",
        fileName));
}
