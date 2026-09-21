using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EdelByte.MailSetup.Core;
using EdelByte.MailSetup.Diagnostics;
using Microsoft.Win32;

namespace EdelByte.MailSetup.CalDav;

/// <summary>
/// Outlook CalDav Synchronizer – ausschliesslich vom offiziellen Upstream
/// github.com/aluxnimm/outlookcaldavsynchronizer (AGPL-3.0). Vor der
/// Installation: Prüfsumme aus der Release-API und Authenticode-Signatur des
/// MSI. Installiert wird still per Windows Installer (msiexec /qn), was für
/// jedes MSI offiziell unterstützt ist; scheitert das (z. B. fehlende
/// Voraussetzung), startet der mitgelieferte setup.exe-Bootstrapper sichtbar.
/// </summary>
public static class SynchronizerInstaller
{
    public const string Owner = "aluxnimm";
    public const string Repo = "outlookcaldavsynchronizer";
    private const string ExpectedSignerFragment = "Generalize-IT Solutions";

    public sealed record Installed(string Version, string? UninstallString);
    public sealed record Release(string Version, string ZipUrl, string? Sha256, long Size);

    /// <summary>Installierte Version über die Deinstallationsliste – ohne Outlook zu starten.</summary>
    public static Installed? Detect()
    {
        foreach (var (hive, path) in new[]
                 {
                     (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                     (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                 })
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                if (k?.GetValue("DisplayName") is string dn && dn.Equals("CalDavSynchronizer", StringComparison.OrdinalIgnoreCase))
                    return new Installed(k.GetValue("DisplayVersion") as string ?? "?", k.GetValue("UninstallString") as string);
            }
        }
        return null;
    }

    /// <summary>Ist das Add-in bei Outlook registriert (LoadBehavior 3 = beim Start laden)?</summary>
    public static bool AddinRegistered()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var k = hive.OpenSubKey(@"Software\Microsoft\Office\Outlook\Addins\CalDavSynchronizer.1");
            if (k?.GetValue("LoadBehavior") is int lb && (lb & 2) != 0) return true;
        }
        return false;
    }

    public static async Task<Release> LatestAsync(HttpClient http)
    {
        var data = await http.GetFromJsonAsync<GhRelease>($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest")
                   ?? throw new SetupException(Codes.SynchronizerDownload, "Die Kalender-Erweiterung ist gerade nicht abrufbar.");
        var zip = data.assets?.FirstOrDefault(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                  ?? throw new SetupException(Codes.SynchronizerDownload, "Die Kalender-Erweiterung ist gerade nicht abrufbar.", "kein ZIP im Release");
        var sha = zip.digest is { } d && d.StartsWith("sha256:") ? d[7..] : null;
        return new Release((data.tag_name ?? "").TrimStart('v'), zip.browser_download_url, sha, zip.size);
    }

    /// <summary>Lädt, prüft (SHA-256 + Signatur), installiert. Idempotent: bei passender Version passiert nichts.</summary>
    public static async Task<string> EnsureInstalledAsync(HttpClient http, IProgress<string>? progress = null)
    {
        var installed = Detect();
        var latest = await LatestAsync(http);
        if (installed is not null && VersionAtLeast(installed.Version, latest.Version))
        {
            Log.Info($"CalDavSynchronizer {installed.Version} bereits installiert (aktuell: {latest.Version})");
            return installed.Version;
        }

        var work = Path.Combine(Path.GetTempPath(), "EdelByteMailSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zipPath = Path.Combine(work, "ocs.zip");
            progress?.Report("Kalender-Erweiterung wird geladen …");
            Log.Info($"Lade CalDavSynchronizer {latest.Version} ({latest.Size / 1024} KB)");
            await using (var s = await http.GetStreamAsync(latest.ZipUrl))
            await using (var f = File.Create(zipPath))
                await s.CopyToAsync(f);

            if (latest.Sha256 is not null)
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(zipPath))).ToLowerInvariant();
                if (!actual.Equals(latest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new SetupException(Codes.SynchronizerHash, "Die geladene Datei ist beschädigt. Bitte erneut versuchen.", "SHA-256 stimmt nicht");
                Log.Info("SHA-256 geprüft");
            }
            else Log.Warn("Release ohne Prüfsumme – nur Signatur geprüft");

            ZipFile.ExtractToDirectory(zipPath, work);
            var msi = Directory.EnumerateFiles(work, "*.msi").FirstOrDefault()
                      ?? throw new SetupException(Codes.SynchronizerInstall, "Die Kalender-Erweiterung konnte nicht installiert werden.", "kein MSI im Archiv");

            VerifySignature(msi);

            progress?.Report("Kalender-Erweiterung wird installiert …");
            var log = Path.Combine(Log.Directory, $"caldavsynchronizer-{DateTime.Now:yyyyMMdd-HHmmss}.msi.log");
            var rc = await RunAsync("msiexec.exe", $"/i \"{msi}\" /qn /norestart /l*v \"{log}\"");
            if (rc is 0 or 3010)
            {
                Log.Info($"CalDavSynchronizer {latest.Version} installiert (msiexec rc={rc})");
                return latest.Version;
            }

            // 1603 = Voraussetzung fehlt o. ä. – der Bootstrapper zeigt dann sichtbar, was fehlt.
            Log.Warn($"Stille Installation fehlgeschlagen (rc={rc}), starte setup.exe sichtbar");
            var setup = Directory.EnumerateFiles(work, "setup.exe").FirstOrDefault();
            if (setup is null) throw new SetupException(Codes.SynchronizerInstall, "Die Kalender-Erweiterung konnte nicht installiert werden.", $"msiexec rc={rc}");
            VerifySignature(setup);
            progress?.Report("Bitte die Installation der Kalender-Erweiterung bestätigen …");
            var rc2 = await RunAsync(setup, "");
            if (Detect() is null) throw new SetupException(Codes.SynchronizerInstall, "Die Kalender-Erweiterung wurde nicht installiert.", $"setup.exe rc={rc2}");
            return Detect()!.Version;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    private static void VerifySignature(string file)
    {
        try
        {
            var cert = X509Certificate.CreateFromSignedFile(file);
            if (!cert.Subject.Contains(ExpectedSignerFragment, StringComparison.OrdinalIgnoreCase))
                throw new SetupException(Codes.SynchronizerHash, "Die Kalender-Erweiterung stammt nicht vom erwarteten Herausgeber.", $"Signatur: {cert.Subject}");
            Log.Info($"Signatur geprüft: {cert.Subject.Split(',')[0]}");
        }
        catch (SetupException) { throw; }
        catch (Exception ex)
        {
            throw new SetupException(Codes.SynchronizerHash, "Die Kalender-Erweiterung ist nicht signiert.", ex.Message, ex);
        }
    }

    private static async Task<int> RunAsync(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true })
                      ?? throw new InvalidOperationException("Prozess konnte nicht gestartet werden");
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    public static bool VersionAtLeast(string have, string want) =>
        Version.TryParse(have, out var h) && Version.TryParse(want, out var w) && h >= w;

    private sealed record GhRelease(string? tag_name, GhAsset[]? assets);
    private sealed record GhAsset(string name, string browser_download_url, long size, string? digest);
}
