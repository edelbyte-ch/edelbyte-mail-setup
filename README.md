# EdelByte Mail Setup (Windows)

Richtet E-Mail, Kalender und Kontakte eines EdelByte-Postfachs auf einem
Windows-PC ein. Der Kunde gibt nur E-Mail-Adresse und Passwort ein; das Programm
erledigt Outlook-Konto, Kalender und Kontakte automatisch.

Gegenstück auf der Website: <https://edelbyte.ch/mail-setup> → Windows.

## Architektur

```
edelbyte.ch/mail-setup           Kundenportal, Download, Diagnose (Next.js)
        │  GET /api/mail-setup/windows/latest  → Version, Download, SHA-256
        ▼
EdelByteMailSetup.exe            dieses Repository (C# / .NET 8 / WPF)
        │  IMAP/SMTP/DAV direkt
        ▼
mail.edelbyte.ch                 Mailcow / SOGo (Backend, unverändert)
```

Der Windows-Client spricht ausschliesslich mit dem Mailserver. **Das Passwort
erreicht edelbyte.ch nie** – es bleibt lokal und geht nur an mail.edelbyte.ch.

## Was das Programm tut

1. Server + Anmeldung prüfen (IMAP über TLS).
2. Classic Outlook finden (New Outlook wird erkannt, aber nicht dafür verwendet).
3. Outlook freundlich schliessen; Mailkonto anlegen (`/importprf`) oder
   vorhandenes wiederverwenden – nie doppelt.
4. Passwort DPAPI-geschützt so ablegen, wie Outlook es selbst tut – Outlook und
   das Kalender-Add-in teilen es, der Kunde gibt es nur einmal ein.
5. Outlook CalDav Synchronizer vom offiziellen Upstream installieren
   (Prüfsumme + Signatur geprüft, still per `msiexec /qn`).
6. Kalender- und Kontakteordner über Outlook COM ermitteln (EntryID/StoreID
   dynamisch, nie statisch), DAV-Adressen per Discovery bestätigen,
   Sync-Profile schreiben – idempotent.
7. Outlook starten.

Mehrfaches Ausführen repariert statt zu duplizieren.

## Entwicklung

Voraussetzungen: .NET 8 SDK, Windows.

```powershell
dotnet restore
dotnet build -c Release
dotnet test  -c Release
```

Diagnose ohne GUI (read-only, keine Passwörter):

```powershell
EdelByteMailSetup.exe --diagnose
EdelByteMailSetup.exe --version
```

## Build (Installer)

Ein Single-File-EXE mit eingebetteter Laufzeit – keine separate .NET-Installation
beim Kunden:

```powershell
dotnet publish src/EdelByte.MailSetup/EdelByte.MailSetup.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:Version=1.0.0 -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -o artifacts
```

Ergebnis: `artifacts/EdelByteMailSetup.exe` (~66 MB, WPF ist nicht trimmbar).

## Veröffentlichung

Tag `vX.Y.Z` pushen → GitHub Actions (`.github/workflows/release.yml`) baut,
testet, veröffentlicht das EXE samt `.sha256`. Die Website liest die neueste
Veröffentlichung automatisch.

## Code Signing

**Noch nicht vorhanden.** Ohne Authenticode-Zertifikat warnt Windows SmartScreen
beim ersten Start. Für die produktive Kundenauslieferung ist ein Zertifikat
Pflicht – siehe `docs/security.md`. Test-Builds dürfen unsigniert sein.

## Mailcow-Abhängigkeiten

- IMAP `mail.edelbyte.ch:993` (SSL/TLS)
- SMTP `mail.edelbyte.ch:465` (SSL/TLS), Ausweichweg 587 (STARTTLS)
- CalDAV/CardDAV unter `https://mail.edelbyte.ch/SOGo/dav/` (SOGo)
- Autodiscover/Autoconfig unter `autodiscover.` / `autoconfig.edelbyte.ch`

Ändert sich am Server einer dieser Werte, gehört die Anpassung nach
`src/EdelByte.MailSetup/Mail/MailServer.cs` (eine Quelle) und auf der Website
nach `src/lib/mail-setup.ts`.

## Lizenzen Dritter

Outlook CalDav Synchronizer (AGPL-3.0) wird **nicht** mitgeliefert, sondern zur
Laufzeit vom offiziellen Upstream geladen und installiert.
