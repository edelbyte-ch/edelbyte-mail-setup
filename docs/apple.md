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
   per Ausgabe-Callback mit `openssl_cms_sign` und dem gültigen
   mail.edelbyte.ch-Zertifikat (`/etc/ssl/mail/cert.pem`, via
   docker-compose.override.yml read-only in php-fpm gemountet). DER/CMS mit
   eingebettetem Inhalt und voller Kette bis ISRG Root X1 → iOS/macOS zeigt
   „Verifiziert". Der Callback ist fail-open: schlägt Signierung fehl, wird das
   unsignierte Profil ausgeliefert (kann die Profilerzeugung nie brechen).

Backups der Originaldateien liegen als `*.bak-JJJJMMTT-HHMM` daneben.
