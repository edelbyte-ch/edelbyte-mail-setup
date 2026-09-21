# Outlook-Details

## Kontoanlage – zwei Wege

**Befund (getestet 21.09.2026 auf Microsoft 365 Outlook, Build 16.0.20326):**
Aktuelle Microsoft-365-Builds legen IMAP-Konten **nicht mehr** über eine
PRF-Datei an – Outlook startet zwar in das Zielprofil, ignoriert aber die
`[IMAP_*]`-Kontosektion. Auch reines Registry-Seeding scheitert, weil Outlook
`IMAP Store EID`, `Delivery Store EntryID` u. a. erst beim Anlegen des lokalen
IMAP-Speichers selbst erzeugt. Microsoft hat die stille PRF-Provisionierung
bewusst eingeschränkt.

Der Client behandelt das in `Core/SetupOrchestrator.EnsureAccountAsync`:

1. **PRF-Import** (`/profile <P> /importprf <datei>`) – funktioniert in Outlook
   2016/2019 und manchen 365-Builds still. Danach prüft der Client, ob das Konto
   wirklich im Profil steht.
2. **Outlooks Kontoassistent**, falls (1) leer bleibt. Weil die Autokonfiguration
   jetzt serverseitig funktioniert (autodiscover.edelbyte.ch liefert IMAP 993/SSL,
   SMTP 465/SSL und sogar CalDAV/CardDAV), bleibt für den Kunden nur: Passwort
   einmal in Outlooks eigenem Dialog bestätigen. Der Client wartet, bis das Konto
   erscheint, und richtet **Kalender und Kontakte danach automatisch** ein.

## PRF-Details

`OUTLOOK.EXE /importprf <datei.prf>`. Die erzeugte Datei
(`Outlook/PrfWriter.cs`) beschreibt ein IMAP-Konto:

- `AccountType=IMAP`, `IMAPServer=mail.edelbyte.ch`, `IMAPPort=993`,
  `IMAPUseSSL=1`
- `SMTPServer=mail.edelbyte.ch`, `SMTPPort=465`, `SMTPUseSSL=1`,
  `SMTPUseAuth=1`
- `OverwriteProfile=Append` – bestehendes Profil ergänzen, nichts überschreiben
- Property-Tag-Zuordnung in Abschnitt `[IMAP_…]` gemäss Office Customization
  Tool

Die PRF-Spezifikation kennt kein Passwortfeld. Das Passwort wird separat gesetzt.

## Passwort im Profil

Outlook legt IMAP-Passwörter unter
`…\Outlook\Profiles\{Profil}\9375CFF0413111d3B88A00104B2A6676\{Konto}` im Wert
`IMAP Password` ab: ein Kennbyte `0x02`, danach ein DPAPI-Blob
(Benutzerkontext) des UTF-16-Strings mit Nullterminator. `OutlookProfileRegistry`
schreibt exakt dieses Format, dazu `SMTP Use Auth=1` und die SSL-Schalter.

## EntryID / StoreID

Der CalDav Synchronizer braucht je Profil EntryID und StoreID des Outlook-
Ordners. Beide werden zur Laufzeit über das Outlook-Objektmodell ermittelt
(`OutlookCom.DefaultFolder`) – nie statisch gespeichert, nie zwischen Benutzern
oder von einem Test-PC übernommen.

## Neustart

Läuft Outlook, wird es freundlich geschlossen: erst `Application.Quit` über COM,
dann `WM_CLOSE` ans Hauptfenster. Ein harter Kill bleibt aus, solange vermeidbar.
Nach der Einrichtung startet Classic Outlook wieder.
