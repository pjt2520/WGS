# Start the permanent Tangerang WGS application, without touching the
# disposable TANGERANG_WGS_DEV sandbox.
# The script belongs in the root of the managed installation:
#   <Root>\Start-Tangerang.ps1
$ErrorActionPreference = 'Stop'
$homeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $homeDir 'App\WindowsGameServer.exe'

if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "The Tangerang application was not found: $exe"
}
if (-not (Test-Path -LiteralPath (Join-Path $homeDir 'Data') -PathType Container)) {
    throw 'Missing managed Data directory. Do not create a blank world by accident; run Install-Tangerang.ps1 first.'
}

$previousHome = $env:TANGERANG_WGS_HOME
$previousDev = $env:TANGERANG_WGS_DEV
try {
    # These variables are inherited by the child process. They do NOT change
    # machine-wide or user-wide environment variables.
    $env:TANGERANG_WGS_HOME = $homeDir
    Remove-Item Env:TANGERANG_WGS_DEV -ErrorAction SilentlyContinue

    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -ErrorAction Stop
}
finally {
    if ($null -eq $previousHome) {
        Remove-Item Env:TANGERANG_WGS_HOME -ErrorAction SilentlyContinue
    } else {
        $env:TANGERANG_WGS_HOME = $previousHome
    }
    if ($null -eq $previousDev) {
        Remove-Item Env:TANGERANG_WGS_DEV -ErrorAction SilentlyContinue
    } else {
        $env:TANGERANG_WGS_DEV = $previousDev
    }
}
