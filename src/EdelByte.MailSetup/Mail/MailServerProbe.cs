using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Xml.Linq;
using EdelByte.MailSetup.Core;
using EdelByte.MailSetup.Diagnostics;

namespace EdelByte.MailSetup.Mail;

public sealed record ProbeResult(bool Ok, string Detail);

/// <summary>
/// Prüft den Mailserver direkt vom PC des Kunden aus – DNS, TLS, IMAP, SMTP,
/// Kalender/Kontakte. Das Passwort geht ausschliesslich verschlüsselt an
/// mail.edelbyte.ch, nie an eine andere Adresse, nie ins Protokoll.
/// </summary>
public static class MailServerProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task<ProbeResult> DnsAsync(string host = MailServer.Host)
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host);
            return addrs.Length > 0 ? new(true, $"{addrs.Length} Adresse(n)") : new(false, "keine Adresse");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    /// <summary>TLS-Handshake mit strikter Zertifikatsprüfung – so wie Outlook es tut.</summary>
    public static async Task<ProbeResult> TlsAsync(int port, string host = MailServer.Host)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port).WaitAsync(Timeout);
            await using var ssl = new SslStream(tcp.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }).WaitAsync(Timeout);
            var cert = ssl.RemoteCertificate;
            return new(true, $"{ssl.SslProtocol}, Zertifikat bis {cert?.GetExpirationDateString()}");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    /// <summary>
    /// Anmeldung per IMAP über TLS. Unterscheidet «Server nicht erreichbar» von
    /// «Passwort falsch», damit die Meldung an den Kunden stimmt.
    /// </summary>
    public static async Task<ProbeResult> ImapLoginAsync(string email, string password, string host = MailServer.Host, int port = MailServer.ImapPort)
    {
        if (password.IndexOfAny(new[] { '\r', '\n' }) >= 0) return new(false, "ungültiges Passwort");
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port).WaitAsync(Timeout);
            await using var ssl = new SslStream(tcp.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(host).WaitAsync(Timeout);
            using var reader = new StreamReader(ssl, Encoding.UTF8, false, 4096, leaveOpen: true);
            await using var writer = new StreamWriter(ssl, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

            var greeting = await reader.ReadLineAsync().WaitAsync(Timeout);
            if (greeting is null || !greeting.StartsWith("* OK")) return new(false, "keine IMAP-Begrüssung");

            await writer.WriteLineAsync($"a1 LOGIN {Quote(email)} {Quote(password)}");
            string? line;
            while ((line = await reader.ReadLineAsync().WaitAsync(Timeout)) is not null)
            {
                if (line.StartsWith("a1 ", StringComparison.Ordinal))
                {
                    var ok = line.StartsWith("a1 OK", StringComparison.Ordinal);
                    try { await writer.WriteLineAsync("a2 LOGOUT"); } catch { }
                    return ok ? new(true, "Anmeldung erfolgreich") : new(false, "Anmeldung abgelehnt");
                }
            }
            return new(false, "Verbindung abgebrochen");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public static async Task<ProbeResult> SmtpAsync(string host = MailServer.Host, int port = MailServer.SmtpPort)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port).WaitAsync(Timeout);
            await using var ssl = new SslStream(tcp.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(host).WaitAsync(Timeout);
            using var reader = new StreamReader(ssl, Encoding.ASCII, false, 1024, leaveOpen: true);
            var greeting = await reader.ReadLineAsync().WaitAsync(Timeout);
            return greeting is not null && greeting.StartsWith("220") ? new(true, "bereit") : new(false, greeting ?? "keine Antwort");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    /// <summary>
    /// Submission-Port (587) prüft den Klartext-Banner: dieser Port beginnt
    /// unverschlüsselt und wechselt erst per STARTTLS – ein direkter TLS-Handshake
    /// wie auf 465 scheitert hier zwangsläufig.
    /// </summary>
    public static async Task<ProbeResult> SubmissionBannerAsync(string host = MailServer.Host, int port = MailServer.SubmissionPort)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port).WaitAsync(Timeout);
            using var reader = new StreamReader(tcp.GetStream(), Encoding.ASCII);
            var greeting = await reader.ReadLineAsync().WaitAsync(Timeout);
            return greeting is not null && greeting.StartsWith("220") ? new(true, "bereit (STARTTLS)") : new(false, greeting ?? "keine Antwort");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    /// <summary>
    /// Kalender-/Kontakteadresse per DAV-Discovery (current-user-principal →
    /// home-set). SOGo liefert ein vorhersehbares Layout; die Discovery
    /// bestätigt es, statt dass wir es blind annehmen. Fällt sie aus, gilt
    /// das bekannte Layout mit Hinweis im Protokoll.
    /// </summary>
    public static async Task<(ProbeResult calendar, ProbeResult contacts, string calendarUrl, string contactsUrl)> DavAsync(string email, string password)
    {
        var calendarUrl = MailServer.CalendarUrl(email);
        var contactsUrl = MailServer.ContactsUrl(email);
        using var http = new HttpClient { Timeout = Timeout };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{password}")));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EdelByteMailSetup/1.0");

        try
        {
            var principal = await PropfindHrefAsync(http, MailServer.DavBase, "current-user-principal", 0);
            if (principal is not null)
            {
                var calHome = await PropfindHrefAsync(http, Abs(principal), "calendar-home-set", 0, "urn:ietf:params:xml:ns:caldav");
                var cardHome = await PropfindHrefAsync(http, Abs(principal), "addressbook-home-set", 0, "urn:ietf:params:xml:ns:carddav");
                if (calHome is not null) calendarUrl = Abs(calHome).TrimEnd('/') + "/personal/";
                if (cardHome is not null) contactsUrl = Abs(cardHome).TrimEnd('/') + "/personal/";
                Log.Info("DAV-Discovery bestätigt");
            }
            else Log.Warn("DAV-Discovery ohne Principal – verwende bekanntes SOGo-Layout");
        }
        catch (Exception ex) { Log.Warn($"DAV-Discovery fehlgeschlagen: {ex.Message}"); }

        var cal = await HeadAsync(http, calendarUrl);
        var card = await HeadAsync(http, contactsUrl);
        return (cal, card, calendarUrl, contactsUrl);
    }

    private static async Task<ProbeResult> HeadAsync(HttpClient http, string url)
    {
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            req.Headers.Add("Depth", "0");
            req.Content = new StringContent("<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");
            using var res = await http.SendAsync(req);
            return (int)res.StatusCode is 207 or 200 ? new(true, "erreichbar") : new(false, $"HTTP {(int)res.StatusCode}");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    private static async Task<string?> PropfindHrefAsync(HttpClient http, string url, string prop, int depth, string ns = "DAV:")
    {
        using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
        req.Headers.Add("Depth", depth.ToString());
        req.Content = new StringContent($"<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\" xmlns:x=\"{ns}\"><d:prop><{(ns == "DAV:" ? "d" : "x")}:{prop}/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");
        using var res = await http.SendAsync(req);
        if ((int)res.StatusCode != 207) return null;
        var xml = XDocument.Parse(await res.Content.ReadAsStringAsync());
        XNamespace d = "DAV:";
        XNamespace x = ns;
        return xml.Descendants(x + prop).Descendants(d + "href").Select(h => h.Value.Trim()).FirstOrDefault(v => v.Length > 0);
    }

    private static string Abs(string href) => href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : $"https://{MailServer.Host}{href}";

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
