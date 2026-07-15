using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class PackagingReleaseTests
{
    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        ".."));

    [Fact]
    public void Central_version_and_milestone_identify_M4()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        var propertyGroup = Assert.Single(document.Root!.Elements("PropertyGroup"));

        Assert.Equal("0.22.0", propertyGroup.Element("Version")?.Value);
        Assert.Equal("0.22.0-m4", propertyGroup.Element("InformationalVersion")?.Value);
        Assert.Equal("M4", propertyGroup.Element("WeeklyPlannerMilestone")?.Value);
    }

    [Fact]
    public void Verification_script_stops_on_restore_build_or_test_failure()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "verify.ps1"));

        Assert.Contains("dotnet restore", source, StringComparison.Ordinal);
        Assert.Contains("dotnet build", source, StringComparison.Ordinal);
        Assert.Contains("dotnet test", source, StringComparison.Ordinal);
        Assert.True(
            source.Split("$LASTEXITCODE -ne 0", StringSplitOptions.None).Length >= 5,
            "Ogni comando dotnet deve essere seguito da un controllo esplicito dell'exit code.");
    }

    [Fact]
    public void Publish_script_builds_portable_and_self_contained_archives_without_trimming()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "publish.ps1"));

        Assert.Contains("'Portable', 'SelfContained', 'All'", source, StringComparison.Ordinal);
        Assert.Contains("Name = 'portable'", source, StringComparison.Ordinal);
        Assert.Contains("Name = 'self-contained'", source, StringComparison.Ordinal);
        Assert.Contains("SelfContained = $false", source, StringComparison.Ordinal);
        Assert.Contains("SelfContained = $true", source, StringComparison.Ordinal);
        Assert.Contains("-p:PublishSingleFile=false", source, StringComparison.Ordinal);
        Assert.Contains("-p:PublishTrimmed=false", source, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", source, StringComparison.Ordinal);
        Assert.Contains("package-info.json", source, StringComparison.Ordinal);
        Assert.Contains("RELEASE-CHECKLIST-M4.md", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_verifier_rejects_user_data_and_distinguishes_runtime_modes()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "verify-package.ps1"));

        Assert.Contains("settings.json", source, StringComparison.Ordinal);
        Assert.Contains("*.db-wal", source, StringComparison.Ordinal);
        Assert.Contains("*.db-shm", source, StringComparison.Ordinal);
        Assert.Contains("*.log", source, StringComparison.Ordinal);
        Assert.Contains("*.pdb", source, StringComparison.Ordinal);
        Assert.Contains("e_sqlite3.dll", source, StringComparison.Ordinal);
        Assert.Contains("coreclr.dll", source, StringComparison.Ordinal);
        Assert.Contains("SelfContained", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Distribution_manifest_and_release_documentation_are_present()
    {
        var requiredFiles = new[]
        {
            Path.Combine("packaging", "README-DISTRIBUZIONE.txt"),
            Path.Combine("docs", "BACKUP-RIPRISTINO.md"),
            Path.Combine("docs", "SMOKE-TEST-M4.md"),
            Path.Combine("docs", "RELEASE-CHECKLIST-M4.md"),
            Path.Combine("docs", "RELEASE-NOTES-M4.md"),
            Path.Combine("docs", "ADR-0020-packaging-mvp-locale.md"),
        };

        foreach (var relativePath in requiredFiles)
        {
            Assert.True(
                File.Exists(Path.Combine(RepositoryRoot, relativePath)),
                $"File di packaging assente: {relativePath}");
        }

        var distributionReadme = File.ReadAllText(
            Path.Combine(RepositoryRoot, "packaging", "README-DISTRIBUZIONE.txt"));
        Assert.Contains("richiede il runtime .NET 10 x64", distributionReadme, StringComparison.Ordinal);
        Assert.Contains("include il runtime .NET necessario", distributionReadme, StringComparison.Ordinal);
        Assert.Contains("non contiene database", distributionReadme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Continuous_integration_uses_supported_action_majors()
    {
        var workflow = File.ReadAllText(
            Path.Combine(RepositoryRoot, ".github", "workflows", "ci.yml"));

        Assert.Contains("actions/checkout@v6", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@v5", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("@v7", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_workflow_invokes_release_script_and_uploads_checksums()
    {
        var workflow = File.ReadAllText(
            Path.Combine(RepositoryRoot, ".github", "workflows", "package.yml"));

        Assert.Contains("actions/checkout@v6", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@v5", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts\\release.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/release/*.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/release/SHA256SUMS.txt", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_info_contract_is_valid_json_when_materialized()
    {
        const string sample = """
            {
              "Product": "WeeklyPlanner",
              "Version": "0.22.0-m4",
              "Milestone": "M4",
              "RuntimeIdentifier": "win-x64",
              "PackageMode": "self-contained",
              "SelfContained": true,
              "EntryPoint": "WeeklyPlanner.App.exe"
            }
            """;

        using var document = JsonDocument.Parse(sample);
        var root = document.RootElement;

        Assert.Equal("WeeklyPlanner", root.GetProperty("Product").GetString());
        Assert.True(root.GetProperty("SelfContained").GetBoolean());
        Assert.Equal("WeeklyPlanner.App.exe", root.GetProperty("EntryPoint").GetString());
    }
}
