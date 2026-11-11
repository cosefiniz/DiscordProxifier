# Regenerates dist/manifest.json from the files in dist/.
# The app reads this file from GitHub and only installs downloads whose SHA256 matches.
# Run it after changing anything in dist/, then commit and push.
#
# -App: also publish the freshly built exe (bin\Release) as the self-update for this version.
#       Its download URL is the GitHub release asset, so create the release (with the exe
#       attached) before pushing the manifest. Without -App the previous exe entry is kept.
param([switch]$App)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'

# Owner/repo/branch come from the app itself so they can't drift apart.
$updater = Get-Content (Join-Path $root 'DiscordProxifier\Core\Updater.cs') -Raw
$owner = [regex]::Match($updater, 'Owner = "([^"]+)"').Groups[1].Value
$repo = [regex]::Match($updater, 'Repo = "([^"]+)"').Groups[1].Value
$asmInfo = Get-Content (Join-Path $root 'DiscordProxifier\Properties\AssemblyInfo.cs') -Raw
$appVersion = ([version][regex]::Match($asmInfo, 'AssemblyVersion\("([^"]+)"\)').Groups[1].Value).ToString(3)

# Versions of the bundled tools that are not detected automatically.
$old = $null
$manifestPath = Join-Path $dist 'manifest.json'
if (Test-Path $manifestPath) { $old = Get-Content $manifestPath -Raw | ConvertFrom-Json }
function OldVersion($key, $default) {
    if ($old -and $old.files.$key) { return $old.files.$key.version }
    return $default
}

# Tor version: ask the tor.exe inside Tor.zip.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("dp-manifest-" + [guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null
try {
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $dist 'Tor.zip'))
    [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry('tor.exe'), (Join-Path $tmp 'tor.exe'))
    $zip.Dispose()
    $torVersion = [regex]::Match((& (Join-Path $tmp 'tor.exe') --version | Out-String), 'Tor version (\d+(\.\d+)+)').Groups[1].Value
} finally {
    Remove-Item $tmp -Recurse -Force
}

$dllVersion = (Get-Content (Join-Path $dist 'DllVer') -Raw).Trim()

$entries = [ordered]@{
    'Tor.zip'         = $torVersion
    'gost.exe'        = (OldVersion 'gost.exe' '2.12.0')
    'wgcf.exe'        = (OldVersion 'wgcf.exe' '2')
    'wireproxy.exe'   = (OldVersion 'wireproxy.exe' '1')
    'x64/version.dll' = $dllVersion
    'x86/version.dll' = $dllVersion
}

$files = [ordered]@{}
foreach ($key in $entries.Keys) {
    $path = Join-Path $dist ($key -replace '/', '\')
    $files[$key] = [ordered]@{
        version = $entries[$key]
        sha256  = (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
        size    = (Get-Item $path).Length
        path    = 'dist/' + $key
    }
}

if ($App) {
    $exe = Join-Path $root 'DiscordProxifier\bin\Release\DiscordProxifier.exe'
    $exeVersion = ([version](Get-Item $exe).VersionInfo.FileVersion).ToString(3)
    if ($exeVersion -ne $appVersion) { throw "Built exe is $exeVersion but AssemblyInfo says $appVersion; rebuild first" }
    $files['DiscordProxifier.exe'] = [ordered]@{
        version = $appVersion
        sha256  = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
        size    = (Get-Item $exe).Length
        url     = "https://github.com/$owner/$repo/releases/download/v$appVersion/DiscordProxifier.exe"
    }
} elseif ($old -and $old.files.'DiscordProxifier.exe') {
    $files['DiscordProxifier.exe'] = $old.files.'DiscordProxifier.exe'
    $appVersion = $old.appVersion   # keep announcing the published version, not a local build
} elseif ($old) {
    $appVersion = $old.appVersion
}

$manifest = [ordered]@{
    appVersion  = $appVersion
    appDownload = "https://github.com/$owner/$repo/releases/latest"
    files       = $files
}
$json = $manifest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($manifestPath, $json + "`n", (New-Object Text.UTF8Encoding $false))
Write-Host "dist/manifest.json written (app $appVersion, Tor $torVersion, version.dll $dllVersion)"
