using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EdelByte.MailSetup.Diagnostics;

/// <summary>
/// Protokoll unter %LOCALAPPDATA%\EdelByte\MailSetup\logs\setup-JJJJ-MM-TT.log.
/// Erlaubt: Version, Windows, Outlook, Schritt, Ergebnis, Fehlercode.
/// Nie: Passwörter, Tokens, Authorization-Header, Mailinhalte.
/// E-Mail-Adressen werden vor dem Schreiben maskiert (e***@firma.ch).
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly Regex Email = new(@"(?<![\w.+-])([\w.+-])[\w.+-]*@([\w.-]+\.[a-z]{2,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Secret = new(@"(?i)(password|passwort|pwd|authorization|token|secret)\s*[:=]\s*\S+", RegexOptions.Compiled);
    private static readonly StringBuilder Session = new();

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdelByte", "MailSetup", "logs");

    public static string CurrentFile => Path.Combine(Directory, $"setup-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", ex is null ? message : $"{message} – {ex.GetType().Name}: {ex.Message}");
        if (ex?.StackTrace is { } st) Write("ERROR", Mask(st));
    }

    /// <summary>Das bisherige Protokoll dieser Sitzung – für «Supportinformationen kopieren».</summary>
    public static string SessionText()
    {
        lock (Gate) return Session.ToString();
    }

    /// <summary>Maskiert Adressen und offensichtliche Geheimnisse in beliebigem Text.</summary>
    public static string Mask(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var t = Email.Replace(text, m => $"{m.Groups[1].Value}***@{m.Groups[2].Value}");
        return Secret.Replace(t, m => m.Value.Split(new[] { ':', '=' }, 2)[0] + "=***");
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {Mask(message)}";
        lock (Gate)
        {
            Session.AppendLine(line);
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(CurrentFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Ein Protokoll, das nicht geschrieben werden kann, darf das Setup nicht stoppen.
            }
        }
    }
}
