# Discord Proxifier v2

Routes the Discord desktop app through **Tor** or **Cloudflare WARP** so it works where Discord is blocked. Voice, video and streaming keep going direct.

Based on [s0rp/DiscordProxifier](https://github.com/s0rp/DiscordProxifier), which uses the proxy method from [aiqinxuancai/discord-proxy](https://github.com/aiqinxuancai/discord-proxy).

## Use

1. Download the zip from [Releases](../../releases/latest) and extract it anywhere.
2. Run `DiscordProxifier.exe`, press **Connect**, then **Launch**.
3. Pick **Tor** or **WARP** with the switch. Some providers block WARP; Tor works almost everywhere.

The app connects automatically when it opens (it remembers the last route). Language: English or Türkçe in Settings.

## Kullanım (TR)

1. [Releases](../../releases/latest) sayfasından zip'i indirip bir klasöre çıkarın.
2. `DiscordProxifier.exe`'yi açın, **Connect**'e ve ardından **Launch**'a basın.
3. Anahtarla **Tor** veya **WARP** seçin. Bazı sağlayıcılar WARP'ı engeller; Tor neredeyse her yerde çalışır.

Uygulama açılınca son kullanılan yol ile otomatik bağlanır. Dil Ayarlar'dan Türkçe yapılabilir.

## Updates

The app reads [`dist/manifest.json`](dist/manifest.json) from this repo and installs files from `dist/` only when their SHA256 matches. version.dll updates automatically; Tor updates from **Settings → Update Tor**.

Maintainer scripts (PowerShell, in `tools/`):

| Script | Does |
|---|---|
| `update-tor.ps1` | Latest Tor Expert Bundle from dist.torproject.org, hash-checked, repacked into `dist/Tor.zip` |
| `update-versiondll.ps1` | Latest version.dll from aiqinxuancai/discord-proxy into `dist/x64`, `dist/x86` |
| `update-manifest.ps1` | Rebuilds `dist/manifest.json` (run by the two scripts above) |
| `build-package.ps1` | Builds the exe and the release zip into `out/` |

After an update script: commit and push. After a new app version: bump `AssemblyVersion`, run `update-manifest.ps1` and `build-package.ps1`, push, and attach the zip to a new GitHub Release.

## Build

Visual Studio 2022 or its MSBuild, .NET Framework 4.7.2. `dotnet build` does not work (COM reference).
