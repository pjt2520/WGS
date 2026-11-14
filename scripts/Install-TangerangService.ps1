#requires -Version 5.1
<#
.SYNOPSIS
    Stage the Tangerang ServiceHost preview or register it for MANUAL testing.
.DESCRIPTION
    By default this only publishes ServiceHost into the managed installation.
    -Register additionally updates the detachable GUI, creates a Windows
    service that initially has MANUAL startup, configures the shared local
    bearer token, and enables Service Mode in WGS.

    IMPORTANT: Windows service registration requires an elevated PowerShell.
    It does not start the service, enable any game's AutoStart, erase a world,
    configure Windows startup, or change your Discord bot credentials.
.EXAMPLE
    .\scripts\Install-TangerangService.ps1
.EXAMPLE
    # Only after reviewing the printed paths, run PowerShell as Administrator:
    .\scripts\Install-TangerangService.ps1 -Register
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path $env:USERPROFILE 'TangerangWGS'),
    [switch]$Register
)
$ErrorActionPreference = 'Stop'
$serviceName = 'TangerangWGSHost'
$Root = [IO.Path]::GetFullPath($Root)
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (-not (Test-Path -LiteralPath (Join-Path $Root 'Data\settings.json') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $Root 'Data\servers.json') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $Root 'Servers') -PathType Container)) {
    throw "Managed Tangerang WGS installation missing under $Root. Install the desktop manager first."
}
if ($Root.TrimEnd('\') -eq [IO.Path]::GetPathRoot($Root).TrimEnd('\')) {
    throw 'Refusing to deploy into a drive root.'
}
$installed = Join-Path $Root 'ServiceHost'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$unique = [guid]::NewGuid().ToString('N').Substring(0,8)
$stage = Join-Path $Root ".service-staging-$stamp-$unique"

$registered = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($registered) {
    throw "Windows service $serviceName already exists. Do not replace an installed engine without a separate reviewed upgrade/rollback plan."
}

if ($Register) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Registration requires PowerShell Run as Administrator. Staging alone does not.'
    }
    $active = @(Get-Process -Name 'WindowsGameServer','valheim_server' -ErrorAction SilentlyContinue)
    if ($active.Count -gt 0) {
        throw 'Close Tangerang WGS and stop Valheim before registering the service. No changes made.'
    }
    # Update the desktop to the EXACT same source revision as this ServiceHost,
    # before switching its configuration to remote service mode. Otherwise an
    # older GUI could try to become a second process owner.
    $desktopInstaller = Join-Path $PSScriptRoot 'Install-Tangerang.ps1'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $desktopInstaller -Root $Root
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed. No Windows service registered.' }
}

