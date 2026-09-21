using System.Runtime.InteropServices;
using EdelByte.MailSetup.Diagnostics;

namespace EdelByte.MailSetup.Outlook;

/// <summary>
/// Outlook-Objektmodell, spät gebunden (dynamic). Kein Interop-Paket nötig,
/// das EXE startet auch ohne Outlook. Nur das Nötigste: Konten, Ordner,
/// EntryID/StoreID – nichts, was Mailinhalte berührt.
/// </summary>
public static class OutlookCom
{
    private const int OlFolderCalendar = 9;
    private const int OlFolderContacts = 10;

    public sealed record FolderRef(string EntryId, string StoreId, string Name);
    public sealed record AccountRef(string SmtpAddress, string DisplayName, dynamic Store);

    /// <summary>Bereits laufendes Outlook holen (ROT). Null, wenn keines läuft.</summary>
    public static dynamic? GetRunning()
    {
        try
        {
            var hr = CLSIDFromProgID("Outlook.Application", out var clsid);
            if (hr < 0) return null;
            hr = GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
            return hr < 0 ? null : obj;
        }
        catch { return null; }
    }

    /// <summary>Outlook per COM starten oder anhängen. Bei explizitem Profil vorher anmelden.</summary>
    public static dynamic Attach(string? profileName)
    {
        var running = GetRunning();
        if (running is not null) return running;

        var t = Type.GetTypeFromProgID("Outlook.Application") ?? throw new InvalidOperationException("Outlook.Application nicht registriert");
        dynamic app = Activator.CreateInstance(t) ?? throw new InvalidOperationException("Outlook konnte nicht erzeugt werden");
        if (!string.IsNullOrEmpty(profileName))
        {
            dynamic ns = app.GetNamespace("MAPI");
            ns.Logon(profileName, "", false, true);
        }
        return app;
    }

    public static void TryQuit()
    {
        var app = GetRunning();
        if (app is null) return;
        try { app.Quit(); }
        finally { Release(app); }
    }

    /// <summary>Das Konto mit dieser Adresse – oder null.</summary>
    public static AccountRef? FindAccount(dynamic app, string email)
    {
        dynamic ns = app.GetNamespace("MAPI");
        dynamic accounts = ns.Accounts;
        int count = accounts.Count;
        for (var i = 1; i <= count; i++)
        {
            dynamic a = accounts.Item(i);
            string smtp = a.SmtpAddress ?? "";
            if (string.Equals(smtp, email, StringComparison.OrdinalIgnoreCase))
                return new AccountRef(smtp, (string)(a.DisplayName ?? smtp), a.DeliveryStore);
        }
        return null;
    }

    /// <summary>
    /// Kalender bzw. Kontakte im Datenspeicher dieses Kontos. Outlook legt für
    /// IMAP-Konten eigene «Nur dieser Computer»-Ordner an – das sind die
    /// Standardordner des Speichers. Kein Extra-Ordner nötig, nichts Fremdes
    /// wird angefasst.
    /// </summary>
    public static FolderRef DefaultFolder(dynamic store, bool calendar)
    {
        dynamic f = store.GetDefaultFolder(calendar ? OlFolderCalendar : OlFolderContacts);
        return new FolderRef((string)f.EntryID, (string)f.StoreID, (string)f.Name);
    }

    /// <summary>
    /// Fallback: benannten Ordner im Speicher finden oder genau einmal anlegen.
    /// Erneutes Ausführen findet den vorhandenen – kein «(2)».
    /// </summary>
    public static FolderRef EnsureNamedFolder(dynamic store, string name, bool calendar)
    {
        dynamic root = store.GetRootFolder();
        dynamic folders = root.Folders;
        int count = folders.Count;
        for (var i = 1; i <= count; i++)
        {
            dynamic f = folders.Item(i);
            if (string.Equals((string)f.Name, name, StringComparison.OrdinalIgnoreCase))
                return new FolderRef((string)f.EntryID, (string)f.StoreID, (string)f.Name);
        }
        dynamic created = folders.Add(name, calendar ? OlFolderCalendar : OlFolderContacts);
        Log.Info($"Ordner «{name}» angelegt");
        return new FolderRef((string)created.EntryID, (string)created.StoreID, (string)created.Name);
    }

    /// <summary>Name des aktuell geladenen Profils.</summary>
    public static string? CurrentProfileName(dynamic app)
    {
        try { return (string)app.GetNamespace("MAPI").CurrentProfileName; } catch { return null; }
    }

    public static void Release(object? o)
    {
        if (o is not null && Marshal.IsComObject(o))
            try { Marshal.FinalReleaseComObject(o); } catch { }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
}
