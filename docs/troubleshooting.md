# Störungssuche

## Zuerst: Status prüfen

`https://edelbyte.ch/mail-setup/diagnose` (Website) oder
`EdelByteMailSetup.exe --diagnose` (PC, read-only). Zeigt, ob Server, Webmail,
Kalender/Kontakte und Autodiscover erreichbar sind.

## Häufige Fälle

| Symptom | Ursache | Lösung |
|---|---|---|
| „Adresse oder Passwort stimmt nicht" (`EB-SERVER-004`) | falsche Zugangsdaten | Passwort prüfen; ggf. in Mailcow zurücksetzen |
| „Outlook (classic) benötigt" (`EB-OUTLOOK-002`) | nur New Outlook | Classic Outlook aus Microsoft 365/Office installieren |
| Kalender/Kontakte fehlen | Add-in nicht geladen | Outlook → Add-ins → CalDavSynchronizer aktivieren; Setup erneut |
| SmartScreen-Warnung beim Start | EXE unsigniert | „Weitere Informationen" → „Trotzdem ausführen"; dauerhaft: Code Signing |
| Outlook lässt sich nicht schliessen (`EB-OUTLOOK-003`) | offener Dialog in Outlook | Outlook manuell schliessen, Setup erneut |
| Zertifikatsfehler bei Autodiscover | Traefik-Router fehlt | siehe `mailcow.md` |

## Support kopieren

Im Client „Supportinformationen kopieren" – legt einen Bericht ohne
Zugangsdaten in die Zwischenablage (App-, Windows-, Outlook-, Plugin-Version,
Server-Erreichbarkeit, letzter Fehlercode).
