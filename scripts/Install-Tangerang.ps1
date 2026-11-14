#requires -Version 5.1
<#
.SYNOPSIS
    Build and install Tangerang WGS with persistent server worlds.
.EXAMPLE
    .\scripts\Install-Tangerang.ps1 -MigrateDev -CreateDesktopShortcut
.EXAMPLE
    .\scripts\Install-Tangerang.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path $env:USERPROFILE 'TangerangWGS'),
    [switch]$MigrateDev,
    [switch]$CreateDesktopShortcut
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$Root = [IO.Path]::GetFullPath($Root)
$rootPrefix = $Root.TrimEnd('\') + '\'
$repoPrefix = $repo.TrimEnd('\') + '\'
if ($rootPrefix.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $repoPrefix.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Install Root must be outside the source repository and must not contain it.'
}
if ($Root.TrimEnd('\') -eq [IO.Path]::GetPathRoot($Root).TrimEnd('\')) {
    throw 'Install Root cannot be an entire drive.'
}

$debugBase = Join-Path $repo 'WGS\bin\Debug\net10.0-windows'
$devData = Join-Path $debugBase 'Tangerang_DevData'
$devServers = Join-Path $debugBase 'Tangerang_DevServers'
$devBackups = Join-Path $debugBase 'Tangerang_DevBackups'
$installData = Join-Path $Root 'Data'
$installServers = Join-Path $Root 'Servers'
$installBackups = Join-Path $Root 'Backups'
$installedApp = Join-Path $Root 'App'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$unique = [guid]::NewGuid().ToString('N').Substring(0,8)
$appStage = Join-Path $Root ".app-staging-$stamp-$unique"
$copyStages = @()

# Refuse to take an inconsistent copy of the world's files or overwrite
# application binaries while WGS or any Valheim server is still running.
$active = @(Get-Process -Name 'WindowsGameServer','valheim_server' -ErrorAction SilentlyContinue)
if ($active.Count -gt 0) {
    $summary = ($active | ForEach-Object { "$($_.ProcessName) PID $($_.Id)" }) -join ', '
    throw "Close WGS and stop Valheim before installing/migrating: $summary"
}
if ($MigrateDev) {
    if (-not (Test-Path -LiteralPath (Join-Path $devData 'servers.json'))) {
        throw "Development settings not found: $devData\servers.json. Nothing migrated."
    }
    if (-not (Test-Path -LiteralPath $devServers -PathType Container)) {
        throw "Development server files not found: $devServers. Nothing migrated."
    }
    foreach ($dest in @($installData, $installServers, $installBackups)) {
        if (Test-Path -LiteralPath $dest) {
            throw "Refusing to overwrite durable data at $dest. To update only the app, omit -MigrateDev."
        }
    }
}
function Copy-Tree([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return }
    foreach ($item in (Get-ChildItem -LiteralPath $Source -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force -ErrorAction Stop
    }
    $srcFiles = @(Get-ChildItem -LiteralPath $Source -Recurse -File -Force)
    $dstFiles = @(Get-ChildItem -LiteralPath $Destination -Recurse -File -Force)
    $srcBytes = ($srcFiles | Measure-Object Length -Sum).Sum
    $dstBytes = ($dstFiles | Measure-Object Length -Sum).Sum
    if ($srcFiles.Count -ne $dstFiles.Count -or $srcBytes -ne $dstBytes) {
        throw "Copy validation failed for $Source. Original files remain unchanged."
    }
}
function Set-JsonProperty([object]$Object, [string]$Name, [object]$Value) {
    $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value -Force
}
New-Item -ItemType Directory -Path $Root -Force | Out-Null
try {
    Write-Host 'Publishing self-contained Windows x64 Tangerang WGS...' -ForegroundColor Cyan
    $project = Join-Path $repo 'WGS\WGS.csproj'
    & dotnet publish $project --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugSymbols=false -p:DebugType=none --output $appStage
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed. Nothing has been installed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $appStage 'WindowsGameServer.exe'))) {
        throw 'Publish did not produce WindowsGameServer.exe.'
    }

    if ($MigrateDev) {
        Write-Host 'Copying development worlds and settings to durable folders...' -ForegroundColor Cyan
        $stageData = Join-Path $Root ".data-staging-$unique"
        $stageServers = Join-Path $Root ".servers-staging-$unique"
        $stageBackups = Join-Path $Root ".backups-staging-$unique"
        $copyStages = @($stageData, $stageServers, $stageBackups)
        Copy-Tree $devData $stageData
        Copy-Tree $devServers $stageServers
        Copy-Tree $devBackups $stageBackups

        # Only rewrite documented path properties; never blindly search and
        # replace source strings that could also appear inside user messages.
        $settingsFile = Join-Path $stageData 'settings.json'
        if (Test-Path -LiteralPath $settingsFile) {
            $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
            Set-JsonProperty $settings 'DefaultInstallRoot' $installServers
            Set-JsonProperty $settings 'BackupPath' $installBackups
            Set-JsonProperty $settings 'WebApiEnabled' $false
            Set-JsonProperty $settings 'SlaveMode' $false
            Set-JsonProperty $settings 'ServiceModeEnabled' $false
            Set-JsonProperty $settings 'EnableUPnP' $false
            $settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $settingsFile -Encoding UTF8
        }

        $serversFile = Join-Path $stageData 'servers.json'
        $servers = @(Get-Content -LiteralPath $serversFile -Raw | ConvertFrom-Json)
        $devPrefix = $devServers.TrimEnd('\') + '\'
        foreach ($server in $servers) {
            if ([string]::IsNullOrWhiteSpace($server.InstallPath)) {
                throw "Server $($server.DisplayName) has no install path. Migration stopped."
            }
            $old = [IO.Path]::GetFullPath([string]$server.InstallPath)
            if (-not $old.StartsWith($devPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Server $($server.DisplayName) resides outside dev servers ($old). Review before migrating."
            }
            $relative = $old.Substring($devPrefix.Length)
            if (-not (Test-Path -LiteralPath (Join-Path $stageServers $relative))) {
                throw "Missing copied game files: $relative"
            }
            Set-JsonProperty $server 'InstallPath' (Join-Path $installServers $relative)
            Set-JsonProperty $server 'RunningPid' 0
            Set-JsonProperty $server 'AutoStart' $false
            Set-JsonProperty $server 'DiscordAnnouncementsEnabled' $false
            Set-JsonProperty $server 'DiscordAdminControls' $false
            Write-Host "Prepared: $($server.DisplayName)" -ForegroundColor Green
        }
        ConvertTo-Json -InputObject $servers -Depth 100 | Set-Content -LiteralPath $serversFile -Encoding UTF8

        # DPAPI-encrypted secrets continue working for the same Windows user.
        # First boot starts with bot DISABLED; manually turn it back on after
        # validating channels and Admin User IDs in the new installation.
        $noticesFile = Join-Path $stageData 'notifications.json'
        if (Test-Path -LiteralPath $noticesFile) {
            $notices = Get-Content -LiteralPath $noticesFile -Raw | ConvertFrom-Json
            Set-JsonProperty $notices 'BotEnabled' $false
            $notices | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $noticesFile -Encoding UTF8
        }
        Move-Item -LiteralPath $stageData -Destination $installData
        Move-Item -LiteralPath $stageServers -Destination $installServers
        Move-Item -LiteralPath $stageBackups -Destination $installBackups
        Write-Host 'Migration copy complete. Development originals remain untouched.' -ForegroundColor Green
    }
    else {
        foreach ($path in @($installData, $installServers, $installBackups)) {
            New-Item -ItemType Directory -Path $path -Force | Out-Null
        }
    }

    # Only replace App. Never replace or delete Data, Servers or Backups.
    $oldApp = $null
    if (Test-Path -LiteralPath $installedApp) {
        $oldApp = Join-Path $Root "App.rollback-$stamp-$unique"
        Move-Item -LiteralPath $installedApp -Destination $oldApp
    }
    try {
        Move-Item -LiteralPath $appStage -Destination $installedApp
    }
    catch {
        if ($oldApp -and -not (Test-Path -LiteralPath $installedApp)) {
            Move-Item -LiteralPath $oldApp -Destination $installedApp
        }
        throw
    }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start-TangerangManaged.ps1') -Destination (Join-Path $Root 'Start-Tangerang.ps1') -Force
    if ($CreateDesktopShortcut) {
        $desktop = [Environment]::GetFolderPath('Desktop')
        $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $desktop 'Tangerang WGS.lnk'))
        $lnk.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
        $lnk.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $Root 'Start-Tangerang.ps1') + '"'
        $lnk.WorkingDirectory = $Root
        $lnk.IconLocation = (Join-Path $installedApp 'WindowsGameServer.exe') + ',0'
        $lnk.Save()
    }
    Write-Host ''
    Write-Host 'Tangerang WGS installed. Game worlds are outside the replaceable App folder.' -ForegroundColor Green
    Write-Host "Application: $installedApp"
    Write-Host "Server worlds: $installServers"
    Write-Host "Settings: $installData"
    Write-Host "Backups: $installBackups"
    if ($oldApp) { Write-Host "Previous application retained: $oldApp" }
    # Users with AllSigned/Restricted policy cannot invoke a .ps1 via
    # PowerShell's "&" operator. This matches the desktop shortcut's
    # per-process bypass and does not change global policy.
    Write-Host ("Start with: powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"" +
        (Join-Path $Root 'Start-Tangerang.ps1') + "`"")
    Write-Host 'Or double-click the Tangerang WGS desktop shortcut.'
}
finally {
    # Remove only uncommitted temporary directories. Never delete worlds,
    # durable data or rollback app folders after a failed installation.
    foreach ($temp in (@($appStage) + $copyStages)) {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
