namespace EdelByte.MailSetup.Core;

/// <summary>
/// Fehlercodes, die der Kunde am Telefon vorlesen kann. Die Meldung ist für
/// Menschen, das Detail für den Expertenbereich und das Protokoll.
/// </summary>
public sealed class SetupException : Exception
{
    public string Code { get; }
    public string UserMessage { get; }
    public string? Detail { get; }

    public SetupException(string code, string userMessage, string? detail = null, Exception? inner = null)
        : base($"{code}: {userMessage}{(detail is null ? "" : " – " + detail)}", inner)
    {
        Code = code;
        UserMessage = userMessage;
        Detail = detail;
    }
}

public static class Codes
{
    // Eingaben
    public const string InvalidEmail = "EB-INPUT-001";
    public const string EmptyPassword = "EB-INPUT-002";

    // Server
    public const string DnsFailed = "EB-SERVER-001";
    public const string TlsFailed = "EB-SERVER-002";
    public const string ImapUnreachable = "EB-SERVER-003";
    public const string AuthFailed = "EB-SERVER-004";
    public const string DavUnreachable = "EB-SERVER-005";

    // Outlook
    public const string NoOutlook = "EB-OUTLOOK-001";
    public const string OnlyNewOutlook = "EB-OUTLOOK-002";
    public const string OutlookWontClose = "EB-OUTLOOK-003";
    public const string ProfileImportFailed = "EB-OUTLOOK-004";
    public const string AccountNotFound = "EB-OUTLOOK-005";
    public const string ComFailed = "EB-OUTLOOK-006";
    public const string FolderFailed = "EB-OUTLOOK-007";

    // Kalender/Kontakte
    public const string SynchronizerDownload = "EB-CALDAV-001";
    public const string SynchronizerInstall = "EB-CALDAV-002";
    public const string SynchronizerConfig = "EB-CALDAV-003";
    public const string SynchronizerHash = "EB-CALDAV-004";

    public const string Unexpected = "EB-GENERAL-001";
}
