using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EdelByte.MailSetup.Outlook;

public enum OutlookBitness { Unknown, X86, X64 }

/// <summary>Was auf diesem PC an Outlook vorhanden ist – rein lesend ermittelt.</summary>
public sealed record OutlookInfo(
    string? ClassicPath,
    string? ClassicVersion,
    OutlookBitness ClassicBitness,
    string? RegistryVersionKey,          // "16.0"
    bool NewOutlookInstalled,
    string? NewOutlookVersion,
    IReadOnlyList<string> Profiles,
    string? DefaultProfile,
    bool IsRunning)
{
    public bool HasClassic => ClassicPath is not null;

    public static OutlookInfo Detect()
    {
        var (path, version, bitness) = FindClassic();
        var regVersion = FindRegistryVersion();
        var (newInstalled, newVersion) = FindNewOutlook();
        var profiles = new List<string>();
        string? defaultProfile = null;

        if (regVersion is not null)
        {
            using var ol = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Office\{regVersion}\Outlook");
            defaultProfile = ol?.GetValue("DefaultProfile") as string;
            using var p = ol?.OpenSubKey("Profiles");
            if (p is not null) profiles.AddRange(p.GetSubKeyNames());
        }

        var running = Process.GetProcessesByName("OUTLOOK").Length > 0;
        return new OutlookInfo(path, version, bitness, regVersion, newInstalled, newVersion, profiles, defaultProfile, running);
    }

    private static (string? path, string? version, OutlookBitness bitness) FindClassic()
    {
        foreach (var key in new[]
                 {
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\OUTLOOK.EXE",
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\OUTLOOK.EXE",
                 })
        {
            using var k = Registry.LocalMachine.OpenSubKey(key);
            if (k?.GetValue(null) is string path && File.Exists(path))
            {
                var fvi = FileVersionInfo.GetVersionInfo(path);
                return (path, fvi.FileVersion, PeBitness(path));
            }
        }
        return (null, null, OutlookBitness.Unknown);
    }

    /// <summary>Liest den PE-Header: 0x8664 = x64, 0x014c = x86. Ohne Outlook zu starten.</summary>
    private static OutlookBitness PeBitness(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = br.ReadInt32();
            fs.Seek(peOffset + 4, SeekOrigin.Begin);
            var machine = br.ReadUInt16();
            return machine switch { 0x8664 => OutlookBitness.X64, 0x014c => OutlookBitness.X86, 0xAA64 => OutlookBitness.X64, _ => OutlookBitness.Unknown };
        }
        catch { return OutlookBitness.Unknown; }
    }

    private static string? FindRegistryVersion()
    {
        // Office 2016/2019/2021/2024/365 melden sich alle als 16.0
        using var office = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Office");
        if (office is null) return null;
        foreach (var v in new[] { "16.0", "15.0" })
        {
            using var k = office.OpenSubKey($@"{v}\Outlook");
            if (k is not null) return v;
        }
        return null;
    }

    /// <summary>Das neue «Outlook für Windows» ist eine Store-App (Microsoft.OutlookForWindows).</summary>
    private static (bool installed, string? version) FindNewOutlook()
    {
        try
        {
            var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (!Directory.Exists(packages)) return (false, null);
            var dir = Directory.EnumerateDirectories(packages, "Microsoft.OutlookForWindows_*").FirstOrDefault();
            if (dir is null) return (false, null);
            // Version aus dem Manifest-Cache des Pakets, falls vorhanden
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            var full = k?.GetSubKeyNames().FirstOrDefault(n => n.StartsWith("Microsoft.OutlookForWindows_", StringComparison.OrdinalIgnoreCase));
            var version = full?.Split('_').Skip(1).FirstOrDefault();
            return (true, version);
        }
        catch { return (false, null); }
    }

    public static string WindowsDescription()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = k?.GetValue("ProductName") as string ?? "Windows";
        var display = k?.GetValue("DisplayVersion") as string;
        var build = k?.GetValue("CurrentBuildNumber") as string;
        var ubr = k?.GetValue("UBR");
        // Windows 11 meldet sich in ProductName weiterhin als «Windows 10»; Build ≥ 22000 ist 11.
        if (int.TryParse(build, out var b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
        return $"{product} {display} (Build {build}.{ubr}) {RuntimeInformation.OSArchitecture}";
    }
}
