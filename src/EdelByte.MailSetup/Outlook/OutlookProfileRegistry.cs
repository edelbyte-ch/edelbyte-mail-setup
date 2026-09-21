using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace EdelByte.MailSetup.Outlook;

/// <summary>Ein Konto, wie Outlook es im Profil ablegt – nur die nicht geheimen Felder.</summary>
public sealed record OutlookAccount(string RegistryKey, string AccountName, string? Email, string? ImapServer, int? ImapPort, string? SmtpServer, int? SmtpPort, bool HasPassword);

/// <summary>
/// Zugriff auf Outlooks Kontenliste im Profil:
/// HKCU\Software\Microsoft\Office\16.0\Outlook\Profiles\{Profil}\9375CFF0413111d3B88A00104B2A6676\0000000N
///
/// Das Passwort liegt dort als «IMAP Password»: ein Kennbyte 0x02, danach ein
/// DPAPI-Blob (Benutzerkontext) des UTF-16-Strings mit Nullterminator. Genau so
/// liest es auch der CalDavSynchronizer (OutlookAccountPasswordProvider) – wir
/// schreiben es in derselben Form, damit Outlook und das Add-in dasselbe
/// Passwort nutzen und der Kunde es nur einmal eingibt.
/// </summary>
public sealed class OutlookProfileRegistry
{
    private const string AccountsGuid = "9375CFF0413111d3B88A00104B2A6676";
    private readonly string _profileKey;

    public string ProfileName { get; }

    public OutlookProfileRegistry(string officeVersion, string profileName)
    {
        ProfileName = profileName;
        _profileKey = $@"Software\Microsoft\Office\{officeVersion}\Outlook\Profiles\{profileName}\{AccountsGuid}";
    }

    public bool ProfileExists()
    {
        using var k = Registry.CurrentUser.OpenSubKey(_profileKey);
        return k is not null;
    }

    public IReadOnlyList<OutlookAccount> Accounts()
    {
        var result = new List<OutlookAccount>();
        using var root = Registry.CurrentUser.OpenSubKey(_profileKey);
        if (root is null) return result;
        foreach (var name in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(name);
            if (k is null) continue;
            var accountName = Str(k.GetValue("Account Name"));
            var imap = Str(k.GetValue("IMAP Server"));
            if (accountName is null || imap is null) continue;   // nur IMAP-Konten interessieren
            result.Add(new OutlookAccount(
                name, accountName, Str(k.GetValue("Email")),
                imap, k.GetValue("IMAP Port") as int?,
                Str(k.GetValue("SMTP Server")), k.GetValue("SMTP Port") as int?,
                k.GetValue("IMAP Password") is byte[] { Length: > 1 }));
        }
        return result;
    }

    /// <summary>Das Konto für diese Adresse auf diesem Server – oder null.</summary>
    public OutlookAccount? Find(string email, string host) =>
        Accounts().FirstOrDefault(a =>
            string.Equals(a.ImapServer, host, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(a.AccountName, email, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Schreibt das Passwort so, wie Outlook es selbst ablegt. Der Klartext
    /// verlässt diese Methode nur als DPAPI-Blob; nichts davon wird protokolliert.
    /// </summary>
    public void StorePassword(string accountRegistryKey, string password)
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"{_profileKey}\{accountRegistryKey}", writable: true)
                      ?? throw new InvalidOperationException("Kontoschlüssel nicht gefunden");
        var plain = Encoding.Unicode.GetBytes(password + "\0");
        byte[] blob;
        try
        {
            blob = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            Array.Clear(plain);
        }
        var value = new byte[blob.Length + 1];
        value[0] = 0x02;
        Buffer.BlockCopy(blob, 0, value, 1, blob.Length);
        k.SetValue("IMAP Password", value, RegistryValueKind.Binary);
        // SMTP nutzt dieselben Anmeldedaten («SMTP Use Auth»=1 ohne eigenen Benutzer)
        k.SetValue("SMTP Use Auth", 1, RegistryValueKind.DWord);
        // 465 mit implizitem TLS: so legt es Outlooks eigener Assistent für diesen Server an
        k.SetValue("SMTP Use SSL", 1, RegistryValueKind.DWord);
        k.SetValue("SMTP Secure Connection", 1, RegistryValueKind.DWord);
    }

    private static string? Str(object? v) => v switch
    {
        string s => s,
        byte[] b => Encoding.Unicode.GetString(b).TrimEnd('\0'),
        _ => null,
    };
}
