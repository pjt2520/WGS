# Launch a *separate development UI* without touching the installed WGS.
# Only the Debug build produced by this checkout is allowed.
# Do not configure the development UI to manage live Valheim/Palworld servers.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot 'WGS\bin\Debug\net10.0-windows\WindowsGameServer.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'Development executable missing. First run: dotnet build .\WindowsGameServer.sln --configuration Debug'
}

# ConfigService uses Tangerang_DevData instead of WGS_Data when enabled.
# App.xaml.cs also uses an independent per-machine mutex for this mode.
$oldValue = $env:TANGERANG_WGS_DEV
$env:TANGERANG_WGS_DEV = '1'
Write-Host 'Launching isolated Tangerang WGS UI.' -ForegroundColor Cyan
Write-Host 'No development server should point at your live world.' -ForegroundColor Yellow
try {
    & $exe
} finally {
    if ($null -eq $oldValue) { Remove-Item Env:TANGERANG_WGS_DEV -ErrorAction SilentlyContinue }
    else { $env:TANGERANG_WGS_DEV = $oldValue }
}
