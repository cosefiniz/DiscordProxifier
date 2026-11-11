# Builds the Release exe and packs the distributable zip into out/:
#   DiscordProxifier-v<version>/DiscordProxifier.exe (+ .config), LICENSE, Data/<files from dist>
# Personal files (Config.json, Data/Warp) are never included. Upload the zip to a GitHub Release.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'

& $msbuild (Join-Path $root 'DiscordProxifier.sln') -t:restore -p:RestorePackagesConfig=true -v:minimal -nologo
& $msbuild (Join-Path $root 'DiscordProxifier.sln') -p:Configuration=Release -v:minimal -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$bin = Join-Path $root 'DiscordProxifier\bin\Release'
$version = (Get-Item (Join-Path $bin 'DiscordProxifier.exe')).VersionInfo.FileVersion -replace '\.0$', ''
$name = "DiscordProxifier-v$version"
$out = Join-Path $root 'out'
$stage = Join-Path $out $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory (Join-Path $stage 'Data') -Force | Out-Null

Copy-Item (Join-Path $bin 'DiscordProxifier.exe'), (Join-Path $bin 'DiscordProxifier.exe.config') $stage
Copy-Item (Join-Path $root 'LICENSE') $stage
foreach ($f in 'Tor.zip', 'gost.exe', 'wgcf.exe', 'wireproxy.exe', 'DllVer', 'x64\version.dll', 'x86\version.dll') {
    $dst = Join-Path $stage "Data\$f"
    New-Item -ItemType Directory (Split-Path $dst) -Force | Out-Null
    Copy-Item (Join-Path $root "dist\$f") $dst
}

. (Join-Path $PSScriptRoot 'zip-helper.ps1')
$zip = Join-Path $out "$name.zip"
New-PortableZip $stage $zip "$name/"
Remove-Item $stage -Recurse -Force
# The bare exe is attached to the release as well: the app's self-update downloads it.
Copy-Item (Join-Path $bin 'DiscordProxifier.exe') (Join-Path $out 'DiscordProxifier.exe') -Force
Write-Host "Package: $zip"
Write-Host "Self-update exe: $(Join-Path $out 'DiscordProxifier.exe')"
