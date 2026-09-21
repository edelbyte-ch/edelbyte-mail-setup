using EdelByte.MailSetup.Outlook;
using Xunit;

namespace EdelByte.MailSetup.Tests;

public class PrfWriterTests
{
    private static PrfWriter.Settings Sample() => new(
        "Outlook", true, false, "kunde@edelbyte.ch", "kunde@edelbyte.ch", "kunde@edelbyte.ch",
        "mail.edelbyte.ch", 993, "mail.edelbyte.ch", 465);

    [Fact]
    public void Build_contains_imap_account_with_ssl()
    {
        var prf = PrfWriter.Build(Sample());
        Assert.Contains("AccountType=IMAP", prf);
        Assert.Contains("IMAPServer=mail.edelbyte.ch", prf);
        Assert.Contains("IMAPPort=993", prf);
        Assert.Contains("IMAPUseSSL=1", prf);
        Assert.Contains("SMTPPort=465", prf);
        Assert.Contains("SMTPUseAuth=1", prf);
    }

    [Fact]
    public void Build_never_contains_password_field()
    {
        // Kein Passwort-Wert in der PRF: die Spezifikation kennt kein solches Feld,
        // und wir schreiben auch keins hinein. Kommentarwörter zählen nicht – wir
        // prüfen auf ein Schlüssel=Wert-Paar.
        foreach (var line in PrfWriter.Build(Sample()).Split('\n'))
        {
            var l = line.TrimStart();
            if (l.StartsWith(";")) continue;
            var lower = l.ToLowerInvariant();
            Assert.False(lower.StartsWith("password=") || lower.StartsWith("passwort=") ||
                         lower.Contains("password=") || lower.Contains("passwort="),
                $"Unerwartetes Passwortfeld: {line}");
        }
    }

    [Fact]
    public void Build_appends_into_existing_profile()
    {
        var prf = PrfWriter.Build(Sample());
        Assert.Contains("OverwriteProfile=Append", prf);
    }
}
