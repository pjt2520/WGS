# Tangerang 1.5.43 integration preview

Base: safe-stop preview 504b2d7 plus upstream v1.5.43. This is a development
package, not an installed upgrade. Keep the playable installation and the
sandbox separate. Do not run the stock updater on this custom build.

## Included

- Upstream service-owned scheduling, uptime, console and performance display;
  retained authenticated loopback API, service ownership lock and single writer
  of managed configuration. One UI poll loop retains unknown counts and PlayFab
  join-code handling. Polling includes stopped servers to retain final logs.
- Isolated Ctrl+C Valheim shutdown, exit verification, failed-stop propagation,
  no implicit kill on engine exit, and preserved final output.
- Palworld normal Stop now requires successful REST save and shutdown requests
  and verified process exit. The force-stop REST endpoint is never a fallback.
  Transport/HTTP/timeout failures throw and block subsequent backup/update/start.
  Explicit Force Kill remains available and can lose unsaved progress.
- Palworld health requires valid current/max players, FPS, uptime and world days.
  Missing, null or invalid fields, zero FPS, invalid players response and mismatched
  metrics/list counts remain unknown. Confirmed occupancy expires after 60 seconds.
  Sustained Palworld degradation is logged/notified; no automatic forced recovery
  or Windows reboot. Crash recovery for a process that actually exits remains opt-in.
- Palworld REST credentials and settings are validated. At Start, a staged INI
  edit enables REST and updates only admin/password/name/capacity/public port
  settings, preserving world tuning and unknown fields. Previous INI is backed up.
  Existing settings are used, or the game's shipped default template for new installs.
  Community listing is configurable. Existing API port defaults remain 21512;
  for the existing Tangerang Palworld migration explicitly select 8212.
- Protected games refuse duplicate installation ownership, unverifiable saved PIDs
  and occupied ports. Lifecycle backend operations are serialized per server.
  Failed updates no longer restart in a finally block. Idle shutdown cancels its
  backup if Stop fails. Backups of these games require stopped processes so a
  multi-file world is not presented as a consistent snapshot while being written.
- Automatic upstream EXE replacement is blocked for the custom package.

## Verification and deployment gates

Desktop Valheim gameplay save/reload was verified by Patrick on Nov 15 using
504b2d7: save 2 loaded and placed objects persisted; subsequent stop completed
save 3. This is evidence for the previous desktop preview, not service-account
validation or verification of this newly integrated build.

Run scripts/Test-ValheimSafeStop.ps1 for synthetic Windows console, lifecycle,
Palworld HTTP/metrics/configuration tests. Build-ValheimStopPreview.ps1 runs tests
before publishing both applications and the console helper. No script registers
or replaces an installed Windows service.

Before making either game a playable WGS-managed world:

1. Test this package in a separate home, game install, save directory and ports.
   Do not point development and production managers at the same game folder.
2. Verify Valheim Start -> Xbox join -> place object -> Stop -> Start -> rejoin.
   Then repeat through a separately configured LocalService/session-0 host and
   test GUI disconnect/reconnect and normal service Stop. Confirm all saves.
3. Verify a disposable Palworld world with matching REST admin password/port:
   metrics, players, announce, save, normal Stop, restart, and actual world reload.
   Confirm missing credentials/failed REST leaves a live process running and
   update/backup blocked. Use synthetic fixtures for hangs, not a valuable world.
4. Test stopped-world backup and restore in the sandbox. Keep all save files,
   including Valheim's current chunk/DB2/FWL files and Palworld's SaveGames tree.
5. Create the new manually started `Tangerang Official` Valheim world only after
   these gates pass. Keep AutoStart, WakeOnDemand, automatic updates, daily restarts
   and automatic recovery off until each desired behavior is separately tested.
6. Migrate existing Palworld only in a maintenance window. Save/stop using its
   current owner; disable its old watchdog/update/start tasks; verify no game
   process remains; preserve a stopped copy of Saves and Config and task settings.
   Import a copy into WGS first, preserve world identity and tuning, verify reload,
   then choose ONE owner. The old watchdog and WGS must never control it together.
   Rollback requires stopping WGS's instance before restoring the old ownership.

No automatic migration, watchdog activation, service deployment, world creation,
router changes or production updates were performed by this integration.

## Remaining limits

Valheim PlayFab crossplay has no reliable vanilla A2S census; unknown occupancy
must remain unknown and blocks empty-only maintenance. Palworld REST acceptance
and process exit are not proof of world integrity; live save/reload is mandatory.
Palworld uses its native console rather than redirected stdin; health is available
through REST, while historical game console capture is not provided by this patch.
Changing active credentials/ports requires a stop/start. No mods are required.
Backup restore is destructive and must be tested against disposable data first.

Official REST references:
- https://docs.palworldgame.com/api/rest-api/metrics/
- https://docs.palworldgame.com/api/rest-api/save/
- https://docs.palworldgame.com/api/rest-api/shutdown/
