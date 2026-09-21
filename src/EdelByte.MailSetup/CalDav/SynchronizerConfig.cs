using System.IO;
using System.Text;
using System.Xml.Linq;
using EdelByte.MailSetup.Diagnostics;

namespace EdelByte.MailSetup.CalDav;

/// <summary>
/// Schreibt die Synchronisationsprofile des CalDavSynchronizer, so wie das
/// Add-in sie selbst ablegt (Version 4.7.1, Contracts/Options.cs):
///
///   %LOCALAPPDATA%\CalDavSynchronizer\profiles.xml            Outlook-Profil → Datenordner
///   %LOCALAPPDATA%\CalDavSynchronizer\{GUID}\options.xml      ArrayOfOptions
///
/// Bestehende Profile anderer Herkunft bleiben unangetastet: die Datei wird als
/// XML-Baum bearbeitet, nicht neu erzeugt. Unsere Profile werden über die
/// Server-Adresse wiedererkannt und ersetzt – so entsteht beim zweiten Lauf
/// kein zweites Profil.
/// </summary>
public sealed class SynchronizerConfig
{
    public static string BaseDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalDavSynchronizer");

    public sealed record Target(string Name, string Url, string FolderEntryId, string FolderStoreId, string AccountName, string Email, bool IsCalendar);

    private readonly string _outlookProfileName;

    public SynchronizerConfig(string outlookProfileName) => _outlookProfileName = outlookProfileName;

