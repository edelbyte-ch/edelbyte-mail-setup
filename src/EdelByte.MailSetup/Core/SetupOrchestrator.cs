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

        var regVer = ol.RegistryVersionKey ?? "16.0";
        var reg = new OutlookProfileRegistry(regVer, profile);
        var account = reg.Find(email, MailServer.Host);
        var accountWasCreatedInteractively = false;
        if (account is null)
        {
            status?.Report("E-Mail-Konto wird eingerichtet …");
            account = await EnsureAccountAsync(ol, profile, regVer, email, status, ct);
            accountWasCreatedInteractively = account is not null && OutlookProcess.IsRunning;
            reg = new OutlookProfileRegistry(regVer, profile);
            account ??= reg.Find(email, MailServer.Host)
                        ?? throw new SetupException(Codes.AccountNotFound, "Das E-Mail-Konto konnte nicht angelegt werden.");
            Progress.Set("mail", StepState.Done, "neu eingerichtet");
        }
        else
        {
            Log.Info("E-Mail-Konto bereits vorhanden");
            Progress.Set("mail", StepState.Done, "bereits vorhanden");
        }

        // Passwort so ablegen, dass Outlook UND das Add-in es teilen. Wenn der
        // Kunde das Konto gerade eben interaktiv in Outlook bestätigt hat, hat
        // Outlook das Passwort schon selbst gespeichert – dann nicht überschreiben.
        if (!accountWasCreatedInteractively)
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

    /// <summary>
    /// Stellt das Mailkonto sicher. Zwei Wege, je nach Outlook-Bau:
    ///
    /// 1) PRF-Import (`/importprf`) – der klassische, von Microsoft dokumentierte
    ///    Weg. Funktioniert in Outlook 2016/2019 und vielen Builds still.
    ///
    /// 2) Fällt der PRF-Import leer aus (aktuelle Microsoft-365-Builds
    ///    provisionieren IMAP-Konten nicht mehr über PRF), übernimmt Outlooks
    ///    eigener Kontoassistent. Weil die Autokonfiguration serverseitig jetzt
    ///    funktioniert (autodiscover.edelbyte.ch liefert Server, Ports und
    ///    Verschlüsselung), bleibt für den Kunden nur: Adresse ist vorbelegt,
    ///    Passwort einmal eingeben. Danach erkennt das Setup das Konto selbst
    ///    und richtet Kalender und Kontakte automatisch weiter ein.
    /// </summary>
    private static async Task<OutlookAccount?> EnsureAccountAsync(OutlookInfo ol, string profile, string regVer, string email, IProgress<string>? status, CancellationToken ct)
    {
        var prf = PrfWriter.Build(new PrfWriter.Settings(
            ProfileName: profile,
            MakeDefault: string.IsNullOrEmpty(ol.DefaultProfile),
            ModifyDefaultProfileIfPresent: !string.IsNullOrEmpty(ol.DefaultProfile),
            AccountName: email, DisplayName: email, Email: email,
            ImapHost: MailServer.Host, ImapPort: MailServer.ImapPort,
            SmtpHost: MailServer.Host, SmtpPort: MailServer.SmtpPort));

        var path = Path.Combine(Path.GetTempPath(), $"edelbyte-{Guid.NewGuid():N}.prf");
        await File.WriteAllTextAsync(path, prf, ct);
        try
        {
            // Weg 1: stiller PRF-Import, in das Zielprofil hinein starten.
            OutlookProcess.Start(ol.ClassicPath!, $"/profile \"{profile}\" /importprf \"{path}\"");
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            await OutlookProcess.QuitGracefullyAsync(TimeSpan.FromSeconds(20));

            var reg = new OutlookProfileRegistry(regVer, profile);
            var acc = reg.Find(email, MailServer.Host);
            if (acc is not null) { Log.Info("Konto über PRF-Import eingerichtet"); return acc; }

            // Weg 2: Outlooks Kontoassistent (Autodiscover-gestützt). Nicht
            // vollständig still, aber dank funktionierender Autokonfiguration
            // auf zwei Felder reduziert.
            Log.Info("PRF-Import hat kein Konto erzeugt (aktueller Outlook-Bau) – öffne Outlook-Kontoassistent");
            status?.Report("Outlook öffnet die Kontoanmeldung – bitte einmal das Passwort bestätigen. Der Rest läuft danach automatisch.");
            OutlookProcess.Start(ol.ClassicPath!, $"/profile \"{profile}\"");

            // Bis zu drei Minuten auf das Konto warten, das Outlook nach der
            // Bestätigung anlegt.
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                acc = new OutlookProfileRegistry(regVer, profile).Find(email, MailServer.Host);
                if (acc is not null) { Log.Info("Konto über Outlook-Assistent eingerichtet"); return acc; }
            }
            return null;
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
