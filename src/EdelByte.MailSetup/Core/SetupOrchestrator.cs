using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using EdelByte.MailSetup.CalDav;
using EdelByte.MailSetup.Diagnostics;
using EdelByte.MailSetup.Mail;
using EdelByte.MailSetup.Outlook;

namespace EdelByte.MailSetup.Core;

/// <summary>
/// Führt die Einrichtung Schritt für Schritt aus. Jeder Schritt ist idempotent:
/// vorhandenes wird erkannt und wiederverwendet, nichts wird dupliziert. Das
/// Passwort bleibt im Speicher dieser Anwendung und geht nur an mail.edelbyte.ch.
/// </summary>
public sealed class SetupOrchestrator
{
    private static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private readonly HttpClient _http;

    public SetupProgress Progress { get; } = new();

    public SetupOrchestrator(HttpClient http) => _http = http;

    public async Task RunAsync(string email, string password, IProgress<string>? status, CancellationToken ct)
    {
        email = email.Trim();
        if (!EmailRx.IsMatch(email)) throw new SetupException(Codes.InvalidEmail, "Bitte eine gültige E-Mail-Adresse eingeben.");
        if (string.IsNullOrEmpty(password)) throw new SetupException(Codes.EmptyPassword, "Bitte Ihr Passwort eingeben.");

        Log.Info($"Einrichtung gestartet für {Log.Mask(email)}");

        // 1) Server + Anmeldung
        Progress.Set("server", StepState.Running);
        status?.Report("Server wird geprüft …");
        await CheckServerAsync(email, password, ct);
        Progress.Set("server", StepState.Done);

        // 2) Outlook
        Progress.Set("outlook", StepState.Running);
        var ol = OutlookInfo.Detect();
        if (!ol.HasClassic)
        {
            Progress.Set("outlook", StepState.Failed);
            throw ol.NewOutlookInstalled
                ? new SetupException(Codes.OnlyNewOutlook,
                    "Für die vollständige Einrichtung von E-Mail, Kalender und Kontakten wird Microsoft Outlook (classic) benötigt.",
                    "nur New Outlook installiert")
                : new SetupException(Codes.NoOutlook,
                    "Auf diesem Computer wurde kein Microsoft Outlook gefunden.");
        }
        if (ol.NewOutlookInstalled) Log.Info("New Outlook vorhanden – verwende Classic Outlook");
        var profile = ol.DefaultProfile ?? ol.Profiles.FirstOrDefault() ?? "Outlook";
        Progress.Set("outlook", StepState.Done, $"{ol.ClassicVersion} · Profil {profile}");

        // 3) Outlook schliessen, Mailkonto sicherstellen
        Progress.Set("mail", StepState.Running);
        status?.Report("Outlook wird für die Einrichtung kurz geschlossen …");
        if (!await OutlookProcess.QuitGracefullyAsync(TimeSpan.FromSeconds(20)))
            throw new SetupException(Codes.OutlookWontClose, "Bitte Outlook schliessen und die Einrichtung erneut starten.");

        var reg = new OutlookProfileRegistry(ol.RegistryVersionKey ?? "16.0", profile);
        var account = reg.Find(email, MailServer.Host);
        if (account is null)
        {
            status?.Report("E-Mail-Konto wird eingerichtet …");
            await ImportAccountAsync(ol, profile, email, ct);
            reg = new OutlookProfileRegistry(ol.RegistryVersionKey ?? "16.0", profile);
            account = reg.Find(email, MailServer.Host)
                      ?? throw new SetupException(Codes.AccountNotFound, "Das E-Mail-Konto konnte nicht angelegt werden.");
            Progress.Set("mail", StepState.Done, "neu eingerichtet");
        }
        else
        {
            Log.Info("E-Mail-Konto bereits vorhanden");
            Progress.Set("mail", StepState.Done, "bereits vorhanden");
        }

        // Passwort so ablegen, dass Outlook UND das Add-in es teilen (einmal eingeben)
        reg.StorePassword(account.RegistryKey, password);

        // 4) Kalender-Erweiterung
        Progress.Set("addin", StepState.Running);
        status?.Report("Kalender-Erweiterung wird vorbereitet …");
        var cdsVersion = await SynchronizerInstaller.EnsureInstalledAsync(_http, new Progress<string>(m => status?.Report(m)));
        Progress.Set("addin", StepState.Done, $"v{cdsVersion}");

        // 5)+6) Kalender & Kontakte einrichten (Ordner ermitteln, Discovery, Profile schreiben)
        Progress.Set("calendar", StepState.Running);
        Progress.Set("contacts", StepState.Running);
        status?.Report("Kalender und Kontakte werden eingerichtet …");
        await ConfigureDavAsync(profile, account, email, password, ct);
        Progress.Set("calendar", StepState.Done);
        Progress.Set("contacts", StepState.Done);

        // 7) Outlook starten und initialen Sync anstossen
        Progress.Set("start", StepState.Running);
        status?.Report("Outlook wird gestartet …");
        OutlookProcess.Start(ol.ClassicPath!, string.IsNullOrEmpty(ol.DefaultProfile) ? $"/profile \"{profile}\"" : "");
        await OutlookProcess.WaitUntilRunningAsync(TimeSpan.FromSeconds(30));
        Progress.Set("start", StepState.Done);

        Log.Info("Einrichtung abgeschlossen");
    }

