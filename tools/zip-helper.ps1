# Creates a zip with forward-slash entry names (Windows PowerShell 5.1's
# ZipFile.CreateFromDirectory writes backslashes, which some unzip tools mishandle).
function New-PortableZip([string]$SourceDir, [string]$ZipPath, [string]$Prefix = '') {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (Test-Path $ZipPath) { Remove-Item $ZipPath }
    $src = (Resolve-Path $SourceDir).Path.TrimEnd('\')
    $zip = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $src -Recurse -File | ForEach-Object {
            $name = $Prefix + $_.FullName.Substring($src.Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally {
        $zip.Dispose()
    }
}
