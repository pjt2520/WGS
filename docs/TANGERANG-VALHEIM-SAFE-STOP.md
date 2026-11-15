# Valheim safe-stop preview

Normal Stop previously reached Process.Kill because vanilla Valheim does not
provide a stdin stop command. The temporary local guard returned success even
when the game remained alive, allowing restart/update callers to continue.

This preview starts Valheim with its own hidden Windows console, captures stdout
and stderr through explicitly inherited pipes, and uses a short-lived separate
helper to attach to that console and send Ctrl+C. The tracked PID remains the
actual Valheim process, not a wrapper. No Valheim mods are required.

The helper verifies PID plus process creation time and refuses to signal a
console containing any process other than itself and the target. It does not
attach the WGS engine to a game console. CTRL_C_EVENT is broadcast only after
this isolation check; a PID is not used as an unsupported Ctrl+C group target.

Stop waits up to 120 seconds for exit. Failure throws to the backend, preventing
restart, update, and shutdown-backup continuation. The GUI's Restart path now
uses the backend directly so its Stop error handler cannot swallow the failure.
The process remains tracked if alive. Explicit Kill remains distinct. Implicit
engine/desktop exit cleanup does not kill Valheim after a refused/timed-out Stop.
Host shutdown blocks new starts and runs per-game stops concurrently.

Final output and stop messages remain available through log polling after the
process exits. Changes under the configured `InstallPath/saves` directory are
reported as supporting evidence only. No claim of save integrity is made based
on process exit or file timestamps. Custom arguments overriding `-savedir` mean
the actual alternate path must be checked separately.

Legacy instances launched with CreateNoWindow have no attachable console and
cannot acquire one retroactively. This preview refuses their normal Stop. Use a
separate maintenance plan for an already-running old instance. Do not install
this preview over the running engine to try to convert it.

## Current scope and limits

- Palworld and its existing watchdog are untouched. Migration remains deferred.
- The user's File.Replace backup-path correction is included.
- The temporary Valheim early-return guard is superseded, not required.
- Other games retain their existing game-specific stop/fallback behavior.
- No silent pre-flight kill of an existing Valheim instance from the same path.
- Helper isolation refusal is intentional if a mod starts another process in the
  same console. Investigate the layout rather than relaxing the check.
- Windows shutdown/termination can still terminate processes; this is not a
  guarantee that an OS shutdown can always be postponed for a save.
- After service restart, a verified surviving Valheim process can be reattached
  and signalled; its previous redirected output cannot be recovered by the new
  engine. Stop reports save evidence and process exit without claiming log replay.
- Scheduled-stop/runtime interactions are serialized per server, but this is not
  a wholesale rewrite of WGS's updater, crash loop, or process ownership model.

## Safe next step on the development PC

Keep the dirty existing checkout and installed service unchanged. If using the
downloadable Git bundle, save it as C:\Temp\Tangerang-ValheimSafeStop.bundle, then
verify/fetch it and create a separate worktree:

```powershell
Set-Location C:\Dev\TangerangWGS
git bundle verify C:\Temp\Tangerang-ValheimSafeStop.bundle
git fetch C:\Temp\Tangerang-ValheimSafeStop.bundle feature/tangerang-valheim-safe-stop
git worktree add --detach C:\Dev\TangerangWGS-ValheimSafeStop FETCH_HEAD
Set-Location C:\Dev\TangerangWGS-ValheimSafeStop
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-ValheimSafeStop.ps1
```

Tests compile synthetic console fixtures and the real lifecycle/backend code.
They never launch game executables from installed servers, connect to PlayFab,
register a service, or access production worlds. They test console isolation,
creation-time mismatch, refusal of shared/no consoles, timeout without killing,
final output capture, duplicate Start refusal, failed Stop blocking backups and
updates, host-exit preservation, and explicit Kill. Configuration is isolated in
the test output directory by TANGERANG_WGS_DEV.

Optional package build (still no deployment):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-ValheimStopPreview.ps1
```

The helper is copied alongside both executables in Debug builds and win-x64
publishes. Publish never registers or upgrades the installed service. Do not run
Install-TangerangService.ps1 against the existing engine for this test.

## Supervised game/service test still required

Passing synthetic tests is necessary but does not prove Valheim will save under
LocalService/session 0. Before deployment, use a disposable copy of Tangerang
Fresh Test, separate save directory and ports, with auto-start/restart disabled.
Never point two engines at the same world or executable directory.

1. Verify the existing production Palworld PID is unaffected.
2. Start the test world; retain the actual Valheim PID and startup log.
3. Join, make a recognizable disposable world change, then disconnect.
4. Stop through the GUI. Confirm Ctrl+C dispatch, game save/exit output, final
   file-activity report and actual process exit. Verify no auto-restart occurs.
5. Restart the world and verify the change persisted, then stop it again.
6. Repeat under a separately configured LocalService/session-0 test host, checking
   engine and desktop logs. Do not change the installed host until this passes.
7. Test console-less refusal and timeout using the synthetic fixtures rather
   than deliberately hanging a live world.

Record results and retain the prior service/desktop binaries before any future
upgrade. This branch is a reviewable preview, not a production deployment.

## References

- Microsoft GenerateConsoleCtrlEvent:
  https://learn.microsoft.com/en-us/windows/console/generateconsolectrlevent
- Microsoft process creation flags and explicit handle inheritance:
  https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags
  https://learn.microsoft.com/en-us/windows/win32/procthread/creating-processes

## Validation recorded for this preview

The desktop/ServiceHost and lifecycle test projects compile with .NET SDK 10.0.404,
with zero warnings/errors. Four portable save-evidence checks pass. A self-contained
win-x64 ServiceHost publish includes WGS.ValheimControl.exe after packaging checks.
The Windows-only synthetic console/lifecycle tests are included but have not been
executed in the Linux development environment. LocalService/session-0 and actual
Valheim save/reload verification remain pending. No live engine was deployed.
