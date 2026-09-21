using System.Diagnostics;
using Lychee.Core;

namespace Lychee.Platform;

/// <summary>
/// Posts user notifications via AppleScript's `display notification`.
/// Works from a bare executable with no signing, notarization, or bundle
/// requirements. Windows builds show toasts in-app instead.
///
/// Fully fire-and-forget: never blocks the calling (UI) thread. At most one
/// osascript is in flight at a time; a still-running one causes the next
/// notification to be skipped instead of accumulating processes. A hung
/// osascript is killed after 3 seconds.
/// </summary>
public static class MacNotifier
{
    private static int _inFlight;

    public static void Notify(string title, string message)
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            return; // a notification is still being posted; skip rather than queue up

        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(
                    $"display notification \"{Escape(message)}\" with title \"{Escape(title)}\"");

                using var process = Process.Start(psi);
                if (process != null && !process.WaitForExit(3000))
                {
                    try { process.Kill(); } catch { }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("notify", ex);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        });
    }

    private static string Escape(string s) =>
        (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}
