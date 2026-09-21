# Apple (iPhone, iPad, Mac)

Mailcow erzeugt ein Konfigurationsprofil (`.mobileconfig`) mit Mail, Kalender
und Kontakten – nach Anmeldung auf `mail.edelbyte.ch`.

## Ablauf

1. `/mail-setup/apple` → „Profil für mein Apple-Gerät holen" öffnet
   `https://mail.edelbyte.ch/index.php?mobileconfig`.
2. Der Kunde meldet sich dort an; Mailcow liefert das Profil.
3. iPhone/iPad: **Einstellungen → Profil geladen → Installieren**.
   Mac: **Systemeinstellungen → Allgemein → Geräteverwaltung → Installieren**.

## Warum so

Das Profil enthält die persönlichen Zugangsdaten und entsteht deshalb direkt auf
dem Mailserver. edelbyte.ch baut **keine** eigene Passwort-Proxy-API und sieht
das Passwort nie. Dasselbe Profil funktioniert auf iOS/iPadOS und aktuellem
macOS.

## App-Passwort und Signierung (mailcow-Anpassungen)

Zwei kleine Patches an der mailcow-Weboberfläche (`data/web`, bind-gemountet,
**update-unsafe** – nach einem mailcow-Update erneut anwenden):

1. **Kein 4×-Passwort auf dem Gerät.** Der Apple-Button lädt
   `mobileconfig.php?app_password`. mailcow erzeugt dann ein eigenes
   Gerätepasswort und bettet es ins Profil ein (Mail, CalDAV, CardDAV) – der
   Kunde meldet sich nur einmal auf mail.edelbyte.ch an und tippt auf dem Gerät
   nichts mehr. Patch: `inc/triggers.user.inc.php` reicht den Parameter
   `app_password` durch den Login-Redirect (mailcow verwarf ihn sonst).

2. **Signiert / „Verifiziert" (grün).** `mobileconfig.php` signiert das Profil
   per Ausgabe-Callback mit `openssl_cms_sign` (DER/CMS, Inhalt eingebettet,
   volle Kette). **Wichtig: Apple akzeptiert nur RSA-signierte Profile** – das
   mailcow-Zertifikat ist ECDSA und wird von iOS als „Ungültiges Profil"
   abgelehnt. Deshalb ein separates **RSA-Let's-Encrypt-Zertifikat** nur fürs
   Signieren:
   - Ausgestellt via `acme.sh` mit Cloudflare-DNS-01 (`--keylength 2048`),
     Home `~/.acme.sh` auf ws01-edelbyte, Cloudflare-Token (nur DNS edelbyte.ch)
     in `~/.acme.sh/account.conf`.
   - `acme.sh --install-cert` legt es nach `/home/bedmin/mail-signing/{cert,key}.pem`,
     read-only in php-fpm gemountet als `/etc/ssl/mail-rsa` (override).
   - Auto-Erneuerung: acme.sh-Cron (4×/Tag), reloadcmd chmod 644; php-fpm liest
     das Zertifikat pro Request → kein Neustart bei Erneuerung nötig.
   - Callback ist fail-open: schlägt Signierung fehl, kommt das unsignierte
     Profil (kann die Profilerzeugung nie brechen).
   - **OPcache-Falle:** nach Änderung an `mobileconfig.php` `touch` + php-fpm
     neu starten, sonst serviert OPcache die alte Version (validate_timestamps
     greift bei zurückdatierten mtimes nicht).

Backups der Originaldateien liegen als `*.bak-JJJJMMTT-HHMM` daneben.
