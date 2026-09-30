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
}
