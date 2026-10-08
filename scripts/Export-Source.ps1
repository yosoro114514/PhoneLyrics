param([string]$OutputFile = (Join-Path $PSScriptRoot '../artifacts/PhoneLyrics-source.zip'))
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputFile = [IO.Path]::GetFullPath($OutputFile)
$files = @(& git -C $projectRoot -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0) { throw 'Initialize a local Git repository before exporting source.' }
if ($files.Count -eq 0) { throw 'No source files found.' }

# A tracked file bypasses .gitignore; refuse local records even if accidentally staged.
$private = @($files | Where-Object {
    $_ -match '^(artifacts|diagnostics|verification|references|local-only)/|(^|/)(bin|obj)/|overlay-settings\.json$|\.(jsonl|log)$' -or
    $_ -match '(^|/)\.env($|\.)' -and $_ -notmatch '\.env\.example$'
})
if ($private.Count -gt 0) { throw 'Source selection includes local data. Untrack it before exporting.' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputFile)) | Out-Null
$stream = [IO.File]::Create($OutputFile)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $files) {
        $source = [IO.Path]::GetFullPath((Join-Path $projectRoot $relative))
        if (!$source.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Source path is outside the project.'
        }
        if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Source export does not follow filesystem links.'
        }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $source,
            ('PhoneLyrics/' + $relative.Replace('\', '/')), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose(); $stream.Dispose() }
Write-Host ("Source: {0}; files: {1}" -f $OutputFile, $files.Count)
Get-FileHash -LiteralPath $OutputFile -Algorithm SHA256
