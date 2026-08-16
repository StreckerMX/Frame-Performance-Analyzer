using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Single shared STA host for every WPF window test in this assembly. One
/// Application (with the real theme resources) and every test window live on
/// one dedicated dispatcher thread — the arrangement of the real application —
/// so Application creation, resource lookup, and window show/close can never
/// race across parallel xUnit facts (WPF allows only one Application per
/// AppDomain and dispatcher objects are thread-affine).
/// </summary>
internal static class WpfStaTestHost
{
    private static readonly object Gate = new();
    private static readonly ManualResetEventSlim Ready = new(false);
    private static Dispatcher? _dispatcher;
    private static Exception? _startupFailure;

    /// <summary>Runs the action on the shared STA dispatcher thread.</summary>
    public static void Run(Action action)
    {
        lock (Gate)
        {
            EnsureStarted();
            Exception? failure = null;
            _dispatcher!.Invoke(() =>
            {
                try
                {
                    action();
                }
                catch (Exception error)
                {
                    failure = error;
                }
            });
            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    /// <summary>Creates the one test Application with the real theme resources.</summary>
    public static void EnsureApplication()
    {
        // Runs inline when already on the host dispatcher thread (inside a
        // Run action); otherwise hops to the host thread. Never re-enters
        // the Gate lock from the dispatcher thread, which would deadlock the
        // owning xUnit thread.
        if (_dispatcher is not null && Dispatcher.FromThread(Thread.CurrentThread) == _dispatcher)
        {
            CreateApplicationIfMissing();
            return;
        }

        Run(CreateApplicationIfMissing);
    }

    private static void CreateApplicationIfMissing()
    {
        if (Application.Current is not null)
        {
            return;
        }

        // OnExplicitShutdown: closing the last test window must never shut
        // down the Application (default OnLastWindowClose would end the
        // dispatcher loop and null Application.Current, poisoning every
        // subsequent test on the shared host thread).
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var name in new[] { "Colors.xaml", "Typography.xaml", "Buttons.xaml" })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/FrameViewAnalyzer.App;component/Themes/{name}", UriKind.Relative),
            });
        }
    }

    private static void EnsureStarted()
    {
        if (_dispatcher is not null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                _dispatcher = Dispatcher.CurrentDispatcher;
                Ready.Set();
                Dispatcher.Run();
            }
            catch (Exception error)
            {
                _startupFailure = error;
                Ready.Set();
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ready.Wait();
        if (_dispatcher is null)
        {
            throw _startupFailure ?? new InvalidOperationException("WPF STA test host failed to start.");
        }
    }
}
