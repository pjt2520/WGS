using WGS.Models;

namespace WGS.Services;

/// <summary>
/// Stateless text and parsing rules for the Tangerang Discord Announcement
/// Center. These methods have no Discord credentials, disk writes, or network
/// calls so we can test every safety boundary without a live server.
///
/// IMPORTANT: the Discord HTTP request must also set allowed_mentions.parse
/// to [] to prevent user-generated text from pinging everyone or arbitrary
/// roles. Text cleaning alone is not an adequate mention-safety control.
/// </summary>
public static class TangerangAnnouncementFormatter
{
    /// <summary>
    /// Commands use a pipe delimiter so multiword server names and free-text
    /// messages are unambiguous: !announce Tangerang Test | Restart soon.
    /// Empty, oversized, and control-character-only messages are rejected.
    /// </summary>
    public static bool TryParseAnnouncement(string raw, out string serverName, out string message)
    {
        serverName = message = string.Empty;
        int separator = raw.IndexOf('|');
        if (separator <= 0) return false;
        serverName = raw[..separator].Trim();
        message = Clean(raw[(separator + 1)..]);
        return serverName.Length is > 0 and <= 100
            && message.Length is > 0 and <= 500;
    }

    public static bool TryParseRestart(string raw, int defaultSeconds,
        out string serverName, out int seconds)
    {
        serverName = string.Empty;
        seconds = 0;
        int separator = raw.IndexOf('|');
        var name = separator < 0 ? raw : raw[..separator];
        serverName = name.Trim();
        if (serverName.Length is < 1 or > 100) return false;
        if (separator < 0)
            seconds = defaultSeconds;
        else if (!int.TryParse(raw[(separator + 1)..].Trim(), out seconds))
            return false;
        return seconds is >= 30 and <= 600;
    }

    /// <summary>
    /// Removes control characters while preserving ordinary punctuation.
    /// Discord's allowed_mentions=[] remains the actual anti-ping safeguard.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = new string(text.Select(c =>
            char.IsControl(c) ? ' ' : c).ToArray());
        return cleaned.Trim();
    }

    /// <summary>
    /// Fail-closed admin allow-list. An empty list grants **no** access,
    /// even if somebody sees a bot command in the configured control channel.
    /// Discord snowflakes are decimal strings; never trust a display name.
    /// </summary>
    public static bool IsAuthorizedAdmin(string? allowList, string? userId)
        => !string.IsNullOrWhiteSpace(allowList)
           && !string.IsNullOrWhiteSpace(userId)
           && allowList.Split(',', StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
               .Contains(userId, StringComparer.Ordinal);

    /// <summary>
    /// Never fall back to the private control channel for announcements, even
    /// if a status channel was not explicitly configured. Admin instructions
    /// should not mingle with messages meant for game players.
    /// </summary>
    public static string? ResolveAnnouncementChannel(
        string? perServer, string? statusChannel, string? controlChannel)
    {
        var channel = string.IsNullOrWhiteSpace(perServer)
            ? statusChannel?.Trim()
            : perServer.Trim();
        if (string.IsNullOrWhiteSpace(channel) || channel == controlChannel)
            return null;
        return System.Text.RegularExpressions.Regex.IsMatch(channel, @"^\d{17,20}$")
            ? channel : null;
    }

    /// <summary>
    /// The empty parse list prevents *all* mentions (including everyone/here)
    /// in the Discord HTTP API even when source text contains @role syntax.
    /// </summary>
    public static object DiscordPayload(string message)
        => new
        {
            content = message,
            allowed_mentions = new { parse = Array.Empty<string>() }
        };

    /// <summary>Pure check to avoid sending any unrequested announcements.</summary>
    public static bool MayAnnounce(GameServer server)
        => server.DiscordAnnouncementsEnabled;

    public static string LifecycleMessage(GameServer server, ServerStatus status)
    {
        var name = Clean(server.DisplayName);
        return status switch
        {
            ServerStatus.Running => $"🟢 **{name}** server process started. Game initialization may still be in progress.",
            ServerStatus.Stopped => $"⚫ **{name}** is offline.",
            ServerStatus.Error   => $"🔴 **{name}** encountered a server error.",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Announce readiness only after Valheim's registered PlayFab session
    /// line has been parsed by its live tracker. The join code is included
    /// only when the *separate* DiscordPublishJoinCode checkbox is enabled.
    /// A placeholder/malformed/stale code must never be announced.
    /// </summary>
    public static string PlayFabReadyMessage(GameServer server, string? joinCode)
    {
        if (server.GameId != "valheim" || server.Status != ServerStatus.Running
            || !server.GameSpecificSettings.TryGetValue("crossplay", out var crossplay)
            || crossplay != "true"
            || string.IsNullOrEmpty(joinCode)
            || !System.Text.RegularExpressions.Regex.IsMatch(joinCode, @"^\d{6}$"))
            return string.Empty;

        var message = $"🛡️ **{Clean(server.DisplayName)}** PlayFab session is ready for Xbox, PlayStation and PC crossplay.";
        return server.DiscordPublishJoinCode
            ? message + $"\n🔑 Join code: `{joinCode}`"
            : message + "\nJoin code sharing is disabled for this server.";
    }

    public static string CustomMessage(GameServer server, string text)
        => $"📣 **{Clean(server.DisplayName)}**\n{Clean(text)}";

    public static string RestartWarning(GameServer server, int seconds)
    {
        var unit = seconds == 60 ? "1 minute" : $"{seconds} seconds";
        return $"🔄 **{Clean(server.DisplayName)}** will restart in **{unit}**. Save your progress and find a safe place.";
    }

    /// <summary>
    /// Emission checkpoints are unique and ordered by remaining time.
    /// A 60s countdown announces 60s and 10s, a 30s countdown 30s and 10s;
    /// long countdowns also receive a 60s reminder. Existing manual
    /// Restart and daily automatic restart are unaffected.
    /// </summary>
    public static int[] RestartCheckpoints(int seconds)
    {
        if (seconds is < 30 or > 600) return [];
        var points = new List<int> { seconds };
        if (seconds > 60) points.Add(60);
        if (seconds > 10) points.Add(10);
        return points.Distinct().OrderDescending().ToArray();
    }
}


/// <summary>
/// Atomic one-way state gate for warned restarts. A cancellation racing the
/// countdown finishing must never claim success *after* restart execution has
/// been authorized. Exactly one of TryCancel and TryBeginRestart can win.
/// No game access or network calls; tested with deterministic state checks.
/// </summary>
public sealed class TangerangRestartGate
{
    // 0 = countdown active; 1 = restart executing; 2 = cancelled;
    // 3 = completed / abandoned without a restart.
    private int _state;

    public bool TryCancel()
        => Interlocked.CompareExchange(ref _state, 2, 0) == 0;

    public bool TryBeginRestart()
        => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

    public void Finish() => Interlocked.Exchange(ref _state, 3);
}
