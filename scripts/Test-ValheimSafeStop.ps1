#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$oldDev = $env:TANGERANG_WGS_DEV
$oldHome = $env:TANGERANG_WGS_HOME
try {
    # Must be set before the test CLR loads ConfigService's static fields.
    $env:TANGERANG_WGS_DEV = '1'
    Remove-Item Env:TANGERANG_WGS_HOME -ErrorAction SilentlyContinue
    foreach ($project in @('WGS.ValheimControl.Tests', 'WGS.ValheimLifecycle.Tests')) {
        & dotnet build (Join-Path $repo "$project\$project.csproj") --configuration Debug
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
        $framework = if ($project -eq 'WGS.ValheimControl.Tests') { 'net10.0' } else { 'net10.0-windows' }
        & (Join-Path $repo "$project\bin\Debug\$framework\$project.exe")
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
    }
    Write-Host 'Valheim console isolation and lifecycle tests passed. Only synthetic processes were used.' -ForegroundColor Green
} finally {
    $env:TANGERANG_WGS_DEV = $oldDev
    $env:TANGERANG_WGS_HOME = $oldHome
}
