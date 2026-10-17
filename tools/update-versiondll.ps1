# Pulls the latest version.dll release from aiqinxuancai/discord-proxy into dist/x64 and dist/x86
# and refreshes dist/manifest.json. Review the upstream release notes before publishing:
# whatever is pushed here is installed into every user's Discord automatically.
param([string]$Tag)  # optional, e.g. v0.0.13; default = latest release
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'

$api = 'https://api.github.com/repos/aiqinxuancai/discord-proxy/releases'
$release = if ($Tag) { Invoke-RestMethod "$api/tags/$Tag" } else { Invoke-RestMethod "$api/latest" }
Write-Host "discord-proxy release: $($release.tag_name) ($($release.published_at))"

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("dp-dll-" + [guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null
try {
    foreach ($arch in 'x64', 'x86') {
        $asset = $release.assets | Where-Object { $_.name -match $arch } | Select-Object -First 1
        if (-not $asset) { throw "No $arch asset in $($release.tag_name)" }
        $zip = Join-Path $tmp "$arch.zip"
        Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
        Expand-Archive $zip -DestinationPath (Join-Path $tmp $arch) -Force
        $dll = Get-ChildItem (Join-Path $tmp $arch) -Recurse -Filter version.dll | Select-Object -First 1
        if (-not $dll) { throw "version.dll not found in $($asset.name)" }
        New-Item -ItemType Directory (Join-Path $dist $arch) -Force | Out-Null
        Copy-Item $dll.FullName (Join-Path $dist "$arch\version.dll") -Force
    }
} finally {
    Remove-Item $tmp -Recurse -Force
}
[IO.File]::WriteAllText((Join-Path $dist 'DllVer'), $release.tag_name)

& (Join-Path $PSScriptRoot 'update-manifest.ps1')
Write-Host "version.dll updated to $($release.tag_name). Commit and push to publish."
