using System.Diagnostics;
using System.Runtime.InteropServices;
using EdelByte.MailSetup.Diagnostics;

namespace EdelByte.MailSetup.Outlook;

/// <summary>Starten, freundlich beenden, warten – ohne den Prozess hart zu töten, solange es geht.</summary>
public static class OutlookProcess
{
    public static bool IsRunning => Process.GetProcessesByName("OUTLOOK").Length > 0;

    public static Process Start(string exePath, string arguments)
    {
        Log.Info($"Starte Outlook: {arguments}");
        var p = Process.Start(new ProcessStartInfo(exePath, arguments) { UseShellExecute = true })
                ?? throw new InvalidOperationException("Outlook konnte nicht gestartet werden");
        return p;
    }

    /// <summary>
    /// Erst per COM (Application.Quit), dann per WM_CLOSE ans Hauptfenster.
    /// Ein harter Kill bleibt bewusst aus – Outlook könnte gerade schreiben.
    /// </summary>
    public static async Task<bool> QuitGracefullyAsync(TimeSpan timeout)
    {
        if (!IsRunning) return true;
        Log.Info("Beende Outlook für die Einrichtung");
        try { OutlookCom.TryQuit(); } catch (Exception ex) { Log.Warn($"COM-Quit nicht möglich: {ex.Message}"); }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning) return true;
            await Task.Delay(500);
            if (DateTime.UtcNow > deadline - TimeSpan.FromSeconds(timeout.TotalSeconds / 2))
                foreach (var p in Process.GetProcessesByName("OUTLOOK"))
                    try { if (p.MainWindowHandle != IntPtr.Zero) PostMessage(p.MainWindowHandle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero); } catch { }
        }
        return !IsRunning;
    }

    public static async Task<bool> WaitUntilRunningAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsRunning) return true;
            await Task.Delay(300);
        }
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
