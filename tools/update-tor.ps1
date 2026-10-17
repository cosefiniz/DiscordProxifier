# Fetches the newest Tor Expert Bundle (Windows x86_64) from dist.torproject.org,
# verifies it against Tor Project's published SHA256 list, repacks it into dist/Tor.zip
# (tor.exe + PluggableTransports\*) and refreshes dist/manifest.json.
# Afterwards: commit + push, and users get it via Settings → Update Tor.
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$base = 'https://dist.torproject.org/torbrowser/'

$versions = [regex]::Matches((Invoke-WebRequest $base -UseBasicParsing).Content, 'href="(\d+\.\d+(\.\d+)?)/"') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object { [version]$_ } -Descending
$version = $null
foreach ($v in $versions) {
    $listing = (Invoke-WebRequest "$base$v/" -UseBasicParsing).Content
    if ($listing -match "tor-expert-bundle-windows-x86_64-$([regex]::Escape($v))\.tar\.gz") { $version = $v; break }
}
if (-not $version) { throw 'No Windows expert bundle found' }
$file = "tor-expert-bundle-windows-x86_64-$version.tar.gz"
Write-Host "Latest Tor Expert Bundle: $version"

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("dp-tor-" + [guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null
try {
    Invoke-WebRequest "$base$version/$file" -OutFile (Join-Path $tmp $file) -UseBasicParsing
    $sums = (Invoke-WebRequest "$base$version/sha256sums-signed-build.txt" -UseBasicParsing).Content
    $expected = [regex]::Match($sums, "([0-9a-f]{64})\s+$([regex]::Escape($file))").Groups[1].Value
    $actual = (Get-FileHash (Join-Path $tmp $file) -Algorithm SHA256).Hash.ToLower()
    if (-not $expected -or $expected -ne $actual) { throw "SHA256 mismatch: expected $expected, got $actual" }
    Write-Host "SHA256 verified: $actual"

    Push-Location $tmp
    tar -xzf $file
    Pop-Location

    $stage = Join-Path $tmp 'stage'
    New-Item -ItemType Directory (Join-Path $stage 'PluggableTransports') -Force | Out-Null
    Copy-Item (Join-Path $tmp 'tor\tor.exe') $stage
    Get-ChildItem (Join-Path $tmp 'tor\pluggable_transports') -Filter *.exe | Copy-Item -Destination (Join-Path $stage 'PluggableTransports')

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $out = Join-Path $dist 'Tor.zip'
    if (Test-Path $out) { Remove-Item $out }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $out, [IO.Compression.CompressionLevel]::Optimal, $false)
} finally {
    Remove-Item $tmp -Recurse -Force
}

& (Join-Path $PSScriptRoot 'update-manifest.ps1')
Write-Host "dist/Tor.zip updated to $version. Commit and push to publish."
