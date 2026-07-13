# WatchDB Smart Organizer for Jellyfin

WatchDB turns release-like orphan episodes into a safe, Jellyfin-compatible library tree. It does the work automatically: no manual rename and no manual move.

For example, it can recognize both:

```text
Rooster - S01E01 - Épisode 1 2160p.DV.HDR.x265-Amen.mkv
Georgie.and.Mandys.First.Marriage.S02E09.1080p.x265-ELiTE/
Georgie.and.Mandys.First.Marriage.S02E10.1080p.x265-ELiTE/
```

and create this *without moving either original*:

```text
/media/Jellyfin-Series/
  /Rooster (2026) [tmdbid-...]/Season 01/Rooster - S01E01 - Épisode 1 ...mkv -> original file
  /Georgie & Mandy's First Marriage (2024) [tmdbid-...]/Season 02/
    /Georgie.and.Mandys.First.Marriage.S02E09....mkv -> original file
    /Georgie.and.Mandys.First.Marriage.S02E10....mkv -> original file
```

Jellyfin scans only `/media/Jellyfin-Series`. It sees a conventional series/season layout and therefore groups every confirmed episode beneath one series card.

## Automatic workflow

Once configured, the normal flow is fully automatic:

1. Your download/import tool drops a release in one of the configured source folders.
2. Jellyfin completes its normal library scan and metadata work.
3. WatchDB runs as an `ILibraryPostScanTask`, parses orphan episode files and release-folder names, then asks TMDb to validate the series and the exact `SxxExx`.
4. A high-confidence match becomes a symbolic link in the clean destination tree. A low-confidence or ambiguous match remains untouched.
5. If WatchDB created links, it queues one Jellyfin library scan so the new episodes appear without user action.

The dashboard task remains available as a one-click repair/retry action. It processes the same workflow; it is not a file-by-file chore.

## Safety model

- The initial default is **Dry run**: it creates no files and logs every decision.
- A match must be confirmed by TMDb: the selected series must contain the parsed `SxxExx`.
- The title score must exceed the configured threshold and be distinctly better than the runner-up.
- The first active mode creates symbolic links only; source files are never renamed, moved, or deleted.
- Existing target paths are never overwritten.
- Source and destination folders cannot overlap, and WatchDB refuses destination symbolic links/reparse points.
- Reparse points in source trees are skipped; WatchDB will not follow an arbitrary linked directory.
- One run is limited to a configured number of files (500 by default), and concurrent runs are skipped.
- The TMDb token is never written to logs and is not inserted back into the settings page after saving.

## Install and configure

This project targets Jellyfin **10.11.11** and .NET SDK 9.0. The Jellyfin plugin API is version-sensitive: change the two Jellyfin package versions in `Jellyfin.Plugin.WatchDB.csproj` to exactly match the server version when necessary.

```bash
dotnet publish Jellyfin.Plugin.WatchDB/Jellyfin.Plugin.WatchDB.csproj --configuration Release
```

Copy the generated `Jellyfin.Plugin.WatchDB.dll` into a dedicated folder in Jellyfin's plugin directory, restart the server, then configure **WatchDB Smart Organizer** from the dashboard.

1. Enter one or more source/inbox directories, one per line. Do not add these folders directly to the Jellyfin TV library.
2. Enter a separate destination directory, mounted read/write in the Jellyfin container or server process. Add this folder to the Jellyfin TV library.
3. Create a TMDb API **Read Access Token** and expose it to the Jellyfin process as `WATCHDB_TMDB_TOKEN` (recommended). The settings page also accepts a local fallback token, but environment variables avoid storing the secret in the plugin configuration.
4. Leave the plugin in **Simulation** mode and run `WatchDB — Organiser les épisodes orphelins` from Scheduled Tasks.
5. Review the task log. If the results look right, switch to **Créer des liens symboliques**. From then on, WatchDB runs automatically after Jellyfin scans and requests one follow-up scan whenever it creates new links.

If Jellyfin runs in Docker, the source directories and destination directory must be mounted inside the container; a host path by itself is not visible to the plugin.

## Install from Jellyfin

After the first GitHub release has completed, add this custom repository once in **Dashboard → Plugins → Repositories**:

- **Name:** `WatchDB`
- **Repository URL:** `https://raw.githubusercontent.com/Becausebreast77/WatchDB/main/manifest.json`

The WatchDB plugin will then appear in the plugin catalog. Install it there and restart Jellyfin when requested. Do not use the GitHub repository URL or the GitHub Release URL in this field: Jellyfin expects the JSON manifest URL.

## Publish a release

Maintainers publish a version from the **Actions → Release WatchDB → Run workflow** page on the `main` branch. Enter a four-part version such as `0.1.0.0`. The workflow builds and tests the plugin, creates the plugin zip, creates the GitHub Release, calculates its MD5 checksum, and updates `manifest.json` automatically.

## Scope of the first version

The first version deliberately handles the problem that Jellyfin misses: release-style single episodes and one-release-per-folder layouts. It does not alter existing Jellyfin metadata or merge arbitrary already-indexed library items. That keeps its actions reversible and auditable.
