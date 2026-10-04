using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using Xunit;

namespace CamelGraph.Core.Tests;

public class SettingsCatalogTests
{
    [Fact]
    public void EverySettingIsWellFormed()
    {
        Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.All.Select(s => s.Id).Distinct().Count());
        foreach (var setting in SettingsCatalog.All)
        {
            Assert.Contains(setting.Section, SettingsCatalog.Sections);
            Assert.False(string.IsNullOrWhiteSpace(setting.Title));
            Assert.False(string.IsNullOrWhiteSpace(setting.Description));
            if (setting.Kind == SettingKind.Choice)
            {
                Assert.True(setting.Options.Count >= 2, setting.Id);
                Assert.NotNull(setting.OptionFor(setting.DefaultChoice));
                Assert.Equal(setting.Options.Count, setting.Options.Select(o => o.Label).Distinct().Count());
            }
            else
            {
                Assert.Empty(setting.Options);
            }
        }
    }

    [Fact]
    public void EverySectionWithoutACustomPageHasSettings()
    {
        // Shortcuts and Diagnostics are built from other sources; the rest list catalogue settings.
        foreach (var section in SettingsCatalog.Sections.Where(s => s != "Shortcuts" && s != "Diagnostics"))
        {
            Assert.NotEmpty(SettingsCatalog.InSection(section));
        }
    }

    [Fact]
    public void SearchMatchesTitleDescriptionAndSection()
    {
        Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.Search(string.Empty).Count);
        Assert.Contains(SettingsCatalog.Search("grid"), s => s.Id == "showGrid");
        Assert.Contains(SettingsCatalog.Search("wires curves"), s => s.Id == "straightWires");
        Assert.Equal(SettingsCatalog.InSection("Canvas").Count(), SettingsCatalog.Search("canvas").Count(s => s.Section == "Canvas"));
        Assert.Empty(SettingsCatalog.Search("zzzznotasetting"));
    }

    [Fact]
    public void FindReturnsTheDescriptorOrNull()
    {
        Assert.Equal("Node density", SettingsCatalog.Find("density")!.Title);
        Assert.Null(SettingsCatalog.Find("nope"));
    }
}

public class KeymapGlobalRuleTests
{
    [Fact]
    public void GlobalCommandsNeedCtrlAltOrAFunctionKeyBecauseTheyAlsoFireWhileTyping()
    {
        var keymap = new Keymap();
        Assert.Contains("typed into text boxes", keymap.Validate("graph.run", "K"));
        Assert.Contains("typed into text boxes", keymap.Validate("graph.run", "Shift+K"));
        Assert.Null(keymap.Validate("graph.run", "F9"));
        Assert.Null(keymap.Validate("graph.run", "Ctrl+J"));
        Assert.Null(keymap.Validate("graph.run", "Alt+K"));
    }

    [Fact]
    public void CanvasCommandsMayUseSingleKeys()
    {
        Assert.Null(new Keymap().Validate("node.mute", "K"));
    }
}

public class HelpContentKeymapTests
{
    [Fact]
    public void HelpShowsTheChordsInForceAndDropsUnboundCommands()
    {
        var overrides = new Dictionary<string, string> { ["node.mute"] = "Ctrl+Alt+M", ["node.collapse"] = string.Empty };
        var sections = HelpContent.Build(new Keymap(overrides));
        var lines = sections.SelectMany(s => s.Lines).ToList();

        Assert.Contains(lines, l => l.Action == CommandCatalog.Find("node.mute")!.Title && l.Keys == "Ctrl+Alt+M");
        Assert.DoesNotContain(lines, l => l.Action == CommandCatalog.Find("node.collapse")!.Title);
    }

    [Fact]
    public void HelpWithoutAKeymapUsesTheDefaults()
    {
        var lines = HelpContent.Build().SelectMany(s => s.Lines).ToList();
        Assert.Contains(lines, l => l.Keys == "Ctrl+Z");
        Assert.Contains(lines, l => l.Keys.Contains("Ctrl+Shift+Z"));
    }
}
