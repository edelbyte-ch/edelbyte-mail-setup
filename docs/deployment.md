# Deployment

## Website

Teil des bestehenden Repos `edelbyte-ch/edelbyte`, Branch `main`. Push auf `main`
löst den Dokploy-Build der App `frontend` (`edelbyte-frontend-yq8x7x`) aus und
deployt `edelbyte.ch`. Kein separates Setup-Projekt, kein Staging.

Vor dem Push lokal: `pnpm run build` (enthält Geo-Sync-Check), `npx tsc --noEmit`
(bis auf den bekannten `calendar.tsx`-Fehler grün).

## Windows-Client

Eigenes Repo `edelbyte-ch/edelbyte-mail-setup`.

- Push/PR → `.github/workflows/build.yml`: restore, build, test.
- Tag `vX.Y.Z` → `.github/workflows/release.yml`: test, publish (self-contained
  Single-File), SHA-256, GitHub-Release mit EXE + `.sha256`.

Die Website liest die neueste Veröffentlichung live über die GitHub-Release-API
(`src/lib/mail-setup-release.ts`, stundenweise gecacht). Neue Version = neuer
Tag; die Website zieht sie automatisch, ohne eigenen Deploy.
