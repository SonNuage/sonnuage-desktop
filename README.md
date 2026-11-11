# SonNuage desktop

**English** · [Français](README.fr.md)

Desktop app for [SonNuage](https://github.com/SonNuage/sonnuage), a self-hosted music server, for
Windows, macOS and Linux. The pages of the web client are shown in the system webview (WebView2 on Windows,
WKWebView on macOS, WebKitGTK on Linux) thanks to [Photino](https://www.tryphotino.io/). No embedded Chromium:
about 40 MB, .NET runtime included, and 120 MB of memory.

Status: works, used occasionally, so less tested than the web player and the Android app. See
[Known limits](#known-limits).

| Folder | Role |
|---|---|
| `src/SonNuage.Desktop` | App: Photino host, tray icon, storage of the accounts |
| `build/` | Packages per system (`package.sh`): Windows installer (`windows/`), `.deb` (`linux/`), macOS signing and entitlements (`macos/`), Info.plist, icon, Linux launcher |

The pages, the components and the player (`WebPlayer`) come from the `SonNuage.UI` library of the
[sonnuage](https://github.com/SonNuage/sonnuage) repository:

- **in CI**: NuGet package published by the CI of `sonnuage` (version in `Directory.Packages.props`)
- **in development**: if `sonnuage` is cloned next to this repository (`../sonnuage`), its sources are used
  directly (see `Directory.Build.props`, property `SonNuageRepo`).

To follow a new server version, raise `SonNuage.UI` in `Directory.Packages.props` to the published
version `X.Y.Z` (server tag). The `0.1.0-ci.N` builds of `main` do not stay in the registry: do not rely on
them for a version of the app. The `CompositionTests` test reports a new dependency that would be missing.

## Development

```bash
dotnet run --project src/SonNuage.Desktop
dotnet test
```

The tests (`tests/SonNuage.Desktop.Tests`) cover local storage (`DesktopStore`), the server address
(`AccountEndpoint`), the single instance, the startup entries of each system, updates (reading the
release, choosing the file, SHA-256 checksum) and the components shown by SonNuage.UI. The tray icon,
the window and the installers are tried by hand, on each system.

## How it works

- **Accounts**: on first launch, the app asks for the server address, then the login. Several servers
  and accounts, as on Android. Changing account stops playback and reloads the queue of the other account.
- **System tray**: closing the window hides it and the music goes on. The icon reopens it (click on Windows,
  menu elsewhere) and offers play/pause, next, previous and Quit. Without an icon (Linux without an indicator), closing
  the window quits the app.
- **Media keys** and system controls: through the webview's MediaSession API, as in a browser.
- **Single instance**: launching it again brings back the window of the one that is running. The first one holds `instance.lock` (data
  folder) and listens on a Unix socket `app.sock`; macOS, for its part, sends "reopen" to the app (Finder, Dock).
- **Launch with the session** (off by default) and **hidden start** (only the tray icon
  appears), in Settings → Desktop app. System entry: value `SonNuage` under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `~/Library/LaunchAgents/org.sonnuage.desktop.plist`,
  `~/.config/autostart/sonnuage.desktop`. The app launched this way receives `--autostart`.
- **Updates**: the latest GitLab release of this project is read 30 s after startup, then every 6 hours
  (address set at build time by the CI, `UPDATE_FEED`; no check in development nor for the
  `0.1.0-ci.N` builds). A banner offers the new version: the Windows installer runs silently and restarts
  the app, the macOS disk image opens in the Finder, the `.deb` in the software center. The file is checked against the
  release's `SHA256SUMS`. Archive (zip, tar.gz) or failure: the version's page opens in the browser. The
  GitLab project must be public (at least its releases and its package registry) for the app to reach it.
- **Data**: `%APPDATA%\SonNuage`, `~/Library/Application Support/SonNuage` or `~/.config/SonNuage`. Settings
  in `settings.json`. The session is in `session.json`, encrypted by DPAPI on Windows, readable by the
  user only on macOS and Linux.

## Packages

```bash
build/package.sh win-x64 0.1.0 dist
```

| System | RID | Packages |
|---|---|---|
| Windows | `win-x64` | `SonNuage-<version>-win-x64-setup.exe`: NSIS installer for the current user (no administrator rights, in `%LOCALAPPDATA%\Programs\SonNuage`, removable from Settings) · `SonNuage-<version>-win-x64.zip`: standalone `SonNuage.exe`. WebView2 is provided by Windows 10 and 11 |
| macOS | `osx-arm64`, `osx-x64` | `SonNuage-<version>-<rid>.dmg`: disk image, drag SonNuage into Applications · `SonNuage-<version>-<rid>.zip`: `SonNuage.app`. Unsigned without a certificate: first launch by right-click → Open (or System Settings → Privacy → Open Anyway) |
| Linux | `linux-x64` | `sonnuage_<version>_amd64.deb`: Debian, Ubuntu (in `/opt/sonnuage`, command `sonnuage`) · `SonNuage-<version>-linux-x64.tar.gz`: executable and `.desktop` launcher |

Tools: `zip`, `makensis` and `osslsigncode` (Windows), `dpkg-deb` (Linux), `xorriso` and `curl` (macOS), all on
Linux: the CI needs neither a Windows machine nor a Mac. The macOS image is an ISO 9660 with Rock Ridge (permissions and
link to Applications), which macOS opens like a `.dmg`, uncompressed.

### Signing

Each step runs only if its variables exist (GitLab: Settings → CI/CD → Variables, **protected** so that
they serve only `v*` tags, passwords **masked**). Without them, the packages are produced unsigned.

| System | Variables | Step |
|---|---|---|
| Windows | `WINDOWS_CERT_PFX` (File type, .pfx code-signing certificate), `WINDOWS_CERT_PASSWORD`, optional `WINDOWS_TIMESTAMP_URL` | `build/windows/sign.sh`: `SonNuage.exe` then the installer, with `osslsigncode` |
| macOS | `MACOS_CERT_P12` (File type, "Developer ID Application" and its key), `MACOS_CERT_PASSWORD` | `build/macos/sign.sh`: signing with hardened runtime (`build/macos/entitlements.plist`) by `rcodesign` 0.27.0, downloaded at that moment (`RCODESIGN_SHA256` to check the archive) |
| macOS | `APPLE_API_KEY_JSON` (File type, App Store Connect key converted by `rcodesign encode-app-store-connect-api-key`) | notarization and stapling of the ticket on `SonNuage.app`, before the `.dmg` |

No secret value is printed in the log: the passwords go through temporary files.

On Linux, `libwebkit2gtk-4.1-0` is needed and, for the icon, `libayatana-appindicator3-1` (GNOME: with the
AppIndicator extension).

## CI/CD

- **GitLab** ([.gitlab-ci.yml](.gitlab-ci.yml)): the tests and the four packages in parallel on each push, as artifacts.
  On `main`, semantic-release creates the `vX.Y.Z` tag and its release, to which the tag's pipeline attaches the packages. The project must be allowed in
  sonnuage → Settings → CI/CD → Job token permissions, to read the `SonNuage.UI` package.
- **GitHub Actions** ([.github/workflows/ci.yml](.github/workflows/ci.yml)): the same, with the sources of
  `sonnuage` checked out next to it. Private repository: secret `SONNUAGE_REPO_TOKEN`.

## Versions

Versions are published by [semantic-release](https://semantic-release.gitbook.io/) ([.releaserc.json](.releaserc.json)),
from the commit messages in the [Conventional Commits](https://www.conventionalcommits.org/en/) format:
`fix: …` gives a patch (1.0.1), `feat: …` a minor version (1.1.0), `feat!: …` or a
`BREAKING CHANGE:` footer a major version (2.0.0). `docs`, `chore`, `ci`, `refactor`, `test` publish nothing.
On `main`, the `semantic-release` job creates the `vX.Y.Z` tag and the GitLab release with its notes. The tag's
pipeline then publishes the version. It uses `GITLAB_TOKEN`, a token of the `sonnuage` group (masked and protected variable,
protected `v*` tags).

## Known limits

- macOS and Linux have not been tried on a real machine yet. Only Windows has.
- Preloading of the next tracks: it goes through `fetch` from the page (origin `http://localhost` on
  Windows, `app://localhost` elsewhere). The server allows it on the stream and the covers from its version
  1.25.0; with an older server, only playback works.
- "Desktop app" settings and update banner: shown by SonNuage.UI from 1.25.0 (declared
  by `DesktopPlatform.SettingsSections` and `Notices`). With 1.24.0, the app builds but does not show them.

## Licence

SonNuage is free software under the [GNU AGPL v3.0 or later](LICENSE) licence: anyone can use it,
study it, modify it and redistribute it. A modified version, including one offered as an online service, must
publish its source code under the same licence.

Copyright © 2026 pmj.bailly and the SonNuage contributors.
