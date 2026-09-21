using System.Net.Http;
using System.Runtime.InteropServices;
using EdelByte.MailSetup.Diagnostics;
using EdelByte.MailSetup.Update;

namespace EdelByte.MailSetup;

/// <summary>
/// Einstieg. Ohne Argumente die grafische Einrichtung. Mit --diagnose bzw.
/// --version die Konsolenmodi (read-only, keine Passwörter).
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--version", StringComparison.OrdinalIgnoreCase) || a == "-v"))
        {
            AttachConsole();
            Console.WriteLine($"EdelByte Mail Setup {UpdateChecker.CurrentVersion}");
            return 0;
        }

        if (args.Any(a => a.Equals("--diagnose", StringComparison.OrdinalIgnoreCase) || a == "-d"))
        {
            AttachConsole();
            return RunDiagnose().GetAwaiter().GetResult();
        }

        Log.Info($"Start (GUI) {UpdateChecker.CurrentVersion} auf {OutlookInfoLine()}");
        var app = new App();
        app.InitializeComponent();
        return app.Run(new Ui.MainWindow());
    }

    private static async Task<int> RunDiagnose()
    {
        Console.WriteLine("EdelByte Mail Setup – Diagnose");
        Console.WriteLine(new string('-', 48));
        try
        {
            Console.WriteLine(await DiagnoseReport.BuildAsync());
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Diagnose fehlgeschlagen: " + ex.Message);
            return 1;
        }
    }

    private static string OutlookInfoLine()
    {
        try { return Outlook.OutlookInfo.WindowsDescription(); } catch { return "Windows"; }
    }

    /// <summary>Als WinExe hat der Prozess keine Konsole – für die CLI-Modi eine anhängen.</summary>
    private static void AttachConsole()
    {
        if (!AttachConsole(-1)) AllocConsole();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();
}
