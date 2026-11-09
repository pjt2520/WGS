# Tangerang WGS Announcement Center

**Development branch:** `feature/announcement-center`

This opt-in feature extends the already tested Valheim PlayFab Discord
status board. No changes are made to your installed WGS until you explicitly
pull, build and launch the new branch. Existing running game servers are
untouched by GitHub changes.

## Features

1. Per-server opt-in announcements. Nothing is broadcast unless
   `DiscordAnnouncementsEnabled` is enabled for that server.
2. In-app Announcements panel: send a manual message to the configured
   channel; show delivery status and a 15-second anti-spam cooldown.
3. Opt-in lifecycle notifications for server Running / Stopped / Error.
   Repeated identical status events within 15 seconds are suppressed.
4. **PlayFab-ready notifications:** when a Valheim crossplay lobby actually
   registers with PlayFab, the bot can announce that its session is ready.
   A join code is included only if the server's separate "Show Valheim
   PlayFab join code on Discord status board" toggle is also enabled.
   Leading zeros are preserved; no registration means no announcement.
5. A **warned restart** button, using the same graceful restart callback
   as normal WGS. It warns at the beginning, 60 seconds remaining when
   applicable, and 10 seconds remaining. Countdown: 30 to 600 seconds.
6. A **Cancel countdown** button and `!cancelrestart` command.
7. Discord chat commands for authorized admins:
   - `!announce Tangerang Test | Server maintenance in 15 minutes`
   - `!restartwarn Tangerang Test | 90`
   - `!cancelrestart Tangerang Test`
8. All public announcements suppress `allowed_mentions`: no @everyone,
   @here, user or role pings from remote message text.
9. Existing Valheim join code on the live status board remains unchanged.

## Configure the bot (required)

1. Create a **private** Discord control channel with the bot's commands.
2. Create a **separate** player-facing status/announcement channel.
3. In WGS Settings -> Discord Remote Control Bot:
   - Set the private Control Channel ID.
   - Add your own exact Discord user ID under Admin User IDs.
   - Configure a *separate* Status Channel ID and enable the live status board.
   - Save settings. The Bot Token must be kept secret; do not post it in a
     screenshot, public issue, chat transcript, or GitHub repository.
4. On the chosen WGS server -> Settings -> Tangerang Announcement Center:
   - Check **Enable server announcements in Discord**.
   - Leave Announcement channel ID blank to use the bot Status Channel ID
     or provide a different valid Discord channel ID (17-20 digits).
   - Choose 30-600 seconds for the warned restart countdown.

**Security change:** An empty Admin User IDs list now DENIES all
remote-control text commands and administrator buttons. Prior WGS versions
silently allowed everyone with control-channel access when this list was
empty. The read-only Discord status board still works with no admins.

Announcements intentionally refuse to use the bot's **control** channel.
This prevents game announcements from being mixed into a sensitive
administrative command channel. Use an independent Discord channel.

## Manual announcement

In WGS, enter a message (up to 500 characters) and click
**Send announcement**. The bot posts the message to the configured channel
and the WGS UI reports whether delivery succeeded. The bot limits manual
announcements to one per 15 seconds per server to avoid accidental flooding.

The admin-only Discord command `!announce SERVER | MESSAGE` provides
the same functionality using a `|` to separate a server name containing
spaces from its message.

## Warned restarts

The local **Warn and restart** button asks for confirmation before
starting a countdown. Discord admins can use `!restartwarn SERVER | SECONDS`
where seconds is between 30 and 600. When omitted, the per-server
configured countdown is used. At most one warned restart can be pending
for a server. Warning messages are posted at configured checkpoints:

| Countdown | Discord warnings |
|---|---|
| 30 sec | 30, 10 sec |
| 60 sec | 60, 10 sec |
| 120 sec | 120, 60, 10 sec |
| 300 sec | 300, 60, 10 sec |

The server is restarted only after the last warning and countdown are
complete. If Discord cannot deliver a warning, the warned restart ABORTS
rather than silently stopping an occupied server.

**Cancellation:** Press Cancel countdown or issue `!cancelrestart SERVER`.
Bot shutdown/reconfiguration also cancels pending timers. Timers are not
persisted; after a Windows restart they do **not** unexpectedly fire.

**Important:** the existing immediate Restart button, automatic crash
recovery, daily scheduled restart and update flows are NOT converted
into warned restarts by this feature. The warned countdown is always an
explicit separate operation.

## Regression tests

Safe commands from PowerShell, with no Discord or game interaction:

```powershell
Set-Location 'C:\Dev\TangerangWGS'
git fetch origin
git switch --track origin/feature/announcement-center
dotnet build '.\WindowsGameServer.sln' --configuration Debug
dotnet run --project '.\WGS.HealthChecks.Tests\WGS.HealthChecks.Tests.csproj' --configuration Debug
dotnet run --project '.\WGS.Crossplay.Tests\WGS.Crossplay.Tests.csproj' --configuration Debug
dotnet run --project '.\WGS.Announcement.Tests\WGS.Announcement.Tests.csproj' --configuration Debug
git status
```

## Live Discord test (only when convenient)

1. Run the development UI with
   `scripts\Start-TangerangDev.ps1`. Do not overwrite the installed WGS.
2. Confirm the test bot's control channel is private and Admin User IDs
   includes only users intended to manage servers.
3. Check the server's opt-in box. Send a harmless custom announcement
   using its desktop panel. Verify the Discord message and no role ping.
4. Use `!announce SERVER | hello` from an authorized account and verify
   a user not on the admin list cannot send commands.
5. Only with a disposable server and a free test window, run a 30-second
   warned restart, then test cancelling a later warned restart.
6. Verify the PlayFab join code and status board still work after restart.

## Known limitations and safety notes

- This feature is a Discord **bot-based** announcement center, not the
  separate Discord Webhook Notifications feature.
- It does not warn inside Valheim itself. In-game announcements will require
  a game-specific messaging route that supports unmodded Xbox/PS5 clients.
- The bot must remain online for countdown notices and the final restart.
- The UI and commands share an in-memory countdown map; a WGS shutdown
  cancels it. WGS does not resume countdowns after a restart.
- ServiceHost / remote-mode end-to-end Discord status remains unverified.
- Player counts remain `?` when PlayFab cannot provide an authoritative
  count, and PS5 player tracking awaits a real PlayStation connection.
- The 15-second lifecycle deduplication is in-memory and may be reset when
  WGS itself restarts.

Keep this branch as a **draft PR** until Windows CI and a disposable-server
Discord smoke test pass. No deployment to a production server is implied.
