# Tangerang WGS: Persistent Server Manager Preview

This is the production-layout *preview* for the customized WGS fork.
The announcement, PlayFab, Xbox/PS5-compatible vanilla Valheim tracking,
server health, and Discord work remain on development branches until merged.

## Layout

The default root is `%USERPROFILE%\TangerangWGS` (no administrator rights needed).
It contains:

| Location | Purpose | Safe to replace during app update? |
|---|---|---|
| `App\` | Published WindowsGameServer.exe and runtime dependencies | Yes, after WGS is closed |
| `Data\` | servers.json, notifications.json, settings.json, SteamCMD and bot message IDs | **No** |
| `Servers\` | Valheim and future game executables, world saves and configs | **No** |
| `Backups\` | Game backups | **No** |
| `Logs\` | Tangerang app crash logs | Do not overwrite |
| `Start-Tangerang.ps1` | Sets managed environment for that single process | Replace only through installer |

Both a development sandbox and a managed preview may exist on one PC, but
**do not run both while they point to the same ports or Discord bot.**

## First-time promotion of the existing Tangerang Test world

1. In the WGS developer sandbox, **stop Tangerang Test** and let any save
   finish. Close WGS completely (not minimize to tray).
2. Make sure no WindowsGameServer.exe or valheim_server.exe process remains.
3. Open PowerShell from the clone `C:\Dev\TangerangWGS`.
4. Fetch the new branch, build, and run its regression tests:

```powershell
Set-Location 'C:\Dev\TangerangWGS'
git fetch origin
git switch --track origin/feature/tangerang-managed-install
dotnet build '.\WindowsGameServer.sln' --configuration Debug
dotnet run --project '.\WGS.ManagedInstall.Tests\WGS.ManagedInstall.Tests.csproj' --configuration Debug
```

5. Copy, validate and install the manager:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\scripts\Install-Tangerang.ps1' -MigrateDev -CreateDesktopShortcut
```

6. Run the generated desktop shortcut, or:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\TangerangWGS\Start-Tangerang.ps1"
```

This syntax starts a new PowerShell process and is compatible with a
local `Restricted` or `AllSigned` script policy, without permanently changing
the user's execution policy. The installed desktop shortcut uses the same
command. If an enforced MachinePolicy or UserPolicy blocks bypass, consult
the policy administrator rather than weakening a managed security setting.

7. Verify Tangerang Test appears once, its Install Path points to
   `%USERPROFILE%\TangerangWGS\Servers\valheim\Tangerang Test`,
   and the existing world loads correctly. Existing **dev files are left
   unchanged** so there is a fallback.
8. Verify its Discord control channel is private, and Admin User IDs are
   correct; only then re-enable the Discord bot. We turn BotEnabled OFF in
   the copied settings and disable server announcements/admin buttons at
   first launch to avoid duplicate/disruptive notifications.
9. Start the server from the managed preview, wait for new PlayFab code
   registration, and confirm a known Xbox player can join. Check the
   Discord status board after opting in.
10. Only after validation, decide whether to enable **Start with Windows**
    inside WGS Settings. That option now points to the persistent launcher.

### Migration safety

- Copies existing `Tangerang_DevData`, `Tangerang_DevServers` and optional
  `Tangerang_DevBackups`; it does not delete or move the original files.
- Validates file count and byte totals before rewriting saved paths.
- Rewrites known `DefaultInstallRoot`, `BackupPath`, `InstallPath` fields only.
- Clears cached process IDs and AutoStart flags so the migrated copy
  cannot accidentally reattach to the old development process.
- Keeps existing Discord tokens encrypted for the SAME Windows user via
  Windows DPAPI. Moving to another user/computer requires reconnecting.
- Refuses to overwrite an existing Data/Servers/Backups folder when using
  `-MigrateDev`; prevents repeated migrations from erasing live worlds.
- If a server's install directory points outside the known dev tree,
  migration refuses to guess what to copy. Review the error first.
- Builds under the source checkout but installs the compiled app and durable
  state completely outside the source tree.

## Upgrade after the first migration

Close the running managed WGS (stopping servers gracefully) and run:

```powershell
Set-Location 'C:\Dev\TangerangWGS'
git pull --ff-only
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\scripts\Install-Tangerang.ps1'
```

**Do NOT use `-MigrateDev` for later updates.** The installer swaps only
the `App` directory and retains the previous app as `App.rollback-*`.
`Data`, `Servers`, and `Backups` are neither deleted nor replaced.

Built-in **upstream WGS auto-update is deliberately disabled** for managed
installs: that updater knows how to replace one upstream executable, not a
complete Tangerang app bundle. Future Tangerang releases will have a
separate update process with checksums and backups.

## If the managed build has a problem

1. Stop the game normally and close WGS.
2. Keep `Data`, `Servers` and `Backups` untouched.
3. Rename `App` to `App.failed`, rename the most recent `App.rollback-*`
   folder to `App`, then run `Start-Tangerang.ps1`.
4. For first-install issues, the ORIGINAL dev server/world still exists
   beneath the source checkout's Debug folder; do not run both copies.

**Important:** this preview operates as an ordinary desktop program.
If Windows logs off or the application exits, WGS's existing shutdown
logic stops managed game servers. Running unattended as a Windows service
is a separate milestone; service mode is not enabled by this migration.

## CI packaging and GitHub releases

GitHub Actions builds the Windows win-x64 manager, validates all four
test suites and PowerShell script syntax, publishes a self-contained
preview, and uploads a SHA-256-checksummed zip as a workflow artifact.
This is **not yet** a public stable GitHub Release or automatic installer.
Creating a GitHub pre-release requires an explicit tag and release step
after live migration testing.

### Next milestones

1. Validate managed app launch + migrated world + discord in real use.
2. Test backup/restore and safe app update/rollback.
3. Add supervised logon startup or ServiceHost hardening and test survival
   when the desktop UI is closed, if unattended hosting is desired.
4. Package and tag an official Tangerang pre-release.