try {
    Write-Host 'Publishing self-contained Tangerang ServiceHost (Windows x64)...' -ForegroundColor Cyan
    & dotnet publish (Join-Path $repo 'WGS.ServiceHost\WGS.ServiceHost.csproj') --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugSymbols=false -p:DebugType=none --output $stage
    if ($LASTEXITCODE -ne 0) { throw 'ServiceHost publish failed. No service registered.' }
    $publishedExe = Join-Path $stage 'WGS.ServiceHost.exe'
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
        throw "Missing published ServiceHost exe: $publishedExe"
    }
    $prior = $null
    if (Test-Path -LiteralPath $installed) {
        $prior = Join-Path $Root "ServiceHost.rollback-$stamp-$unique"
        Move-Item -LiteralPath $installed -Destination $prior
    }
    try { Move-Item -LiteralPath $stage -Destination $installed }
    catch {
        if ($prior -and -not (Test-Path -LiteralPath $installed)) {
            Move-Item -LiteralPath $prior -Destination $installed
        }
        throw
    }
    Write-Host "Service host staged at $installed" -ForegroundColor Green
    if ($prior) { Write-Host "Previous binaries preserved at $prior" }

    if ($Register) {
        $settingsFile = Join-Path $Root 'Data\settings.json'
        $backupDir = Join-Path $Root 'Backups\Installer'
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        $backupSettings = Join-Path $backupDir "settings-before-service-$stamp-$unique.json"
        Copy-Item -LiteralPath $settingsFile -Destination $backupSettings -ErrorAction Stop

        $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
        $token = [string]$settings.WebApiToken
        if ([string]::IsNullOrWhiteSpace($token) -or $token.Length -lt 32) {
            # Cryptographically random 256-bit token. Do not echo it to the
            # terminal, put it on the command line or commit it to Git.
            $bytes = New-Object byte[] 32
            $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
            try { $generator.GetBytes($bytes) }
            finally { $generator.Dispose() }
            $token = [BitConverter]::ToString($bytes).Replace('-', '')
        }

        foreach ($pair in @(
            @('WebApiToken', $token),
            @('ServiceModeToken', $token),
            @('ServiceModeUrl', 'http://localhost:8766'),
            @('ServiceModeEnabled', $true),
            @('WebApiEnabled', $false),
            @('SlaveMode', $false),
            @('EnableUPnP', $false)
        )) {
            $settings | Add-Member -MemberType NoteProperty -Name $pair[0] -Value $pair[1] -Force
        }
        $tempSettings = "$settingsFile.staging-$unique"
        $settings | ConvertTo-Json -Depth 100 |
            Set-Content -LiteralPath $tempSettings -Encoding UTF8
        try { [IO.File]::Replace($tempSettings, $settingsFile, $null) }
        catch {
            if (Test-Path -LiteralPath $tempSettings) {
                Remove-Item -LiteralPath $tempSettings -Force
            }
            throw
        }

        # Never run a LocalSystem service using binaries writable by a
        # normal user. Copy the *compiled* engine to Program Files under
        # administrator control. Durable Data/Servers remain under Root.
        $created = $false
        try {
            $protectedEngine = Join-Path $env:ProgramFiles 'TangerangWGS\Engine'
        if (Test-Path -LiteralPath $protectedEngine) {
            throw "Protected service binary location already exists: $protectedEngine. Review it before registration."
        }
        New-Item -ItemType Directory -Path $protectedEngine -Force | Out-Null
        foreach ($binary in (Get-ChildItem -LiteralPath $installed -Force)) {
            Copy-Item -LiteralPath $binary.FullName -Destination $protectedEngine -Recurse -Force -ErrorAction Stop
        }
        $serviceExe = Join-Path $protectedEngine 'WGS.ServiceHost.exe'
        if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
            throw "Protected engine publish missing: $serviceExe"
        }
        $binaryPath = '"' + $serviceExe + '" --tangerang-home "' + $Root + '"'

            New-Service -Name $serviceName -BinaryPathName $binaryPath -DisplayName 'Tangerang WGS Engine' -StartupType Manual -ErrorAction Stop | Out-Null
            $created = $true

            # LocalService is significantly less privileged than SYSTEM.
            # Avoid storing a password or granting administrator rights.
            & sc.exe config $serviceName obj= 'NT AUTHORITY\LocalService' password= '' | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not configure LocalService identity.' }
            & sc.exe sidtype $serviceName unrestricted | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not enable isolated Windows service SID.' }

            $serviceSid = 'NT SERVICE\' + $serviceName
            $rootGrant = $serviceSid + ':(RX)'
            $modifyGrant = $serviceSid + ':(OI)(CI)M'
            & icacls.exe $Root '/grant' $rootGrant | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not grant service access to managed root.' }

            foreach ($dir in @('Data', 'Servers', 'Backups', 'Logs')) {
                $target = Join-Path $Root $dir
                New-Item -ItemType Directory -Path $target -Force | Out-Null
                & icacls.exe $target '/grant' $modifyGrant '/T' | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "Could not grant service write access to $target"
                }
            }
        }
        catch {
            if ($created) {
                & sc.exe delete $serviceName | Out-Null
                Write-Warning 'Registration failed; service deletion requested. Review with Get-Service.'
            }
            # Service registration is the commit point. A failure must not
            # strand WGS in remote mode with no engine to connect to.
            Copy-Item -LiteralPath $backupSettings -Destination $settingsFile -Force
            Write-Warning "Original WGS settings restored from: $backupSettings"
            throw
        }
        Write-Host "Protected engine binary: $serviceExe"
        Write-Host 'Windows service account: NT AUTHORITY\LocalService'

        # Configure crash recovery only; do not enable Windows startup yet.
        # Recovery settings do not restart the actual game when this service
        # is intentionally stopped through the Service Control Manager.
        & sc.exe failure $serviceName reset= 86400 actions= "restart/60000/restart/120000/restart/300000" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'Windows service registered but SCM recovery setup failed. Review manually.'
        }
        Write-Host 'Tangerang WGS Engine registered for MANUAL startup only.' -ForegroundColor Green
        Write-Host 'No game was started and no Windows automatic startup was enabled.'
        Write-Host 'Start only for a supervised trial: Start-Service TangerangWGSHost'
        Write-Host "Local status API: http://localhost:8766 (requires the configured bearer token)."
        Write-Host "Settings backup: $backupSettings"
    }
    else {
        Write-Host 'Stage-only complete. No Windows service installed; no settings changed.'
        Write-Host 'Review docs/TANGERANG-SERVICE-FOUNDATION.md before using -Register.'
    }
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
