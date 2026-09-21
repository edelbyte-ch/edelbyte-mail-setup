# Sicherheit

## Das Passwort

- Wird **nur** an `mail.edelbyte.ch` gesendet (IMAP-Login, DAV-Discovery).
- Erreicht **nie** edelbyte.ch – die Website hat keine Route, die es annimmt.
- Landet **nie** in Logs, Telemetrie, Crash-Reports, URLs oder Klartext-Dateien.
  Das Protokoll maskiert Adressen und filtert Passwort-/Token-Zuweisungen
  (`Diagnostics/Log.cs`).
- Wird DPAPI-geschützt (Benutzerkontext) abgelegt, exakt wie Outlook es selbst
  tut – Kennbyte `0x02` plus `ProtectedData.Protect`-Blob unter
  `…\Outlook\Profiles\{Profil}\9375CFF0413111d3B88A00104B2A6676\{Konto}\IMAP Password`.
- Der CalDav Synchronizer liest dasselbe Outlook-Passwort
  (`UseAccountPassword=true`); es wird kein zweites Passwort gespeichert.

## Fremde Software

Outlook CalDav Synchronizer wird ausschliesslich vom offiziellen Upstream
(`github.com/aluxnimm/outlookcaldavsynchronizer`, AGPL-3.0) geladen. Vor der
Installation:

- **SHA-256** gegen die Prüfsumme der GitHub-Release-API.
- **Authenticode-Signatur** des MSI/EXE; erwarteter Signierer
  „Generalize-IT Solutions".

Schlägt eine der Prüfungen fehl, bricht die Installation ab
(`EB-CALDAV-004`).

## Code Signing (offener Pflichtpunkt)

Der EdelByteMailSetup.exe ist **derzeit unsigniert**. Folgen:

- SmartScreen warnt beim ersten Start („Der Computer wurde geschützt").
- Der Kunde muss „Weitere Informationen" → „Trotzdem ausführen" wählen.

Vor produktiver Auslieferung ist ein **Authenticode Code Signing Certificate**
(OV oder EV) erforderlich. Mit EV-Zertifikat entfällt die SmartScreen-Warnung
sofort, mit OV nach ausreichender Reputation. Signiert wird im Release-Workflow
nach dem Publish:

```
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 \
  /a artifacts/EdelByteMailSetup.exe
```

Bis dahin gilt: Test-/Entwicklungs-Builds unsigniert zulässig, Kundenrelease
nicht.

## Berechtigungen

Der Client läuft als `asInvoker` (kein Administrator). Er schreibt nur in HKCU
und `%LOCALAPPDATA%`. Einzig das CalDavSynchronizer-MSI verlangt erhöhte Rechte;
die holt `msiexec` selbst per UAC.
