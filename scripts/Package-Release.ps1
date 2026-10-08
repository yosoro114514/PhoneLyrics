param(
    [string]$PortableDirectory = (Join-Path $PSScriptRoot '../artifacts/portable'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/release')
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$PortableDirectory = [IO.Path]::GetFullPath($PortableDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/PhoneLyrics/PhoneLyrics.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric three-part project version.' }
$sourceExe = Join-Path $PortableDirectory 'PhoneLyrics.exe'
$info = (Get-Item -LiteralPath $sourceExe).VersionInfo
# The .NET SDK appends +<commit SHA> after a repository has a commit.
$executableVersion = ([string]$info.ProductVersion -split '\+', 2)[0]
if ($info.ProductName -ne 'PhoneLyrics' -or $executableVersion -ne $version) {
    throw 'Publish the current PhoneLyrics project before packaging; executable version/product does not match.'
}

# Refuse a console app or another architecture; its dependent DLLs would be missing.
$reader = [IO.BinaryReader]::new([IO.File]::OpenRead($sourceExe))
try {
    $reader.BaseStream.Position = 0x3c
    $peOffset = $reader.ReadInt32()
    $reader.BaseStream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) {
        throw 'Expected a Windows x64 PE executable.'
    }
    $reader.BaseStream.Position = $peOffset + 24 + 68
    if ($reader.ReadUInt16() -ne 2) { throw 'Expected the portable Windows GUI executable, not console diagnostics.' }
} finally { $reader.Dispose() }

$packageName = "PhoneLyrics-v$version-win-x64"
$notes = Join-Path $projectRoot "docs/releases/v$version.md"
$files = @(
    @{ source = $sourceExe; entry = 'PhoneLyrics.exe' },
    @{ source = (Join-Path $projectRoot 'docs/PORTABLE.md'); entry = '使用说明.md' },
    @{ source = $notes; entry = 'RELEASE_NOTES.md' },
    @{ source = (Join-Path $projectRoot 'LICENSE'); entry = 'LICENSE' },
    @{ source = (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md'); entry = 'THIRD_PARTY_NOTICES.md' },
    @{ source = (Join-Path $projectRoot 'licenses/README.md'); entry = 'licenses/README.md' },
    @{ source = (Join-Path $projectRoot 'licenses/dotnet-runtime-LICENSE.txt'); entry = 'licenses/dotnet-runtime-LICENSE.txt' },
    @{ source = (Join-Path $projectRoot 'licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt'); entry = 'licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt' },
    @{ source = (Join-Path $projectRoot 'licenses/windowsdesktop-runtime-LICENSE.txt'); entry = 'licenses/windowsdesktop-runtime-LICENSE.txt' },
    @{ source = (Join-Path $projectRoot 'licenses/cswinrt-LICENSE.txt'); entry = 'licenses/cswinrt-LICENSE.txt' },
    @{ source = (Join-Path $projectRoot 'licenses/windows-sdk-LICENSE.rtf'); entry = 'licenses/windows-sdk-LICENSE.rtf' },
    @{ source = (Join-Path $projectRoot 'licenses/windows-sdk-LICENSE.txt'); entry = 'licenses/windows-sdk-LICENSE.txt' }
)
foreach ($file in $files) {
    if (!(Test-Path -LiteralPath $file.source -PathType Leaf)) { throw "Missing release input: $($file.source)" }
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$outputExe = Join-Path $OutputDirectory "$packageName.exe"
if (Get-Process PhoneLyrics, $packageName -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $outputExe }) {
    throw 'Exit the release executable before replacing it, or choose another output directory.'
}
if ($sourceExe -ne $outputExe) { Copy-Item -LiteralPath $sourceExe -Destination $outputExe }
$exeHash = (Get-FileHash -LiteralPath $outputExe -Algorithm SHA256).Hash.ToLowerInvariant()
$zipPath = Join-Path $OutputDirectory "$packageName.zip"
$stream = [IO.File]::Create($zipPath)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    # Explicit files only: never sweep logs, settings or personal captures into a release.
    foreach ($file in $files) {
        $compression = if ($file.entry -eq 'PhoneLyrics.exe') { [IO.Compression.CompressionLevel]::NoCompression } else { [IO.Compression.CompressionLevel]::Optimal }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.source,
            "$packageName/$($file.entry)", $compression) | Out-Null
    }
    $checksumEntry = $archive.CreateEntry("$packageName/SHA256SUMS.txt")
    $writer = [IO.StreamWriter]::new($checksumEntry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.WriteLine("$exeHash  PhoneLyrics.exe") } finally { $writer.Dispose() }
} finally { $archive.Dispose(); $stream.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksums = "$exeHash  $packageName.exe`n$zipHash  $packageName.zip`n"
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
Write-Host "Release package: $zipPath"
Write-Host "Standalone executable: $outputExe"
Write-Host $checksums
