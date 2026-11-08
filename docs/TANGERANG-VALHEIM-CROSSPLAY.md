# Tangerang WGS: Valheim crossplay player observations

This development-only feature targets **Valheim 1.0.21 / WGS 1.5.42** using **PlayFab crossplay**.

## Why this exists

Stock WGS queries Steam A2S on the game port plus one. A PlayFab-hosted
server can be fully playable while A2S returns no response. A failed
query or an empty list of observed names does not mean nobody is online.

## Implemented

- Parse PlayFab socket-child, platform-ID, and character-name events, plus legacy Steam events.
- Suppress duplicate socket or platform messages.
- Discard uncertain PlayFab observations after an unattributed `Player connection lost` event.
- Skip Steam player queries for PlayFab when optional OdinEye has not supplied a verified list.
- Keep `GameServer.PlayerCountUnknown=true` for log-derived observations.
- Desktop, web and Discord display unknown totals rather than guessed exact counts.

## Known limitations

1. **Not an exact census.** Character-name lines omit the connection ID. Simultaneous joins may be misattributed.
2. Platform identities without character-name events are intentionally excluded.
3. Unattributed disconnects clear all PlayFab observations. Remaining players may disappear temporarily.
4. The legacy `OnlinePlayer.SteamId` field also carries Xbox/PlayFab identifiers.
5. The synthetic PlayStation-shaped fixture only tests platform-neutral parsing; real PS5 logs still need checking.
6. Remote-service mode doesn't yet transmit full player-count confidence from the server; the non-OdinEye client uses Unknown.
7. A PlayFab-aware health probe is a separate task. Draft PR #1 prevents a false Steam A2S freeze alarm.

## Safe developer validation

Do not run a development WGS desktop instance against your live WGS configuration.
Build and test only:

```powershell
Set-Location 'C:\Dev\TangerangWGS'
git fetch origin
git switch --track origin/feature/crossplay-players
dotnet build '.\WindowsGameServer.sln' --configuration Debug
dotnet run --project '.\WGS.Crossplay.Tests\WGS.Crossplay.Tests.csproj' --configuration Debug
git status
```

The regression runner uses fabricated player IDs and in-memory log events.
It never starts a game server or accesses Valheim world files.

## Before merge or deployment

- Require a successful Windows CI run for the latest commit.
- Verify Xbox reconnect, actual PS5 join, and concurrent players in an isolated test instance.
- Keep idle shutdown disabled for production PlayFab Valheim.
- Preserve production WGS and world backups. Do not overwrite installed files during tests.
- Keep this pull request in draft until integration tests and rollback are documented.
