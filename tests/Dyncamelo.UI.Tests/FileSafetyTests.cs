using System;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>Dialogs that answer from a script and remember what they were asked.</summary>
internal sealed class ScriptedDialogs : IDialogService
{
    public SaveChoice SaveAnswer = SaveChoice.DontSave;
    public bool ConfirmAnswer = true;
    public string? SavePath;
    public string? PromptAnswer;
    public int SaveQuestions;
    public string? LastSaveQuestion;
    public int ConfirmQuestions;
    public string? LastConfirm;
    public string? LastError;

    public string? ShowOpenFile(string filter, string title) => null;

    public string? ShowSaveFile(string filter, string title, string defaultFileName) => SavePath;

    public bool Confirm(string message, string title)
    {
        ConfirmQuestions++;
        LastConfirm = message;
        return ConfirmAnswer;
    }

    public SaveChoice AskSaveChanges(string message, string title)
    {
        SaveQuestions++;
        LastSaveQuestion = message;
        return SaveAnswer;
    }

    public void ShowError(string message, string title) => LastError = message;

    public string? Prompt(string message, string title, string defaultValue) => PromptAnswer;

    public string? PickFolder(string title, string initialFolder) => null;
}

/// <summary>Unsaved-work safety: the modified marker, the questions before work is replaced, autosave and recovery.</summary>
public class FileSafetyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dyc-filesafety-" + Guid.NewGuid().ToString("N"));

    public FileSafetyTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
    }

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    private GraphEditorViewModel NewEditor(ScriptedDialogs dialogs)
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestSum", () => new SumNode());
        return new GraphEditorViewModel(registry, dialogs, new UiSettingsService(SettingsFile));
    }

    [Fact]
    public void ANewEditorIsNotModifiedAndTheFirstEditMarksTheTitle()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor(new ScriptedDialogs());
            Assert.False(vm.IsModified);
            Assert.Equal("Untitled", vm.Title);

            vm.Graph.AddNode(new SumNode());

            Assert.True(vm.IsModified);
            Assert.Equal("Untitled*", vm.Title);
        });
    }

    [Fact]
    public void SavingClearsTheMarkAndUndoingAfterwardsSetsItAgain()
    {
        StaHost.Run(() =>
        {
            var dialogs = new ScriptedDialogs { SavePath = Path.Combine(_folder, "plan.dyc") };
            var vm = NewEditor(dialogs);
            vm.Graph.AddNode(new SumNode());

            vm.SaveCommand.Execute(null);

            Assert.False(vm.IsModified);
            Assert.DoesNotContain("*", vm.Title);
            Assert.True(File.Exists(dialogs.SavePath));

            vm.UndoCommand.Execute(null);

            Assert.True(vm.IsModified);
            Assert.Contains("*", vm.Title);
        });
    }

    [Fact]
    public void ChangingTheRunModeCountsAsAChange()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor(new ScriptedDialogs());
            var before = vm.Graph.RunType;

            vm.Graph.RunType = before == RunType.Automatic ? RunType.Manual : RunType.Automatic;

            Assert.True(vm.IsModified);
        });
    }

    [Fact]
    public void NewAsksWhenThereAreUnsavedChangesAndCancelKeepsTheGraph()
    {
        StaHost.Run(() =>
        {
            var dialogs = new ScriptedDialogs { SaveAnswer = SaveChoice.Cancel };
            var vm = NewEditor(dialogs);
            vm.Graph.AddNode(new SumNode());

            vm.NewCommand.Execute(null);

            Assert.Equal(1, dialogs.SaveQuestions);
            Assert.Contains("Untitled", dialogs.LastSaveQuestion);
            Assert.Single(vm.Graph.Nodes);
            Assert.True(vm.IsModified);
        });
    }

    [Fact]
    public void NewWithDontSaveDiscardsTheGraphAndItIsThenClean()
    {
        StaHost.Run(() =>
        {
            var dialogs = new ScriptedDialogs { SaveAnswer = SaveChoice.DontSave };
            var vm = NewEditor(dialogs);
            vm.Graph.AddNode(new SumNode());

            vm.NewCommand.Execute(null);

            Assert.Empty(vm.Graph.Nodes);
            Assert.False(vm.IsModified);
        });
    }

    [Fact]
    public void NewWithSaveWritesTheFileFirst()
    {
        StaHost.Run(() =>
        {
            var dialogs = new ScriptedDialogs { SaveAnswer = SaveChoice.Save, SavePath = Path.Combine(_folder, "kept.dyc") };
            var vm = NewEditor(dialogs);
            vm.Graph.AddNode(new SumNode());

            vm.NewCommand.Execute(null);

            Assert.True(File.Exists(dialogs.SavePath));
            Assert.Empty(vm.Graph.Nodes);
        });
    }

    [Fact]
    public void NewKeepsTheGraphWhenTheSaveIsCancelled()
    {
        StaHost.Run(() =>
        {
            // "Save" but the Save As dialog is cancelled (no path): the work must not be thrown away.
            var dialogs = new ScriptedDialogs { SaveAnswer = SaveChoice.Save, SavePath = null };
            var vm = NewEditor(dialogs);
            vm.Graph.AddNode(new SumNode());

            vm.NewCommand.Execute(null);

            Assert.Single(vm.Graph.Nodes);
        });
    }

    [Fact]
    public void AnUnchangedGraphIsReplacedWithoutAskingEvenWhenItHasNodes()
    {
        StaHost.Run(() =>
        {
            var dialogs = new ScriptedDialogs();
            var vm = NewEditor(dialogs);
            var graph = new GraphModel { Name = "Loaded" };
            graph.AddNode(new SumNode());
            vm.LoadGraph(graph);
            Assert.False(vm.IsModified);

            vm.NewCommand.Execute(null);

            Assert.Equal(0, dialogs.SaveQuestions);
            Assert.Empty(vm.Graph.Nodes);
        });
    }

    [Fact]
    public void AutosaveWritesOnlyWhileThereAreUnsavedChangesAndSavingRemovesIt()
    {
        StaHost.Run(() =>
        {
            var dialogs = new ScriptedDialogs { SavePath = Path.Combine(_folder, "plan.dyc") };
            var vm = NewEditor(dialogs);
            var recovery = new UiSettingsService(SettingsFile).RecoveryDirectory;

            Assert.False(vm.AutosaveNow());

            vm.Graph.AddNode(new SumNode());
            Assert.True(vm.AutosaveNow());
            Assert.Single(Directory.GetFiles(recovery, "*.dyc"));
            Assert.False(vm.AutosaveNow());   // nothing new since

            vm.Graph.AddNode(new SumNode());
            Assert.True(vm.AutosaveNow());

            vm.SaveCommand.Execute(null);
            Assert.Empty(Directory.GetFiles(recovery, "*.dyc"));
        });
    }

    [Fact]
    public void AutosaveCanBeSwitchedOff()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor(new ScriptedDialogs());
            vm.IsAutosaveEnabled = false;
            vm.Graph.AddNode(new SumNode());

            Assert.False(vm.AutosaveNow());
            Assert.Equal(false, vm.ReadSetting("autosave"));
        });
    }

    [Fact]
    public void WorkLeftBehindByAClosedPaneIsOfferedAndRestoredByTheNextEditor()
    {
        StaHost.Run(() =>
        {
            var first = NewEditor(new ScriptedDialogs());
            first.Graph.Name = "Plan";
            first.Graph.AddNode(new SumNode());
            first.Graph.AddNode(new SumNode());
            first.EndSession();   // the pane closes with unsaved work

            var dialogs = new ScriptedDialogs { ConfirmAnswer = true };
            var second = NewEditor(dialogs);

            Assert.True(second.OfferRecovery());

            Assert.Equal(1, dialogs.ConfirmQuestions);
            Assert.Contains("Plan", dialogs.LastConfirm);
            Assert.Equal(2, second.Graph.Nodes.Count);
            Assert.True(second.IsModified);   // not on disk yet
            Assert.False(second.OfferRecovery());   // asked once per editor
        });
    }

    [Fact]
    public void DecliningTheRecoveryDeletesItSoItIsNotAskedAgain()
    {
        StaHost.Run(() =>
        {
            var first = NewEditor(new ScriptedDialogs());
            first.Graph.AddNode(new SumNode());
            first.EndSession();

            var declined = new ScriptedDialogs { ConfirmAnswer = false };
            var second = NewEditor(declined);
            Assert.False(second.OfferRecovery());
            Assert.Equal(1, declined.ConfirmQuestions);
            second.EndSession();

            var asked = new ScriptedDialogs();
            var third = NewEditor(asked);
            Assert.False(third.OfferRecovery());
            Assert.Equal(0, asked.ConfirmQuestions);
        });
    }

    [Fact]
    public void ClosingAPaneWithNothingUnsavedLeavesNothingToRecover()
    {
        StaHost.Run(() =>
        {
            var first = NewEditor(new ScriptedDialogs { SavePath = Path.Combine(_folder, "done.dyc") });
            first.Graph.AddNode(new SumNode());
            first.SaveCommand.Execute(null);
            first.EndSession();

            var asked = new ScriptedDialogs();
            var second = NewEditor(asked);

            Assert.False(second.OfferRecovery());
            Assert.Equal(0, asked.ConfirmQuestions);
        });
    }

    [Fact]
    public void RecoveryIsNotOfferedOverWorkAlreadyOnTheCanvas()
    {
        StaHost.Run(() =>
        {
            var first = NewEditor(new ScriptedDialogs());
            first.Graph.AddNode(new SumNode());
            first.EndSession();

            var asked = new ScriptedDialogs();
            var second = NewEditor(asked);
            second.Graph.AddNode(new SumNode());

            Assert.False(second.OfferRecovery());
            Assert.Equal(0, asked.ConfirmQuestions);
        });
    }

    [Fact]
    public void DroppingAFileThatIsNotAGraphSaysSoAndOpensNothing()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor(new ScriptedDialogs());

            Assert.False(vm.OpenDroppedFiles(new[] { Path.Combine(_folder, "notes.txt") }));
            Assert.Contains(".dyc", vm.StatusMessage);
        });
    }

    [Fact]
    public void DroppingAGraphOpensItAfterAskingAboutUnsavedChanges()
    {
        StaHost.Run(() =>
        {
            var path = Path.Combine(_folder, "other.dyc");
            var source = NewEditor(new ScriptedDialogs { SavePath = path });
            source.Graph.AddNode(new SumNode());
            source.Graph.AddNode(new SumNode());
            source.Graph.AddNode(new SumNode());
            source.SaveCommand.Execute(null);

            var dialogs = new ScriptedDialogs { SaveAnswer = SaveChoice.Cancel };
            var vm = NewEditor(dialogs);
            vm.Graph.AddNode(new SumNode());

            Assert.False(vm.OpenDroppedFiles(new[] { path }));
            Assert.Single(vm.Graph.Nodes);   // cancelled: the canvas is untouched

            dialogs.SaveAnswer = SaveChoice.DontSave;
            Assert.True(vm.OpenDroppedFiles(new[] { Path.Combine(_folder, "readme.md"), path }));
            Assert.Equal(3, vm.Graph.Nodes.Count);
            Assert.False(vm.IsModified);
            Assert.Equal(path, vm.CurrentFilePath);
        });
    }
}
