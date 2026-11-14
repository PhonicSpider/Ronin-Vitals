using System.Windows;
using System.Windows.Threading;
using RoninVitals.Modules;
using Xunit;

namespace RoninVitals.Tests;

/// <summary>Runs a real (never visible: off-screen, no taskbar, not activated) window on its own UI thread.</summary>
public class CloseGuardTests
{
    private sealed class Probe
    {
        public Window Window = null!;
        public int ClosedCount;
        public List<Exception> Unhandled = new();
        public bool IsClosed => ClosedCount > 0;
    }

    private sealed class ThrowingScreen : ICanLeave
    {
        public Task<bool> CanLeaveAsync() => throw new InvalidOperationException("broken screen");
    }

    private static void RunOnUiThread(Func<Window, Func<Task<bool>>> makeGuard, Func<Probe, Task> body)
    {
        var probe = new Probe();
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, e) =>
            {
                probe.Unhandled.Add(e.Exception);
                e.Handled = true;
            };

            try
            {
                probe.Window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                    Width = 100,
                    Height = 100
                };
                probe.Window.Closed += (_, _) => probe.ClosedCount++;
                _ = new CloseGuard(probe.Window, makeGuard(probe.Window));
                probe.Window.Show();

                dispatcher.InvokeAsync(async () =>
                {
                    try { await body(probe); }
                    catch (Exception ex) { failure = ex; }
                    finally { dispatcher.InvokeShutdown(); }
                });
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The window test did not finish.");

        if (failure is not null) throw new Xunit.Sdk.XunitException("Test body failed: " + failure);
        Assert.Empty(probe.Unhandled);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
    }

    [Fact]
    public void ACompletedYesClosesTheWindowWithoutAnException()
    {
        RunOnUiThread(_ => () => Task.FromResult(true), async p =>
        {
            p.Window.Close();
            await WaitUntil(() => p.IsClosed);
            Assert.Equal(1, p.ClosedCount);
        });
    }

    [Fact]
    public void ANoKeepsTheWindowOpen()
    {
        RunOnUiThread(_ => () => Task.FromResult(false), async p =>
        {
            p.Window.Close();
            await Task.Delay(200);
            Assert.False(p.IsClosed);
            Assert.True(p.Window.IsLoaded);
            p.Window.Dispatcher.InvokeShutdown();
        });
    }

    [Fact]
    public void ALaterYesClosesTheWindowOnceAnswered()
    {
        var answer = new TaskCompletionSource<bool>();
        RunOnUiThread(_ => () => answer.Task, async p =>
        {
            p.Window.Close();
            await Task.Delay(200);
            Assert.False(p.IsClosed);

            answer.SetResult(true);
            await WaitUntil(() => p.IsClosed);
            Assert.Equal(1, p.ClosedCount);
        });
    }

    [Fact]
    public void ASecondCloseWhileTheQuestionIsPendingLetsTheWindowClose()
    {
        var answer = new TaskCompletionSource<bool>();
        RunOnUiThread(_ => () => answer.Task, async p =>
        {
            p.Window.Close();
            await Task.Delay(100);
            Assert.False(p.IsClosed);

            p.Window.Close();
            await WaitUntil(() => p.IsClosed);
            Assert.Equal(1, p.ClosedCount);

            // The answer that arrives afterwards neither throws nor closes anything again.
            answer.SetResult(true);
            await Task.Delay(200);
            Assert.Equal(1, p.ClosedCount);
        });
    }

    [Fact]
    public void AGuardThatThrowsLetsTheWindowClose()
    {
        RunOnUiThread(_ => () => LeaveGuard.CanLeaveAsync(new ThrowingScreen()), async p =>
        {
            p.Window.Close();
            await WaitUntil(() => p.IsClosed);
            Assert.Equal(1, p.ClosedCount);
        });
    }

    [Fact]
    public void AQuestionThatThrowsLetsTheWindowClose()
    {
        RunOnUiThread(_ => () => throw new InvalidOperationException("broken question"), async p =>
        {
            p.Window.Close();
            await WaitUntil(() => p.IsClosed);
            Assert.Equal(1, p.ClosedCount);
        });
    }
}
