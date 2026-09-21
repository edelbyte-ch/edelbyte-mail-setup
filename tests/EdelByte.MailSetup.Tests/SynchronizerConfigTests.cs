using System.IO;
using System.Xml.Linq;
using EdelByte.MailSetup.CalDav;
using Xunit;

namespace EdelByte.MailSetup.Tests;

public class SynchronizerConfigTests
{
    [Fact]
    public void SogoProfile_shape_matches_addin_contract()
    {
        // Wir bauen ein Ziel und prüfen die für SOGo entscheidenden Felder.
        var cfg = new SynchronizerConfig("UnitTestProfile-" + Guid.NewGuid().ToString("N"));
        var opt = cfg.ResolveOptionsPath();          // legt profiles.xml an
        Assert.True(File.Exists(Path.Combine(SynchronizerConfig.BaseDirectory, "profiles.xml")));

        var (replaced, added) = cfg.Apply(new[]
        {
            new SynchronizerConfig.Target("EdelByte Kalender", "https://mail.edelbyte.ch/SOGo/dav/x/Calendar/personal/", "E1", "S1", "acc", "x@edelbyte.ch", true),
            new SynchronizerConfig.Target("EdelByte Kontakte", "https://mail.edelbyte.ch/SOGo/dav/x/Contacts/personal/", "E2", "S2", "acc", "x@edelbyte.ch", false),
        });
        Assert.Equal(2, added);
        Assert.Equal(0, replaced);

        var doc = XDocument.Parse(File.ReadAllText(opt));
        var options = doc.Root!.Elements("Options").ToList();
        Assert.Equal(2, options.Count);
        Assert.All(options, o =>
        {
            Assert.Equal("Sogo", (string?)o.Element("ProfileTypeOrNull"));
            Assert.Equal("true", (string?)o.Element("UseAccountPassword"));
            Assert.Equal("MergeInBothDirections", (string?)o.Element("SynchronizationMode"));
            Assert.Null(o.Element("ProtectedPassword"));   // kein Passwort in der Datei
        });
    }

    [Fact]
    public void Apply_twice_is_idempotent()
    {
        var cfg = new SynchronizerConfig("UnitTestProfile-" + Guid.NewGuid().ToString("N"));
        var target = new[]
        {
            new SynchronizerConfig.Target("EdelByte Kalender", "https://mail.edelbyte.ch/SOGo/dav/y/Calendar/personal/", "E1", "S1", "acc", "y@edelbyte.ch", true),
        };
        var first = cfg.Apply(target);
        var second = cfg.Apply(target);
        Assert.Equal(1, first.added);
        Assert.Equal(0, second.added);
        Assert.Equal(1, second.replaced);

        var opt = cfg.ResolveOptionsPath();
        var count = XDocument.Parse(File.ReadAllText(opt)).Root!.Elements("Options").Count();
        Assert.Equal(1, count);   // kein zweites Profil
    }
}
