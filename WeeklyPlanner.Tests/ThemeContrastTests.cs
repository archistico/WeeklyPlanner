using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class ThemeContrastTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Colors.Light.axaml")]
    [InlineData("Colors.Dark.axaml")]
    public void Theme_text_and_action_pairs_meet_the_normal_text_contrast_budget(string fileName)
    {
        var colors = ReadTheme(fileName);

        AssertContrast(colors, "TextPrimaryBrush", "WindowBackgroundBrush", 4.5);
        AssertContrast(colors, "TextSecondaryBrush", "WindowBackgroundBrush", 4.5);
        AssertContrast(colors, "TextPrimaryBrush", "SurfaceBackgroundBrush", 4.5);
        AssertContrast(colors, "AccentForegroundBrush", "AccentBrush", 4.5);
        AssertContrast(colors, "DangerForegroundBrush", "DangerBrush", 4.5);
    }

    private static Dictionary<string, string> ReadTheme(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "WeeklyPlanner.App",
            "Themes",
            fileName));
        var document = XDocument.Load(path);

        return document
            .Descendants(Avalonia + "SolidColorBrush")
            .ToDictionary(
                element => (string?)element.Attribute(X + "Key")
                    ?? throw new InvalidOperationException("Risorsa tema senza chiave."),
                element => (string?)element.Attribute("Color")
                    ?? throw new InvalidOperationException("Risorsa tema senza colore."),
                StringComparer.Ordinal);
    }

    private static void AssertContrast(
        IReadOnlyDictionary<string, string> colors,
        string foregroundKey,
        string backgroundKey,
        double minimumRatio)
    {
        var ratio = ContrastRatio(colors[foregroundKey], colors[backgroundKey]);
        Assert.True(
            ratio >= minimumRatio,
            $"{foregroundKey} su {backgroundKey} ha contrasto {ratio:F2}:1, inferiore a {minimumRatio:F1}:1.");
    }

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
        {
            throw new FormatException($"Colore non supportato: {hex}");
        }

        var red = ParseChannel(hex.AsSpan(1, 2));
        var green = ParseChannel(hex.AsSpan(3, 2));
        var blue = ParseChannel(hex.AsSpan(5, 2));
        return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
    }

    private static double ParseChannel(ReadOnlySpan<char> value)
    {
        var normalized = int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        return normalized <= 0.04045
            ? normalized / 12.92
            : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }
}
