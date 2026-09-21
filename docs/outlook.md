# Outlook-Details

## Kontoanlage per PRF

Der von Microsoft dokumentierte Weg für Classic Outlook ist
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
