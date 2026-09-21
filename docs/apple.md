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
