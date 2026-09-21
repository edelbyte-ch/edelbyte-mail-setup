using System.Text;

namespace EdelByte.MailSetup.Outlook;

/// <summary>
/// Erzeugt eine Outlook-Profildatei (PRF) für ein IMAP-Konto. Das ist der von
/// Microsoft dokumentierte Weg («OUTLOOK.EXE /importprf»); die Schlüssel und
/// Property-Tags in Abschnitt 7 entsprechen der Vorlage des Office
/// Customization Tool. Die PRF-Spezifikation kennt bewusst kein Passwortfeld –
/// das Passwort wird separat und DPAPI-geschützt abgelegt
/// (siehe <see cref="OutlookProfileRegistry.StorePassword"/>).
/// </summary>
public static class PrfWriter
{
    public sealed record Settings(
        string ProfileName,
        bool MakeDefault,
        bool ModifyDefaultProfileIfPresent,
        string AccountName,
        string DisplayName,
        string Email,
        string ImapHost, int ImapPort,
        string SmtpHost, int SmtpPort);

    public static string Build(Settings s)
    {
        var sb = new StringBuilder();
        sb.AppendLine(";*** EdelByte Mail Setup – Outlook-Profildatei (ohne Passwort)");
        sb.AppendLine("[General]");
        sb.AppendLine("Custom=1");
        sb.AppendLine($"ProfileName={s.ProfileName}");
        sb.AppendLine($"DefaultProfile={(s.MakeDefault ? "Yes" : "No")}");
        // Append: bestehendes Profil ergänzen statt ersetzen – Kunde verliert nichts.
        sb.AppendLine("OverwriteProfile=Append");
        sb.AppendLine($"ModifyDefaultProfileIfPresent={(s.ModifyDefaultProfileIfPresent ? "TRUE" : "FALSE")}");
        sb.AppendLine("BackupProfile=No");
        sb.AppendLine();
        sb.AppendLine("[Service List]");
        sb.AppendLine("Service1=Microsoft Outlook Client");
        sb.AppendLine();
        sb.AppendLine("[Internet Account List]");
        sb.AppendLine("Account1=EdelByteIMAP");
        sb.AppendLine();
        sb.AppendLine("[Service1]");
        sb.AppendLine();
        sb.AppendLine("[EdelByteIMAP]");
        sb.AppendLine("AccountType=IMAP");
        sb.AppendLine($"AccountName={s.AccountName}");
        sb.AppendLine($"DisplayName={s.DisplayName}");
        sb.AppendLine($"EmailAddress={s.Email}");
        sb.AppendLine($"IMAPServer={s.ImapHost}");
        sb.AppendLine($"IMAPUserName={s.Email}");
        sb.AppendLine($"IMAPPort={s.ImapPort}");
        sb.AppendLine("IMAPUseSSL=1");
        sb.AppendLine("IMAPUseSPA=0");
        sb.AppendLine($"SMTPServer={s.SmtpHost}");
        sb.AppendLine($"SMTPPort={s.SmtpPort}");
        sb.AppendLine("SMTPUseSSL=1");
        sb.AppendLine("SMTPUseAuth=1");
        sb.AppendLine("SMTPAuthMethod=0");
        sb.AppendLine("SMTPUseSPA=0");
        sb.AppendLine("ConnectionType=0");
        sb.AppendLine("ServerTimeOut=60");
        sb.AppendLine();
        // Abschnitt 6/7: Zuordnung zu MAPI-Property-Tags (Office Customization Tool)
        sb.AppendLine("[Microsoft Outlook Client]");
        sb.AppendLine("SectionGUID=0a0d020000000000c000000000000046");
        sb.AppendLine();
        sb.AppendLine("[IMAP_EdelByteIMAP]");
        sb.AppendLine("AccountType=IMAP");
        sb.AppendLine("AccountName=PT_UNICODE,0x0002");
        sb.AppendLine("DisplayName=PT_UNICODE,0x000B");
        sb.AppendLine("EmailAddress=PT_UNICODE,0x000C");
        sb.AppendLine("IMAPServer=PT_UNICODE,0x0100");
        sb.AppendLine("IMAPUserName=PT_UNICODE,0x0101");
        sb.AppendLine("IMAPUseSPA=PT_LONG,0x0108");
        sb.AppendLine("Organization=PT_UNICODE,0x0107");
        sb.AppendLine("ReplyEmailAddress=PT_UNICODE,0x0103");
        sb.AppendLine("IMAPPort=PT_LONG,0x0104");
        sb.AppendLine("IMAPUseSSL=PT_LONG,0x0105");
        sb.AppendLine("SMTPServer=PT_UNICODE,0x0200");
        sb.AppendLine("SMTPUseAuth=PT_LONG,0x0203");
        sb.AppendLine("SMTPAuthMethod=PT_LONG,0x0208");
        sb.AppendLine("SMTPUserName=PT_UNICODE,0x0204");
        sb.AppendLine("SMTPUseSPA=PT_LONG,0x0207");
        sb.AppendLine("ConnectionType=PT_LONG,0x000F");
        sb.AppendLine("ConnectionOID=PT_UNICODE,0x0010");
        sb.AppendLine("SMTPPort=PT_LONG,0x0201");
        sb.AppendLine("SMTPUseSSL=PT_LONG,0x0202");
        sb.AppendLine("ServerTimeOut=PT_LONG,0x0209");
        sb.AppendLine("CheckNewImap=PT_LONG,0x1100");
        sb.AppendLine("RootFolder=PT_UNICODE,0x1101");
        return sb.ToString();
    }
}
