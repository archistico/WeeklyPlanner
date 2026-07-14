using System.Text.RegularExpressions;
using WeeklyPlanner.App.Diagnostics;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed partial class ErrorReferenceGeneratorTests
{
    [Fact]
    public void References_are_short_readable_and_unique()
    {
        var generator = new ErrorReferenceGenerator();

        var first = generator.Create();
        var second = generator.Create();

        Assert.Matches(ReferenceRegex(), first);
        Assert.Matches(ReferenceRegex(), second);
        Assert.NotEqual(first, second);
    }

    [GeneratedRegex("^WP-[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();
}
