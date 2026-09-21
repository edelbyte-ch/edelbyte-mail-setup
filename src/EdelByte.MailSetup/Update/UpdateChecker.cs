using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using EdelByte.MailSetup.Diagnostics;
using EdelByte.MailSetup.Mail;

namespace EdelByte.MailSetup.Update;

public sealed record LatestInfo(string version, string download, string? sha256);

/// <summary>
/// Fragt edelbyte.ch nach der aktuellen Version. Nur lesen – der Kunde
/// entscheidet, ob er die neue Version lädt. Geladen wird nur, wenn die
/// Prüfsumme stimmt. Es wird nichts über den Kunden übertragen.
/// </summary>
public static class UpdateChecker
{
    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
            return v.Split('+')[0];
        }
    }

    public static async Task<LatestInfo?> CheckAsync(HttpClient http)
    {
        try
        {
            var info = await http.GetFromJsonAsync<LatestInfo>(MailServer.LatestApi);
            if (info is null) return null;
            var newer = Version.TryParse(info.version, out var l) && Version.TryParse(CurrentVersion, out var c) && l > c;
            Log.Info($"Version {CurrentVersion}, verfügbar {info.version}{(newer ? " (neuer)" : "")}");
            return newer ? info : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Versionsprüfung nicht möglich: {ex.Message}");
            return null;
        }
    }

    /// <summary>Lädt die neue Version, prüft die Summe, startet sie und beendet sich.</summary>
    public static async Task<bool> DownloadAndRunAsync(HttpClient http, LatestInfo info)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EdelByteMailSetup");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"EdelByteMailSetup-{info.version}.exe");
        await using (var s = await http.GetStreamAsync(info.download))
        await using (var f = File.Create(target))
            await s.CopyToAsync(f);

        if (!string.IsNullOrEmpty(info.sha256))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(target))).ToLowerInvariant();
            if (!actual.Equals(info.sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log.Error("Prüfsumme der neuen Version stimmt nicht – Datei verworfen");
                try { File.Delete(target); } catch { }
                return false;
            }
        }
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return true;
    }
}
