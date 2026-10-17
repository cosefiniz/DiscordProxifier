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

## If Windows Security or your antivirus blocks it

**Why it happens.** To make Discord's built-in updater use the proxy, the app places a small `version.dll` into Discord's folder, and Discord loads it at startup (DLL hijacking with [Microsoft Detours](https://github.com/microsoft/Detours) hooks, from [aiqinxuancai/discord-proxy](https://github.com/aiqinxuancai/discord-proxy)). Malware uses the same technique, so antivirus software may flag or quarantine `DiscordProxifier.exe`, the files in `Data\`, or `version.dll` inside Discord's folder. The exe is also unsigned, so SmartScreen may warn on first launch.

**Symptoms:** the exe or files in `Data\` disappear after extracting, Windows Security reports a threat, the app shows `DP-103` / `DP-502`, or Discord hangs on "Starting" again.

**What to do, in this order:**

1. **Make sure your copy is genuine.** Only use the zip from this repo's [Releases](../../releases/latest) page. Don't add exclusions for a copy you got from anywhere else.
2. **Restore the file.** Windows Security → Virus & threat protection → Protection history → open the entry → Actions → **Allow on device** (or **Restore**).
3. **Exclude only this app.** Windows Security → Virus & threat protection → *Virus & threat protection settings* → Manage settings → Exclusions → Add or remove exclusions → Add an exclusion:
   - **Folder:** the folder you extracted the zip into, e.g. `C:\Tools\DiscordProxifier-v2.0.2`
   - **Process:** `DiscordProxifier.exe`
   - Only if `version.dll` keeps being removed from Discord: **File** → `%LOCALAPPDATA%\Discord\app-<version>\version.dll` (the newest `app-*` folder).

   Or, in PowerShell run as administrator (adjust the path):
   ```powershell
   Add-MpPreference -ExclusionPath "C:\Tools\DiscordProxifier-v2.0.2"
   Add-MpPreference -ExclusionProcess "DiscordProxifier.exe"
   ```
   With another antivirus, use its "exclusions" / "trusted applications" setting for the same folder and process.
4. **Report the false positive** so it gets fixed for everyone: [Microsoft file submission](https://www.microsoft.com/en-us/wdsi/filesubmission) (choose "Incorrectly detected as malware").

Don't turn your antivirus off. Excluding just this folder is enough. Remove the exclusion if you stop using the app.

## Windows Güvenliği veya antivirüs engellerse

**Neden oluyor?** Uygulama, Discord'un kendi güncelleyicisinin de proxy'yi kullanabilmesi için Discord klasörüne küçük bir `version.dll` koyuyor; Discord açılırken bu dosyayı yüklüyor. Bu yöntem [aiqinxuancai/discord-proxy](https://github.com/aiqinxuancai/discord-proxy) projesinden geliyor; teknik adı DLL hijacking'dir ve [Microsoft Detours](https://github.com/microsoft/Detours) kancaları kullanır. Zararlı yazılımlar da aynı tekniği kullandığı için antivirüsler şunları işaretleyebilir ya da karantinaya alabilir:
- `DiscordProxifier.exe`,
- `Data\` klasöründeki dosyalar,
- Discord klasöründeki `version.dll`.

Exe dijital olarak imzalı olmadığı için SmartScreen de ilk açılışta uyarı gösterebilir.

**Belirtiler:**
- Zip'ten çıkardıktan sonra exe ya da `Data\` içindeki dosyalar kayboluyor.
- Windows Güvenliği bir tehdit bildiriyor.
- Uygulama `DP-103` / `DP-502` hatası veriyor.
- Discord yine "Starting" ekranında takılıyor.

**Sırasıyla yapın:**

1. **Kopyanızın orijinal olduğundan emin olun.** Yalnızca bu reponun [Releases](../../releases/latest) sayfasındaki zip'i kullanın. Başka bir yerden gelen kopya için dışlama eklemeyin.
2. **Dosyayı geri yükleyin.** Windows Güvenliği → Virüs ve tehdit koruması → Koruma geçmişi → kaydı açın → Eylemler → **Cihazda izin ver** (ya da **Geri yükle**).
3. **Yalnızca bu uygulamayı dışlayın.** Windows Güvenliği → Virüs ve tehdit koruması → *Virüs ve tehdit koruması ayarları* → Ayarları yönet → Dışlamalar → Dışlama ekle veya kaldır → Dışlama ekle:
   - **Klasör:** zip'i çıkardığınız klasör, örneğin `C:\Tools\DiscordProxifier-v2.0.2`
   - **İşlem:** `DiscordProxifier.exe`
   - Yalnızca `version.dll` Discord klasöründen silinmeye devam ederse: **Dosya** → `%LOCALAPPDATA%\Discord\app-<sürüm>\version.dll` (en yeni `app-*` klasörü).

   Ya da yönetici olarak açılmış PowerShell'de (yolu kendinize göre düzeltin):
   ```powershell
   Add-MpPreference -ExclusionPath "C:\Tools\DiscordProxifier-v2.0.2"
   Add-MpPreference -ExclusionProcess "DiscordProxifier.exe"
   ```
   Başka bir antivirüs kullanıyorsanız, aynı klasör ve işlem için onun "dışlamalar" ya da "güvenilen uygulamalar" ayarını kullanın.
4. **Yanlış alarmı bildirin**; düzeltilirse herkes için çözülür: [Microsoft dosya gönderimi](https://www.microsoft.com/en-us/wdsi/filesubmission) ("Incorrectly detected as malware" seçeneği).

Antivirüsünüzü kapatmayın; yalnızca bu klasörü dışlamak yeterli. Uygulamayı kullanmayı bıraktığınızda dışlamayı kaldırın.

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
