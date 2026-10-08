param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/diagnostics'))
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
& dotnet publish (Join-Path $projectRoot 'src/PhoneLyrics/PhoneLyrics.csproj') -c Release -p:PublishProfile=Diagnostics -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }
Write-Host "Diagnostics: $OutputDirectory"
