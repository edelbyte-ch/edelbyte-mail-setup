# Architektur

## Überblick

```
┌─────────────────────────────────────────────────────────────┐
│  edelbyte.ch  (Next.js 16, App Router, Dokploy)              │
│                                                              │
│  /mail-setup            Geräteauswahl                        │
│  /mail-setup/windows    Download-Seite                       │
│  /mail-setup/apple      Apple-Konfigurationsprofil           │
│  /mail-setup/android    Mail + DAVx⁵                         │
│  /mail-setup/manuell    Serverwerte zum Abtippen             │
│  /mail-setup/diagnose   Live-Status öffentlicher Dienste     │
│                                                              │
│  /api/mail-setup/health                                      │
│  /api/mail-setup/diagnose      serverseitige Statuscodes     │
│  /api/mail-setup/windows/latest  Version/Download/SHA-256    │
│  /mail-setup/download/windows   Redirect auf GitHub-Release  │
└───────────────┬─────────────────────────────────────────────┘
                │ Download EXE            ▲ latest.json
                ▼                         │
┌─────────────────────────────────────────┴───────────────────┐
│  EdelByteMailSetup.exe  (C# / .NET 8 / WPF)                  │
│  Repo: edelbyte-ch/edelbyte-mail-setup                       │
│                                                              │
│  IMAP 993 · SMTP 465 · CalDAV/CardDAV · Autodiscover        │
└───────────────┬─────────────────────────────────────────────┘
                ▼
┌──────────────────────────────────────────────────────────────┐
│  mail.edelbyte.ch  (Mailcow, Docker Compose, unverändert)    │
│  Postfix · Dovecot · SOGo 5.12 · Rspamd · nginx              │
│  Traefik (Dokploy) terminiert TLS für mail/autodiscover/     │
│  autoconfig.edelbyte.ch                                       │
└──────────────────────────────────────────────────────────────┘
```

## Grundsätze

- **Ein Server, ein Hostname.** Empfang, Versand, Kalender, Kontakte laufen alle
  über `mail.edelbyte.ch`. Kein zweiter Name, keine Setup-Subdomain.
- **Das Passwort erreicht edelbyte.ch nie.** Die Website reicht keine
  Zugangsdaten weiter. Der Windows-Client spricht direkt mit dem Mailserver.
- **Eine Quelle je Seite.** Serverwerte stehen in `src/lib/mail-setup.ts`
  (Website) bzw. `Mail/MailServer.cs` (Client). Ändert sich der Server, wird an
  genau einer Stelle je Projekt angepasst.
- **Der Mailserver bleibt Backend.** `/mail-setup` ist Portal und Assistent,
  nicht Mailclient.

## Warum kein Passwort-Proxy für Apple

Mailcow erzeugt das Apple-Konfigurationsprofil selbst, nach Anmeldung auf
`mail.edelbyte.ch`. Die Website leitet nur dorthin – so sieht edelbyte.ch das
Passwort nie, und es entsteht keine eigene, sicherheitskritische Proxy-API.
