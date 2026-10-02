using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>
/// A release is made by pushing a new tag name to dist/RELEASE_VERSION. These tests keep the pieces it reads in step: the version in the
/// build properties, the changelog, the notes of a source-only release (dist/RELEASE_ASSETS = source) and the workflow's guards that
/// keep a source-only release from building an installer and keep the Autodesk App Store package for when it is asked for.
/// </summary>
public class ReleaseFilesTests
{
    private static string Root() => Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!;

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string ReleaseVersion() => Read("dist", "RELEASE_VERSION").Trim();

    [Fact]
    public void TheReleaseVersionIsATagNameThatMatchesTheBuildProperties()
    {
        var tag = ReleaseVersion();
        Assert.Matches("^v\\d+\\.\\d+\\.\\d+$", tag);

        var props = Read("Directory.Build.props");
        Assert.Contains("<Version>" + tag.Substring(1) + "</Version>", props);
    }

    [Fact]
    public void TheChangelogHasAnEntryForTheReleaseVersion()
    {
        Assert.Matches("(?m)^## " + Regex.Escape(ReleaseVersion().Substring(1)) + " - \\d{4}-\\d{2}-\\d{2}\\r?$", Read("CHANGELOG.md"));
    }

    [Fact]
    public void ASourceOnlyReleaseHasNotesThatNameItsVersionAndLicence()
    {
        var path = Path.Combine(Root(), "dist", "RELEASE_ASSETS");
        if (!File.Exists(path))
        {
            return;
        }

        var assets = File.ReadAllText(path).Trim().ToLowerInvariant();
        Assert.True(assets == "source" || assets == "all", "dist/RELEASE_ASSETS must say 'source' or 'all'");
        if (assets != "source")
        {
            return;
        }

        var notes = Read("dist", "RELEASE_NOTES.md");
        Assert.Contains(ReleaseVersion(), notes);
        Assert.Contains("source code", notes);
        Assert.Contains("PolyForm Noncommercial", notes);
    }

    [Fact]
    public void TheWorkflowSkipsTheBuildForASourceOnlyReleaseAndBuildsTheStorePackageOnlyOnRequest()
    {
        var workflow = Read(".github", "workflows", "release.yml");

        foreach (var step in new[]
        {
            "Build & stage per-year bundle (2024/2025/2026)",
            "Build graphical installer (DyncameloSetup.exe)",
            "Smoke-test the installer (install, upgrade over the top, uninstall)",
            "Package bundle zip",
            "Create GitHub release",
        })
        {
            Assert.Matches("- name: " + Regex.Escape(step) + "\\r?\\n\\s+if: [^\\r\\n]*SOURCE_ONLY != 'true'", workflow);
        }

        Assert.Matches("- name: Build the Autodesk App Store package\\r?\\n\\s+if: [^\\r\\n]*STORE == 'true'", workflow);
        Assert.Matches("- name: Keep the Autodesk App Store package\\r?\\n\\s+if: [^\\r\\n]*STORE == 'true'", workflow);
        Assert.Matches("- name: Create GitHub release \\(source code only\\)\\r?\\n\\s+if: [^\\r\\n]*SOURCE_ONLY == 'true'", workflow);
        Assert.Contains("[store]", workflow);
    }
}
