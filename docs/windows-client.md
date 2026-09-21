# Windows-Client

## Aufbau

| Bereich | Datei | Zweck |
|---|---|---|
| Einstieg | `Program.cs` | GUI, `--diagnose`, `--version` |
| Oberfläche | `Ui/MainWindow.xaml(.cs)` | Eingabe, Fortschritt, Fehler, Experten-Details |
| Ablauf | `Core/SetupOrchestrator.cs` | die sieben Schritte, idempotent |
| Outlook erkennen | `Outlook/OutlookInfo.cs` | Version, Bitness, Classic/New, Profile |
| Konto anlegen | `Outlook/PrfWriter.cs` | `/importprf`-Profildatei (ohne Passwort) |
| Passwort ablegen | `Outlook/OutlookProfileRegistry.cs` | DPAPI, wie Outlook selbst |
| Outlook steuern | `Outlook/OutlookProcess.cs`, `OutlookCom.cs` | schliessen/starten, Ordner, EntryID |
| Server prüfen | `Mail/MailServerProbe.cs` | DNS, TLS, IMAP-Login, SMTP, DAV-Discovery |
| Add-in | `CalDav/SynchronizerInstaller.cs` | laden, prüfen, installieren |
| Sync-Profile | `CalDav/SynchronizerConfig.cs` | `options.xml` im Add-in-Format |
| Update | `Update/UpdateChecker.cs` | Version von edelbyte.ch, Hash-Prüfung |
| Diagnose | `Diagnostics/DiagnoseReport.cs` | read-only Sammelbericht |
| Protokoll | `Diagnostics/Log.cs` | maskiert, keine Geheimnisse |

## Idempotenz

Jeder Schritt erkennt Vorhandenes:

- **Mailkonto**: über Adresse + Server im Profil; vorhandenes wird
  wiederverwendet. Neu: PRF-Import, sonst Outlooks Kontoassistent (auf aktuellen
  Microsoft-365-Builds provisioniert PRF keine IMAP-Konten mehr – siehe
  `outlook.md`). Kein doppeltes Konto, weil vorher gesucht wird.
- **Sync-Profile**: über die DAV-Adresse bzw. den Namen; unsere Profile werden
  ersetzt, fremde bleiben. Zweiter Lauf → weiterhin genau ein Kalender, ein
  Kontakteprofil.
- **Ordner**: bevorzugt die Standardordner des Kontospeichers; nur falls nötig
  „EdelByte Kalender/Kontakte", und die werden beim zweiten Lauf gefunden, nicht
  neu angelegt.

## Classic vs. New Outlook

- Beide vorhanden → Classic wird verwendet.
- Nur New Outlook → Abbruch mit Erklärung (`EB-OUTLOOK-002`); kein
  Halb-Setup, kein Hack an CalDAV/CardDAV, keine inkompatiblen Add-ins.
- Kein Outlook → `EB-OUTLOOK-001`.

## Fehlercodes

`EB-INPUT-*` Eingabe · `EB-SERVER-*` Server/Netz · `EB-OUTLOOK-*` Outlook ·
`EB-CALDAV-*` Kalender-Erweiterung · `EB-GENERAL-001` unerwartet.
Der Kunde sieht eine verständliche Meldung plus Code; Details im Experten-/
Diagnosebereich und im Protokoll.

## Protokoll

`%LOCALAPPDATA%\EdelByte\MailSetup\logs\setup-JJJJ-MM-TT.log`. Enthält App-,
Windows-, Outlook-, Plugin-Version, Arbeitsschritte, Ergebnisse, Fehlercodes.
Adressen maskiert (`e***@firma.ch`), nie Passwörter/Tokens/Mailinhalte.
