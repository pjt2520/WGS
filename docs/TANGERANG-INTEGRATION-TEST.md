# Tangerang WGS: integrated Valheim validation plan

Base: upstream WGS v1.5.42; development-only branch `feature/tangerang-integration`.
This combines the separate health-guard and console-observation work.

## Features

1. Crossplay Valheim skips Steam A2S **freeze** probes, keeping OS process-crash recovery unchanged.
2. The optional OdinEye API still provides an independent exact player list if it succeeds.
3. Vanilla PlayFab console lines provide **observed** player names, not an exact live count.
4. Overlapping connections do not associate a character name with an arbitrary platform ID.
5. The desktop Players tab displays the **active PlayFab six-digit join code** and Copy button.
   It shows a waiting or stopped placeholder instead of a previous session's code.
6. Web and Discord player counters remain unknown for unverified PlayFab counts.

## Verification without touching the game servers

From PowerShell:

```powershell
Set-Location 'C:\Dev\TangerangWGS'
git fetch origin
git switch --track origin/feature/tangerang-integration
dotnet build '.\WindowsGameServer.sln' --configuration Debug
dotnet run --project '.\WGS.HealthChecks.Tests\WGS.HealthChecks.Tests.csproj' --configuration Debug
dotnet run --project '.\WGS.Crossplay.Tests\WGS.Crossplay.Tests.csproj' --configuration Debug
git status
```

The health-policy test runner includes six scenarios. Crossplay tests include
synthetic identity events, recovery, ambiguous disconnects, concurrent joins,
and session join-code rotation with leading zero preservation.

## No live deployment yet

Do **not** launch the development WGS executable against the live install.
ConfigService stores its WGS_Data folder **beside the executable**. A developer
build can have its own settings, but it must not be configured to manage the
same Valheim process, world, network ports, or scheduled tasks as stock WGS.

An isolated GUI test should use a distinct test server installation, copy of
world data, and separate ports. Never run two watchdogs against one process.

## Limitations

- Current player observations can miss names or temporarily clear everyone
  after an anonymous PlayFab disconnect. Unknown does **not** mean zero.
- PlayFab's console messages do not provide reliable socket IDs for character
  names, so overlapping joins remain unassigned.
- The join-code display currently targets the in-process desktop WGS mode.
  ServiceHost/remote-mode code propagation and a Discord join-code announcement
  require separate work.
- The health guard avoids an invalid freeze alarm, but does not yet implement
  an active PlayFab health probe.
- Live Xbox/PS5 integration testing is required before merge or release.

## Rollback

Stock WGS remains installed and unmodified. Nothing needs restoring until a
separate development installer is deliberately deployed. Before any future
deployment, back up WGS_Data and all server worlds and stop the running game.