    private async Task CheckServerAsync(string email, string password, CancellationToken ct)
    {
        var dns = await MailServerProbe.DnsAsync();
        if (!dns.Ok) throw new SetupException(Codes.DnsFailed, "Der Mailserver ist zurzeit nicht erreichbar. Bitte Internetverbindung prüfen.", dns.Detail);
        var tls = await MailServerProbe.TlsAsync(MailServer.ImapPort);
        if (!tls.Ok) throw new SetupException(Codes.TlsFailed, "Der Mailserver ist zurzeit nicht erreichbar.", tls.Detail);
        var login = await MailServerProbe.ImapLoginAsync(email, password);
        if (!login.Ok)
        {
            // «abgelehnt» = falsches Passwort; alles andere = Server-/Netzproblem
            if (login.Detail.Contains("abgelehnt"))
                throw new SetupException(Codes.AuthFailed, "E-Mail-Adresse oder Passwort stimmt nicht. Bitte prüfen.");
            throw new SetupException(Codes.ImapUnreachable, "Der Mailserver ist zurzeit nicht erreichbar.", login.Detail);
        }
    }

    private static async Task ImportAccountAsync(OutlookInfo ol, string profile, string email, CancellationToken ct)
    {
        var prf = PrfWriter.Build(new PrfWriter.Settings(
            ProfileName: profile,
            MakeDefault: string.IsNullOrEmpty(ol.DefaultProfile),
            ModifyDefaultProfileIfPresent: !string.IsNullOrEmpty(ol.DefaultProfile),
            AccountName: email,
            DisplayName: email,
            Email: email,
            ImapHost: MailServer.Host, ImapPort: MailServer.ImapPort,
            SmtpHost: MailServer.Host, SmtpPort: MailServer.SmtpPort));

        var path = Path.Combine(Path.GetTempPath(), $"edelbyte-{Guid.NewGuid():N}.prf");
        await File.WriteAllTextAsync(path, prf, ct);
        try
        {
            // /importprf ist der von Microsoft dokumentierte Weg für Konten in Classic Outlook.
            var p = OutlookProcess.Start(ol.ClassicPath!, $"/importprf \"{path}\"");
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
            await OutlookProcess.QuitGracefullyAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private async Task ConfigureDavAsync(string profile, OutlookAccount account, string email, string password, CancellationToken ct)
    {
        // Kalender-/Kontakteadresse per Discovery bestätigen
        var (calProbe, cardProbe, calUrl, cardUrl) = await MailServerProbe.DavAsync(email, password);
        if (!calProbe.Ok) throw new SetupException(Codes.DavUnreachable, "Kalender konnte nicht erreicht werden.", calProbe.Detail);
        if (!cardProbe.Ok) throw new SetupException(Codes.DavUnreachable, "Kontakte konnten nicht erreicht werden.", cardProbe.Detail);

        // Outlook-Ordner über COM ermitteln (EntryID/StoreID – nie statisch, nie geteilt)
        OutlookCom.FolderRef calendarFolder, contactsFolder;
        string accountName;
        dynamic? app = null;
        try
        {
            app = OutlookCom.Attach(profile);
            var acc = OutlookCom.FindAccount(app, email)
                      ?? throw new SetupException(Codes.AccountNotFound, "Das E-Mail-Konto wurde in Outlook nicht gefunden.");
            accountName = acc.DisplayName;
            try
            {
                calendarFolder = OutlookCom.DefaultFolder(acc.Store, calendar: true);
                contactsFolder = OutlookCom.DefaultFolder(acc.Store, calendar: false);
            }
            catch
            {
                // Manche Speicher haben keine Standardordner dieses Typs – dann genau einen anlegen.
                calendarFolder = OutlookCom.EnsureNamedFolder(acc.Store, "EdelByte Kalender", calendar: true);
                contactsFolder = OutlookCom.EnsureNamedFolder(acc.Store, "EdelByte Kontakte", calendar: false);
            }
        }
        catch (SetupException) { throw; }
        catch (Exception ex)
        {
            throw new SetupException(Codes.ComFailed, "Kalender und Kontakte konnten nicht mit Outlook verbunden werden.", ex.Message, ex);
        }
        finally
        {
            OutlookCom.TryQuit();
            OutlookCom.Release(app);
        }

        var cfg = new SynchronizerConfig(profile);
        cfg.Apply(new[]
        {
            new SynchronizerConfig.Target("EdelByte Kalender", calUrl, calendarFolder.EntryId, calendarFolder.StoreId, accountName, email, IsCalendar: true),
            new SynchronizerConfig.Target("EdelByte Kontakte", cardUrl, contactsFolder.EntryId, contactsFolder.StoreId, accountName, email, IsCalendar: false),
        });
    }
}
