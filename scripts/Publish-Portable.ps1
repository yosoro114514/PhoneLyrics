param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/portable'))
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$targetExe = Join-Path $OutputDirectory 'PhoneLyrics.exe'
if (Get-Process PhoneLyrics -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe }) {
    throw 'Exit PhoneLyrics from the tray before publishing here, or select a different -OutputDirectory.'
}
& dotnet publish (Join-Path $projectRoot 'src/PhoneLyrics/PhoneLyrics.csproj') -c Release -p:PublishProfile=Portable -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }
$executable = Get-Item (Join-Path $OutputDirectory 'PhoneLyrics.exe')
Write-Host ('Ready: {0} ({1:N1} MiB)' -f $executable.FullName, ($executable.Length / 1MB))
Get-FileHash -LiteralPath $executable.FullName -Algorithm SHA256
