# Tangerang WGS Engine: Detachable Service Foundation

> **PREVIEW.** Windows build, unit tests and a controlled local smoke test are required before
> installing a service on your actual hosting PC. This PR does not remotely modify your PC.

## What changed

The WGS.ServiceHost now runs as a separate headless Windows service.
Game processes belong to that process rather than the WPF management window.
The desktop uses the existing RemoteServerBackend HTTP API to send start/stop/
restart commands, and polls actual service state and live Valheim PlayFab codes.
Closing the desktop no longer shuts down service-owned game processes.

### Safeguards

- Require `--tangerang-home <absolute managed root>` when starting the service.
- Take an exclusive file lock on `Data/service-owner.lock` for the engine lifetime.
  A stale file after reboot is harmless; a live owner blocks any second engine.
- The desktop refuses to start in local mode when the service already owns
  that home, and never reattaches local PIDs, local-autostarts, or overwrites
  service-owned `servers.json` while configured in service mode.
- Service's API listens on **localhost:8766 only**, with a bearer token.
  It does not open a Windows Firewall port, expose the dashboard/status
  page or enable unauthenticated wake-from-web.
- A service token is provisioned by the installer using cryptographic
  randomness before the service launches. It is not printed into logs.
- Service auto-starts only game servers individually marked `AutoStart=true`.
  The existing Tangerang migration defaults these flags to false.
- Graceful game shutdown and existing backup-on-stop behavior run when
  intentionally stopping the Windows service. WGS process-exit cleanup is
  still a last-resort fallback, not a promise that abrupt power loss can save.
- Existing world files remain at `<Root>\Servers`, settings in `Data`,
  backups in `Backups`. The service binaries occupy `ServiceHost`.
- The desktop title bar shows 'Tangerang engine: connected' or retrying.
  An API disconnect never means 0 players or triggers a local restart.

### Known limitations before stable deployment

- **Discord Bot is still GUI-owned.** When the GUI closes, the bot goes
  offline even though the Valheim game process continues under the service.
  Migrating the bot to the service requires a reviewed credentials handoff:
  the existing bot token is protected with Windows DPAPI CurrentUser and
  cannot be decrypted by the LocalSystem service. Do not copy a plaintext
  bot token into a service config file or disable encryption.
- **Some server settings are read-only in service mode.** The desktop
  intentionally skips its 5-second writes to `servers.json` to avoid
  overwriting service-owned PID and settings. We still need a dedicated
  authenticated server-settings update API before calling this feature
  fully detachable in day-to-day configuration.
- The service uses LocalSystem in this preview. This is higher privilege
  than ideal; a future milestone should use a restricted service account
  and explicitly grant it access only to the durable Tangerang folders.
- Steam credentials protected with DPAPI CurrentUser cannot be decrypted
  by the LocalSystem service. Anonymous SteamCMD installs work normally;
  authenticated/paid game downloads need separate credential handling.
- WGS service mode is opt-in, and service may not be usable with every
  game-specific UI command until those commands use IServerBackend.
- When you choose to stop the Windows service itself, it will try to
  gracefully stop the game servers. Only closing/reopening the WPF window
  is designed to leave them online.

## Safe workflow

First, ensure the managed desktop build from
`docs/TANGERANG-MANAGED-INSTALL.md` is installed and works with the world
before moving on to service tests.

**Stage-only, no elevation or registration:**

```powershell
Set-Location 'C:\Dev\TangerangWGS'
git fetch origin
git switch --track origin/feature/tangerang-service-foundation
dotnet build '.\WindowsGameServer.sln' -c Debug
dotnet run --project '.\WGS.ManagedInstall.Tests\WGS.ManagedInstall.Tests.csproj' -c Debug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\scripts\Install-TangerangService.ps1'
```

This publishes a test service executable to `<Root>\ServiceHost`.
It **does not** register a Windows service or modify any configuration.

**Before live registration,** stop Tangerang Test, fully exit the WGS
desktop, verify `Get-Process WindowsGameServer,valheim_server` returns
nothing, and take a separate offline backup of your Data/Servers/Backups.

From **Administrator PowerShell** (after reviewing the script):

```powershell
Set-Location 'C:\Dev\TangerangWGS'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\scripts\Install-TangerangService.ps1' -Register
```

The installer updates the desktop to the same commit, backs up `settings.json`,
configures `ServiceModeEnabled=true`, provisions its token, and registers
`TangerangWGSHost` as **Manual** startup. **It does not start any game.**

**Supervised trial only, once you're ready:**

```powershell
Start-Service TangerangWGSHost
Get-Service TangerangWGSHost
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\TangerangWGS\Start-Tangerang.ps1"
```

Check that the desktop shows **connected** to the headless engine.
Then click **Start** on the disposable Tangerang Test server; observe the
fresh six-digit PlayFab join code. Test from Xbox only after confirming
it is the migrated test world.

Close only the desktop window and confirm the Valheim process and service
remain running. Reopen the desktop, confirm connection and unchanged world.
Finally, stop the test Valheim normally through the UI.

### Crash recovery and Windows startup

`Install-TangerangService.ps1 -Register` sets **manual** service startup
and preconfigures SCM crash recovery. It does not enable automatic startup.
After the full supervised test, `sc.exe config TangerangWGSHost start= auto`
can enable launching the ENGINE before a user logs in. To start a game at
boot, also opt that specific server into AutoStart through an approved
config change while the engine is stopped. Do not enable either blindly.

### Rollback

Stop game servers gracefully from the GUI. Then, in Administrator
PowerShell, after confirming no game server is still running:

```powershell
Stop-Service TangerangWGSHost
sc.exe config TangerangWGSHost start= demand
```

Restore `Data/settings.json` from `Backups\Installer\settings-before-service-*`,
**only while WGS and the service are both stopped**, to return
`ServiceModeEnabled` to its prior value. Keep all game worlds in `Servers`.
Only after a successful rollback is it appropriate to delete the service
using `sc.exe delete TangerangWGSHost`. Never delete the `Data`, `Servers`,
or `Backups` folders as part of a service uninstall.

## CI and release status

The GitHub Actions workflow validates all existing health, crossplay,
Announcement Center and managed-install tests plus ownership and status
parsing checks. It publishes a self-contained ServiceHost preview zip.
No stable GitHub Release or unattended deployment is created until
these live checks pass.
