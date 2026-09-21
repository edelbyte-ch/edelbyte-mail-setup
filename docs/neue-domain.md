# Neue Kundendomain aufschalten (z. B. xyz.ch, info@xyz.ch)

Kurzfassung: **Das EdelByte-Setup funktioniert für jede Domain automatisch** –
Windows-Client, Apple-Profil, Webmail und die manuelle Einrichtung zeigen alle
auf `mail.edelbyte.ch`, unabhängig von der E-Mail-Domain. Für die **automatische
Erkennung in Outlook/Thunderbird** (Kunde tippt nur E-Mail + Passwort) braucht
jede Domain zusätzlich ein DNS-Record – das liegt in der Natur von Autodiscover,
das immer die Domain des Kunden abfragt.

## 1. Postfach anlegen (immer nötig)

In Mailcow: Domain `xyz.ch` hinzufügen, Mailbox `info@xyz.ch` anlegen. Damit
funktionieren sofort, ohne weiteres:

- **EdelByte Mail Setup (Windows)** – der Client nutzt fest `mail.edelbyte.ch`.
- **Apple-Profil** – wird nach Login auf `mail.edelbyte.ch` erzeugt.
- **Webmail**, **manuelle Einrichtung** (`/mail-setup/manuell`).

## 2. DNS für xyz.ch (beim DNS-Anbieter der Domain)

**Mail-Grundlagen (Pflicht):**

| Typ | Name | Wert |
|---|---|---|
| MX | `xyz.ch` | `10 mail.edelbyte.ch.` |
| TXT (SPF) | `xyz.ch` | `v=spf1 mx ip4:81.221.36.169 -all` |
| TXT (DKIM) | `dkim._domainkey.xyz.ch` | Schlüssel aus Mailcow (pro Domain eigener) |
| TXT (DMARC) | `_dmarc.xyz.ch` | `v=DMARC1; p=reject; rua=mailto:alert@edelbyte.ch` |

**Automatische Erkennung in Outlook – der saubere Weg (empfohlen):**

| Typ | Name | Wert |
|---|---|---|
| SRV | `_autodiscover._tcp.xyz.ch` | `0 0 443 mail.edelbyte.ch.` |

Damit verbindet sich Outlook zu `mail.edelbyte.ch` (gültiges Zertifikat) und holt
sich die Einstellungen. **Kein eigenes Zertifikat für xyz.ch nötig, keine
Server-Änderung.** Genau so laufen edelbyte.ch, social-wall.ch, eventshot.ch.

**Optional für Thunderbird-Autoerkennung:**

| Typ | Name | Wert |
|---|---|---|
| CNAME | `autoconfig.xyz.ch` | `mail.edelbyte.ch.` |

Nachteil: Thunderbird ruft `https://autoconfig.xyz.ch/…` auf und erwartet ein
Zertifikat für **autoconfig.xyz.ch**. Dafür braucht Traefik einen Router pro
Domain (siehe 3). Wer das nicht will: Thunderbird-Kunden nutzen die manuelle
Seite – dieselben Werte, einmal eingetragen.

## 3. Nur falls A-Records statt SRV gewünscht (Server-Änderung)

Zeigt eine Domain `autodiscover.xyz.ch` per **A-Record** direkt auf den Server
(statt SRV), braucht Traefik einen Router **plus Let's-Encrypt-Zertifikat** für
diesen Namen – sonst kommt das Traefik-Default-Zertifikat und Outlook bricht ab
(genau der Fehler, der bei `huterauto.ch` aktuell besteht).

In `/opt/mailcow-dockerized/docker-compose.override.yml` je Hostname zwei Router
ergänzen (Traefik v3: `Host()` nimmt genau **ein** Argument):

```yaml
      - "traefik.http.routers.mailcow-ad-xyz-web.rule=Host(`autodiscover.xyz.ch`)"
      - "traefik.http.routers.mailcow-ad-xyz-web.entrypoints=web"
      - "traefik.http.routers.mailcow-ad-xyz-web.service=mailcow"
      - "traefik.http.routers.mailcow-ad-xyz.rule=Host(`autodiscover.xyz.ch`)"
      - "traefik.http.routers.mailcow-ad-xyz.entrypoints=websecure"
      - "traefik.http.routers.mailcow-ad-xyz.tls.certresolver=letsencrypt"
      - "traefik.http.routers.mailcow-ad-xyz.service=mailcow"
```

danach `docker compose up -d nginx-mailcow`. mailcow-nginx bedient
`autodiscover.*`/`autoconfig.*` bereits hostunabhängig; nur Traefik braucht den
konkreten Namen für das Zertifikat. **Der SRV-Weg (2) erspart diesen Schritt.**

## Zusammenfassung

| Baustein | Neue Domain automatisch? |
|---|---|
| Windows-Client, Apple-Profil, Webmail, manuell | ✅ ohne Zusatz-DNS (nur MX fürs Mailrouting) |
| Empfang/Versand, Kalender, Kontakte | ✅ über mail.edelbyte.ch |
| Outlook-Autoerkennung | ✅ mit 1 SRV-Record pro Domain |
| Thunderbird-Autoerkennung | ⚠️ autoconfig-CNAME + Zertifikat, oder manuell |

Es gibt keinen server- oder codeseitigen Weg, Autodiscover **ganz ohne**
per-Domain-DNS zu machen – das Protokoll fragt zwingend die Kundendomain ab. Der
SRV-Record ist der geringste Aufwand (ein Eintrag, kein Zertifikat).
