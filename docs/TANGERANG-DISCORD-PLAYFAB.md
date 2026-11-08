# Tangerang WGS: opt-in PlayFab join codes on the Discord status board

## Scope

On a Valheim server using `crossplay=true`, the Discord bot's *existing*
live status board can optionally include the current six-digit PlayFab
join code. This is an in-place edit, not an additional message each minute.

## Enable

1. In WGS's **Settings → Discord Bot**, connect the bot token, select its
   status channel, and turn on the existing **live status board**.
2. On the desired Valheim server, open **Settings** and check
   **Show Valheim PlayFab join code on Discord status board**.
3. Ensure the server has `crossplay=true`, is running, and has registered
   its PlayFab lobby. The board will update on its existing 60-second cycle.

**Important:** this setting starts **disabled** for every existing server.
Anyone with access to the selected status channel may read the invite.
Do not enable this in a channel where you do not want the join code shared.
Users will still need the Valheim server password to enter.

## Safety and data sources

- The code comes from the current in-memory ValheimPlayerTracker.
- No join code is persisted to settings JSON or copied from historic logs.
- The board never publishes codes for a stopped server, for non-crossplay
  Valheim, or for another game (even if its settings contain `crossplay`).
- The code must be exactly six ASCII digits. Strings with markup, mentions,
  or a trailing newline are rejected.
- A running crossplay server that hasn't registered yet displays
  `join code pending`; otherwise the status board shows only that this is
  a PlayFab connection when sharing is disabled.
- Ordinary non-crossplay game status boards still use IP/port as before.
- The owner must opt in per-server, even when a global bot status channel
  is already configured.

## Known limits

- This feature updates the bot's **live status board**; it does not send
  standalone ping/announcement messages or issue role mentions.
- The status board is refreshed by the existing WGS bot every ~60 seconds.
  The previous Discord message can show an old code for up to one refresh
  interval after a restart. If WGS is closed or loses connectivity, Discord
  may retain the last published message until a successful update.
- Local desktop mode is supported. Background-ServiceHost and remote-mode
  PlayFab code propagation still require separate verification.
- The original Valheim desktop join-code display is unchanged.

## Verify in development (no Discord account required)

```powershell
Set-Location 'C:\Dev\TangerangWGS'
git fetch origin
git switch --track origin/feature/discord-playfab-board
dotnet build '.\WindowsGameServer.sln' --configuration Debug
dotnet run --project '.\WGS.HealthChecks.Tests\WGS.HealthChecks.Tests.csproj' --configuration Debug
dotnet run --project '.\WGS.Crossplay.Tests\WGS.Crossplay.Tests.csproj' --configuration Debug
```

To perform a live Discord test, use an isolated bot token/status channel,
or intentionally enable the setting on a designated test server. Verify
the code appears only after registration and is removed on a later board
refresh after stopping the server. Do not post bot tokens or webhooks in
GitHub issues or console logs.

Keep the change in draft until live Discord edit behavior is verified.
