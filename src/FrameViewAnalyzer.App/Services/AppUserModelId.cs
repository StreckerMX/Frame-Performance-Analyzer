using System.Runtime.InteropServices;

namespace FrameViewAnalyzer.App.Services;

/// <summary>
/// Windows AppUserModelID integration (parity gap G9). One stable, reverse
/// domain-style identifier — deterministic, machine-independent, no personal
/// information, no paths. Applying it early lets Windows group taskbar
/// windows and notifications under the same application identity.
/// </summary>
public static class AppUserModelId
{
    public const string Value = "StreckerMX.FrameViewAnalyzer";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    /// <summary>Applies the stable AppUserModelID to the current process.</summary>
    public static void ApplyToCurrentProcess()
    {
        _ = SetCurrentProcessExplicitAppUserModelID(Value);
    }
}
