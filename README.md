# WatchDB Smart Organizer for Jellyfin

WatchDB repairs the grouping mistakes Jellyfin can make with release-style episode files. It works automatically after a library scan: no manual rename, no folder configuration, and no TMDb token to create.

For example, it can recognize both:

```text
Rooster - S01E01 - Épisode 1 2160p.DV.HDR.x265-Amen.mkv
Georgie.and.Mandys.First.Marriage.S02E09.1080p.x265-ELiTE/
Georgie.and.Mandys.First.Marriage.S02E10.1080p.x265-ELiTE/
```

It reads the **actual filename**, extracts the series title and `SxxExx`, then uses the metadata providers already configured in Jellyfin to locate the canonical series card. Display titles and embedded media titles are never used to infer the series name.

## Automatic workflow

Once configured, the normal flow is fully automatic:

1. Jellyfin scans your normal Films and Séries folders.
2. WatchDB runs after the scan.
3. It merges series cards which Jellyfin has already confirmed as the same TMDb series.
4. For an episode whose `SeasonId` is invalid, it parses the raw filename (for example `Cape.Fear.S01E01...`), finds one unambiguous existing series, and attaches the episode to its matching scanned season.
5. Jellyfin can then apply normal metadata, including the localized episode title.

The dashboard task **WatchDB — Organiser les épisodes orphelins** remains available as a one-click repair/retry action. It runs the same automatic workflow; it is not a file-by-file chore.

## Safety model

- WatchDB never renames, moves, links, deletes, or opens your media files.
- It only changes Jellyfin's internal parent links for an episode when the filename has a valid `SxxExx` pattern and the target series is unambiguous.
- It relies on the metadata providers you already enabled in Jellyfin; no API key or token is stored by WatchDB.
- It only attaches an episode to a season that Jellyfin has already scanned. If the canonical series or season is absent, WatchDB leaves the item untouched and records why in the server log.
- A run is protected from running concurrently.

## Install and configure

This project targets Jellyfin **10.11.11** and .NET SDK 9.0. The Jellyfin plugin API is version-sensitive: change the two Jellyfin package versions in `Jellyfin.Plugin.WatchDB.csproj` to exactly match the server version when necessary.

```bash
dotnet publish Jellyfin.Plugin.WatchDB/Jellyfin.Plugin.WatchDB.csproj --configuration Release
```

Copy the generated `Jellyfin.Plugin.WatchDB.dll` into a dedicated folder in Jellyfin's plugin directory and restart the server. There is no configuration to enter: enable the metadata providers you normally use in Jellyfin (for example TMDb) and let the next library scan complete.

## Install from Jellyfin

After the first GitHub release has completed, add this custom repository once in **Dashboard → Plugins → Repositories**:

- **Name:** `WatchDB`
- **Repository URL:** `https://raw.githubusercontent.com/Becausebreast77/WatchDB/main/manifest.json`

The WatchDB plugin will then appear in the plugin catalog. Install it there and restart Jellyfin when requested. Do not use the GitHub repository URL or the GitHub Release URL in this field: Jellyfin expects the JSON manifest URL.

## Publish a release

Maintainers publish a version from the **Actions → Release WatchDB → Run workflow** page on the `main` branch. Enter a new four-part version such as `0.2.3.0`. The workflow builds and tests the plugin, creates the plugin zip, creates the GitHub Release, calculates its MD5 checksum, and updates `manifest.json` automatically.

## Current scope

WatchDB deliberately does not create virtual series or season entries, because that would compete with Jellyfin's own scanner and metadata providers. It fixes safely identifiable links to series and seasons already present in the library, and logs the remaining cases for a later scan or improvement.
