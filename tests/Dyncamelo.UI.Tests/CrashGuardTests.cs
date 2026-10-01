using System;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;
using Xunit;

namespace Dyncamelo.UI.Tests;

public class CrashGuardTests
{
    [Fact]
    public void AFailingCommandIsReportedInsteadOfEscaping()
    {
        StaHost.Run(() =>
        {
            Exception? seen = null;
            var previous = CommandGuard.Handler;
            CommandGuard.Handler = ex => seen = ex;
            try
            {
                var command = new RelayCommand(() => throw new InvalidOperationException("boom"));
                command.Execute(null);
                Assert.IsType<InvalidOperationException>(seen);

                seen = null;
                var typed = new RelayCommand<string>(_ => throw new InvalidOperationException("typed boom"));
                typed.Execute("x");
                Assert.Equal("typed boom", seen!.Message);
            }
            finally
            {
                CommandGuard.Handler = previous;
            }
        });
    }

    [Fact]
    public void WithoutAHandlerExceptionsPropagateUnchanged()
    {
        StaHost.Run(() =>
        {
            var previous = CommandGuard.Handler;
            CommandGuard.Handler = null;
            try
            {
                var command = new RelayCommand(() => throw new InvalidOperationException("visible"));
                Assert.Throws<InvalidOperationException>(() => command.Execute(null));
            }
            finally
            {
                CommandGuard.Handler = previous;
            }
        });
    }

    [Fact]
    public void FatalExceptionsAreNeverSwallowed()
    {
        Assert.False(CommandGuard.IsRecoverable(new OutOfMemoryException()));
        Assert.True(CommandGuard.IsRecoverable(new InvalidOperationException()));
    }

    [Fact]
    public void ATemplateOfOursThatFailsToLoadIsRecognisedByTheFileItWasReading()
    {
        // "pack://" is only a known URI scheme once WPF has started (the shared test thread creates the Application); on a test
        // that runs first, new Uri("pack://application:,,,/...") reads ":,,," as a port and throws.
        StaHost.Run(() => { });
        Assert.True(CrashGuard.IsDyncameloFile(new Uri("pack://application:,,,/Dyncamelo.UI;component/themes/dyncamelodark.xaml")));
        Assert.False(CrashGuard.IsDyncameloFile(new Uri("pack://application:,,,/Autodesk.Navisworks.Gui;component/ribbon.xaml")));
        Assert.False(CrashGuard.IsDyncameloFile(null));
    }

    [Fact]
    public void ExceptionsThrownInDyncameloCodeAreRecognised()
    {
        Exception caught;
        try
        {
            new RelayCommand<string>(_ => { }).Execute(null);
            throw new InvalidOperationException("from a Dyncamelo assembly");
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        Assert.True(CrashGuard.IsFromDyncamelo(caught));
    }

    [Fact]
    public void AFailureToBuildSomethingFromXamlOrAResourceIsRecognised()
    {
        Assert.True(CrashGuard.IsWpfLoadFailure(new System.Windows.Markup.XamlParseException("template")));
        Assert.True(CrashGuard.IsWpfLoadFailure(new InvalidOperationException("outer", new System.Windows.Markup.XamlParseException("inner"))));
        Assert.True(CrashGuard.IsWpfLoadFailure(new System.IO.FileNotFoundException("Could not load file or assembly")));
        Assert.True(CrashGuard.IsWpfLoadFailure(new TypeInitializationException("Some.Type", new InvalidOperationException())));
        Assert.False(CrashGuard.IsWpfLoadFailure(new InvalidOperationException("an ordinary failure")));
        Assert.False(CrashGuard.IsWpfLoadFailure(new ArgumentException("another")));
    }

    [Fact]
    public void AWpfLoadFailureSoonAfterTheEditorDidSomethingIsTheEditors()
    {
        // A template that cannot be built is reported with WPF frames only and, for a control of Nodify's, a file that is not ours.
        CrashGuard.NoteActivity();
        var failure = new System.Windows.Markup.XamlParseException("Cannot create an instance of 'Something'");
        Assert.True(CrashGuard.IsOurFailure(failure));

        // Without a WPF load failure the activity alone proves nothing.
        Assert.False(CrashGuard.IsOurFailure(new InvalidOperationException("not WPF loading and not our code")));
    }
}
