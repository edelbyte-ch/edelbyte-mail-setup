using EdelByte.MailSetup.Diagnostics;
using Xunit;

namespace EdelByte.MailSetup.Tests;

public class LogMaskTests
{
    [Theory]
    [InlineData("kunde@firma.ch", "k***@firma.ch")]
    [InlineData("Login für info@edelbyte.ch ok", "Login für i***@edelbyte.ch ok")]
    public void Mask_hides_local_part_of_email(string input, string expected)
        => Assert.Equal(expected, Log.Mask(input));

    [Fact]
    public void Mask_hides_password_assignments()
    {
        Assert.Contains("Passwort=***", Log.Mask("Passwort=geheim123"));
        Assert.Contains("token=***", Log.Mask("token: abc.def.ghi"));
    }

    [Fact]
    public void Mask_leaves_plain_text_untouched()
        => Assert.Equal("Outlook 16.0 gestartet", Log.Mask("Outlook 16.0 gestartet"));
}