    /// <summary>Datenordner für dieses Outlook-Profil – anlegen, falls das Add-in noch nie lief.</summary>
    public string ResolveOptionsPath()
    {
        Directory.CreateDirectory(BaseDirectory);
        var profilesPath = Path.Combine(BaseDirectory, "profiles.xml");
        XDocument doc;
        if (File.Exists(profilesPath))
            doc = XDocument.Parse(File.ReadAllText(profilesPath));
        else
            doc = new XDocument(new XElement("ArrayOfProfileEntry",
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance")));

        var root = doc.Root!;
        var entry = root.Elements("ProfileEntry").FirstOrDefault(e =>
            string.Equals((string?)e.Attribute("ProfileName"), _outlookProfileName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            entry = new XElement("ProfileEntry",
                new XAttribute("ProfileName", _outlookProfileName),
                new XAttribute("ConfigFileName", "options.xml"),
                new XAttribute("DataDirectoryName", Guid.NewGuid().ToString()));
            root.Add(entry);
            Save(doc, profilesPath);
            Log.Info("CalDavSynchronizer-Profilverzeichnis angelegt");
        }

        var dirName = (string?)entry.Attribute("DataDirectoryName");
        var dir = string.IsNullOrEmpty(dirName) ? BaseDirectory : Path.Combine(BaseDirectory, dirName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, (string?)entry.Attribute("ConfigFileName") ?? "options.xml");
    }

    /// <summary>Unsere zwei Profile setzen; fremde bleiben, unsere alten werden ersetzt.</summary>
    public (int replaced, int added) Apply(IEnumerable<Target> targets)
    {
        var path = ResolveOptionsPath();
        XDocument doc = File.Exists(path)
            ? XDocument.Parse(File.ReadAllText(path))
            : new XDocument(new XElement("ArrayOfOptions",
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance")));
        var root = doc.Root!;

        int replaced = 0, added = 0;
        foreach (var t in targets)
        {
            var existing = root.Elements("Options").FirstOrDefault(o =>
                string.Equals((string?)o.Element("CalenderUrl"), t.Url, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)o.Element("Name"), t.Name, StringComparison.OrdinalIgnoreCase));
            var id = existing is not null && Guid.TryParse((string?)existing.Element("Id"), out var g) ? g : Guid.NewGuid();
            var fresh = Build(t, id);
            if (existing is not null) { existing.ReplaceWith(fresh); replaced++; }
            else { root.Add(fresh); added++; }
        }

        Save(doc, path);
        Log.Info($"CalDavSynchronizer-Profile geschrieben: {added} neu, {replaced} aktualisiert");
        return (replaced, added);
    }

    /// <summary>Gibt es unsere Profile bereits (für Diagnose und Idempotenz)?</summary>
    public IReadOnlyList<string> ExistingProfileNames(string urlPrefix)
    {
        var path = Path.Combine(BaseDirectory, "profiles.xml");
        if (!File.Exists(path)) return Array.Empty<string>();
        try
        {
            var opt = ResolveOptionsPath();
            if (!File.Exists(opt)) return Array.Empty<string>();
            return XDocument.Parse(File.ReadAllText(opt)).Root!
                .Elements("Options")
                .Where(o => ((string?)o.Element("CalenderUrl") ?? "").StartsWith(urlPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(o => (string?)o.Element("Name") ?? "?")
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static XElement Build(Target t, Guid id)
    {
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var mapping = t.IsCalendar ? EventMapping() : ContactMapping();
        mapping.Add(new XAttribute(xsi + "type", t.IsCalendar ? "EventMappingConfiguration" : "ContactMappingConfiguration"));

        return new XElement("Options",
            new XElement("Inactive", "false"),
            new XElement("Name", t.Name),
            new XElement("Id", id.ToString()),
            new XElement("OutlookFolderEntryId", t.FolderEntryId),
            new XElement("OutlookFolderStoreId", t.FolderStoreId),
            new XElement("OutlookFolderAccountName", t.AccountName),
            new XElement("IgnoreSynchronizationTimeRange", t.IsCalendar ? "false" : "true"),
            new XElement("DaysToSynchronizeInThePast", "60"),
            new XElement("DaysToSynchronizeInTheFuture", "365"),
            new XElement("SynchronizationMode", "MergeInBothDirections"),
            new XElement("ConflictResolution", "Automatic"),
            new XElement("CalenderUrl", t.Url),
            new XElement("EmailAddress", t.Email),
            new XElement("UserName", t.Email),
            new XElement("SynchronizationIntervalInMinutes", "15"),
            new XElement("UseWebDavCollectionSync", "false"),
            // Kein eigenes Passwort: das Add-in liest das IMAP-Passwort des Outlook-Kontos.
            new XElement("UseAccountPassword", "true"),
            new XElement("ServerAdapterType", "Default"),
            new XElement("CloseAfterEachRequest", "false"),
            new XElement("PreemptiveAuthentication", "true"),
            new XElement("ForceBasicAuthentication", "true"),
            new XElement("EnableChangeTriggeredSynchronization", "true"),
            new XElement("IsChunkedSynchronizationEnabled", "true"),
            new XElement("ChunkSize", "100"),
            new XElement("ProxyOptions",
                new XElement("ProxyUseDefault", "true"),
                new XElement("ProxyUseManual", "false")),
            mapping,
            new XElement("ProfileTypeOrNull", "Sogo"));
    }

    /// <summary>Vorgaben des SOGo-Profiltyps (ProfileTypes/ConcreteTypes/SogoProfile.cs).</summary>
    private static XElement EventMapping() => new("MappingConfiguration",
        new XElement("MapReminder", "JustUpcoming"),
        new XElement("MapSensitivityPrivateToClassConfidential", "false"),
        new XElement("MapClassConfidentialToSensitivityPrivate", "false"),
        new XElement("MapClassPublicToSensitivityPrivate", "false"),
        new XElement("MapSensitivityPublicToDefault", "false"),
        new XElement("MapAttendees", "true"),
        new XElement("ScheduleAgentClient", "false"),
        new XElement("SendNoAppointmentNotifications", "true"),
        new XElement("OrganizerAsDelegate", "false"),
        new XElement("MapBody", "true"),
        new XElement("MapRtfBodyToXAltDesc", "false"),
        new XElement("MapXAltDescToRtfBody", "false"),
        new XElement("CreateEventsInUTC", "false"),
        new XElement("UseIanaTz", "false"),
        new XElement("EventTz", SystemIanaTimeZone()),
        new XElement("IncludeHistoricalData", "false"),
        new XElement("UseGlobalAppointmentID", "true"),
        new XElement("IncludeEmptyEventCategoryFilter", "false"),
        new XElement("InvertEventCategoryFilter", "false"),
        new XElement("IsCategoryFilterSticky", "false"),
        new XElement("CleanupDuplicateEvents", "false"),
        new XElement("MapCustomProperties", "false"),
        new XElement("MapEventColorToCategory", "false"),
        new XElement("UserDefinedCustomPropertyMappings"));

    private static XElement ContactMapping() => new("MappingConfiguration",
        new XElement("MapAnniversary", "true"),
        new XElement("MapBirthday", "true"),
        new XElement("MapContactPhoto", "true"),
        new XElement("KeepOutlookPhoto", "false"),
        new XElement("KeepOutlookFileAs", "true"),
        new XElement("FixPhoneNumberFormat", "false"),
        new XElement("MapOutlookEmail1ToWork", "false"),
        new XElement("WriteImAsImpp", "false"),
        new XElement("DefaultImServicType", "AIM"),
        new XElement("MapDistributionLists", "true"),
        new XElement("DistributionListType", "Sogo"));

    private static string SystemIanaTimeZone()
    {
        var win = TimeZoneInfo.Local.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(win, out var iana) ? iana : "Etc/GMT";
    }

    /// <summary>UTF-16 mit BOM und Deklaration – identisch zur Ausgabe des XmlSerializer im Add-in.</summary>
    private static void Save(XDocument doc, string path)
    {
        doc.Declaration = new XDeclaration("1.0", "utf-16", null);
        using var w = new StreamWriter(path, false, new UnicodeEncoding(false, true));
        doc.Save(w);
    }
}
