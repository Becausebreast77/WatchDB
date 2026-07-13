# Security policy

## Supported versions

Security fixes are applied to the latest release line only. Do not publish a release built against a different Jellyfin API version without updating and testing its package references.

## Reporting a vulnerability

Do not open a public issue for a vulnerability that could expose a Jellyfin server, media paths, or a TMDb token. Until a private reporting channel is configured for the public repository, contact the repository owner privately and include:

- affected WatchDB version and Jellyfin version;
- reproducible steps using fake paths and fake tokens;
- impact and any suggested mitigation.

## Security boundaries

WatchDB runs with the filesystem permissions of the Jellyfin process. It must therefore be given only:

- read access to explicitly configured source folders;
- read/write access to one dedicated destination folder;
- no access to the Jellyfin configuration, plugin, or host system folders beyond what Jellyfin itself requires.

The destination must not be writable by untrusted users or download clients. A user able to write there can race filesystem operations or replace links.

TMDb traffic is HTTPS-only. The TMDb Read Access Token is a local server secret: pass it through the Jellyfin process environment (`WATCHDB_TMDB_TOKEN`) where possible; never commit it, paste it into screenshots, or include it in support logs.
