using System.Net.Http;
using System.Text;
using EdelByte.MailSetup.CalDav;
using EdelByte.MailSetup.Mail;
using EdelByte.MailSetup.Outlook;
using EdelByte.MailSetup.Update;

namespace EdelByte.MailSetup.Diagnostics;

/// <summary>
/// Read-only-Diagnose für «--diagnose» und «Supportinformationen kopieren».
/// Enthält nie Zugangsdaten; Adressen erscheinen nur maskiert.
/// </summary>
public static class DiagnoseReport
{
    public static async Task<string> BuildAsync(string? errorCode = null)
    {
        var sb = new StringBuilder();
        var ol = OutlookInfo.Detect();
        sb.AppendLine($"EdelByte Mail Setup: {UpdateChecker.CurrentVersion}");
        sb.AppendLine($"Windows: {OutlookInfo.WindowsDescription()}");
        sb.AppendLine($"Outlook Classic: {(ol.HasClassic ? $"{ol.ClassicVersion} ({ol.ClassicBitness})" : "nicht gefunden")}");
        sb.AppendLine($"Outlook New: {(ol.NewOutlookInstalled ? ol.NewOutlookVersion ?? "vorhanden" : "nicht gefunden")}");
        sb.AppendLine($"Outlook läuft: {(ol.IsRunning ? "ja" : "nein")}");
        sb.AppendLine($"Outlook-Profile: {ol.Profiles.Count} (Standard: {ol.DefaultProfile ?? "–"})");

        if (ol.RegistryVersionKey is not null && ol.DefaultProfile is not null)
        {
            var reg = new OutlookProfileRegistry(ol.RegistryVersionKey, ol.DefaultProfile);
            var ours = reg.Accounts().Where(a => string.Equals(a.ImapServer, MailServer.Host, StringComparison.OrdinalIgnoreCase)).ToList();
            sb.AppendLine($"EdelByte-Konten im Profil: {ours.Count}" + (ours.Count > 0 ? " (" + string.Join(", ", ours.Select(a => Log.Mask(a.AccountName))) + ")" : ""));
        }

        var cds = SynchronizerInstaller.Detect();
        sb.AppendLine($"CalDavSynchronizer: {(cds is null ? "nicht installiert" : cds.Version + (SynchronizerInstaller.AddinRegistered() ? ", Add-in registriert" : ", Add-in NICHT registriert"))}");
        if (ol.DefaultProfile is not null)
        {
            var names = new SynchronizerConfig(ol.DefaultProfile).ExistingProfileNames(MailServer.DavBase);
            sb.AppendLine($"Sync-Profile (EdelByte): {(names.Count == 0 ? "keine" : string.Join(", ", names))}");
        }

        sb.AppendLine();
        var dns = await MailServerProbe.DnsAsync();
        sb.AppendLine($"DNS {MailServer.Host}: {(dns.Ok ? "ok" : "FEHLER")} – {dns.Detail}");
        var tls = await MailServerProbe.TlsAsync(443);
        sb.AppendLine($"HTTPS/TLS: {(tls.Ok ? "ok" : "FEHLER")} – {tls.Detail}");
        var imap = await MailServerProbe.TlsAsync(MailServer.ImapPort);
        sb.AppendLine($"IMAP {MailServer.ImapPort}: {(imap.Ok ? "ok" : "FEHLER")} – {imap.Detail}");
        var smtp = await MailServerProbe.SmtpAsync();
        sb.AppendLine($"SMTP {MailServer.SmtpPort}: {(smtp.Ok ? "ok" : "FEHLER")} – {smtp.Detail}");
        var sub = await MailServerProbe.SubmissionBannerAsync();
        sb.AppendLine($"SMTP {MailServer.SubmissionPort}: {(sub.Ok ? "ok" : "FEHLER")} – {sub.Detail}");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EdelByteMailSetup/" + UpdateChecker.CurrentVersion);
        foreach (var (label, url, okCodes) in new[]
                 {
                     ("Webmail", MailServer.Webmail, new[] { 200, 302 }),
                     ("Kalender/Kontakte (DAV)", MailServer.DavBase, new[] { 401, 207, 200 }),
                     ("Autodiscover", "https://autodiscover.edelbyte.ch/autodiscover/autodiscover.xml", new[] { 200 }),
                 })
        {
            try
            {
                using var res = await http.GetAsync(url);
                sb.AppendLine($"{label}: {(okCodes.Contains((int)res.StatusCode) ? "ok" : "FEHLER")} – HTTP {(int)res.StatusCode}");
            }
            catch (Exception ex) { sb.AppendLine($"{label}: FEHLER – {ex.Message}"); }
        }

        if (errorCode is not null) sb.AppendLine().AppendLine($"Error: {errorCode}");
        sb.AppendLine().AppendLine($"Protokoll: {Log.CurrentFile}");
        return sb.ToString();
    }
}
