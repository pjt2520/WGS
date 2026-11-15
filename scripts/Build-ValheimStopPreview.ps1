#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
# Fixed location in this checkout. Never publish into the installed service.
$out = Join-Path $repo ('releases\ValheimStopPreview-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-ValheimSafeStop.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Synthetic regression tests failed. Preview was not published.' }
foreach ($project in @('WGS', 'WGS.ServiceHost')) {
    $dest = Join-Path $out $project
    & dotnet publish (Join-Path $repo "$project\$project.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o $dest
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    if (-not (Test-Path (Join-Path $dest 'WGS.ValheimControl.exe'))) { throw 'Console helper missing from published preview.' }
}
Write-Host "Preview built at $out" -ForegroundColor Green
Write-Host 'No Windows service, managed installation, watchdog or game was changed.'
Write-Host 'Read docs/TANGERANG-VALHEIM-SAFE-STOP.md before a supervised game test.'
