# sm0k3r

A Windows tool to pin your Steam client to a specific version and install [SteamTools](https://steamtools.net/) DLLs. Prevents Steam from auto-updating to incompatible versions.

## What it does

1. **Pins Steam to a known-good version** -- downloads the target client packages, runs Steam in update mode against a local file server to apply the exact version, then writes `steam.cfg` to block future auto-updates.
2. **Installs SteamTools DLLs** -- downloads `xinput1_4.dll` and `dwmapi.dll` into your Steam directory, sets the required registry keys, and verifies hashes against known-good values.
3. **Works in both directions** -- can update or downgrade Steam depending on whether your current version is older or newer than the target.

## Usage

Download `sm0k3r.exe` from the [latest release](https://github.com/Selectively11/sm0k3r/releases/latest) and run it. No installation needed -- it's a single self-contained executable.

The tool auto-detects your Steam installation from the registry and shows a context-aware menu:

```
=== sm0k3r v0.1.1 ===

Remote config loaded (target version: 1773426488)
Steam install path: C:\Program Files (x86)\Steam
Current Steam client version: 1773426488
Steam is up to date.
SteamTools is up to date
SteamTools is compatible with this version of Steam!
Steam updates are blocked

Select an option:
  1) Verify installation
  2) Reinstall Steam at current version
  3) Install SteamTools
  0) Exit
```

- **Option 1** runs the full flow: pins Steam to the target version and installs SteamTools. Skips steps that are already current.
- **Option 2** applies the target Steam version (update, downgrade, or reinstall depending on your current version).
- **Option 3** installs or updates SteamTools DLLs only.

No games or saves are affected -- only the Steam client binaries and configuration are changed.

## How it works

The downgrade/update mechanism:

1. Kills Steam and clears the `package/` directory
2. Downloads the target client manifest and all package files from Valve's CDN
3. Starts a local HTTP file server on port 1666
4. Launches Steam with `-forcesteamupdate -forcepackagedownload -overridepackageurl http://127.0.0.1:1666/ -exitsteam`
5. Steam applies whatever version is served, regardless of direction
6. Writes `steam.cfg` with `BootStrapperInhibitAll=enable` to prevent auto-updates
7. Clears `appcache/` to prevent update triggers on next launch

## Version management

The target Steam version and SteamTools hashes are served from the [`config` release](https://github.com/Selectively11/sm0k3r/releases/tag/config) on this repo. GitHub Actions check for new Steam versions hourly and SteamTools hash changes daily.

New Steam versions go through a manual approval process -- the Action creates a PR rather than auto-updating, so SteamTools compatibility can be tested before users receive the update.

## Building from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.

```
dotnet publish Sm0k3r.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true
```

Output: `bin/Release/net8.0-windows/win-x64/publish/sm0k3r.exe`

Run tests:

```
dotnet test
```
