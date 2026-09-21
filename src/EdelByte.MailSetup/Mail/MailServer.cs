namespace EdelByte.MailSetup.Mail;

/// <summary>
/// Der EdelByte-Mailserver. Werte geprüft gegen die laufende Mailcow-Installation
/// (Autoconfig unter https://mail.edelbyte.ch/mail/config-v1.1.xml) am 21.09.2026.
/// Outlook bekommt SMTP 465 mit implizitem TLS: das ist die Variante, die Outlooks
/// eigener Assistent für diesen Server anlegt, und die PRF-Vorlage kennt keinen
/// STARTTLS-Schalter.
/// </summary>
public static class MailServer
{
    public const string Host = "mail.edelbyte.ch";
    public const int ImapPort = 993;
    public const int SmtpPort = 465;
    public const int SubmissionPort = 587;
    public const string DavBase = "https://mail.edelbyte.ch/SOGo/dav/";
    public const string Webmail = "https://mail.edelbyte.ch/SOGo/";
    public const string SetupSite = "https://edelbyte.ch/mail-setup";
    public const string LatestApi = "https://edelbyte.ch/api/mail-setup/windows/latest";

    public static string CalendarUrl(string email) => $"{DavBase}{Uri.EscapeDataString(email)}/Calendar/personal/";
    public static string ContactsUrl(string email) => $"{DavBase}{Uri.EscapeDataString(email)}/Contacts/personal/";
}
