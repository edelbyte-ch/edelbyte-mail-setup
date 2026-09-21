# Mailcow-Abhängigkeiten

Der Server (`mail.edelbyte.ch`, Mailcow Docker Compose) bleibt Backend und wird
vom Setup nicht verändert. Relevante, öffentliche Endpunkte:

| Zweck | Adresse | Port/Pfad |
|---|---|---|
| Empfang (IMAP) | mail.edelbyte.ch | 993, SSL/TLS |
| Versand (SMTP) | mail.edelbyte.ch | 465 SSL/TLS · 587 STARTTLS |
| Kalender/Kontakte | mail.edelbyte.ch | `/SOGo/dav/` (CalDAV/CardDAV) |
| Webmail | mail.edelbyte.ch | `/SOGo/` |
| Apple-Profil | mail.edelbyte.ch | `/index.php?mobileconfig` |
| Autodiscover (Outlook) | autodiscover.edelbyte.ch | `/autodiscover/autodiscover.xml` |
| Autoconfig (Thunderbird/Apple) | autoconfig.edelbyte.ch | `/mail/config-v1.1.xml` |

## Einmalige Server-Anpassung (21.09.2026)

`autodiscover.edelbyte.ch` und `autoconfig.edelbyte.ch` hatten A-Records auf den
Server, aber keinen Traefik-Router – ausgeliefert wurde das Traefik-
Default-Zertifikat, Clients brachen mit Zertifikatsfehler ab. Ergänzt wurden je
Hostname Router in
`/opt/mailcow-dockerized/docker-compose.override.yml` (Traefik v3: `Host()`
nimmt genau ein Argument, daher ein Router je Name). mailcow-nginx bedient die
Pfade bereits hostunabhängig (`server_name … autodiscover.* autoconfig.*`).

Rollback: `docker-compose.override.yml` aus dem Backup
`docker-compose.override.yml.bak-YYYYMMDD-HHMM` zurückkopieren und
`docker compose up -d nginx-mailcow` ausführen. MX, SPF, DKIM, DMARC, IMAP, SMTP
blieben unberührt.
